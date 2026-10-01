using DictaMeeting.AI.Models;

namespace DictaMeeting.AI.Interfaces;

/// <summary>
/// Contrato desacoplado para el motor de inferencia de resumen en vivo local.
/// </summary>
public interface ILiveSummaryService : IDisposable, IAsyncDisposable
{
    bool IsModelLoaded { get; }
    bool IsGenerating { get; }
    SummaryMetrics? LastMetrics { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<string> GenerateSummaryAsync(
        string previousSummary,
        string newTranscriptWindow,
        string language = "Spanish",
        CancellationToken cancellationToken = default);

    void CancelCurrentGeneration();
}
