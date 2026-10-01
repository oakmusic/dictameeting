using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.AI.Services;
using DictaMeeting.App.ViewModels;
using DictaMeeting.Audio.Events;
using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Models;
using DictaMeeting.Audio.Services;
using DictaMeeting.Diarization.Interfaces;
using DictaMeeting.Diarization.Models;
using DictaMeeting.Diarization.Pipeline;
using DictaMeeting.Diarization.Services;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Services;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class AudioImportTests : IDisposable
{
    private readonly string _testTempDir;

    public AudioImportTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), $"DictaMeeting_AudioImportTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testTempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }
        catch { }
    }

    private class TestMeetingRepository : IMeetingRepository
    {
        private readonly string _baseDir;
        public List<Meeting> SavedMeetings { get; } = new();

        public TestMeetingRepository(string baseDir)
        {
            _baseDir = baseDir;
        }

        public Task SaveMeetingAsync(Meeting meeting, CancellationToken cancellationToken = default)
        {
            SavedMeetings.RemoveAll(m => m.Id == meeting.Id);
            SavedMeetings.Add(meeting);
            return Task.CompletedTask;
        }

        public Task SaveMeetingAsync(Meeting meeting, MeetingSessionParameters? parameters, CancellationToken cancellationToken = default)
        {
            SavedMeetings.RemoveAll(m => m.Id == meeting.Id);
            SavedMeetings.Add(meeting);
            return Task.CompletedTask;
        }

        public Task<Meeting?> GetMeetingAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(SavedMeetings.FirstOrDefault(m => m.Id == id));

        public Task<IReadOnlyList<Meeting>> GetAllMeetingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Meeting>>(SavedMeetings);

        public string GetMeetingDirectoryPath(Meeting meeting)
        {
            var dir = Path.Combine(_baseDir, meeting.Id);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public string GetMeetingFileBaseName(Meeting meeting) => $"{meeting.Title}_{meeting.Date:yyyy-MM-dd_HH-mm}";

        public string GetAudioFilePath(Meeting meeting) => GetAudioFilePath(meeting, ".mp3");

        public string GetAudioFilePath(Meeting meeting, string? extension)
        {
            var dir = GetMeetingDirectoryPath(meeting);
            var ext = string.IsNullOrWhiteSpace(extension) ? ".mp3" : extension;
            return Path.Combine(dir, $"{meeting.Title}_audio{ext}");
        }
    }

    private class MockModelManager : IModelManager
    {
        public string ModelsDirectory { get; private set; } = "C:\\test\\models";
        public void SetModelsDirectory(string path) => ModelsDirectory = path;

        public Task<HardwareCapabilities> DetectHardwareAsync()
        {
            return Task.FromResult(new HardwareCapabilities
            {
                CpuLogicalCores = 8,
                TotalRamGb = 16.0,
                RecommendedModel = ModelSize.Qwen3_06B,
                RecommendedLiveModel = ModelSize.Qwen3_06B,
                RecommendedFinalModel = ModelSize.Qwen3_06B
            });
        }

        public IReadOnlyList<TranscriptionModelInfo> GetAvailableModels() => new List<TranscriptionModelInfo>
        {
            new() { Size = ModelSize.Qwen3_06B, Name = "Qwen3 0.6B", IsDownloaded = true },
            new() { Size = ModelSize.Tiny, Name = "Tiny", IsDownloaded = true }
        };

        public Task<string> EnsureModelDownloadedAsync(ModelSize size, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult("dummy_path");

        public bool IsDiarizationModelDownloaded() => true;
        public Task<string> EnsureDiarizationModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult("dummy_diarization_path");

        public bool DeleteModel(ModelSize size) => true;
    }

    private class MockProcessingService : IMeetingProcessingService
    {
        public bool IsProcessing { get; set; }
        public string? CurrentMeetingId { get; set; }
        public double CurrentProgress { get; set; }
        public string CurrentStatusText { get; set; } = string.Empty;
        public Task? CurrentProcessingTask { get; set; }

        public event EventHandler<MeetingProcessingProgressEventArgs>? ProgressChanged;
        public event EventHandler<MeetingProcessingCompletedEventArgs>? ProcessingCompleted;

        public Task EnqueueOrProcessAsync(string meetingId, string? targetModel = null, CancellationToken cancellationToken = default)
        {
            IsProcessing = true;
            CurrentMeetingId = meetingId;
            return Task.CompletedTask;
        }

        public Task CancelProcessingAsync(string meetingId) => Task.CompletedTask;
        public Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // =========================================================================
    // 1. TESTS DE FORMATOS ADMITIDOS
    // =========================================================================

    [Theory]
    [InlineData(".mp3", true)]
    [InlineData(".wav", true)]
    [InlineData(".m4a", true)]
    [InlineData(".aac", true)]
    [InlineData(".wma", true)]
    [InlineData(".flac", true)]
    [InlineData(".ogg", true)]
    [InlineData(".mp4", true)]
    [InlineData("audio.MP3", true)]
    [InlineData("recording.WAV", true)]
    [InlineData("Reunion_PRISMA2.m4a", true)]
    [InlineData(".txt", false)]
    [InlineData("documento.pdf", false)]
    [InlineData("app.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SupportedAudioFormats_ValidatesExtensionsCorrectly(string? pathOrExt, bool expected)
    {
        var result = SupportedAudioFormats.IsSupported(pathOrExt);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SupportedAudioFormats_DisplayList_ContainsAllExpectedFormats()
    {
        var display = SupportedAudioFormats.DisplayList;
        Assert.Contains("MP3", display);
        Assert.Contains("WAV", display);
        Assert.Contains("M4A", display);
        Assert.Contains("AAC", display);
        Assert.Contains("WMA", display);
        Assert.Contains("FLAC", display);
        Assert.Contains("OGG", display);
        Assert.Contains("MP4", display);
    }

    // =========================================================================
    // 2. TESTS DE MeetingService.CreateImportedMeetingAsync
    // =========================================================================

    [Fact]
    public async Task CreateImportedMeetingAsync_CreatesMeetingWithFileNameAsTitle_AndCopiesAudio()
    {
        // Arrange
        var repo = new TestMeetingRepository(_testTempDir);
        var service = new MeetingService(repo);

        var originalFilePath = Path.Combine(_testTempDir, "Reunion_PRISMA2.mp3");
        await File.WriteAllBytesAsync(originalFilePath, new byte[] { 0x01, 0x02, 0x03, 0x04 });

        var duration = TimeSpan.FromMinutes(25);

        // Act
        var meeting = await service.CreateImportedMeetingAsync(
            title: string.Empty, // Debe inferir "Reunion_PRISMA2" del nombre de archivo
            sourceAudioFilePath: originalFilePath,
            audioDuration: duration,
            language: "Spanish");

        // Assert
        Assert.NotNull(meeting);
        Assert.Equal("Reunion_PRISMA2", meeting.Title);
        Assert.Equal(MeetingState.Completed, meeting.State);
        Assert.Equal(ProcessingStatus.Pending, meeting.ProcessingStatus);
        Assert.Equal(duration, meeting.Duration);
        Assert.Equal("Spanish", meeting.Language);

        // Comprobar que el archivo original no fue eliminado
        Assert.True(File.Exists(originalFilePath));

        // Comprobar que el archivo de audio fue copiado al directorio de la reunión
        Assert.NotNull(meeting.AudioFilePath);
        Assert.True(File.Exists(meeting.AudioFilePath));
        Assert.NotEqual(originalFilePath, meeting.AudioFilePath);

        var copiedBytes = await File.ReadAllBytesAsync(meeting.AudioFilePath);
        Assert.Equal(4, copiedBytes.Length);

        // Comprobar que la reunión fue guardada en el repositorio
        var saved = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(saved);
        Assert.Equal("Reunion_PRISMA2", saved.Title);

        // Comprobar que MeetingService mantiene la reunión como CurrentMeeting
        Assert.Same(meeting, service.CurrentMeeting);
    }

    [Fact]
    public async Task CreateImportedMeetingAsync_ThrowsFileNotFoundException_WhenFileDoesNotExist()
    {
        var repo = new TestMeetingRepository(_testTempDir);
        var service = new MeetingService(repo);
        var nonExistentPath = Path.Combine(_testTempDir, "ghost_meeting.mp3");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            service.CreateImportedMeetingAsync("Ghost", nonExistentPath));
    }

    // =========================================================================
    // 3. TESTS DE MainViewModel (Interacción y Modal)
    // =========================================================================

    [Fact]
    public void OpenAndCloseImportAudioModal_TogglesVisibilityAndResetsState()
    {
        // Arrange
        var repo = new TestMeetingRepository(_testTempDir);
        var service = new MeetingService(repo);
        var docExporter = new DocumentExporter();
        using var audioDeviceService = new AudioDeviceService();
        var audioCap = new MockAudioCaptureService();
        var transcriber = new MockTranscriptionService();
        var modelManager = new MockModelManager();
        var diarizer = new PyAnnoteCommunity1DiarizationService();
        var processing = new MockProcessingService();
        var livePipeline = new LiveTranscriptionPipeline(transcriber, audioCap);
        var reconciler = new SpeakerTranscriptAligner();
        var secureStorage = new WindowsDpapiSecureStorageService();
        var actaService = new OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

        var vm = new MainViewModel(
            service,
            docExporter,
            repo,
            audioDeviceService,
            audioCap,
            modelManager,
            transcriber,
            livePipeline,
            diarizer,
            reconciler,
            actaService,
            secureStorage,
            null);

        // Act 1: Abrir modal
        Assert.False(vm.IsImportAudioModalOpen);
        vm.OpenImportAudioModalCommand.Execute(null);

        // Assert 1
        Assert.True(vm.IsImportAudioModalOpen);
        Assert.Null(vm.SelectedImportAudioFilePath);
        Assert.False(vm.HasSelectedImportAudioFile);

        // Act 2: Cerrar modal
        vm.CloseImportAudioModalCommand.Execute(null);

        // Assert 2
        Assert.False(vm.IsImportAudioModalOpen);
    }

    [Fact]
    public async Task HandleDroppedAudioFile_ValidAudio_SetsPropertiesAndEnablesImport()
    {
        // Arrange
        var repo = new TestMeetingRepository(_testTempDir);
        var service = new MeetingService(repo);
        var docExporter = new DocumentExporter();
        using var audioDeviceService = new AudioDeviceService();
        var audioCap = new MockAudioCaptureService();
        var transcriber = new MockTranscriptionService();
        var modelManager = new MockModelManager();
        var diarizer = new PyAnnoteCommunity1DiarizationService();
        var processing = new MockProcessingService();
        var livePipeline = new LiveTranscriptionPipeline(transcriber, audioCap);
        var reconciler = new SpeakerTranscriptAligner();
        var secureStorage = new WindowsDpapiSecureStorageService();
        var actaService = new OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

        var vm = new MainViewModel(
            service,
            docExporter,
            repo,
            audioDeviceService,
            audioCap,
            modelManager,
            transcriber,
            livePipeline,
            diarizer,
            reconciler,
            actaService,
            secureStorage,
            null);

        var audioPath = Path.Combine(_testTempDir, "Entrevista_Tecnica.wav");
        await File.WriteAllBytesAsync(audioPath, new byte[2048]);

        // Act: arrastrar archivo válido
        vm.HandleDroppedAudioFile(audioPath);

        // Assert
        Assert.Equal(audioPath, vm.SelectedImportAudioFilePath);
        Assert.Equal("Entrevista_Tecnica.wav", vm.SelectedImportAudioFileName);
        Assert.True(vm.HasSelectedImportAudioFile);
        Assert.Null(vm.ImportAudioErrorMessage);
        Assert.Equal("2.0 KB", vm.SelectedImportAudioFileSizeText);
    }

    [Fact]
    public async Task HandleDroppedAudioFile_UnsupportedFile_ShowsErrorMessageAndDisablesImport()
    {
        // Arrange
        var repo = new TestMeetingRepository(_testTempDir);
        var service = new MeetingService(repo);
        var docExporter = new DocumentExporter();
        using var audioDeviceService = new AudioDeviceService();
        var audioCap = new MockAudioCaptureService();
        var transcriber = new MockTranscriptionService();
        var modelManager = new MockModelManager();
        var diarizer = new PyAnnoteCommunity1DiarizationService();
        var processing = new MockProcessingService();
        var livePipeline = new LiveTranscriptionPipeline(transcriber, audioCap);
        var reconciler = new SpeakerTranscriptAligner();
        var secureStorage = new WindowsDpapiSecureStorageService();
        var actaService = new OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

        var vm = new MainViewModel(
            service,
            docExporter,
            repo,
            audioDeviceService,
            audioCap,
            modelManager,
            transcriber,
            livePipeline,
            diarizer,
            reconciler,
            actaService,
            secureStorage,
            null);

        var invalidPath = Path.Combine(_testTempDir, "documento.pdf");
        await File.WriteAllBytesAsync(invalidPath, new byte[100]);

        // Act: arrastrar archivo no compatible
        vm.HandleDroppedAudioFile(invalidPath);

        // Assert
        Assert.False(vm.HasSelectedImportAudioFile);
        Assert.NotNull(vm.ImportAudioErrorMessage);
        Assert.Contains("Formato no compatible", vm.ImportAudioErrorMessage);
    }

    [Fact]
    public async Task ConfirmImportAudio_CreatesMeeting_AddsToSavedMeetings_AndStartsBackgroundProcessing()
    {
        // Arrange
        var repo = new TestMeetingRepository(_testTempDir);
        var service = new MeetingService(repo);
        var docExporter = new DocumentExporter();
        using var audioDeviceService = new AudioDeviceService();
        var audioCap = new MockAudioCaptureService();
        var transcriber = new MockTranscriptionService();
        var modelManager = new MockModelManager();
        var diarizer = new PyAnnoteCommunity1DiarizationService();
        var processing = new MockProcessingService();
        var livePipeline = new LiveTranscriptionPipeline(transcriber, audioCap);
        var reconciler = new SpeakerTranscriptAligner();
        var secureStorage = new WindowsDpapiSecureStorageService();
        var actaService = new OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

        var vm = new MainViewModel(
            service,
            docExporter,
            repo,
            audioDeviceService,
            audioCap,
            modelManager,
            transcriber,
            livePipeline,
            diarizer,
            reconciler,
            actaService,
            secureStorage,
            null,
            null,
            processing);

        var audioPath = Path.Combine(_testTempDir, "Comite_Direccion.mp3");
        await File.WriteAllBytesAsync(audioPath, new byte[4096]);

        vm.OpenImportAudioModalCommand.Execute(null);
        vm.HandleDroppedAudioFile(audioPath);

        // Act: Confirmar importación
        await vm.ConfirmImportAudioCommand.ExecuteAsync(null);

        // Assert
        Assert.False(vm.IsImportAudioModalOpen);
        Assert.Equal(1, vm.SelectedNavigationTab); // Navega al Historial
        Assert.Single(vm.SavedMeetings);
        Assert.NotNull(vm.SelectedHistoricalMeeting);
        Assert.Equal("Comite_Direccion", vm.SelectedHistoricalMeeting.Title);
        Assert.Equal(ProcessingStatus.Pending, vm.SelectedHistoricalMeeting.ProcessingStatus);

        // Comprueba que el procesamiento en segundo plano fue lanzado
        Assert.True(processing.IsProcessing);
        Assert.Equal(vm.SelectedHistoricalMeeting.Id, processing.CurrentMeetingId);
    }
}
