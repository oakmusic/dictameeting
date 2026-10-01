using DictaMeeting.Diarization.Interfaces;
using DictaMeeting.Diarization.Models;
using DictaMeeting.Meetings.Models;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.Diarization.Pipeline;

/// <summary>
/// Reconciliador y alineador temporal entre la transcripción textual de Qwen3-ASR
/// y los segmentos de diarización exclusiva de PyAnnote Community-1.
/// Asigna la identidad técnica (SPEAKER_00, SPEAKER_01...) y preserva los nombres
/// de participantes asignados por el usuario.
/// </summary>
public sealed class SpeakerTranscriptAligner : ISpeakerTranscriptAligner
{
    private readonly ILogger<SpeakerTranscriptAligner>? _logger;

    public SpeakerTranscriptAligner(ILogger<SpeakerTranscriptAligner>? logger = null)
    {
        _logger = logger;
    }

    public TranscriptAlignmentResult Align(
        IReadOnlyList<TranscriptSegment> asrSegments,
        DiarizationResult diarizationResult,
        IReadOnlyList<Speaker>? existingParticipants = null)
    {
        var result = new TranscriptAlignmentResult();
        var displayNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Mapeo base de participantes existentes previamente asignados
        if (existingParticipants != null)
        {
            foreach (var participant in existingParticipants)
            {
                if (!string.IsNullOrWhiteSpace(participant.Id) && !string.IsNullOrWhiteSpace(participant.DisplayName))
                {
                    displayNameMap[participant.Id] = participant.DisplayName;
                }
            }
        }

        // Si no hay segmentos de diarización exclusiva, mantener hablantes actuales o SPEAKER_00
        var exclusiveSpans = diarizationResult?.ExclusiveSegments ?? Array.Empty<ExclusiveSpeakerSegment>();
        if (exclusiveSpans.Count == 0)
        {
            var fallbackList = new List<TranscriptSegment>(asrSegments.Count);
            foreach (var asr in asrSegments)
            {
                string speakerId = !string.IsNullOrWhiteSpace(asr.SpeakerId) ? asr.SpeakerId : "SPEAKER_00";
                string displayName = displayNameMap.TryGetValue(speakerId, out var name)
                    ? name
                    : (!string.IsNullOrWhiteSpace(asr.SpeakerDisplayName) ? asr.SpeakerDisplayName : speakerId);

                fallbackList.Add(new TranscriptSegment
                {
                    Id = asr.Id,
                    StartTime = asr.StartTime,
                    EndTime = asr.EndTime,
                    SpeakerId = speakerId,
                    SpeakerDisplayName = displayName,
                    Text = asr.Text,
                    Confidence = asr.Confidence,
                    IsFinal = asr.IsFinal
                });
            }

            result.ReconciledSegments = fallbackList;
            result.SpeakerIdToDisplayNameMap = displayNameMap;
            return result;
        }

        // 2. Alinear cada segmento de texto de ASR con el hablante exclusivo de mayor solapamiento
        var reconciledList = new List<TranscriptSegment>();
        string? previousAssignedSpeaker = null;

        foreach (var asr in asrSegments)
        {
            double asrStartSec = asr.StartTime.TotalSeconds;
            double asrEndSec = asr.EndTime.TotalSeconds;
            double asrDuration = Math.Max(0.01, asrEndSec - asrStartSec);

            // Calcular solapamiento temporal con cada hablante exclusivo en este intervalo
            var speakerOverlapDurations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var span in exclusiveSpans)
            {
                double spanStartSec = span.StartTime.TotalSeconds;
                double spanEndSec = span.EndTime.TotalSeconds;

                double overlapStart = Math.Max(asrStartSec, spanStartSec);
                double overlapEnd = Math.Min(asrEndSec, spanEndSec);

                if (overlapEnd > overlapStart)
                {
                    double dur = overlapEnd - overlapStart;
                    speakerOverlapDurations[span.SpeakerId] = speakerOverlapDurations.GetValueOrDefault(span.SpeakerId) + dur;
                }
            }

            // 2.A Si hay 2 o más interlocutores con solapamiento relevante y el texto contiene diálogo o múltiples oraciones
            if (speakerOverlapDurations.Count >= 2 && asrDuration >= 1.5)
            {
                var sentenceParts = SplitIntoSentenceOrDialogueParts(asr.Text);
                if (sentenceParts.Count >= 2)
                {
                    double totalChars = sentenceParts.Sum(p => Math.Max(1, p.Length));
                    double currentStart = asrStartSec;

                    foreach (var part in sentenceParts)
                    {
                        double partDuration = asrDuration * ((double)part.Length / totalChars);
                        double partEnd = Math.Min(asrEndSec, currentStart + partDuration);

                        string partSpeaker = FindBestSpeakerForInterval(currentStart, partEnd, exclusiveSpans) ?? previousAssignedSpeaker ?? "SPEAKER_00";
                        previousAssignedSpeaker = partSpeaker;

                        string partDisplayName = displayNameMap.TryGetValue(partSpeaker, out var name) && !string.IsNullOrWhiteSpace(name)
                            ? name
                            : partSpeaker;

                        reconciledList.Add(new TranscriptSegment
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            StartTime = TimeSpan.FromSeconds(currentStart),
                            EndTime = TimeSpan.FromSeconds(partEnd),
                            SpeakerId = partSpeaker,
                            SpeakerDisplayName = partDisplayName,
                            Text = part,
                            Confidence = asr.Confidence,
                            IsFinal = asr.IsFinal
                        });

                        currentStart = partEnd;
                    }

                    continue;
                }
            }

