using CommunityToolkit.Mvvm.ComponentModel;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;

namespace DictaMeeting.App.ViewModels;

public partial class MeetingHistoryItemViewModel : ObservableObject
{
    public Meeting Meeting { get; }

    public string Id => Meeting.Id;
    public string Title
    {
        get => Meeting.Title;
        set
        {
            if (Meeting.Title != value)
            {
                Meeting.Title = value;
                OnPropertyChanged(nameof(Title));
            }
        }
    }
    public DateTime Date => Meeting.Date.TimeOfDay != TimeSpan.Zero
        ? Meeting.Date
        : (Meeting.StartTime?.LocalDateTime ?? Meeting.Date);
    public TimeSpan Duration => Meeting.Duration;

    public string FormattedDate => Date.ToString("dd/MM/yyyy HH:mm");
    public string FormattedDuration => $"{(int)Meeting.Duration.TotalHours:D2}:{Meeting.Duration.Minutes:D2}:{Meeting.Duration.Seconds:D2}";
    public int ParticipantCount => Meeting.Participants.Count;
    public int SegmentCount => Meeting.Transcript.Count;
    public bool HasActa => !string.IsNullOrWhiteSpace(Meeting.ActaMarkdown);

    private static DictaMeeting.App.Services.LocalizationManager Loc => DictaMeeting.App.Services.LocalizationManager.Instance;

    public string SummaryText => string.Format(Loc["History_Summary_Line"], ParticipantCount, SegmentCount);

    public string TranscriptPreview
    {
        get
        {
            if (Meeting.Transcript.Count == 0) return Loc["History_No_Transcript"];
            var text = string.Join(" ", Meeting.Transcript.Take(3).Select(s => s.Text.Trim()));
            if (text.Length > 160) text = text[..157] + "...";
            return $"\"{text}\"";
        }
    }

    public IReadOnlyList<TranscriptSegmentPreviewItem> PreviewSegments
    {
        get
        {
            if (Meeting.Transcript == null || Meeting.Transcript.Count == 0)
                return Array.Empty<TranscriptSegmentPreviewItem>();

            return Meeting.Transcript.Take(3).Select(seg =>
            {
                var sp = Meeting.Participants.FirstOrDefault(p => string.Equals(p.Id, seg.SpeakerId, StringComparison.OrdinalIgnoreCase));
                var name = !string.IsNullOrWhiteSpace(sp?.DisplayName)
                    ? sp.DisplayName
                    : (!string.IsNullOrWhiteSpace(seg.SpeakerDisplayName) ? seg.SpeakerDisplayName : seg.SpeakerId);
                return new TranscriptSegmentPreviewItem
                {
                    SpeakerDisplayName = string.IsNullOrWhiteSpace(name) ? Loc["History_Default_Speaker"] : name,
                    ColorHex = sp?.ColorHex ?? "#0071E3",
                    FormattedTime = seg.FormattedStartTime,
                    Text = seg.Text
                };
            }).ToList();
        }
    }

    public bool HasSummary => (Meeting.SummarySegments != null && Meeting.SummarySegments.Count > 0) || !string.IsNullOrWhiteSpace(Meeting.SummaryText);

    public string SummaryPreview
    {
        get
        {
            if (Meeting.SummarySegments != null && Meeting.SummarySegments.Count > 0)
            {
                var text = Meeting.SummarySegments[0].Text?.Trim() ?? string.Empty;
                return text.Length > 220 ? text[..217] + "..." : text;
            }
            if (!string.IsNullOrWhiteSpace(Meeting.SummaryText))
            {
                var text = Meeting.SummaryText.Trim();
                return text.Length > 220 ? text[..217] + "..." : text;
            }
            return Loc["History_No_Summary"];
        }
    }

    public string ActaStatusText => HasActa ? Loc["History_Acta_Status_Done"] : Loc["History_Acta_Status_None"];

