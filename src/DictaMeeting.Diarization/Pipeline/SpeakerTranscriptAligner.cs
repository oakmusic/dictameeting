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
        IReadOnlyList<Speaker>? existingParticipants = null,
        bool preserveExistingSegments = false,
        IReadOnlyDictionary<string, float[]>? existingCentroids = null)
    {
        var result = new TranscriptAlignmentResult();
        var displayNameMap = BuildSpeakerMapping(asrSegments, diarizationResult, existingParticipants, existingCentroids);

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

            // 2.A Si no estamos en modo no destructivo, y hay 2 o más interlocutores con solapamiento relevante
            // y el texto contiene diálogo o múltiples oraciones, dividir el segmento
            if (!preserveExistingSegments && speakerOverlapDurations.Count >= 2 && asrDuration >= 1.5)
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

    /// <summary>
    /// Construye el mapeo de nombres de interlocutores, preservando de forma conservadora
    /// y fiable los nombres asignados previamente por el usuario basándose en similitud acústica y solapamiento temporal.
    /// </summary>
    private Dictionary<string, string> BuildSpeakerMapping(
        IReadOnlyList<TranscriptSegment> asrSegments,
        DiarizationResult? diarizationResult,
        IReadOnlyList<Speaker>? existingParticipants,
        IReadOnlyDictionary<string, float[]>? existingCentroids)
    {
        var displayNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (existingParticipants == null || existingParticipants.Count == 0)
        {
            return displayNameMap;
        }

        // Identificar interlocutores previos con nombres no técnicos asignados por el usuario
        var customOldParticipants = existingParticipants
            .Where(p => !string.IsNullOrWhiteSpace(p.Id) &&
                        !string.IsNullOrWhiteSpace(p.DisplayName) &&
                        !Meeting.IsTechnicalSpeakerName(p.DisplayName))
            .ToList();

        var newSpeakerIds = (diarizationResult?.ExclusiveSegments?
            .Select(s => s.SpeakerId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>());

        // Si no hay nombres personalizados previos o no hay nuevos speakers, usar mapeo directo por ID
        if (customOldParticipants.Count == 0 || newSpeakerIds.Count == 0)
        {
            foreach (var p in existingParticipants)
            {
                if (!string.IsNullOrWhiteSpace(p.Id) && !string.IsNullOrWhiteSpace(p.DisplayName))
                {
                    displayNameMap[p.Id] = p.DisplayName;
                }
            }
            return displayNameMap;
        }

        // Comprobar si se trata de una re-diarización sobre segmentos previamente diarizados
        // o si disponemos de centroides acústicos previos para re-identificación.
        var distinctOldSpeakerIds = asrSegments
            .Select(s => s.SpeakerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool hasCentroids = existingCentroids != null && existingCentroids.Count > 0;
        bool isReDiarization = hasCentroids || distinctOldSpeakerIds.Count >= 2;

        if (!isReDiarization)
        {
            // Primera diarización sobre segmentos en vivo o brutos de ASR: mapeo directo
            foreach (var p in existingParticipants)
            {
                if (!string.IsNullOrWhiteSpace(p.Id) && !string.IsNullOrWhiteSpace(p.DisplayName))
                {
                    displayNameMap[p.Id] = p.DisplayName;
                }
            }
            return displayNameMap;
        }

        // Evaluar correspondencia para cada hablante antiguo personalizado
        var assignedNewSpeakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var oldSpeaker in customOldParticipants)
        {
            string? bestMatchNewSpeaker = null;
            double bestScore = 0.0;
            double secondBestScore = 0.0;

            // Calcular duración total del hablante antiguo en los segmentos de ASR
            var oldSegments = asrSegments
                .Where(s => string.Equals(s.SpeakerId, oldSpeaker.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            double totalOldDuration = oldSegments.Sum(s => Math.Max(0.01, (s.EndTime - s.StartTime).TotalSeconds));

            foreach (var newSpeaker in newSpeakerIds)
            {
                if (assignedNewSpeakers.Contains(newSpeaker)) continue;

                double score = 0.0;
                bool hasAcoustic = false;
                double cosineSim = 0.0;

                // 1. Similitud acústica de centroides si están disponibles
                if (existingCentroids != null &&
                    existingCentroids.TryGetValue(oldSpeaker.Id, out var oldVec) &&
                    diarizationResult?.SpeakerCentroids != null &&
                    diarizationResult.SpeakerCentroids.TryGetValue(newSpeaker, out var newVec))
                {
                    cosineSim = ComputeCosineSimilarity(oldVec, newVec);
                    hasAcoustic = true;
                }

                // 2. Solapamiento temporal (IoU y Recall)
                double overlapSec = 0.0;
                var newSpans = diarizationResult?.ExclusiveSegments?
                    .Where(s => string.Equals(s.SpeakerId, newSpeaker, StringComparison.OrdinalIgnoreCase))
                    .ToList() ?? new List<ExclusiveSpeakerSegment>();
                double totalNewDuration = newSpans.Sum(s => Math.Max(0.01, (s.EndTime - s.StartTime).TotalSeconds));

                if (totalOldDuration > 0 && totalNewDuration > 0)
                {
                    foreach (var oSeg in oldSegments)
                    {
                        double oStart = oSeg.StartTime.TotalSeconds;
                        double oEnd = oSeg.EndTime.TotalSeconds;
                        foreach (var nSeg in newSpans)
                        {
                            double overlap = Math.Max(0.0, Math.Min(oEnd, nSeg.EndTime.TotalSeconds) - Math.Max(oStart, nSeg.StartTime.TotalSeconds));
                            overlapSec += overlap;
                        }
                    }
                }

                double recall = totalOldDuration > 0 ? overlapSec / totalOldDuration : 0.0;
                double unionSec = totalOldDuration + totalNewDuration - overlapSec;
                double iou = unionSec > 0 ? overlapSec / unionSec : 0.0;

                // Calcular puntuación combinada
                if (hasAcoustic)
                {
                    if (iou > 0.0)
                    {
                        score = (0.55 * Math.Max(0.0, cosineSim)) + (0.45 * iou);
                    }
                    else
                    {
                        score = Math.Max(0.0, cosineSim);
                    }
                }
                else
                {
                    score = iou;
                }

                // Verificar si supera los umbrales de fiabilidad
                bool isReliable;
                if (hasAcoustic && iou > 0.15)
                {
                    isReliable = cosineSim >= 0.70 || score >= 0.55;
                }
                else if (hasAcoustic)
                {
                    isReliable = cosineSim >= 0.78;
                }
                else
                {
                    // Solo temporal: requerir solapamiento relevante (> 40% IoU y > 50% recall)
                    isReliable = iou >= 0.40 && recall >= 0.50;
                }

                if (isReliable && score > bestScore)
                {
                    secondBestScore = bestScore;
                    bestScore = score;
                    bestMatchNewSpeaker = newSpeaker;
                }
                else if (score > secondBestScore)
                {
                    secondBestScore = score;
                }
            }

            // Regla de margen para evitar ambigüedades:
            // Debe superar holgadamente al segundo mejor candidato si hay más de uno
            bool clearMargin = (bestScore - secondBestScore) >= 0.12 || secondBestScore < 0.30;
            if (bestMatchNewSpeaker != null && clearMargin)
            {
                _logger?.LogInformation(
                    "Preservando identidad de participante '{DisplayName}' para hablante técnico '{NewSpeaker}' (Score: {Score:F2})",
                    oldSpeaker.DisplayName, bestMatchNewSpeaker, bestScore);
                displayNameMap[bestMatchNewSpeaker] = oldSpeaker.DisplayName;
                assignedNewSpeakers.Add(bestMatchNewSpeaker);
            }
            else
            {
                _logger?.LogWarning(
                    "No se pudo asociar de forma fiable la identidad previa '{DisplayName}' a ningún hablante detectado. Se mantiene sin transferir.",
                    oldSpeaker.DisplayName);
            }
        }

        // Asignar los demás hablantes existentes que no hayan colisionado
        if (existingParticipants != null)
        {
            foreach (var p in existingParticipants)
            {
                if (!string.IsNullOrWhiteSpace(p.Id) && !displayNameMap.ContainsKey(p.Id))
                {
                    displayNameMap[p.Id] = p.DisplayName;
                }
            }
        }

        return displayNameMap;
    }

    private static double ComputeCosineSimilarity(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length == 0 || b.Length == 0 || a.Length != b.Length)
            return 0.0;

        double dot = 0.0;
        double normA = 0.0;
        double normB = 0.0;

        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        double denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom > 1e-9 ? dot / denom : 0.0;
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
