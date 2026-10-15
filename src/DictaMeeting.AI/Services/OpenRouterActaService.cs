using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Vocabulary;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Respuesta normalizada de una llamada a la API de Chat Completions de OpenRouter.
/// </summary>
public class OpenRouterChatResponse
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

/// <summary>
/// Servicio de generación de actas ejecutivas mediante la API de OpenRouter.
/// Aplica prompts estrictos anti-alucinaciones, respeta la privacidad de datos (cero audio transmitido),
/// gestiona límites de tokens adaptativos según modelo y nivel de detalle, y emplea generación
/// jerárquica en varias etapas para reuniones largas.
/// </summary>
public class OpenRouterActaService : IAiActaService, IAiActaProvider
{
    public const string ProviderIdentifier = "OpenRouter";
    public string ProviderId => ProviderIdentifier;
    public string DisplayName => "OpenRouter";

    public const string OpenRouterApiKeyStorageKey = "OpenRouter_ApiKey";
    public const string DefaultEndpoint = "https://openrouter.ai/api/v1/chat/completions";
    public const string DefaultModel = "openai/gpt-6-luna";
    public const string HttpReferer = "https://github.com/oakmusic/dictameeting";
    public const string AppTitle = "DictaMeeting Windows App";

    private readonly HttpClient _httpClient;
    private readonly ISecureStorageService? _secureStorage;
    private readonly IVocabularyService? _vocabularyService;
    private readonly ILogger<OpenRouterActaService>? _logger;

    private static readonly HttpClient DefaultSharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(180)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public OpenRouterActaService(
        HttpClient? httpClient = null,
        ISecureStorageService? secureStorage = null,
        IVocabularyService? vocabularyService = null,
        ILogger<OpenRouterActaService>? logger = null)
    {
        _httpClient = httpClient ?? DefaultSharedHttpClient;
        _secureStorage = secureStorage;
        _vocabularyService = vocabularyService;
        _logger = logger;
    }

