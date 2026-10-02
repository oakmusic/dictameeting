namespace DictaMeeting.AI.Models;

/// <summary>
/// Configuración para el servicio de resumen en vivo local.
/// </summary>
public class LiveSummaryConfig
{
    /// <summary>
    /// Duración de la ventana de transcripción para cada tarjeta de resumen (1 a 2 minutos).
    /// </summary>
    public TimeSpan ContextWindowDuration { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Intervalo mínimo entre generaciones consecutivas de tarjetas de resumen (1 minuto).
    /// </summary>
    public TimeSpan SummaryInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Mínimo de palabras nuevas requeridas para disparar una tarjeta de resumen.
    /// Evita ejecuciones innecesarias en silencios o monosílabos.
    /// </summary>
    public int MinimumNewWordsThreshold { get; set; } = 25;

    /// <summary>
    /// Tamaño de contexto en tokens para llama.cpp (predeterminado 2048).
    /// Mantiene un bajo consumo de memoria RAM (~1.5–2 GB).
    /// </summary>
    public uint ContextSizeTokens { get; set; } = 2048;

    /// <summary>
    /// Máximo de tokens a generar por tarjeta de resumen (~50-60 palabras = ~80-110 tokens, con margen de seguridad).
    /// </summary>
    public int MaxTokensToGenerate { get; set; } = 160;

    /// <summary>
    /// Temperatura de muestreo. Un valor de 0.3f equilibra fidelidad factual y fluidez natural.
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    /// <summary>
    /// Top-P sampling.
    /// </summary>
    public float TopP { get; set; } = 0.85f;

    /// <summary>
    /// Penalización de repetición para prevenir bucles degenerativos en modelos locales compactos.
    /// </summary>
    public float RepeatPenalty { get; set; } = 1.18f;

    /// <summary>
    /// Penalización por frecuencia de tokens repetidos.
    /// </summary>
    public float FrequencyPenalty { get; set; } = 0.3f;

    /// <summary>
    /// Penalización por presencia de tokens previamente generados.
    /// </summary>
    public float PresencePenalty { get; set; } = 0.15f;

    /// <summary>
    /// Cantidad de tokens anteriores a considerar para la penalización de repetición.
    /// </summary>
    public int PenaltyCount { get; set; } = 128;

    /// <summary>
    /// Hilos de CPU para inferencia. Si es null, usa la mitad de núcleos lógicos (máx 4)
    /// para garantizar que Qwen3-ASR, Silero VAD y la UI tengan prioridad absoluta.
    /// </summary>
    public int? ThreadCount { get; set; } = null;

    /// <summary>
    /// Habilitar aceleración GPU NVIDIA opcional si CUDA está disponible, con fallback automático a CPU.
    /// </summary>
    public bool EnableGpuIfAvailable { get; set; } = true;
}
