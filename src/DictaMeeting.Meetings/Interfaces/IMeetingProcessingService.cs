using DictaMeeting.Meetings.Enums;

namespace DictaMeeting.Meetings.Interfaces;

public class MeetingProcessingProgressEventArgs : EventArgs
{
    public string MeetingId { get; }
    public double Progress { get; }
    public string StatusText { get; }

    public MeetingProcessingProgressEventArgs(string meetingId, double progress, string statusText)
    {
        MeetingId = meetingId;
        Progress = progress;
        StatusText = statusText;
    }
}

public class MeetingProcessingCompletedEventArgs : EventArgs
{
    public string MeetingId { get; }
    public ProcessingStatus Status { get; }
    public string? ErrorMessage { get; }

    public MeetingProcessingCompletedEventArgs(string meetingId, ProcessingStatus status, string? errorMessage = null)
    {
        MeetingId = meetingId;
        Status = status;
        ErrorMessage = errorMessage;
    }
}

public interface IMeetingProcessingService
{
    bool IsProcessing { get; }
    string? CurrentMeetingId { get; }
    double CurrentProgress { get; }
    string CurrentStatusText { get; }

    event EventHandler<MeetingProcessingProgressEventArgs>? ProgressChanged;
    event EventHandler<MeetingProcessingCompletedEventArgs>? ProcessingCompleted;

    Task EnqueueOrProcessAsync(string meetingId, string? targetModel = null, CancellationToken cancellationToken = default);
    Task CancelProcessingAsync(string meetingId);
    Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default);
}
