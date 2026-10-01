using System.Text;
using System.Text.Json;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;

namespace DictaMeeting.Meetings.Exporters;

public class DocumentExporter : IDocumentExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string ExportToMarkdown(Meeting meeting)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {meeting.Title}");
        sb.AppendLine();

        sb.AppendLine($"**Fecha:** {meeting.Date:dd/MM/yyyy}");
        if (meeting.StartTime.HasValue)
        {
            sb.AppendLine($"**Hora:** {meeting.StartTime.Value:HH:mm}");
        }

        var duration = meeting.Duration;
        sb.AppendLine($"**Duración:** {(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}");

        if (!string.IsNullOrWhiteSpace(meeting.Organizer))
        {
            sb.AppendLine($"**Organizador:** {meeting.Organizer}");
        }

        if (!string.IsNullOrWhiteSpace(meeting.Company))
        {
            sb.AppendLine($"**Empresa:** {meeting.Company}");
        }

        sb.AppendLine();
        sb.AppendLine("## Participantes");
        sb.AppendLine();

        var userParticipants = meeting.GetUserParticipants();
        if (userParticipants.Count > 0)
        {
            foreach (var name in userParticipants)
            {
                sb.AppendLine($"- {name}");
            }
        }
        else if (meeting.Participants.Count > 0)
        {
            foreach (var p in meeting.Participants)
            {
                var name = string.IsNullOrWhiteSpace(p.DisplayName) ? p.Id : p.DisplayName;
                sb.AppendLine($"- {name}");
            }
        }
        else
        {
            sb.AppendLine("_Sin participantes registrados._");
        }

        sb.AppendLine();
        sb.AppendLine("## Transcripción");
        sb.AppendLine();

        if (meeting.Transcript.Count > 0)
        {
            foreach (var segment in meeting.Transcript)
            {
                var time = segment.FormattedStartTime;
                var speaker = string.IsNullOrWhiteSpace(segment.SpeakerDisplayName)
                    ? (string.IsNullOrWhiteSpace(segment.SpeakerId) ? "Hablante" : segment.SpeakerId)
                    : segment.SpeakerDisplayName;

                sb.AppendLine($"### {time} — {speaker}");
                sb.AppendLine();
                sb.AppendLine(segment.Text);
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("_Transcripción vacía._");
        }

        if (!string.IsNullOrWhiteSpace(meeting.ActaMarkdown))
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine(meeting.ActaMarkdown);
        }

        return sb.ToString();
    }

    public string ExportToPlainText(Meeting meeting)
    {
        var sb = new StringBuilder();

        sb.AppendLine(meeting.Title);
        sb.AppendLine(new string('=', meeting.Title.Length));
        sb.AppendLine($"Fecha: {meeting.Date:dd/MM/yyyy}");
        if (meeting.StartTime.HasValue)
        {
            sb.AppendLine($"Hora: {meeting.StartTime.Value:HH:mm}");
        }

        var duration = meeting.Duration;
        sb.AppendLine($"Duración: {(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}");

        if (!string.IsNullOrWhiteSpace(meeting.Organizer))
        {
            sb.AppendLine($"Organizador: {meeting.Organizer}");
        }

        sb.AppendLine();
        sb.AppendLine("PARTICIPANTES:");
        var userParticipants = meeting.GetUserParticipants();
        if (userParticipants.Count > 0)
        {
            foreach (var name in userParticipants)
            {
                sb.AppendLine($"- {name}");
            }
        }
        else
        {
            foreach (var p in meeting.Participants)
            {
                var name = string.IsNullOrWhiteSpace(p.DisplayName) ? p.Id : p.DisplayName;
                sb.AppendLine($"- {name}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("TRANSCRIPCIÓN:");
        sb.AppendLine(new string('-', 30));

        foreach (var segment in meeting.Transcript)
        {
            var time = segment.FormattedStartTime;
            var speaker = string.IsNullOrWhiteSpace(segment.SpeakerDisplayName)
                ? (string.IsNullOrWhiteSpace(segment.SpeakerId) ? "Hablante" : segment.SpeakerId)
                : segment.SpeakerDisplayName;

            sb.AppendLine($"[{time}] {speaker}:");
            sb.AppendLine(segment.Text);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public string ExportSummaryToPlainText(Meeting meeting)
    {
        var sb = new StringBuilder();

        sb.AppendLine(meeting.Title);
        sb.AppendLine(new string('=', meeting.Title.Length));
        sb.AppendLine($"Fecha: {meeting.Date:dd/MM/yyyy}");
        if (meeting.StartTime.HasValue)
        {
            sb.AppendLine($"Hora: {meeting.StartTime.Value:HH:mm}");
        }

        var duration = meeting.Duration;
        sb.AppendLine($"Duración: {(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}");

        if (!string.IsNullOrWhiteSpace(meeting.Organizer))
        {
            sb.AppendLine($"Organizador: {meeting.Organizer}");
        }

        sb.AppendLine();
        sb.AppendLine("RESUMEN DE LA REUNIÓN:");
        sb.AppendLine(new string('-', 30));

        if (meeting.SummarySegments != null && meeting.SummarySegments.Count > 0)
        {
            foreach (var card in meeting.SummarySegments)
            {
                var timeHeader = !string.IsNullOrWhiteSpace(card.FormattedTimeRange)
                    ? $"[{card.FormattedTimeRange}]"
                    : $"[{card.StartTime:mm\\:ss} - {card.EndTime:mm\\:ss}]";

                sb.AppendLine(timeHeader);
                sb.AppendLine(card.Text.Trim());
                sb.AppendLine();
            }
        }
        else if (!string.IsNullOrWhiteSpace(meeting.SummaryText))
        {
            sb.AppendLine(meeting.SummaryText.Trim());
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("_Sin resumen disponible._");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public string ExportToJson(Meeting meeting)
    {
        return JsonSerializer.Serialize(meeting, JsonOptions);
    }
}
