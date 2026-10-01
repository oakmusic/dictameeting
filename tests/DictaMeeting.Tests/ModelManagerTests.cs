using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class ModelManagerTests : IDisposable
{
    private readonly string _testModelsFolder;

    public ModelManagerTests()
    {
        _testModelsFolder = Path.Combine(Path.GetTempPath(), "DictaMeeting_ModelsTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testModelsFolder);
    }

    [Fact]
    public void GetAvailableModels_ReturnsCatalogWithSupportedSizes()
    {
        // Arrange
        var manager = new ModelManager(_testModelsFolder);

        // Act
        var models = manager.GetAvailableModels();

        // Assert
        Assert.NotNull(models);
        Assert.True(models.Count >= 5);
        Assert.Contains(models, m => m.Size == ModelSize.Tiny);
        Assert.Contains(models, m => m.Size == ModelSize.Base);
        Assert.Contains(models, m => m.Size == ModelSize.Small);
        Assert.Contains(models, m => m.Size == ModelSize.Medium);
        Assert.Contains(models, m => m.Size == ModelSize.LargeV3Turbo);
        Assert.Contains(models, m => m.Size == ModelSize.Qwen3_06B);
        Assert.Contains(models, m => m.Size == ModelSize.Qwen3_17B);
    }

    [Fact]
    public void GetAvailableModels_QwenModelsHaveCorrectMetadata()
    {
        // Arrange
        var manager = new ModelManager(_testModelsFolder);

        // Act
        var models = manager.GetAvailableModels();
        var qwen06 = models.First(m => m.Size == ModelSize.Qwen3_06B);
        var qwen17 = models.First(m => m.Size == ModelSize.Qwen3_17B);

        // Assert
        Assert.Equal(ModelEngineType.Qwen3, qwen06.Engine);
        Assert.Equal("Qwen3-ASR (Alibaba)", qwen06.FamilyName);
        Assert.True(qwen06.IsDirectory);

        Assert.Equal(ModelEngineType.Qwen3, qwen17.Engine);
        Assert.Equal("Qwen3-ASR (Alibaba)", qwen17.FamilyName);
        Assert.True(qwen17.IsDirectory);
        Assert.NotNull(qwen17.DownloadFiles);
        Assert.Equal(6, qwen17.DownloadFiles.Count);
        Assert.Contains(qwen17.DownloadFiles, f => f.RelativePath == "conv_frontend.onnx");
        Assert.Contains(qwen17.DownloadFiles, f => f.RelativePath == "encoder.int8.onnx");
        Assert.Contains(qwen17.DownloadFiles, f => f.RelativePath == "decoder.int8.onnx");
    }

    [Fact]
    public void GetAvailableModels_QwenModelsAppearFirstInCatalog()
    {
        // Arrange
        var manager = new ModelManager(_testModelsFolder);

        // Act
        var models = manager.GetAvailableModels();

        // Assert
        Assert.True(models.Count >= 2);
        Assert.Equal(ModelSize.Qwen3_06B, models[0].Size);
        Assert.Equal(ModelSize.Qwen3_17B, models[1].Size);
    }

    [Fact]
    public void ModelIsDownloaded_ReturnsTrue_WhenFileExistsAndHasValidSize()
    {
        // Arrange
        var manager = new ModelManager(_testModelsFolder);
        var models = manager.GetAvailableModels();
        var tinyModel = models.First(m => m.Size == ModelSize.Tiny);

        // Crear archivo simulado con más de 1 MB
        var dummyData = new byte[1024 * 1024 + 100];
        File.WriteAllBytes(tinyModel.LocalPath, dummyData);

        // Act
        var refreshedModels = manager.GetAvailableModels();
        var refreshedTiny = refreshedModels.First(m => m.Size == ModelSize.Tiny);

        // Assert
        Assert.True(refreshedTiny.IsDownloaded);
    }

    [Fact]
    public void ModelIsDownloaded_ReturnsTrue_ForQwen3_WhenDirectoryHasRequiredOnnxFiles()
    {
        // Arrange
        var manager = new ModelManager(_testModelsFolder);
        var models = manager.GetAvailableModels();
        var qwen06 = models.First(m => m.Size == ModelSize.Qwen3_06B);

        Directory.CreateDirectory(qwen06.LocalPath);
        File.WriteAllText(Path.Combine(qwen06.LocalPath, "conv_frontend.onnx"), "dummy");
        File.WriteAllText(Path.Combine(qwen06.LocalPath, "encoder.int8.onnx"), "dummy");
        File.WriteAllText(Path.Combine(qwen06.LocalPath, "decoder.int8.onnx"), "dummy");

        // Act
        var refreshed = manager.GetAvailableModels().First(m => m.Size == ModelSize.Qwen3_06B);

        // Assert
        Assert.True(refreshed.IsDownloaded);
    }

    [Fact]
    public void DeleteModel_DeletesExistingModelFile_AndReturnsTrue()
    {
        // Arrange
        var manager = new ModelManager(_testModelsFolder);
        var models = manager.GetAvailableModels();
        var tinyModel = models.First(m => m.Size == ModelSize.Tiny);

        // Crear archivo simulado
        File.WriteAllBytes(tinyModel.LocalPath, new byte[1024 * 1024 + 10]);
        Assert.True(File.Exists(tinyModel.LocalPath));

        // Act
        var result = manager.DeleteModel(ModelSize.Tiny);

        // Assert
        Assert.True(result);
        Assert.False(File.Exists(tinyModel.LocalPath));
        Assert.False(manager.GetAvailableModels().First(m => m.Size == ModelSize.Tiny).IsDownloaded);
    }

    [Fact]
    public void DeleteModel_DeletesQwen3Directory_AndReturnsTrue()
    {
        // Arrange
        var manager = new ModelManager(_testModelsFolder);
        var qwen06 = manager.GetAvailableModels().First(m => m.Size == ModelSize.Qwen3_06B);

        Directory.CreateDirectory(qwen06.LocalPath);
        File.WriteAllText(Path.Combine(qwen06.LocalPath, "conv_frontend.onnx"), "dummy");
        File.WriteAllText(Path.Combine(qwen06.LocalPath, "encoder.int8.onnx"), "dummy");
        File.WriteAllText(Path.Combine(qwen06.LocalPath, "decoder.int8.onnx"), "dummy");

        Assert.True(Directory.Exists(qwen06.LocalPath));

        // Act
        var result = manager.DeleteModel(ModelSize.Qwen3_06B);

        // Assert
        Assert.True(result);
        Assert.False(Directory.Exists(qwen06.LocalPath));
        Assert.False(manager.GetAvailableModels().First(m => m.Size == ModelSize.Qwen3_06B).IsDownloaded);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testModelsFolder))
            {
                Directory.Delete(_testModelsFolder, recursive: true);
            }
        }
        catch { }
    }
}
