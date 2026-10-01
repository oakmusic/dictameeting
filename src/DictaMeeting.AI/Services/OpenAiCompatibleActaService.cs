using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Vocabulary;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Servicio genérico de generación de actas ejecutivas mediante cualquier servidor compatible con OpenAI Chat Completions
/// (por ejemplo, LiteLLM Proxy, gateways internos de empresa, servidores privados vLLM, Ollama, etc.).
/// No contiene dependencias, credenciales ni URLs específicas de ninguna empresa.
/// </summary>
public class OpenAiCompatibleActaService : IAiActaService, IAiActaProvider
{
    public const string ProviderIdentifier = "CustomServer";
    public const string ProviderDisplayName = "Servidor personalizado";
    public const string CustomServerApiKeyStorageKey = "CustomServer_ApiKey";
    public const string CustomServerModelId = "custom_server";
    public const string DefaultDescription = "Conecta DictaMeeting a un servidor compatible con la API de OpenAI, como LiteLLM.";

    public string ProviderId => ProviderIdentifier;
    public string DisplayName => ProviderDisplayName;

    private readonly HttpClient _httpClient;
    private readonly ISecureStorageService? _secureStorage;
    private readonly IUserSettingsService? _userSettingsService;
    private readonly IVocabularyService? _vocabularyService;
    private readonly ILogger<OpenAiCompatibleActaService>? _logger;

    private static readonly HttpClient DefaultSharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(120)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public OpenAiCompatibleActaService(
        HttpClient? httpClient = null,
        ISecureStorageService? secureStorage = null,
        IUserSettingsService? userSettingsService = null,
        IVocabularyService? vocabularyService = null,
        ILogger<OpenAiCompatibleActaService>? logger = null)
    {
        _httpClient = httpClient ?? DefaultSharedHttpClient;
        _secureStorage = secureStorage;
        _userSettingsService = userSettingsService;
        _vocabularyService = vocabularyService;
        _logger = logger;
    }