    public string ParticipantsPreviewText
    {
        get
        {
            var names = Meeting.GetUserParticipants();
            if (names.Count > 0) return string.Join(", ", names);
            if (Meeting.Participants.Count > 0)
                return string.Format(Loc["History_Detected_Voices_Format"], Meeting.Participants.Count, string.Join(", ", Meeting.Participants.Select(p => p.DisplayName)));
            return Loc["History_No_Participants"];
        }
    }

    public string LanguageText => !string.IsNullOrWhiteSpace(Meeting.Language) ? Meeting.Language : "Español";
    public bool HasAudio => !string.IsNullOrWhiteSpace(Meeting.AudioFilePath) && System.IO.File.Exists(Meeting.AudioFilePath);
    public string AudioStatusText => HasAudio ? Loc["Meeting_Audio_Available"] : Loc["Meeting_Audio_None"];
    public string RemainingSegmentCountText => SegmentCount > 3 ? string.Format(Loc["History_Remaining_Segments"], SegmentCount - 3) : string.Empty;

    public ProcessingStatus ProcessingStatus => Meeting.ProcessingStatus;
    public bool IsProcessing => Meeting.ProcessingStatus == ProcessingStatus.Processing;
    public bool IsPending => Meeting.ProcessingStatus == ProcessingStatus.Pending;
    public bool IsCancelled => Meeting.ProcessingStatus == ProcessingStatus.Cancelled;
    public bool HasError => Meeting.ProcessingStatus == ProcessingStatus.Error;
    public bool IsCompleted => Meeting.ProcessingStatus == ProcessingStatus.Completed;
    public bool CanRetry => Meeting.ProcessingStatus == ProcessingStatus.Error || Meeting.ProcessingStatus == ProcessingStatus.Cancelled || Meeting.ProcessingStatus == ProcessingStatus.Pending;
    public bool CanReprocess => Meeting.ProcessingStatus == ProcessingStatus.Completed;

    public string ProcessingStatusBadgeText => Meeting.ProcessingStatus switch
    {
        ProcessingStatus.Processing => string.Format(Loc["History_Status_Processing"], (int)(Meeting.ProcessingProgress * 100)),
        ProcessingStatus.Pending => Loc["History_Status_Pending"],
        ProcessingStatus.Cancelled => Loc["History_Status_Cancelled"],
        ProcessingStatus.Error => Loc["Common_Error"],
        ProcessingStatus.Completed => Loc["History_Status_Completed"],
        _ => string.Empty
    };

    public string ProcessingStatusColorHex => Meeting.ProcessingStatus switch
    {
        ProcessingStatus.Processing => "#6366F1", // Indigo
        ProcessingStatus.Pending => "#F59E0B",    // Amber
        ProcessingStatus.Cancelled => "#94A3B8",  // Slate
        ProcessingStatus.Error => "#EF4444",      // Red
        ProcessingStatus.Completed => "#10B981",  // Emerald
        _ => "#64748B"
    };

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(ProcessingStatus));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanReprocess));
        OnPropertyChanged(nameof(ProcessingStatusBadgeText));
        OnPropertyChanged(nameof(ProcessingStatusColorHex));
        OnPropertyChanged(nameof(SegmentCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(TranscriptPreview));
        OnPropertyChanged(nameof(PreviewSegments));
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(SummaryPreview));
        OnPropertyChanged(nameof(HasActa));
        OnPropertyChanged(nameof(ActaStatusText));
        OnPropertyChanged(nameof(ParticipantsPreviewText));
        OnPropertyChanged(nameof(HasAudio));
        OnPropertyChanged(nameof(AudioStatusText));
        OnPropertyChanged(nameof(RemainingSegmentCountText));
    }

    public MeetingHistoryItemViewModel(Meeting meeting)
    {
        Meeting = meeting;
    }
}

public class TranscriptSegmentPreviewItem
{
    public string SpeakerDisplayName { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#0071E3";
    public string FormattedTime { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
