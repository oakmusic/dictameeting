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
/// </summary>
public class LiveSummaryCoordinator : ILiveSummaryCoordinator
{
    private readonly ILiveSummaryService _summaryService;
    private readonly ILiveSummaryModelManager _modelManager;
    private readonly LiveSummaryConfig _config;
    private readonly ILogger<LiveSummaryCoordinator>? _logger;

    private readonly object _lock = new();
    private readonly List<TranscriptSegment> _segments = new();
    private readonly List<SummarySegment> _cards = new();

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

    private Timer? _periodicCheckTimer;

    public LiveSummaryCoordinator(
        ILiveSummaryService summaryService,
        ILiveSummaryModelManager modelManager,
        LiveSummaryConfig? config = null,
        ILogger<LiveSummaryCoordinator>? logger = null)
    {
        _summaryService = summaryService;
        _modelManager = modelManager;
        _config = config ?? new LiveSummaryConfig();
        _logger = logger;
    }

    public void Start(string language = "Spanish")
    {
        lock (_lock)
        {
            _segments.Clear();
            _cards.Clear();
            _currentSummary = string.Empty;
            _lastUpdatedTime = null;
            _lastSummarizedEndTime = TimeSpan.Zero;
            _lastCardGeneratedElapsed = TimeSpan.Zero;
            _lastSummarizedTotalWords = 0;
            _language = string.IsNullOrWhiteSpace(language) ? "Spanish" : language;
            _isRunning = true;
            _isGenerating = false;
            _meetingStopwatch = Stopwatch.StartNew();
            _meetingCts?.Cancel();
            _meetingCts = new CancellationTokenSource();

            _periodicCheckTimer?.Dispose();
            _periodicCheckTimer = new Timer(_ => TryTriggerSummaryIfEligible(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        _logger?.LogInformation("Coordinador de resumen en vivo iniciado (Idioma: {Language}).", _language);
    }

    public void Stop()
    {
        lock (_lock)
        {
            _isRunning = false;
            _meetingStopwatch?.Stop();
            _periodicCheckTimer?.Dispose();
            _periodicCheckTimer = null;
        }

        try
        {
            _meetingCts?.Cancel();
            _summaryService.CancelCurrentGeneration();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error al cancelar la generación durante la detención del coordinador.");
        }

        _logger?.LogInformation("Coordinador de resumen en vivo detenido.");
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
        }
    }

    public void AddSegment(TranscriptSegment segment)
    {
        if (segment == null || string.IsNullOrWhiteSpace(segment.Text)) return;

        lock (_lock)
        {
            _segments.Add(segment);
        }

        TryTriggerSummaryIfEligible();
    }

    public void UpdateSegment(TranscriptSegment segment)
    {
        if (segment == null) return;

        lock (_lock)
        {
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

            // Condición 1: Comprobar intervalo transcurrido (mínimo SummaryInterval, ej. 60s)
            bool timeConditionMet = _cards.Count == 0
                ? elapsed >= TimeSpan.FromSeconds(45)
                : timeSinceLastCard >= _config.SummaryInterval;

            if (!timeConditionMet) return;

            // Condición 2: Comprobar que hay segmentos nuevos desde el último tramo resumido
            var unsummarized = _segments.Where(s => s.EndTime > _lastSummarizedEndTime).ToList();
            if (unsummarized.Count == 0) return;

            int newWordsInWindow = CountTotalWords(unsummarized);
            if (newWordsInWindow >= _config.MinimumNewWordsThreshold)
            {
                shouldTrigger = true;
            }
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
                    _logger?.LogError(ex, "Error en ciclo asíncrono de resumen en vivo.");
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

        if (pendingSegments.Count > 0 && CountTotalWords(pendingSegments) >= 15)
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

        lock (_lock)
        {
            if (_isGenerating) return;
            if (_segments.Count == 0) return;

            _isGenerating = true;
            lang = _language;
            cancelToken = _meetingCts?.Token ?? CancellationToken.None;

            // Extraer segmentos no resumidos aún
            windowSegments = _segments.Where(s => s.EndTime > _lastSummarizedEndTime).ToList();

            if (windowSegments.Count == 0)
            {
                // Si no hay nuevos, tomar los últimos disponibles para refrescar si aplica
                windowSegments = _segments.TakeLast(5).ToList();
            }

            cardStartTime = _lastSummarizedEndTime;
            cardEndTime = windowSegments[^1].EndTime;
            if (cardEndTime <= cardStartTime)
            {
                cardEndTime = cardStartTime + TimeSpan.FromSeconds(60);
            }

            windowText = FormatSegmentsForPrompt(windowSegments);
        }

        GeneratingStateChanged?.Invoke(this, true);

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken, externalCancellationToken);

            _logger?.LogDebug("Ejecutando inferencia de tarjeta de resumen en vivo para tramo {Start} - {End}...", cardStartTime, cardEndTime);
            var cardSummary = await _summaryService.GenerateSummaryAsync(string.Empty, windowText, lang, linkedCts.Token);

            if (!string.IsNullOrWhiteSpace(cardSummary))
            {
                var now = DateTimeOffset.Now;
                var timeRangeFormatted = $"{FormatTimeSpan(cardStartTime)} - {FormatTimeSpan(cardEndTime)}";

                var newCard = new SummarySegment
                {
                    Id = Guid.NewGuid().ToString("N"),
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

                _logger?.LogInformation("Tarjeta de resumen generada con éxito ({Range}): {Words} palabras.", timeRangeFormatted, CountTotalWords(new[] { new TranscriptSegment { Text = cardSummary } }));
                SummaryUpdated?.Invoke(this, new SummaryUpdatedEventArgs(totalSummaryText, now, _summaryService.LastMetrics, newCard, allCardsSnapshot));
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Ciclo de resumen cancelado.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Fallo al generar tarjeta de resumen en vivo (Fallback activo: la transcripción continúa intacta).");
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
        foreach (var seg in segments)
        {
            var speaker = !string.IsNullOrWhiteSpace(seg.SpeakerDisplayName)
                ? seg.SpeakerDisplayName
                : (!string.IsNullOrWhiteSpace(seg.SpeakerId) ? seg.SpeakerId : null);

            if (!string.IsNullOrWhiteSpace(speaker))
            {
                sb.AppendLine($"{speaker}: {seg.Text.Trim()}");
            }
            else
            {
                sb.AppendLine(seg.Text.Trim());
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
