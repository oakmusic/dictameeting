using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;

namespace DictaMeeting.Meetings.Interfaces;

public interface IMeetingService
{
    Meeting? CurrentMeeting { get; }
    MeetingState CurrentState { get; }

    event EventHandler<MeetingState>? StateChanged;
    event EventHandler<TranscriptSegment>? SegmentAppended;
    event EventHandler<TranscriptSegment>? SegmentUpdated;
    event EventHandler<Speaker>? SpeakerUpdated;
    event EventHandler<(string MergedSpeakerId, string TargetSpeakerId)>? SpeakerMerged;

    Task StartMeetingAsync(string title, string organizer = "", string company = "", CancellationToken cancellationToken = default);
    Task<Meeting> CreateImportedMeetingAsync(string title, string sourceAudioFilePath, TimeSpan? audioDuration = null, string? language = null, CancellationToken cancellationToken = default);
    Task<Meeting> CreateMeetingFromTranscriptAsync(string title, string sourceTranscriptFilePath, CancellationToken cancellationToken = default);
    Task StopMeetingAsync(CancellationToken cancellationToken = default);
    void UpdateSpeakerDisplayName(string speakerId, string displayName);
    void AppendSegment(TranscriptSegment segment);
    void OpenMeeting(Meeting meeting);
    void Reset();
}
