using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Services;
using DictaMeeting.App.ViewModels;
using DictaMeeting.Audio.Services;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Diarization.Pipeline;
using DictaMeeting.Diarization.Services;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Services;
using DictaMeeting.Transcription.Services;
using System.Text.Json;
using Xunit;

namespace DictaMeeting.Tests;

public class RecordingNoticeAndPrivacySettingsTests
{
    private static void SafeDelete(string path)
    {
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* Ignore transient file locks */ }
        }
    }

    private (MainViewModel Vm, UserSettingsService SettingsService, string TempFile, MockTranscriptionService Transcriber) CreateTestContext(bool seedHasSeen = false, bool seedShowNotice = true)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dictameeting_notice_test_{Guid.NewGuid():N}.json");
        var settingsService = new UserSettingsService(tempFile);
        settingsService.SaveSettings(new UserSettings
        {
            ShowRecordingNotice = seedShowNotice,
            HasSeenRecordingNotice = seedHasSeen
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
        var secureStorage = new WindowsDpapiSecureStorageService();
        var aiActaService = new OpenRouterActaService(new HttpClient(), secureStorage);

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
            settingsService);

        return (vm, settingsService, tempFile, transcriptionService);
    }

    [Fact]
    public async Task Case1_FirstRecording_ShowsRecordingNoticeDialog()
    {
        var (vm, _, tempFile, _) = CreateTestContext(seedHasSeen: false, seedShowNotice: true);
        try
        {
            // Initial state: ShowRecordingNotice is true
            Assert.True(vm.ShowRecordingNotice);
            Assert.False(vm.IsRecordingNoticeModalOpen);
            Assert.Equal(MeetingState.Idle, vm.CurrentState);

            // User clicks "Iniciar"
            await vm.StartMeetingCommand.ExecuteAsync(null);

            // Notice must be displayed, recording must NOT have started
            Assert.True(vm.IsRecordingNoticeModalOpen);
            Assert.False(vm.IsRecording);
            Assert.False(vm.IsStartingMeeting);
            Assert.Equal(MeetingState.Idle, vm.CurrentState);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Case2_FirstRecording_Cancel_DoesNotStartRecording()
    {
        var (vm, _, tempFile, _) = CreateTestContext(seedHasSeen: false, seedShowNotice: true);
        try
        {
            // Open modal via Iniciar
            await vm.StartMeetingCommand.ExecuteAsync(null);
            Assert.True(vm.IsRecordingNoticeModalOpen);

            // User clicks "Cancelar"
            vm.CancelRecordingNoticeCommand.Execute(null);

            // Modal closes, recording did not start, ShowRecordingNotice remains true
            Assert.False(vm.IsRecordingNoticeModalOpen);
            Assert.False(vm.IsRecording);
            Assert.True(vm.ShowRecordingNotice);
            Assert.Equal(MeetingState.Idle, vm.CurrentState);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Case3_FirstRecording_Confirm_DeactivatesNoticeCheckAndStartsRecording()
    {
        var (vm, settingsService, tempFile, _) = CreateTestContext(seedHasSeen: false, seedShowNotice: true);
        try
        {
            // Open modal
            await vm.StartMeetingCommand.ExecuteAsync(null);
            Assert.True(vm.IsRecordingNoticeModalOpen);

            // User clicks "Comenzar"
            await vm.ConfirmRecordingNoticeAndStartCommand.ExecuteAsync(null);

            // Modal must close, ShowRecordingNotice must be deactivated (false), recording must start
            Assert.False(vm.IsRecordingNoticeModalOpen);
            Assert.False(vm.ShowRecordingNotice);
            Assert.True(vm.IsRecording);
            Assert.Equal(MeetingState.Recording, vm.CurrentState);

            // Must have persisted to disk as deactivated (false)
            var loaded = settingsService.LoadSettings();
            Assert.False(loaded.ShowRecordingNotice);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Case4_SecondRecording_DoesNotShowNoticeAgain()
    {
        var (vm, _, tempFile, _) = CreateTestContext(seedHasSeen: true, seedShowNotice: false);
        try
        {
            Assert.False(vm.ShowRecordingNotice);

            // User clicks "Iniciar" for the second time
            await vm.StartMeetingCommand.ExecuteAsync(null);

            // Notice modal should NOT open; should directly start recording
            Assert.False(vm.IsRecordingNoticeModalOpen);
            Assert.True(vm.IsRecording);
            Assert.Equal(MeetingState.Recording, vm.CurrentState);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Case5_UserDisablesOption_DoesNotShowNotice()
    {
        var (vm, settingsService, tempFile, _) = CreateTestContext(seedHasSeen: false, seedShowNotice: true);
        try
        {
            // User unchecks "Mostrar recordatorio antes de iniciar una grabación"
            vm.ShowRecordingNotice = false;

            // Verified saved in settings
            var saved = settingsService.LoadSettings();
            Assert.False(saved.ShowRecordingNotice);

            // User clicks "Iniciar"
            await vm.StartMeetingCommand.ExecuteAsync(null);

            // Notice should NOT appear, recording begins directly
            Assert.False(vm.IsRecordingNoticeModalOpen);
            Assert.True(vm.IsRecording);
            Assert.Equal(MeetingState.Recording, vm.CurrentState);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Case6_UserReEnablesOption_ReEnablesReminderForNextRecording()
    {
        var (vm, settingsService, tempFile, _) = CreateTestContext(seedHasSeen: true, seedShowNotice: false);
        try
        {
            // User had it deactivated after previous confirmation
            Assert.False(vm.ShowRecordingNotice);

            // User re-enables the option in settings
            vm.ShowRecordingNotice = true;

            var saved = settingsService.LoadSettings();
            Assert.True(saved.ShowRecordingNotice);

            // Now clicks "Iniciar"
            await vm.StartMeetingCommand.ExecuteAsync(null);

            // Dialog must appear again
            Assert.True(vm.IsRecordingNoticeModalOpen);
            Assert.False(vm.IsRecording);

            // When user clicks Comenzar, it unchecks the option again
            await vm.ConfirmRecordingNoticeAndStartCommand.ExecuteAsync(null);
            Assert.False(vm.IsRecordingNoticeModalOpen);
            Assert.False(vm.ShowRecordingNotice);
            Assert.True(vm.IsRecording);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public void Case7_PersistenceAfterAppRestart_PreservesPreferences()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dictameeting_persist_test_{Guid.NewGuid():N}.json");
        try
        {
            var service1 = new UserSettingsService(tempFile);
            var initial = service1.LoadSettings();
            Assert.True(initial.ShowRecordingNotice);

            // Simulate user deactivating option
            initial.ShowRecordingNotice = false;
            service1.SaveSettings(initial);

            // Simulate new app session loading settings
            var service2 = new UserSettingsService(tempFile);
            var reloaded = service2.LoadSettings();

            Assert.False(reloaded.ShowRecordingNotice);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Case8_NormalRecordingWorkflow_IsNotBroken()
    {
        var (vm, _, tempFile, _) = CreateTestContext(seedHasSeen: false, seedShowNotice: true);
        try
        {
            // 1. Initial click -> notice dialog
            await vm.StartMeetingCommand.ExecuteAsync(null);
            Assert.True(vm.IsRecordingNoticeModalOpen);

            // 2. Click Comenzar -> recording starts, check becomes false
            await vm.ConfirmRecordingNoticeAndStartCommand.ExecuteAsync(null);
            Assert.True(vm.IsRecording);
            Assert.False(vm.ShowRecordingNotice);

            // 3. Stop meeting -> normal transition to Idle/Completed
            await vm.StopMeetingCommand.ExecuteAsync(null);
            Assert.False(vm.IsRecording);
            Assert.True(vm.IsIdle || vm.CurrentState == MeetingState.Completed);

            // 4. Start next meeting -> starts directly without dialog because check is false
            await vm.StartMeetingCommand.ExecuteAsync(null);
            Assert.False(vm.IsRecordingNoticeModalOpen);
            Assert.True(vm.IsRecording);

            await vm.StopMeetingCommand.ExecuteAsync(null);
            Assert.False(vm.IsRecording);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Case9_DuringActiveRecording_NoticeCannotBeShown()
    {
        var (vm, _, tempFile, _) = CreateTestContext(seedHasSeen: true, seedShowNotice: false);
        try
        {
            await vm.StartMeetingCommand.ExecuteAsync(null);
            Assert.True(vm.IsRecording);

            // Even if notice is re-enabled while recording is active
            vm.ShowRecordingNotice = true;

            // Attempt to trigger start meeting command while already recording
            await vm.StartMeetingCommand.ExecuteAsync(null);

            // Notice must NOT be shown because state is Recording, not Idle
            Assert.False(vm.IsRecordingNoticeModalOpen);
            Assert.True(vm.IsRecording);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public void Case10_NoUnnecessaryPersonalData_GeneratedInSettings()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dictameeting_privacy_data_{Guid.NewGuid():N}.json");
        try
        {
            var service = new UserSettingsService(tempFile);
            var settings = new UserSettings
            {
                ShowRecordingNotice = true
            };
            service.SaveSettings(settings);

            var json = File.ReadAllText(tempFile);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Must contain only the boolean configuration keys
            Assert.True(root.TryGetProperty("showRecordingNotice", out var showNoticeProp));
            Assert.Equal(JsonValueKind.True, showNoticeProp.ValueKind);

            // Must NOT contain personal identifiers, participants, timestamps, or recording logs in settings
            Assert.False(root.TryGetProperty("participants", out _));
            Assert.False(root.TryGetProperty("consentGivenBy", out _));
            Assert.False(root.TryGetProperty("userEmail", out _));
            Assert.False(root.TryGetProperty("ipAddress", out _));
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public void PrivacySettings_NavigationAndSummary_WorkCorrectly()
    {
        var (vm, _, tempFile, _) = CreateTestContext();
        try
        {
            // Initial modals closed
            Assert.False(vm.IsGlobalSettingsModalOpen);
            Assert.False(vm.IsPrivacySettingsModalOpen);

            // Open Global Settings
            vm.OpenGlobalSettingsCommand.Execute(null);
            Assert.True(vm.IsGlobalSettingsModalOpen);

            // Navigate to Privacy Settings from Global Settings
            vm.OpenPrivacySettingsFromSettingsCommand.Execute(null);
            Assert.False(vm.IsGlobalSettingsModalOpen);
            Assert.True(vm.IsPrivacySettingsModalOpen);

            // Close Privacy Settings -> returns to Global Settings
            vm.ClosePrivacySettingsModalCommand.Execute(null);
            Assert.False(vm.IsPrivacySettingsModalOpen);
            Assert.True(vm.IsGlobalSettingsModalOpen);

            // Provider summary reflects current AI provider
            Assert.Equal("OpenRouter", vm.PrivacyAiProviderSummary);

            vm.SelectedAiConfigProvider = "CustomServer";
            Assert.Equal("Servidor personalizado", vm.PrivacyAiProviderSummary);

            vm.SelectedAiConfigProvider = "OpenRouter";
            Assert.Equal("OpenRouter", vm.PrivacyAiProviderSummary);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }
}
