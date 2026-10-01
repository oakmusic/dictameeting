using DictaMeeting.Meetings.Models;
using Xunit;

namespace DictaMeeting.Tests;

public class SpeakerRenamingTests
{
    [Fact]
    public void UpdateSpeakerDisplayName_UpdatesParticipantsAndAllExistingSegments_PreservingSpeakerId()
    {
        // Arrange
        var meeting = new Meeting { Title = "Reunión de Diseño" };
        var speakerId = "SPEAKER_00";

        meeting.Participants.Add(new Speaker
        {
            Id = speakerId,
            DisplayName = speakerId
        });

        meeting.Transcript.Add(new TranscriptSegment
        {
            Id = "seg-1",
            SpeakerId = speakerId,
            SpeakerDisplayName = speakerId,
            Text = "Buenos días a todos."
        });

        meeting.Transcript.Add(new TranscriptSegment
        {
            Id = "seg-2",
            SpeakerId = speakerId,
            SpeakerDisplayName = speakerId,
            Text = "Continuamos con el orden del día."
        });

        // Act - Renombrado en caliente
        meeting.UpdateSpeakerDisplayName(speakerId, "Aritz Villodas");

        // Assert
        var participant = meeting.Participants.First(p => p.Id == speakerId);
        Assert.Equal("Aritz Villodas", participant.DisplayName);
        Assert.Equal("SPEAKER_00", participant.Id); // Preservado intacto

        Assert.All(meeting.Transcript, segment =>
        {
            Assert.Equal("Aritz Villodas", segment.SpeakerDisplayName);
            Assert.Equal("SPEAKER_00", segment.SpeakerId);
        });
    }

    [Fact]
    public void UpdateSpeakerDisplayName_WhenSpeakerDoesNotExistYet_AddsNewParticipant()
    {
        // Arrange
        var meeting = new Meeting();

        // Act
        meeting.UpdateSpeakerDisplayName("SPEAKER_01", "John Smith");

        // Assert
        var participant = Assert.Single(meeting.Participants);
        Assert.Equal("SPEAKER_01", participant.Id);
        Assert.Equal("John Smith", participant.DisplayName);
    }

    [Fact]
    public async Task MeetingService_AppendSegment_AppliesExistingSpeakerCustomDisplayNameAutomatically()
    {
        // Arrange
        var exporter = new DictaMeeting.Meetings.Exporters.DocumentExporter();
        var repo = new DictaMeeting.Infrastructure.Persistence.LocalFileMeetingRepository(exporter);
        var meetingService = new DictaMeeting.Meetings.Services.MeetingService(repo);
        await meetingService.StartMeetingAsync("Test Meeting");

        // Renombrar antes de la siguiente intervención
        meetingService.UpdateSpeakerDisplayName("SPEAKER_00", "Aritz Villodas");

        // Act: Llega un nuevo segmento atribuido a SPEAKER_00
        var newSegment = new TranscriptSegment
        {
            Id = "seg-new",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(10),
            EndTime = TimeSpan.FromSeconds(14),
            Text = "Nuevo fragmento en vivo."
        };

        meetingService.AppendSegment(newSegment);

        // Assert
        Assert.Equal("Aritz Villodas", newSegment.SpeakerDisplayName);
    }

    [Fact]
    public void SpeakerViewModel_AddSpeakingTime_UpdatesSpeakingTimeFormatted()
    {
        // Arrange
        var vm = new DictaMeeting.App.ViewModels.SpeakerViewModel("SPEAKER_00", "Aritz", "#6366F1");

        // Act
        vm.AddSpeakingTime(TimeSpan.FromSeconds(125)); // 2 min 5 seg

        // Assert
        Assert.Equal("02:05", vm.SpeakingTimeFormatted);
    }

    [Fact]
    public void SpeakerViewModel_AllowsTrailingSpacesWhileTyping_AndTrimsOnCommit()
    {
        // Arrange
        string committedName = string.Empty;
        var vm = new DictaMeeting.App.ViewModels.SpeakerViewModel(
            "SPEAKER_00",
            "Carlos",
            "#6366F1",
            onNameChanged: null,
            onNameCommitted: (_, finalName) => committedName = finalName);

        // Act 1: El usuario teclea espacios al final mientras escribe
        vm.DisplayName = "Carlos ";
        Assert.Equal("Carlos ", vm.DisplayName);

        vm.DisplayName = "Carlos Perez  ";
        Assert.Equal("Carlos Perez  ", vm.DisplayName);

        // Act 2: Finaliza la edición (Enter o LostFocus)
        vm.CommitDisplayName();

        // Assert
        Assert.Equal("Carlos Perez", vm.DisplayName);
        Assert.Equal("Carlos Perez", committedName);
    }

    [Fact]
    public void SpeakerViewModel_CommitDisplayName_WhenWhitespaceOnly_RevertsToId()
    {
        // Arrange
        string committedName = string.Empty;
        var vm = new DictaMeeting.App.ViewModels.SpeakerViewModel(
            "SPEAKER_01",
            "Initial Name",
            "#10B981",
            onNameChanged: null,
            onNameCommitted: (_, finalName) => committedName = finalName);

        // Act: El usuario borra todo o escribe solo espacios y confirma edición
        vm.DisplayName = "    ";
        vm.CommitDisplayName();

        // Assert: Debe revertir al Id del hablante
        Assert.Equal("SPEAKER_01", vm.DisplayName);
        Assert.Equal("SPEAKER_01", committedName);
    }

    [Fact]
    public void SpeakerViewModel_CallsOnNameChangedWhileTyping_AndOnNameCommittedOnCommit()
    {
        // Arrange
        var changes = new List<string>();
        var commits = new List<string>();

        var vm = new DictaMeeting.App.ViewModels.SpeakerViewModel(
            "SPEAKER_00",
            "Initial",
            "#6366F1",
            onNameChanged: (_, name) => changes.Add(name),
            onNameCommitted: (_, name) => commits.Add(name));

        // Act
        vm.DisplayName = "Ana ";
        vm.DisplayName = "Ana Lopez ";
        vm.CommitDisplayName();

        // Assert: onNameChanged capturó los cambios con espacios en vivo
        Assert.Contains("Ana ", changes);
        Assert.Contains("Ana Lopez ", changes);

        // onNameCommitted solo recibió el valor final debidamente recortado
        Assert.Single(commits);
        Assert.Equal("Ana Lopez", commits[0]);
        Assert.Equal("Ana Lopez", vm.DisplayName);
    }
}
