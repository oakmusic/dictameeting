using System;

namespace DictaMeeting.AI.Models;

/// <summary>
/// Respuesta normalizada universal para solicitudes de Chat Completions
/// (OpenRouter, Servidores OpenAI-compatibles, gateways internos vLLM/LiteLLM/Ollama, etc.).
/// </summary>
public class AiChatResponse
{
    public bool Success { get; set; }
    public string? Content { get; set; }
    public string? FinishReason { get; set; }
    public string? ErrorMessage { get; set; }
    public int? StatusCode { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? ReasoningTokens { get; set; }
    public int? TotalTokens { get; set; }
    public bool IsTruncated => string.Equals(FinishReason, "length", StringComparison.OrdinalIgnoreCase);
    public string? Refusal { get; set; }
    public string? ModelUsed { get; set; }
    public string? RequestId { get; set; }
}

