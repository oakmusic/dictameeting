using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Services;
using DictaMeeting.App.ViewModels;
using DictaMeeting.Audio.Services;
using DictaMeeting.Diarization.Pipeline;
using DictaMeeting.Diarization.Services;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Services;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class UserSettingsServiceTests
{
    [Fact]
    public void UserSettingsService_SaveAndLoad_RoundtripsSuccessfully()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dictameeting_settings_test_{Guid.NewGuid():N}.json");
        try
        {
            var service = new UserSettingsService(tempFile);

            var original = new UserSettings
            {
                CaptureMicrophone = true,
                SelectedMicrophoneId = "mic-123",
                SelectedMicrophoneName = "Microfono USB HyperX",
                CaptureSystemAudio = false,
                SelectedSystemDeviceId = "loopback-456",
                SelectedSystemDeviceName = "Altavoces Realtek",
                SelectedLiveModel = "Base",
                SelectedFinalModel = "LargeV3Turbo",
                SelectedLanguage = "Spanish",
                UiLanguage = "en",
                IsDarkMode = true
            };

            service.SaveSettings(original);

            Assert.True(File.Exists(tempFile));

            var loaded = service.LoadSettings();

            Assert.NotNull(loaded);
            Assert.True(loaded.CaptureMicrophone);
            Assert.Equal("mic-123", loaded.SelectedMicrophoneId);
            Assert.Equal("Microfono USB HyperX", loaded.SelectedMicrophoneName);
            Assert.False(loaded.CaptureSystemAudio);
            Assert.Equal("loopback-456", loaded.SelectedSystemDeviceId);
            Assert.Equal("Altavoces Realtek", loaded.SelectedSystemDeviceName);
            Assert.Equal("Base", loaded.SelectedLiveModel);
            Assert.Equal("LargeV3Turbo", loaded.SelectedFinalModel);
            Assert.Equal("Spanish", loaded.SelectedLanguage);
            Assert.Equal("en", loaded.UiLanguage);
            Assert.True(loaded.IsDarkMode);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { /* Ignore transient file locks */ }
            }
        }
    }

    [Fact]
    public void UserSettingsService_LoadNonExistent_ReturnsDefaults()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid():N}.json");
        var service = new UserSettingsService(tempFile, systemCultureProvider: () => new System.Globalization.CultureInfo("es-ES"));

        var settings = service.LoadSettings();

        Assert.NotNull(settings);
        Assert.True(settings.CaptureMicrophone);
        Assert.True(settings.CaptureSystemAudio);
        Assert.Equal("es", settings.UiLanguage);
        Assert.False(settings.IsDarkMode);
    }

    [Fact]
    public void MainViewModel_SettingsModalAndSummaries_WorkCorrectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"vm_settings_test_{Guid.NewGuid():N}.json");
        try
        {
            var userSettingsService = new UserSettingsService(tempFile);

            // Seed initial settings
            userSettingsService.SaveSettings(new UserSettings
            {
                CaptureMicrophone = true,
                SelectedLiveModel = "Base",
                SelectedFinalModel = "SameAsLive",
                SelectedLanguage = "Spanish"
            });

            var repo = new TwoPassModelSelectionTests.DummyMeetingRepository();
            var meetingService = new MeetingService(repo);
            var docExporter = new DocumentExporter();
            var audioDeviceService = new AudioDeviceService();
            var audioCaptureService = new MockAudioCaptureService();
            var modelManager = new TwoPassModelSelectionTests.DummyModelManager();
            var transcriptionService = new MockTranscriptionService();
            var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
            var diarizationService = new PyAnnoteCommunity1DiarizationService();
            var speakerAligner = new SpeakerTranscriptAligner();
            var aiActaService = new OpenRouterActaService(new HttpClient(), new WindowsDpapiSecureStorageService());
            var secureStorage = new WindowsDpapiSecureStorageService();

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

            // Modal initial state
            Assert.False(vm.IsMeetingSettingsModalOpen);

            // Open modal
            vm.OpenMeetingSettingsCommand.Execute(null);
            Assert.True(vm.IsMeetingSettingsModalOpen);

            // Close modal
            vm.CloseMeetingSettingsCommand.Execute(null);
            Assert.False(vm.IsMeetingSettingsModalOpen);

            // Test summary properties
            Assert.NotNull(vm.MicrophoneSummary);
            Assert.NotNull(vm.SystemAudioSummary);
            Assert.Contains("En vivo:", vm.LiveModelSummary);
            Assert.Contains("Final:", vm.FinalModelSummary);
            Assert.Contains("Idioma:", vm.LanguageSummary);

            // Toggling microphone updates summary and persists
            vm.CaptureMicrophone = false;
            Assert.Equal("Micro: Desactivado", vm.MicrophoneSummary);

            var reloaded = userSettingsService.LoadSettings();
            Assert.False(reloaded.CaptureMicrophone);

            // Action buttons initial state: disabled before meeting is created
            Assert.False(vm.CanOpenMeetingFolder);
            Assert.False(vm.CanExportMeeting);
            Assert.False(vm.HasCurrentMeeting);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { /* Ignore transient file locks */ }
            }
        }
    }

    [Fact]
    public void MainViewModel_RestoresDisabledAudioAndModels_OnStartup()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"vm_settings_restore_test_{Guid.NewGuid():N}.json");
        try
        {
            var userSettingsService = new UserSettingsService(tempFile);

            // Seed settings with microphone disabled and english language
            userSettingsService.SaveSettings(new UserSettings
            {
                CaptureMicrophone = false,
                CaptureSystemAudio = true,
                SelectedLiveModel = "Qwen3_06B",
                SelectedFinalModel = "LargeV3Turbo",
                SelectedLanguage = "English"
            });

            var repo = new TwoPassModelSelectionTests.DummyMeetingRepository();
            var meetingService = new MeetingService(repo);
            var docExporter = new DocumentExporter();
            var audioDeviceService = new AudioDeviceService();
            var audioCaptureService = new MockAudioCaptureService();
            var modelManager = new TwoPassModelSelectionTests.DummyModelManager();
            var transcriptionService = new MockTranscriptionService();
            var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
            var diarizationService = new PyAnnoteCommunity1DiarizationService();
            var speakerAligner = new SpeakerTranscriptAligner();
            var aiActaService = new OpenRouterActaService(new HttpClient(), new WindowsDpapiSecureStorageService());
            var secureStorage = new WindowsDpapiSecureStorageService();

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

            // Assert: Microphone is restored as disabled
            Assert.False(vm.CaptureMicrophone);
            Assert.True(vm.CaptureSystemAudio);
            Assert.Equal("English", vm.LanguageDisplayShort);
            Assert.Equal("0", vm.ParticipantsSummaryText);
            Assert.Contains("Micrófono: Desactivado", vm.AudioSourcesToolTip);

            // Settings file on disk must not have been overwritten back to true
            var loaded = userSettingsService.LoadSettings();
            Assert.False(loaded.CaptureMicrophone);
            Assert.True(loaded.CaptureSystemAudio);
            Assert.Equal("English", loaded.SelectedLanguage);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}

