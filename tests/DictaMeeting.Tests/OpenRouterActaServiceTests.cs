using System.Net;
using System.Text;
using System.Text.Json;
using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.AI.Services;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using Xunit;

namespace DictaMeeting.Tests;

public class OpenRouterActaServiceTests
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
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"Acta OK\"}}]}", Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public void GetRecommendedModels_ReturnsConfiguredTwoModelsInOrder()
    {
        var service = new OpenRouterActaService();
        var models = service.GetRecommendedModels();

        Assert.Equal(2, models.Count);
        Assert.Equal("openai/gpt-6-luna", models[0].Id);
        Assert.Equal("qwen/qwen3.8-flash", models[1].Id);
        Assert.Equal("openai/gpt-6-luna", OpenRouterActaService.DefaultModel);
    }

    [Fact]
    public async Task GenerateActaAsync_ReturnsError_WhenNoApiKeyConfigured()
    {
        // Arrange
        var fakeStorage = new FakeSecureStorage(); // empty
        var service = new OpenRouterActaService(secureStorage: fakeStorage);

        var meeting = new Meeting { Title = "Test" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Hola" });

        var options = new ActaGenerationOptions { ApiKey = null };

        // Act
        var result = await service.GenerateActaAsync(meeting, options);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("No se ha configurado la API Key", result.ErrorMessage);
    }

    [Fact]
    public async Task GenerateActaAsync_ReturnsError_WhenTranscriptIsEmpty()
    {
        // Arrange
        var fakeStorage = new FakeSecureStorage();
        fakeStorage.SaveSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey, "sk-or-test-key");
        var service = new OpenRouterActaService(secureStorage: fakeStorage);

        var meeting = new Meeting { Title = "Reunión vacía" }; // no transcript segments
        var options = new ActaGenerationOptions();

        // Act
        var result = await service.GenerateActaAsync(meeting, options);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("no contiene fragmentos de transcripción", result.ErrorMessage);
    }

    [Fact]
    public async Task GenerateActaAsync_UsesSecureStorageKey_WhenOptionsApiKeyIsNull()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = request =>
            {
                Assert.Equal("Bearer sk-or-storage-key", request.Headers.Authorization?.ToString());
                Assert.Contains("DictaMeeting Windows App", request.Headers.GetValues("X-Title"));

                var responseJson = new
                {
                    choices = new[]
                    {
                        new { message = new { content = "# Acta Ejecutiva\nTodo acordado." } }
                    },
                    usage = new { prompt_tokens = 450, completion_tokens = 120 }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseJson), Encoding.UTF8, "application/json")
                });
            }
        };

        var httpClient = new HttpClient(mockHandler);
        var fakeStorage = new FakeSecureStorage();
        fakeStorage.SaveSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey, "sk-or-storage-key");

        var service = new OpenRouterActaService(httpClient, fakeStorage);

        var meeting = new Meeting { Title = "Reunión de Prueba" };
        meeting.Transcript.Add(new TranscriptSegment { SpeakerId = "SPEAKER_00", Text = "Primer punto del día." });

        var options = new ActaGenerationOptions { ModelName = "anthropic/claude-3.5-sonnet" };

        // Act
        var result = await service.GenerateActaAsync(meeting, options);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("# Acta Ejecutiva\nTodo acordado.", result.MarkdownContent);
        Assert.Equal(450, result.PromptTokensUsed);
        Assert.Equal(120, result.CompletionTokensUsed);
        Assert.Equal(570, result.TotalTokensUsed);
        Assert.Equal("anthropic/claude-3.5-sonnet", result.ModelUsed);
    }

    [Fact]
    public async Task GenerateActaAsync_HandlesHttpErrorStatus_Gracefully()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ =>
            {
                var errorObj = new
                {
                    error = new { message = "User has insufficient credits" }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PaymentRequired)
                {
                    Content = new StringContent(JsonSerializer.Serialize(errorObj), Encoding.UTF8, "application/json")
                });
            }
        };

        var httpClient = new HttpClient(mockHandler);
        var service = new OpenRouterActaService(httpClient);

        var meeting = new Meeting { Title = "Reunión de Presupuesto" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Texto" });

        var options = new ActaGenerationOptions { ApiKey = "sk-test" };

        // Act
        var result = await service.GenerateActaAsync(meeting, options);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("insufficient credits", result.ErrorMessage);
    }

    [Fact]
    public async Task GenerateActaAsync_HandlesUnauthorizedStatus401_WithClearMessage()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("Invalid credentials", Encoding.UTF8, "text/plain")
            })
        };

        var httpClient = new HttpClient(mockHandler);
        var service = new OpenRouterActaService(httpClient);

        var meeting = new Meeting { Title = "Reunión" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Texto" });

        var options = new ActaGenerationOptions { ApiKey = "sk-bad-key" };

        // Act
        var result = await service.GenerateActaAsync(meeting, options);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("no válida", result.ErrorMessage);
    }

    [Fact]
    public async Task GenerateActaAsync_HandlesNetworkException_Gracefully()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => throw new HttpRequestException("Connection refused")
        };

        var httpClient = new HttpClient(mockHandler);
        var service = new OpenRouterActaService(httpClient);

        var meeting = new Meeting { Title = "Reunión" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Texto" });

        var options = new ActaGenerationOptions { ApiKey = "sk-test" };

        // Act
        var result = await service.GenerateActaAsync(meeting, options);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Error de conexión", result.ErrorMessage);
    }

    [Fact]
    public void WindowsDpapiSecureStorageService_CanSave_Retrieve_AndDeleteSecret()
    {
        // Arrange
        var dpapiService = new WindowsDpapiSecureStorageService();
        var testKey = $"UnitTestKey_{Guid.NewGuid():N}";
        var secretValue = "sk-or-v1-secret-test-key-123456789";

        try
        {
            // Act: Guardar
            dpapiService.SaveSecret(testKey, secretValue);

            // Assert: Recuperar y verificar
            var retrieved = dpapiService.GetSecret(testKey);
            Assert.Equal(secretValue, retrieved);

            // Act: Eliminar
            dpapiService.DeleteSecret(testKey);

            // Assert: Verificar eliminación
            var afterDelete = dpapiService.GetSecret(testKey);
            Assert.Null(afterDelete);
        }
        finally
        {
            dpapiService.DeleteSecret(testKey);
        }
    }

    [Fact]
    public async Task GenerateActaAsync_WhenImpersonalIsTrue_SendsImpersonalInstructionsInPayload()
    {
        // Arrange
        string? capturedRequestBody = null;
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = request =>
            {
                capturedRequestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                var responseJson = new
                {
                    choices = new[]
                    {
                        new { message = new { content = "# Acta Impersonal\nSe ha acordado avanzar." } }
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseJson), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Reunión de Estrategia" };
        meeting.Transcript.Add(new TranscriptSegment { SpeakerId = "SPEAKER_00", Text = "Se propone cambiar el diseño." });

        var options = new ActaGenerationOptions
        {
            ApiKey = "sk-test",
            IsImpersonal = true
        };

        // Act
        var result = await service.GenerateActaAsync(meeting, options);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedRequestBody);
        Assert.Contains("MODALIDAD ACTA IMPERSONAL", capturedRequestBody);
        Assert.Contains("DIRECTRICES MANDATORIAS DE ACTA IMPERSONAL", capturedRequestBody);
        Assert.Contains("SPEAKER_00, SPEAKER_01", capturedRequestBody);
        Assert.Contains("Se ha acordado", capturedRequestBody);
    }

    [Fact]
    public async Task GenerateActaAsync_WithGpt6Luna_SendsExpectedReasoningAndNoTemperature()
    {
        // Arrange
        string? capturedRequestBody = null;
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = request =>
            {
                capturedRequestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                var responseJson = new
                {
                    choices = new[]
                    {
                        new { message = new { content = "# Acta OK" } }
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseJson), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Test Meeting" };
        meeting.Transcript.Add(new TranscriptSegment { SpeakerId = "SPEAKER_00", Text = "Contenido" });

        var options = new ActaGenerationOptions
        {
            ApiKey = "sk-test",
            ModelName = "openai/gpt-6-luna",
            Temperature = 0.2
        };

        // Act
        var result = await service.GenerateActaAsync(meeting, options);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedRequestBody);

        using var doc = JsonDocument.Parse(capturedRequestBody);
        var root = doc.RootElement;
        Assert.Equal("openai/gpt-6-luna", root.GetProperty("model").GetString());
        Assert.Equal(4000, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.TryGetProperty("temperature", out _));

        var reasoning = root.GetProperty("reasoning");
        Assert.Equal("high", reasoning.GetProperty("effort").GetString());
        Assert.False(reasoning.TryGetProperty("max_tokens", out _), "OpenRouter reasoning NO debe incluir max_tokens si ya se especifica effort.");
        Assert.True(reasoning.GetProperty("exclude").GetBoolean());
    }

    [Fact]
    public async Task GenerateActaAsync_WithQwen38Flash_SendsExpectedReasoningAndTemperature()
    {
        // Arrange
        string? capturedRequestBody = null;
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = request =>
            {
                capturedRequestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                var responseJson = new
                {
                    choices = new[]
                    {
                        new { message = new { content = "# Acta OK" } }
                    }
                };

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseJson), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Test Meeting" };
        meeting.Transcript.Add(new TranscriptSegment { SpeakerId = "SPEAKER_00", Text = "Contenido" });

        var options = new ActaGenerationOptions
        {
            ApiKey = "sk-test",
            ModelName = "qwen/qwen3.8-flash",
            Temperature = 0.2
        };

        // Act
        var result = await service.GenerateActaAsync(meeting, options);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedRequestBody);

        using var doc = JsonDocument.Parse(capturedRequestBody);
        var root = doc.RootElement;
        Assert.Equal("qwen/qwen3.8-flash", root.GetProperty("model").GetString());
        Assert.Equal(4000, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.2, root.GetProperty("temperature").GetDouble(), precision: 2);

        var reasoning = root.GetProperty("reasoning");
        Assert.Equal("high", reasoning.GetProperty("effort").GetString());
        Assert.False(reasoning.TryGetProperty("max_tokens", out _), "OpenRouter reasoning NO debe incluir max_tokens si ya se especifica effort.");
        Assert.True(reasoning.GetProperty("exclude").GetBoolean());
    }

    // 1. La request de OpenRouter NO contiene simultáneamente reasoning.effort y reasoning.max_tokens
    [Fact]
    public async Task GenerateActaAsync_NeverSendsEffortAndMaxTokensSimultaneously()
    {
        // Caso A: Opciones por defecto (envía effort, NO max_tokens dentro de reasoning)
        string? capturedBodyDefault = null;
        var mockHandlerA = new MockHttpMessageHandler
        {
            HandlerFunc = request =>
            {
                capturedBodyDefault = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                var responseJson = new { choices = new[] { new { message = new { content = "# Acta A" } } } };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseJson), Encoding.UTF8, "application/json")
                });
            }
        };

        var serviceA = new OpenRouterActaService(new HttpClient(mockHandlerA));
        var meeting = new Meeting { Title = "Reunión" };
        meeting.Transcript.Add(new TranscriptSegment { SpeakerId = "SPEAKER_00", Text = "Texto de debate" });

        var optionsDefault = new ActaGenerationOptions { ApiKey = "sk-test", ModelName = "qwen/qwen3.8-flash" };
        await serviceA.GenerateActaAsync(meeting, optionsDefault);

        Assert.NotNull(capturedBodyDefault);
        using (var docA = JsonDocument.Parse(capturedBodyDefault))
        {
            var reasoningA = docA.RootElement.GetProperty("reasoning");
            bool hasEffort = reasoningA.TryGetProperty("effort", out _);
            bool hasMaxTokens = reasoningA.TryGetProperty("max_tokens", out _);

            Assert.True(hasEffort, "Por defecto debe enviar effort.");
            Assert.False(hasMaxTokens, "Por defecto NO debe enviar max_tokens dentro de reasoning.");
            Assert.False(hasEffort && hasMaxTokens, "NUNCA deben enviarse reasoning.effort y reasoning.max_tokens simultáneamente.");
        }

        // Caso B: Opciones con ReasoningMaxTokens configurado (envía max_tokens, NO effort dentro de reasoning)
        string? capturedBodyExplicitTokens = null;
        var mockHandlerB = new MockHttpMessageHandler
        {
            HandlerFunc = request =>
            {
                capturedBodyExplicitTokens = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                var responseJson = new { choices = new[] { new { message = new { content = "# Acta B" } } } };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseJson), Encoding.UTF8, "application/json")
                });
            }
        };

        var serviceB = new OpenRouterActaService(new HttpClient(mockHandlerB));
        var optionsWithBudget = new ActaGenerationOptions
        {
            ApiKey = "sk-test",
            ModelName = "qwen/qwen3.8-flash",
            ReasoningMaxTokens = 3000
        };
        await serviceB.GenerateActaAsync(meeting, optionsWithBudget);

        Assert.NotNull(capturedBodyExplicitTokens);
        using (var docB = JsonDocument.Parse(capturedBodyExplicitTokens))
        {
            var reasoningB = docB.RootElement.GetProperty("reasoning");
            bool hasEffort = reasoningB.TryGetProperty("effort", out _);
            bool hasMaxTokens = reasoningB.TryGetProperty("max_tokens", out _);

            Assert.True(hasMaxTokens, "Al configurar ReasoningMaxTokens debe enviarse max_tokens.");
            Assert.False(hasEffort, "Al configurar ReasoningMaxTokens NO debe enviarse effort.");
            Assert.Equal(3000, reasoningB.GetProperty("max_tokens").GetInt32());
            Assert.False(hasEffort && hasMaxTokens, "NUNCA deben enviarse reasoning.effort y reasoning.max_tokens simultáneamente.");
        }
    }

    // 2. La request generada para un modelo de reasoning es válida y compatible
    [Theory]
    [InlineData("openai/gpt-6-luna")]
    [InlineData("qwen/qwen3.8-flash")]
    [InlineData("anthropic/claude-3.5-sonnet")]
    public async Task GenerateActaAsync_ReasoningRequestPayload_IsValidForVariousModels(string modelName)
    {
        string? capturedBody = null;
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = request =>
            {
                capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                var responseJson = new { choices = new[] { new { message = new { content = "# Acta" } } } };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseJson), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Reunión" };
        meeting.Transcript.Add(new TranscriptSegment { SpeakerId = "SPEAKER_00", Text = "Puntos clave de la reunión" });

        var options = new ActaGenerationOptions
        {
            ApiKey = "sk-test",
            ModelName = modelName
        };

        var result = await service.GenerateActaAsync(meeting, options);
        Assert.True(result.Success);
        Assert.NotNull(capturedBody);

        using var doc = JsonDocument.Parse(capturedBody);
        var root = doc.RootElement;
        Assert.Equal(modelName, root.GetProperty("model").GetString());

        // Reasoning object schema validation
        Assert.True(root.TryGetProperty("reasoning", out var reasoning));
        Assert.True(reasoning.TryGetProperty("effort", out var effortProp) || reasoning.TryGetProperty("max_tokens", out var _));
        Assert.False(reasoning.TryGetProperty("effort", out _) && reasoning.TryGetProperty("max_tokens", out _));
        Assert.True(reasoning.GetProperty("exclude").GetBoolean());
    }

    // 3. El límite de salida sigue configurándose correctamente
    [Fact]
    public async Task GenerateActaAsync_OutputLimit_ConfiguredCorrectlySeparateFromReasoning()
    {
        string? capturedBody = null;
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = request =>
            {
                capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                var responseJson = new { choices = new[] { new { message = new { content = "# Acta" } } } };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseJson), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Reunión" };
        meeting.Transcript.Add(new TranscriptSegment { SpeakerId = "SPEAKER_00", Text = "Puntos del día" });

        var options = new ActaGenerationOptions { ApiKey = "sk-test", ModelName = "qwen/qwen3.8-flash" };
        var result = await service.GenerateActaAsync(meeting, options);

        Assert.True(result.Success);
        Assert.NotNull(capturedBody);

        using var doc = JsonDocument.Parse(capturedBody);
        var root = doc.RootElement;

        // El límite de tokens de respuesta del payload principal debe ser 4000
        Assert.Equal(4000, root.GetProperty("max_tokens").GetInt32());
    }

    // 6. No se rompe la selección de idioma del Acta
    [Theory]
    [InlineData(DictaMeeting.Meetings.Enums.LanguageMode.Spanish, "Español", "DIRECTRICES MANDATORIAS DE ACTA IMPERSONAL")]
    [InlineData(DictaMeeting.Meetings.Enums.LanguageMode.English, "English", "MANDATORY IMPERSONAL MINUTES DIRECTIVES")]
    public async Task GenerateActaAsync_PreservesLanguageSelection(DictaMeeting.Meetings.Enums.LanguageMode language, string expectedLangWord, string expectedDirective)
    {
        string? capturedBody = null;
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = request =>
            {
                capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                var responseJson = new { choices = new[] { new { message = new { content = "# Minutes" } } } };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseJson), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Bilingual Meeting" };
        meeting.Transcript.Add(new TranscriptSegment { SpeakerId = "SPEAKER_00", Text = "Hello team, let's begin." });

        var options = new ActaGenerationOptions
        {
            ApiKey = "sk-test",
            ModelName = "qwen/qwen3.8-flash",
            Language = language,
            IsImpersonal = true
        };

        var result = await service.GenerateActaAsync(meeting, options);
        Assert.True(result.Success);
        Assert.NotNull(capturedBody);

        using var doc = JsonDocument.Parse(capturedBody);
        var messages = doc.RootElement.GetProperty("messages");
        var userContent = messages[1].GetProperty("content").GetString();

        Assert.NotNull(userContent);
        Assert.Contains(expectedLangWord, userContent);
        Assert.Contains(expectedDirective, userContent);
    }

    // 7. No se rompe la selección de nivel de detalle
    [Theory]
    [InlineData(DictaMeeting.Meetings.Enums.ActaDetailLevel.Breve)]
    [InlineData(DictaMeeting.Meetings.Enums.ActaDetailLevel.Normal)]
    [InlineData(DictaMeeting.Meetings.Enums.ActaDetailLevel.Detallada)]
    public async Task GenerateActaAsync_PreservesDetailLevelSelection(DictaMeeting.Meetings.Enums.ActaDetailLevel detailLevel)
    {
        string? capturedBody = null;
        var mockHandler = new MockHttpMessageHandler
        {
            HandlerFunc = request =>
            {
                capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                var responseJson = new { choices = new[] { new { message = new { content = "# Acta" } } } };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(responseJson), Encoding.UTF8, "application/json")
                });
            }
        };

        var service = new OpenRouterActaService(new HttpClient(mockHandler));
        var meeting = new Meeting { Title = "Reunión de prueba" };
        meeting.Transcript.Add(new TranscriptSegment { SpeakerId = "SPEAKER_00", Text = "Detalles y acuerdos." });

        var options = new ActaGenerationOptions
        {
            ApiKey = "sk-test",
            ModelName = "qwen/qwen3.8-flash",
            DetailLevel = detailLevel
        };

        var result = await service.GenerateActaAsync(meeting, options);
        Assert.True(result.Success);
        Assert.NotNull(capturedBody);

        using var doc = JsonDocument.Parse(capturedBody);
        var messages = doc.RootElement.GetProperty("messages");
        var userContent = messages[1].GetProperty("content").GetString();

        Assert.NotNull(userContent);
        Assert.Contains($"Nivel de detalle requerido: {detailLevel}", userContent);
    }

    // Prueba real de integración con OpenRouter utilizando la clave configurada en Windows DPAPI
    [Fact]
    public async Task RealOpenRouter_GeneratesActa_WithConfiguredModel_WithoutError()
    {
        var secureStorage = new WindowsDpapiSecureStorageService();
        var key = secureStorage.GetSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey);
        if (string.IsNullOrWhiteSpace(key))
        {
            // Omitir si la máquina no cuenta con API Key en DPAPI
            return;
        }

        using var client = new HttpClient();
        var service = new OpenRouterActaService(client, secureStorage);

        var connResult = await service.TestConnectionAsync();
        Assert.True(connResult.Success, connResult.Message);

        var meeting = new Meeting
        {
            Title = "Reunión de prueba DictaMeeting 1.5.4",
            Date = DateTime.Today,
            Organizer = "Equipo Técnico"
        };
        meeting.Transcript.Add(new TranscriptSegment
        {
            SpeakerId = "SPEAKER_00",
            Text = "Buenos días. En esta reunión acordamos lanzar la versión 1.5.4 de DictaMeeting con la corrección definitiva para OpenRouter."
        });

        var options = new ActaGenerationOptions
        {
            ModelName = "qwen/qwen3.8-flash",
            Language = DictaMeeting.Meetings.Enums.LanguageMode.Spanish,
            DetailLevel = DictaMeeting.Meetings.Enums.ActaDetailLevel.Normal
        };

        var actaResult = await service.GenerateActaAsync(meeting, options);

        Assert.True(actaResult.Success, $"Fallo en OpenRouter: {actaResult.ErrorMessage}");
        Assert.False(string.IsNullOrWhiteSpace(actaResult.MarkdownContent), "El acta devuelta no debe estar vacía.");
        Assert.Null(actaResult.ErrorMessage);
        Assert.NotNull(actaResult.PromptTokensUsed);
        Assert.NotNull(actaResult.CompletionTokensUsed);
    }

    // Prueba real de integración de Custom Provider con servidor HTTP local
    [Fact]
    public async Task RealCustomProvider_GeneratesActa_WithLocalServer_Successfully()
    {
        // Levantar un HttpListener local en un puerto dinámico
        var listener = new System.Net.HttpListener();
        var testPort = 59123;
        var prefix = $"http://127.0.0.1:{testPort}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        string? receivedRequestBody = null;
        var serverTask = Task.Run(async () =>
        {
            try
            {
                var context = await listener.GetContextAsync();
                using var reader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8);
                receivedRequestBody = await reader.ReadToEndAsync();

                var responsePayload = new
                {
                    id = "custom-chat-1",
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                role = "assistant",
                                content = "# Acta de Reunión Personalizada\n\n## Resumen\nTodo correcto."
                            },
                            finish_reason = "stop"
                        }
                    },
                    usage = new
                    {
                        prompt_tokens = 150,
                        completion_tokens = 40,
                        total_tokens = 190
                    }
                };

                var responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responsePayload));
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = responseBytes.Length;
                await context.Response.OutputStream.WriteAsync(responseBytes);
                context.Response.Close();
            }
            catch
            {
                // listener stopped
            }
        });

        try
        {
            using var client = new HttpClient();
            var service = new OpenAiCompatibleActaService(client);

            var meeting = new Meeting { Title = "Reunión Custom Server" };
            meeting.Transcript.Add(new TranscriptSegment { SpeakerId = "SPEAKER_00", Text = "Prueba con servidor compatible OpenAI." });

            var options = new ActaGenerationOptions
            {
                Endpoint = $"http://127.0.0.1:{testPort}/v1/chat/completions",
                CustomModelName = "custom-local-model",
                ApiKey = "dummy-custom-key"
            };

            var result = await service.GenerateActaAsync(meeting, options);
            await serverTask;

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains("# Acta de Reunión Personalizada", result.MarkdownContent);
            Assert.Equal(150, result.PromptTokensUsed);
            Assert.Equal(40, result.CompletionTokensUsed);

            Assert.NotNull(receivedRequestBody);
            using var doc = JsonDocument.Parse(receivedRequestBody);
            var root = doc.RootElement;
            Assert.Equal("custom-local-model", root.GetProperty("model").GetString());
            Assert.Equal(4000, root.GetProperty("max_tokens").GetInt32());
            Assert.False(root.TryGetProperty("reasoning", out _), "El Custom Provider no debe incluir propiedad reasoning.");
            Assert.False(root.TryGetProperty("temperature", out _), "El Custom Provider no debe incluir propiedad temperature.");
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }
}



