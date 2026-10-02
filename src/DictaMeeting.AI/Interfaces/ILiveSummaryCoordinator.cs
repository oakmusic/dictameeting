using DictaMeeting.AI.Models;
using DictaMeeting.Meetings.Models;

namespace DictaMeeting.AI.Interfaces;

public class SummaryUpdatedEventArgs : EventArgs
{
    public string MeetingId { get; }
    public string Summary { get; }
    public DateTimeOffset Timestamp { get; }
    public SummaryMetrics? Metrics { get; }
    public SummarySegment? Card { get; }
    public IReadOnlyList<SummarySegment> AllCards { get; }

    public SummaryUpdatedEventArgs(
        string meetingId,
        string summary,
        DateTimeOffset timestamp,
        SummaryMetrics? metrics = null,
        SummarySegment? card = null,
        IReadOnlyList<SummarySegment>? allCards = null)
    {
        MeetingId = meetingId ?? string.Empty;
        Summary = summary;
        Timestamp = timestamp;
        Metrics = metrics;
        Card = card;
        AllCards = allCards ?? Array.Empty<SummarySegment>();
    }

    public SummaryUpdatedEventArgs(
        string summary,
        DateTimeOffset timestamp,
        SummaryMetrics? metrics = null,
        SummarySegment? card = null,
        IReadOnlyList<SummarySegment>? allCards = null)
        : this(string.Empty, summary, timestamp, metrics, card, allCards)
    {
    }
}

/// <summary>
/// Coordinador de la generación periódica de tarjetas de resumen en vivo (cada 1-2 min, ~50 palabras).
/// Asegura que el resumen se ejecute de forma asíncrona sin bloquear la captura de audio ni Qwen3-ASR.
/// </summary>
public interface ILiveSummaryCoordinator : IDisposable, IAsyncDisposable
{
    string? CurrentMeetingId { get; }
    IReadOnlyList<SummarySegment> SummaryCards { get; }
    string CurrentSummary { get; }
    DateTimeOffset? LastUpdatedTime { get; }
    bool IsGenerating { get; }
    bool IsRunning { get; }

    event EventHandler<SummaryUpdatedEventArgs>? SummaryUpdated;
    event EventHandler<bool>? GeneratingStateChanged;

    void Start(string meetingId, string language = "Spanish");
    void Start(string language = "Spanish");
    void Stop();
    void Reset();
    void AddSegment(TranscriptSegment segment);
    void UpdateSegment(TranscriptSegment segment);
    Task TriggerImmediateSummaryAsync(CancellationToken cancellationToken = default);
    Task FinalizePendingSummaryAsync(CancellationToken cancellationToken = default);
}