    public bool SupportsModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return true;
        return !modelId.Equals("custom_server", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<AiModelOption> GetRecommendedModels()
    {
        return new List<AiModelOption>
        {
            new("openai/gpt-6-luna", "GPT-6 Luna", "OpenAI", "Modelo insignia de OpenAI de última generación para actas complejas y máxima precisión."),
            new("qwen/qwen3.8-flash", "Qwen 3.8 Flash", "Qwen", "Modelo ultrarrápido y de alta fidelidad para síntesis y razonamiento de actas.")
        };
    }

    public string BuildPrompt(Meeting meeting, ActaGenerationOptions options)
    {
        return ActaPromptBuilder.BuildPrompt(meeting, options, _vocabularyService);
    }

    /// <summary>
    /// Calcula el presupuesto de salida efectivo (max_tokens) adaptativo según el modelo,
    /// nivel de detalle solicitado, esfuerzo de razonamiento y espacio de contexto restante.
    /// Evita asfixiar las respuestas largas o el razonamiento con límites fijos insuficientes (como 4000).
    /// </summary>
    public static int CalculateEffectiveMaxTokens(
        string model,
        ActaGenerationOptions options,
        int estimatedPromptTokens = 0)
    {
        // 1. Presupuesto base según el nivel de detalle solicitado (o límite explícito si se configuró)
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
        // En OpenRouter, muchos modelos descuentan tokens de razonamiento de max_tokens.
        // Si no se añade margen para el razonamiento, la fase de razonamiento puede agotar
        // los tokens antes de producir texto del acta (causando respuestas vacías o truncadas).
        int reasoningReservation = 0;
        if (IsReasoningModel(model) || options.ReasoningMaxTokens.HasValue ||
            (!string.IsNullOrWhiteSpace(options.ReasoningEffort) && !options.ReasoningEffort.Equals("none", StringComparison.OrdinalIgnoreCase)))
        {
            if (options.ReasoningMaxTokens.HasValue && options.ReasoningMaxTokens.Value > 0)
            {
                reasoningReservation = options.ReasoningMaxTokens.Value;
            }
            else
            {
                reasoningReservation = options.ReasoningEffort?.ToLowerInvariant() switch
                {
                    "low" => 2000,
                    "medium" => 4000,
                    "high" => 8000,
                    _ => 4000
                };
            }
        }

        int totalDesired = cappedOutput + reasoningReservation;

        // 4. Asegurar que prompt + output no supere la ventana de contexto del modelo
        int contextWindow = GetModelContextWindow(model);
        int safeContextRemaining = Math.Max(2000, contextWindow - estimatedPromptTokens - 1000);

        return Math.Min(totalDesired, safeContextRemaining);
    }

    public static int GetModelMaxCompletionTokens(string model)
    {
        if (model.Contains("claude", StringComparison.OrdinalIgnoreCase)) return 8192;
        if (model.Contains("qwen", StringComparison.OrdinalIgnoreCase)) return 16384;
        if (model.Contains("gpt-6", StringComparison.OrdinalIgnoreCase)) return 32768;
        if (model.Contains("o1", StringComparison.OrdinalIgnoreCase) || model.Contains("o3", StringComparison.OrdinalIgnoreCase)) return 32768;
        return 16384;
    }

    public static int GetModelContextWindow(string model)
    {
        if (model.Contains("claude", StringComparison.OrdinalIgnoreCase)) return 200000;
        return 128000;
    }

    public static bool IsReasoningModel(string model)
    {
        return model.Equals("openai/gpt-6-luna", StringComparison.OrdinalIgnoreCase) ||
               model.StartsWith("openai/o1", StringComparison.OrdinalIgnoreCase) ||
               model.StartsWith("openai/o3", StringComparison.OrdinalIgnoreCase) ||
               model.Contains("deepseek-r1", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTemperatureUnsupported(string model)
    {
        return model.Equals("openai/gpt-6-luna", StringComparison.OrdinalIgnoreCase) ||
               model.StartsWith("openai/o1", StringComparison.OrdinalIgnoreCase) ||
               model.StartsWith("openai/o3", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ActaGenerationResult> GenerateActaAsync(
        Meeting meeting,
        ActaGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. Obtener API Key (de las opciones o del almacenamiento seguro DPAPI)
        var apiKey = !string.IsNullOrWhiteSpace(options.ApiKey)
            ? options.ApiKey.Trim()
            : _secureStorage?.GetSecret(OpenRouterApiKeyStorageKey)?.Trim();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger?.LogWarning("Intento de generar acta sin API Key de OpenRouter configurada.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = "No se ha configurado la API Key de OpenRouter. Por favor, introduzca y guarde su clave en la configuración para poder generar actas con IA."
            };
        }

        // 2. Validar que la reunión tenga transcripción
        if (meeting.Transcript.Count == 0)
        {
            _logger?.LogWarning("Intento de generar acta sobre una reunión sin transcripción.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = "La reunión no contiene fragmentos de transcripción. No es posible redactar un acta sin contenido hablado."
            };
        }

        var modelToUse = string.IsNullOrWhiteSpace(options.ModelName) ? DefaultModel : options.ModelName.Trim();

        try
        {
            // 3. Decidir si utilizar procesamiento jerárquico para reuniones largas
            bool useHierarchical = TranscriptChunker.ShouldUseHierarchicalGeneration(meeting, options);

            if (useHierarchical)
            {
                _logger?.LogInformation("Activando estrategia de generación jerárquica para reunión larga '{Title}' ({Duration:hh\\:mm\\:ss}).",
                    meeting.Title, meeting.Duration);

                return await HierarchicalActaGenerator.GenerateHierarchicalActaAsync(
                    meeting,
                    options,
                    (payload, ct) => SendChatCompletionPayloadAsync(payload, apiKey, ct),
                    _vocabularyService,
                    _logger,
                    cancellationToken);
            }

            // 4. Generación directa para reuniones cortas/medianas
            options.Progress?.Report($"Conectando con OpenRouter ({modelToUse})...");

            var fullPrompt = BuildPrompt(meeting, options);
            var systemPrompt = ActaPromptBuilder.BuildSystemPrompt(options);

            var messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = fullPrompt }
            };

            int estimatedPromptTokens = TranscriptChunker.EstimateTokens(systemPrompt) + TranscriptChunker.EstimateTokens(fullPrompt);
            int maxOutputTokens = CalculateEffectiveMaxTokens(modelToUse, options, estimatedPromptTokens);

            object reasoning = options.ReasoningMaxTokens.HasValue && options.ReasoningMaxTokens.Value > 0
                ? new
                {
                    max_tokens = options.ReasoningMaxTokens.Value,
                    exclude = true
                }
                : new
                {
                    effort = !string.IsNullOrWhiteSpace(options.ReasoningEffort) ? options.ReasoningEffort : "high",
                    exclude = true
                };

            bool isTemperatureUnsupported = IsTemperatureUnsupported(modelToUse);

            object requestPayload = isTemperatureUnsupported
                ? new
                {
                    model = modelToUse,
                    messages,
                    reasoning,
                    max_tokens = maxOutputTokens
                }
                : new
                {
                    model = modelToUse,
                    messages,
                    reasoning,
                    max_tokens = maxOutputTokens,
                    temperature = options.Temperature
                };

            _logger?.LogInformation("Enviando solicitud directa a OpenRouter con modelo '{Model}' y max_tokens={MaxTokens}...", modelToUse, maxOutputTokens);

            var chatResponse = await SendChatCompletionPayloadAsync(requestPayload, apiKey, cancellationToken);

            if (!chatResponse.Success)
            {
                return new ActaGenerationResult
                {
                    Success = false,
                    ErrorMessage = chatResponse.ErrorMessage,
                    ModelUsed = modelToUse,
                    PromptTokensUsed = chatResponse.PromptTokens,
                    CompletionTokensUsed = chatResponse.CompletionTokens,
                    ReasoningTokensUsed = chatResponse.ReasoningTokens,
                    Duration = stopwatch.Elapsed,
                    RequestId = chatResponse.RequestId
                };
            }

            string markdownContent = chatResponse.Content ?? string.Empty;
            bool wasRecovered = false;

            // Manejo de truncamiento por límite de salida en generación directa
            if (chatResponse.IsTruncated)
            {
                _logger?.LogWarning("La respuesta directa de OpenRouter terminó por límite de tokens (finish_reason=length). Intentando recuperación...");
                options.Progress?.Report("El acta fue interrumpida por límite de tokens. Recuperando continuación...");

                var contPrompt = ActaPromptBuilder.BuildContinuationPrompt(markdownContent, meeting, options);
                var contMessages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = fullPrompt },
                    new { role = "assistant", content = markdownContent },
                    new { role = "user", content = contPrompt }
                };

                object contPayload = isTemperatureUnsupported
                    ? new
                    {
                        model = modelToUse,
                        messages = contMessages,
                        reasoning,
                        max_tokens = maxOutputTokens
                    }
                    : new
                    {
                        model = modelToUse,
                        messages = contMessages,
                        reasoning,
                        max_tokens = maxOutputTokens,
                        temperature = options.Temperature
                    };

                var contResponse = await SendChatCompletionPayloadAsync(contPayload, apiKey, cancellationToken);
                if (contResponse.Success && !contResponse.IsTruncated && !string.IsNullOrWhiteSpace(contResponse.Content))
                {
                    markdownContent = markdownContent.TrimEnd() + "\n\n" + contResponse.Content.TrimStart();
                    wasRecovered = true;
                    _logger?.LogInformation("Continuación recuperada con éxito en modo directo.");
                }
                else
                {
                    // Si no se pudo continuar o la continuación también se truncó por longitud, reportar truncamiento
                    return new ActaGenerationResult
                    {
                        Success = false,
                        IsTruncated = true,
                        ErrorMessage = "El modelo alcanzó el límite de tokens antes de completar todas las secciones del acta. Pruebe a seleccionar un nivel de detalle menor o active el modo jerárquico.",
                        PartialMarkdownContent = markdownContent + (!string.IsNullOrWhiteSpace(contResponse.Content) ? "\n\n" + contResponse.Content : ""),
                        ModelUsed = modelToUse,
                        PromptTokensUsed = (chatResponse.PromptTokens ?? 0) + (contResponse.PromptTokens ?? 0),
                        CompletionTokensUsed = (chatResponse.CompletionTokens ?? 0) + (contResponse.CompletionTokens ?? 0),
                        ReasoningTokensUsed = (chatResponse.ReasoningTokens ?? 0) + (contResponse.ReasoningTokens ?? 0),
                        FinishReason = "length",
                        Duration = stopwatch.Elapsed,
                        RequestId = chatResponse.RequestId
                    };
                }
            }

            if (string.IsNullOrWhiteSpace(markdownContent))
            {
                return new ActaGenerationResult
                {
                    Success = false,
                    ErrorMessage = "OpenRouter devolvió una respuesta vacía.",
                    ModelUsed = modelToUse,
                    FinishReason = chatResponse.FinishReason,
                    Duration = stopwatch.Elapsed,
                    RequestId = chatResponse.RequestId
                };
            }

            markdownContent = SanitizeActaMarkdown(markdownContent, meeting, options);
            stopwatch.Stop();

            _logger?.LogInformation("Acta generada con éxito con OpenRouter. Tokens: prompt={Prompt}, completion={Completion}, reasoning={Reasoning}.",
                chatResponse.PromptTokens, chatResponse.CompletionTokens, chatResponse.ReasoningTokens);

            return new ActaGenerationResult
            {
                Success = true,
                MarkdownContent = markdownContent,
                ModelUsed = modelToUse,
                PromptTokensUsed = chatResponse.PromptTokens,
                CompletionTokensUsed = chatResponse.CompletionTokens,
                ReasoningTokensUsed = chatResponse.ReasoningTokens,
                MaxOutputTokensConfigured = maxOutputTokens,
                Duration = stopwatch.Elapsed,
                FinishReason = chatResponse.FinishReason ?? "stop",
                WasRecovered = wasRecovered,
                RequestId = chatResponse.RequestId
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation("Generación de acta cancelada por el usuario.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = "Generación de acta cancelada.",
                ModelUsed = modelToUse
            };
        }
        catch (TaskCanceledException)
        {
            _logger?.LogError("Tiempo de espera agotado al conectar con OpenRouter.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = "El tiempo de espera de la solicitud a OpenRouter se ha agotado (timeout). Compruebe su conexión o reintente.",
                ModelUsed = modelToUse
            };
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "Error de red al conectar con OpenRouter.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = $"Error de conexión con el servicio de OpenRouter: {ex.Message}. Verifique su conexión a Internet.",
                ModelUsed = modelToUse
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error inesperado durante la generación del acta.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = $"Error inesperado al generar el acta: {ex.Message}",
                ModelUsed = modelToUse
            };
        }
    }

