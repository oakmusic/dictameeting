using DictaMeeting.AI.Models;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Representa un fragmento acotado de transcripción con marcas temporales y contexto de oradores.
/// </summary>
public class TranscriptChunk
{
    public int Index { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string TimeRange => $"{FormatTime(StartTime)} - {FormatTime(EndTime)}";
    public List<TranscriptSegment> Segments { get; set; } = new();
    public int EstimatedTokens { get; set; }

    private static string FormatTime(TimeSpan ts)
    {
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
}

/// <summary>
/// Servicio de segmentación inteligente de transcripciones para reuniones largas.
/// Divide la transcripción respetando límites naturales de locución, marcas de tiempo y
/// manteniendo una pequeña superposición de contexto para evitar pérdida de acuerdos o decisiones.
/// </summary>
public static class TranscriptChunker
{
    public const int DefaultTargetTokensPerChunk = 5000;
    public const int DefaultOverlapSegments = 2;
    public static readonly TimeSpan DefaultTargetDurationPerChunk = TimeSpan.FromMinutes(25);

    /// <summary>
    /// Estimación rápida y conservadora del número de tokens para texto en español o inglés.
    /// Aproximadamente 3.8 caracteres por token en promedio.
    /// </summary>
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / 3.8);
    }

    /// <summary>
    /// Estima los tokens totales de una lista de segmentos de transcripción incluyendo metadatos de locutor y tiempos.
    /// </summary>
    public static int EstimateTranscriptTokens(IReadOnlyList<TranscriptSegment>? segments)
    {
        if (segments == null || segments.Count == 0) return 0;

        int totalChars = 0;
        foreach (var s in segments)
        {
            totalChars += 15; // marcas [00:00:00]
            totalChars += (s.SpeakerDisplayName?.Length ?? s.SpeakerId?.Length ?? 10) + 2;
            totalChars += (s.Text?.Length ?? 0) + 2;
        }

        return (int)Math.Ceiling(totalChars / 3.8);
    }

    /// <summary>
    /// Determina si una reunión debe procesarse mediante la estrategia jerárquica por etapas
    /// o si es seguro utilizar la generación directa en una única llamada.
    /// </summary>
    public static bool ShouldUseHierarchicalGeneration(Meeting meeting, ActaGenerationOptions options)
    {
        if (options.DisableHierarchicalProcessing == true) return false;
        if (options.ForceHierarchicalProcessing == true) return true;

        if (meeting.Transcript == null || meeting.Transcript.Count == 0) return false;

        // 1. Duración de la reunión (>= 60 min general, o >= 45 min con detalle alto)
        if (meeting.Duration >= TimeSpan.FromMinutes(60)) return true;
        if (meeting.Duration >= TimeSpan.FromMinutes(45) &&
            (options.DetailLevel == ActaDetailLevel.Detallada || options.DetailLevel == ActaDetailLevel.Exhaustiva))
        {
            return true;
        }

        // 2. Volumen estimado de tokens en la transcripción (>= 12.000 general, o >= 8.000 con detalle alto)
        int estimatedTokens = EstimateTranscriptTokens(meeting.Transcript);
        if (estimatedTokens >= 12000) return true;
        if (estimatedTokens >= 8000 &&
            (options.DetailLevel == ActaDetailLevel.Detallada || options.DetailLevel == ActaDetailLevel.Exhaustiva))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Divide la transcripción de una reunión en fragmentos manejables respetando turnos de habla y marcas temporales.
    /// </summary>
    public static IReadOnlyList<TranscriptChunk> ChunkTranscript(
        Meeting meeting,
        int targetTokensPerChunk = DefaultTargetTokensPerChunk,
        TimeSpan? targetDurationPerChunk = null,
        int overlapSegments = DefaultOverlapSegments)
    {
        var result = new List<TranscriptChunk>();
        var segments = meeting.Transcript;

        if (segments == null || segments.Count == 0)
        {
            return result;
        }

        int totalEstimatedTokens = EstimateTranscriptTokens(segments);
        var targetDuration = targetDurationPerChunk ?? DefaultTargetDurationPerChunk;

        // Si el total cabe holgadamente en un único fragmento, devolverlo directamente
        if (totalEstimatedTokens <= targetTokensPerChunk && meeting.Duration <= targetDuration)
        {
            var singleChunk = new TranscriptChunk
            {
                Index = 0,
                StartTime = segments[0].StartTime,
                EndTime = segments[^1].EndTime,
                Segments = new List<TranscriptSegment>(segments),
                EstimatedTokens = totalEstimatedTokens
            };
            result.Add(singleChunk);
            return result;
        }

        int currentIndex = 0;
        int chunkIndex = 0;
        int totalSegments = segments.Count;

        while (currentIndex < totalSegments)
        {
            var chunkSegments = new List<TranscriptSegment>();
            int accumulatedTokens = 0;
            var chunkStartTime = segments[currentIndex].StartTime;
            int segmentStart = currentIndex;

            while (currentIndex < totalSegments)
            {
                var segment = segments[currentIndex];
                int segTokens = EstimateTokens($"[{segment.FormattedStartTime}] {segment.SpeakerDisplayName}: {segment.Text}");

                chunkSegments.Add(segment);
                accumulatedTokens += segTokens;
                currentIndex++;

                var currentDuration = segment.EndTime - chunkStartTime;

                // Criterio de corte: alcanzar presupuesto de tokens o de duración,
                // asegurando que al menos haya 5 segmentos o que queden pocos segmentos para el final.
                bool tokenLimitReached = accumulatedTokens >= targetTokensPerChunk;
                bool durationLimitReached = currentDuration >= targetDuration;

                if ((tokenLimitReached || durationLimitReached) && (totalSegments - currentIndex) >= 3)
                {
                    break;
                }
            }

            var chunkEndTime = chunkSegments[^1].EndTime;

            var chunk = new TranscriptChunk
            {
                Index = chunkIndex++,
                StartTime = chunkStartTime,
                EndTime = chunkEndTime,
                Segments = chunkSegments,
                EstimatedTokens = accumulatedTokens
            };
            result.Add(chunk);

            // Si aún quedan segmentos, aplicar solapamiento controlado para mantener continuidad contextual
            if (currentIndex < totalSegments && overlapSegments > 0)
            {
                int overlap = Math.Min(overlapSegments, chunkSegments.Count - 1);
                currentIndex -= overlap;
                // Prevenir bucles infinitos si no hay avance
                if (currentIndex <= segmentStart)
                {
                    currentIndex = segmentStart + 1;
                }
            }
        }

        return result;
    }
}
