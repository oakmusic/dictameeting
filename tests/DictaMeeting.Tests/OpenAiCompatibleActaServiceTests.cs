using System.Net;
using System.Text;
using System.Text.Json;
using DictaMeeting.AI.Models;
using DictaMeeting.AI.Services;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using Xunit;

namespace DictaMeeting.Tests;

public class OpenAiCompatibleActaServiceTests
{
    private class FakeSecureStorage : ISecureStorageService
    {
        private readonly Dictionary<string, string> _store = new();

        public void SaveSecret(string key, string secret)
        {
            if (string.IsNullOrEmpty(secret)) _store.Remove(key);
            else _store[key] = secret;
        }

        public string? GetSecret(string key) => _store.TryGetValue(key, out var val) ? val : null;
        public void DeleteSecret(string key) => _store.Remove(key);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? HandlerFunc { get; set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (HandlerFunc != null)
            {
                return await HandlerFunc(request);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"# Acta Test\\nAcuerdos tomados.\"}}]}", Encoding.UTF8, "application/json")
            };
        }
    }

    // 1. BuildChatCompletionsUrl - Normalizes URL correctly whether /v1, /chat/completions, or trailing slash is provided
    [Theory]
    [InlineData("https://mi-servidor.com/v1", "https://mi-servidor.com/v1/chat/completions")]
    [InlineData("https://mi-servidor.com/v1/", "https://mi-servidor.com/v1/chat/completions")]
    [InlineData("https://mi-servidor.com", "https://mi-servidor.com/chat/completions")]
    [InlineData("https://mi-servidor.com/api/v1", "https://mi-servidor.com/api/v1/chat/completions")]
    [InlineData("https://mi-servidor.com/v1/chat/completions", "https://mi-servidor.com/v1/chat/completions")]
    public void BuildChatCompletionsUrl_NormalizesCorrectly(string input, string expected)
    {
        var url = OpenAiCompatibleActaService.BuildChatCompletionsUrl(input);
        Assert.Equal(expected, url);
    }

    // 2. Endpoint válido + API Key válida genera acta con payload OpenAI limpio (sin reasoning ni temperature)
    [Fact]
    public async Task GenerateActaAsync_WithValidEndpointAndKey_GeneratesActaCleanPayload()
    {
        string? capturedBody = null;
        string? authHeader = null;
        Uri? requestUri = null;

        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = async request =>
            {
                requestUri = request.RequestUri;
                authHeader = request.Headers.Authorization?.ToString();
                capturedBody = await request.Content!.ReadAsStringAsync();

                var responseObj = new
                {
                    id = "chatcmpl-test-123",
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                role = "assistant",
                                content = "# Acta de Reunión\n\n## Acuerdos\n1. Iniciar despliegue."
                            }
                        }
                    },
                    usage = new
                    {
                        prompt_tokens = 500,
                        completion_tokens = 200,
                        total_tokens = 700
                    }
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseObj), Encoding.UTF8, "application/json")
                };
            }
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));

        var meeting = new Meeting { Title = "Reunión de Estrategia" };
        meeting.Transcript.Add(new TranscriptSegment { SpeakerId = "SPEAKER_00", Text = "Debemos aprobar el presupuesto." });

        var options = new ActaGenerationOptions
        {
            Endpoint = "https://litellm-proxy.empresa.local/v1",
            CustomModelName = "qwen3.8-27b",
            ApiKey = "secret-token-xyz-123"
        };

        var result = await service.GenerateActaAsync(meeting, options);

        Assert.True(result.Success);
        Assert.Contains("Acta de Reunión", result.MarkdownContent);
        Assert.Equal(500, result.PromptTokensUsed);
        Assert.Equal(200, result.CompletionTokensUsed);
        Assert.Equal(700, result.TotalTokensUsed);
        Assert.Equal("qwen3.8-27b", result.ModelUsed);

        Assert.Equal("https://litellm-proxy.empresa.local/v1/chat/completions", requestUri?.ToString());
        Assert.Equal("Bearer secret-token-xyz-123", authHeader);

        // Verify generic OpenAI payload: NO reasoning, NO temperature
        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody);
        var root = doc.RootElement;
        Assert.Equal("qwen3.8-27b", root.GetProperty("model").GetString());
        Assert.True(root.TryGetProperty("messages", out var messages));
        Assert.Equal(2, messages.GetArrayLength()); // system + user
        Assert.Equal(4000, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.TryGetProperty("reasoning", out _));
        Assert.False(root.TryGetProperty("temperature", out _));
    }

    // 3. Fallo de autenticación (401 / 403) devuelve mensaje claro y NUNCA expone la API Key
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Error de autenticación (HTTP 401)")]
    [InlineData(HttpStatusCode.Forbidden, "Error de autenticación o acceso denegado (HTTP 403)")]
    public async Task GenerateActaAsync_InvalidApiKey_ReturnsClearAuthErrorWithoutExposingKey(HttpStatusCode statusCode, string expectedSnippet)
    {
        const string secretKey = "super-sensitive-secret-token-99999";
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Invalid API Key\"}}", Encoding.UTF8, "application/json")
            })
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));

        var meeting = new Meeting { Title = "Reunión" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Hola mundo" });

        var options = new ActaGenerationOptions
        {
            Endpoint = "https://my-server.com/v1",
            CustomModelName = "my-model",
            ApiKey = secretKey
        };

        var result = await service.GenerateActaAsync(meeting, options);

        Assert.False(result.Success);
        Assert.Contains(expectedSnippet, result.ErrorMessage);
        Assert.DoesNotContain(secretKey, result.ErrorMessage);
    }

    // 4. Modelo no encontrado (404)
    [Fact]
    public async Task GenerateActaAsync_NonExistentModel_ReturnsModelNotFoundError()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Model non-existent-model does not exist.\"}}", Encoding.UTF8, "application/json")
            })
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));

        var meeting = new Meeting { Title = "Test" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Texto" });

        var options = new ActaGenerationOptions
        {
            Endpoint = "https://my-server.com/v1",
            CustomModelName = "non-existent-model",
            ApiKey = "key123"
        };

        var result = await service.GenerateActaAsync(meeting, options);

        Assert.False(result.Success);
        Assert.Contains("Modelo no disponible", result.ErrorMessage);
    }

    // 5. Endpoint inaccesible (HttpRequestException)
    [Fact]
    public async Task GenerateActaAsync_InaccessibleEndpoint_ReturnsEndpointError()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => throw new HttpRequestException("No such host is known.")
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));

        var meeting = new Meeting { Title = "Test" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Texto" });

        var options = new ActaGenerationOptions
        {
            Endpoint = "https://invalid-host-unreachable-999.local/v1",
            CustomModelName = "model",
            ApiKey = "key"
        };

        var result = await service.GenerateActaAsync(meeting, options);

        Assert.False(result.Success);
        Assert.Contains("Endpoint no accesible", result.ErrorMessage);
    }

    // 6. Timeout / Cancelación
    [Fact]
    public async Task GenerateActaAsync_Timeout_ReturnsTimeoutErrorMessage()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => throw new TaskCanceledException("The operation was canceled due to timeout.")
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));

        var meeting = new Meeting { Title = "Test" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Texto" });

        var options = new ActaGenerationOptions
        {
            Endpoint = "https://slow-server.com/v1",
            CustomModelName = "model",
            ApiKey = "key"
        };

        var result = await service.GenerateActaAsync(meeting, options);

        Assert.False(result.Success);
        Assert.Contains("Timeout excedido", result.ErrorMessage);
    }

    // 7. Respuesta incompatible o malformada (sin choices / contenido vacío)
    [Theory]
    [InlineData("{\"invalid\": true}")]
    [InlineData("{\"choices\": []}")]
    [InlineData("{\"choices\": [{\"message\": {\"content\": null}}]}")]
    [InlineData("{\"choices\": [{\"message\": {}}]}")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    public async Task GenerateActaAsync_IncompatibleResponse_ReturnsDescriptiveError(string responseBody)
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            })
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));

        var meeting = new Meeting { Title = "Test" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Texto" });

        var options = new ActaGenerationOptions
        {
            Endpoint = "https://my-server.com/v1",
            CustomModelName = "model",
            ApiKey = "key"
        };

        var result = await service.GenerateActaAsync(meeting, options);

        Assert.False(result.Success);
        Assert.Contains("Respuesta incompatible", result.ErrorMessage);
    }

    // 8. Test Connection - Éxito
    [Fact]
    public async Task TestConnectionAsync_Success_ReturnsConnectedMessage()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = request =>
            {
                Assert.Equal("https://custom-gateway.local/v1/chat/completions", request.RequestUri?.ToString());
                Assert.Equal("Bearer key-12345", request.Headers.Authorization?.ToString());

                var responseJson = new
                {
                    choices = new[]
                    {
                        new { message = new { content = "PONG" } }
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseJson), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));

        var testResult = await service.TestConnectionAsync("https://custom-gateway.local/v1", "my-qwen-model", "key-12345");

        Assert.True(testResult.Success);
        Assert.Contains("✓ Conexión correcta", testResult.Message);
        Assert.Equal("my-qwen-model", testResult.ModelTested);
    }

    // 9. Test Connection - Fallo con endpoint vacío o modelo vacío
    [Fact]
    public async Task TestConnectionAsync_MissingParameters_FailsWithoutNetworkCall()
    {
        var service = new OpenAiCompatibleActaService();

        var res1 = await service.TestConnectionAsync("", "model", "key");
        Assert.False(res1.Success);
        Assert.Contains("Endpoint no accesible", res1.Message);

        var res2 = await service.TestConnectionAsync("https://url.com", "", "key");
        Assert.False(res2.Success);
        Assert.Contains("Modelo no disponible", res2.Message);

        var res3 = await service.TestConnectionAsync("https://url.com", "model", "");
        Assert.False(res3.Success);
        Assert.Contains("Error de autenticación", res3.Message);
    }

    // 10. Test Connection - Error 401 sin exponer la clave en el mensaje
    [Fact]
    public async Task TestConnectionAsync_Unauthorized_ReturnsClearErrorWithoutExposingKey()
    {
        const string sensitiveKey = "super-secret-pw-123456789";
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"Invalid key\"}", Encoding.UTF8, "application/json")
            })
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));

        var result = await service.TestConnectionAsync("https://gateway.com/v1", "test-model", sensitiveKey);

        Assert.False(result.Success);
        Assert.Contains("✕ No se pudo conectar", result.Message);
        Assert.Contains("Error de autenticación", result.Message);
        Assert.DoesNotContain(sensitiveKey, result.Message);
    }

    // 11. Uso de ISecureStorageService para API Key persistida
    [Fact]
    public async Task GenerateActaAsync_ReadsKeyFromSecureStorage_WhenOptionsApiKeyNotProvided()
    {
        var fakeStorage = new FakeSecureStorage();
        fakeStorage.SaveSecret(OpenAiCompatibleActaService.CustomServerApiKeyStorageKey, "dpapi-stored-key");

        string? capturedAuth = null;
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = request =>
            {
                capturedAuth = request.Headers.Authorization?.ToString();
                var resp = new
                {
                    choices = new[]
                    {
                        new { message = new { content = "# Acta Guardada" } }
                    }
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(resp), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler), fakeStorage);

        var meeting = new Meeting { Title = "Test" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Texto" });

        var options = new ActaGenerationOptions
        {
            Endpoint = "https://my-server.com/v1",
            CustomModelName = "test-model",
            ApiKey = null // Empty, should fallback to secureStorage
        };

        var result = await service.GenerateActaAsync(meeting, options);

        Assert.True(result.Success);
        Assert.Equal("Bearer dpapi-stored-key", capturedAuth);
    }

    // 12. La API Key no aparece NUNCA en los logs bajo ninguna circunstancia (éxito o error)
    private class TestLogCollector : Microsoft.Extensions.Logging.ILogger<OpenAiCompatibleActaService>
    {
        public List<string> LoggedMessages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            LoggedMessages.Add(msg);
            if (exception != null)
            {
                LoggedMessages.Add(exception.ToString());
            }
        }
    }

    [Fact]
    public async Task GenerateActaAsync_ApiKeyNeverAppearsInLogs_EvenOnErrorOrSuccess()
    {
        const string ultraSecretApiKey = "sk-super-secret-production-token-9999999";
        var logger = new TestLogCollector();

        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Unauthorized\"}}", Encoding.UTF8, "application/json")
            })
        };

        var service = new OpenAiCompatibleActaService(
            new HttpClient(mockHandler),
            logger: logger);

        var meeting = new Meeting { Title = "Test" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Hola" });

        var options = new ActaGenerationOptions
        {
            Endpoint = "https://custom.host/v1",
            CustomModelName = "qwen",
            ApiKey = ultraSecretApiKey
        };

        var result = await service.GenerateActaAsync(meeting, options);
        Assert.False(result.Success);

        // Assert: ultraSecretApiKey should NEVER appear in any logged line
        Assert.NotEmpty(logger.LoggedMessages);
        foreach (var loggedLine in logger.LoggedMessages)
        {
            Assert.DoesNotContain(ultraSecretApiKey, loggedLine);
        }
        Assert.DoesNotContain(ultraSecretApiKey, result.ErrorMessage);
    }

    // 13. Test Connection - Modelo de razonamiento local (DeepSeek-R1 / Ollama) con content vacío y reasoning_content
    [Fact]
    public async Task TestConnectionAsync_ReasoningModelWithEmptyContent_ReturnsSuccess()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = async request =>
            {
                var body = await request.Content!.ReadAsStringAsync();
                Assert.Contains("\"max_tokens\":50", body);

                var responseJson = new
                {
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                role = "assistant",
                                content = "",
                                reasoning_content = "Pensando en responder únicamente OK..."
                            },
                            finish_reason = "length"
                        }
                    }
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseJson), Encoding.UTF8, "application/json")
                };
            }
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));
        var result = await service.TestConnectionAsync("http://localhost:11434/v1", "deepseek-r1:latest", "ollama");

        Assert.True(result.Success);
        Assert.Contains("✓ Conexión correcta", result.Message);
        Assert.Equal("deepseek-r1:latest", result.ModelTested);
    }

    // 14. Test Connection - Servidor local devolviendo message con content null y finish_reason length
    [Fact]
    public async Task TestConnectionAsync_LocalServerWithNullContentAndFinishReason_ReturnsSuccess()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ =>
            {
                var json = "{\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":null},\"finish_reason\":\"length\"}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));
        var result = await service.TestConnectionAsync("http://localhost:1234/v1", "local-model", "not-needed");

        Assert.True(result.Success);
        Assert.Contains("✓ Conexión correcta", result.Message);
    }

    // 15. Test Connection - Formato array structured content (multimodal/blocks)
    [Fact]
    public async Task TestConnectionAsync_ArrayContentBlocks_ReturnsSuccess()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ =>
            {
                var json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"OK\"}]}}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));
        var result = await service.TestConnectionAsync("http://localhost:8000/v1", "qwen", "key");

        Assert.True(result.Success);
        Assert.Contains("✓ Conexión correcta", result.Message);
    }

    // 16. Test Connection - Formato legacy completions con choices[0].text
    [Fact]
    public async Task TestConnectionAsync_LegacyCompletionsTextFormat_ReturnsSuccess()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ =>
            {
                var json = "{\"choices\":[{\"text\":\"OK\",\"finish_reason\":\"stop\"}]}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));
        var result = await service.TestConnectionAsync("http://localhost:5000/v1", "text-model", "key");

        Assert.True(result.Success);
        Assert.Contains("✓ Conexión correcta", result.Message);
    }

    // 17. Test Connection - Respuesta totalmente incompatible (sin choices / no json) sigue fallando
    [Fact]
    public async Task TestConnectionAsync_IncompatibleResponse_ReturnsIncompatibleError()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"invalid_prop\": true}", Encoding.UTF8, "application/json")
            })
        };

        var service = new OpenAiCompatibleActaService(new HttpClient(mockHandler));
        var result = await service.TestConnectionAsync("http://localhost:5000/v1", "text-model", "key");

        Assert.False(result.Success);
        Assert.Contains("Respuesta incompatible", result.Message);
    }
}
