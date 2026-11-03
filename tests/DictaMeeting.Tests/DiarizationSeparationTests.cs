using DictaMeeting.App.Services;
using DictaMeeting.Diarization.Interfaces;
using DictaMeeting.Diarization.Models;
using DictaMeeting.Diarization.Pipeline;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using Xunit;

namespace DictaMeeting.Tests;

public class DiarizationSeparationTests : IDisposable
{
    private readonly string _tempDir;

    public DiarizationSeparationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_SepTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private class MockRepo : IMeetingRepository
    {
        public Dictionary<string, Meeting> Meetings { get; } = new();

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

        public Task<bool> DeleteMeetingAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Meetings.Remove(id));
        }

        public string GetMeetingDirectoryPath(Meeting meeting) => Path.Combine(Path.GetTempPath(), meeting.Id);
        public string GetMeetingFileBaseName(Meeting meeting) => "meeting";
        public string GetAudioFilePath(Meeting meeting) => meeting.AudioFilePath ?? string.Empty;
        public string GetAudioFilePath(Meeting meeting, string? extension) => meeting.AudioFilePath ?? string.Empty;
        public Task<MeetingActaIndex?> GetMeetingActaIndexAsync(Meeting meeting, CancellationToken cancellationToken = default) => Task.FromResult<MeetingActaIndex?>(null);
        public Task SaveMeetingActaVersionAsync(Meeting meeting, MeetingActaVersion version, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteMeetingActaVersionAsync(Meeting meeting, string versionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetActiveMeetingActaVersionAsync(Meeting meeting, string versionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class MockTranscriptionService : ITranscriptionService
    {
        public bool IsInitialized { get; set; } = true;
        public ModelSize CurrentModel { get; set; } = ModelSize.Qwen3_06B;
        public LanguageMode CurrentLanguage { get; set; } = LanguageMode.Spanish;

        public Task InitializeAsync(ModelSize modelSize, CancellationToken cancellationToken = default)
        {
            CurrentModel = modelSize;
            return Task.CompletedTask;
        }

        public Task<TranscriptSegment?> TranscribeAudioChunkAsync(byte[] pcmAudioData, TimeSpan offset, CancellationToken cancellationToken = default)
            => Task.FromResult<TranscriptSegment?>(null);

        public Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(string audioFilePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => TranscribeAudioFileAsync(audioFilePath, CurrentModel, CurrentLanguage, progress, cancellationToken);

        public Func<string, ModelSize, LanguageMode, IProgress<double>?, CancellationToken, Task<IReadOnlyList<TranscriptSegment>>>? CustomTranscribe;

        public Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(
            string audioFilePath,
            ModelSize modelSize,
            LanguageMode language,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (CustomTranscribe != null)
            {
                return CustomTranscribe(audioFilePath, modelSize, language, progress, cancellationToken);
            }

            var list = new List<TranscriptSegment>
            {
                new()
                {
                    Id = "seg-1",
                    StartTime = TimeSpan.FromSeconds(0),
                    EndTime = TimeSpan.FromSeconds(4),
                    Text = "Buenos días a todos.",
                    SpeakerId = "SPEAKER_00",
                    SpeakerDisplayName = "SPEAKER_00"
                },
                new()
                {
                    Id = "seg-2",
                    StartTime = TimeSpan.FromSeconds(4.5),
                    EndTime = TimeSpan.FromSeconds(8),
                    Text = "Hola Joseba, comenzamos.",
                    SpeakerId = "SPEAKER_00",
                    SpeakerDisplayName = "SPEAKER_00"
                }
            };
            return Task.FromResult<IReadOnlyList<TranscriptSegment>>(list);
        }

        public void Dispose() { }
    }

    private class MockDiarizationService : ISpeakerDiarizationService
    {
        public IReadOnlyList<string> KnownSpeakers => new List<string> { "SPEAKER_00", "SPEAKER_01" };
        public void SetModelsFolder(string? folder) { }
        public void Reset() { }
        public void Dispose() { }

        public Func<string, DiarizationOptions?, IProgress<double>?, CancellationToken, Task<DiarizationResult>>? CustomDiarize;

        public Task<DiarizationResult> DiarizeAudioFileAsync(
            string audioFilePath,
            DiarizationOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (CustomDiarize != null)
            {
                return CustomDiarize(audioFilePath, options, progress, cancellationToken);
            }

            var result = new DiarizationResult
            {
                AudioDuration = TimeSpan.FromSeconds(10),
                DetectedSpeakerCount = 2,
                ExclusiveSegments = new List<ExclusiveSpeakerSegment>
                {
                    new() { StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(4.2), SpeakerId = "SPEAKER_00" },
                    new() { StartTime = TimeSpan.FromSeconds(4.3), EndTime = TimeSpan.FromSeconds(9), SpeakerId = "SPEAKER_01" }
                },
                SpeakerCentroids = new Dictionary<string, float[]>
                {
                    { "SPEAKER_00", new float[] { 1.0f, 0.0f, 0.0f } },
                    { "SPEAKER_01", new float[] { 0.0f, 1.0f, 0.0f } }
                }
            };
            return Task.FromResult(result);
        }

        public Task<DiarizationResult> DiarizeAsync(float[] samples, int sampleRate = 16000, DiarizationOptions? options = null, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            return DiarizeAudioFileAsync(string.Empty, options, progress, cancellationToken);
        }
    }

    private class MockModelManager : IModelManager
    {
        public string ModelsDirectory { get; private set; } = "C:\\test\\models";
        public void SetModelsDirectory(string path) => ModelsDirectory = path;

        public Task<HardwareCapabilities> DetectHardwareAsync() => Task.FromResult(new HardwareCapabilities
        {
            CpuLogicalCores = 8,
            TotalRamGb = 16.0,
            RecommendedModel = ModelSize.Qwen3_06B,
            RecommendedLiveModel = ModelSize.Qwen3_06B,
            RecommendedFinalModel = ModelSize.Qwen3_06B
        });

        public IReadOnlyList<TranscriptionModelInfo> GetAvailableModels() => new List<TranscriptionModelInfo>
        {
            new() { Size = ModelSize.Qwen3_06B, Name = "Qwen3 0.6B", IsDownloaded = true }
        };

        public Task<string> EnsureModelDownloadedAsync(ModelSize size, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult("dummy_model.bin");

        public bool DeleteModel(ModelSize size) => true;

        public bool IsDiarizationModelDownloaded() => true;

        public Task<string> EnsureDiarizationModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult("dummy_diarization");
    }

    [Fact]
    public async Task TranscriptionOnly_ProducesValidMeeting_WithoutRunningDiarization()
    {
        var repo = new MockRepo();
        var asr = new MockTranscriptionService();
        var diar = new MockDiarizationService();
        diar.CustomDiarize = (_, _, _, _) => throw new InvalidOperationException("Diarization should NOT run!");

        var aligner = new SpeakerTranscriptAligner();
        var modelMgr = new MockModelManager();
        var processingService = new MeetingProcessingService(repo, asr, modelMgr, diar, aligner);

        var dummyAudio = Path.Combine(_tempDir, "test.wav");
        File.WriteAllText(dummyAudio, "RIFF dummy audio content");

        var meeting = new Meeting
        {
            Id = "meeting-1",
            Title = "Reunión de prueba ASR",
            AudioFilePath = dummyAudio,
            ProcessingStatus = ProcessingStatus.Pending
        };
        await repo.SaveMeetingAsync(meeting);

        // Act
        await processingService.EnqueueOrProcessAsync(meeting.Id, "Qwen3_06B", ProcessingMode.TranscriptionOnly);

        // Assert
        var saved = await repo.GetMeetingAsync("meeting-1");
        Assert.NotNull(saved);
        Assert.Equal(ProcessingStatus.Completed, saved.ProcessingStatus);
        Assert.Equal(TranscriptionStatus.Completed, saved.TranscriptionStatus);
        Assert.Equal(DiarizationStatus.None, saved.DiarizationStatus);
        Assert.Null(saved.TranscriptionError);
        Assert.Null(saved.DiarizationError);
        Assert.Equal(2, saved.Transcript.Count);
        Assert.Equal("Buenos días a todos.", saved.Transcript[0].Text);
    }

    [Fact]
    public async Task DiarizationOnly_PreservesExistingTranscriptTextAndTimestamps_UpdatesSpeakers()
    {
        var repo = new MockRepo();
        var asr = new MockTranscriptionService();
        asr.CustomTranscribe = (_, _, _, _, _) => throw new InvalidOperationException("ASR should NOT run!");

        var diar = new MockDiarizationService();
        var aligner = new SpeakerTranscriptAligner();
        var modelMgr = new MockModelManager();
        var processingService = new MeetingProcessingService(repo, asr, modelMgr, diar, aligner);

        var dummyAudio = Path.Combine(_tempDir, "test.wav");
        File.WriteAllText(dummyAudio, "dummy audio");

        var initialSegments = new List<TranscriptSegment>
        {
            new()
            {
                Id = "custom-id-1",
                StartTime = TimeSpan.FromSeconds(0.5),
                EndTime = TimeSpan.FromSeconds(3.5),
                Text = "Texto sagrado intocable 1.",
                SpeakerId = "SPEAKER_00",
                SpeakerDisplayName = "SPEAKER_00"
            },
            new()
            {
                Id = "custom-id-2",
                StartTime = TimeSpan.FromSeconds(4.8),
                EndTime = TimeSpan.FromSeconds(8.2),
                Text = "Texto sagrado intocable 2.",
                SpeakerId = "SPEAKER_00",
                SpeakerDisplayName = "SPEAKER_00"
            }
        };

        var meeting = new Meeting
        {
            Id = "meeting-2",
            AudioFilePath = dummyAudio,
            Transcript = new List<TranscriptSegment>(initialSegments),
            TranscriptionStatus = TranscriptionStatus.Completed,
            DiarizationStatus = DiarizationStatus.None
        };
        await repo.SaveMeetingAsync(meeting);

        // Act
        await processingService.EnqueueOrProcessAsync(meeting.Id, null, ProcessingMode.DiarizationOnly);

        // Assert
        var saved = await repo.GetMeetingAsync("meeting-2");
        Assert.NotNull(saved);
        Assert.Equal(ProcessingStatus.Completed, saved.ProcessingStatus);
        Assert.Equal(TranscriptionStatus.Completed, saved.TranscriptionStatus);
        Assert.Equal(DiarizationStatus.Completed, saved.DiarizationStatus);

        // El texto, IDs y timestamps deben ser EXACTAMENTE iguales
        Assert.Equal(2, saved.Transcript.Count);
        Assert.Equal("custom-id-1", saved.Transcript[0].Id);
        Assert.Equal("Texto sagrado intocable 1.", saved.Transcript[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(0.5), saved.Transcript[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(3.5), saved.Transcript[0].EndTime);
        Assert.Equal("SPEAKER_00", saved.Transcript[0].SpeakerId);

        Assert.Equal("custom-id-2", saved.Transcript[1].Id);
        Assert.Equal("Texto sagrado intocable 2.", saved.Transcript[1].Text);
        Assert.Equal(TimeSpan.FromSeconds(4.8), saved.Transcript[1].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(8.2), saved.Transcript[1].EndTime);
        Assert.Equal("SPEAKER_01", saved.Transcript[1].SpeakerId);
    }

    [Fact]
    public async Task DiarizationOnly_FailsGracefully_WhenNoTranscriptExists()
    {
        var repo = new MockRepo();
        var asr = new MockTranscriptionService();
        var diar = new MockDiarizationService();
        var aligner = new SpeakerTranscriptAligner();
        var modelMgr = new MockModelManager();
        var processingService = new MeetingProcessingService(repo, asr, modelMgr, diar, aligner);

        var dummyAudio = Path.Combine(_tempDir, "test.wav");
        File.WriteAllText(dummyAudio, "audio");

        var meeting = new Meeting
        {
            Id = "meeting-no-trans",
            AudioFilePath = dummyAudio,
            Transcript = new List<TranscriptSegment>(),
            TranscriptionStatus = TranscriptionStatus.None,
            DiarizationStatus = DiarizationStatus.None
        };
        await repo.SaveMeetingAsync(meeting);

        // Act
        await processingService.EnqueueOrProcessAsync(meeting.Id, null, ProcessingMode.DiarizationOnly);

        // Assert
        var saved = await repo.GetMeetingAsync("meeting-no-trans");
        Assert.NotNull(saved);
        Assert.Equal(ProcessingStatus.Error, saved.ProcessingStatus);
        Assert.Equal(DiarizationStatus.Error, saved.DiarizationStatus);
        Assert.Contains("transcripción previa", saved.DiarizationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TranscriptionAndDiarization_WhenDiarizationFails_PreservesTranscript()
    {
        var repo = new MockRepo();
        var asr = new MockTranscriptionService();
        var diar = new MockDiarizationService();
        diar.CustomDiarize = (_, _, _, _) => throw new InvalidOperationException("Diarization network failure");

        var aligner = new SpeakerTranscriptAligner();
        var modelMgr = new MockModelManager();
        var processingService = new MeetingProcessingService(repo, asr, modelMgr, diar, aligner);

        var dummyAudio = Path.Combine(_tempDir, "test.wav");
        File.WriteAllText(dummyAudio, "audio");

        var meeting = new Meeting
        {
            Id = "meeting-partial",
            AudioFilePath = dummyAudio
        };
        await repo.SaveMeetingAsync(meeting);

        // Act
        await processingService.EnqueueOrProcessAsync(meeting.Id, "Qwen3_06B", ProcessingMode.TranscriptionAndDiarization);

        // Assert
        var saved = await repo.GetMeetingAsync("meeting-partial");
        Assert.NotNull(saved);
        // Transcripción debe estar completada y los textos disponibles
        Assert.Equal(TranscriptionStatus.Completed, saved.TranscriptionStatus);
        Assert.Equal(2, saved.Transcript.Count);
        Assert.Equal("Buenos días a todos.", saved.Transcript[0].Text);

        // Diarización debe reflejar error
        Assert.Equal(DiarizationStatus.Error, saved.DiarizationStatus);
        Assert.Contains("Diarization network failure", saved.DiarizationError);
        Assert.Equal(ProcessingStatus.Error, saved.ProcessingStatus);
    }

    [Fact]
    public void SpeakerReIdentification_PreservesCustomName_WhenVoiceCentroidMatches()
    {
        var aligner = new SpeakerTranscriptAligner();

        var existingSegments = new List<TranscriptSegment>
        {
            new() { Id = "s1", StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(5), SpeakerId = "SPEAKER_00", SpeakerDisplayName = "Joseba", Text = "Primera intervención." },
            new() { Id = "s2", StartTime = TimeSpan.FromSeconds(6), EndTime = TimeSpan.FromSeconds(10), SpeakerId = "SPEAKER_01", SpeakerDisplayName = "Aritz", Text = "Segunda intervención." }
        };

        var existingParticipants = new List<Speaker>
        {
            new() { Id = "SPEAKER_00", DisplayName = "Joseba" },
            new() { Id = "SPEAKER_01", DisplayName = "Aritz" }
        };

        // Guardamos los centroides acústicos anteriores
        var oldCentroids = new Dictionary<string, float[]>
        {
            { "SPEAKER_00", new float[] { 1.0f, 0.0f, 0.0f, 0.0f } }, // Joseba
            { "SPEAKER_01", new float[] { 0.0f, 1.0f, 0.0f, 0.0f } }  // Aritz
        };

        // En la nueva diarización, el cluster de Joseba sale identificado como SPEAKER_01 y el de Aritz como SPEAKER_00
        var newDiarization = new DiarizationResult
        {
            ExclusiveSegments = new List<ExclusiveSpeakerSegment>
            {
                new() { StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(5), SpeakerId = "SPEAKER_01" }, // Ahora SPEAKER_01 es Joseba
                new() { StartTime = TimeSpan.FromSeconds(6), EndTime = TimeSpan.FromSeconds(10), SpeakerId = "SPEAKER_00" } // Ahora SPEAKER_00 es Aritz
            },
            SpeakerCentroids = new Dictionary<string, float[]>
            {
                { "SPEAKER_00", new float[] { 0.0f, 0.98f, 0.0f, 0.0f } }, // Coincide con Aritz (> 0.95 sim)
                { "SPEAKER_01", new float[] { 0.99f, 0.0f, 0.0f, 0.0f } }  // Coincide con Joseba (> 0.95 sim)
            }
        };

        // Act
        var result = aligner.Align(
            existingSegments,
            newDiarization,
            existingParticipants,
            preserveExistingSegments: true,
            existingCentroids: oldCentroids);

        // Assert: Joseba debe asignarse al nuevo SPEAKER_01 y Aritz al nuevo SPEAKER_00
        Assert.Equal("SPEAKER_01", result.ReconciledSegments[0].SpeakerId);
        Assert.Equal("Joseba", result.ReconciledSegments[0].SpeakerDisplayName);

        Assert.Equal("SPEAKER_00", result.ReconciledSegments[1].SpeakerId);
        Assert.Equal("Aritz", result.ReconciledSegments[1].SpeakerDisplayName);
    }

    [Fact]
    public void SpeakerReIdentification_DoesNotTransferName_WhenVoiceIsAmbiguousOrDissimilar()
    {
        var aligner = new SpeakerTranscriptAligner();

        var existingSegments = new List<TranscriptSegment>
        {
            new() { Id = "s1", StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(5), SpeakerId = "SPEAKER_00", SpeakerDisplayName = "Joseba", Text = "Intervención de Joseba." }
        };

        var existingParticipants = new List<Speaker>
        {
            new() { Id = "SPEAKER_00", DisplayName = "Joseba" }
        };

        var oldCentroids = new Dictionary<string, float[]>
        {
            { "SPEAKER_00", new float[] { 1.0f, 0.0f, 0.0f, 0.0f } }
        };

        // En la nueva diarización, el hablante que habla en ese intervalo tiene un centroide ortogonal / diferente (similitud 0.0)
        var newDiarization = new DiarizationResult
        {
            ExclusiveSegments = new List<ExclusiveSpeakerSegment>
            {
                new() { StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(5), SpeakerId = "SPEAKER_05" }
            },
            SpeakerCentroids = new Dictionary<string, float[]>
            {
                { "SPEAKER_05", new float[] { 0.0f, 0.0f, 1.0f, 0.0f } }
            }
        };

        // Act
        var result = aligner.Align(
            existingSegments,
            newDiarization,
            existingParticipants,
            preserveExistingSegments: true,
            existingCentroids: oldCentroids);

        // Assert: No debe transferir "Joseba" al nuevo speaker dudoso
        Assert.NotEqual("Joseba", result.ReconciledSegments[0].SpeakerDisplayName);
    }

    [Fact]
    public async Task Recovery_RecoversDiarizingState_WithoutLosingTranscript()
    {
        var repo = new MockRepo();
        var asr = new MockTranscriptionService();
        var diar = new MockDiarizationService();
        var aligner = new SpeakerTranscriptAligner();
        var modelMgr = new MockModelManager();
        var processingService = new MeetingProcessingService(repo, asr, modelMgr, diar, aligner);

        var meeting = new Meeting
        {
            Id = "meeting-crash",
            ProcessingStatus = ProcessingStatus.Processing,
            TranscriptionStatus = TranscriptionStatus.Completed,
            DiarizationStatus = DiarizationStatus.Diarizing,
            Transcript = new List<TranscriptSegment>
            {
                new() { Id = "s1", Text = "Transcripción ya guardada y segura." }
            }
        };
        await repo.SaveMeetingAsync(meeting);

        // Act: simular arranque de la aplicación tras caída inesperada
        await processingService.RecoverInterruptedJobsAsync();

        // Assert
        var saved = await repo.GetMeetingAsync("meeting-crash");
        Assert.NotNull(saved);
        Assert.Equal(ProcessingStatus.Pending, saved.ProcessingStatus);
        Assert.Equal(TranscriptionStatus.Completed, saved.TranscriptionStatus);
        Assert.Equal(DiarizationStatus.Pending, saved.DiarizationStatus);
        Assert.Single(saved.Transcript);
        Assert.Equal("Transcripción ya guardada y segura.", saved.Transcript[0].Text);
    }

    [Fact]
    public void Meeting_EnsureCompatibility_UpgradesLegacyMeetingsCorrectly()
    {
        var legacyMeeting = new Meeting
        {
            Id = "legacy-1",
            Transcript = new List<TranscriptSegment>
            {
                new() { Id = "s1", Text = "Texto antiguo", SpeakerId = "SPEAKER_00" }
            },
            Participants = new List<Speaker>
            {
                new() { Id = "SPEAKER_00", DisplayName = "Hablante 1" }
            },
            TranscriptionStatus = TranscriptionStatus.None,
            DiarizationStatus = DiarizationStatus.None
        };

        // Act
        legacyMeeting.EnsureCompatibility();

        // Assert
        Assert.Equal(TranscriptionStatus.Completed, legacyMeeting.TranscriptionStatus);
        Assert.Equal(DiarizationStatus.Completed, legacyMeeting.DiarizationStatus);
    }

    [Fact]
    public void UserSettings_EnableDiarizationOnTranscription_DefaultsToTrue()
    {
        var settings = new UserSettings();
        Assert.True(settings.EnableDiarizationOnTranscription);
    }
}
