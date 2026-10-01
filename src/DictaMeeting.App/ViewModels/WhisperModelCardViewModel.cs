using CommunityToolkit.Mvvm.ComponentModel;
using DictaMeeting.Transcription.Models;

namespace DictaMeeting.App.ViewModels;

public partial class WhisperModelCardViewModel : ObservableObject
{
    public ModelSize Size { get; set; }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _formattedSize = string.Empty;

    [ObservableProperty]
    private string _engineBadge = "Whisper (OpenAI)";

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private bool _isRecommended;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    public string RecommendationBadgeText => IsRecommended
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Models_Recommended_Hardware"]
        : string.Empty;
}
