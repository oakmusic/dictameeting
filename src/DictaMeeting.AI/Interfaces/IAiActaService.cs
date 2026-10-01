using DictaMeeting.AI.Models;
using DictaMeeting.Meetings.Models;

namespace DictaMeeting.AI.Interfaces;

public interface IAiActaService
{
    Task<ActaGenerationResult> GenerateActaAsync(
        Meeting meeting,
        ActaGenerationOptions options,
        CancellationToken cancellationToken = default);

    string BuildPrompt(Meeting meeting, ActaGenerationOptions options);

    IReadOnlyList<AiModelOption> GetRecommendedModels();
}
