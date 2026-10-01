using System.Collections.Concurrent;
using System.IO;
using DictaMeeting.Diarization.Interfaces;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.App.Services;

public class MeetingProcessingService : IMeetingProcessingService
{
    private readonly IMeetingRepository _repository;
    private readonly ITranscriptionService _transcriptionService;
    private readonly IModelManager _modelManager;
    private readonly ISpeakerDiarizationService _diarizationService;
    private readonly ISpeakerTranscriptAligner _speakerAligner;
    private readonly IPunctuationService _punctuationService;
    private readonly ILogger<MeetingProcessingService>? _logger;

    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationSources = new();

    private volatile bool _isProcessing;
    private string? _currentMeetingId;
    private double _currentProgress;
    private string _currentStatusText = string.Empty;

    public bool IsProcessing => _isProcessing;
    public string? CurrentMeetingId => _currentMeetingId;
    public double CurrentProgress => _currentProgress;
    public string CurrentStatusText => _currentStatusText;

    public event EventHandler<MeetingProcessingProgressEventArgs>? ProgressChanged;
    public event EventHandler<MeetingProcessingCompletedEventArgs>? ProcessingCompleted;

    public Task? CurrentProcessingTask { get; private set; }

    public MeetingProcessingService(
        IMeetingRepository repository,
        ITranscriptionService transcriptionService,
        IModelManager modelManager,
        ISpeakerDiarizationService diarizationService,
        ISpeakerTranscriptAligner speakerAligner,
        IPunctuationService? punctuationService = null,
        ILogger<MeetingProcessingService>? logger = null)
    {
        _repository = repository;
        _transcriptionService = transcriptionService;
        _modelManager = modelManager;
        _diarizationService = diarizationService;
        _speakerAligner = speakerAligner;
        _punctuationService = punctuationService ?? new DictaMeeting.Transcription.Services.Punctuation.OnnxPunctuationService();
        _logger = logger;
    }

    public Task EnqueueOrProcessAsync(string meetingId, string? targetModel = null, CancellationToken cancellationToken = default)
    {
        var task = ProcessMeetingInternalAsync(meetingId, targetModel, cancellationToken);
        CurrentProcessingTask = task;
        return task;
    }

