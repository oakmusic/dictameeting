namespace DictaMeeting.AI.Models;

/// <summary>
/// Métricas de diagnóstico de una inferencia de resumen local.
/// </summary>
public record SummaryMetrics(
    string ModelName,
    string Quantization,
    long ModelSizeBytes,
    TimeSpan LoadDuration,
    TimeSpan InferenceDuration,
    int InputTokens,
    int GeneratedTokens,
    double TokensPerSecond,
    bool Success,
    string? ErrorMessage = null
);
