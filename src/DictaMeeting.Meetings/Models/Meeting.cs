using DictaMeeting.Meetings.Enums;

namespace DictaMeeting.Meetings.Models;

/// <summary>
/// Modelo de dominio que representa una sesión completa de reunión.
/// </summary>
public class Meeting
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Nueva Reunión";
    public string Organizer { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Now;
    public DateTimeOffset? StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public TimeSpan Duration => EndTime.HasValue && StartTime.HasValue && EndTime >= StartTime
        ? EndTime.Value - StartTime.Value
        : TimeSpan.Zero;

    public MeetingState State { get; set; } = MeetingState.Idle;
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Completed;
    public string? ProcessingStatusText { get; set; }
    public string? ProcessingModel { get; set; }
    public string? ProcessingError { get; set; }
    public double ProcessingProgress { get; set; }

    public string? AudioFilePath { get; set; }
    public bool AudioFileExists() => !string.IsNullOrEmpty(AudioFilePath) && System.IO.File.Exists(AudioFilePath);
    public string? Language { get; set; }
    public string? Notes { get; set; }
    public string? ActaMarkdown { get; set; }

    public List<string> ExpectedParticipants { get; set; } = new();
    public List<Speaker> Participants { get; set; } = new();
    public List<TranscriptSegment> Transcript { get; set; } = new();

    public string? SummaryText { get; set; }
    public List<SummarySegment> SummarySegments { get; set; } = new();

    /// <summary>
    /// Actualiza el nombre visible de un hablante en la lista de participantes y en todos los segmentos de transcripción.
    /// Si el nombre coincide exactamente con el de otro participante existente, los fusiona automáticamente bajo el participante existente.
    /// </summary>
    /// <returns>El ID definitivo del participante tras la actualización o fusión.</returns>
    public string UpdateSpeakerDisplayName(string speakerId, string newDisplayName)
    {
        if (string.IsNullOrWhiteSpace(speakerId)) return speakerId;

        var cleanName = string.IsNullOrWhiteSpace(newDisplayName) ? speakerId : newDisplayName.Trim();

        // 1. Comprobar si ya existe OTRO participante con exactamente el mismo nombre (fusión automática)
        var existingOther = Participants.FirstOrDefault(p =>
            !string.Equals(p.Id, speakerId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.DisplayName, cleanName, StringComparison.OrdinalIgnoreCase));

        if (existingOther != null)
        {
            // FUSIONAR: Reasignar todos los segmentos del hablante renombrado al existente
            foreach (var segment in Transcript.Where(s => string.Equals(s.SpeakerId, speakerId, StringComparison.OrdinalIgnoreCase)))
            {
                segment.SpeakerId = existingOther.Id;
                segment.SpeakerDisplayName = existingOther.DisplayName;
            }

            // Eliminar el participante duplicado
            Participants.RemoveAll(p => string.Equals(p.Id, speakerId, StringComparison.OrdinalIgnoreCase));

            return existingOther.Id;
        }

        // 2. Renombrado estándar sin colisión
        var participant = Participants.FirstOrDefault(p => string.Equals(p.Id, speakerId, StringComparison.OrdinalIgnoreCase));
        if (participant != null)
        {
            participant.DisplayName = cleanName;
        }
        else
        {
            Participants.Add(new Speaker
            {
                Id = speakerId,
                DisplayName = cleanName
            });
        }

        foreach (var segment in Transcript.Where(s => string.Equals(s.SpeakerId, speakerId, StringComparison.OrdinalIgnoreCase)))
        {
            segment.SpeakerDisplayName = cleanName;
        }

        // Si el nombre asignado es un nombre real (no técnico), sincronizarlo también en ExpectedParticipants
        if (!IsTechnicalSpeakerName(cleanName))
        {
            ExpectedParticipants ??= new();
            if (!ExpectedParticipants.Contains(cleanName, StringComparer.OrdinalIgnoreCase))
            {
                ExpectedParticipants.Add(cleanName);
            }
        }

        return speakerId;
    }

    /// <summary>
    /// Devuelve la lista de nombres de participantes introducidos o asignados por el usuario,
    /// combinando ExpectedParticipants y nombres personalizados no técnicos en Participants.
    /// Excluye automáticamente identificadores técnicos de diarización (e.g. SPEAKER_XX, Hablante).
    /// </summary>
    public List<string> GetUserParticipants()
    {
        var names = new List<string>();

        if (ExpectedParticipants != null)
        {
            foreach (var ep in ExpectedParticipants)
            {
                var trimmed = ep?.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) &&
                    !IsTechnicalSpeakerName(trimmed) &&
                    !names.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(trimmed);
                }
            }
        }

        if (Participants != null)
        {
            foreach (var p in Participants)
            {
                var trimmed = p.DisplayName?.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) &&
                    !IsTechnicalSpeakerName(trimmed) &&
                    !names.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(trimmed);
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Comprueba si un texto corresponde a un identificador técnico de diarización
    /// (e.g. SPEAKER_00, speaker_01, Hablante, Locutor).
    /// </summary>
    public static bool IsTechnicalSpeakerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        var trimmed = name.Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(
            trimmed,
            @"^(?:SPEAKER(?:[_\s\-]\d+|\d+)?|HABLANTE(?:[_\s\-]\d+|\d+)?|LOCUTOR(?:[_\s\-]\d+|\d+)?)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public static readonly string[] SpeakerColorPalette = new[]
    {
        "#4F46E5", // Indigo (azul vivo)
        "#059669", // Emerald (verde esmeralda intenso)
        "#D97706", // Amber (ámbar / dorado cálido)
        "#E11D48", // Crimson (rojo carmín)
        "#9333EA", // Violet (púrpura violeta)
        "#0D9488", // Teal (verde azulado / cian profundo)
        "#DB2777", // Pink (rosa intenso)
        "#EA580C"  // Orange (naranja oscuro)
    };

    /// <summary>
    /// Sincroniza la lista de participantes a partir de los interlocutores presentes en la transcripción.
    /// Garantiza que todo speaker que intervenga en la transcripción esté registrado en la lista de participantes.
    /// </summary>
    public void SyncParticipantsFromTranscript()
    {
        var transcriptSpeakerIds = Transcript
            .Select(s => s.SpeakerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        int colorIdx = Participants.Count;
        foreach (var spId in transcriptSpeakerIds)
        {
            var existing = Participants.FirstOrDefault(p => string.Equals(p.Id, spId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                var firstSegment = Transcript.FirstOrDefault(s => string.Equals(s.SpeakerId, spId, StringComparison.OrdinalIgnoreCase));
                var displayName = !string.IsNullOrWhiteSpace(firstSegment?.SpeakerDisplayName) ? firstSegment.SpeakerDisplayName : spId;
                var color = SpeakerColorPalette[colorIdx % SpeakerColorPalette.Length];
                colorIdx++;

                Participants.Add(new Speaker
                {
                    Id = spId,
                    DisplayName = displayName,
                    ColorHex = color
                });
            }
        }
    }
}