    private async Task ProcessMeetingInternalAsync(string meetingId, string? targetModel, CancellationToken callerCancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
        _cancellationSources[meetingId] = cts;

        ProcessingStatus finalStatus = ProcessingStatus.Error;
        string? finalError = null;

        try
        {
            await _semaphore.WaitAsync(cts.Token);
            _isProcessing = true;
            _currentMeetingId = meetingId;
            _currentProgress = 0.05;
            _currentStatusText = "Iniciando procesamiento en segundo plano...";
            ReportProgress(meetingId, 0.05, _currentStatusText);

            var meeting = await _repository.GetMeetingAsync(meetingId, cts.Token);
            if (meeting == null)
            {
                throw new InvalidOperationException($"Reunión no encontrada: {meetingId}");
            }

            if (string.IsNullOrWhiteSpace(meeting.AudioFilePath) || !File.Exists(meeting.AudioFilePath))
            {
                meeting.ProcessingStatus = ProcessingStatus.Error;
                meeting.ProcessingError = "No se encontró el archivo de audio guardado en disco.";
                meeting.ProcessingStatusText = "Error: Falta el archivo de audio grabado.";
                await _repository.SaveMeetingAsync(meeting, CancellationToken.None);
                finalStatus = ProcessingStatus.Error;
                finalError = meeting.ProcessingError;
                return;
            }

            // 1. Guardar persistentemente estado Processing
            meeting.ProcessingStatus = ProcessingStatus.Processing;
            meeting.ProcessingStatusText = "Procesando...";
            meeting.ProcessingProgress = 0.05;
            meeting.ProcessingError = null;
            if (!string.IsNullOrWhiteSpace(targetModel))
            {
                meeting.ProcessingModel = targetModel;
            }
            await _repository.SaveMeetingAsync(meeting, cts.Token);

            // 2. Determinar modelo de transcripción definitivo
            ModelSize modelSizeToUse = ModelSize.Qwen3_17B;
            if (!string.IsNullOrWhiteSpace(meeting.ProcessingModel) &&
                Enum.TryParse<ModelSize>(meeting.ProcessingModel, true, out var parsedSize))
            {
                modelSizeToUse = parsedSize;
            }
            else
            {
                var hw = await _modelManager.DetectHardwareAsync();
                var rec = hw.RecommendedFinalModel;
                var avail = _modelManager.GetAvailableModels();
                var recInfo = avail.FirstOrDefault(m => m.Size == rec);
                if (recInfo != null && recInfo.IsDownloaded)
                {
                    modelSizeToUse = rec;
                }
                else
                {
                    // Si el modelo recomendado final no está disponible (p. ej. el usuario lo eliminó deliberadamente),
                    // usar el primer modelo disponible que ya esté descargado para evitar descargas no deseadas.
                    var downloaded = avail.FirstOrDefault(m => m.IsDownloaded && m.Size == ModelSize.Qwen3_06B)
                                     ?? avail.FirstOrDefault(m => m.IsDownloaded);
                    modelSizeToUse = downloaded?.Size ?? rec;
                }
                meeting.ProcessingModel = modelSizeToUse.ToString();
            }

            // 3. Descarga del modelo si es requerido (5% a 20%)
            var available = _modelManager.GetAvailableModels();
            var modelInfo = available.FirstOrDefault(m => m.Size == modelSizeToUse);
            if (modelInfo != null && !modelInfo.IsDownloaded)
            {
                _currentStatusText = $"Descargando modelo {modelInfo.Name}...";
                ReportProgress(meetingId, 0.05, _currentStatusText);
                var dlProgress = new Progress<double>(p =>
                {
                    var progress = 0.05 + (p * 0.15);
                    ReportProgress(meetingId, progress, $"Descargando modelo {modelInfo.Name}: {(int)(p * 100)}%");
                });
                await _modelManager.EnsureModelDownloadedAsync(modelSizeToUse, dlProgress, cts.Token);
            }

            cts.Token.ThrowIfCancellationRequested();

            // 4. Transcripción ASR de alta fidelidad desde archivo guardado (20% a 70%)
            _currentStatusText = $"Ejecutando transcripción definitiva ({modelSizeToUse})...";
            ReportProgress(meetingId, 0.20, _currentStatusText);

            var asrProgress = new Progress<double>(p =>
            {
                var progress = 0.20 + (p * 0.50);
                ReportProgress(meetingId, progress, $"Transcripción definitiva ({modelSizeToUse}): {(int)(p * 100)}%");
            });

            var languageMode = LanguageMode.Spanish;
            if (!string.IsNullOrWhiteSpace(meeting.Language))
            {
                if (Enum.TryParse<LanguageMode>(meeting.Language, true, out var parsedMode))
                {
                    languageMode = parsedMode;
                }
                else if (meeting.Language.StartsWith("es", StringComparison.OrdinalIgnoreCase) ||
                         meeting.Language.IndexOf("español", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         meeting.Language.IndexOf("spanish", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    languageMode = LanguageMode.Spanish;
                }
                else if (meeting.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ||
                         meeting.Language.IndexOf("inglés", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         meeting.Language.IndexOf("english", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    languageMode = LanguageMode.English;
                }
                else if (meeting.Language.IndexOf("auto", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    languageMode = LanguageMode.Auto;
                }
            }

            var finalSegments = await _transcriptionService.TranscribeAudioFileAsync(
                meeting.AudioFilePath,
                modelSizeToUse,
                languageMode,
                asrProgress,
                cts.Token);

            cts.Token.ThrowIfCancellationRequested();

            // 5. Diarización acústica desde archivo guardado con PyAnnote Community-1 (70% a 95%)
            _currentStatusText = "Ejecutando diarización definitiva de interlocutores...";
            ReportProgress(meetingId, 0.70, _currentStatusText);

            if (!_modelManager.IsDiarizationModelDownloaded())
            {
                _currentStatusText = "Descargando modelos de diarización PyAnnote Community-1...";
                ReportProgress(meetingId, 0.70, _currentStatusText);
                var dlProgress = new Progress<double>(p =>
                {
                    var progress = 0.70 + (p * 0.05);
                    ReportProgress(meetingId, progress, $"Descargando diarización: {(int)(p * 100)}%");
                });
                await _modelManager.EnsureDiarizationModelDownloadedAsync(dlProgress, cts.Token);
            }

            var diarizationProgress = new Progress<double>(p =>
            {
                var progress = 0.75 + (p * 0.20);
                ReportProgress(meetingId, progress, $"Diarización definitiva: {(int)(p * 100)}%");
            });

            var diarizationResult = await _diarizationService.DiarizeAudioFileAsync(
                meeting.AudioFilePath,
                options: null,
                diarizationProgress,
                cts.Token);

            cts.Token.ThrowIfCancellationRequested();

            // 6. Reconciliación con nombres de participantes (95%)
            _currentStatusText = "Reconciliando participantes y nombres asignados...";
            ReportProgress(meetingId, 0.95, _currentStatusText);

            var baseSegments = finalSegments.Count > 0 ? finalSegments : meeting.Transcript;
            var reconciled = _speakerAligner.Align(baseSegments, diarizationResult, meeting.Participants);
            var candidateSegments = reconciled.ReconciledSegments.Count > 0 ? reconciled.ReconciledSegments : baseSegments;

            cts.Token.ThrowIfCancellationRequested();

            // 7. Restauración local de puntuación y capitalización con ONNX (97% a 99%)
            // Si el servicio está desactivado (por defecto), se preserva la transcripción directa de Qwen3-ASR
            if (_punctuationService.IsEnabled)
            {
                _currentStatusText = "Restaurando puntuación y capitalización de la transcripción...";
                ReportProgress(meetingId, 0.97, _currentStatusText);

                var punctProgress = new Progress<double>(p =>
                {
                    var progress = 0.97 + (p * 0.02);
                    ReportProgress(meetingId, progress, $"Restaurando puntuación: {(int)(p * 100)}%");
                });

                var finalPunctuatedSegments = await _punctuationService.RestorePunctuationAsync(
                    candidateSegments,
                    punctProgress,
                    cts.Token);

                if (finalPunctuatedSegments.Count > 0)
                {
                    meeting.Transcript.Clear();
                    meeting.Transcript.AddRange(finalPunctuatedSegments);
                }
                else if (reconciled.ReconciledSegments.Count > 0)
                {
                    meeting.Transcript.Clear();
                    meeting.Transcript.AddRange(reconciled.ReconciledSegments);
                }
            }
            else
            {
                _logger?.LogInformation("Restauración de puntuación desactivada. Preservando transcripción directa y de alta fidelidad de Qwen3-ASR.");
                if (candidateSegments.Count > 0)
                {
                    meeting.Transcript.Clear();
                    meeting.Transcript.AddRange(candidateSegments);
                }
                else if (reconciled.ReconciledSegments.Count > 0)
                {
                    meeting.Transcript.Clear();
                    meeting.Transcript.AddRange(reconciled.ReconciledSegments);
                }
            }

            // Sincronizar interlocutores detectados en la transcripción con la lista de participantes
            meeting.SyncParticipantsFromTranscript();

            meeting.ProcessingStatus = ProcessingStatus.Completed;
            meeting.ProcessingProgress = 1.0;
            meeting.ProcessingStatusText = "Procesamiento completado con éxito.";
            meeting.ProcessingError = null;

            await _repository.SaveMeetingAsync(meeting, CancellationToken.None);

            _currentStatusText = "Procesamiento completado con éxito.";
            ReportProgress(meetingId, 1.0, _currentStatusText);
            finalStatus = ProcessingStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Procesamiento de reunión {Id} cancelado por el usuario.", meetingId);
            try
            {
                var meeting = await _repository.GetMeetingAsync(meetingId);
                if (meeting != null)
                {
                    meeting.ProcessingStatus = ProcessingStatus.Cancelled;
                    meeting.ProcessingStatusText = "Procesamiento cancelado por el usuario.";
                    await _repository.SaveMeetingAsync(meeting, CancellationToken.None);
                }
            }
            catch { }
            finalStatus = ProcessingStatus.Cancelled;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error durante el procesamiento en segundo plano de la reunión {Id}.", meetingId);
            try
            {
                var meeting = await _repository.GetMeetingAsync(meetingId);
                if (meeting != null)
                {
                    meeting.ProcessingStatus = ProcessingStatus.Error;
                    meeting.ProcessingError = ex.Message;
                    meeting.ProcessingStatusText = $"Error: {ex.Message}";
                    await _repository.SaveMeetingAsync(meeting, CancellationToken.None);
                }
            }
            catch { }
            finalStatus = ProcessingStatus.Error;
            finalError = ex.Message;
        }
        finally
        {
            _cancellationSources.TryRemove(meetingId, out _);
            _isProcessing = false;
            _currentMeetingId = null;
            _semaphore.Release();
        }

        OnProcessingCompleted(meetingId, finalStatus, finalError);
    }

    public Task CancelProcessingAsync(string meetingId)
    {
        if (_cancellationSources.TryGetValue(meetingId, out var cts))
        {
            cts.Cancel();
            _logger?.LogInformation("Señal de cancelación enviada para reunión {Id}.", meetingId);
        }
        return Task.CompletedTask;
    }

    public async Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var meetings = await _repository.GetAllMeetingsAsync(cancellationToken);
            foreach (var meeting in meetings)
            {
                if (meeting.ProcessingStatus == ProcessingStatus.Processing)
                {
                    meeting.ProcessingStatus = ProcessingStatus.Pending;
                    meeting.ProcessingStatusText = "Procesamiento pendiente (interrumpido).";
                    await _repository.SaveMeetingAsync(meeting, cancellationToken);
                    _logger?.LogInformation("Reunión {Id} recuperada a estado Pending tras interrupción previa.", meeting.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al recuperar reuniones pendientes o interrumpidas.");
        }
    }

    private void ReportProgress(string meetingId, double progress, string statusText)
    {
        _currentProgress = progress;
        _currentStatusText = statusText;
        ProgressChanged?.Invoke(this, new MeetingProcessingProgressEventArgs(meetingId, progress, statusText));
    }

    private void OnProcessingCompleted(string meetingId, ProcessingStatus status, string? errorMessage = null)
    {
        ProcessingCompleted?.Invoke(this, new MeetingProcessingCompletedEventArgs(meetingId, status, errorMessage));
    }
}
