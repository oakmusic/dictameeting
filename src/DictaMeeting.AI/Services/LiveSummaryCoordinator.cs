using System.Diagnostics;
using System.Text;
using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.Meetings.Models;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Coordinador de resumen en vivo local con tarjetas periódicas (cada 1–2 minutos, ~50 palabras por tarjeta).
/// Ejecuta la inferencia LLM en segundo plano desacoplada de la captura y transcripción.
/// Asegura aislamiento estricto por MeetingId/SessionId y validación anti-degeneración.
/// </summary>
public class LiveSummaryCoordinator : ILiveSummaryCoordinator
{
    private readonly ILiveSummaryService _summaryService;
    private readonly ILiveSummaryModelManager _modelManager;
    private readonly LiveSummaryConfig _config;
    private readonly ILiveSummaryOutputValidator _validator;
    private readonly ILogger<LiveSummaryCoordinator>? _logger;

    private readonly object _lock = new();
    private readonly List<TranscriptSegment> _segments = new();
    private readonly List<SummarySegment> _cards = new();

    private string? _currentMeetingId;
    private Guid _currentSessionId = Guid.Empty;
    private string _currentSummary = string.Empty;
    private DateTimeOffset? _lastUpdatedTime;
    private Stopwatch? _meetingStopwatch;
    private TimeSpan _lastSummarizedEndTime = TimeSpan.Zero;
    private TimeSpan _lastCardGeneratedElapsed = TimeSpan.Zero;
    private int _lastSummarizedTotalWords;
    private string _language = "Spanish";

    private bool _isRunning;
    private bool _isGenerating;
    private bool _disposed;
    private CancellationTokenSource? _meetingCts;
    private Timer? _periodicCheckTimer;

    public string? CurrentMeetingId
    {
        get { lock (_lock) return _currentMeetingId; }
    }

    public IReadOnlyList<SummarySegment> SummaryCards
    {
        get { lock (_lock) return _cards.ToList(); }
    }

    public string CurrentSummary
    {
        get { lock (_lock) return _currentSummary; }
        private set { lock (_lock) _currentSummary = value; }
    }

    public DateTimeOffset? LastUpdatedTime
    {
        get { lock (_lock) return _lastUpdatedTime; }
        private set { lock (_lock) _lastUpdatedTime = value; }
    }

    public bool IsGenerating => _isGenerating;
    public bool IsRunning => _isRunning;

    public event EventHandler<SummaryUpdatedEventArgs>? SummaryUpdated;
    public event EventHandler<bool>? GeneratingStateChanged;

    public LiveSummaryCoordinator(
        ILiveSummaryService summaryService,
        ILiveSummaryModelManager modelManager,
        LiveSummaryConfig? config = null,
        ILiveSummaryOutputValidator? validator = null,
        ILogger<LiveSummaryCoordinator>? logger = null)
    {
        _summaryService = summaryService;
        _modelManager = modelManager;
        _config = config ?? new LiveSummaryConfig();
        _validator = validator ?? new LiveSummaryOutputValidator();
        _logger = logger;
    }

    public void Start(string language = "Spanish")
    {
        Start(Guid.NewGuid().ToString("N"), language);
    }

