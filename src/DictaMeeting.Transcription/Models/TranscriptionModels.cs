namespace DictaMeeting.Transcription.Models;

public enum ModelSize
{
    Tiny,
    Base,
    Small,
    Medium,
    LargeV3Turbo,
    Qwen3_06B,
    Qwen3_17B
}

public enum ModelEngineType
{
    Whisper,
    Qwen3
}

public class ModelFileDownloadInfo
{
    public string RelativePath { get; set; } = string.Empty;
    public string PrimaryUrl { get; set; } = string.Empty;
    public string? FallbackUrl { get; set; }
    public long ExpectedSizeBytes { get; set; }
}

public class TranscriptionModelInfo
{
    public ModelSize Size { get; set; }
    public ModelEngineType Engine { get; set; } = ModelEngineType.Whisper;
    public string FamilyName { get; set; } = "Whisper (OpenAI)";
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public bool IsDownloaded { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public IReadOnlyList<ModelFileDownloadInfo>? DownloadFiles { get; set; }

    public static bool IsQwenModel(ModelSize size) => size is ModelSize.Qwen3_06B or ModelSize.Qwen3_17B;
}

public class HardwareCapabilities
{
    public int CpuLogicalCores { get; set; }
    public double TotalRamGb { get; set; }
    public bool HasDedicatedGpu { get; set; }
    public string GpuName { get; set; } = string.Empty;
    public double? VramGb { get; set; }
    public ModelSize RecommendedModel { get; set; }
    public ModelSize RecommendedLiveModel { get; set; }
    public ModelSize RecommendedFinalModel { get; set; }
}
