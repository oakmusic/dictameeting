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
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Services;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class TwoPassModelSelectionTests
{
    internal class DummyMeetingRepository : IMeetingRepository
    {
        public List<Meeting> SavedMeetings { get; } = new();

        public Task SaveMeetingAsync(Meeting meeting, CancellationToken cancellationToken = default)
        {
            SavedMeetings.Add(meeting);
            return Task.CompletedTask;
        }

        public Task SaveMeetingAsync(Meeting meeting, MeetingSessionParameters? parameters, CancellationToken cancellationToken = default)
        {
            SavedMeetings.Add(meeting);
            return Task.CompletedTask;
        }

        public Task<Meeting?> GetMeetingAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(SavedMeetings.FirstOrDefault(m => m.Id == id));

        public Task<IReadOnlyList<Meeting>> GetAllMeetingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Meeting>>(SavedMeetings);

        public string GetMeetingDirectoryPath(Meeting meeting)
        {
            var dir = Path.Combine(Path.GetTempPath(), "DictaMeetingTests", meeting.Id);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public string GetMeetingFileBaseName(Meeting meeting) => $"{meeting.Title}_{meeting.Date:yyyy-MM-dd_HH-mm}";
        public string GetAudioFilePath(Meeting meeting) => Path.Combine(GetMeetingDirectoryPath(meeting), "audio.mp3");
    }

    internal class DummyModelManager : IModelManager
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
                RecommendedFinalModel = ModelSize.Qwen3_17B
            });
        }

        public IReadOnlyList<TranscriptionModelInfo> GetAvailableModels()
        {
            return new List<TranscriptionModelInfo>
            {
                new() { Size = ModelSize.Qwen3_06B, Name = "Qwen3_06B", IsDownloaded = true },
                new() { Size = ModelSize.Qwen3_17B, Name = "Qwen3_17B", IsDownloaded = true },
                new() { Size = ModelSize.Tiny, Name = "Tiny", IsDownloaded = true },
                new() { Size = ModelSize.Base, Name = "Base", IsDownloaded = true },
                new() { Size = ModelSize.Small, Name = "Small", IsDownloaded = true },
                new() { Size = ModelSize.Medium, Name = "Medium", IsDownloaded = true },
                new() { Size = ModelSize.LargeV3Turbo, Name = "LargeV3Turbo", IsDownloaded = true },
            };
        }

        public Task<string> EnsureModelDownloadedAsync(ModelSize modelSize, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult($"dummy_path_{modelSize}.bin");
        }

        public bool DeleteModel(ModelSize modelSize) => true;

        public bool IsDiarizationModelDownloaded() => true;
        public Task<string> EnsureDiarizationModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult("dummy_diarization_path");
    }

    internal class DummyLivePipeline : ILiveTranscriptionPipeline
    {
        public event EventHandler<TranscriptSegment>? SegmentProduced
        {
            add { }
            remove { }
        }
        public bool IsRunning { get; private set; }

        public Task StartAsync(TimeSpan startOffset = default, CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void EnqueueAudioChunk(byte[] pcmData) { }
        public void EnqueueAudioChunk(byte[] pcmData, bool isMicrophoneDominant, float micRms = 0f, float loopbackRms = 0f) { }

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private (MainViewModel vm, MockTranscriptionService transcriber, DummyMeetingRepository repo) CreateViewModel()
    {
        var repo = new DummyMeetingRepository();
        var meetingService = new MeetingService(repo);
        var docExporter = new DocumentExporter();
        var audioDeviceService = new AudioDeviceService();
        var audioCaptureService = new MockAudioCaptureService();
        var modelManager = new DummyModelManager();
        var transcriptionService = new MockTranscriptionService();
        var livePipeline = new DummyLivePipeline();
        var diarizationService = new PyAnnoteCommunity1DiarizationService();
        var speakerAligner = new SpeakerTranscriptAligner();
        var aiActaService = new OpenRouterActaService(new HttpClient(), new WindowsDpapiSecureStorageService());
        var secureStorage = new WindowsDpapiSecureStorageService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"user_settings_test_{Guid.NewGuid():N}.json");
        var userSettingsService = new DictaMeeting.Infrastructure.Persistence.UserSettingsService(tempFile);
        userSettingsService.SaveSettings(new UserSettings { ShowRecordingNotice = false });

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

        return (vm, transcriptionService, repo);
    }

    [Fact]
    public void TwoPass_InitializesAvailableLiveAndFinalModels_Correctly()
    {
        var (vm, _, _) = CreateViewModel();

        Assert.NotEmpty(vm.AvailableLiveModels);
        Assert.NotEmpty(vm.AvailableFinalModels);

        // En Vivo debe tener modo automático y modelos ligeros
        Assert.Contains(vm.AvailableLiveModels, m => m.IsAutomatic);
        Assert.Contains(vm.AvailableLiveModels, m => m.ModelSize == ModelSize.Tiny);
        Assert.Contains(vm.AvailableLiveModels, m => m.ModelSize == ModelSize.Base);

        // Final debe tener opción Automático y opción Mismo que en vivo
        Assert.Contains(vm.AvailableFinalModels, m => m.IsAutomatic);
        Assert.Contains(vm.AvailableFinalModels, m => m.IsSameAsLive);
        Assert.Contains(vm.AvailableFinalModels, m => m.ModelSize == ModelSize.LargeV3Turbo);

        // Valores por defecto seleccionados
        Assert.NotNull(vm.SelectedLiveModel);
        Assert.True(vm.SelectedLiveModel.IsAutomatic);

        Assert.NotNull(vm.SelectedFinalModel);
        Assert.True(vm.SelectedFinalModel.IsAutomatic);

        // Qwen3 debe aparecer de primero en las listas de modelos específicos
        Assert.Equal(ModelSize.Qwen3_06B, vm.AvailableLiveModels[1].ModelSize);
        Assert.Equal(ModelSize.Qwen3_17B, vm.AvailableLiveModels[2].ModelSize);
        Assert.Equal(ModelSize.Qwen3_06B, vm.AvailableFinalModels[2].ModelSize);
        Assert.Equal(ModelSize.Qwen3_17B, vm.AvailableFinalModels[3].ModelSize);

        // Retrocompatibilidad con SelectedModel
        Assert.NotNull(vm.SelectedModel);
        Assert.Same(vm.SelectedLiveModel, vm.SelectedModel);
    }

    [Fact]
    public void TwoPass_SynchronizesSelectedLiveModel_And_SelectedModel()
    {
        var (vm, _, _) = CreateViewModel();

        var tinyOption = vm.AvailableLiveModels.First(m => m.ModelSize == ModelSize.Tiny);
        vm.SelectedLiveModel = tinyOption;

        Assert.Same(tinyOption, vm.SelectedModel);

        var baseOption = vm.AvailableLiveModels.First(m => m.ModelSize == ModelSize.Base);
        vm.SelectedModel = baseOption;

        Assert.Same(baseOption, vm.SelectedLiveModel);
    }

    [Fact]
    public async Task TwoPass_StartAndStopMeeting_SwitchesToFinalModelWhenDifferent()
    {
        var (vm, transcriber, repo) = CreateViewModel();

        // Configurar En Vivo = Base, Final = Small
        vm.SelectedLiveModel = vm.AvailableLiveModels.First(m => m.ModelSize == ModelSize.Base);
        vm.SelectedFinalModel = vm.AvailableFinalModels.First(m => m.ModelSize == ModelSize.Small);

        // Iniciar reunión
        await vm.StartMeetingCommand.ExecuteAsync(null);

        // En vivo debe haber inicializado con el modelo Base
        Assert.Equal(ModelSize.Base, transcriber.CurrentModel);

        // Detener reunión (dispara post-procesado Two-Pass)
        await vm.StopMeetingCommand.ExecuteAsync(null);

        // El transcriptor debe haber cambiado al modelo Final (Small)
        Assert.Equal(ModelSize.Small, transcriber.CurrentModel);
    }

    [Fact]
    public async Task TwoPass_StartAndStopMeeting_PreservesModelWhenSameAsLiveSelected()
    {
        var (vm, transcriber, repo) = CreateViewModel();

        // Configurar En Vivo = Base, Final = Mismo que en vivo
        vm.SelectedLiveModel = vm.AvailableLiveModels.First(m => m.ModelSize == ModelSize.Base);
        vm.SelectedFinalModel = vm.AvailableFinalModels.First(m => m.IsSameAsLive);

        // Iniciar reunión
        await vm.StartMeetingCommand.ExecuteAsync(null);
        Assert.Equal(ModelSize.Base, transcriber.CurrentModel);

        // Detener reunión
        await vm.StopMeetingCommand.ExecuteAsync(null);

        // El transcriptor debe seguir en Base (sin recarga)
        Assert.Equal(ModelSize.Base, transcriber.CurrentModel);
    }

    [Fact]
    public async Task TwoPass_AutomaticMode_SelectsQwen3_06B_ForLiveAndQwen3_17B_ForFinal()
    {
        var (vm, transcriber, repo) = CreateViewModel();

        // Por defecto ambos están en modo Automático
        Assert.True(vm.SelectedLiveModel!.IsAutomatic);
        Assert.True(vm.SelectedFinalModel!.IsAutomatic);

        // Iniciar reunión en modo Automático
        await vm.StartMeetingCommand.ExecuteAsync(null);

        // En vivo debe haber resuelto e inicializado Qwen3_06B
        Assert.Equal(ModelSize.Qwen3_06B, transcriber.CurrentModel);

        // Detener reunión
        await vm.StopMeetingCommand.ExecuteAsync(null);

        // Procesado final debe haber resuelto Qwen3_17B
        Assert.Equal(ModelSize.Qwen3_17B, transcriber.CurrentModel);
    }
}