    /// <summary>
    /// Envía un payload a la API de Chat Completions de OpenRouter, parseando rigurosamente
    /// las opciones de choices, finish_reason, reasoning, refusal, errores y uso de tokens.
    /// </summary>
    public async Task<OpenRouterChatResponse> SendChatCompletionPayloadAsync(
        object payload,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var jsonContent = JsonSerializer.Serialize(payload, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, DefaultEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("HTTP-Referer", HttpReferer);
        request.Headers.Add("X-Title", AppTitle);
        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorDetail = ParseErrorMessage(responseBody, response.StatusCode);
            _logger?.LogError("Error devuelto por OpenRouter ({StatusCode}): {ErrorDetail}", response.StatusCode, errorDetail);
            return new OpenRouterChatResponse
            {
                Success = false,
                StatusCode = (int)response.StatusCode,
                ErrorMessage = errorDetail
            };
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        string? requestId = null;
        if (root.TryGetProperty("id", out var idElem) && idElem.ValueKind == JsonValueKind.String)
        {
            requestId = idElem.GetString();
        }

        string? modelUsed = null;
        if (root.TryGetProperty("model", out var modelElem) && modelElem.ValueKind == JsonValueKind.String)
        {
            modelUsed = modelElem.GetString();
        }

        string? markdownContent = null;
        string? finishReason = null;
        string? refusal = null;
        string? choiceError = null;

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];

            if (firstChoice.TryGetProperty("finish_reason", out var frElem) && frElem.ValueKind == JsonValueKind.String)
            {
                finishReason = frElem.GetString();
            }

            markdownContent = ExtractContentFromChoice(firstChoice);

            if (firstChoice.TryGetProperty("message", out var msgObj))
            {
                if (msgObj.TryGetProperty("refusal", out var refElem) && refElem.ValueKind == JsonValueKind.String)
                {
                    refusal = refElem.GetString();
                }
            }

            if (firstChoice.TryGetProperty("error", out var errElem))
            {
                if (errElem.TryGetProperty("message", out var errSubMsg) && errSubMsg.ValueKind == JsonValueKind.String)
                {
                    choiceError = errSubMsg.GetString();
                }
                else if (errElem.ValueKind == JsonValueKind.String)
                {
                    choiceError = errElem.GetString();
                }
            }
        }

