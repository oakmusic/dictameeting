using System.Collections.ObjectModel;
using DictaMeeting.AI.Services;
using DictaMeeting.App.ViewModels;
using DictaMeeting.Audio.Services;
using DictaMeeting.Diarization.Pipeline;
using DictaMeeting.Diarization.Services;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Services;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class MeetingWorkspaceNavigationTests
{
    private class InMemoryRepo : IMeetingRepository
    {
        public List<Meeting> Meetings { get; } = new();

        public Task SaveMeetingAsync(Meeting meeting, CancellationToken cancellationToken = default)
        {
            var existing = Meetings.FirstOrDefault(m => m.Id == meeting.Id);
            if (existing != null)
            {
                Meetings.Remove(existing);
            }
            Meetings.Add(meeting);
            return Task.CompletedTask;
        }

        public Task SaveMeetingAsync(Meeting meeting, MeetingSessionParameters? parameters, CancellationToken cancellationToken = default)
            => SaveMeetingAsync(meeting, cancellationToken);

        public Task<Meeting?> GetMeetingAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(Meetings.FirstOrDefault(m => m.Id == id));

        public Task<IReadOnlyList<Meeting>> GetAllMeetingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Meeting>>(Meetings.ToList());

        public string GetMeetingDirectoryPath(Meeting meeting) => Path.Combine(Path.GetTempPath(), meeting.Id);
        public string GetMeetingFileBaseName(Meeting meeting) => $"{meeting.Title}_{meeting.Date:yyyy-MM-dd_HH-mm}";
        public string GetAudioFilePath(Meeting meeting) => Path.Combine(GetMeetingDirectoryPath(meeting), "audio.wav");
    }

    private (MainViewModel vm, InMemoryRepo repo, MeetingService meetingService) CreateTestFixture()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"vm_nav_test_{Guid.NewGuid():N}.json");
        var userSettingsService = new UserSettingsService(tempFile);
        var repo = new InMemoryRepo();
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
        var aiActaService = new OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

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

        return (vm, repo, meetingService);
    }

    [Fact]
    public void MeetingService_OpenMeeting_SetsCurrentMeetingAndState()
    {
        var repo = new InMemoryRepo();
        var service = new MeetingService(repo);

        var meeting = new Meeting
        {
            Id = "meet-101",
            Title = "Reunión Estratégica",
            State = MeetingState.Completed,
            Date = DateTime.Now.AddDays(-2)
        };

        bool eventFired = false;
        service.StateChanged += (_, s) =>
        {
            if (s == MeetingState.Completed) eventFired = true;
        };

        service.OpenMeeting(meeting);

        Assert.Equal(meeting, service.CurrentMeeting);
        Assert.Equal(MeetingState.Completed, service.CurrentState);
        Assert.True(eventFired);
    }

    [Fact]
    public async Task ExploringHistory_AutoLoadsMeetingIntoWorkspaceWhileStayingInHistoryTab()
    {
        var (vm, repo, meetingService) = CreateTestFixture();

        var m1 = new Meeting
        {
            Id = "m1",
            Title = "Reunión Alfa",
            State = MeetingState.Completed,
            Transcript = new List<TranscriptSegment>
            {
                new() { SpeakerId = "SPEAKER_00", Text = "Texto de la reunión Alfa" }
            }
        };
        var m2 = new Meeting
        {
            Id = "m2",
            Title = "Reunión Beta",
            State = MeetingState.Completed,
            Transcript = new List<TranscriptSegment>
            {
                new() { SpeakerId = "SPEAKER_00", Text = "Texto de la reunión Beta" }
            }
        };
        await repo.SaveMeetingAsync(m1);
        await repo.SaveMeetingAsync(m2);

        // El usuario navega a la pestaña Historial (Tab 1)
        vm.SelectedNavigationTab = 1;
        await vm.LoadSavedMeetingsAsync();

        // Selecciona Reunión Alfa
        vm.SelectedHistoricalMeeting = vm.SavedMeetings.FirstOrDefault(x => x.Id == "m1");
        Assert.NotNull(vm.SelectedHistoricalMeeting);

        // Verificar que NO cambia la pestaña, pero SÍ carga automáticamente la reunión en el workspace
        Assert.Equal(1, vm.SelectedNavigationTab);
        Assert.Equal("m1", meetingService.CurrentMeeting?.Id);
        Assert.Equal("Reunión Alfa", vm.MeetingTitle);
        Assert.Single(vm.TranscriptSegments);
        Assert.Equal("Texto de la reunión Alfa", vm.TranscriptSegments[0].Text);

        // Selecciona Reunión Beta
        vm.SelectedHistoricalMeeting = vm.SavedMeetings.FirstOrDefault(x => x.Id == "m2");
        Assert.NotNull(vm.SelectedHistoricalMeeting);

        // Sigue en la pestaña Historial y el workspace se sincroniza automáticamente con la reunión Beta
        Assert.Equal(1, vm.SelectedNavigationTab);
        Assert.Equal("m2", meetingService.CurrentMeeting?.Id);
        Assert.Equal("Reunión Beta", vm.MeetingTitle);
        Assert.Single(vm.TranscriptSegments);
        Assert.Equal("Texto de la reunión Beta", vm.TranscriptSegments[0].Text);
    }

    [Fact]
    public async Task OpenMeetingCommand_LoadsFullMeetingIntoWorkspaceAndSwitchesTab()
    {
        var (vm, repo, _) = CreateTestFixture();

        var start = DateTimeOffset.Now.AddHours(-1);
        var end = start.AddMinutes(45);

        var meeting = new Meeting
        {
            Id = "meet-full",
            Title = "Proyecto Horizonte",
            State = MeetingState.Completed,
            Date = new DateTime(2026, 9, 25, 10, 0, 0),
            StartTime = start,
            EndTime = end,
            Transcript = new List<TranscriptSegment>
            {
                new() { SpeakerId = "SPEAKER_00", Text = "Primer punto del día.", StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromSeconds(5) },
                new() { SpeakerId = "SPEAKER_01", Text = "De acuerdo con el presupuesto.", StartTime = TimeSpan.FromSeconds(6), EndTime = TimeSpan.FromSeconds(12) }
            },
            Participants = new List<Speaker>
            {
                new() { Id = "SPEAKER_00", DisplayName = "Carlos" },
                new() { Id = "SPEAKER_01", DisplayName = "Elena" }
            },
            SummaryText = "Acuerdo sobre el presupuesto.",
            ActaMarkdown = "# Acta Oficial\nSe aprueba el presupuesto."
        };

        await repo.SaveMeetingAsync(meeting);
        await vm.LoadSavedMeetingsAsync();

        var histItem = vm.SavedMeetings.First(x => x.Id == "meet-full");
        vm.SelectedHistoricalMeeting = histItem;
        vm.SelectedNavigationTab = 1;

        // Act: El usuario pulsa [ Ver reunión ]
        await vm.OpenMeetingCommand.ExecuteAsync(histItem);

        // Assert: Pestaña cambia a Reunión (0)
        Assert.Equal(0, vm.SelectedNavigationTab);

        // El workspace contiene la reunión abierta
        Assert.Equal("Proyecto Horizonte", vm.MeetingTitle);
        Assert.Equal("meet-full", vm.CurrentOpenMeeting?.Id);
        Assert.True(vm.IsOpenMeetingCompleted);

        // Segmentos completos cargados
        Assert.Equal(2, vm.TranscriptSegments.Count);
        Assert.Equal("Primer punto del día.", vm.TranscriptSegments[0].Text);
        Assert.Equal("Carlos", vm.TranscriptSegments[0].SpeakerDisplayName);
        Assert.Equal("De acuerdo con el presupuesto.", vm.TranscriptSegments[1].Text);
        Assert.Equal("Elena", vm.TranscriptSegments[1].SpeakerDisplayName);

        // Participantes cargados
        Assert.Equal(2, vm.Participants.Count);
        Assert.Contains(vm.Participants, p => p.DisplayName == "Carlos");
        Assert.Contains(vm.Participants, p => p.DisplayName == "Elena");

        // Resumen cargado
        Assert.Single(vm.LiveSummaryCards);
        Assert.Equal("Acuerdo sobre el presupuesto.", vm.LiveSummaryCards[0].Text);

        // Acta cargada
        Assert.Equal("# Acta Oficial\nSe aprueba el presupuesto.", vm.GeneratedActaMarkdown);
    }

    [Fact]
    public async Task RenameMeetingTitle_OnCompletedMeeting_PersistsToRepository()
    {
        var (vm, repo, _) = CreateTestFixture();

        var meeting = new Meeting
        {
            Id = "meet-rename",
            Title = "Título Antiguo",
            State = MeetingState.Completed
        };
        await repo.SaveMeetingAsync(meeting);
        await vm.LoadSavedMeetingsAsync();

        var histItem = vm.SavedMeetings.First(x => x.Id == "meet-rename");
        await vm.OpenMeetingCommand.ExecuteAsync(histItem);

        // Act: Editar título
        vm.StartEditMeetingTitleCommand.Execute(null);
        Assert.True(vm.IsEditingMeetingTitle);

        vm.MeetingTitle = "Título Nuevo y Renovado";
        vm.FinishEditMeetingTitleCommand.Execute(null);

        // Assert
        Assert.False(vm.IsEditingMeetingTitle);
        Assert.Equal("Título Nuevo y Renovado", vm.MeetingTitle);

        var persisted = await repo.GetMeetingAsync("meet-rename");
        Assert.NotNull(persisted);
        Assert.Equal("Título Nuevo y Renovado", persisted.Title);
    }

    [Fact]
    public void CreateNewMeetingCommand_CreatesAndOpensFreshMeetingImmediately()
    {
        var (vm, repo, meetingService) = CreateTestFixture();

        vm.SelectedNavigationTab = 1;

        // Act: Pulsar "+ Nueva Reunión"
        vm.CreateNewMeetingCommand.Execute(null);

        // Assert: Abre automáticamente en pestaña 0
        Assert.Equal(0, vm.SelectedNavigationTab);
        Assert.Null(meetingService.CurrentMeeting);
        Assert.StartsWith("Reunión ", vm.MeetingTitle);
        Assert.True(vm.IsPreparedNewMeeting);
        Assert.False(vm.IsOpenMeetingCompleted);
        Assert.Empty(vm.TranscriptSegments);
    }

    [Fact]
    public void MeetingHistoryItemViewModel_PreviewProperties_AreAccurate()
    {
        var start = DateTimeOffset.Now.AddMinutes(-30);
        var end = start.AddMinutes(22);

        var meeting = new Meeting
        {
            Id = "preview-test",
            Title = "Reunión de Prueba",
            StartTime = start,
            EndTime = end,
            Transcript = new List<TranscriptSegment>
            {
                new() { SpeakerId = "SPEAKER_00", Text = "Primera frase del preview.", StartTime = TimeSpan.FromSeconds(2), EndTime = TimeSpan.FromSeconds(5) },
                new() { SpeakerId = "SPEAKER_01", Text = "Segunda frase.", StartTime = TimeSpan.FromSeconds(6), EndTime = TimeSpan.FromSeconds(8) }
            },
            Participants = new List<Speaker>
            {
                new() { Id = "SPEAKER_00", DisplayName = "Aritz" },
                new() { Id = "SPEAKER_01", DisplayName = "Iker" }
            },
            SummaryText = "Resumen de prueba.",
            ActaMarkdown = "Acta generada."
        };

        var vm = new MeetingHistoryItemViewModel(meeting);

        Assert.Equal("Reunión de Prueba", vm.Title);
        Assert.True(vm.HasSummary);
        Assert.Equal("Resumen de prueba.", vm.SummaryPreview);
        Assert.True(vm.HasActa);
        Assert.Equal(DictaMeeting.App.Services.LocalizationManager.Instance["History_Acta_Status_Done"], vm.ActaStatusText);
        Assert.Equal("Aritz, Iker", vm.ParticipantsPreviewText);
        Assert.Equal(2, vm.PreviewSegments.Count);
        Assert.Contains("Primera frase del preview", vm.TranscriptPreview);
    }
}
