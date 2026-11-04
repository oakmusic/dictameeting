using System;
using System.IO;
using System.Threading.Tasks;
using DictaMeeting.App.Services;
using DictaMeeting.Diarization.Interfaces;
using DictaMeeting.Diarization.Models;
using DictaMeeting.Diarization.Services;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class MemoryOptimizationTests
{
    [Fact]
    public void Test_MemoryPressureMonitor_ReturnsValidSnapshot()
    {
        var monitor = new MemoryPressureMonitor();
        var snapshot = monitor.GetCurrentSnapshot();

        Assert.True(snapshot.ProcessWorkingSetMB > 0, "Working set should be positive");
        Assert.True(snapshot.ManagedHeapMB >= 0, "Managed heap should be non-negative");
        Assert.True(snapshot.TotalPhysicalMB >= 0, "Total physical RAM should be non-negative");

        // Calling multiple times within 500ms should return cached snapshot (throttled)
        var snapshot2 = monitor.GetCurrentSnapshot();
        Assert.Equal(snapshot.PressureLevel, snapshot2.PressureLevel);
    }

    [Fact]
    public void Test_MemoryPressureMonitor_CanSafelyLoadModel_RejectsHugeAllocations()
    {
        var monitor = new MemoryPressureMonitor();

        // 1 TB model should be rejected
        long hugeModelBytes = 1024L * 1024 * 1024 * 1024;
        Assert.False(monitor.CanSafelyLoadModel(hugeModelBytes));

        // 100 KB should normally be acceptable unless RAM is critically exhausted
        long smallModelBytes = 100 * 1024;
        var snapshot = monitor.GetCurrentSnapshot();
        if (snapshot.AvailablePhysicalMB > 1000 && snapshot.PressureLevel != MemoryPressureLevel.Critical)
        {
            Assert.True(monitor.CanSafelyLoadModel(smallModelBytes));
        }
    }

    [Fact]
    public void Test_PyAnnote_UnloadPipeline_DoesNotThrow_WhenNoPipelineLoaded()
    {
        using var diarization = new PyAnnoteCommunity1DiarizationService();
        // Should be safe to call even if pipeline was never initialized
        diarization.UnloadPipeline();
        diarization.UnloadPipeline();
    }

    [Fact]
    public void Test_SherpaQwen_UnloadModel_DoesNotThrow_WhenNotInitialized()
    {
        var modelDir = Path.Combine(Path.GetTempPath(), $"dicta_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelDir);
        try
        {
            var modelManager = new ModelManager(modelDir);
            using var qwenService = new SherpaQwenTranscriptionService(modelManager);

            // Should be completely safe to call even when not initialized
            qwenService.UnloadModel();
            Assert.False(qwenService.IsInitialized);
        }
        finally
        {
            try { Directory.Delete(modelDir, true); } catch { }
        }
    }

    [Fact]
    public void Test_Whisper_UnloadModel_DoesNotThrow_WhenNotInitialized()
    {
        var modelDir = Path.Combine(Path.GetTempPath(), $"dicta_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(modelDir);
        try
        {
            var modelManager = new ModelManager(modelDir);
            using var whisperService = new WhisperTranscriptionService(modelManager);

            // Should be completely safe to call even when not initialized
            whisperService.UnloadModel();
            Assert.False(whisperService.IsInitialized);
        }
        finally
        {
            try { Directory.Delete(modelDir, true); } catch { }
        }
    }

    [Fact]
    public void Test_SileroVad_UnloadSequenceSession_DoesNotThrow_WhenNotLoaded()
    {
        using var vad = new SileroVoiceActivityDetector();
        // Should be completely safe to call
        vad.UnloadSequenceSession();
        vad.UnloadSequenceSession();
    }

    [Fact]
    public async Task Test_SherpaQwen_TranscribeAudioFile_ZeroAllocPInvoke_ProducesAccurateText()
    {
        string modelsDir = @"D:\TECNOLOGIA\PROYECTOS\WORK IN PROGRESS\DictaMeeting\DictaMeeting_BACKUP\models";
        string wavPath = Path.Combine(modelsDir, "qwen3-asr-0.6b-int8", "test_wavs", "es1.wav");
        if (!File.Exists(wavPath))
        {
            return; // Skip if backup models not present on local machine
        }

        var modelManager = new ModelManager(modelsDir);
        using var qwenService = new SherpaQwenTranscriptionService(modelManager);

        await qwenService.InitializeAsync(DictaMeeting.Transcription.Models.ModelSize.Qwen3_06B);
        Assert.True(qwenService.IsInitialized, "SherpaQwen should initialize successfully");

        var segments = await qwenService.TranscribeAudioFileAsync(wavPath);
        Assert.NotEmpty(segments);
        Assert.Contains("Esta prenda es amplia", segments[0].Text);

        qwenService.UnloadModel();
        Assert.False(qwenService.IsInitialized);
    }

}
