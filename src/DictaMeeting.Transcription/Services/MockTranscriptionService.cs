using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;

namespace DictaMeeting.Transcription.Services;

public class MockTranscriptionService : ITranscriptionService
{
    public bool IsInitialized { get; private set; } = true;
    public ModelSize CurrentModel { get; private set; } = ModelSize.Base;
    public LanguageMode CurrentLanguage { get; set; } = LanguageMode.Spanish;

    public virtual Task InitializeAsync(ModelSize modelSize, CancellationToken cancellationToken = default)
    {
        CurrentModel = modelSize;
        IsInitialized = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(0.5);
        var list = new List<TranscriptSegment>
        {
            new()
            {
                StartTime = TimeSpan.FromSeconds(0),
                EndTime = TimeSpan.FromSeconds(4),
                Text = "Buenos días a todos, damos inicio a la reunión.",
                Confidence = 0.95f,
                IsFinal = true
            },
            new()
            {
                StartTime = TimeSpan.FromSeconds(5),
                EndTime = TimeSpan.FromSeconds(9),
                Text = "Repasamos los acuerdos pendientes de la sesión anterior.",
                Confidence = 0.92f,
                IsFinal = true
            }
        };
        progress?.Report(1.0);
        return Task.FromResult<IReadOnlyList<TranscriptSegment>>(list);
    }

    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(
        string audioFilePath,
        ModelSize modelSize,
        LanguageMode language,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        CurrentModel = modelSize;
        CurrentLanguage = language;
        return TranscribeAudioFileAsync(audioFilePath, progress, cancellationToken);
    }

    public Task<TranscriptSegment?> TranscribeAudioChunkAsync(
        byte[] pcmAudioData,
        TimeSpan offset,
        CancellationToken cancellationToken = default)
    {
        if (pcmAudioData.Length == 0) return Task.FromResult<TranscriptSegment?>(null);

        var segment = new TranscriptSegment
        {
            StartTime = offset,
            EndTime = offset.Add(TimeSpan.FromSeconds(2)),
            Text = "Texto reconocido de prueba.",
            IsFinal = false
        };

        return Task.FromResult<TranscriptSegment?>(segment);
    }

    public void Dispose()
    {
        IsInitialized = false;
    }
}
