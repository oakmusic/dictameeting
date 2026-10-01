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
    /// Máximo de tokens a generar por tarjeta de resumen (~50-60 palabras = ~80-110 tokens, con margen de seguridad para evitar frases cortadas).
    /// </summary>
    public int MaxTokensToGenerate { get; set; } = 220;

    /// <summary>
    /// Temperatura de muestreo. Un valor bajo (0.2) previene alucinaciones y favorece fidelidad factual.
    /// </summary>
    public float Temperature { get; set; } = 0.2f;

    /// <summary>
    /// Top-P sampling.
    /// </summary>
    public float TopP { get; set; } = 0.85f;

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
