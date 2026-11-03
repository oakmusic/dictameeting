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

public class MeetingDeletionTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly DocumentExporter _exporter;
    private readonly LocalFileMeetingRepository _repository;

    public MeetingDeletionTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), $"dictameeting_del_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testBaseDir);
        _exporter = new DocumentExporter();
        _repository = new LocalFileMeetingRepository(_exporter, _testBaseDir);
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

    private Meeting CreateSampleMeeting(string id, string title, bool withTranscript = false, bool withAudio = false, int actaVersionCount = 0)
    {
        var m = new Meeting
        {
            Id = id,
            Title = title,
            Date = DateTime.Now.AddDays(-1),
            StartTime = DateTimeOffset.Now.AddHours(-2),
            EndTime = DateTimeOffset.Now.AddHours(-1),
            State = MeetingState.Completed,
            ProcessingStatus = ProcessingStatus.Completed
        };

        if (withTranscript)
        {
            m.Participants.Add(new Speaker { Id = "SPK_1", DisplayName = "Alice", ColorHex = "#0071E3" });
            m.Participants.Add(new Speaker { Id = "SPK_2", DisplayName = "Bob", ColorHex = "#34C759" });
            m.Transcript.Add(new TranscriptSegment
            {
                Id = "seg-1",
                SpeakerId = "SPK_1",
                SpeakerDisplayName = "Alice",
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromSeconds(10),
                Text = "Hola a todos, comenzamos la reunión."
            });
            m.Transcript.Add(new TranscriptSegment
            {
                Id = "seg-2",
                SpeakerId = "SPK_2",
                SpeakerDisplayName = "Bob",
                StartTime = TimeSpan.FromSeconds(11),
                EndTime = TimeSpan.FromSeconds(20),
                Text = "De acuerdo, revisemos los puntos."
            });
            m.SpeakerCentroids["SPK_1"] = new float[] { 0.1f, 0.2f, 0.3f };
            m.SpeakerCentroids["SPK_2"] = new float[] { 0.4f, 0.5f, 0.6f };
        }

        var dirPath = _repository.GetMeetingDirectoryPath(m);
        Directory.CreateDirectory(dirPath);

        if (withAudio)
        {
            var audioPath = _repository.GetAudioFilePath(m);
            File.WriteAllText(audioPath, "DUMMY AUDIO CONTENT");
            m.AudioFilePath = audioPath;
        }

        for (int i = 1; i <= actaVersionCount; i++)
        {
            var ver = new MeetingActaVersion
            {
                Id = $"ver-{i}-{Guid.NewGuid():N}",
                MeetingId = m.Id,
                FileName = $"acta_{i}.md",
                Language = LanguageMode.Spanish,
                DetailLevel = (ActaDetailLevel)Math.Min(i, 3),
                CreatedAt = DateTimeOffset.Now.AddMinutes(i * 10),
                MarkdownContent = $"# Acta versión {i}\nContenido detallado {i}."
            };
            m.ActaVersions.Add(ver);
        }

        if (m.ActaVersions.Count > 0)
        {
            var last = m.ActaVersions.Last();
            m.SelectedActaVersionId = last.Id;
            m.ActaMarkdown = last.MarkdownContent;
        }

        return m;
    }

    #region 4.1. Pruebas de eliminación de reuniones en Repositorio

    [Fact]
    public async Task DeleteMeeting_WithoutTranscript_DeletesDirectoryAndFiles_ReturnsTrue()
    {
        var meeting = CreateSampleMeeting("meet-no-trans-01", "Reunión Sin Transcripción");
        await _repository.SaveMeetingAsync(meeting);

        var dirPath = _repository.GetMeetingDirectoryPath(meeting);
        Assert.True(Directory.Exists(dirPath));

        var deleted = await _repository.DeleteMeetingAsync(meeting.Id);

        Assert.True(deleted);
        Assert.False(Directory.Exists(dirPath));

        var fetched = await _repository.GetMeetingAsync(meeting.Id);
        Assert.Null(fetched);
    }

    [Fact]
    public async Task DeleteMeeting_WithTranscript_DeletesAllAssociatedFiles()
    {
        var meeting = CreateSampleMeeting("meet-trans-02", "Reunión Con Transcripción", withTranscript: true);
        await _repository.SaveMeetingAsync(meeting);

        var dirPath = _repository.GetMeetingDirectoryPath(meeting);
        Assert.True(Directory.Exists(dirPath));
        Assert.True(Directory.GetFiles(dirPath, "*_transcript.md").Length > 0);

        var deleted = await _repository.DeleteMeetingAsync(meeting.Id);

        Assert.True(deleted);
        Assert.False(Directory.Exists(dirPath));

        var allMeetings = await _repository.GetAllMeetingsAsync();
        Assert.DoesNotContain(allMeetings, m => m.Id == meeting.Id);
    }

    [Fact]
    public async Task DeleteMeeting_WithDiarizationAndAudio_DeletesAllFiles()
    {
        var meeting = CreateSampleMeeting("meet-diar-audio-03", "Reunión Diarizada", withTranscript: true, withAudio: true);
        await _repository.SaveMeetingAsync(meeting);

        var dirPath = _repository.GetMeetingDirectoryPath(meeting);
        Assert.True(Directory.Exists(dirPath));
        Assert.True(File.Exists(meeting.AudioFilePath));

        var deleted = await _repository.DeleteMeetingAsync(meeting.Id);

        Assert.True(deleted);
        Assert.False(Directory.Exists(dirPath));
        Assert.False(File.Exists(meeting.AudioFilePath));
    }

    [Fact]
    public async Task DeleteMeeting_WithMultipleActaVersions_DeletesAllVersionsAndSubdirectory()
    {
        var meeting = CreateSampleMeeting("meet-actas-04", "Reunión Con Actas", withTranscript: true, actaVersionCount: 3);
        await _repository.SaveMeetingAsync(meeting);

        var dirPath = _repository.GetMeetingDirectoryPath(meeting);
        var actasDir = Path.Combine(dirPath, "actas");
        Assert.True(Directory.Exists(actasDir));
        Assert.True(File.Exists(Path.Combine(actasDir, "index.json")));

        var deleted = await _repository.DeleteMeetingAsync(meeting.Id);

        Assert.True(deleted);
        Assert.False(Directory.Exists(dirPath));
        Assert.False(Directory.Exists(actasDir));
    }

    [Fact]
    public async Task DeleteMeeting_PreservesOtherMeetings()
    {
        var meeting1 = CreateSampleMeeting("meet-keep-01", "Primera Reunión", withTranscript: true);
        var meeting2 = CreateSampleMeeting("meet-del-02", "Segunda Reunión Para Borrar", withTranscript: true);
        var meeting3 = CreateSampleMeeting("meet-keep-03", "Tercera Reunión", withTranscript: true);

        await _repository.SaveMeetingAsync(meeting1);
        await _repository.SaveMeetingAsync(meeting2);
        await _repository.SaveMeetingAsync(meeting3);

        var dir1 = _repository.GetMeetingDirectoryPath(meeting1);
        var dir2 = _repository.GetMeetingDirectoryPath(meeting2);
        var dir3 = _repository.GetMeetingDirectoryPath(meeting3);

        var deleted = await _repository.DeleteMeetingAsync(meeting2.Id);

        Assert.True(deleted);
        Assert.True(Directory.Exists(dir1));
        Assert.False(Directory.Exists(dir2));
        Assert.True(Directory.Exists(dir3));

        var all = await _repository.GetAllMeetingsAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, m => m.Id == meeting1.Id);
        Assert.Contains(all, m => m.Id == meeting3.Id);
        Assert.DoesNotContain(all, m => m.Id == meeting2.Id);
    }

    [Fact]
    public async Task DeleteMeeting_NonExistent_ReturnsFalse()
    {
        var result = await _repository.DeleteMeetingAsync("non-existent-id-9999");
        Assert.False(result);
    }

    [Fact]
    public async Task DeleteMeeting_EmptyOrWhitespaceId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.DeleteMeetingAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.DeleteMeetingAsync("   "));
    }

    #endregion

    #region 4.2. Pruebas de eliminación de versiones de actas en Repositorio

    [Fact]
    public async Task DeleteActaVersion_MultipleVersions_DeletesOnlyTargetVersion_PreservesOtherVersions()
    {
        var meeting = CreateSampleMeeting("meet-multi-actas-01", "Reunión Multi Actas", withTranscript: true, actaVersionCount: 3);
        await _repository.SaveMeetingAsync(meeting);

        var verToDelete = meeting.ActaVersions[1]; // segunda versión
        var verToKeep1 = meeting.ActaVersions[0];
        var verToKeep3 = meeting.ActaVersions[2];

        var deleted = await _repository.DeleteActaVersionAsync(meeting.Id, verToDelete.Id);

        Assert.True(deleted);

        var reloaded = await _repository.GetMeetingAsync(meeting.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded.ActaVersions.Count);
        Assert.Contains(reloaded.ActaVersions, v => v.Id == verToKeep1.Id);
        Assert.Contains(reloaded.ActaVersions, v => v.Id == verToKeep3.Id);
        Assert.DoesNotContain(reloaded.ActaVersions, v => v.Id == verToDelete.Id);

        // Archivo en actas/ debe haber desaparecido
        var dirPath = _repository.GetMeetingDirectoryPath(meeting);
        var deletedFile = Path.Combine(dirPath, "actas", verToDelete.FileName);
        Assert.False(File.Exists(deletedFile));
    }

    [Fact]
    public async Task DeleteActaVersion_CurrentlySelectedVersion_SwitchesActiveVersionToMostRecentRemaining()
    {
        var meeting = CreateSampleMeeting("meet-sel-acta-02", "Reunión Selección", withTranscript: true, actaVersionCount: 3);
        await _repository.SaveMeetingAsync(meeting);

        // La activa es la tercera (más reciente)
        var activeVer = meeting.ActaVersions.OrderByDescending(v => v.CreatedAt).First();
        var remainingExpectedActive = meeting.ActaVersions.Where(v => v.Id != activeVer.Id).OrderByDescending(v => v.CreatedAt).First();

        var deleted = await _repository.DeleteActaVersionAsync(meeting.Id, activeVer.Id);

        Assert.True(deleted);

        var reloaded = await _repository.GetMeetingAsync(meeting.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(remainingExpectedActive.Id, reloaded.SelectedActaVersionId);
        Assert.Equal(remainingExpectedActive.MarkdownContent, reloaded.ActaMarkdown);
    }

    [Fact]
    public async Task DeleteActaVersion_NonSelectedVersion_PreservesSelectedVersion()
    {
        var meeting = CreateSampleMeeting("meet-nonsel-acta-03", "Reunión No Seleccionada", withTranscript: true, actaVersionCount: 3);
        await _repository.SaveMeetingAsync(meeting);

        var oldestVer = meeting.ActaVersions.OrderBy(v => v.CreatedAt).First();
        var currentActiveVer = meeting.GetActiveActaVersion()!;
        Assert.NotEqual(oldestVer.Id, currentActiveVer.Id);

        var deleted = await _repository.DeleteActaVersionAsync(meeting.Id, oldestVer.Id);

        Assert.True(deleted);

        var reloaded = await _repository.GetMeetingAsync(meeting.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(currentActiveVer.Id, reloaded.SelectedActaVersionId);
        Assert.Equal(currentActiveVer.MarkdownContent, reloaded.ActaMarkdown);
    }

    [Fact]
    public async Task DeleteActaVersion_LastVersion_ClearsActaMarkdownAndIndex_PreservesMeetingAndTranscriptAndAudio()
    {
        var meeting = CreateSampleMeeting("meet-last-acta-04", "Reunión Última Acta", withTranscript: true, withAudio: true, actaVersionCount: 1);
        await _repository.SaveMeetingAsync(meeting);

        var singleVer = meeting.ActaVersions.Single();

        var deleted = await _repository.DeleteActaVersionAsync(meeting.Id, singleVer.Id);

        Assert.True(deleted);

        var reloaded = await _repository.GetMeetingAsync(meeting.Id);
        Assert.NotNull(reloaded);
        Assert.Empty(reloaded.ActaVersions);
        Assert.Null(reloaded.SelectedActaVersionId);
        Assert.True(string.IsNullOrWhiteSpace(reloaded.ActaMarkdown));

        // Transcripción, audio y diarización deben seguir intactos
        Assert.Equal(2, reloaded.Transcript.Count);
        Assert.Equal(2, reloaded.Participants.Count);
        Assert.True(reloaded.AudioFileExists());
        Assert.Equal(2, reloaded.SpeakerCentroids.Count);

        // El archivo raíz de acta ya no debe existir
        var dirPath = _repository.GetMeetingDirectoryPath(meeting);
        var baseName = _repository.GetMeetingFileBaseName(meeting);
        Assert.False(File.Exists(Path.Combine(dirPath, $"{baseName}_acta.md")));
    }

    [Fact]
    public async Task DeleteActaVersion_SameLanguageAndDetail_DifferentiatesByIdAndDeletesCorrectOne()
    {
        var meeting = CreateSampleMeeting("meet-same-lang-05", "Reunión Mismo Idioma", withTranscript: true);
        var ver1 = new MeetingActaVersion
        {
            Id = "ver-same-1",
            MeetingId = meeting.Id,
            FileName = "acta_ver1.md",
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            CreatedAt = DateTimeOffset.Now.AddMinutes(-20),
            MarkdownContent = "Contenido Versión 1"
        };
        var ver2 = new MeetingActaVersion
        {
            Id = "ver-same-2",
            MeetingId = meeting.Id,
            FileName = "acta_ver2.md",
            Language = LanguageMode.Spanish,
            DetailLevel = ActaDetailLevel.Normal,
            CreatedAt = DateTimeOffset.Now.AddMinutes(-10),
            MarkdownContent = "Contenido Versión 2"
        };
        meeting.ActaVersions.Add(ver1);
        meeting.ActaVersions.Add(ver2);
        meeting.SelectedActaVersionId = ver2.Id;
        meeting.ActaMarkdown = ver2.MarkdownContent;

        await _repository.SaveMeetingAsync(meeting);

        // Borrar ver1
        var deleted = await _repository.DeleteActaVersionAsync(meeting.Id, ver1.Id);

        Assert.True(deleted);

        var reloaded = await _repository.GetMeetingAsync(meeting.Id);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded.ActaVersions);
        Assert.Equal("ver-same-2", reloaded.ActaVersions[0].Id);
        Assert.Equal("Contenido Versión 2", reloaded.ActaMarkdown);
    }

    #endregion

    #region 4.3. Pruebas de ViewModel para Eliminación de Reuniones y Actas

    private (MainViewModel vm, LocalFileMeetingRepository repo, MeetingService meetingService) CreateViewModelFixture()
    {
        var settingsFile = Path.Combine(_testBaseDir, $"settings_{Guid.NewGuid():N}.json");
        var userSettingsService = new UserSettingsService(settingsFile);
        var meetingService = new MeetingService(_repository);
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
            _exporter,
            _repository,
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

        return (vm, _repository, meetingService);
    }

    [Fact]
    public void PromptDeleteMeeting_SetsTargetAndOpensModal()
    {
        var (vm, repo, _) = CreateViewModelFixture();
        var meeting = CreateSampleMeeting("meet-vm-prompt-01", "Reunión Test Prompt");
        var itemVm = new MeetingHistoryItemViewModel(meeting);

        vm.PromptDeleteMeeting(itemVm);

        Assert.True(vm.IsDeleteMeetingModalOpen);
        Assert.Equal(itemVm, vm.MeetingToDelete);
        Assert.False(vm.IsDeletingMeeting);
        Assert.Empty(vm.DeleteMeetingErrorMessage);
    }

    [Fact]
    public void CancelDeleteMeeting_ClosesModalWithoutChanges()
    {
        var (vm, repo, _) = CreateViewModelFixture();
        var meeting = CreateSampleMeeting("meet-vm-cancel-02", "Reunión Test Cancel");
        var itemVm = new MeetingHistoryItemViewModel(meeting);

        vm.PromptDeleteMeeting(itemVm);
        Assert.True(vm.IsDeleteMeetingModalOpen);

        vm.CancelDeleteMeeting();

        Assert.False(vm.IsDeleteMeetingModalOpen);
        Assert.Null(vm.MeetingToDelete);
    }

    [Fact]
    public async Task ConfirmDeleteMeeting_DeletesFromDiskAndRemovesFromSavedMeetings()
    {
        var (vm, repo, _) = CreateViewModelFixture();
        var meeting1 = CreateSampleMeeting("meet-vm-conf-01", "Reunión 1");
        var meeting2 = CreateSampleMeeting("meet-vm-conf-02", "Reunión 2 Para Borrar");
        await repo.SaveMeetingAsync(meeting1);
        await repo.SaveMeetingAsync(meeting2);

        await vm.LoadSavedMeetingsAsync();
        Assert.Equal(2, vm.SavedMeetings.Count);

        var itemToDelete = vm.SavedMeetings.First(m => m.Id == meeting2.Id);
        vm.PromptDeleteMeeting(itemToDelete);

        await vm.ConfirmDeleteMeetingAsync();

        Assert.False(vm.IsDeleteMeetingModalOpen);
        Assert.Null(vm.MeetingToDelete);
        Assert.Single(vm.SavedMeetings);
        Assert.Equal("meet-vm-conf-01", vm.SavedMeetings[0].Id);

        // Comprobar que en disco ya no existe
        var reloaded = await repo.GetMeetingAsync(meeting2.Id);
        Assert.Null(reloaded);
    }

    [Fact]
    public async Task ConfirmDeleteMeeting_WhenMeetingIsOpenInWorkspace_ResetsWorkspace()
    {
        var (vm, repo, meetingService) = CreateViewModelFixture();
        var meeting = CreateSampleMeeting("meet-vm-open-03", "Reunión Abierta", withTranscript: true);
        await repo.SaveMeetingAsync(meeting);

        await vm.LoadSavedMeetingsAsync();
        vm.LoadMeetingIntoWorkspace(meeting);

        Assert.Equal(meeting.Id, meetingService.CurrentMeeting?.Id);
        Assert.Equal("Reunión Abierta", vm.MeetingTitle);
        Assert.NotEmpty(vm.TranscriptSegments);

        var itemToDelete = vm.SavedMeetings.First(m => m.Id == meeting.Id);
        vm.PromptDeleteMeeting(itemToDelete);

        await vm.ConfirmDeleteMeetingAsync();

        // El workspace debe haberse reseteado a nueva sesión limpia
        Assert.Null(meetingService.CurrentMeeting);
        Assert.Empty(vm.TranscriptSegments);
        Assert.Empty(vm.Participants);
        Assert.Equal(MeetingState.Idle, vm.CurrentState);
    }

    [Fact]
    public async Task ConfirmDeleteActaVersion_RemovesFromCollection_AndSwitchesSelection()
    {
        var (vm, repo, _) = CreateViewModelFixture();
        var meeting = CreateSampleMeeting("meet-vm-acta-04", "Reunión Versiones VM", withTranscript: true, actaVersionCount: 2);
        await repo.SaveMeetingAsync(meeting);

        vm.LoadMeetingIntoWorkspace(meeting);
        Assert.Equal(2, vm.AvailableActaVersions.Count);

        var activeVersion = vm.SelectedActaVersion;
        Assert.NotNull(activeVersion);

        // Solicitar borrado de la versión activa
        vm.PromptDeleteActaVersion(activeVersion);
        Assert.True(vm.IsDeleteActaVersionModalOpen);
        Assert.Equal(activeVersion, vm.ActaVersionToDelete);

        await vm.ConfirmDeleteActaVersionAsync();

        Assert.False(vm.IsDeleteActaVersionModalOpen);
        Assert.Null(vm.ActaVersionToDelete);
        Assert.Single(vm.AvailableActaVersions);
        Assert.NotNull(vm.SelectedActaVersion);
        Assert.NotEqual(activeVersion.Id, vm.SelectedActaVersion.Id);
        Assert.True(vm.HasGeneratedActa);
    }

    [Fact]
    public async Task ConfirmDeleteActaVersion_LastVersion_SetsHasGeneratedActaFalse()
    {
        var (vm, repo, _) = CreateViewModelFixture();
        var meeting = CreateSampleMeeting("meet-vm-last-05", "Reunión Última Versión VM", withTranscript: true, actaVersionCount: 1);
        await repo.SaveMeetingAsync(meeting);

        vm.LoadMeetingIntoWorkspace(meeting);
        Assert.Single(vm.AvailableActaVersions);
        Assert.True(vm.HasGeneratedActa);

        var singleVer = vm.AvailableActaVersions[0];
        vm.PromptDeleteActaVersion(singleVer);

        await vm.ConfirmDeleteActaVersionAsync();

        Assert.False(vm.IsDeleteActaVersionModalOpen);
        Assert.Empty(vm.AvailableActaVersions);
        Assert.Null(vm.SelectedActaVersion);
        Assert.False(vm.HasGeneratedActa);
        Assert.Empty(vm.GeneratedActaMarkdown);
    }

    #endregion
}