        // Extracción de uso de tokens
        int? promptTokens = null;
        int? completionTokens = null;
        int? totalTokens = null;
        int? reasoningTokens = null;

        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var ptVal)) promptTokens = ptVal;
            if (usage.TryGetProperty("completion_tokens", out var ct) && ct.TryGetInt32(out var ctVal)) completionTokens = ctVal;
            if (usage.TryGetProperty("total_tokens", out var tt) && tt.TryGetInt32(out var ttVal)) totalTokens = ttVal;

            if (usage.TryGetProperty("completion_tokens_details", out var ctd))
            {
                if (ctd.TryGetProperty("reasoning_tokens", out var rt) && rt.TryGetInt32(out var rtVal)) reasoningTokens = rtVal;
            }
        }

        // Diagnóstico preciso si la respuesta no contiene contenido útil
        if (string.IsNullOrWhiteSpace(markdownContent))
        {
            string diagnosticError;
            if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                diagnosticError = $"El modelo agotó el límite de tokens (completion_tokens={completionTokens}) durante la fase de razonamiento o generación antes de poder emitir el texto del acta. Se recomienda aumentar el presupuesto de tokens o utilizar la generación jerárquica.";
            }
            else if (!string.IsNullOrWhiteSpace(refusal))
            {
                diagnosticError = $"El modelo rechazó procesar la solicitud: {refusal}";
            }
            else if (string.Equals(finishReason, "content_filter", StringComparison.OrdinalIgnoreCase))
            {
                diagnosticError = "La respuesta fue bloqueada por el filtro de seguridad de contenido del proveedor de IA.";
            }
            else if (!string.IsNullOrWhiteSpace(choiceError))
            {
                diagnosticError = $"El proveedor reportó un error durante la generación: {choiceError}";
            }
            else
            {
                diagnosticError = "OpenRouter devolvió una respuesta vacía.";
            }

            _logger?.LogWarning("Respuesta de OpenRouter sin contenido útil: {Diagnostic}", diagnosticError);

            return new OpenRouterChatResponse
            {
                Success = false,
                FinishReason = finishReason,
                ErrorMessage = diagnosticError,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                ReasoningTokens = reasoningTokens,
                TotalTokens = totalTokens,
                Refusal = refusal,
                ModelUsed = modelUsed,
                RequestId = requestId
            };
        }

        return new OpenRouterChatResponse
        {
            Success = true,
            Content = markdownContent,
            FinishReason = finishReason,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            ReasoningTokens = reasoningTokens,
            TotalTokens = totalTokens,
            ModelUsed = modelUsed,
            RequestId = requestId
        };
    }

    /// <summary>
    /// Extrae contenido textual desde un choice de OpenRouter, soportando cadenas estándar,
    /// arrays multimodales de bloques de texto, reasoning_content o legacy text.
    /// </summary>
    public static string? ExtractContentFromChoice(JsonElement choice)
    {
        if (choice.TryGetProperty("message", out var msg))
        {
            if (msg.TryGetProperty("content", out var contentElem))
            {
                if (contentElem.ValueKind == JsonValueKind.String)
                {
                    var str = contentElem.GetString();
                    if (!string.IsNullOrWhiteSpace(str)) return str;
                }
                else if (contentElem.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var part in contentElem.EnumerateArray())
                    {
                        if (part.ValueKind == JsonValueKind.String)
                        {
                            sb.Append(part.GetString());
                        }
                        else if (part.ValueKind == JsonValueKind.Object &&
                                 part.TryGetProperty("text", out var textElem) &&
                                 textElem.ValueKind == JsonValueKind.String)
                        {
                            sb.Append(textElem.GetString());
                        }
                    }
                    var combined = sb.ToString();
                    if (!string.IsNullOrWhiteSpace(combined)) return combined;
                }
            }
        }

        if (choice.TryGetProperty("text", out var textElemFallback) && textElemFallback.ValueKind == JsonValueKind.String)
        {
            var text = textElemFallback.GetString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }

        return null;
    }

    private static string ParseErrorMessage(string responseBody, System.Net.HttpStatusCode statusCode)
    {
        string? serverMessage = null;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.TryGetProperty("message", out var msgElem) && !string.IsNullOrWhiteSpace(msgElem.GetString()))
                {
                    serverMessage = msgElem.GetString();
                }
                else if (errorObj.ValueKind == JsonValueKind.String)
                {
                    serverMessage = errorObj.GetString();
                }
            }
        }
        catch
        {
            // Fallback a mapeo de códigos comunes si el JSON de error no es estándar
        }

        string defaultDesc = statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "Clave de API de OpenRouter no válida o revocada (Error 401). Verifique su API Key en la configuración.",
            System.Net.HttpStatusCode.PaymentRequired => "Crédito insuficiente en su cuenta de OpenRouter (Error 402). Revise su saldo en openrouter.ai.",
            (System.Net.HttpStatusCode)429 => "Límite de solicitudes alcanzado en OpenRouter (Error 429). Por favor espere unos segundos antes de reintentar.",
            System.Net.HttpStatusCode.NotFound => "El modelo seleccionado no está disponible en OpenRouter (Error 404).",
            _ => $"Error devuelto por el servidor de OpenRouter: {(int)statusCode} ({statusCode})."
        };

        if (!string.IsNullOrWhiteSpace(serverMessage))
        {
            return $"{defaultDesc} Detalle: {serverMessage}";
        }

        return defaultDesc;
    }

    /// <summary>
    /// Sanea el contenido generado del acta para garantizar que no se filtren identificadores
    /// técnicos como SPEAKER_XX en la cabecera ni en el cuerpo, transformándolos en redacciones impersonales.
    /// </summary>
    public static string SanitizeActaMarkdown(string markdown, Meeting meeting, ActaGenerationOptions options)
    {
        return ActaPromptBuilder.SanitizeActaMarkdown(markdown, meeting, options);
    }

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
        => TestConnectionAsync(apiKey: null, cancellationToken);

    public async Task<ConnectionTestResult> TestConnectionAsync(string? apiKey, CancellationToken cancellationToken = default)
    {
        var keyToUse = !string.IsNullOrWhiteSpace(apiKey)
            ? apiKey.Trim()
            : _secureStorage?.GetSecret(OpenRouterApiKeyStorageKey)?.Trim();

        if (string.IsNullOrWhiteSpace(keyToUse))
        {
            return ConnectionTestResult.Failed("✕ No se pudo conectar: Clave de API de OpenRouter no configurada.", DefaultModel, DisplayName);
        }

        try
        {
            var payload = new
            {
                model = DefaultModel,
                messages = new[]
                {
                    new { role = "user", content = "Di únicamente OK." }
                },
                max_tokens = 10
            };

            var jsonContent = JsonSerializer.Serialize(payload, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, DefaultEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", keyToUse);
            request.Headers.Add("HTTP-Referer", HttpReferer);
            request.Headers.Add("X-Title", AppTitle);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorDetail = ParseErrorMessage(responseBody, response.StatusCode);
                return ConnectionTestResult.Failed($"✕ No se pudo conectar: {errorDetail}", DefaultModel, DisplayName);
            }

            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var contentElem) && !string.IsNullOrWhiteSpace(contentElem.GetString()))
                {
                    return ConnectionTestResult.Ok("✓ Conexión correcta con OpenRouter", DefaultModel, DisplayName);
                }
            }

            return ConnectionTestResult.Failed("✕ No se pudo conectar: Respuesta incompatible (no contiene choices[0].message.content).", DefaultModel, DisplayName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ConnectionTestResult.Failed("✕ No se pudo conectar: Prueba de conexión cancelada.", DefaultModel, DisplayName);
        }
        catch (TaskCanceledException)
        {
            return ConnectionTestResult.Failed("✕ No se pudo conectar: Tiempo de espera agotado al conectar con OpenRouter (Timeout).", DefaultModel, DisplayName);
        }
        catch (HttpRequestException ex)
        {
            return ConnectionTestResult.Failed($"✕ No se pudo conectar: Endpoint no accesible ({ex.Message}).", DefaultModel, DisplayName);
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Failed($"✕ No se pudo conectar: Error inesperado ({ex.Message}).", DefaultModel, DisplayName);
        }
    }
}
