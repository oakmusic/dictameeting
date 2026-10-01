using DictaMeeting.AI.Models;

namespace DictaMeeting.AI.Interfaces;

/// <summary>
/// Factoría y resolución de proveedores de generación de actas.
/// </summary>
public interface IAiActaServiceFactory
{
    IAiActaProvider GetProviderForModel(string? modelId);

    IAiActaProvider? GetProvider(string providerId);

    IReadOnlyList<IAiActaProvider> GetAllProviders();

    IReadOnlyList<AiModelOption> GetAllAvailableModels();
}
