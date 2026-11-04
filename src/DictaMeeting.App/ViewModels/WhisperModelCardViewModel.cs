using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictaMeeting.Transcription.Models;

namespace DictaMeeting.App.ViewModels;

public partial class WhisperModelCardViewModel : ObservableObject
{
    public ModelSize? ModelSize { get; set; }
    public ModelSize Size
    {
        get => ModelSize ?? DictaMeeting.Transcription.Models.ModelSize.Base;
        set => ModelSize = value;
    }

    public string Id { get; set; } = string.Empty;

    [ObservableProperty]
    private string _modelType = "ASR";

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
    private bool _isIncomplete;

    [ObservableProperty]
    private bool _isRecommended;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _statusText = "No instalado";

    public CancellationTokenSource? Cts { get; set; }

    public string RecommendationBadgeText => IsRecommended
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Models_Recommended_Hardware"]
        : string.Empty;

    public void CancelDownload()
    {
        try
        {
            Cts?.Cancel();
        }
        catch { }
    }
}
