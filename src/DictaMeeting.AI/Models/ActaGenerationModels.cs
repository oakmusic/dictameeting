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
}

