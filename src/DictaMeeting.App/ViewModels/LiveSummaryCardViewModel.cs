using CommunityToolkit.Mvvm.ComponentModel;

namespace DictaMeeting.App.ViewModels;

/// <summary>
/// ViewModel que representa una tarjeta de resumen generada por tramo de conversación (cada 1-2 min, ~50 palabras).
/// </summary>
public partial class LiveSummaryCardViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _formattedTimeRange = "00:00 - 01:30";

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string _relativeTimeString = "Hace un instante";

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public void UpdateRelativeTime()
    {
        var diff = (int)(DateTimeOffset.Now - Timestamp).TotalSeconds;
        var loc = DictaMeeting.App.Services.LocalizationManager.Instance;
        if (diff < 8)
        {
            RelativeTimeString = loc["Summary_Card_JustNow"];
        }
        else if (diff < 60)
        {
            RelativeTimeString = string.Format(loc["Summary_Card_SecondsAgo"], diff);
        }
        else
        {
            int mins = diff / 60;
            RelativeTimeString = mins == 1
                ? loc["Summary_Card_OneMinuteAgo"]
                : string.Format(loc["Summary_Card_MinutesAgo"], mins);
        }
    }
}
