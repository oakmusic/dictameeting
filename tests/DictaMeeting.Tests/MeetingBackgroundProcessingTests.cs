using DictaMeeting.App.Services;
using DictaMeeting.App.ViewModels;
using DictaMeeting.Audio.Events;
using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Models;
using DictaMeeting.Audio.Services;
using DictaMeeting.Diarization.Interfaces;
using DictaMeeting.Diarization.Models;
using DictaMeeting.Diarization.Pipeline;
using DictaMeeting.Diarization.Services;
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

public class MeetingBackgroundProcessingTests : IDisposable
{
    private readonly string _testBaseDir;

    public MeetingBackgroundProcessingTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_BgTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBaseDir);
    }

    internal class TestRepository : IMeetingRepository
    {
        private readonly string _baseDir;
        public Dictionary<string, Meeting> Meetings { get; } = new();

        public TestRepository(string baseDir)
        {
            _baseDir = baseDir;
        }

        public Task SaveMeetingAsync(Meeting meeting, CancellationToken cancellationToken = default)
        {
            Meetings[meeting.Id] = meeting;
            return Task.CompletedTask;
        }

        public Task SaveMeetingAsync(Meeting meeting, MeetingSessionParameters? parameters, CancellationToken cancellationToken = default)
        {
            Meetings[meeting.Id] = meeting;
            return Task.CompletedTask;
        }

        public Task<Meeting?> GetMeetingAsync(string id, CancellationToken cancellationToken = default)
        {
            Meetings.TryGetValue(id, out var m);
            return Task.FromResult(m);
        }

        public Task<IReadOnlyList<Meeting>> GetAllMeetingsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Meeting>>(Meetings.Values.ToList());
        }

        public string GetMeetingDirectoryPath(Meeting meeting)
        {
            var dir = Path.Combine(_baseDir, meeting.Id);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public string GetMeetingFileBaseName(Meeting meeting) => $"{meeting.Title}_{meeting.Date:yyyy-MM-dd}";
        public string GetAudioFilePath(Meeting meeting) => Path.Combine(GetMeetingDirectoryPath(meeting), "audio.mp3");
    }

    internal class TestModelManager : IModelManager
    {
        public string ModelsDirectory { get; private set; } = "C:\\test\\models";
        public void SetModelsDirectory(string path) => ModelsDirectory = path;

        public Task<HardwareCapabilities> DetectHardwareAsync() => Task.FromResult(new HardwareCapabilities
        {
            CpuLogicalCores = 8,
            TotalRamGb = 16.0,
            RecommendedModel = ModelSize.Base,
            RecommendedLiveModel = ModelSize.Base,
            RecommendedFinalModel = ModelSize.Small
        });

        public IReadOnlyList<TranscriptionModelInfo> GetAvailableModels() => new List<TranscriptionModelInfo>
        {
            new() { Size = ModelSize.Tiny, Name = "Tiny", IsDownloaded = true },
            new() { Size = ModelSize.Base, Name = "Base", IsDownloaded = true },
            new() { Size = ModelSize.Small, Name = "Small", IsDownloaded = true },
            new() { Size = ModelSize.LargeV3Turbo, Name = "LargeV3Turbo", IsDownloaded = true }
        };

        public Task<string> EnsureModelDownloadedAsync(ModelSize modelSize, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult($"dummy_{modelSize}.bin");

        public bool DeleteModel(ModelSize modelSize) => true;

        public bool IsDiarizationModelDownloaded() => true;
        public Task<string> EnsureDiarizationModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult("dummy_diarization_path");
    }

    internal class DelayableTranscriptionService : ITranscriptionService
    {
        public bool ShouldThrow { get; set; }
        public int DelayMs { get; set; }
        public TaskCompletionSource<bool>? ProcessingGate { get; set; }
        public TaskCompletionSource<bool>? ProcessingStartedSignal { get; set; }
        public bool IsInitialized { get; set; } = true;
        public ModelSize CurrentModel { get; set; } = ModelSize.Base;
        public LanguageMode CurrentLanguage { get; set; } = LanguageMode.Spanish;

        public Task InitializeAsync(ModelSize modelSize, CancellationToken cancellationToken = default)
        {
            CurrentModel = modelSize;
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(string audioFilePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            return await TranscribeAudioFileAsync(audioFilePath, CurrentModel, CurrentLanguage, progress, cancellationToken);
        }

        public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(string audioFilePath, ModelSize modelSize, LanguageMode language, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            CurrentModel = modelSize;
            CurrentLanguage = language;
            ProcessingStartedSignal?.TrySetResult(true);

            if (ProcessingGate != null)
            {
                await ProcessingGate.Task;
            }

            if (DelayMs > 0)
            {
                await Task.Delay(DelayMs, cancellationToken);
            }

            if (ShouldThrow)
            {
                throw new InvalidOperationException("Fallo simulado en motor Whisper.");
            }

            progress?.Report(1.0);
            return new List<TranscriptSegment>
            {
                new()
                {
                    StartTime = TimeSpan.FromSeconds(0),
                    EndTime = TimeSpan.FromSeconds(5),
                    Text = $"Transcripción final de alta fidelidad con modelo {modelSize}.",
                    IsFinal = true
                }
            };
        }

        public Task<TranscriptSegment?> TranscribeAudioChunkAsync(byte[] pcmAudioData, TimeSpan offset, CancellationToken cancellationToken = default)
            => Task.FromResult<TranscriptSegment?>(null);

        public void Dispose() { }
    }

    internal class MockDiarizationService : ISpeakerDiarizationService
    {
        public IReadOnlyList<string> KnownSpeakers => new[] { "SPEAKER_00" };
        public void SetModelsFolder(string? folder) { }
        public void Reset() { }

        public Task<DiarizationResult> DiarizeAudioFileAsync(
            string audioFilePath,
            DiarizationOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(1.0);
            return Task.FromResult(new DiarizationResult
            {
                Duration = TimeSpan.FromSeconds(5),
                DetectedSpeakerCount = 1,
                ExclusiveSegments = new List<ExclusiveSpeakerSegment>
                {
                    new()
                    {
                        StartTime = TimeSpan.Zero,
                        EndTime = TimeSpan.FromSeconds(5),
                        SpeakerId = "SPEAKER_00"
                    }
                }
            });
        }

        public Task<DiarizationResult> DiarizeAsync(
            float[] samples,
            int sampleRate = 16000,
            DiarizationOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return DiarizeAudioFileAsync("dummy.wav", options, progress, cancellationToken);
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task StopMeeting_ImmediatelySavesInterimMeeting_AndCompletesBackgroundJob()
    {
        // Arrange
        var repo = new TestRepository(_testBaseDir);
        var gate = new TaskCompletionSource<bool>();
        var startedSignal = new TaskCompletionSource<bool>();
        var transcriber = new DelayableTranscriptionService
        {
            ProcessingGate = gate,
            ProcessingStartedSignal = startedSignal
        };
        var modelManager = new TestModelManager();
        var diarizer = new MockDiarizationService();
        var reconciler = new SpeakerTranscriptAligner();

        var processingService = new MeetingProcessingService(repo, transcriber, modelManager, diarizer, reconciler);
        var meetingService = new MeetingService(repo);
        var docExporter = new DocumentExporter();
        var audioDeviceService = new AudioDeviceService();
        var audioCaptureService = new MockAudioCaptureService();
        var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
        var secureStorage = new WindowsDpapiSecureStorageService();
        var aiActaService = new DictaMeeting.AI.Services.OpenRouterActaService(new HttpClient(), secureStorage);

        var vm = new MainViewModel(
            meetingService,
            docExporter,
            repo,
            audioDeviceService,
            audioCaptureService,
            modelManager,
            transcriber,
            livePipeline,
            diarizer,
            reconciler,
            aiActaService,
            secureStorage,
            null,
            null,
            processingService);

        vm.ShowRecordingNotice = false;

        // Act 1: Iniciar reunión y agregar transcripción provisional
        await vm.StartMeetingCommand.ExecuteAsync(null);
        meetingService.AppendSegment(new TranscriptSegment
        {
            SpeakerId = "SPEAKER_00",
            Text = "Texto provisional en vivo.",
            IsFinal = false
        });

        // Act 2: Detener reunión
        await vm.StopMeetingCommand.ExecuteAsync(null);

        // Assert inmediato: la reunión y el audio están guardados al instante
        var savedMeeting = repo.Meetings[meetingService.CurrentMeeting!.Id];
        Assert.NotNull(savedMeeting);
        Assert.NotNull(savedMeeting.AudioFilePath);
        Assert.True(File.Exists(savedMeeting.AudioFilePath), "El audio debe existir en disco inmediatamente tras detener la reunión.");
        Assert.Contains(savedMeeting.Transcript, s => s.Text == "Texto provisional en vivo.");
        Assert.True(vm.IsPostProcessing, "El ViewModel debe indicar IsPostProcessing activo al detener la reunión.");
        Assert.False(vm.HasPlayerAudio, "El reproductor NO debe mostrarse mientras el post-procesamiento está activo.");

        // Liberar el gate de procesamiento en segundo plano
        gate.TrySetResult(true);

        // Act 3: Esperar que el trabajo en segundo plano termine
        await vm.WaitForCurrentProcessingAsync();

        // Assert post-procesamiento: estado Completado y transcripción definitiva actualizada
        Assert.Equal(ProcessingStatus.Completed, savedMeeting.ProcessingStatus);
        Assert.Contains(savedMeeting.Transcript, s => s.Text.Contains("Transcripción final de alta fidelidad"));
        Assert.False(processingService.IsProcessing, "El servicio debe marcar IsProcessing = false al finalizar.");
        Assert.False(vm.IsPostProcessing, "El ViewModel debe ocultar IsPostProcessing tras completar.");
        Assert.True(vm.HasPlayerAudio, "El reproductor DEBE mostrarse una vez finalizado el post-procesamiento.");
        Assert.Equal("TRANSCRIPCIÓN FINAL", vm.TranscriptHeaderTitle);
    }

    [Fact]
    public async Task PostProcessingCompletion_SyncsDiscoveredSpeakers_AndHidesCancelButton_AndDismissesBanner()
    {
        // Arrange
        var repo = new TestRepository(_testBaseDir);
        var transcriber = new DelayableTranscriptionService();
        var modelManager = new TestModelManager();
        var diarizer = new MockDiarizationService();
        var reconciler = new SpeakerTranscriptAligner();
        var processingService = new MeetingProcessingService(repo, transcriber, modelManager, diarizer, reconciler);
        var meetingService = new MeetingService(repo);
        var docExporter = new DocumentExporter();
        var audioDeviceService = new AudioDeviceService();
        var audioCaptureService = new MockAudioCaptureService();
        var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
        var secureStorage = new WindowsDpapiSecureStorageService();
        var aiActaService = new DictaMeeting.AI.Services.OpenRouterActaService(new HttpClient(), secureStorage);

        var vm = new MainViewModel(
            meetingService,
            docExporter,
            repo,
            audioDeviceService,
            audioCaptureService,
            modelManager,
            transcriber,
            livePipeline,
            diarizer,
            reconciler,
            aiActaService,
            secureStorage,
            null,
            null,
            processingService);

        vm.ShowRecordingNotice = false;

        await vm.StartMeetingCommand.ExecuteAsync(null);
        await vm.StopMeetingCommand.ExecuteAsync(null);

        // Act: esperar a que termine el procesamiento
        await vm.WaitForCurrentProcessingAsync();

        // Assert:
        Assert.False(processingService.IsProcessing);
        Assert.False(vm.IsPostProcessing);
        Assert.False(vm.CanCancelPostProcessing);
        Assert.True(vm.HasPlayerAudio, "El reproductor de audio debe estar disponible tras completar el post-procesamiento.");
        Assert.Equal("TRANSCRIPCIÓN FINAL", vm.TranscriptHeaderTitle);
        Assert.Contains("Alta fidelidad", vm.TranscriptHeaderSubtitle);
    }

    [Fact]
    public async Task CancelProcessing_PreservesAudioAndOriginalTranscript_AndMarksCancelled()
    {
        // Arrange
        var repo = new TestRepository(_testBaseDir);
        var transcriber = new DelayableTranscriptionService { DelayMs = 500 };
        var modelManager = new TestModelManager();
        var diarizer = new MockDiarizationService();
        var reconciler = new SpeakerTranscriptAligner();
        var processingService = new MeetingProcessingService(repo, transcriber, modelManager, diarizer, reconciler);

        var meeting = new Meeting
        {
            Title = "Reunión para Cancelar",
            Date = DateTime.Now
        };
        var dir = repo.GetMeetingDirectoryPath(meeting);
        var audioFile = Path.Combine(dir, "audio.mp3");
        await File.WriteAllBytesAsync(audioFile, new byte[128]);
        meeting.AudioFilePath = audioFile;
        meeting.Transcript.Add(new TranscriptSegment
        {
            SpeakerId = "SPEAKER_00",
            Text = "Transcripción original en vivo que no debe perderse.",
            IsFinal = false
        });

        await repo.SaveMeetingAsync(meeting);

        // Act: Iniciar procesamiento y cancelarlo rápidamente
        var task = processingService.EnqueueOrProcessAsync(meeting.Id, "Small");
        await Task.Delay(50); // dejar que comience
        await processingService.CancelProcessingAsync(meeting.Id);
        await task;

        // Assert
        var updated = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(updated);
        Assert.Equal(ProcessingStatus.Cancelled, updated.ProcessingStatus);
        Assert.True(File.Exists(updated.AudioFilePath), "El audio original debe permanecer intacto tras cancelación.");
        Assert.Single(updated.Transcript);
        Assert.Equal("Transcripción original en vivo que no debe perderse.", updated.Transcript[0].Text);
    }

    [Fact]
    public async Task FailureInProcessing_PreservesAudio_MarksError_AndAllowsRetry()
    {
        // Arrange
        var repo = new TestRepository(_testBaseDir);
        var transcriber = new DelayableTranscriptionService { ShouldThrow = true };
        var modelManager = new TestModelManager();
        var diarizer = new MockDiarizationService();
        var reconciler = new SpeakerTranscriptAligner();
        var processingService = new MeetingProcessingService(repo, transcriber, modelManager, diarizer, reconciler);

        var meeting = new Meeting
        {
            Title = "Reunión con Fallo",
            Date = DateTime.Now
        };
        var dir = repo.GetMeetingDirectoryPath(meeting);
        var audioFile = Path.Combine(dir, "audio.mp3");
        await File.WriteAllBytesAsync(audioFile, new byte[128]);
        meeting.AudioFilePath = audioFile;
        meeting.Transcript.Add(new TranscriptSegment
        {
            SpeakerId = "SPEAKER_00",
            Text = "Transcripción original intacta.",
            IsFinal = false
        });
        await repo.SaveMeetingAsync(meeting);

        // Act 1: Procesar con error
        await processingService.EnqueueOrProcessAsync(meeting.Id, "Small");

        // Assert 1: Fallo controlado
        var failed = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(failed);
        Assert.Equal(ProcessingStatus.Error, failed.ProcessingStatus);
        Assert.NotNull(failed.ProcessingError);
        Assert.Contains("Fallo simulado en motor Whisper", failed.ProcessingError);
        Assert.True(File.Exists(failed.AudioFilePath), "El archivo de audio debe conservarse tras un fallo.");
        Assert.Equal("Transcripción original intacta.", failed.Transcript[0].Text);

        // Act 2: Solucionar fallo y reintentar
        transcriber.ShouldThrow = false;
        await processingService.EnqueueOrProcessAsync(meeting.Id, "Small");

        // Assert 2: Completado exitoso tras reintento
        var retried = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(retried);
        Assert.Equal(ProcessingStatus.Completed, retried.ProcessingStatus);
        Assert.Null(retried.ProcessingError);
        Assert.Contains("Transcripción final de alta fidelidad", retried.Transcript[0].Text);
    }

    [Fact]
    public async Task RecoverInterruptedJobs_NormalizesProcessingToPending()
    {
        // Arrange
        var repo = new TestRepository(_testBaseDir);
        var transcriber = new DelayableTranscriptionService();
        var modelManager = new TestModelManager();
        var diarizer = new MockDiarizationService();
        var reconciler = new SpeakerTranscriptAligner();
        var processingService = new MeetingProcessingService(repo, transcriber, modelManager, diarizer, reconciler);

        var meeting = new Meeting
        {
            Title = "Reunión Interrumpida",
            Date = DateTime.Now,
            ProcessingStatus = ProcessingStatus.Processing,
            ProcessingStatusText = "Procesando en el momento del corte..."
        };
        await repo.SaveMeetingAsync(meeting);

        // Act: Simular arranque y recuperación tras cierre de app
        await processingService.RecoverInterruptedJobsAsync();

        // Assert: Queda en Pending con mensaje de recuperación
        var recovered = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(recovered);
        Assert.Equal(ProcessingStatus.Pending, recovered.ProcessingStatus);
        Assert.Contains("interrumpido", recovered.ProcessingStatusText);
    }

    [Fact]
    public async Task ReprocessCompletedMeeting_WithAnotherModel_UpdatesTranscriptOnSuccess()
    {
        // Arrange
        var repo = new TestRepository(_testBaseDir);
        var transcriber = new DelayableTranscriptionService();
        var modelManager = new TestModelManager();
        var diarizer = new MockDiarizationService();
        var reconciler = new SpeakerTranscriptAligner();
        var processingService = new MeetingProcessingService(repo, transcriber, modelManager, diarizer, reconciler);

        var meeting = new Meeting
        {
            Title = "Reunión Reprocesable",
            Date = DateTime.Now,
            ProcessingStatus = ProcessingStatus.Completed
        };
        var dir = repo.GetMeetingDirectoryPath(meeting);
        var audioFile = Path.Combine(dir, "audio.mp3");
        await File.WriteAllBytesAsync(audioFile, new byte[128]);
        meeting.AudioFilePath = audioFile;
        meeting.Transcript.Add(new TranscriptSegment
        {
            SpeakerId = "SPEAKER_00",
            Text = "Resultado con modelo Tiny original.",
            IsFinal = true
        });
        await repo.SaveMeetingAsync(meeting);

        // Act: Reprocesar con LargeV3Turbo
        await processingService.EnqueueOrProcessAsync(meeting.Id, "LargeV3Turbo");

        // Assert
        var updated = await repo.GetMeetingAsync(meeting.Id);
        Assert.NotNull(updated);
        Assert.Equal(ProcessingStatus.Completed, updated.ProcessingStatus);
        Assert.Equal("LargeV3Turbo", updated.ProcessingModel);
        Assert.Contains("LargeV3Turbo", updated.Transcript[0].Text);
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
}
