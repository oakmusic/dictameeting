using DictaMeeting.Meetings.Models;

namespace DictaMeeting.Meetings.Services;

/// <summary>
/// Motor de consolidación natural de frases y fragmentos de transcripción.
/// Agrupa intervenciones consecutivas para evitar tarjetas cortadas de 10-15 palabras,
/// generando párrafos cómodos de leer de ~25 a 50 palabras (y hasta 90 palabras en explicaciones largas).
/// </summary>
public static class TranscriptSegmentConsolidator
{
    private static readonly HashSet<string> IncompleteEndings = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "el", "la", "los", "las", "un", "una", "unos", "unas", "de", "del",
        "que", "y", "e", "o", "u", "pero", "porque", "para", "con", "a", "por",
        "como", "es", "son", "al", "se", "su", "sus", "mi", "mis", "tu", "tus",
        "este", "esta", "estos", "estas", "ese", "esa", "esos", "esas", "aquel"
    };

    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public static bool ShouldMerge(
        TranscriptSegment previous,
        TranscriptSegment incoming,
        double maxGapSeconds = 3.0,
        int targetMaxWords = 50,
        int explanationMaxWords = 90)
    {
        if (previous == null || incoming == null) return false;
        if (string.IsNullOrWhiteSpace(previous.Text) || string.IsNullOrWhiteSpace(incoming.Text)) return false;

        // Verificar mismo interlocutor
        bool sameSpeaker = string.Equals(previous.SpeakerId ?? string.Empty, incoming.SpeakerId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        if (!sameSpeaker && (!string.IsNullOrWhiteSpace(previous.SpeakerId) || !string.IsNullOrWhiteSpace(incoming.SpeakerId)))
        {
            return false;
        }

        // Verificar proximidad temporal
        var gap = (incoming.StartTime - previous.EndTime).TotalSeconds;
        if (gap > maxGapSeconds)
        {
            return false;
        }

        int prevWords = CountWords(previous.Text);
        int incomingWords = CountWords(incoming.Text);
        int totalWords = prevWords + incomingWords;

        // 1. Si el anterior es muy corto (< 20 palabras), siempre fusionar dentro del margen de pausa
        if (prevWords < 20 && totalWords <= explanationMaxWords)
        {
            return true;
        }

        // 2. Si el texto anterior terminó a mitad de frase o el nuevo empieza en minúscula
        bool isIncomplete = IsSentenceIncomplete(previous.Text, incoming.Text);
        if (isIncomplete && totalWords <= explanationMaxWords)
        {
            return true;
        }

        // 3. Si el total combinado no supera el rango cómodo (~30 a 50 palabras)
        if (totalWords <= targetMaxWords)
        {
            return true;
        }

        return false;
    }

    public static bool IsSentenceIncomplete(string prevText, string incomingText)
    {
        if (string.IsNullOrWhiteSpace(prevText)) return false;
        var trimmed = prevText.Trim();

        // No termina con punto, interrogación o exclamación
        char lastChar = trimmed[^1];
        if (lastChar == ',' || lastChar == ':' || lastChar == ';' || lastChar == '-' || lastChar == '–')
        {
            return true;
        }

        if (lastChar != '.' && lastChar != '?' && lastChar != '!' && lastChar != '…')
        {
            return true;
        }

        // Si la siguiente frase empieza con minúscula, es continuación directa
        if (!string.IsNullOrWhiteSpace(incomingText))
        {
            var nextTrimmed = incomingText.Trim();
            if (nextTrimmed.Length > 0 && char.IsLower(nextTrimmed[0]))
            {
                return true;
            }
        }

        // Si termina con punto pero la última palabra es un conector/artículo/preposición (ej. "trabajando en el.", "hacemos es.")
        var withoutPunct = trimmed.TrimEnd('.', '!', '?', '…', ',', ';', ':');
        var words = withoutPunct.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0 && IncompleteEndings.Contains(words[^1]))
        {
            return true;
        }

        return false;
    }

    public static string CombineText(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first)) return second?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(second)) return first.Trim();

        var cleanFirst = first.Trim();
        var cleanSecond = second.Trim();

        // Si el primero terminó en punto artificial pero era incompleto o el siguiente viene en minúscula
        if (cleanFirst.EndsWith('.') && IsSentenceIncomplete(cleanFirst, cleanSecond))
        {
            cleanFirst = cleanFirst.TrimEnd('.');
        }

        return $"{cleanFirst} {cleanSecond}";
    }

    public static List<TranscriptSegment> ConsolidateSegments(IReadOnlyList<TranscriptSegment> segments)
    {
        if (segments == null || segments.Count <= 1) return segments?.ToList() ?? new List<TranscriptSegment>();

        var result = new List<TranscriptSegment>();
        TranscriptSegment? current = null;

        foreach (var seg in segments)
        {
            if (current == null)
            {
                current = new TranscriptSegment
                {
                    Id = seg.Id,
                    StartTime = seg.StartTime,
                    EndTime = seg.EndTime,
                    SpeakerId = seg.SpeakerId,
                    SpeakerDisplayName = seg.SpeakerDisplayName,
                    Text = seg.Text,
                    Confidence = seg.Confidence,
                    IsFinal = seg.IsFinal
                };
                continue;
            }

            if (ShouldMerge(current, seg))
            {
                current.Text = CombineText(current.Text, seg.Text);
                current.EndTime = seg.EndTime > current.EndTime ? seg.EndTime : current.EndTime;
            }
            else
            {
                result.Add(current);
                current = new TranscriptSegment
                {
                    Id = seg.Id,
                    StartTime = seg.StartTime,
                    EndTime = seg.EndTime,
                    SpeakerId = seg.SpeakerId,
                    SpeakerDisplayName = seg.SpeakerDisplayName,
                    Text = seg.Text,
                    Confidence = seg.Confidence,
                    IsFinal = seg.IsFinal
                };
            }
        }

        if (current != null)
        {
            result.Add(current);
        }

        return result;
    }
}
