namespace DictaMeeting.Meetings.Models;

/// <summary>
/// Representa a un participante de la reunión identificado por la diarización.
/// Mantiene estrictamente separado el ID técnico (SpeakerId) del nombre asignado por el usuario (DisplayName).
/// </summary>
public class Speaker
{
    public string Id { get; set; } = string.Empty; // e.g. "SPEAKER_00"
    public string DisplayName { get; set; } = string.Empty; // e.g. "Aritz Villodas" o "SPEAKER_00"
    public string ColorHex { get; set; } = "#6366F1"; // Color distintivo para UI
    public TimeSpan TotalSpeakingTime { get; set; } = TimeSpan.Zero;
    public int UtteranceCount { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
}