            string assignedSpeaker;

            if (speakerOverlapDurations.Count > 0)
            {
                // Seleccionar el hablante con mayor tiempo acumulado de solapamiento
                var bestCandidate = speakerOverlapDurations.OrderByDescending(kv => kv.Value).First();

                // Umbral de protección contra micro-jitter temporal:
                // Si el mejor candidato tiene menos del 35% de solapamiento con la frase
                // y el hablante anterior está activo cerca, favorecer continuidad.
                if (bestCandidate.Value / asrDuration < 0.35 && previousAssignedSpeaker != null &&
                    speakerOverlapDurations.ContainsKey(previousAssignedSpeaker))
                {
                    assignedSpeaker = previousAssignedSpeaker;
                }
                else
                {
                    assignedSpeaker = bestCandidate.Key;
                }
            }
            else
            {
                // Si no hay solapamiento exacto (p. ej. pausas de margen o audio marginal),
                // buscar el segmento de diarización más cercano en el tiempo (< 1.5s)
                string? nearestSpeaker = FindNearestSpeaker(asrStartSec, asrEndSec, exclusiveSpans, maxDistanceSec: 1.5);
                assignedSpeaker = nearestSpeaker ?? previousAssignedSpeaker ?? "SPEAKER_00";
            }

            previousAssignedSpeaker = assignedSpeaker;

            string displayName = displayNameMap.TryGetValue(assignedSpeaker, out var customName) && !string.IsNullOrWhiteSpace(customName)
                ? customName
                : assignedSpeaker;

            reconciledList.Add(new TranscriptSegment
            {
                Id = asr.Id,
                StartTime = asr.StartTime,
                EndTime = asr.EndTime,
                SpeakerId = assignedSpeaker,
                SpeakerDisplayName = displayName,
                Text = asr.Text,
                Confidence = asr.Confidence,
                IsFinal = asr.IsFinal
            });
        }

        result.ReconciledSegments = reconciledList;
        result.SpeakerIdToDisplayNameMap = displayNameMap;
        return result;
    }

    private static List<string> SplitIntoSentenceOrDialogueParts(string text)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        // Comprobar marcas de diálogo con guión (e.g. "-Yo no puedo con tres. -¿Un tres?")
        if (text.Contains(" -") || text.Contains("\n-") || (text.StartsWith("-") && text.IndexOf("-", 1) > 0))
        {
            var rawParts = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?\s])\s*-\s*");
            foreach (var p in rawParts)
            {
                var clean = p.Trim();
                if (clean.StartsWith("-")) clean = clean.TrimStart('-', ' ').Trim();
                if (!string.IsNullOrWhiteSpace(clean))
                {
                    list.Add(clean);
                }
            }
            if (list.Count >= 2) return list;
            list.Clear();
        }

        // Si no son guiones, comprobar división por frases (. / ? / ! / \n)
        var sentences = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?])\s+|\n+");
        foreach (var s in sentences)
        {
            var clean = s.Trim();
            if (!string.IsNullOrWhiteSpace(clean))
            {
                list.Add(clean);
            }
        }

        return list;
    }

    private static string? FindBestSpeakerForInterval(double startSec, double endSec, IReadOnlyList<ExclusiveSpeakerSegment> spans)
    {
        var overlap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var span in spans)
        {
            double sStart = span.StartTime.TotalSeconds;
            double sEnd = span.EndTime.TotalSeconds;
            double oStart = Math.Max(startSec, sStart);
            double oEnd = Math.Min(endSec, sEnd);
            if (oEnd > oStart)
            {
                double d = oEnd - oStart;
                overlap[span.SpeakerId] = overlap.GetValueOrDefault(span.SpeakerId) + d;
            }
        }

        if (overlap.Count > 0)
        {
            return overlap.OrderByDescending(kv => kv.Value).First().Key;
        }

        return FindNearestSpeaker(startSec, endSec, spans, 2.0);
    }

    private static string? FindNearestSpeaker(double startSec, double endSec, IReadOnlyList<ExclusiveSpeakerSegment> spans, double maxDistanceSec)
    {
        string? nearest = null;
        double minDistance = maxDistanceSec;

        foreach (var s in spans)
        {
            double dist;
            if (s.EndTime.TotalSeconds <= startSec)
            {
                dist = startSec - s.EndTime.TotalSeconds;
            }
            else if (s.StartTime.TotalSeconds >= endSec)
            {
                dist = s.StartTime.TotalSeconds - endSec;
            }
            else
            {
                return s.SpeakerId;
            }

            if (dist < minDistance)
            {
                minDistance = dist;
                nearest = s.SpeakerId;
            }
        }

        return nearest;
    }
}
