using DictaMeeting.Meetings.Enums;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class QwenAndCompositeTranscriptionTests
{
    [Theory]
    [InlineData(ModelSize.Qwen3_06B, true)]
    [InlineData(ModelSize.Qwen3_17B, true)]
    [InlineData(ModelSize.Tiny, false)]
    [InlineData(ModelSize.Base, false)]
    [InlineData(ModelSize.Small, false)]
    [InlineData(ModelSize.Medium, false)]
    [InlineData(ModelSize.LargeV3Turbo, false)]
    public void IsQwenModel_ReturnsExpectedResult(ModelSize size, bool expectedIsQwen)
    {
        Assert.Equal(expectedIsQwen, TranscriptionModelInfo.IsQwenModel(size));
    }

    [Fact]
    public void ModelCatalog_ContainsBothQwen06B_And_Qwen17B()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_QwenTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new ModelManager(tempDir);
            var models = manager.GetAvailableModels();

            var qwen06 = models.FirstOrDefault(m => m.Size == ModelSize.Qwen3_06B);
            var qwen17 = models.FirstOrDefault(m => m.Size == ModelSize.Qwen3_17B);

            Assert.NotNull(qwen06);
            Assert.Contains("0.6B", qwen06.Name);
            Assert.Equal(ModelEngineType.Qwen3, qwen06.Engine);
            Assert.True(qwen06.FileSizeBytes > 0);

            Assert.NotNull(qwen17);
            Assert.Contains("1.7B", qwen17.Name);
            Assert.Equal(ModelEngineType.Qwen3, qwen17.Engine);
            Assert.True(qwen17.FileSizeBytes > qwen06.FileSizeBytes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void CompositeTranscriptionService_CurrentLanguage_PropagatesToUnderlyingEngines()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_CompTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new ModelManager(tempDir);
            var whisperService = new WhisperTranscriptionService(manager);
            var qwenService = new SherpaQwenTranscriptionService(manager);
            using var composite = new CompositeTranscriptionService(whisperService, qwenService);

            composite.CurrentLanguage = LanguageMode.Spanish;

            Assert.Equal(LanguageMode.Spanish, composite.CurrentLanguage);
            Assert.Equal(LanguageMode.Spanish, whisperService.CurrentLanguage);
            Assert.Equal(LanguageMode.Spanish, qwenService.CurrentLanguage);

            composite.CurrentLanguage = LanguageMode.English;
            Assert.Equal(LanguageMode.English, composite.CurrentLanguage);
            Assert.Equal(LanguageMode.English, whisperService.CurrentLanguage);
            Assert.Equal(LanguageMode.English, qwenService.CurrentLanguage);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
