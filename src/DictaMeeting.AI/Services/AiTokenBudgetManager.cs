using System;
using DictaMeeting.AI.Models;
using DictaMeeting.Meetings.Enums;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Gestor dinámico de presupuesto de tokens y ventanas de contexto para modelos de IA.
/// Centraliza el cálculo de límites para evitar asfixiar respuestas largas (como actas Exhaustivas)
/// y garantizar que las solicitudes no excedan la capacidad de contexto del modelo o del servidor.
/// </summary>
public static class AiTokenBudgetManager
{
    public const int DefaultContextWindow = 128000;
    public const int DefaultMaxCompletionTokens = 16384;
    public const int DefaultSafetyMargin = 1500;

    /// <summary>
    /// Calcula el presupuesto de salida efectivo (max_tokens) adaptativo según el modelo,
    /// nivel de detalle solicitado, opciones del usuario y espacio de contexto restante.
    /// Para actas "Exhaustivas", permite aprovechar hasta 16.000 tokens en modelos compatibles (Qwen, GPT, Claude).
    /// </summary>
    public static int CalculateEffectiveMaxTokens(
        string model,
        ActaGenerationOptions options,
        int estimatedPromptTokens = 0)
    {
        // 1. Presupuesto base según el nivel de detalle solicitado (o límite explícito si se configuró en options)
        int baseOutputTokens;
        if (options.MaxOutputTokens.HasValue && options.MaxOutputTokens.Value > 0)
        {
            baseOutputTokens = options.MaxOutputTokens.Value;
        }
        else
        {
            baseOutputTokens = options.DetailLevel switch
            {
                ActaDetailLevel.Breve => 4000,
                ActaDetailLevel.Normal => 8000,
                ActaDetailLevel.Detallada => 12000,
                ActaDetailLevel.Exhaustiva => 16000,
                _ => 8000
            };
        }

        // 2. Respetar límites máximos documentados por proveedor/modelo
        int modelMaxCompletion = GetModelMaxCompletionTokens(model);
        int cappedOutput = Math.Min(baseOutputTokens, modelMaxCompletion);

        // 3. Reserva para modelos de razonamiento (CoT):
        int reasoningReservation = 0;
        if (options.ReasoningMaxTokens.HasValue && options.ReasoningMaxTokens.Value > 0)
        {
            reasoningReservation = options.ReasoningMaxTokens.Value;
        }
        else if (IsReasoningModel(model) &&
                 !string.IsNullOrWhiteSpace(options.ReasoningEffort) &&
                 !options.ReasoningEffort.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            reasoningReservation = options.ReasoningEffort.ToLowerInvariant() switch
            {
                "low" => 2000,
                "medium" => 4000,
                "high" => 8000,
                _ => 4000
            };
        }

        int totalDesired = cappedOutput + reasoningReservation;

        // 4. Asegurar que prompt + output no supere la ventana de contexto del modelo
        int contextWindow = GetModelContextWindow(model);
        int safeContextRemaining = Math.Max(2000, contextWindow - estimatedPromptTokens - DefaultSafetyMargin);

        return Math.Min(totalDesired, safeContextRemaining);
    }

    /// <summary>
    /// Devuelve el límite máximo de tokens de salida (generación) que admite el modelo según sus especificaciones.
    /// </summary>
    public static int GetModelMaxCompletionTokens(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return DefaultMaxCompletionTokens;

        if (model.Contains("claude", StringComparison.OrdinalIgnoreCase)) return 8192;
        if (model.Contains("qwen", StringComparison.OrdinalIgnoreCase)) return 16384;
        if (model.Contains("gpt-6", StringComparison.OrdinalIgnoreCase)) return 32768;
        if (model.Contains("o1", StringComparison.OrdinalIgnoreCase) || model.Contains("o3", StringComparison.OrdinalIgnoreCase)) return 32768;
        if (model.Contains("deepseek", StringComparison.OrdinalIgnoreCase)) return 8192;

        return DefaultMaxCompletionTokens;
    }

    /// <summary>
    /// Devuelve la ventana de contexto total (entrada + salida) del modelo.
    /// </summary>
    public static int GetModelContextWindow(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return DefaultContextWindow;

        if (model.Contains("claude", StringComparison.OrdinalIgnoreCase)) return 200000;
        if (model.Contains("qwen", StringComparison.OrdinalIgnoreCase)) return 128000;
        if (model.Contains("gpt-4o", StringComparison.OrdinalIgnoreCase)) return 128000;
        if (model.Contains("gpt-6", StringComparison.OrdinalIgnoreCase)) return 128000;

        return DefaultContextWindow;
    }

    /// <summary>
    /// Determina si el modelo utiliza una fase de razonamiento (CoT) cuyos tokens descuentan de max_tokens.
    /// </summary>
    public static bool IsReasoningModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;

        return model.Equals("openai/gpt-6-luna", StringComparison.OrdinalIgnoreCase) ||
               model.StartsWith("openai/o1", StringComparison.OrdinalIgnoreCase) ||
               model.StartsWith("openai/o3", StringComparison.OrdinalIgnoreCase) ||
               model.Contains("deepseek-r1", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Estimación conservadora de tokens para modelos BPE (Qwen, GPT, Claude).
    /// Utiliza un ratio de 3.5 caracteres por token para texto en español/multilingüe, evitando subestimaciones.
    /// </summary>
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / 3.5);
    }
}
