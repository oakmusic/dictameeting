using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class MeetingServiceLifecycleTests
{
    private class InMemoryMeetingRepository : IMeetingRepository
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

        public string GetMeetingDirectoryPath(Meeting meeting) => Path.Combine(Path.GetTempPath(), meeting.Id);
        public string GetMeetingFileBaseName(Meeting meeting) => $"{meeting.Title}_{meeting.Date:yyyy-MM-dd_HH-mm}";
        public string GetAudioFilePath(Meeting meeting) => Path.Combine(GetMeetingDirectoryPath(meeting), "audio.mp3");
    }

    [Fact]
    public async Task StartAndStopMeeting_TransitionsThroughExpectedStates()
    {
        // Arrange
        var repo = new InMemoryMeetingRepository();
        var service = new MeetingService(repo);
        var observedStates = new List<MeetingState>();

        service.StateChanged += (_, state) => observedStates.Add(state);

        // Act
        await service.StartMeetingAsync("Sprint Planning", "Aritz", "TechCorp");
        Assert.Equal(MeetingState.Recording, service.CurrentState);

        service.AppendSegment(new TranscriptSegment
        {
            SpeakerId = "SPEAKER_00",
            Text = "Comenzamos la sesión."
        });

        await service.StopMeetingAsync();

        // Assert
        Assert.Equal(MeetingState.Completed, service.CurrentState);
        Assert.Single(repo.SavedMeetings);
        Assert.Equal("Sprint Planning", repo.SavedMeetings[0].Title);
        Assert.Single(repo.SavedMeetings[0].Transcript);

        Assert.Contains(MeetingState.Preparing, observedStates);
        Assert.Contains(MeetingState.Recording, observedStates);
        Assert.Contains(MeetingState.Processing, observedStates);
        Assert.Contains(MeetingState.Finalizing, observedStates);
        Assert.Contains(MeetingState.Completed, observedStates);
    }

    [Fact]
    public async Task AppendSegment_SameSpeaker_DirectAppend_PreservesSegmentsWithoutArtificialConcatenation()
    {
        // Arrange
        var repo = new InMemoryMeetingRepository();
        var service = new MeetingService(repo);
        await service.StartMeetingAsync("Continuous Speech Test");

        int appendedCount = 0;
        int updatedCount = 0;
        service.SegmentAppended += (_, _) => appendedCount++;
        service.SegmentUpdated += (_, _) => updatedCount++;

        // Act - Fragmento 1 (0s a 4s)
        service.AppendSegment(new TranscriptSegment
        {
            Id = "seg-1",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(0),
            EndTime = TimeSpan.FromSeconds(4),
            Text = "Hola a todos,"
        });

        // Act - Fragmento 2 tras 0.4s (4.4s a 8s)
        service.AppendSegment(new TranscriptSegment
        {
            Id = "seg-2",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(4.4),
            EndTime = TimeSpan.FromSeconds(8),
            Text = "vamos a empezar la reunión."
        });

        // Assert: Frases continuas con coma o pausa corta (<3s) se consolidan en una tarjeta legible
        Assert.Equal(1, appendedCount);
        Assert.Equal(1, updatedCount);
        Assert.NotNull(service.CurrentMeeting);
        Assert.Single(service.CurrentMeeting.Transcript);
        Assert.Equal("Hola a todos, vamos a empezar la reunión.", service.CurrentMeeting.Transcript[0].Text);
    }

    [Fact]
    public async Task AppendSegment_SameSpeaker_SlightPause_AppendsSeparateSegments()
    {
        // Arrange
        var repo = new InMemoryMeetingRepository();
        var service = new MeetingService(repo);
        await service.StartMeetingAsync("Paragraph Break Test");

        int appendedCount = 0;
        int updatedCount = 0;
        service.SegmentAppended += (_, _) => appendedCount++;
        service.SegmentUpdated += (_, _) => updatedCount++;

        // Act - Fragmento 1 (0s a 4s) sin punto final (<25 palabras)
        service.AppendSegment(new TranscriptSegment
        {
            Id = "seg-1",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(0),
            EndTime = TimeSpan.FromSeconds(4),
            Text = "Primer punto del orden del día"
        });

        // Act - Fragmento 2 tras 1.8s de pausa (5.8s a 9s)
        service.AppendSegment(new TranscriptSegment
        {
            Id = "seg-2",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(5.8),
            EndTime = TimeSpan.FromSeconds(9),
            Text = "Pasamos ahora a revisar el presupuesto."
        });

        // Assert: Al no tener punto y ser menor a 25 palabras, se consolida fluidamente
        Assert.Equal(1, appendedCount);
        Assert.Equal(1, updatedCount);
        Assert.NotNull(service.CurrentMeeting);
        Assert.Single(service.CurrentMeeting.Transcript);
        Assert.Equal("Primer punto del orden del día Pasamos ahora a revisar el presupuesto.", service.CurrentMeeting.Transcript[0].Text);
    }

    [Fact]
    public async Task AppendSegment_SameSpeaker_PauseOver3Point5Seconds_CreatesNewSegment()
    {
        // Arrange
        var repo = new InMemoryMeetingRepository();
        var service = new MeetingService(repo);
        await service.StartMeetingAsync("Long Pause Test");

        int appendedCount = 0;
        int updatedCount = 0;
        service.SegmentAppended += (_, _) => appendedCount++;
        service.SegmentUpdated += (_, _) => updatedCount++;

        // Act - Fragmento 1 (0s a 4s)
        service.AppendSegment(new TranscriptSegment
        {
            Id = "seg-1",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(0),
            EndTime = TimeSpan.FromSeconds(4),
            Text = "Primera intervención."
        });

        // Act - Fragmento 2 del mismo speaker tras 4 segundos de silencio (8s a 12s) (> 3.5s)
        service.AppendSegment(new TranscriptSegment
        {
            Id = "seg-2",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(8),
            EndTime = TimeSpan.FromSeconds(12),
            Text = "Segunda intervención tras una pausa larga."
        });

        // Assert: Al superar los 3.5 segundos, debe crear una nueva tarjeta
        Assert.Equal(2, appendedCount);
        Assert.Equal(0, updatedCount);
        Assert.NotNull(service.CurrentMeeting);
        Assert.Equal(2, service.CurrentMeeting.Transcript.Count);
        Assert.Equal("Primera intervención.", service.CurrentMeeting.Transcript[0].Text);
        Assert.Equal("Segunda intervención tras una pausa larga.", service.CurrentMeeting.Transcript[1].Text);
    }

    [Fact]
    public async Task AppendSegment_DifferentSpeaker_CreatesNewSegmentEvenWithoutPause()
    {
        // Arrange
        var repo = new InMemoryMeetingRepository();
        var service = new MeetingService(repo);
        await service.StartMeetingAsync("Speaker Change Test");

        int appendedCount = 0;
        service.SegmentAppended += (_, _) => appendedCount++;

        // Act - SPEAKER_00 (0s a 4s)
        service.AppendSegment(new TranscriptSegment
        {
            Id = "seg-1",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(0),
            EndTime = TimeSpan.FromSeconds(4),
            Text = "¿Qué opináis al respecto?"
        });

        // Act - SPEAKER_01 responde inmediatamente (4.1s a 7s)
        service.AppendSegment(new TranscriptSegment
        {
            Id = "seg-2",
            SpeakerId = "SPEAKER_01",
            StartTime = TimeSpan.FromSeconds(4.1),
            EndTime = TimeSpan.FromSeconds(7),
            Text = "A mí me parece una excelente propuesta."
        });

        // Assert: Cambio de participante debe generar obligatoriamente nueva línea
        Assert.Equal(2, appendedCount);
        Assert.NotNull(service.CurrentMeeting);
        Assert.Equal(2, service.CurrentMeeting.Transcript.Count);
        Assert.Equal("SPEAKER_00", service.CurrentMeeting.Transcript[0].SpeakerId);
        Assert.Equal("SPEAKER_01", service.CurrentMeeting.Transcript[1].SpeakerId);
    }

    [Fact]
    public async Task UpdateSpeakerDisplayName_WhenSameNameAsExisting_MergesBothSpeakersIntoOne()
    {
        // Arrange
        var repo = new InMemoryMeetingRepository();
        var service = new MeetingService(repo);
        await service.StartMeetingAsync("Merge Test");

        bool mergeEventFired = false;
        string mergedFrom = "";
        string mergedTo = "";
        service.SpeakerMerged += (_, args) =>
        {
            mergeEventFired = true;
            mergedFrom = args.MergedSpeakerId;
            mergedTo = args.TargetSpeakerId;
        };

        // Participante 1: SPEAKER_00 llamado "Aritz"
        service.AppendSegment(new TranscriptSegment
        {
            Id = "s1",
            SpeakerId = "SPEAKER_00",
            Text = "Intervención de Aritz.",
            StartTime = TimeSpan.FromSeconds(0),
            EndTime = TimeSpan.FromSeconds(3)
        });
        service.UpdateSpeakerDisplayName("SPEAKER_00", "Aritz");

        // Participante 2: SPEAKER_01 inicialmente llamado "SPEAKER_01"
        service.AppendSegment(new TranscriptSegment
        {
            Id = "s2",
            SpeakerId = "SPEAKER_01",
            Text = "Segunda intervención de Aritz pero detectada como otro speaker.",
            StartTime = TimeSpan.FromSeconds(10),
            EndTime = TimeSpan.FromSeconds(14)
        });

        Assert.Equal(2, service.CurrentMeeting!.Participants.Count);

        // Act: El usuario renombra SPEAKER_01 a "Aritz" (mismo nombre que SPEAKER_00)
        service.UpdateSpeakerDisplayName("SPEAKER_01", "Aritz");

        // Assert: Debe fusionar ambos participantes en 1 solo
        Assert.True(mergeEventFired);
        Assert.Equal("SPEAKER_01", mergedFrom);
        Assert.Equal("SPEAKER_00", mergedTo);
        Assert.Single(service.CurrentMeeting.Participants);
        Assert.Equal("Aritz", service.CurrentMeeting.Participants[0].DisplayName);

        // Todos los segmentos deben estar unificados bajo SPEAKER_00
        Assert.Equal(2, service.CurrentMeeting.Transcript.Count);
        Assert.All(service.CurrentMeeting.Transcript, seg =>
        {
            Assert.Equal("SPEAKER_00", seg.SpeakerId);
            Assert.Equal("Aritz", seg.SpeakerDisplayName);
        });

        // Si llega un nuevo segmento para SPEAKER_01, debe asignarse automáticamente a SPEAKER_00
        service.AppendSegment(new TranscriptSegment
        {
            Id = "s3",
            SpeakerId = "SPEAKER_01",
            Text = "Tercera intervención en vivo.",
            StartTime = TimeSpan.FromSeconds(20),
            EndTime = TimeSpan.FromSeconds(24)
        });

        Assert.Equal("SPEAKER_00", service.CurrentMeeting.Transcript[2].SpeakerId);
        Assert.Equal("Aritz", service.CurrentMeeting.Transcript[2].SpeakerDisplayName);
    }

    [Fact]
    public async Task StartMeetingAsync_CapturesComputerClockDateAndTime()
    {
        var repo = new InMemoryMeetingRepository();
        var service = new MeetingService(repo);

        var before = DateTime.Now.AddSeconds(-1);
        await service.StartMeetingAsync("Reunión de Prueba");
        var after = DateTime.Now.AddSeconds(1);

        Assert.NotNull(service.CurrentMeeting);
        Assert.InRange(service.CurrentMeeting.Date, before, after);
        Assert.NotEqual(TimeSpan.Zero, service.CurrentMeeting.Date.TimeOfDay);
        Assert.NotNull(service.CurrentMeeting.StartTime);
    }

    [Fact]
    public async Task Reset_ClearsMeetingAndResetsStateToIdle()
    {
        var repo = new InMemoryMeetingRepository();
        var service = new MeetingService(repo);

        await service.StartMeetingAsync("Reunión Previa");
        Assert.NotNull(service.CurrentMeeting);
        Assert.Equal(MeetingState.Recording, service.CurrentState);

        service.Reset();

        Assert.Null(service.CurrentMeeting);
        Assert.Equal(MeetingState.Idle, service.CurrentState);
    }
}
