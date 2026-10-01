using System.Net;
using System.Text;
using System.Text.Json;
using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.AI.Services;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DictaMeeting.Tests;

public class CompositeAiActaServiceAndFactoryTests
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

    private class FakeUserSettingsService : IUserSettingsService
    {
        private UserSettings _settings = new();
        public UserSettings LoadSettings() => _settings;
        public void SaveSettings(UserSettings settings) => _settings = settings;
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? HandlerFunc { get; set; }
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            if (HandlerFunc != null) return await HandlerFunc(request);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}", Encoding.UTF8, "application/json")
            };
        }
    }

    // 1. Factory registra ambos proveedores y permite resolverlos
    [Fact]
    public void AiActaServiceFactory_RegistersAndResolvesBothProviders()
    {
        var openRouter = new OpenRouterActaService();
        var customServer = new OpenAiCompatibleActaService();
        var factory = new AiActaServiceFactory(new IAiActaProvider[] { openRouter, customServer });

        var providers = factory.GetAllProviders();
        Assert.Equal(2, providers.Count);

        var resolvedOpenRouter = factory.GetProvider(OpenRouterActaService.ProviderIdentifier);
        var resolvedCustomServer = factory.GetProvider(OpenAiCompatibleActaService.ProviderIdentifier);

        Assert.NotNull(resolvedOpenRouter);
        Assert.NotNull(resolvedCustomServer);
        Assert.IsType<OpenRouterActaService>(resolvedOpenRouter);
        Assert.IsType<OpenAiCompatibleActaService>(resolvedCustomServer);
    }

    // 2. CompositeAiActaService devuelve los 3 modelos disponibles para el selector
    [Fact]
    public void CompositeAiActaService_ReturnsThreeAvailableModelsInDropdown()
    {
        var openRouter = new OpenRouterActaService();
        var customServer = new OpenAiCompatibleActaService();
        var factory = new AiActaServiceFactory(new IAiActaProvider[] { openRouter, customServer });
        var composite = new CompositeAiActaService(factory);

        var models = composite.GetRecommendedModels();

        Assert.Equal(3, models.Count);
        Assert.Equal("openai/gpt-6-luna", models[0].Id);
        Assert.Equal("qwen/qwen3.8-flash", models[1].Id);
        Assert.Equal(OpenAiCompatibleActaService.CustomServerModelId, models[2].Id);
        Assert.Equal("Servidor personalizado", models[2].DisplayName);
    }

    // 3. Coexistencia de credenciales independientes en almacenamiento seguro sin sobreescritura
    [Fact]
    public void MultiProvider_IndependentCredentials_StoredSimultaneouslyWithoutCrossPollution()
    {
        var storage = new FakeSecureStorage();

        // Almacenar credenciales de OpenRouter
        storage.SaveSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey, "sk-or-v1-my-openrouter-key");

        // Almacenar credenciales de Servidor personalizado
        storage.SaveSecret(OpenAiCompatibleActaService.CustomServerApiKeyStorageKey, "bearer-token-my-custom-proxy");

        // Ambas deben coexistir intactas
        Assert.Equal("sk-or-v1-my-openrouter-key", storage.GetSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey));
        Assert.Equal("bearer-token-my-custom-proxy", storage.GetSecret(OpenAiCompatibleActaService.CustomServerApiKeyStorageKey));

        // Borrar una no debe afectar a la otra
        storage.DeleteSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey);
        Assert.Null(storage.GetSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey));
        Assert.Equal("bearer-token-my-custom-proxy", storage.GetSecret(OpenAiCompatibleActaService.CustomServerApiKeyStorageKey));
    }

    // 4. Enrutamiento: Selección de GPT-6 Luna usa OpenRouter sin invocar Custom Server
    [Fact]
    public async Task GenerateActaAsync_SelectedGpt6Luna_RoutesToOpenRouter()
    {
        var openRouterHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"# Acta OpenRouter\"}}]}", Encoding.UTF8, "application/json")
            })
        };
        var customServerHandler = new MockHttpMessageHandler();

        var fakeStorage = new FakeSecureStorage();
        fakeStorage.SaveSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey, "sk-or-valid-key");

        var openRouter = new OpenRouterActaService(new HttpClient(openRouterHandler), fakeStorage);
        var customServer = new OpenAiCompatibleActaService(new HttpClient(customServerHandler), fakeStorage);

        var factory = new AiActaServiceFactory(new IAiActaProvider[] { openRouter, customServer });
        var composite = new CompositeAiActaService(factory);

        var meeting = new Meeting { Title = "Test Meeting" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Hola" });

        var options = new ActaGenerationOptions { ModelName = "openai/gpt-6-luna" };

        var result = await composite.GenerateActaAsync(meeting, options);

        Assert.True(result.Success);
        Assert.Equal("# Acta OpenRouter", result.MarkdownContent);
        Assert.Equal(1, openRouterHandler.CallCount);
        Assert.Equal(0, customServerHandler.CallCount); // Custom server nunca fue llamado
    }

    // 5. Enrutamiento: Selección de Servidor Personalizado usa OpenAiCompatible sin invocar OpenRouter
    [Fact]
    public async Task GenerateActaAsync_SelectedCustomServer_RoutesToCustomServerWithoutSilentFallback()
    {
        var openRouterHandler = new MockHttpMessageHandler();
        var customServerHandler = new MockHttpMessageHandler
        {
            HandlerFunc = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"# Acta Servidor Personalizado\"}}]}", Encoding.UTF8, "application/json")
            })
        };

        var fakeStorage = new FakeSecureStorage();
        fakeStorage.SaveSecret(OpenAiCompatibleActaService.CustomServerApiKeyStorageKey, "custom-key");
        fakeStorage.SaveSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey, "sk-or-valid-key"); // Even if OpenRouter is configured!

        var settingsService = new FakeUserSettingsService();
        settingsService.SaveSettings(new UserSettings
        {
            CustomAiEndpoint = "https://custom.gateway.internal/v1",
            CustomAiModel = "llama-3.3-70b"
        });

        var openRouter = new OpenRouterActaService(new HttpClient(openRouterHandler), fakeStorage);
        var customServer = new OpenAiCompatibleActaService(new HttpClient(customServerHandler), fakeStorage, settingsService);

        var factory = new AiActaServiceFactory(new IAiActaProvider[] { openRouter, customServer });
        var composite = new CompositeAiActaService(factory);

        var meeting = new Meeting { Title = "Test Meeting" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Hola" });

        var options = new ActaGenerationOptions
        {
            ModelName = OpenAiCompatibleActaService.CustomServerModelId
        };

        var result = await composite.GenerateActaAsync(meeting, options);

        Assert.True(result.Success);
        Assert.Equal("# Acta Servidor Personalizado", result.MarkdownContent);
        Assert.Equal(1, customServerHandler.CallCount);
        Assert.Equal(0, openRouterHandler.CallCount); // OpenRouter NO se usa como fallback
    }

    // 6. Sin fallback silencioso: Si Servidor Personalizado no está configurado, error claro y 0 llamadas a OpenRouter
    [Fact]
    public async Task GenerateActaAsync_CustomServerMisconfigured_ReturnsClearErrorWithoutFallingBackToOpenRouter()
    {
        var openRouterHandler = new MockHttpMessageHandler();
        var fakeStorage = new FakeSecureStorage();
        // OpenRouter configured, but Custom Server is NOT
        fakeStorage.SaveSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey, "sk-or-valid-key");

        var settingsService = new FakeUserSettingsService(); // No endpoint/model configured!

        var openRouter = new OpenRouterActaService(new HttpClient(openRouterHandler), fakeStorage);
        var customServer = new OpenAiCompatibleActaService(secureStorage: fakeStorage, userSettingsService: settingsService);

        var factory = new AiActaServiceFactory(new IAiActaProvider[] { openRouter, customServer });
        var composite = new CompositeAiActaService(factory);

        var meeting = new Meeting { Title = "Test Meeting" };
        meeting.Transcript.Add(new TranscriptSegment { Text = "Hola" });

        var options = new ActaGenerationOptions
        {
            ModelName = OpenAiCompatibleActaService.CustomServerModelId
        };

        var result = await composite.GenerateActaAsync(meeting, options);

        Assert.False(result.Success);
        Assert.Contains("Servidor personalizado", result.ErrorMessage);
        Assert.Equal(0, openRouterHandler.CallCount); // Verificación estricta: CERO fallback a OpenRouter
    }

    // 7. Persistencia de configuración: UserSettings almacena y recupera datos del servidor personalizado
    [Fact]
    public void UserSettings_PersistsAndRestoresCustomServerConfiguration()
    {
        var settingsService = new FakeUserSettingsService();

        var initialSettings = new UserSettings
        {
            CustomAiEndpoint = "https://internal-llm.corp.net/v1",
            CustomAiModel = "qwen-2.5-32b-instruct",
            SelectedAiModelId = OpenAiCompatibleActaService.CustomServerModelId,
            SelectedAiConfigProvider = "CustomServer"
        };

        settingsService.SaveSettings(initialSettings);

        var loadedSettings = settingsService.LoadSettings();

        Assert.Equal("https://internal-llm.corp.net/v1", loadedSettings.CustomAiEndpoint);
        Assert.Equal("qwen-2.5-32b-instruct", loadedSettings.CustomAiModel);
        Assert.Equal(OpenAiCompatibleActaService.CustomServerModelId, loadedSettings.SelectedAiModelId);
        Assert.Equal("CustomServer", loadedSettings.SelectedAiConfigProvider);
    }

    // 8. Inyección de dependencias (DI): Resolución limpia de IAiActaServiceFactory e IAiActaService sin ambigüedad de constructores
    [Fact]
    public void DependencyInjection_ResolvesAiActaServicesWithoutAmbiguity()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<HttpClient>();
        services.AddSingleton<ISecureStorageService, FakeSecureStorage>();
        services.AddSingleton<IUserSettingsService, FakeUserSettingsService>();
        services.AddSingleton<OpenRouterActaService>();
        services.AddSingleton<OpenAiCompatibleActaService>();
        services.AddSingleton<IAiActaProvider>(sp => sp.GetRequiredService<OpenRouterActaService>());
        services.AddSingleton<IAiActaProvider>(sp => sp.GetRequiredService<OpenAiCompatibleActaService>());
        services.AddSingleton<IAiActaServiceFactory, AiActaServiceFactory>();
        services.AddSingleton<IAiActaService, CompositeAiActaService>();

        using var serviceProvider = services.BuildServiceProvider();

        var factory = serviceProvider.GetRequiredService<IAiActaServiceFactory>();
        var aiService = serviceProvider.GetRequiredService<IAiActaService>();

        Assert.NotNull(factory);
        Assert.NotNull(aiService);
        Assert.IsType<CompositeAiActaService>(aiService);
        Assert.Equal(3, aiService.GetRecommendedModels().Count);
    }
}
