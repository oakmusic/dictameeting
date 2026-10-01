using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class HardwareDetectorTests
{
    [Fact]
    public void Detect_ReturnsValidHardwareSpecifications()
    {
        // Act
        var capabilities = HardwareDetector.Detect();

        // Assert
        Assert.True(capabilities.CpuLogicalCores > 0, "Debe detectar al menos 1 núcleo de CPU.");
        Assert.True(capabilities.TotalRamGb > 0, "Debe detectar memoria RAM total válida.");
        Assert.NotNull(capabilities.GpuName);
    }

    [Fact]
    public void Detect_OnStandardMachine_RecommendsReasonableModel()
    {
        // Act
        var capabilities = HardwareDetector.Detect();

        // Assert - La recomendación debe ser un modelo soportado
        Assert.True(
            capabilities.RecommendedModel == Transcription.Models.ModelSize.Tiny ||
            capabilities.RecommendedModel == Transcription.Models.ModelSize.Qwen3_06B);

        Assert.True(
            capabilities.RecommendedLiveModel == Transcription.Models.ModelSize.Tiny ||
            capabilities.RecommendedLiveModel == Transcription.Models.ModelSize.Qwen3_06B);

        Assert.True(
            capabilities.RecommendedFinalModel == Transcription.Models.ModelSize.Tiny ||
            capabilities.RecommendedFinalModel == Transcription.Models.ModelSize.Qwen3_17B);
    }

    [Fact]
    public void Detect_OnStandardMachine_RecommendsQwen3_06B_Live_And_Qwen3_17B_Final_AsDefault()
    {
        var capabilities = HardwareDetector.Detect();
        if (capabilities.TotalRamGb >= 4.0)
        {
            Assert.Equal(Transcription.Models.ModelSize.Qwen3_06B, capabilities.RecommendedLiveModel);
            Assert.Equal(Transcription.Models.ModelSize.Qwen3_17B, capabilities.RecommendedFinalModel);
            Assert.Equal(Transcription.Models.ModelSize.Qwen3_06B, capabilities.RecommendedModel);
        }
    }
}
