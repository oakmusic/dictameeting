using System.Text.Json;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Models;
using Xunit;

namespace DictaMeeting.Tests;

public class MeetingSerializationAndExportTests
{
    private readonly DocumentExporter _exporter = new();

    [Fact]
    public void ExportToMarkdown_FollowsRequiredProfessionalFormat()
    {
        // Arrange
        var meeting = new Meeting
        {
            Title = "Project X - Monthly Meeting",
            Organizer = "Aritz Villodas",
            Company = "Company X",
            Date = new DateTime(2026, 9, 21),
            StartTime = new DateTimeOffset(2026, 9, 21, 10, 32, 0, TimeSpan.FromHours(2)),
            EndTime = new DateTimeOffset(2026, 9, 21, 12, 04, 17, TimeSpan.FromHours(2))
        };

        meeting.Participants.Add(new Speaker { Id = "SPEAKER_00", DisplayName = "Aritz Villodas" });
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_01", DisplayName = "John Smith" });

        meeting.Transcript.Add(new TranscriptSegment
        {
            StartTime = TimeSpan.FromMinutes(0),
            EndTime = TimeSpan.FromSeconds(5),
            SpeakerId = "SPEAKER_00",
            SpeakerDisplayName = "Aritz Villodas",
            Text = "Buenos días, empezamos la reunión."
        });

        // Act
        var markdown = _exporter.ExportToMarkdown(meeting);

        // Assert
        Assert.Contains("# Project X - Monthly Meeting", markdown);
        Assert.Contains("**Fecha:** 21/09/2026", markdown);
        Assert.Contains("**Hora:** 10:32", markdown);
        Assert.Contains("## Participantes", markdown);
        Assert.Contains("- Aritz Villodas", markdown);
        Assert.Contains("- John Smith", markdown);
        Assert.Contains("## Transcripción", markdown);
        Assert.Contains("### 00:00:00 — Aritz Villodas", markdown);
        Assert.Contains("Buenos días, empezamos la reunión.", markdown);
    }

    [Fact]
    public void ExportToJson_RoundTripsAccurately()
    {
        // Arrange
        var meeting = new Meeting
        {
            Title = "Sprint Retrospective",
            Organizer = "Scrum Master",
            Date = new DateTime(2026, 9, 21)
        };
        meeting.Participants.Add(new Speaker { Id = "SPEAKER_00", DisplayName = "Dev 1" });
        meeting.Transcript.Add(new TranscriptSegment
        {
            SpeakerId = "SPEAKER_00",
            SpeakerDisplayName = "Dev 1",
            Text = "Todo ha ido sobre ruedas."
        });

        // Act
        var json = _exporter.ExportToJson(meeting);
        var deserialized = JsonSerializer.Deserialize<Meeting>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("Sprint Retrospective", deserialized.Title);
        Assert.Single(deserialized.Participants);
        Assert.Equal("Dev 1", deserialized.Participants[0].DisplayName);
        Assert.Single(deserialized.Transcript);
        Assert.Equal("Todo ha ido sobre ruedas.", deserialized.Transcript[0].Text);
    }
}
