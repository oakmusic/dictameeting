using System.Text.RegularExpressions;
using DictaMeeting.Meetings.Models;

namespace DictaMeeting.Meetings.Services;

/// <summary>
/// Parsea el contenido de un archivo de texto plano hacia la lista interna de
/// <see cref="TranscriptSegment"/> que utiliza DictaMeeting.
///
/// Estrategia de parseo (en orden de prioridad):
///   1. Formato SRT — bloques numerados con timestamps "HH:MM:SS,mmm --> HH:MM:SS,mmm"
///   2. Formato VTT — bloques con timestamps "HH:MM:SS.mmm --> HH:MM:SS.mmm"
///   3. Formato DictaMeeting/export — "[HH:MM:SS] SPEAKER_ID: texto"
///   4. Formato Markdown exportado — "### HH:MM:SS — Nombre\n\ntexto"
///   5. Texto sin estructura — cada párrafo (o línea no vacía) se convierte en un segmento sin speaker ni timestamp
/// </summary>
public static class TranscriptTextParser
{
    // ─── Patrones de reconocimiento ──────────────────────────────────────────

    // [00:01:23] SPEAKER_00: texto
    private static readonly Regex _patternTimestampSpeaker =
        new(@"^\[(?<h>\d+):(?<m>\d{2}):(?<s>\d{2})\]\s+(?<spk>[^\:]+?)\s*:\s*(?<txt>.+)$",
            RegexOptions.Compiled | RegexOptions.Multiline);

    // ### 00:01:23 — Nombre del hablante
    private static readonly Regex _patternMarkdownHeader =
        new(@"^#{1,4}\s+(?<h>\d+):(?<m>\d{2}):(?<s>\d{2})\s+[—\-]\s+(?<spk>.+)$",
            RegexOptions.Compiled | RegexOptions.Multiline);

    // SRT: "HH:MM:SS,mmm --> HH:MM:SS,mmm"
    private static readonly Regex _patternSrtTimestamp =
        new(@"(?<h1>\d+):(?<m1>\d{2}):(?<s1>\d{2}),(?<ms1>\d{3})\s+-->\s+(?<h2>\d+):(?<m2>\d{2}):(?<s2>\d{2}),(?<ms2>\d{3})",
            RegexOptions.Compiled);

    // VTT: "HH:MM:SS.mmm --> HH:MM:SS.mmm" (también acepta MM:SS.mmm)
    private static readonly Regex _patternVttTimestamp =
        new(@"(?:(?<h1>\d+):)?(?<m1>\d{2}):(?<s1>\d{2})\.(?<ms1>\d{3})\s+-->\s+(?:(?<h2>\d+):)?(?<m2>\d{2}):(?<s2>\d{2})\.(?<ms2>\d{3})",
            RegexOptions.Compiled);

    // Nombre de speaker dentro de texto SRT/VTT: "[SPEAKER_00]" o "<v Speaker>"
    private static readonly Regex _patternSpeakerTag =
        new(@"^\[(?<spk>[^\]]+)\]|^<v\s+(?<spk>[^>]+)>",
            RegexOptions.Compiled);

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parsea <paramref name="content"/> y devuelve la lista de segmentos.
    /// Si el contenido está vacío o solo contiene espacios en blanco, devuelve lista vacía.
    /// </summary>
    public static List<TranscriptSegment> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new List<TranscriptSegment>();

        // Intentar cada estrategia en orden, usar la primera que produzca al menos un segmento con texto
        var result = TryParseSrt(content);
        if (result.Count > 0) return result;

        result = TryParseVtt(content);
        if (result.Count > 0) return result;

        result = TryParseTimestampSpeakerLines(content);
        if (result.Count > 0) return result;

        result = TryParseMarkdownTranscript(content);
        if (result.Count > 0) return result;

