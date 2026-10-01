using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Factoría de proveedores de IA para generación de actas.
/// Resuelve dinámicamente qué proveedor utilizar según el modelo seleccionado o identificador.
/// </summary>
public class AiActaServiceFactory : IAiActaServiceFactory
{
    private readonly IReadOnlyList<IAiActaProvider> _providers;

    public AiActaServiceFactory(IEnumerable<IAiActaProvider> providers)
    {
        _providers = providers.ToList();
    }

    public IAiActaProvider GetProviderForModel(string? modelId)
    {
        foreach (var provider in _providers)
        {
            if (provider.SupportsModel(modelId))
            {
                return provider;
            }
        }

        return _providers.FirstOrDefault(p => p.ProviderId == OpenRouterActaService.ProviderIdentifier)
               ?? _providers.First();
    }

    public IAiActaProvider? GetProvider(string providerId)
    {
        return _providers.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<IAiActaProvider> GetAllProviders() => _providers;

    public IReadOnlyList<AiModelOption> GetAllAvailableModels()
    {
        var list = new List<AiModelOption>();
        foreach (var provider in _providers)
        {
            list.AddRange(provider.GetRecommendedModels());
        }
        return list;
    }
}
