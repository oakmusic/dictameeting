using DictaMeeting.Transcription.Models;

namespace DictaMeeting.Transcription.Interfaces;

public interface IModelManager
{
    string ModelsDirectory { get; }
    void SetModelsDirectory(string path);

    Task<HardwareCapabilities> DetectHardwareAsync();
    IReadOnlyList<TranscriptionModelInfo> GetAvailableModels();
    Task<string> EnsureModelDownloadedAsync(ModelSize modelSize, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    bool DeleteModel(ModelSize modelSize);

    bool IsDiarizationModelDownloaded();
    Task<string> EnsureDiarizationModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

