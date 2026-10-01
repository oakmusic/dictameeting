using DictaMeeting.Meetings.Models;

namespace DictaMeeting.Meetings.Interfaces;

public interface IMeetingRepository
{
    Task SaveMeetingAsync(Meeting meeting, CancellationToken cancellationToken = default);
    Task SaveMeetingAsync(Meeting meeting, MeetingSessionParameters? parameters, CancellationToken cancellationToken = default);
    Task<Meeting?> GetMeetingAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Meeting>> GetAllMeetingsAsync(CancellationToken cancellationToken = default);
    string GetMeetingDirectoryPath(Meeting meeting);
    string GetMeetingFileBaseName(Meeting meeting);
    string GetAudioFilePath(Meeting meeting);
    string GetAudioFilePath(Meeting meeting, string? extension) => GetAudioFilePath(meeting);
}
