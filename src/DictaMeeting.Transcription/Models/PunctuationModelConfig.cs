namespace DictaMeeting.Transcription.Models;

/// <summary>
/// Configuración para el motor ONNX de restauración de puntuación y capitalización.
/// </summary>
public class PunctuationModelConfig
{
    /// <summary>
    /// Indica si el procesamiento de restauración de puntuación está habilitado.
    /// Por defecto es false para evitar sobreescribir la puntuación natural generada por Qwen3-ASR.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Ruta explícita al modelo ONNX (model_int8.onnx o model.onnx).
    /// Si es nula, se buscará automáticamente en las rutas estándar de la aplicación.
    /// </summary>
    public string? ModelPath { get; set; }

    /// <summary>
    /// Ruta explícita al modelo SentencePiece (sentencepiece.bpe.model).
    /// Si es nula, se buscará automáticamente en las rutas estándar de la aplicación.
    /// </summary>
    public string? SentencePieceModelPath { get; set; }

    /// <summary>
    /// Tamaño máximo de palabras por ventana de inferencia (el modelo soporta hasta 512 tokens).
    /// Un valor de 200 palabras garantiza ~260-320 tokens, manteniéndose cómodamente bajo el límite.
    /// </summary>
    public int MaxWordsPerWindow { get; set; } = 200;

    /// <summary>
    /// Número de palabras de solapamiento entre ventanas sucesivas para mantener el contexto continuo.
    /// </summary>
    public int OverlapWords { get; set; } = 30;

    /// <summary>
    /// Límite estricto de tokens por ventana ONNX (máximo del modelo es 512, reservando 2 para BOS y EOS).
    /// </summary>
    public int HardTokenLimit { get; set; } = 500;

    /// <summary>
    /// Hilos intra-op de ONNX Runtime para inferencia en CPU.
    /// </summary>
    public int IntraOpNumThreads { get; set; } = 2;

    /// <summary>
    /// Hilos inter-op de ONNX Runtime para inferencia en CPU.
    /// </summary>
    public int InterOpNumThreads { get; set; } = 1;
}