    public void Start(string meetingId, string language = "Spanish")
    {
        lock (_lock)
        {
            // 1. Detener temporizador previo y cancelar tareas pendientes de reunión anterior
            _periodicCheckTimer?.Dispose();
            _periodicCheckTimer = null;

            try
            {
                _meetingCts?.Cancel();
                _meetingCts?.Dispose();
            }
            catch { }

            // 2. Limpieza total del estado de resumen
            _segments.Clear();
            _cards.Clear();
            _currentSummary = string.Empty;
            _lastUpdatedTime = null;
            _lastSummarizedEndTime = TimeSpan.Zero;
            _lastCardGeneratedElapsed = TimeSpan.Zero;
            _lastSummarizedTotalWords = 0;
            _isGenerating = false;

            // 3. Establecer nueva identidad de reunión y sesión
            _currentMeetingId = string.IsNullOrWhiteSpace(meetingId) ? Guid.NewGuid().ToString("N") : meetingId.Trim();
            _currentSessionId = Guid.NewGuid();
            _language = string.IsNullOrWhiteSpace(language)
                ? (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase) ? "English" : "Spanish")
                : language;
            _isRunning = true;
            _meetingStopwatch = Stopwatch.StartNew();
            _meetingCts = new CancellationTokenSource();

            // 4. Iniciar temporizador periódico para la nueva reunión
            _periodicCheckTimer = new Timer(_ => TryTriggerSummaryIfEligible(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        try
        {
            _summaryService.CancelCurrentGeneration();
        }
        catch { }

        _logger?.LogInformation("[LiveSummary] Coordinador iniciado para MeetingId: {MeetingId}, Sesión: {SessionId} (Idioma: {Language}).",
            _currentMeetingId, _currentSessionId, _language);
    }

    public void Stop()
    {
        lock (_lock)
        {
            _isRunning = false;
            _meetingStopwatch?.Stop();
            _periodicCheckTimer?.Dispose();
            _periodicCheckTimer = null;
            // Invalida inmediatamente cualquier tarea de inferencia en curso para evitar escrituras zombi
            _currentSessionId = Guid.Empty;
        }

        try
        {
            _meetingCts?.Cancel();
            _summaryService.CancelCurrentGeneration();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[LiveSummary] Error al cancelar la generación durante la detención del coordinador.");
        }

        _logger?.LogInformation("[LiveSummary] Coordinador detenido para MeetingId: {MeetingId}.", _currentMeetingId);
    }

    public void Reset()
    {
        Stop();
        lock (_lock)
        {
            _segments.Clear();
            _cards.Clear();
            _currentSummary = string.Empty;
            _lastUpdatedTime = null;
            _lastSummarizedEndTime = TimeSpan.Zero;
            _lastCardGeneratedElapsed = TimeSpan.Zero;
            _lastSummarizedTotalWords = 0;
            _currentMeetingId = null;
            _currentSessionId = Guid.Empty;
            _isGenerating = false;
        }
        _logger?.LogInformation("[LiveSummary] Coordinador reseteado por completo.");
    }

    public void AddSegment(TranscriptSegment segment)
    {
        if (segment == null || string.IsNullOrWhiteSpace(segment.Text)) return;

        lock (_lock)
        {
            if (!_isRunning) return;
            _segments.Add(segment);
        }

        TryTriggerSummaryIfEligible();
    }

    public void UpdateSegment(TranscriptSegment segment)
    {
        if (segment == null) return;

        lock (_lock)
        {
            if (!_isRunning) return;
            var idx = _segments.FindLastIndex(s => s.Id == segment.Id);
            if (idx >= 0)
            {
                _segments[idx] = segment;
            }
            else
            {
                _segments.Add(segment);
            }
        }

        TryTriggerSummaryIfEligible();
    }

    private void TryTriggerSummaryIfEligible()
    {
        bool shouldTrigger = false;

        lock (_lock)
        {
            if (!_isRunning || _isGenerating) return;
            if (!_modelManager.IsModelDownloaded()) return;
            if (_segments.Count == 0) return;

            var elapsed = _meetingStopwatch?.Elapsed ?? TimeSpan.Zero;
            var timeSinceLastCard = elapsed - _lastCardGeneratedElapsed;

            // Condición 1: Comprobar intervalo transcurrido (mínimo SummaryInterval, ej. 60s; 45s para la primera)
            bool timeConditionMet = _cards.Count == 0
                ? elapsed >= TimeSpan.FromSeconds(45)
                : timeSinceLastCard >= _config.SummaryInterval;

            if (!timeConditionMet) return;

            // Condición 2: Comprobar que hay segmentos nuevos estrictamente posteriores a _lastSummarizedEndTime
            var unsummarized = _segments.Where(s => s.EndTime > _lastSummarizedEndTime).ToList();
            if (unsummarized.Count == 0) return;

            int newWordsInWindow = CountTotalWords(unsummarized);
            if (newWordsInWindow < _config.MinimumNewWordsThreshold) return;

            // Evitar disparar en silencios o tartamudeos de una sola palabra
            int uniqueWords = CountUniqueWords(unsummarized);
            if (uniqueWords < 8) return;

            shouldTrigger = true;
        }

        if (shouldTrigger)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await GenerateSummaryCycleAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[LiveSummary] Error en ciclo asíncrono de resumen en vivo.");
                }
            });
        }
    }

    public async Task TriggerImmediateSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (_isGenerating || !_modelManager.IsModelDownloaded()) return;
        await GenerateSummaryCycleAsync(cancellationToken);
    }

    public async Task FinalizePendingSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (!_modelManager.IsModelDownloaded()) return;

        List<TranscriptSegment> pendingSegments;
        lock (_lock)
        {
            pendingSegments = _segments.Where(s => s.EndTime > _lastSummarizedEndTime).ToList();
        }

        if (pendingSegments.Count > 0 && CountTotalWords(pendingSegments) >= 10 && CountUniqueWords(pendingSegments) >= 4)
        {
            await GenerateSummaryCycleAsync(cancellationToken);
        }
    }

    private async Task GenerateSummaryCycleAsync(CancellationToken externalCancellationToken = default)
    {
        string windowText;
        string lang;
        CancellationToken cancelToken;
        TimeSpan cardStartTime;
        TimeSpan cardEndTime;
        List<TranscriptSegment> windowSegments;
        string meetingId;
        Guid sessionId;
        string? previousCardText;

        lock (_lock)
        {
            if (_isGenerating || !_isRunning) return;
            if (_segments.Count == 0) return;

            sessionId = _currentSessionId;
            meetingId = _currentMeetingId ?? string.Empty;
            if (sessionId == Guid.Empty || string.IsNullOrEmpty(meetingId)) return;

            // Extraer segmentos no resumidos aún
            windowSegments = _segments.Where(s => s.EndTime > _lastSummarizedEndTime).ToList();

            // REGLA CRÍTICA: Si no hay segmentos nuevos, NO generar tarjeta
            if (windowSegments.Count == 0) return;

            int wordsInWindow = CountTotalWords(windowSegments);
            if (wordsInWindow < 4) return;

            cardStartTime = _lastSummarizedEndTime;
            cardEndTime = windowSegments[^1].EndTime;
            if (cardEndTime <= cardStartTime) return;

            previousCardText = _cards.Count > 0 ? _cards[^1].Text : null;
            windowText = FormatSegmentsForPrompt(windowSegments);
            lang = _language;
            cancelToken = _meetingCts?.Token ?? CancellationToken.None;

            _isGenerating = true;
        }

        GeneratingStateChanged?.Invoke(this, true);

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken, externalCancellationToken);
            if (linkedCts.IsCancellationRequested) return;

            var timeRangeFormatted = $"{FormatTimeSpan(cardStartTime)} - {FormatTimeSpan(cardEndTime)}";

            _logger?.LogInformation(
                "[LiveSummary] Iniciando inferencia para MeetingId: {MeetingId}, Rango: {Range}, Palabras: {Words}, Chars: {Chars}, Temp: {Temp}, RepPenalty: {RepPenalty}.",
                meetingId, timeRangeFormatted, CountTotalWords(windowSegments), windowText.Length, _config.Temperature, _config.RepeatPenalty);

            var cardSummary = await _summaryService.GenerateSummaryAsync(string.Empty, windowText, lang, linkedCts.Token);

            // Verificación 1: Comprobar si la sesión/reunión cambió durante la inferencia
            lock (_lock)
            {
                if (!_isRunning || _currentSessionId != sessionId || _currentMeetingId != meetingId)
                {
                    _logger?.LogWarning(
                        "[LiveSummary] Inferencia completada pero DESCARTADA: la sesión/reunión cambió (Sesión esperada: {Expected}, Actual: {Actual}).",
                        sessionId, _currentSessionId);
                    return;
                }
            }

            if (linkedCts.IsCancellationRequested)
            {
                _logger?.LogInformation("[LiveSummary] Inferencia descartada por cancelación solicitada.");
                return;
            }

            if (string.IsNullOrWhiteSpace(cardSummary))
            {
                _logger?.LogWarning("[LiveSummary] Inferencia retornó resultado vacío para rango {Range}.", timeRangeFormatted);
                return;
            }

            // Verificación 2: Validación del output antes de mostrarlo
            var validation = _validator.ValidateOutput(cardSummary, windowText, previousCardText, cardStartTime, cardEndTime, _lastSummarizedEndTime);
            if (!validation.IsValid)
            {
                _logger?.LogWarning(
                    "[LiveSummary] Validación fallida para MeetingId: {MeetingId}, Rango: {Range}. Motivo: {Reason} ({Details}). Texto descartado: \"{Text}\"",
                    meetingId, timeRangeFormatted, validation.FailureReason, validation.Details, cardSummary);

                // Intento de regeneración controlada SOLO si es bucle degenerativo o repetición
                if (validation.FailureReason == LiveSummaryValidationFailureReason.RepeatedSentence ||
                    validation.FailureReason == LiveSummaryValidationFailureReason.DegenerativeLoop)
                {
                    _logger?.LogInformation("[LiveSummary] Intentando regeneración controlada única para rango {Range}...", timeRangeFormatted);
                    cardSummary = await _summaryService.GenerateSummaryAsync(string.Empty, windowText, lang, linkedCts.Token);

                    // Re-validar
                    validation = _validator.ValidateOutput(cardSummary, windowText, previousCardText, cardStartTime, cardEndTime, _lastSummarizedEndTime);
                    if (!validation.IsValid)
                    {
                        _logger?.LogWarning(
                            "[LiveSummary] Segunda validación fallida tras reintento para MeetingId: {MeetingId}, Rango: {Range}. DESCARTANDO actualización para evitar mostrar contenido defectuoso.",
                            meetingId, timeRangeFormatted);
                        return;
                    }
                }
                else
                {
                    // No recuperable con simple reintento o es desconexión semántica / rango inválido: descartar
                    return;
                }
            }

            // Publicación segura de la tarjeta validada
            var now = DateTimeOffset.Now;
            var newCard = new SummarySegment
            {
                Id = Guid.NewGuid().ToString("N"),
                MeetingId = meetingId,
                StartTime = cardStartTime,
                EndTime = cardEndTime,
                FormattedTimeRange = timeRangeFormatted,
                Text = cardSummary.Trim(),
                Timestamp = now
            };

            IReadOnlyList<SummarySegment> allCardsSnapshot;
            string totalSummaryText;

            lock (_lock)
            {
                if (!_isRunning || _currentSessionId != sessionId || _currentMeetingId != meetingId)
                {
                    _logger?.LogWarning("[LiveSummary] Sesión cambiada antes de insertar tarjeta. Descartando.");
                    return;
                }

                _cards.Add(newCard);
                _lastSummarizedEndTime = cardEndTime;
                _lastCardGeneratedElapsed = _meetingStopwatch?.Elapsed ?? TimeSpan.Zero;
                _lastSummarizedTotalWords = CountTotalWords(_segments);
                _lastUpdatedTime = now;

                var sb = new StringBuilder();
                foreach (var c in _cards)
                {
                    sb.AppendLine($"[{c.FormattedTimeRange}]");
                    sb.AppendLine(c.Text);
                    sb.AppendLine();
                }
                _currentSummary = sb.ToString().Trim();
                totalSummaryText = _currentSummary;
                allCardsSnapshot = _cards.ToList();
            }

            _logger?.LogInformation(
                "[LiveSummary] Tarjeta aprobada y publicada para MeetingId: {MeetingId} ({Range}): {Words} palabras. Total tarjetas: {CardCount}.",
                meetingId, timeRangeFormatted, CountTotalWords(new[] { new TranscriptSegment { Text = cardSummary } }), allCardsSnapshot.Count);

            SummaryUpdated?.Invoke(this, new SummaryUpdatedEventArgs(meetingId, totalSummaryText, now, _summaryService.LastMetrics, newCard, allCardsSnapshot));
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("[LiveSummary] Ciclo de resumen cancelado.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[LiveSummary] Fallo al generar tarjeta de resumen en vivo.");
        }
        finally
        {
            _isGenerating = false;
            GeneratingStateChanged?.Invoke(this, false);
        }
    }

    public static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public static string FormatSegmentsForPrompt(IReadOnlyList<TranscriptSegment> segments)
    {
        var sb = new StringBuilder();
        string? lastText = null;
        string? lastSpeaker = null;

        foreach (var seg in segments)
        {
            var text = seg.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) continue;

            var speaker = !string.IsNullOrWhiteSpace(seg.SpeakerDisplayName)
                ? seg.SpeakerDisplayName
                : (!string.IsNullOrWhiteSpace(seg.SpeakerId) ? seg.SpeakerId : null);

            // Condensar duplicados consecutivos exactos del mismo hablante (tartamudeo de transcripción ASR)
            if (string.Equals(text, lastText, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(speaker, lastSpeaker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lastText = text;
            lastSpeaker = speaker;

            if (!string.IsNullOrWhiteSpace(speaker))
            {
                sb.AppendLine($"{speaker}: {text}");
            }
            else
            {
                sb.AppendLine(text);
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Compatibilidad histórica para pruebas y ventana deslizante.
    /// </summary>
    public static string ExtractSlidingWindowText(IReadOnlyList<TranscriptSegment> segments, TimeSpan windowDuration)
    {
        if (segments.Count == 0) return string.Empty;

        var lastSegment = segments[^1];
        var endTime = lastSegment.EndTime;
        var startTimeThreshold = endTime > windowDuration ? endTime - windowDuration : TimeSpan.Zero;

        var windowSegments = segments.Where(s => s.EndTime >= startTimeThreshold).ToList();
        if (windowSegments.Count == 0)
        {
            windowSegments = segments.TakeLast(5).ToList();
        }

        return FormatSegmentsForPrompt(windowSegments);
    }

    private static int CountTotalWords(IEnumerable<TranscriptSegment> segments)
    {
        int count = 0;
        foreach (var s in segments)
        {
            if (string.IsNullOrWhiteSpace(s.Text)) continue;
            count += s.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        }
        return count;
    }

    private static int CountUniqueWords(IEnumerable<TranscriptSegment> segments)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in segments)
        {
            if (string.IsNullOrWhiteSpace(s.Text)) continue;
            var tokens = s.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in tokens)
            {
                if (t.Length >= 2) set.Add(t);
            }
        }
        return set.Count;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _meetingCts?.Dispose();
        _meetingCts = null;
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
