using DictaMeeting.Transcription.Models;

namespace DictaMeeting.Transcription.Interfaces;

public interface IModelManager
{
    string ModelsDirectory { get; }
    void SetModelsDirectory(string path);

    Task<HardwareCapabilities> DetectHardwareAsync();
    IReadOnlyList<TranscriptionModelInfo> GetAvailableModels();

    // Comprobación de estado y rutas sin descarga
    bool IsModelDownloaded(ModelSize modelSize) => GetAvailableModels().FirstOrDefault(m => m.Size == modelSize)?.IsDownloaded ?? false;
    string GetModelPath(ModelSize modelSize) => GetAvailableModels().FirstOrDefault(m => m.Size == modelSize)?.LocalPath ?? string.Empty;
    bool IsModelIncomplete(ModelSize modelSize) => false;

    // Descarga explícita invocada únicamente por acción del usuario
    Task<string> DownloadModelAsync(ModelSize modelSize, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        => EnsureModelDownloadedAsync(modelSize, progress, cancellationToken);
    Task<string> EnsureModelDownloadedAsync(ModelSize modelSize, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    bool DeleteModel(ModelSize modelSize);

    // Diarización
    bool IsDiarizationModelDownloaded();
    string GetDiarizationModelPath() => System.IO.Path.Combine(ModelsDirectory, "diarization");
    bool IsDiarizationModelIncomplete() => false;
    Task<string> DownloadDiarizationModelAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        => EnsureDiarizationModelDownloadedAsync(progress, cancellationToken);
    Task<string> EnsureDiarizationModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    // Silero VAD
    bool IsVadModelDownloaded() => false;
    string GetVadModelPath() => System.IO.Path.Combine(ModelsDirectory, "silero", "silero_vad.onnx");
    bool IsVadModelIncomplete() => false;
    Task<string> DownloadVadModelAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        => Task.FromResult(GetVadModelPath());
}

