using DictaMeeting.Diarization.Models;
using DictaMeeting.Diarization.Pipeline;
using DictaMeeting.Meetings.Models;
using Xunit;

namespace DictaMeeting.Tests;

public class SpeakerTranscriptAlignerTests
{
    [Fact]
    public void Align_WithoutFinalDiarization_PreservesLiveSegmentsAndAssignedNames()
    {
        // Arrange
        var aligner = new SpeakerTranscriptAligner();

        var liveSegments = new List<TranscriptSegment>
        {
            new() { Id = "s1", StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(3), SpeakerId = "SPEAKER_00", SpeakerDisplayName = "Aritz Villodas", Text = "Hola" },
            new() { Id = "s2", StartTime = TimeSpan.FromSeconds(4), EndTime = TimeSpan.FromSeconds(7), SpeakerId = "SPEAKER_01", SpeakerDisplayName = "John Smith", Text = "Hello" }
        };

        var participants = new List<Speaker>
        {
            new() { Id = "SPEAKER_00", DisplayName = "Aritz Villodas" },
            new() { Id = "SPEAKER_01", DisplayName = "John Smith" }
        };

        var diarizationResult = new DiarizationResult();

        // Act
        var result = aligner.Align(liveSegments, diarizationResult, participants);

        // Assert
        Assert.Equal(2, result.ReconciledSegments.Count);
        Assert.Equal("Aritz Villodas", result.ReconciledSegments[0].SpeakerDisplayName);
        Assert.Equal("John Smith", result.ReconciledSegments[1].SpeakerDisplayName);
        Assert.Equal("Aritz Villodas", result.SpeakerIdToDisplayNameMap["SPEAKER_00"]);
    }

    [Fact]
    public void Align_WithFinalDiarizationSpans_MapsFinalSpeakersToAssignedDisplayNames()
    {
        // Arrange
        var aligner = new SpeakerTranscriptAligner();

        var liveSegments = new List<TranscriptSegment>
        {
            new() { Id = "s1", StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(4), SpeakerId = "SPEAKER_00", SpeakerDisplayName = "SPEAKER_00", Text = "Iniciamos la sesión." },
            new() { Id = "s2", StartTime = TimeSpan.FromSeconds(5), EndTime = TimeSpan.FromSeconds(9), SpeakerId = "SPEAKER_01", SpeakerDisplayName = "SPEAKER_01", Text = "Segundo punto del orden del día." }
        };

        var participants = new List<Speaker>
        {
            new() { Id = "SPEAKER_00", DisplayName = "Aritz Villodas" },
            new() { Id = "SPEAKER_01", DisplayName = "Maria Garcia" }
        };

        var diarizationResult = new DiarizationResult
        {
            Duration = TimeSpan.FromSeconds(10),
            DetectedSpeakerCount = 2,
            ExclusiveSegments = new List<ExclusiveSpeakerSegment>
            {
                new() { StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(4.5), SpeakerId = "SPEAKER_00" },
                new() { StartTime = TimeSpan.FromSeconds(4.8), EndTime = TimeSpan.FromSeconds(9.2), SpeakerId = "SPEAKER_01" }
            }
        };

        // Act
        var result = aligner.Align(liveSegments, diarizationResult, participants);

        // Assert
        Assert.Equal(2, result.ReconciledSegments.Count);
        Assert.Equal("Aritz Villodas", result.ReconciledSegments[0].SpeakerDisplayName);
        Assert.Equal("Maria Garcia", result.ReconciledSegments[1].SpeakerDisplayName);
        Assert.Equal("Aritz Villodas", result.SpeakerIdToDisplayNameMap["SPEAKER_00"]);
        Assert.Equal("Maria Garcia", result.SpeakerIdToDisplayNameMap["SPEAKER_01"]);
    }

    [Fact]
    public void Align_MultiSpeakerDialogueInSingleSegment_SplitsIntoDistinctSpeakers()
    {
        // Arrange
        var aligner = new SpeakerTranscriptAligner();

        var liveSegments = new List<TranscriptSegment>
        {
            new()
            {
                Id = "s1",
                StartTime = TimeSpan.FromSeconds(0),
                EndTime = TimeSpan.FromSeconds(10),
                SpeakerId = "SPEAKER_00",
                SpeakerDisplayName = "SPEAKER_00",
                Text = "-Yo no puedo con tres. -¿Un tres? Yo le voy a poner un cinco."
            }
        };

        var participants = new List<Speaker>
        {
            new() { Id = "SPEAKER_00", DisplayName = "Mikel" },
            new() { Id = "SPEAKER_01", DisplayName = "Monica" }
        };

        var diarizationResult = new DiarizationResult
        {
            Duration = TimeSpan.FromSeconds(10),
            DetectedSpeakerCount = 2,
            ExclusiveSegments = new List<ExclusiveSpeakerSegment>
            {
                new() { StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(4), SpeakerId = "SPEAKER_00" },
                new() { StartTime = TimeSpan.FromSeconds(4), EndTime = TimeSpan.FromSeconds(10), SpeakerId = "SPEAKER_01" }
            }
        };

        // Act
        var result = aligner.Align(liveSegments, diarizationResult, participants);

        // Assert: se debe haber dividido en 2 segmentos con sus correspondientes speakers
        Assert.Equal(2, result.ReconciledSegments.Count);
        Assert.Equal("SPEAKER_00", result.ReconciledSegments[0].SpeakerId);
        Assert.Equal("Mikel", result.ReconciledSegments[0].SpeakerDisplayName);
        Assert.Contains("tres", result.ReconciledSegments[0].Text);

        Assert.Equal("SPEAKER_01", result.ReconciledSegments[1].SpeakerId);
        Assert.Equal("Monica", result.ReconciledSegments[1].SpeakerDisplayName);
        Assert.Contains("cinco", result.ReconciledSegments[1].Text);
    }
}
