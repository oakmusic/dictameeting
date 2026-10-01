using DictaMeeting.App.ViewModels;
using DictaMeeting.Audio.Events;
using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Models;
using DictaMeeting.Audio.Processing;
using DictaMeeting.Audio.Services;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class MicrophoneMuteFeatureTests
{
    [Fact]
    public void MockAudioCaptureService_MuteStateAndEvent_WorkCorrectly()
    {
        using var captureService = new MockAudioCaptureService();
        Assert.False(captureService.IsMicrophoneMuted);

        var eventRaisedCount = 0;
        bool? lastEventVal = null;
        captureService.MicrophoneMuteChanged += (_, muted) =>
        {
            eventRaisedCount++;
            lastEventVal = muted;
        };

        captureService.IsMicrophoneMuted = true;
        Assert.True(captureService.IsMicrophoneMuted);

        captureService.IsMicrophoneMuted = false;
        Assert.False(captureService.IsMicrophoneMuted);
    }

    [Fact]
    public void AudioStreamMixer_WithMutedMicrophone_EmitsOnlySystemAudio()
    {
        var mixer = new AudioMixer();
        var streamMixer = new AudioStreamMixer(mixer);
        var emittedChunks = new List<MixedAudioChunk>();

        streamMixer.MixedChunkAvailable += (_, chunk) => emittedChunks.Add(chunk);

        // Simular 3200 muestras de micrófono silenciado (todos ceros)
        var mutedMicSamples = new float[3200];
        Array.Clear(mutedMicSamples, 0, mutedMicSamples.Length);

        // Simular 3200 muestras de audio de sistema activo (0.4f)
        var sysSamples = new float[3200];
        Array.Fill(sysSamples, 0.4f);

        streamMixer.EnqueueMicrophoneSamples(mutedMicSamples);
        streamMixer.EnqueueSystemSamples(sysSamples);

        Assert.True(emittedChunks.Count >= 2);
        var chunk = emittedChunks[0];

        // El nivel RMS del micrófono debe ser 0 absoluto
        Assert.Equal(0f, chunk.MicRms);
        // El nivel del sistema debe ser mayor que 0
        Assert.True(chunk.SysRms > 0f);
        // La fuente dominante debe ser el audio del sistema
        Assert.Equal(AudioDeviceType.SystemLoopback, chunk.DominantSource);
        // Las muestras mezcladas deben ser iguales al audio del sistema
        Assert.Equal(0.4f, chunk.Samples[0], precision: 2);
    }

    [Fact]
    public void MainViewModel_ToggleMuteMicrophone_UpdatesMutePropertiesAndText()
    {
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vm_mute_test_{Guid.NewGuid():N}.json");
        try
        {
            var userSettingsService = new DictaMeeting.Infrastructure.Persistence.UserSettingsService(tempFile);
            var repo = new TwoPassModelSelectionTests.DummyMeetingRepository();
            var meetingService = new DictaMeeting.Meetings.Services.MeetingService(repo);
            var docExporter = new DictaMeeting.Meetings.Exporters.DocumentExporter();
            using var audioDeviceService = new AudioDeviceService();
            var audioCaptureService = new MockAudioCaptureService();
            var modelManager = new TwoPassModelSelectionTests.DummyModelManager();
            var transcriptionService = new MockTranscriptionService();
            var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
            var diarizationService = new DictaMeeting.Diarization.Services.PyAnnoteCommunity1DiarizationService();
            var speakerAligner = new DictaMeeting.Diarization.Pipeline.SpeakerTranscriptAligner();
            var secureStorage = new DictaMeeting.Infrastructure.Security.WindowsDpapiSecureStorageService();
            var aiActaService = new DictaMeeting.AI.Services.OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

            var vm = new MainViewModel(
                meetingService,
                docExporter,
                repo,
                audioDeviceService,
                audioCaptureService,
                modelManager,
                transcriptionService,
                livePipeline,
                diarizationService,
                speakerAligner,
                aiActaService,
                secureStorage,
                userSettingsService);

            // Estado inicial: no silenciado
            Assert.False(vm.IsMicrophoneMuted);
            Assert.Equal("Silenciar", vm.MuteButtonText);
            Assert.Equal("Micrófono", vm.MicrophoneLiveLabelText);
            Assert.Contains("Silenciar micrófono temporalmente", vm.MuteButtonToolTip);

            // Activar silencio
            vm.ToggleMuteMicrophoneCommand.Execute(null);

            Assert.True(vm.IsMicrophoneMuted);
            Assert.True(audioCaptureService.IsMicrophoneMuted);
            Assert.Equal("Silenciado", vm.MuteButtonText);
            Assert.Equal("Mic (Silenciado)", vm.MicrophoneLiveLabelText);
            Assert.Contains("reactivar", vm.MuteButtonToolTip, StringComparison.OrdinalIgnoreCase);

            // Desactivar silencio
            vm.ToggleMuteMicrophoneCommand.Execute(null);

            Assert.False(vm.IsMicrophoneMuted);
            Assert.False(audioCaptureService.IsMicrophoneMuted);
            Assert.Equal("Silenciar", vm.MuteButtonText);
            Assert.Equal("Micrófono", vm.MicrophoneLiveLabelText);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void MainViewModel_CanMuteMicrophone_RequiresCaptureMicrophoneEnabled()
    {
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vm_mute_can_test_{Guid.NewGuid():N}.json");
        try
        {
            var userSettingsService = new DictaMeeting.Infrastructure.Persistence.UserSettingsService(tempFile);
            var repo = new TwoPassModelSelectionTests.DummyMeetingRepository();
            var meetingService = new DictaMeeting.Meetings.Services.MeetingService(repo);
            var docExporter = new DictaMeeting.Meetings.Exporters.DocumentExporter();
            using var audioDeviceService = new AudioDeviceService();
            var audioCaptureService = new MockAudioCaptureService();
            var modelManager = new TwoPassModelSelectionTests.DummyModelManager();
            var transcriptionService = new MockTranscriptionService();
            var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
            var diarizationService = new DictaMeeting.Diarization.Services.PyAnnoteCommunity1DiarizationService();
            var speakerAligner = new DictaMeeting.Diarization.Pipeline.SpeakerTranscriptAligner();
            var secureStorage = new DictaMeeting.Infrastructure.Security.WindowsDpapiSecureStorageService();
            var aiActaService = new DictaMeeting.AI.Services.OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

            var vm = new MainViewModel(
                meetingService,
                docExporter,
                repo,
                audioDeviceService,
                audioCaptureService,
                modelManager,
                transcriptionService,
                livePipeline,
                diarizationService,
                speakerAligner,
                aiActaService,
                secureStorage,
                userSettingsService);

            // En reposo (IsRecording = false), CanMuteMicrophoneDuringRecording es false
            Assert.False(vm.CanMuteMicrophoneDuringRecording);

            // Si CaptureMicrophone es falso, no se puede silenciar
            vm.CaptureMicrophone = false;
            Assert.False(vm.CanMuteMicrophoneDuringRecording);

            // Si no se captura micrófono, ToggleMute no tiene efecto
            vm.ToggleMuteMicrophoneCommand.Execute(null);
            Assert.False(vm.IsMicrophoneMuted);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void AudioCaptureService_IsMicrophoneMuted_RaisesEventAndResetsLevel()
    {
        var mixer = new AudioMixer();
        var recService = new DummyRecordingService();
        using var captureService = new AudioCaptureService(mixer, recService);

        Assert.False(captureService.IsMicrophoneMuted);

        var raised = false;
        bool? mutedVal = null;
        captureService.MicrophoneMuteChanged += (_, val) =>
        {
            raised = true;
            mutedVal = val;
        };

        var levelReset = false;
        captureService.AudioLevelUpdated += (_, args) =>
        {
            if (args.DeviceType == AudioDeviceType.Microphone && args.PeakLevel == 0f && args.RmsLevel == 0f)
            {
                levelReset = true;
            }
        };

        captureService.IsMicrophoneMuted = true;

        Assert.True(captureService.IsMicrophoneMuted);
        Assert.True(raised);
        Assert.True(mutedVal);
        Assert.True(levelReset);
    }

    [Fact]
    public void AudioCaptureService_IsSystemAudioMuted_RaisesEventAndResetsLevel()
    {
        var mixer = new AudioMixer();
        var recService = new DummyRecordingService();
        using var captureService = new AudioCaptureService(mixer, recService);

        Assert.False(captureService.IsSystemAudioMuted);

        var raised = false;
        bool? mutedVal = null;
        captureService.SystemAudioMuteChanged += (_, val) =>
        {
            raised = true;
            mutedVal = val;
        };

        var levelReset = false;
        captureService.AudioLevelUpdated += (_, args) =>
        {
            if (args.DeviceType == AudioDeviceType.SystemLoopback && args.PeakLevel == 0f && args.RmsLevel == 0f)
            {
                levelReset = true;
            }
        };

        captureService.IsSystemAudioMuted = true;

        Assert.True(captureService.IsSystemAudioMuted);
        Assert.True(raised);
        Assert.True(mutedVal);
        Assert.True(levelReset);
    }

    [Fact]
    public void AudioStreamMixer_WithMutedSystemAudio_EmitsOnlyMicrophone()
    {
        var mixer = new AudioMixer();
        var streamMixer = new AudioStreamMixer(mixer);
        var emittedChunks = new List<MixedAudioChunk>();

        streamMixer.MixedChunkAvailable += (_, chunk) => emittedChunks.Add(chunk);

        // Simular 3200 muestras de micrófono activo (0.5f)
        var micSamples = new float[3200];
        Array.Fill(micSamples, 0.5f);

        // Simular 3200 muestras de audio de sistema silenciado (todos ceros)
        var mutedSysSamples = new float[3200];
        Array.Clear(mutedSysSamples, 0, mutedSysSamples.Length);

        streamMixer.EnqueueMicrophoneSamples(micSamples);
        streamMixer.EnqueueSystemSamples(mutedSysSamples);

        Assert.True(emittedChunks.Count >= 2);
        var chunk = emittedChunks[0];

        // El nivel RMS del sistema debe ser 0
        Assert.Equal(0f, chunk.SysRms);
        // El nivel del micrófono debe ser mayor que 0
        Assert.True(chunk.MicRms > 0f);
        // La fuente dominante debe ser el micrófono
        Assert.Equal(AudioDeviceType.Microphone, chunk.DominantSource);
        // Las muestras mezcladas deben ser iguales a las del micrófono
        Assert.Equal(0.5f, chunk.Samples[0], precision: 2);
    }

    [Fact]
    public void MainViewModel_ToggleMuteSystemAudio_UpdatesPropertiesAndText()
    {
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vm_sysmute_test_{Guid.NewGuid():N}.json");
        try
        {
            var userSettingsService = new DictaMeeting.Infrastructure.Persistence.UserSettingsService(tempFile);
            var repo = new TwoPassModelSelectionTests.DummyMeetingRepository();
            var meetingService = new DictaMeeting.Meetings.Services.MeetingService(repo);
            var docExporter = new DictaMeeting.Meetings.Exporters.DocumentExporter();
            using var audioDeviceService = new AudioDeviceService();
            var audioCaptureService = new MockAudioCaptureService();
            var modelManager = new TwoPassModelSelectionTests.DummyModelManager();
            var transcriptionService = new MockTranscriptionService();
            var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
            var diarizationService = new DictaMeeting.Diarization.Services.PyAnnoteCommunity1DiarizationService();
            var speakerAligner = new DictaMeeting.Diarization.Pipeline.SpeakerTranscriptAligner();
            var secureStorage = new DictaMeeting.Infrastructure.Security.WindowsDpapiSecureStorageService();
            var aiActaService = new DictaMeeting.AI.Services.OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

            var vm = new MainViewModel(
                meetingService,
                docExporter,
                repo,
                audioDeviceService,
                audioCaptureService,
                modelManager,
                transcriptionService,
                livePipeline,
                diarizationService,
                speakerAligner,
                aiActaService,
                secureStorage,
                userSettingsService);

            // Estado inicial: no silenciado
            Assert.False(vm.IsSystemAudioMuted);
            Assert.Equal("Audio del sistema", vm.SystemAudioLiveLabelText);
            Assert.Contains("Silenciar audio del sistema", vm.SystemAudioToolTip);

            // Activar silencio de sistema
            vm.ToggleMuteSystemAudioCommand.Execute(null);

            Assert.True(vm.IsSystemAudioMuted);
            Assert.True(audioCaptureService.IsSystemAudioMuted);
            Assert.Equal("Sistema (Silenciado)", vm.SystemAudioLiveLabelText);
            Assert.Contains("reactivar", vm.SystemAudioToolTip, StringComparison.OrdinalIgnoreCase);

            // Desactivar silencio
            vm.ToggleMuteSystemAudioCommand.Execute(null);

            Assert.False(vm.IsSystemAudioMuted);
            Assert.False(audioCaptureService.IsSystemAudioMuted);
            Assert.Equal("Audio del sistema", vm.SystemAudioLiveLabelText);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }

    private class DummyRecordingService : IAudioRecordingService
    {
        public bool IsRecording { get; set; }
        public string? CurrentOutputFilePath { get; set; }
        public void StartRecording(string destinationCompressedFilePath) { IsRecording = true; }
        public void WriteChunk(ReadOnlySpan<float> floatSamples) { }
        public Task<string> StopRecordingAsync(CancellationToken cancellationToken = default) { IsRecording = false; return Task.FromResult(string.Empty); }
        public void Dispose() { }
    }
}
