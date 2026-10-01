using CommunityToolkit.Mvvm.ComponentModel;
using DictaMeeting.Transcription.Models;

namespace DictaMeeting.App.ViewModels;

/// <summary>
/// Representa un modelo en el asistente de primer arranque (onboarding).
/// </summary>
public partial class FirstRunModelItemViewModel : ObservableObject
{
    public string Id { get; set; } = string.Empty;

    public ModelSize? ModelSize { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string FormattedSize { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private bool _isWaiting;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;
}
