using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;

namespace DictaMeeting.AI.Models;

public record AiModelOption(string Id, string DisplayName, string Provider, string Description)
{
    public override string ToString() => DisplayName;
}

public class ActaGenerationOptions
{
    public LanguageMode Language { get; set; } = LanguageMode.Spanish;
    public ActaDetailLevel DetailLevel { get; set; } = ActaDetailLevel.Normal;
    public bool IncludeParticipants { get; set; } = true;
    public bool IncludeDecisions { get; set; } = true;
    public bool IncludeActionItems { get; set; } = true;
    public bool IncludePendingQuestions { get; set; } = true;
    public bool IsImpersonal { get; set; } = true;
    public string ModelName { get; set; } = "anthropic/claude-3.5-sonnet";
    public string? ApiKey { get; set; }
    public double Temperature { get; set; } = 0.2;
    public IReadOnlyList<string>? VocabularyTerms { get; set; }
    public string? Endpoint { get; set; }
    public string? CustomModelName { get; set; }

    /// <summary>
    /// Nivel de esfuerzo de razonamiento para OpenRouter ("high", "medium", "low", "none", etc.).
    /// Por defecto es "high". Solo se envía si ReasoningMaxTokens no está definido.
    /// </summary>
    public string? ReasoningEffort { get; set; } = "high";

    /// <summary>
    /// Límite explícito de tokens de razonamiento para OpenRouter.
    /// Si tiene valor > 0, se envía reasoning.max_tokens en lugar de reasoning.effort.
    /// En ningún caso se envían ambos simultáneamente para respetar la API de OpenRouter.
    /// </summary>
    public int? ReasoningMaxTokens { get; set; }

    /// <summary>
    /// Presupuesto explícito de tokens de salida (respuesta) para el acta.
    /// Si no se especifica, se calcula adaptativamente según el modelo, el nivel de detalle y el razonamiento.
    /// </summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// Notificador de progreso comprensible para informar al usuario de las etapas de generación.
    /// </summary>
    public IProgress<string>? Progress { get; set; }

    /// <summary>
    /// Fuerza el procesamiento jerárquico por fragmentos incluso en reuniones cortas (útil para pruebas o actas exhaustivas).
    /// </summary>
    public bool? ForceHierarchicalProcessing { get; set; }

    /// <summary>
    /// Desactiva el procesamiento jerárquico forzando generación directa en una única llamada (salvo que sea inviable).
    /// </summary>
    public bool? DisableHierarchicalProcessing { get; set; }
}

public class ActaGenerationResult
{
    public bool Success { get; set; }
    public string? MarkdownContent { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ModelUsed { get; set; }
    public int? PromptTokensUsed { get; set; }
    public int? CompletionTokensUsed { get; set; }
    public int? TotalTokensUsed => (PromptTokensUsed ?? 0) + (CompletionTokensUsed ?? 0);

    /// <summary>
    /// Motivo de finalización devuelto por el modelo (e.g. 'stop', 'length', 'content_filter').
    /// </summary>
    public string? FinishReason { get; set; }

    /// <summary>
    /// Indica si la respuesta se interrumpió por haber alcanzado el límite de tokens de salida.
    /// </summary>
    public bool IsTruncated { get; set; }

    /// <summary>
    /// Tokens consumidos específicamente en la fase de razonamiento (CoT), si el proveedor los reporta.
    /// </summary>
    public int? ReasoningTokensUsed { get; set; }

    /// <summary>
    /// Límite de tokens de salida configurado para la petición.
    /// </summary>
    public int? MaxOutputTokensConfigured { get; set; }

    /// <summary>
    /// Número de etapas ejecutadas (1 para directa, >1 para jerárquica).
    /// </summary>
    public int StagesExecuted { get; set; } = 1;

    /// <summary>
    /// Duración total del proceso de generación.
    /// </summary>
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Contenido Markdown parcial conservado en caso de truncamiento o fallo en etapa posterior.
    /// </summary>
    public string? PartialMarkdownContent { get; set; }

    /// <summary>
    /// Indica si el acta fue recuperada tras una interrupción parcial inicial mediante continuación.
    /// </summary>
    public bool WasRecovered { get; set; }

    /// <summary>
    /// Identificador único de solicitud asignado por el proveedor de IA.
    /// </summary>
    public string? RequestId { get; set; }
}

