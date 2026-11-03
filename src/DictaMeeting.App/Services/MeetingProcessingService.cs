using System.Collections.Concurrent;
using System.IO;
using DictaMeeting.App.Diagnostics;
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

    public Task EnqueueOrProcessAsync(
        string meetingId,
        string? targetModel = null,
        ProcessingMode mode = ProcessingMode.TranscriptionAndDiarization,
        CancellationToken cancellationToken = default)
    {
        var task = ProcessMeetingInternalAsync(meetingId, targetModel, mode, cancellationToken);
        CurrentProcessingTask = task;
        return task;
    }

    private async Task ProcessMeetingInternalAsync(
        string meetingId,
        string? targetModel,
        ProcessingMode mode,
        CancellationToken callerCancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
        _cancellationSources[meetingId] = cts;

        ProcessingStatus finalStatus = ProcessingStatus.Error;
        string? finalError = null;

        using var op = CrashDiagnosticTracker.BeginOperation("Meeting_BackgroundProcessing", meetingId);
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

            // Validar requerimientos para el modo solicitado
            if (mode == ProcessingMode.DiarizationOnly)
            {
                if (meeting.Transcript == null || meeting.Transcript.Count == 0)
                {
                    meeting.ProcessingStatus = ProcessingStatus.Error;
                    meeting.DiarizationStatus = DiarizationStatus.Error;
                    meeting.DiarizationError = "No se puede ejecutar solo diarización porque la reunión no tiene una transcripción previa.";
                    meeting.ProcessingStatusText = "Error: Falta transcripción previa para diarizar.";
                    await _repository.SaveMeetingAsync(meeting, CancellationToken.None);
                    finalStatus = ProcessingStatus.Error;
                    finalError = meeting.DiarizationError;
                    return;
                }
            }

            // 1. Guardar persistentemente estado Processing
            meeting.ProcessingStatus = ProcessingStatus.Processing;
            meeting.ProcessingProgress = 0.05;
            meeting.ProcessingError = null;

            if (mode == ProcessingMode.TranscriptionOnly)
            {
                meeting.TranscriptionStatus = TranscriptionStatus.Transcribing;
                meeting.TranscriptionError = null;
                meeting.ProcessingStatusText = "Transcribiendo audio...";
            }
            else if (mode == ProcessingMode.DiarizationOnly)
            {
                meeting.DiarizationStatus = DiarizationStatus.Diarizing;
                meeting.DiarizationError = null;
                meeting.ProcessingStatusText = "Diarizando interlocutores...";
            }
            else // TranscriptionAndDiarization
            {
                meeting.TranscriptionStatus = TranscriptionStatus.Transcribing;
                meeting.TranscriptionError = null;
                meeting.DiarizationStatus = DiarizationStatus.Pending;
                meeting.DiarizationError = null;
                meeting.ProcessingStatusText = "Procesando transcripción y diarización...";
            }

            if (!string.IsNullOrWhiteSpace(targetModel))
            {
                meeting.ProcessingModel = targetModel;
            }
            await _repository.SaveMeetingAsync(meeting, cts.Token);

            List<TranscriptSegment> finalSegments = new();

            // =========================================================================
            // FASE 1: TRANSCRIPCIÓN ASR (si el modo lo incluye)
            // =========================================================================
            if (mode == ProcessingMode.TranscriptionOnly || mode == ProcessingMode.TranscriptionAndDiarization)
            {
                // Determinar modelo de transcripción definitivo
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
                        var downloaded = avail.FirstOrDefault(m => m.IsDownloaded && m.Size == ModelSize.Qwen3_06B)
                                         ?? avail.FirstOrDefault(m => m.IsDownloaded);
                        modelSizeToUse = downloaded?.Size ?? rec;
                    }
                    meeting.ProcessingModel = modelSizeToUse.ToString();
                }

                // Descarga del modelo si es requerido
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

                // Transcripción ASR de alta fidelidad desde archivo guardado
                double asrStart = mode == ProcessingMode.TranscriptionOnly ? 0.10 : 0.15;
                double asrSpan = mode == ProcessingMode.TranscriptionOnly ? 0.80 : 0.35;

                _currentStatusText = $"Ejecutando transcripción definitiva ({modelSizeToUse})...";
                ReportProgress(meetingId, asrStart, _currentStatusText);

                var asrProgress = new Progress<double>(p =>
                {
                    var progress = asrStart + (p * asrSpan);
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

                finalSegments = (await _transcriptionService.TranscribeAudioFileAsync(
                    meeting.AudioFilePath,
                    modelSizeToUse,
                    languageMode,
                    asrProgress,
                    cts.Token)).ToList();

                cts.Token.ThrowIfCancellationRequested();

                // Guardar transcripción inmediatamente a disco para asegurar su persistencia
                meeting.Transcript.Clear();
                meeting.Transcript.AddRange(finalSegments);
                meeting.TranscriptionStatus = TranscriptionStatus.Completed;
                meeting.TranscriptionError = null;

                if (mode == ProcessingMode.TranscriptionOnly)
                {
                    // Si solo se transcribió, opcionalmente restaurar puntuación y finalizar
                    if (_punctuationService.IsEnabled)
                    {
                        _currentStatusText = "Restaurando puntuación y capitalización...";
                        ReportProgress(meetingId, 0.92, _currentStatusText);
                        var punctuated = await _punctuationService.RestorePunctuationAsync(
                            meeting.Transcript,
                            new Progress<double>(p => ReportProgress(meetingId, 0.92 + (p * 0.06), $"Restaurando puntuación: {(int)(p * 100)}%")),
                            cts.Token);

                        if (punctuated.Count > 0)
                        {
                            meeting.Transcript.Clear();
                            meeting.Transcript.AddRange(punctuated);
                        }
                    }

                    meeting.ProcessingStatus = ProcessingStatus.Completed;
                    meeting.ProcessingProgress = 1.0;
                    meeting.ProcessingStatusText = "Transcripción completada con éxito.";
                    meeting.ProcessingError = null;
                    await _repository.SaveMeetingAsync(meeting, CancellationToken.None);

                    _currentStatusText = "Transcripción completada con éxito.";
                    ReportProgress(meetingId, 1.0, _currentStatusText);
                    finalStatus = ProcessingStatus.Completed;
                    return;
                }
                else
                {
                    // En modo combinado, guardar la transcripción completada antes de iniciar la diarización
                    meeting.ProcessingProgress = 0.50;
                    meeting.ProcessingStatusText = "Transcripción completada. Iniciando diarización...";
                    await _repository.SaveMeetingAsync(meeting, cts.Token);
                }
            }

            // =========================================================================
            // FASE 2: DIARIZACIÓN ACÚSTICA (si el modo lo incluye)
            // =========================================================================
            if (mode == ProcessingMode.DiarizationOnly || mode == ProcessingMode.TranscriptionAndDiarization)
            {
                try
                {
                    meeting.DiarizationStatus = DiarizationStatus.Diarizing;
                    meeting.DiarizationError = null;
                    await _repository.SaveMeetingAsync(meeting, cts.Token);

                    double diaStart = mode == ProcessingMode.DiarizationOnly ? 0.10 : 0.52;
                    double diaSpan = mode == ProcessingMode.DiarizationOnly ? 0.80 : 0.40;

                    if (!_modelManager.IsDiarizationModelDownloaded())
                    {
                        _currentStatusText = "Descargando modelos de diarización PyAnnote Community-1...";
                        ReportProgress(meetingId, diaStart, _currentStatusText);
                        var dlProgress = new Progress<double>(p =>
                        {
                            var progress = diaStart + (p * 0.05);
                            ReportProgress(meetingId, progress, $"Descargando diarización: {(int)(p * 100)}%");
                        });
                        await _modelManager.EnsureDiarizationModelDownloadedAsync(dlProgress, cts.Token);
                    }

                    cts.Token.ThrowIfCancellationRequested();

                    _currentStatusText = "Ejecutando diarización definitiva de interlocutores...";
                    ReportProgress(meetingId, diaStart + 0.05, _currentStatusText);

                    var diarizationProgress = new Progress<double>(p =>
                    {
                        var progress = diaStart + 0.05 + (p * (diaSpan - 0.05));
                        ReportProgress(meetingId, progress, $"Diarización definitiva: {(int)(p * 100)}%");
                    });

                    var diarizationResult = await _diarizationService.DiarizeAudioFileAsync(
                        meeting.AudioFilePath,
                        options: null,
                        diarizationProgress,
                        cts.Token);

                    cts.Token.ThrowIfCancellationRequested();

                    // Reconciliación y preservación de nombres de participantes
                    _currentStatusText = "Reconciliando participantes y nombres asignados...";
                    ReportProgress(meetingId, 0.95, _currentStatusText);

                    bool preserveExistingSegments = (mode == ProcessingMode.DiarizationOnly);
                    var baseSegments = meeting.Transcript.Count > 0 ? meeting.Transcript : finalSegments;

                    var reconciled = _speakerAligner.Align(
                        baseSegments,
                        diarizationResult,
                        meeting.Participants,
                        preserveExistingSegments: preserveExistingSegments,
                        existingCentroids: meeting.SpeakerCentroids);

                    if (preserveExistingSegments)
                    {
                        // En modo Solo Diarización, actualizar únicamente SpeakerId y SpeakerDisplayName sin alterar texto ni timestamps
                        for (int i = 0; i < meeting.Transcript.Count && i < reconciled.ReconciledSegments.Count; i++)
                        {
                            meeting.Transcript[i].SpeakerId = reconciled.ReconciledSegments[i].SpeakerId;
                            meeting.Transcript[i].SpeakerDisplayName = reconciled.ReconciledSegments[i].SpeakerDisplayName;
                        }
                    }
                    else
                    {
                        var candidateSegments = reconciled.ReconciledSegments.Count > 0 ? reconciled.ReconciledSegments : baseSegments;

                        if (_punctuationService.IsEnabled)
                        {
                            _currentStatusText = "Restaurando puntuación y capitalización...";
                            ReportProgress(meetingId, 0.97, _currentStatusText);
                            var punctuated = await _punctuationService.RestorePunctuationAsync(candidateSegments, null, cts.Token);
                            if (punctuated.Count > 0)
                            {
                                candidateSegments = punctuated;
                            }
                        }

                        meeting.Transcript.Clear();
                        meeting.Transcript.AddRange(candidateSegments);
                    }

                    // Guardar los nuevos centroides para futuras re-identificaciones
                    if (diarizationResult.SpeakerCentroids != null && diarizationResult.SpeakerCentroids.Count > 0)
                    {
                        meeting.SpeakerCentroids = diarizationResult.SpeakerCentroids.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
                    }

                    // Sincronizar interlocutores detectados en la transcripción con la lista de participantes
                    meeting.SyncParticipantsFromTranscript();

                    meeting.DiarizationStatus = DiarizationStatus.Completed;
                    meeting.DiarizationError = null;
                    meeting.ProcessingStatus = ProcessingStatus.Completed;
                    meeting.ProcessingProgress = 1.0;
                    meeting.ProcessingStatusText = "Procesamiento completado con éxito.";
                    meeting.ProcessingError = null;

                    await _repository.SaveMeetingAsync(meeting, CancellationToken.None);

                    _currentStatusText = "Procesamiento completado con éxito.";
                    ReportProgress(meetingId, 1.0, _currentStatusText);
                    finalStatus = ProcessingStatus.Completed;
                }
                catch (Exception ex) when (mode == ProcessingMode.TranscriptionAndDiarization)
                {
                    _logger?.LogError(ex, "Error durante la etapa de diarización de la reunión {Id}. La transcripción previa queda preservada.", meetingId);
                    meeting.DiarizationStatus = DiarizationStatus.Error;
                    meeting.DiarizationError = ex.Message;
                    meeting.ProcessingStatus = ProcessingStatus.Error;
                    meeting.ProcessingStatusText = $"Transcripción completada con éxito, pero la diarización falló: {ex.Message}";
                    await _repository.SaveMeetingAsync(meeting, CancellationToken.None);
                    finalStatus = ProcessingStatus.Error;
                    finalError = ex.Message;
                    return;
                }
            }
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
            CrashDiagnosticTracker.RecordCrashReport(ex, $"MeetingProcessingService ({meetingId})", isTerminating: false);
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
            OnProcessingCompleted(meetingId, finalStatus, finalError);
        }
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
        using var op = CrashDiagnosticTracker.BeginOperation("Startup_RecoverInterruptedJobs");
        try
        {
            var meetings = await _repository.GetAllMeetingsAsync(cancellationToken);
            foreach (var meeting in meetings)
            {
                bool modified = false;

                if (meeting.TranscriptionStatus == TranscriptionStatus.Transcribing)
                {
                    meeting.TranscriptionStatus = TranscriptionStatus.None;
                    meeting.ProcessingStatus = ProcessingStatus.Pending;
                    meeting.ProcessingStatusText = "Transcripción pendiente (interrumpida).";
                    modified = true;
                }

                if (meeting.DiarizationStatus == DiarizationStatus.Diarizing)
                {
                    meeting.DiarizationStatus = DiarizationStatus.Pending;
                    meeting.ProcessingStatus = ProcessingStatus.Pending;
                    meeting.ProcessingStatusText = "Diarización pendiente (interrumpida).";
                    modified = true;
                }

                if (meeting.ProcessingStatus == ProcessingStatus.Processing && !modified)
                {
                    meeting.ProcessingStatus = ProcessingStatus.Pending;
                    meeting.ProcessingStatusText = "Procesamiento pendiente (interrumpido).";
                    modified = true;
                }

                if (modified)
                {
                    await _repository.SaveMeetingAsync(meeting, cancellationToken);
                    _logger?.LogInformation("Reunión {Id} recuperada a estado Pending tras interrupción previa.", meeting.Id);
                    CrashDiagnosticTracker.AddBreadcrumb($"[Recuperación] Reunión {meeting.Id} restablecida tras interrupción.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al recuperar reuniones pendientes o interrumpidas.");
            CrashDiagnosticTracker.AddBreadcrumb($"[Error en RecoverInterruptedJobsAsync] {ex.Message}");
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
