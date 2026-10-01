namespace DictaMeeting.Meetings.Models;

/// <summary>
/// Parámetros técnicos y mapa de archivos asociados a una sesión de reunión grabada.
/// </summary>
public class MeetingSessionParameters
{
    public string? MeetingId { get; set; }
    public string? Title { get; set; }
    public DateTime? Date { get; set; }
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string? LiveModel { get; set; }
    public string? FinalModel { get; set; }
    public string? MicrophoneDevice { get; set; }
    public string? SystemAudioDevice { get; set; }
    public string? Language { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
    public Dictionary<string, string> GeneratedFiles { get; set; } = new();
}
