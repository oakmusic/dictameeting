namespace DictaMeeting.Meetings.Models;

/// <summary>
/// Representa una tarjeta o párrafo de resumen correspondiente a un tramo temporal de la reunión.
/// </summary>
public class SummarySegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? MeetingId { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string FormattedTimeRange { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
}
