using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Vocabulary;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Whisper.net;

namespace DictaMeeting.Transcription.Services;

public class WhisperTranscriptionService : ITranscriptionService
{
    private readonly IModelManager _modelManager;
    private readonly IVocabularyService? _vocabularyService;
    private readonly ILogger<WhisperTranscriptionService>? _logger;
    private readonly object _factoryLock = new();

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private ModelSize _currentModel = ModelSize.Base;
    private LanguageMode _currentLanguage = LanguageMode.Auto;
    private int _activeOperations;
    private bool _isInitialized;
    private bool _disposed;

    public bool IsInitialized
    {
        get
        {
            lock (_factoryLock)
            {
                return _isInitialized;
            }
        }
    }

    public ModelSize CurrentModel => _currentModel;

    public LanguageMode CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            lock (_factoryLock)
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    if (_isInitialized && _factory != null)
                    {
                        RebuildProcessor();
                    }
                }
            }
        }
    }

    public WhisperTranscriptionService(
        IModelManager modelManager,
        IVocabularyService? vocabularyService = null,
        ILogger<WhisperTranscriptionService>? logger = null)
    {
        _modelManager = modelManager;
        _vocabularyService = vocabularyService;
        _logger = logger;

        if (_vocabularyService != null)
        {
            _vocabularyService.VocabularyChanged += OnVocabularyChanged;
        }
    }

    private void OnVocabularyChanged(object? sender, EventArgs e)
    {
        lock (_factoryLock)
        {
            if (_isInitialized && _factory != null)
            {
                RebuildProcessor();
            }
        }
    }

    public async Task InitializeAsync(ModelSize modelSize, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_modelManager.IsModelDownloaded(modelSize))
            {
                throw new InvalidOperationException($"El modelo Whisper '{modelSize}' no está instalado. Debe descargarse manualmente antes de inicializar la transcripción.");
            }

            var modelPath = _modelManager.GetModelPath(modelSize);

            lock (_factoryLock)
            {
                _currentModel = modelSize;
                DisposeProcessorInternal();

                _factory = WhisperFactory.FromPath(modelPath);
                RebuildProcessor();
                _isInitialized = true;
            }

            _logger?.LogInformation("Motor Whisper inicializado con modelo {Model} e idioma {Language}.", modelSize, _currentLanguage);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al inicializar el motor Whisper con el modelo {Model}.", modelSize);
            throw;
        }
    }

    /// <summary>
    /// Descarga el modelo Whisper de memoria si no hay operaciones de inferencia activas.
    /// </summary>
    public void UnloadModel()
    {
        lock (_factoryLock)
        {
            if (_activeOperations > 0)
            {
                _logger?.LogDebug("No se puede descargar el modelo Whisper: hay {Count} operación(es) activa(s).", _activeOperations);
                return;
            }

            if (_factory != null)
            {
                _logger?.LogInformation("Descargando modelo Whisper de memoria para liberar recursos.");
                DisposeProcessorInternal();
            }
        }
    }

    private void RebuildProcessor()
    {
        if (_factory == null) return;

        _processor?.Dispose();

        int optimalThreads = Math.Max(1, Math.Min(Environment.ProcessorCount - 2, 8));
        var builder = _factory.CreateBuilder()
            .WithThreads(optimalThreads)
            .WithSegmentEventHandler(OnSegmentProcessed);

        string glossary = _vocabularyService?.FormatWhisperGlossaryPrompt() ?? string.Empty;

        switch (_currentLanguage)
        {
            case LanguageMode.Spanish:
                string promptEs = string.IsNullOrWhiteSpace(glossary)
                    ? "Transcripción de reunión en español de España, castellano peninsular."
                    : $"Transcripción de reunión en español de España, castellano peninsular. {glossary}";
                builder.WithLanguage("es")
                       .WithPrompt(promptEs);
                break;
            case LanguageMode.English:
                string promptEn = string.IsNullOrWhiteSpace(glossary)
                    ? "Meeting transcription in English."
                    : $"Meeting transcription in English. {glossary}";
                builder.WithLanguage("en")
                       .WithPrompt(promptEn);
                break;
            default:
                builder.WithLanguage("auto");
                if (!string.IsNullOrWhiteSpace(glossary))
                {
                    builder.WithPrompt(glossary);
                }
                break;
        }

        _processor = builder.Build();
    }

    private void OnSegmentProcessed(SegmentData segmentData)
    {
        // Segmento recibido durante el procesamiento continuo
    }

    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return TranscribeAudioFileAsync(audioFilePath, _currentModel, _currentLanguage, progress, cancellationToken);
    }

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(
        string audioFilePath,
        ModelSize modelSize,
        LanguageMode language,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(audioFilePath))
        {
            return Array.Empty<TranscriptSegment>();
        }

        if (!_modelManager.IsModelDownloaded(modelSize))
        {
            throw new InvalidOperationException($"El modelo Whisper '{modelSize}' no está instalado. Debe descargarse manualmente antes de transcribir.");
        }

        var modelPath = _modelManager.GetModelPath(modelSize);
        var segments = new List<TranscriptSegment>();
        var tempWavPath = Path.Combine(Path.GetTempPath(), $"dictameeting_asr_{Guid.NewGuid():N}.wav");

        try
        {
            await Task.Run(async () =>
            {
                // Cargar audio y asegurar formato 16 kHz Mono para Whisper sin cargarlo todo en memoria RAM
                using (var reader = new AudioFileReader(audioFilePath))
                {
                    var outFormat = new WaveFormat(16000, 16, 1);
                    using var resampler = new MediaFoundationResampler(reader, outFormat);
                    WaveFileWriter.CreateWaveFile(tempWavPath, resampler);
                }

                cancellationToken.ThrowIfCancellationRequested();

                WhisperFactory? tempFactory = null;
                WhisperProcessor processor;

                lock (_factoryLock)
                {
                    bool useExistingFactory = _factory != null && modelSize == _currentModel && !_disposed;
                    if (!useExistingFactory)
                    {
                        tempFactory = WhisperFactory.FromPath(modelPath);
                    }

                    WhisperFactory activeFactory = useExistingFactory ? _factory! : tempFactory!;
                    int optimalThreads = Math.Max(1, Math.Min(Environment.ProcessorCount - 2, 8));
                    var builder = activeFactory.CreateBuilder()
                        .WithThreads(optimalThreads);

                    string fileGlossary = _vocabularyService?.FormatWhisperGlossaryPrompt() ?? string.Empty;
                    switch (language)
                    {
                        case LanguageMode.Spanish:
                            string promptEs = string.IsNullOrWhiteSpace(fileGlossary)
                                ? "Transcripción de reunión en español de España, castellano peninsular."
                                : $"Transcripción de reunión en español de España, castellano peninsular. {fileGlossary}";
                            builder.WithLanguage("es")
                                   .WithPrompt(promptEs);
                            break;
                        case LanguageMode.English:
                            string promptEn = string.IsNullOrWhiteSpace(fileGlossary)
                                ? "Meeting transcription in English."
                                : $"Meeting transcription in English. {fileGlossary}";
                            builder.WithLanguage("en")
                                   .WithPrompt(promptEn);
                            break;
                        default:
                            builder.WithLanguage("auto");
                            if (!string.IsNullOrWhiteSpace(fileGlossary))
                            {
                                builder.WithPrompt(fileGlossary);
                            }
                            break;
                    }

                    processor = builder.Build();
                    _activeOperations++;
                }

                try
                {
                    using (processor)
                    using (var fileStream = File.OpenRead(tempWavPath))
                    {
                        long totalBytes = fileStream.Length;
                        if (totalBytes <= 0) totalBytes = 1;

                        await foreach (var segmentData in processor.ProcessAsync(fileStream, cancellationToken))
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var text = segmentData.Text?.Trim() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                if (_vocabularyService != null)
                                {
                                    text = _vocabularyService.ReplaceAliases(text);
                                }

                                segments.Add(new TranscriptSegment
                                {
                                    StartTime = segmentData.Start,
                                    EndTime = segmentData.End,
                                    Text = text,
                                    Confidence = segmentData.Probability,
                                    IsFinal = true
                                });
                            }

                            if (fileStream.Position > 0)
                            {
                                double p = Math.Min(1.0, (double)fileStream.Position / totalBytes);
                                progress?.Report(p);
                            }
                        }
                    }
                }
                finally
                {
                    lock (_factoryLock)
                    {
                        _activeOperations--;
                        tempFactory?.Dispose();
                    }
                }
            }, cancellationToken);
        }
        finally
        {
            try
            {
                if (File.Exists(tempWavPath))
                {
                    File.Delete(tempWavPath);
                }
            }
            catch { }
        }

        return segments;
    }

    public async Task<TranscriptSegment?> TranscribeAudioChunkAsync(
        byte[] pcmAudioData,
        TimeSpan offset,
        CancellationToken cancellationToken = default)
    {
        WhisperProcessor? currentProcessor;
        lock (_factoryLock)
        {
            if (!_isInitialized || _processor == null || pcmAudioData.Length == 0 || _disposed)
            {
                return null;
            }
            currentProcessor = _processor;
            _activeOperations++;
        }

        try
        {
            return await Task.Run(async () =>
            {
                using var wavStream = new MemoryStream();
                using (var writer = new WaveFileWriter(wavStream, new WaveFormat(16000, 16, 1)))
                {
                    writer.Write(pcmAudioData, 0, pcmAudioData.Length);
                }
                wavStream.Position = 0;

                var detectedTexts = new List<string>();
                TimeSpan start = offset;
                TimeSpan end = offset;

                await foreach (var segment in currentProcessor.ProcessAsync(wavStream, cancellationToken))
                {
                    var text = segment.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        detectedTexts.Add(text);
                        end = offset + segment.End;
                    }
                }

                if (detectedTexts.Count == 0) return null;

                var fullText = string.Join(" ", detectedTexts);
                if (_vocabularyService != null)
                {
                    fullText = _vocabularyService.ReplaceAliases(fullText);
                }

                return new TranscriptSegment
                {
                    StartTime = start,
                    EndTime = end > start ? end : start.Add(TimeSpan.FromSeconds(2)),
                    Text = fullText,
                    IsFinal = false
                };
            }, cancellationToken);
        }
        finally
        {
            lock (_factoryLock)
            {
                _activeOperations--;
            }
        }
    }

    private void DisposeProcessorInternal()
    {
        _processor?.Dispose();
        _processor = null;
        _factory?.Dispose();
        _factory = null;
        _isInitialized = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_vocabularyService != null)
        {
            _vocabularyService.VocabularyChanged -= OnVocabularyChanged;
        }

        lock (_factoryLock)
        {
            DisposeProcessorInternal();
        }
    }
}