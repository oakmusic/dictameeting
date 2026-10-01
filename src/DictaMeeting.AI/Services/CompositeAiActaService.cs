using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Vocabulary;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Orquestador compuesto de generación de actas.
/// Implementa IAiActaService delegando en el proveedor adecuado según el modelo seleccionado.
/// Permite desacoplar totalmente al consumidor (ViewModels, UI) de los detalles de implementación de cada proveedor.
/// </summary>
public class CompositeAiActaService : IAiActaService
{
    private readonly IAiActaServiceFactory _factory;
    private readonly IVocabularyService? _vocabularyService;

    public IAiActaServiceFactory Factory => _factory;

    public CompositeAiActaService(
        IAiActaServiceFactory factory,
        IVocabularyService? vocabularyService = null)
    {
        _factory = factory;
        _vocabularyService = vocabularyService;
    }

    public IReadOnlyList<AiModelOption> GetRecommendedModels()
    {
        return _factory.GetAllAvailableModels();
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
        var provider = _factory.GetProviderForModel(options.ModelName);
        return await provider.GenerateActaAsync(meeting, options, cancellationToken);
    }
}
