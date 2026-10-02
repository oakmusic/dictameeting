namespace DictaMeeting.AI.Interfaces;

/// <summary>
/// Motivos de fallo de validación del resumen en vivo.
/// </summary>
public enum LiveSummaryValidationFailureReason
{
    None = 0,
    EmptyOrTooShort,
    ExcessiveLength,
    RepeatedSentence,
    SentenceDominance,
    DegenerativeLoop,
    DuplicateOfPreviousCard,
    InvalidTimeRange,
    SemanticDisconnect
}

/// <summary>
/// Resultado de la validación del resumen generado antes de ser expuesto a la interfaz de usuario.
/// </summary>
public class LiveSummaryValidationResult
{
    public bool IsValid => FailureReason == LiveSummaryValidationFailureReason.None;
    public LiveSummaryValidationFailureReason FailureReason { get; set; } = LiveSummaryValidationFailureReason.None;
    public string Details { get; set; } = string.Empty;

    public static LiveSummaryValidationResult Success() => new();

    public static LiveSummaryValidationResult Failure(LiveSummaryValidationFailureReason reason, string details) =>
        new()
        {
            FailureReason = reason,
            Details = details
        };
}

/// <summary>
/// Interfaz para validar tarjetas de resumen antes de ser mostradas al usuario,
/// descartando repeticiones degenerativas, bucles, textos desmedidos o desconexión contextual.
/// </summary>
public interface ILiveSummaryOutputValidator
{
    LiveSummaryValidationResult ValidateOutput(
        string generatedSummary,
        string inputTranscript,
        string? previousCardText,
        TimeSpan cardStartTime,
        TimeSpan cardEndTime,
        TimeSpan lastSummarizedEndTime);
}