        // Fallback: texto plano sin estructura
        return ParseAsPlainText(content);
    }

    // ─── SRT ─────────────────────────────────────────────────────────────────

    private static List<TranscriptSegment> TryParseSrt(string content)
    {
        var segments = new List<TranscriptSegment>();

        // Los bloques SRT están separados por líneas en blanco y empiezan con número
        var blocks = Regex.Split(content.Trim(), @"\r?\n\r?\n");
        foreach (var block in blocks)
        {
            var lines = block.Trim().Split('\n');
            if (lines.Length < 2) continue;

            // Línea de timestamps
            string? tsLine = null;
            int textStart = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (_patternSrtTimestamp.IsMatch(lines[i].Trim()))
                {
                    tsLine = lines[i].Trim();
                    textStart = i + 1;
                    break;
                }
            }
            if (tsLine == null || textStart >= lines.Length) continue;

            var tsMatch = _patternSrtTimestamp.Match(tsLine);
            var startTime = ParseTimespan(tsMatch, "h1", "m1", "s1", "ms1");
            var endTime = ParseTimespan(tsMatch, "h2", "m2", "s2", "ms2");

            // El resto son las líneas de texto; puede incluir tag de speaker
            var textLines = lines.Skip(textStart).Select(l => l.Trim()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (textLines.Count == 0) continue;

            string speakerId = string.Empty;
            string speakerDisplayName = string.Empty;

            // Comprobar tag de speaker en primera línea
            var spkMatch = _patternSpeakerTag.Match(textLines[0]);
            if (spkMatch.Success)
            {
                speakerId = spkMatch.Groups["spk"].Value.Trim();
                speakerDisplayName = speakerId;
                textLines[0] = textLines[0].Substring(spkMatch.Length).Trim();
                if (string.IsNullOrWhiteSpace(textLines[0]))
                    textLines.RemoveAt(0);
            }

            var text = string.Join(" ", textLines);
            if (string.IsNullOrWhiteSpace(text)) continue;

            segments.Add(new TranscriptSegment
            {
                StartTime = startTime,
                EndTime = endTime,
                SpeakerId = speakerId,
                SpeakerDisplayName = speakerDisplayName,
                Text = text,
                IsFinal = true
            });
        }

        return segments;
    }

    // ─── VTT ─────────────────────────────────────────────────────────────────

    private static List<TranscriptSegment> TryParseVtt(string content)
    {
        if (!content.TrimStart().StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            return new List<TranscriptSegment>();

        var segments = new List<TranscriptSegment>();
        var blocks = Regex.Split(content.Trim(), @"\r?\n\r?\n");

        foreach (var block in blocks)
        {
            var lines = block.Trim().Split('\n');
            if (lines.Length < 2) continue;

            string? tsLine = null;
            int textStart = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (_patternVttTimestamp.IsMatch(lines[i].Trim()))
                {
                    tsLine = lines[i].Trim();
                    textStart = i + 1;
                    break;
                }
            }
            if (tsLine == null || textStart >= lines.Length) continue;

            var tsMatch = _patternVttTimestamp.Match(tsLine);
            var startTime = ParseVttTimespan(tsMatch, "h1", "m1", "s1", "ms1");
            var endTime = ParseVttTimespan(tsMatch, "h2", "m2", "s2", "ms2");

            var textLines = lines.Skip(textStart).Select(l => StripVttTags(l.Trim())).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (textLines.Count == 0) continue;

            string speakerId = string.Empty;
            string speakerDisplayName = string.Empty;

            var spkMatch = _patternSpeakerTag.Match(textLines[0]);
            if (spkMatch.Success)
            {
                speakerId = spkMatch.Groups["spk"].Value.Trim();
                speakerDisplayName = speakerId;
                textLines[0] = textLines[0].Substring(spkMatch.Length).Trim();
                if (string.IsNullOrWhiteSpace(textLines[0]))
                    textLines.RemoveAt(0);
            }

            var text = string.Join(" ", textLines);
            if (string.IsNullOrWhiteSpace(text)) continue;

            segments.Add(new TranscriptSegment
            {
                StartTime = startTime,
                EndTime = endTime,
                SpeakerId = speakerId,
                SpeakerDisplayName = speakerDisplayName,
                Text = text,
                IsFinal = true
            });
        }

        return segments;
    }

    // ─── Formato DictaMeeting TXT export ─────────────────────────────────────

    private static List<TranscriptSegment> TryParseTimestampSpeakerLines(string content)
    {
        var matches = _patternTimestampSpeaker.Matches(content);
        if (matches.Count == 0) return new List<TranscriptSegment>();

        var segments = new List<TranscriptSegment>();
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var start = new TimeSpan(
                int.Parse(m.Groups["h"].Value),
                int.Parse(m.Groups["m"].Value),
                int.Parse(m.Groups["s"].Value));

            // Estimar fin como el inicio del siguiente segmento (o inicio + 5 s como fallback)
            TimeSpan end = i + 1 < matches.Count
                ? new TimeSpan(
                    int.Parse(matches[i + 1].Groups["h"].Value),
                    int.Parse(matches[i + 1].Groups["m"].Value),
                    int.Parse(matches[i + 1].Groups["s"].Value))
                : start.Add(TimeSpan.FromSeconds(5));

            var spkId = m.Groups["spk"].Value.Trim();
            segments.Add(new TranscriptSegment
            {
                StartTime = start,
                EndTime = end,
                SpeakerId = spkId,
                SpeakerDisplayName = spkId,
                Text = m.Groups["txt"].Value.Trim(),
                IsFinal = true
            });
        }

        return segments;
    }

    // ─── Formato Markdown DictaMeeting (### HH:MM:SS — Nombre) ───────────────

    private static List<TranscriptSegment> TryParseMarkdownTranscript(string content)
    {
        var matches = _patternMarkdownHeader.Matches(content);
        if (matches.Count == 0) return new List<TranscriptSegment>();

        var segments = new List<TranscriptSegment>();
        var lines = content.Split('\n');

        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var start = new TimeSpan(
                int.Parse(m.Groups["h"].Value),
                int.Parse(m.Groups["m"].Value),
                int.Parse(m.Groups["s"].Value));

            TimeSpan end = i + 1 < matches.Count
                ? new TimeSpan(
                    int.Parse(matches[i + 1].Groups["h"].Value),
                    int.Parse(matches[i + 1].Groups["m"].Value),
                    int.Parse(matches[i + 1].Groups["s"].Value))
                : start.Add(TimeSpan.FromSeconds(5));

            // Extraer texto entre este encabezado y el siguiente
            int headerLineIdx = GetLineIndex(lines, m.Index, content);
            int nextHeaderLineIdx = i + 1 < matches.Count
                ? GetLineIndex(lines, matches[i + 1].Index, content)
                : lines.Length;

            var textLines = lines
                .Skip(headerLineIdx + 1)
                .Take(nextHeaderLineIdx - headerLineIdx - 1)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .ToList();

            var text = string.Join(" ", textLines);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var spkId = m.Groups["spk"].Value.Trim();
            segments.Add(new TranscriptSegment
            {
                StartTime = start,
                EndTime = end,
                SpeakerId = spkId,
                SpeakerDisplayName = spkId,
                Text = text,
                IsFinal = true
            });
        }

        return segments;
    }

    // ─── Texto plano sin estructura ───────────────────────────────────────────

    private static List<TranscriptSegment> ParseAsPlainText(string content)
    {
        var segments = new List<TranscriptSegment>();

        // Dividir en párrafos (bloques separados por líneas en blanco)
        var paragraphs = Regex.Split(content.Trim(), @"\r?\n\s*\r?\n")
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        // Si no hay párrafos múltiples, usar líneas individuales
        if (paragraphs.Count == 0)
        {
            paragraphs = content.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
        }

        // Asignar timestamps ficticios incrementales de 5 s por párrafo
        var currentTime = TimeSpan.Zero;
        var increment = TimeSpan.FromSeconds(5);

        foreach (var para in paragraphs)
        {
            var endTime = currentTime + increment;
            segments.Add(new TranscriptSegment
            {
                StartTime = currentTime,
                EndTime = endTime,
                SpeakerId = string.Empty,
                SpeakerDisplayName = string.Empty,
                Text = para,
                IsFinal = true
            });
            currentTime = endTime;
        }

        return segments;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static TimeSpan ParseTimespan(Match m, string hGroup, string mGroup, string sGroup, string msGroup)
    {
        int h = int.Parse(m.Groups[hGroup].Value);
        int min = int.Parse(m.Groups[mGroup].Value);
        int s = int.Parse(m.Groups[sGroup].Value);
        int ms = int.Parse(m.Groups[msGroup].Value);
        return new TimeSpan(0, h, min, s, ms);
    }

    private static TimeSpan ParseVttTimespan(Match m, string hGroup, string mGroup, string sGroup, string msGroup)
    {
        // hGroup puede estar vacío en VTT si el formato es MM:SS.mmm
        int h = m.Groups[hGroup].Success && !string.IsNullOrEmpty(m.Groups[hGroup].Value)
            ? int.Parse(m.Groups[hGroup].Value)
            : 0;
        int min = int.Parse(m.Groups[mGroup].Value);
        int s = int.Parse(m.Groups[sGroup].Value);
        int ms = int.Parse(m.Groups[msGroup].Value);
        return new TimeSpan(0, h, min, s, ms);
    }

    private static string StripVttTags(string line)
    {
        // Eliminar tags HTML básicos de VTT como <b>, <i>, <u>, <c.color>, etc.
        return Regex.Replace(line, @"<[^>]+>", string.Empty);
    }

    private static int GetLineIndex(string[] lines, int charIndex, string fullContent)
    {
        int pos = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            pos += lines[i].Length + 1; // +1 por el \n
            if (pos > charIndex) return i;
        }
        return lines.Length - 1;
    }
}
