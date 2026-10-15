using System.Diagnostics;
using DictaMeeting.AI.Models;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Vocabulary;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Orquestador adaptativo de generación de actas para reuniones largas en varias etapas:
/// 1. Segmentación natural de la transcripción en fragmentos con solapamiento contextual.
/// 2. Análisis y extracción factual estructurada por fragmento con recuperación y reintentos acotados.
/// 3. Síntesis e integración global no destructiva (inicio, desarrollo y acuerdos de cierre).
/// 4. Redacción del documento final completo respetando opciones, idioma y modo impersonal.
/// 5. Validación y saneamiento del resultado.
/// </summary>
public static class HierarchicalActaGenerator
{
    public static async Task<ActaGenerationResult> GenerateHierarchicalActaAsync(
        Meeting meeting,
        ActaGenerationOptions options,
        Func<object, CancellationToken, Task<OpenRouterChatResponse>> sendChatFunc,
        IVocabularyService? vocabularyService = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var modelToUse = string.IsNullOrWhiteSpace(options.ModelName) ? OpenRouterActaService.DefaultModel : options.ModelName.Trim();

        // Etapa 1: Segmentación de la transcripción
        options.Progress?.Report("Analizando la transcripción y preparando fragmentos...");
        var chunks = TranscriptChunker.ChunkTranscript(meeting);

        if (chunks.Count == 0)
        {
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = "No fue posible dividir la transcripción en fragmentos válidos.",
                ModelUsed = modelToUse
            };
        }

        logger?.LogInformation("Iniciando generación jerárquica para reunión '{Title}' dividida en {Count} fragmentos.", meeting.Title, chunks.Count);

        int totalPromptTokens = 0;
        int totalCompletionTokens = 0;
        int totalReasoningTokens = 0;
        var chunkAnalyses = new List<string>();

        // Etapa 2: Análisis de cada fragmento
        for (int i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = chunks[i];

            options.Progress?.Report($"Procesando fragmento {i + 1} de {chunks.Count} ({chunk.TimeRange})...");

            var chunkSystemPrompt = ActaPromptBuilder.BuildChunkAnalysisSystemPrompt(options);
            var chunkPrompt = ActaPromptBuilder.BuildChunkAnalysisPrompt(chunk, meeting, options, vocabularyService);

            var messages = new[]
            {
                new { role = "system", content = chunkSystemPrompt },
                new { role = "user", content = chunkPrompt }
            };

            // Para la extracción de fragmentos se utiliza un presupuesto enfocado
            int chunkOutputTokens = 4000;
            object chunkPayload = BuildPayload(modelToUse, messages, chunkOutputTokens, options, isExtractionStage: true);

            var chunkResponse = await ExecuteWithRetryAsync(
                sendChatFunc,
                chunkPayload,
                maxRetries: 2,
                logger,
                stageName: $"Análisis de fragmento #{i + 1}",
                cancellationToken);

            if (!chunkResponse.Success || string.IsNullOrWhiteSpace(chunkResponse.Content))
            {
                logger?.LogError("Error definitivo al procesar fragmento #{Index}: {Error}", i + 1, chunkResponse.ErrorMessage);
                return new ActaGenerationResult
                {
                    Success = false,
                    ErrorMessage = $"Fallo al procesar el fragmento #{i + 1} ({chunk.TimeRange}): {chunkResponse.ErrorMessage}",
                    ModelUsed = modelToUse,
                    StagesExecuted = i + 1,
                    Duration = stopwatch.Elapsed
                };
            }

            chunkAnalyses.Add(chunkResponse.Content);
            totalPromptTokens += chunkResponse.PromptTokens ?? 0;
            totalCompletionTokens += chunkResponse.CompletionTokens ?? 0;
            totalReasoningTokens += chunkResponse.ReasoningTokens ?? 0;
        }

        // Etapa 3: Síntesis global e integración de temas y decisiones
        string globalSynthesis;
        if (chunks.Count > 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            options.Progress?.Report("Integrando temas, acuerdos y decisiones globales...");

            var synthSystemPrompt = ActaPromptBuilder.BuildGlobalSynthesisSystemPrompt(options);
            var synthPrompt = ActaPromptBuilder.BuildGlobalSynthesisPrompt(chunkAnalyses, meeting, options);

            var synthMessages = new[]
            {
                new { role = "system", content = synthSystemPrompt },
                new { role = "user", content = synthPrompt }
            };

            int synthOutputTokens = 6000;
            object synthPayload = BuildPayload(modelToUse, synthMessages, synthOutputTokens, options, isExtractionStage: false);

            var synthResponse = await ExecuteWithRetryAsync(
                sendChatFunc,
                synthPayload,
                maxRetries: 2,
                logger,
                stageName: "Síntesis global",
                cancellationToken);

            if (!synthResponse.Success || string.IsNullOrWhiteSpace(synthResponse.Content))
            {
                logger?.LogError("Error definitivo en la síntesis global: {Error}", synthResponse.ErrorMessage);
                return new ActaGenerationResult
                {
                    Success = false,
                    ErrorMessage = $"Fallo durante la consolidación global de la reunión: {synthResponse.ErrorMessage}",
                    ModelUsed = modelToUse,
                    StagesExecuted = chunks.Count + 1,
                    Duration = stopwatch.Elapsed
                };
            }

            globalSynthesis = synthResponse.Content;
            totalPromptTokens += synthResponse.PromptTokens ?? 0;
            totalCompletionTokens += synthResponse.CompletionTokens ?? 0;
            totalReasoningTokens += synthResponse.ReasoningTokens ?? 0;
        }
        else
        {
            globalSynthesis = chunkAnalyses[0];
        }

