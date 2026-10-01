namespace DictaMeeting.AI.Interfaces;

/// <summary>
/// Gestor de descarga, verificación y almacenamiento del modelo local GGUF para resúmenes.
/// </summary>
public interface ILiveSummaryModelManager
{
    string ModelsDirectory { get; }
    void SetModelsDirectory(string path);

    string ModelName { get; }
    string Quantization { get; }
    long ModelSizeBytes { get; }
    string LocalModelPath { get; }

    string GetModelPath();
    bool IsModelDownloaded();
    Task<string> EnsureModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    bool DeleteModel();
}
