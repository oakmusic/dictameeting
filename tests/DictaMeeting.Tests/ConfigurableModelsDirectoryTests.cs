using System.IO;
using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Services;
using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Models;
using DictaMeeting.Diarization.Interfaces;
using DictaMeeting.Diarization.Services;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Vocabulary;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using DictaMeeting.App.ViewModels;
using Xunit;

namespace DictaMeeting.Tests;

public class ConfigurableModelsDirectoryTests : IDisposable
{
    private readonly string _testBaseDir;

    public ConfigurableModelsDirectoryTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_ModelsDirTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBaseDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testBaseDir))
            {
                Directory.Delete(_testBaseDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void Test1_DefaultLocationResolution_WhenNotConfigured()
    {
        // Arrange
        var settings = new UserSettings();

        // Act & Assert
        Assert.Null(settings.ModelsDirectory);
        Assert.False(settings.IsCustomModelsDirectory);
        var expectedDefault = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models");
        Assert.Equal(expectedDefault, settings.EffectiveModelsDirectory);
    }

    [Fact]
    public void Test2_CustomLocationResolution_WhenConfigured()
    {
        // Arrange
        var customPath = Path.Combine(_testBaseDir, "CustomModels");
        Directory.CreateDirectory(customPath);
        var settings = new UserSettings { ModelsDirectory = customPath };

        // Act & Assert
        Assert.True(settings.IsCustomModelsDirectory);
        Assert.Equal(customPath, settings.EffectiveModelsDirectory);
    }

    [Fact]
    public void Test3_ModelManager_DynamicallyUpdatesPaths_WhenModelsDirectoryChanged()
    {
        // Arrange
        var initialDir = Path.Combine(_testBaseDir, "InitialModels");
        var newDir = Path.Combine(_testBaseDir, "NewModels");
        Directory.CreateDirectory(initialDir);
        Directory.CreateDirectory(newDir);

        var manager = new ModelManager(initialDir);

        // Act & Assert Initial
        Assert.Equal(Path.GetFullPath(initialDir), manager.ModelsDirectory);
        var models1 = manager.GetAvailableModels();
        var tiny1 = models1.First(m => m.Size == ModelSize.Tiny);
        Assert.StartsWith(Path.GetFullPath(initialDir), tiny1.LocalPath);

        // Act & Assert After Change
        manager.SetModelsDirectory(newDir);
        Assert.Equal(Path.GetFullPath(newDir), manager.ModelsDirectory);
        var models2 = manager.GetAvailableModels();
        var tiny2 = models2.First(m => m.Size == ModelSize.Tiny);
        Assert.StartsWith(Path.GetFullPath(newDir), tiny2.LocalPath);
    }

    [Fact]
    public void Test4_ModelStorageManager_ScansValidModels_AndIgnoresJunkAndTemporaries()
    {
        // Arrange
        var modelsDir = Path.Combine(_testBaseDir, "ScanTest");
        Directory.CreateDirectory(modelsDir);

        // 1. Whisper ggml model
        var whisperPath = Path.Combine(modelsDir, "ggml-tiny.bin");
        File.WriteAllBytes(whisperPath, new byte[1024 * 1024 + 100]); // > 1MB

        // 2. Qwen3-ASR 0.6B directory
        var qwen06Dir = Path.Combine(modelsDir, "qwen3-asr-0.6b-int8");
        Directory.CreateDirectory(qwen06Dir);
        File.WriteAllBytes(Path.Combine(qwen06Dir, "conv_frontend.onnx"), new byte[100]);
        File.WriteAllBytes(Path.Combine(qwen06Dir, "encoder.int8.onnx"), new byte[100]);
        File.WriteAllBytes(Path.Combine(qwen06Dir, "decoder.int8.onnx"), new byte[100]);

        // 3. Junk & Temporaries that should NOT be detected
        File.WriteAllText(Path.Combine(modelsDir, "some_log.log"), "Log data");
        File.WriteAllText(Path.Combine(modelsDir, "temp_file.tmp"), "Temp data");
        File.WriteAllBytes(Path.Combine(modelsDir, "ggml-base.bin.download"), new byte[500]);
        var unknownSubdir = Path.Combine(modelsDir, "unknown_user_folder");
        Directory.CreateDirectory(unknownSubdir);
        File.WriteAllText(Path.Combine(unknownSubdir, "notes.txt"), "Important user note");

        // Act
        var scanned = ModelStorageManager.ScanModels(modelsDir);

        // Assert
        Assert.Equal(2, scanned.Count);
        Assert.Contains(scanned, s => s.Id == "ggml-tiny.bin" && s.IsValid);
        Assert.Contains(scanned, s => s.Id == "qwen3_06b" && s.IsValid);
        Assert.DoesNotContain(scanned, s => s.RelativePath.EndsWith(".log"));
        Assert.DoesNotContain(scanned, s => s.RelativePath.EndsWith(".tmp"));
        Assert.DoesNotContain(scanned, s => s.RelativePath.EndsWith(".download"));
        Assert.DoesNotContain(scanned, s => s.RelativePath == "unknown_user_folder");
    }

    [Fact]
    public void Test5_ValidateDestination_DetectsSamePathOrSubdirectoryErrors()
    {
        // Arrange
        var sourceDir = Path.Combine(_testBaseDir, "SourceModels");
        Directory.CreateDirectory(sourceDir);
        var subDir = Path.Combine(sourceDir, "NestedTarget");

        // Act & Assert 1: Same directory
        var sameResult = ModelStorageManager.ValidateDestination(sourceDir, sourceDir);
        Assert.False(sameResult.IsValid);
        Assert.Contains("misma", sameResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Act & Assert 2: Subdirectory of source
        var subResult = ModelStorageManager.ValidateDestination(sourceDir, subDir);
        Assert.False(subResult.IsValid);
        Assert.Contains("dentro", subResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Act & Assert 3: Valid separate directory
        var validTarget = Path.Combine(_testBaseDir, "TargetModels");
        var validResult = ModelStorageManager.ValidateDestination(sourceDir, validTarget);
        Assert.True(validResult.IsValid);
        Assert.Null(validResult.ErrorMessage);
    }

    [Fact]
    public async Task Test6_SafeMigration_CopiesVerifiesAndDeletesOriginals()
    {
        // Arrange
        var sourceDir = Path.Combine(_testBaseDir, "Source6");
        var destDir = Path.Combine(_testBaseDir, "Dest6");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);

        // Create Whisper model in source
        var sourceWhisper = Path.Combine(sourceDir, "ggml-base.bin");
        File.WriteAllBytes(sourceWhisper, new byte[1024 * 1024 + 500]);

        // Create Qwen 0.6B model in source
        var sourceQwen = Path.Combine(sourceDir, "qwen3-asr-0.6b-int8");
        Directory.CreateDirectory(sourceQwen);
        File.WriteAllBytes(Path.Combine(sourceQwen, "conv_frontend.onnx"), new byte[200]);
        File.WriteAllBytes(Path.Combine(sourceQwen, "encoder.int8.onnx"), new byte[200]);
        File.WriteAllBytes(Path.Combine(sourceQwen, "decoder.int8.onnx"), new byte[200]);

        // Also create an unknown user file in source that must NOT be deleted
        var userDoc = Path.Combine(sourceDir, "user_readme.txt");
        File.WriteAllText(userDoc, "Do not touch me");

        // Act
        await ModelStorageManager.MigrateModelsAsync(sourceDir, destDir);

        // Assert
        // Destination has verified models
        var destScanned = ModelStorageManager.ScanModels(destDir);
        Assert.Equal(2, destScanned.Count);
        Assert.True(File.Exists(Path.Combine(destDir, "ggml-base.bin")));
        Assert.True(ModelManager.IsQwenDirectoryValid(Path.Combine(destDir, "qwen3-asr-0.6b-int8")));

        // Source originals for models have been removed
        Assert.False(File.Exists(sourceWhisper));
        Assert.False(Directory.Exists(sourceQwen));

        // Unknown user file in source was PRESERVED!
        Assert.True(File.Exists(userDoc));
        Assert.Equal("Do not touch me", File.ReadAllText(userDoc));
    }

    [Fact]
    public async Task Test7_SafeMigration_DestinationAlreadyHasValidModel_DoesNotReCopyOrOverwrite()
    {
        // Arrange
        var sourceDir = Path.Combine(_testBaseDir, "Source7");
        var destDir = Path.Combine(_testBaseDir, "Dest7");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);

        // Whisper model in source
        var sourceWhisper = Path.Combine(sourceDir, "ggml-small.bin");
        File.WriteAllBytes(sourceWhisper, new byte[1024 * 1024 + 1000]);

        // Destination ALREADY has this valid Whisper model!
        var destWhisper = Path.Combine(destDir, "ggml-small.bin");
        var existingContent = new byte[1024 * 1024 + 2000];
        existingContent[0] = 42; // Marker byte
        File.WriteAllBytes(destWhisper, existingContent);

        // Act
        await ModelStorageManager.MigrateModelsAsync(sourceDir, destDir);

        // Assert
        // The existing destination model was NOT overwritten
        var destBytes = File.ReadAllBytes(destWhisper);
        Assert.Equal(42, destBytes[0]);
        Assert.Equal(1024 * 1024 + 2000, destBytes.Length);

        // Source original was deleted after verification
        Assert.False(File.Exists(sourceWhisper));
    }

    [Fact]
    public async Task Test8_SafeMigration_OnCancellation_KeepsSourceIntactAndCleansPartials()
    {
        // Arrange
        var sourceDir = Path.Combine(_testBaseDir, "Source8");
        var destDir = Path.Combine(_testBaseDir, "Dest8");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);

        var sourceWhisper = Path.Combine(sourceDir, "ggml-tiny.bin");
        File.WriteAllBytes(sourceWhisper, new byte[1024 * 1024 + 500]);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await ModelStorageManager.MigrateModelsAsync(sourceDir, destDir, cancellationToken: cts.Token);
        });

        // Source model is 100% intact
        Assert.True(File.Exists(sourceWhisper));

        // Destination has no corrupted files
        Assert.Empty(Directory.GetFiles(destDir));
    }

    [Fact]
    public void Test9_DriveInaccessible_Detection()
    {
        // Arrange
        string inaccessiblePath = @"Z:\NonExistentDrive_9999\Models";

        // Act
        bool isAccessible = ModelStorageManager.IsDirectoryAccessible(inaccessiblePath);

        // Assert
        Assert.False(isAccessible);
    }

    [Fact]
    public void Test10_DeleteModel_DeletesFromConfiguredFolder()
    {
        // Arrange
        var customFolder = Path.Combine(_testBaseDir, "DeleteTestModels");
        Directory.CreateDirectory(customFolder);

        var manager = new ModelManager(customFolder);
        var models = manager.GetAvailableModels();
        var tiny = models.First(m => m.Size == ModelSize.Tiny);

        // Create file in custom folder
        File.WriteAllBytes(tiny.LocalPath, new byte[1024 * 1024 + 50]);
        Assert.True(File.Exists(tiny.LocalPath));

        // Act
        bool deleted = manager.DeleteModel(ModelSize.Tiny);

        // Assert
        Assert.True(deleted);
        Assert.False(File.Exists(tiny.LocalPath));
    }

    [Fact]
    public void Test11_ChangeWithoutMove_PreservesOldModelsAndUpdatesConfig()
    {
        // Arrange
        var oldFolder = Path.Combine(_testBaseDir, "OldFolder");
        var newFolder = Path.Combine(_testBaseDir, "NewFolder");
        Directory.CreateDirectory(oldFolder);
        Directory.CreateDirectory(newFolder);

        // Create model in old folder
        var oldModelFile = Path.Combine(oldFolder, "ggml-tiny.bin");
        File.WriteAllBytes(oldModelFile, new byte[1024 * 1024 + 100]);

        var settingsFile = Path.Combine(_testBaseDir, "settings.json");
        var settingsService = new UserSettingsService(settingsFile);
        var modelManager = new ModelManager(oldFolder);
        var diarService = new PyAnnoteCommunity1DiarizationService(Path.Combine(oldFolder, "diarization"));
        var summaryManager = new LocalLiveSummaryModelManager(Path.Combine(oldFolder, "summary"));

        // Act: Change to new folder
        modelManager.SetModelsDirectory(newFolder);
        summaryManager.SetModelsDirectory(Path.Combine(newFolder, "summary"));
        diarService.SetModelsFolder(Path.Combine(newFolder, "diarization"));

        var settings = new UserSettings { ModelsDirectory = newFolder };
        settingsService.SaveSettings(settings);

        // Assert: Old model remains in old folder
        Assert.True(File.Exists(oldModelFile));

        // ModelManager is now pointing to new folder
        Assert.Equal(Path.GetFullPath(newFolder), modelManager.ModelsDirectory);
        var available = modelManager.GetAvailableModels();
        var tiny = available.First(m => m.Size == ModelSize.Tiny);
        Assert.False(tiny.IsDownloaded); // In new folder, it has not been downloaded yet

        // Reload settings verifies persistence
        var reloaded = settingsService.LoadSettings();
        Assert.Equal(newFolder, reloaded.ModelsDirectory);
        Assert.True(reloaded.IsCustomModelsDirectory);
    }
}
