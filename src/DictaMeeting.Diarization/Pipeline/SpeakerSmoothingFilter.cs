using DictaMeeting.Diarization.Interfaces;
using DictaMeeting.Diarization.Models;

namespace DictaMeeting.Diarization.Pipeline;

/// <summary>
/// Filtro de suavizado temporal para mitigar microsegmentos espurios y fusionar turnos
/// consecutivos del mismo interlocutor separados por breves pausas naturales.
/// </summary>
public sealed class SpeakerSmoothingFilter : ISpeakerSmoothingFilter
{
    public IReadOnlyList<ExclusiveSpeakerSegment> Smooth(
        IReadOnlyList<ExclusiveSpeakerSegment> segments,
        TimeSpan minSegmentDuration,
        TimeSpan mergeGap)
    {
        if (segments == null || segments.Count == 0)
        {
            return Array.Empty<ExclusiveSpeakerSegment>();
        }

        // 1. Filtrar segmentos inválidos o con duración inferior al umbral mínimo
        var filtered = new List<ExclusiveSpeakerSegment>();
        foreach (var seg in segments)
        {
            if (seg.EndTime > seg.StartTime && seg.Duration >= minSegmentDuration && !string.IsNullOrWhiteSpace(seg.SpeakerId))
            {
                filtered.Add(new ExclusiveSpeakerSegment
                {
                    StartTime = seg.StartTime,
                    EndTime = seg.EndTime,
                    SpeakerId = seg.SpeakerId
                });
            }
        }

        if (filtered.Count <= 1)
        {
            return filtered;
        }

        // Ordenar cronológicamente
        filtered.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

        // 2. Fusionar segmentos consecutivos del mismo interlocutor si el gap es menor o igual a mergeGap
        var merged = new List<ExclusiveSpeakerSegment>();
        var current = filtered[0];

        for (int i = 1; i < filtered.Count; i++)
        {
            var next = filtered[i];

            if (string.Equals(current.SpeakerId, next.SpeakerId, StringComparison.OrdinalIgnoreCase))
            {
                var gap = next.StartTime > current.EndTime ? next.StartTime - current.EndTime : TimeSpan.Zero;
                if (gap <= mergeGap)
                {
                    // Extender el segmento actual hasta el fin del siguiente
                    current.EndTime = next.EndTime > current.EndTime ? next.EndTime : current.EndTime;
                    continue;
                }
            }

            merged.Add(current);
            current = next;
        }

        merged.Add(current);
        return merged;
    }
}