    public bool SupportsModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)) return false;
        return modelId.Equals(CustomServerModelId, StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<AiModelOption> GetRecommendedModels()
    {
        return new List<AiModelOption>
        {
            new(CustomServerModelId, "Servidor personalizado", ProviderDisplayName, DefaultDescription)
        };
    }

    public string BuildPrompt(Meeting meeting, ActaGenerationOptions options)
    {
        return ActaPromptBuilder.BuildPrompt(meeting, options, _vocabularyService);
    }

    public static string BuildChatCompletionsUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return string.Empty;
        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        return $"{trimmed}/chat/completions";
    }

    public async Task<ActaGenerationResult> GenerateActaAsync(
        Meeting meeting,
        ActaGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        // 1. Resolver configuración (de las opciones o del almacenamiento persistente)
        var settings = _userSettingsService?.LoadSettings();

        var endpoint = !string.IsNullOrWhiteSpace(options.Endpoint)
            ? options.Endpoint.Trim()
            : settings?.CustomAiEndpoint?.Trim();

        var modelToUse = !string.IsNullOrWhiteSpace(options.CustomModelName)
            ? options.CustomModelName.Trim()
            : (!string.IsNullOrWhiteSpace(settings?.CustomAiModel) ? settings.CustomAiModel.Trim() : null);

        var apiKey = !string.IsNullOrWhiteSpace(options.ApiKey)
            ? options.ApiKey.Trim()
            : _secureStorage?.GetSecret(CustomServerApiKeyStorageKey)?.Trim();

        // 2. Validaciones estrictas previas
        if (meeting.Transcript.Count == 0)
        {
            _logger?.LogWarning("Intento de generar acta sobre una reunión sin transcripción.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = "La reunión no contiene fragmentos de transcripción. No es posible redactar un acta sin contenido hablado."
            };
        }

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(modelToUse) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger?.LogWarning("Servidor personalizado no configurado completamente (Endpoint, Modelo o API Key ausente).");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = "Debe configurar el Servidor personalizado (Endpoint, Modelo y API Key) en Ajustes antes de generar el acta."
            };
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uriResult) ||
            (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
        {
            _logger?.LogWarning("El endpoint configurado para el servidor personalizado no es una URL válida.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = "El endpoint del Servidor personalizado no es una URL válida (debe comenzar por http:// o https://)."
            };
        }

        try
        {
            // 3. Construir mensajes compatibles con OpenAI Chat Completions
            var fullPrompt = BuildPrompt(meeting, options);
            var systemPrompt = ActaPromptBuilder.BuildSystemPrompt(options);

            var messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = fullPrompt }
            };

            // Payload básico y universal OpenAI-compatible (sin reasoning ni parámetros propietarios)
            var requestPayload = new
            {
                model = modelToUse,
                messages,
                max_tokens = 4000
            };

            var jsonContent = JsonSerializer.Serialize(requestPayload, JsonOptions);
            var targetUrl = BuildChatCompletionsUrl(endpoint);

            using var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger?.LogInformation("Enviando solicitud de generación de acta al servidor personalizado en '{Url}' con modelo '{Model}'...", targetUrl, modelToUse);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorDetail = ParseErrorMessage(responseBody, response.StatusCode);
                _logger?.LogError("Error devuelto por el servidor personalizado ({StatusCode}): {ErrorDetail}", response.StatusCode, errorDetail);
                return new ActaGenerationResult
                {
                    Success = false,
                    ErrorMessage = errorDetail,
                    ModelUsed = modelToUse
                };
            }

            // 4. Procesar respuesta exitosa según especificación OpenAI Chat Completions
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            string? markdownContent = null;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                markdownContent = ExtractContentFromChoice(firstChoice);
            }

            if (string.IsNullOrWhiteSpace(markdownContent))
            {
                return new ActaGenerationResult
                {
                    Success = false,
                    ErrorMessage = "Respuesta incompatible o vacía: el servidor personalizado no devolvió contenido en choices[0].message.content.",
                    ModelUsed = modelToUse
                };
            }

            markdownContent = ActaPromptBuilder.SanitizeActaMarkdown(markdownContent, meeting, options);

            int? promptTokens = null;
            int? completionTokens = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var ptVal)) promptTokens = ptVal;
                if (usage.TryGetProperty("completion_tokens", out var ct) && ct.TryGetInt32(out var ctVal)) completionTokens = ctVal;
            }

            _logger?.LogInformation("Acta generada con éxito con Servidor personalizado. Tokens: prompt={PromptTokens}, completion={CompletionTokens}.", promptTokens, completionTokens);

            return new ActaGenerationResult
            {
                Success = true,
                MarkdownContent = markdownContent,
                ModelUsed = modelToUse,
                PromptTokensUsed = promptTokens,
                CompletionTokensUsed = completionTokens
            };
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Error al interpretar la respuesta JSON del servidor personalizado.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = $"Respuesta incompatible del servidor: no se pudo interpretar el formato JSON ({ex.Message}).",
                ModelUsed = modelToUse
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation("Generación de acta cancelada por el usuario en servidor personalizado.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = "Generación de acta cancelada.",
                ModelUsed = modelToUse
            };
        }
        catch (TaskCanceledException)
        {
            _logger?.LogError("Tiempo de espera agotado al conectar con el servidor personalizado.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = "Timeout excedido: el tiempo de espera de la solicitud al servidor personalizado se ha agotado. Compruebe la conexión o reintente.",
                ModelUsed = modelToUse
            };
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "Error de red al conectar con el servidor personalizado.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = $"Endpoint no accesible o error de red: {ex.Message}. Verifique el endpoint y la disponibilidad de conexión.",
                ModelUsed = modelToUse
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error inesperado durante la generación del acta con servidor personalizado.");
            return new ActaGenerationResult
            {
                Success = false,
                ErrorMessage = $"Error inesperado al generar el acta con servidor personalizado: {ex.Message}",
                ModelUsed = modelToUse
            };
        }
    }

    public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = _userSettingsService?.LoadSettings();
        var endpoint = settings?.CustomAiEndpoint;
        var model = settings?.CustomAiModel;
        var apiKey = _secureStorage?.GetSecret(CustomServerApiKeyStorageKey);

        return TestConnectionAsync(endpoint, model, apiKey, cancellationToken);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        string? endpoint,
        string? model,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        var ep = endpoint?.Trim();
        var mdl = model?.Trim();
        var key = apiKey?.Trim();

        // 1. Validar endpoint
        if (string.IsNullOrWhiteSpace(ep))
        {
            return ConnectionTestResult.Failed("✕ No se pudo conectar: Endpoint no accesible (el campo Endpoint no puede estar vacío).", mdl, DisplayName);
        }

        if (!Uri.TryCreate(ep, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return ConnectionTestResult.Failed("✕ No se pudo conectar: Endpoint no accesible (la URL no es válida, debe comenzar por http:// o https://).", mdl, DisplayName);
        }

        // 2. Validar modelo
        if (string.IsNullOrWhiteSpace(mdl))
        {
            return ConnectionTestResult.Failed("✕ No se pudo conectar: Modelo no disponible (el campo Modelo no puede estar vacío).", mdl, DisplayName);
        }

        // 3. Validar API Key
        if (string.IsNullOrWhiteSpace(key))
        {
            return ConnectionTestResult.Failed("✕ No se pudo conectar: Error de autenticación (la API Key no puede estar vacía).", mdl, DisplayName);
        }

        try
        {
            var targetUrl = BuildChatCompletionsUrl(ep);

            var payload = new
            {
                model = mdl,
                messages = new[]
                {
                    new { role = "user", content = "Di únicamente OK." }
                },
                max_tokens = 50
            };

            var jsonContent = JsonSerializer.Serialize(payload, JsonOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorDetail = ParseErrorMessage(responseBody, response.StatusCode);
                return ConnectionTestResult.Failed($"✕ No se pudo conectar: {errorDetail}", mdl, DisplayName);
            }

            // 4. Verificar estructura OpenAI-compatible y extracción de texto
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                var extracted = ExtractContentFromChoice(firstChoice);

                // Si pudimos extraer contenido textual
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    return ConnectionTestResult.Ok("✓ Conexión correcta", mdl, DisplayName);
                }

                // Si la estructura OpenAI es formalmente válida aunque el texto esté vacío o en proceso de razonamiento
                // (por ejemplo si contiene 'message', 'text', 'finish_reason' o 'delta')
                if (firstChoice.TryGetProperty("message", out _) ||
                    firstChoice.TryGetProperty("text", out _) ||
                    firstChoice.TryGetProperty("finish_reason", out _) ||
                    firstChoice.TryGetProperty("delta", out _))
                {
                    return ConnectionTestResult.Ok("✓ Conexión correcta", mdl, DisplayName);
                }
            }

            return ConnectionTestResult.Failed("✕ No se pudo conectar: Respuesta incompatible (el servidor no devolvió una estructura con choices[0].message.content).", mdl, DisplayName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ConnectionTestResult.Failed("✕ No se pudo conectar: Prueba de conexión cancelada.", mdl, DisplayName);
        }
        catch (TaskCanceledException)
        {
            return ConnectionTestResult.Failed("✕ No se pudo conectar: Tiempo de espera agotado al conectar con el servidor (Timeout).", mdl, DisplayName);
        }
        catch (HttpRequestException ex)
        {
            return ConnectionTestResult.Failed($"✕ No se pudo conectar: Endpoint no accesible ({ex.Message}).", mdl, DisplayName);
        }
        catch (Exception ex)
        {
            return ConnectionTestResult.Failed($"✕ No se pudo conectar: {ex.Message}", mdl, DisplayName);
        }
    }

    private static string ParseErrorMessage(string responseBody, System.Net.HttpStatusCode statusCode)
    {
        string? serverMessage = null;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorObj))
            {
                if (errorObj.ValueKind == JsonValueKind.String)
                {
                    serverMessage = errorObj.GetString();
                }
                else if (errorObj.TryGetProperty("message", out var msgElem) && !string.IsNullOrWhiteSpace(msgElem.GetString()))
                {
                    serverMessage = msgElem.GetString();
                }
            }
        }
        catch
        {
            // Ignorar fallo de parseo JSON para proceder al código de estado
        }

        string defaultDesc = statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "Error de autenticación (HTTP 401). Verifique la API Key configurada.",
            System.Net.HttpStatusCode.Forbidden => "Error de autenticación o acceso denegado (HTTP 403). Verifique los permisos de la API Key.",
            System.Net.HttpStatusCode.NotFound => "Modelo no disponible o endpoint no encontrado (HTTP 404). Verifique el nombre del modelo.",
            (System.Net.HttpStatusCode)429 => "Límite de solicitudes alcanzado en el servidor (HTTP 429).",
            _ => $"Error devuelto por el servidor: código HTTP {(int)statusCode} ({statusCode})."
        };

        if (!string.IsNullOrWhiteSpace(serverMessage))
        {
            return $"{defaultDesc} Detalle: {serverMessage}";
        }

        return defaultDesc;
    }

    /// <summary>
    /// Extrae contenido textual desde un elemento choice compatible con la API de OpenAI,
    /// soportando string directo en message.content, arrays estructurados de bloques de texto (multimodal/structured content),
    /// reasoning_content / reasoning (DeepSeek-R1 / Ollama / LM Studio), choices[0].text (legacy completions) o delta.
    /// </summary>
    public static string? ExtractContentFromChoice(JsonElement choice)
    {
        // 1. Chat Completions estándar: choice.message
        if (choice.TryGetProperty("message", out var msg))
        {
            if (msg.TryGetProperty("content", out var contentElem))
            {
                if (contentElem.ValueKind == JsonValueKind.String)
                {
                    var str = contentElem.GetString();
                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        return str;
                    }
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
                    if (!string.IsNullOrWhiteSpace(combined))
                    {
                        return combined;
                    }
                }
            }

            // 1b. Fallback a reasoning_content / reasoning (modelos CoT como DeepSeek-R1 en Ollama o LM Studio)
            if (msg.TryGetProperty("reasoning_content", out var rcElem) &&
                rcElem.ValueKind == JsonValueKind.String)
            {
                var rc = rcElem.GetString();
                if (!string.IsNullOrWhiteSpace(rc))
                {
                    return rc;
                }
            }

            if (msg.TryGetProperty("reasoning", out var rElem) &&
                rElem.ValueKind == JsonValueKind.String)
            {
                var r = rElem.GetString();
                if (!string.IsNullOrWhiteSpace(r))
                {
                    return r;
                }
            }
        }

        // 2. Fallback a choices[0].text (formato completions legacy o proxies híbridos)
        if (choice.TryGetProperty("text", out var textElemFallback) &&
            textElemFallback.ValueKind == JsonValueKind.String)
        {
            var text = textElemFallback.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        // 3. Fallback a choices[0].delta.content (formato streaming/delta)
        if (choice.TryGetProperty("delta", out var deltaElem) &&
            deltaElem.TryGetProperty("content", out var deltaContent) &&
            deltaContent.ValueKind == JsonValueKind.String)
        {
            var delta = deltaContent.GetString();
            if (!string.IsNullOrWhiteSpace(delta))
            {
                return delta;
            }
        }

        return null;
    }
}
