using System.IO;
using DictaMeeting.AI.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class LiveSummaryModelManagerTests
{
    [Fact]
    public void ModelProperties_HaveCorrectQwen25Specification()
    {
        var manager = new LocalLiveSummaryModelManager();

        Assert.Equal("Qwen2.5-1.5B-Instruct", manager.ModelName);
        Assert.Equal("Q4_K_M", manager.Quantization);
        Assert.True(manager.ModelSizeBytes > 900L * 1024 * 1024);
        Assert.EndsWith("qwen2.5-1.5b-instruct-q4_k_m.gguf", manager.LocalModelPath);
    }

    [Fact]
    public void IsModelDownloaded_ReturnsFalse_WhenFileDoesNotExist()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"dictameeting_summary_test_{Guid.NewGuid():N}");
        try
        {
            var manager = new LocalLiveSummaryModelManager(tempDir);
            Assert.False(manager.IsModelDownloaded());
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void IsModelDownloaded_ReturnsTrue_WhenFileExistsWithSufficientSize()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"dictameeting_summary_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            string filePath = Path.Combine(tempDir, LocalLiveSummaryModelManager.DefaultFileName);

            // Crear archivo temporal con tamaño simulado mayor a 900 MB
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                fs.SetLength(950L * 1024 * 1024);
            }

            var manager = new LocalLiveSummaryModelManager(tempDir);
            Assert.True(manager.IsModelDownloaded());

            bool deleted = manager.DeleteModel();
            Assert.True(deleted);
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