        // Etapa 4: Redacción del acta final en Markdown
        cancellationToken.ThrowIfCancellationRequested();
        options.Progress?.Report("Redactando el acta ejecutiva final...");

        var finalSystemPrompt = ActaPromptBuilder.BuildSystemPrompt(options);
        var finalDraftPrompt = ActaPromptBuilder.BuildFinalDraftFromSynthesisPrompt(globalSynthesis, meeting, options, vocabularyService);

        var finalMessages = new[]
        {
            new { role = "system", content = finalSystemPrompt },
            new { role = "user", content = finalDraftPrompt }
        };

        int finalMaxTokens = OpenRouterActaService.CalculateEffectiveMaxTokens(modelToUse, options, estimatedPromptTokens: 3000);
        object finalDraftPayload = BuildPayload(modelToUse, finalMessages, finalMaxTokens, options, isExtractionStage: false);

        var finalDraftResponse = await ExecuteWithRetryAsync(
            sendChatFunc,
            finalDraftPayload,
            maxRetries: 2,
            logger,
            stageName: "Redacción final",
            cancellationToken);

        if (!finalDraftResponse.Success || string.IsNullOrWhiteSpace(finalDraftResponse.Content))
        {
            logger?.LogError("Error definitivo al redactar el acta final: {Error}", finalDraftResponse.ErrorMessage);
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = $"Fallo al redactar el documento final del acta: {finalDraftResponse.ErrorMessage}",
                ModelUsed = modelToUse,
                StagesExecuted = chunks.Count + (chunks.Count > 1 ? 2 : 1),
                Duration = stopwatch.Elapsed
            };
        }

        string rawMarkdown = finalDraftResponse.Content;
        totalPromptTokens += finalDraftResponse.PromptTokens ?? 0;
        totalCompletionTokens += finalDraftResponse.CompletionTokens ?? 0;
        totalReasoningTokens += finalDraftResponse.ReasoningTokens ?? 0;
        bool wasRecovered = false;

        // Si la redacción final fue interrumpida por límite de longitud, intentar recuperación por continuación
        if (finalDraftResponse.IsTruncated)
        {
            logger?.LogWarning("La redacción final se truncó por límite de tokens. Intentando continuación controlada...");
            options.Progress?.Report("Recuperando continuación de las secciones pendientes...");

            var contPrompt = ActaPromptBuilder.BuildContinuationPrompt(rawMarkdown, meeting, options);
            var contMessages = new[]
            {
                new { role = "system", content = finalSystemPrompt },
                new { role = "user", content = finalDraftPrompt },
                new { role = "assistant", content = rawMarkdown },
                new { role = "user", content = contPrompt }
            };

            object contPayload = BuildPayload(modelToUse, contMessages, 4000, options, isExtractionStage: false);
            var contResponse = await ExecuteWithRetryAsync(
                sendChatFunc,
                contPayload,
                maxRetries: 1,
                logger,
                stageName: "Continuación final",
                cancellationToken);

            if (contResponse.Success && !contResponse.IsTruncated && !string.IsNullOrWhiteSpace(contResponse.Content))
            {
                rawMarkdown = rawMarkdown.TrimEnd() + "\n\n" + contResponse.Content.TrimStart();
                totalPromptTokens += contResponse.PromptTokens ?? 0;
                totalCompletionTokens += contResponse.CompletionTokens ?? 0;
                totalReasoningTokens += contResponse.ReasoningTokens ?? 0;
                wasRecovered = true;
                logger?.LogInformation("Continuación del acta completada con éxito.");
            }
        }

        // Etapa 5: Saneamiento y validación estructural
        options.Progress?.Report("Validando y saneando el documento final...");
        var sanitizedMarkdown = ActaPromptBuilder.SanitizeActaMarkdown(rawMarkdown, meeting, options);

        bool isValid = ValidateMinutesStructure(sanitizedMarkdown);
        if (!isValid)
        {
            logger?.LogWarning("El acta generada no contiene la estructura mínima requerida.");
        }

        stopwatch.Stop();
        int totalStages = chunks.Count + (chunks.Count > 1 ? 2 : 1) + (wasRecovered ? 1 : 0);

        logger?.LogInformation("Acta generada con éxito en {Stages} etapas ({Duration} ms). Tokens: prompt={Prompt}, completion={Completion}, reasoning={Reasoning}.",
            totalStages, stopwatch.ElapsedMilliseconds, totalPromptTokens, totalCompletionTokens, totalReasoningTokens);

        return new ActaGenerationResult
        {
            Success = true,
            MarkdownContent = sanitizedMarkdown,
            ModelUsed = modelToUse,
            PromptTokensUsed = totalPromptTokens,
            CompletionTokensUsed = totalCompletionTokens,
            ReasoningTokensUsed = totalReasoningTokens > 0 ? totalReasoningTokens : null,
            StagesExecuted = totalStages,
            Duration = stopwatch.Elapsed,
            FinishReason = "stop",
            WasRecovered = wasRecovered,
            RequestId = finalDraftResponse.RequestId
        };
    }

    private static object BuildPayload(
        string model,
        object messages,
        int maxTokens,
        ActaGenerationOptions options,
        bool isExtractionStage)
    {
        bool isTempUnsupported = OpenRouterActaService.IsTemperatureUnsupported(model);

        // Reasoning configuration
        object reasoning = options.ReasoningMaxTokens.HasValue && options.ReasoningMaxTokens.Value > 0
            ? new
            {
                max_tokens = options.ReasoningMaxTokens.Value,
                exclude = true
            }
            : new
            {
                effort = !string.IsNullOrWhiteSpace(options.ReasoningEffort)
                    ? options.ReasoningEffort
                    : (isExtractionStage ? "medium" : "high"),
                exclude = true
            };

        if (isTempUnsupported)
        {
            return new
            {
                model,
                messages,
                reasoning,
                max_tokens = maxTokens
            };
        }

        return new
        {
            model,
            messages,
            reasoning,
            max_tokens = maxTokens,
            temperature = options.Temperature
        };
    }

    private static async Task<OpenRouterChatResponse> ExecuteWithRetryAsync(
        Func<object, CancellationToken, Task<OpenRouterChatResponse>> sendChatFunc,
        object payload,
        int maxRetries,
        ILogger? logger,
        string stageName,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        OpenRouterChatResponse lastResponse = new() { Success = false, ErrorMessage = "No ejecutado." };

        while (attempt <= maxRetries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            try
            {
                lastResponse = await sendChatFunc(payload, cancellationToken);
                if (lastResponse.Success && !string.IsNullOrWhiteSpace(lastResponse.Content))
                {
                    return lastResponse;
                }

                // Si es un error permanente de credenciales o modelo inexistente, no reintentar
                if (lastResponse.StatusCode is 400 or 401 or 402 or 403 or 404)
                {
                    logger?.LogWarning("{Stage}: Error HTTP no transitorio ({Status}), abortando reintentos.", stageName, lastResponse.StatusCode);
                    return lastResponse;
                }

                if (attempt <= maxRetries)
                {
                    logger?.LogWarning("{Stage}: Fallo en intento {Attempt}/{Max}: {Error}. Reintentando...",
                        stageName, attempt, maxRetries + 1, lastResponse.ErrorMessage);
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastResponse = new OpenRouterChatResponse
                {
                    Success = false,
                    ErrorMessage = $"Error en {stageName}: {ex.Message}"
                };

                if (attempt <= maxRetries)
                {
                    logger?.LogWarning(ex, "{Stage}: Excepción en intento {Attempt}/{Max}. Reintentando...", stageName, attempt, maxRetries + 1);
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                }
            }
        }

        return lastResponse;
    }

    private static bool ValidateMinutesStructure(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown) || markdown.Length < 100) return false;

        // Comprobar que no termine abruptamente a mitad de palabra o frase inacabada
        var trimmed = markdown.Trim();
        char lastChar = trimmed[^1];
        bool endsPlausibly = lastChar is '.' or '|' or ')' or ']' or '*' or '`' or ':' or '}' or '>' or '\n';

        // Comprobar presencia de al menos dos cabeceras principales de markdown
        int headerCount = 0;
        if (trimmed.Contains("# Acta", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("# Minutes", StringComparison.OrdinalIgnoreCase)) headerCount++;
        if (trimmed.Contains("## Información", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("## General", StringComparison.OrdinalIgnoreCase)) headerCount++;
        if (trimmed.Contains("## Resumen", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("## Executive", StringComparison.OrdinalIgnoreCase)) headerCount++;
        if (trimmed.Contains("## Temas", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("## Topics", StringComparison.OrdinalIgnoreCase)) headerCount++;
        if (trimmed.Contains("## Decisiones", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("## Acciones", StringComparison.OrdinalIgnoreCase)) headerCount++;

        return endsPlausibly && headerCount >= 2;
    }
}
