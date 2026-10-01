using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.Transcription.Services;

/// <summary>
/// Despachador unificado que enruta de forma transparente las peticiones de transcripción
/// hacia el motor correspondiente (Whisper o Qwen3-ASR) según el modelo configurado.
/// </summary>
public class CompositeTranscriptionService : ITranscriptionService
{
    private readonly WhisperTranscriptionService _whisperService;
    private readonly SherpaQwenTranscriptionService _qwenService;
    private readonly ILogger<CompositeTranscriptionService>? _logger;

    private ModelSize _currentModel = ModelSize.Qwen3_06B;
    private LanguageMode _currentLanguage = LanguageMode.Auto;
    private bool _disposed;

    public bool IsInitialized => TranscriptionModelInfo.IsQwenModel(_currentModel)
        ? _qwenService.IsInitialized
        : _whisperService.IsInitialized;

    public ModelSize CurrentModel => _currentModel;

    public LanguageMode CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            _currentLanguage = value;
            _whisperService.CurrentLanguage = value;
            _qwenService.CurrentLanguage = value;
        }
    }

    public CompositeTranscriptionService(
        WhisperTranscriptionService whisperService,
        SherpaQwenTranscriptionService qwenService,
        ILogger<CompositeTranscriptionService>? logger = null)
    {
        _whisperService = whisperService;
        _qwenService = qwenService;
        _logger = logger;
    }

    public async Task InitializeAsync(ModelSize modelSize, CancellationToken cancellationToken = default)
    {
        _currentModel = modelSize;

        if (TranscriptionModelInfo.IsQwenModel(modelSize))
        {
            _logger?.LogInformation("Enrutando inicialización al motor Qwen3-ASR para modelo {Model}.", modelSize);
            _qwenService.CurrentLanguage = _currentLanguage;
            await _qwenService.InitializeAsync(modelSize, cancellationToken);
        }
        else
        {
            _logger?.LogInformation("Enrutando inicialización al motor Whisper para modelo {Model}.", modelSize);
            _whisperService.CurrentLanguage = _currentLanguage;
            await _whisperService.InitializeAsync(modelSize, cancellationToken);
        }
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
        if (TranscriptionModelInfo.IsQwenModel(modelSize))
        {
            _logger?.LogInformation("Transcribiendo archivo con motor Qwen3-ASR ({Model}, {Lang}).", modelSize, language);
            return await _qwenService.TranscribeAudioFileAsync(audioFilePath, modelSize, language, progress, cancellationToken);
        }

        _logger?.LogInformation("Transcribiendo archivo con motor Whisper ({Model}, {Lang}).", modelSize, language);
        return await _whisperService.TranscribeAudioFileAsync(audioFilePath, modelSize, language, progress, cancellationToken);
    }

    public async Task<TranscriptSegment?> TranscribeAudioChunkAsync(
        byte[] pcmAudioData,
        TimeSpan offset,
        CancellationToken cancellationToken = default)
    {
        if (TranscriptionModelInfo.IsQwenModel(_currentModel))
        {
            return await _qwenService.TranscribeAudioChunkAsync(pcmAudioData, offset, cancellationToken);
        }

        return await _whisperService.TranscribeAudioChunkAsync(pcmAudioData, offset, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _whisperService.Dispose();
        _qwenService.Dispose();
    }
}
