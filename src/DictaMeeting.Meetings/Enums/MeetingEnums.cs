namespace DictaMeeting.Meetings.Enums;

public enum MeetingState
{
    Idle,
    Preparing,
    Recording,
    Processing,
    Finalizing,
    Completed,
    Error
}

public enum ActaDetailLevel
{
    Breve,
    Normal,
    Detallada,
    Exhaustiva
}

public enum LanguageMode
{
    Auto,
    Spanish,
    English
}

public enum ProcessingStatus
{
    Pending,
    Processing,
    Completed,
    Cancelled,
    Error
}
