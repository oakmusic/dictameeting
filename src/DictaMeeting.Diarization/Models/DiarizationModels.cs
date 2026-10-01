namespace DictaMeeting.Diarization.Models;

/// <summary>
/// Identificador técnico formal de interlocutor (e.g. SPEAKER_00, SPEAKER_01).
/// No confundir con el nombre de participante editable por el usuario.
/// </summary>
public readonly record struct SpeakerId
{
    public string Value { get; }

    public SpeakerId(string value)
    {
        Value = string.IsNullOrWhiteSpace(value) ? "SPEAKER_00" : value.Trim();
    }

    public static SpeakerId FromIndex(int index) => new($"SPEAKER_{index:D2}");

    public override string ToString() => Value;

    public static implicit operator string(SpeakerId id) => id.Value;
    public static implicit operator SpeakerId(string value) => new(value);
}

/// <summary>
/// Segmento temporal atribuido a un hablante técnico con probabilidad/confianza.
/// </summary>
public class SpeakerSegment
{
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string SpeakerId { get; set; } = string.Empty;
    public float Confidence { get; set; } = 1.0f;

    public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;

    public override string ToString() => $"{StartTime:hh\\:mm\\:ss\\.ff} - {EndTime:hh\\:mm\\:ss\\.ff}: {SpeakerId} ({Confidence:P0})";
}

/// <summary>
/// Segmento de diarización exclusiva (non-overlapping), donde a lo sumo un único interlocutor
/// está activo en cada instante temporal.
/// </summary>
public class ExclusiveSpeakerSegment
{
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string SpeakerId { get; set; } = string.Empty;

    public TimeSpan Duration => EndTime > StartTime ? EndTime - StartTime : TimeSpan.Zero;

    public override string ToString() => $"{StartTime:hh\\:mm\\:ss\\.ff} - {EndTime:hh\\:mm\\:ss\\.ff}: {SpeakerId} [Exclusive]";
}

/// <summary>
/// Embedding acústico de 256 dimensiones extraído de un fragmento de voz.
/// </summary>
public class SpeakerEmbedding
{
    public int ChunkIndex { get; set; }
    public int LocalSpeakerIndex { get; set; }
    public float[] Vector { get; set; } = Array.Empty<float>();
    public float ActiveRatio { get; set; }
}

/// <summary>
/// Opciones de configuración para el pipeline de diarización PyAnnote Community-1.
/// </summary>
public class DiarizationOptions
{
    /// <summary>
    /// Número exacto de interlocutores. Si se especifica, fuerza la partición de clusters.
    /// Por defecto null (autodetección bayesiana con VBx).
    /// </summary>
    public int? NumSpeakers { get; set; }

    /// <summary>
    /// Número mínimo de interlocutores a detectar (por defecto 1).
    /// </summary>
    public int? MinSpeakers { get; set; } = 1;

    /// <summary>
    /// Número máximo de interlocutores a detectar (por defecto 8).
    /// </summary>
    public int? MaxSpeakers { get; set; } = 8;

    /// <summary>
    /// Duración mínima de un segmento de voz para no ser descartado como ruido/glitch (por defecto 250ms).
    /// </summary>
    public TimeSpan MinSegmentDuration { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Intervalo máximo entre segmentos contiguos del mismo hablante para fusionarse (por defecto 500ms).
    /// </summary>
    public TimeSpan MergeGap { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Proveedor de ejecución preferido para ONNX Runtime (CPU, CUDA, DirectML). Por defecto autodetectado.
    /// </summary>
    public string? PreferredExecutionProvider { get; set; }

    /// <summary>
    /// Tamaño de lote para inferencia de segmentación (por defecto 16).
    /// </summary>
    public int BatchSize { get; set; } = 16;
}

/// <summary>
/// Resultado completo de la ejecución de diarización PyAnnote Community-1.
/// </summary>
public class DiarizationResult
{
    /// <summary>
    /// Segmentos estándar con posibilidad de solapamiento (overlap-aware).
    /// </summary>
    public IReadOnlyList<SpeakerSegment> RegularSegments { get; set; } = Array.Empty<SpeakerSegment>();

    /// <summary>
    /// Segmentos de diarización exclusiva (non-overlapping) para reconciliación limpia con ASR.
    /// </summary>
    public IReadOnlyList<ExclusiveSpeakerSegment> ExclusiveSegments { get; set; } = Array.Empty<ExclusiveSpeakerSegment>();

    /// <summary>
    /// Centroides promedio de embeddings (256-D) de cada interlocutor detectado (SPEAKER_00, SPEAKER_01...).
    /// </summary>
    public IReadOnlyDictionary<string, float[]> SpeakerCentroids { get; set; } = new Dictionary<string, float[]>();

    /// <summary>
    /// Número de interlocutores distintos identificados.
    /// </summary>
    public int DetectedSpeakerCount { get; set; }

    /// <summary>
    /// Duración total del audio procesado.
    /// </summary>
    public TimeSpan AudioDuration { get; set; }

    /// <summary>
    /// Alias de conveniencia para la duración del audio.
    /// </summary>
    public TimeSpan Duration { get => AudioDuration; set => AudioDuration = value; }

    /// <summary>
    /// Tiempo total consumido por la inferencia y clustering.
    /// </summary>
    public TimeSpan ExecutionTime { get; set; }

    /// <summary>
    /// Execution Provider de ONNX Runtime empleado ("CPUExecutionProvider", "CUDAExecutionProvider", etc.).
    /// </summary>
    public string ExecutionProvider { get; set; } = "CPU";
}

/// <summary>
/// Resultado de la alineación y reconciliación entre ASR y Diarización.
/// </summary>
public class TranscriptAlignmentResult
{
    public IReadOnlyList<DictaMeeting.Meetings.Models.TranscriptSegment> ReconciledSegments { get; set; } = Array.Empty<DictaMeeting.Meetings.Models.TranscriptSegment>();
    public IReadOnlyDictionary<string, string> SpeakerIdToDisplayNameMap { get; set; } = new Dictionary<string, string>();
}
