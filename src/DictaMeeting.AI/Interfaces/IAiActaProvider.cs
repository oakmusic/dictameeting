using DictaMeeting.AI.Models;
using DictaMeeting.Meetings.Models;

namespace DictaMeeting.AI.Interfaces;

/// <summary>
/// Contrato común para proveedores de IA generadores de actas de reunión.
/// Permite desacoplar proveedores específicos (OpenRouter, Servidor compatible OpenAI, etc.).
/// </summary>
public interface IAiActaProvider
{
    string ProviderId { get; }
    string DisplayName { get; }

    bool SupportsModel(string? modelId);

    IReadOnlyList<AiModelOption> GetRecommendedModels();

    Task<ActaGenerationResult> GenerateActaAsync(
        Meeting meeting,
        ActaGenerationOptions options,
        CancellationToken cancellationToken = default);

    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}
