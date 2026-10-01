using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Transcription.Models;

namespace DictaMeeting.Transcription.Interfaces;

public interface ITranscriptionService : IDisposable
{
    bool IsInitialized { get; }
    ModelSize CurrentModel { get; }
    LanguageMode CurrentLanguage { get; set; }

    Task InitializeAsync(ModelSize modelSize, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(string audioFilePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(string audioFilePath, ModelSize modelSize, LanguageMode language, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<TranscriptSegment?> TranscribeAudioChunkAsync(byte[] pcmAudioData, TimeSpan offset, CancellationToken cancellationToken = default);
}
