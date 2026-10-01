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
/// Servicio de generación de actas ejecutivas mediante la API de OpenRouter.
/// Aplica prompts estrictos anti-alucinaciones, respeta la privacidad de datos (cero audio transmitido),
/// y recupera la API Key de forma segura mediante Windows DPAPI.
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
        Timeout = TimeSpan.FromSeconds(120)
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

    public async Task<ActaGenerationResult> GenerateActaAsync(
        Meeting meeting,
        ActaGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
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
            // 3. Construir mensajes para la API de Chat Completions
            var fullPrompt = BuildPrompt(meeting, options);

            var systemPrompt = ActaPromptBuilder.BuildSystemPrompt(options);

            var messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = fullPrompt }
            };

            var reasoning = new
            {
                effort = "high",
                max_tokens = 6000,
                exclude = true
            };

            object requestPayload = modelToUse.Equals("openai/gpt-6-luna", StringComparison.OrdinalIgnoreCase)
                ? new
                {
                    model = modelToUse,
                    messages,
                    reasoning,
                    max_tokens = 4000
                }
                : new
                {
                    model = modelToUse,
                    messages,
                    reasoning,
                    max_tokens = 4000,
                    temperature = options.Temperature
                };

            var jsonContent = JsonSerializer.Serialize(requestPayload, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, DefaultEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Add("HTTP-Referer", HttpReferer);
            request.Headers.Add("X-Title", AppTitle);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger?.LogInformation("Enviando solicitud de generación de acta a OpenRouter con modelo '{Model}'...", modelToUse);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorDetail = ParseErrorMessage(responseBody, response.StatusCode);
                _logger?.LogError("Error devuelto por OpenRouter ({StatusCode}): {ErrorDetail}", response.StatusCode, errorDetail);
                return new ActaGenerationResult
                {
                    Success = false,
                    ErrorMessage = errorDetail,
                    ModelUsed = modelToUse
                };
            }

            // 4. Procesar respuesta exitosa
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            string? markdownContent = null;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var messageObj) &&
                    messageObj.TryGetProperty("content", out var contentElem))
                {
                    markdownContent = contentElem.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(markdownContent))
            {
                return new ActaGenerationResult
                {
                    Success = false,
                    ErrorMessage = "OpenRouter devolvió una respuesta vacía.",
                    ModelUsed = modelToUse
                };
            }

            markdownContent = SanitizeActaMarkdown(markdownContent, meeting, options);

            int? promptTokens = null;
            int? completionTokens = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ct)) completionTokens = ct.GetInt32();
            }

            _logger?.LogInformation("Acta generada con éxito con OpenRouter. Tokens: prompt={PromptTokens}, completion={CompletionTokens}.", promptTokens, completionTokens);

            return new ActaGenerationResult
            {
                Success = true,
                MarkdownContent = markdownContent,
                ModelUsed = modelToUse,
                PromptTokensUsed = promptTokens,
                CompletionTokensUsed = completionTokens
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

    private static string ParseErrorMessage(string responseBody, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.TryGetProperty("message", out var msgElem) && !string.IsNullOrWhiteSpace(msgElem.GetString()))
                {
                    return msgElem.GetString()!;
                }
            }
        }
        catch
        {
            // Fallback a mapeo de códigos comunes si el JSON de error no es estándar
        }

        return statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "Clave de API de OpenRouter no válida o revocada (Error 401). Verifique su API Key en la configuración.",
            System.Net.HttpStatusCode.PaymentRequired => "Crédito insuficiente en su cuenta de OpenRouter (Error 402). Revise su saldo en openrouter.ai.",
            (System.Net.HttpStatusCode)429 => "Límite de solicitudes alcanzado en OpenRouter (Error 429). Por favor espere unos segundos antes de reintentar.",
            System.Net.HttpStatusCode.NotFound => "El modelo seleccionado no está disponible en OpenRouter (Error 404).",
            _ => $"Error devuelto por el servidor de OpenRouter: {(int)statusCode} ({statusCode})."
        };
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
