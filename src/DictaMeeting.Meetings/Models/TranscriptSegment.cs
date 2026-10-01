namespace DictaMeeting.Meetings.Models;

/// <summary>
/// Representa un segmento de habla con marcas de tiempo, texto reconocido e identificación de hablante.
/// </summary>
public class TranscriptSegment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string SpeakerId { get; set; } = string.Empty; // e.g. "SPEAKER_00"
    public string SpeakerDisplayName { get; set; } = string.Empty; // e.g. "Aritz Villodas"
    public string Text { get; set; } = string.Empty;
    public float? Confidence { get; set; }
    public bool IsFinal { get; set; }

    public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;

    public string FormattedStartTime => $"{(int)StartTime.TotalHours:D2}:{StartTime.Minutes:D2}:{StartTime.Seconds:D2}";
}
