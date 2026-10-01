using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DictaMeeting.App.ViewModels;

public partial class SpeakerViewModel : ObservableObject
{
    private readonly Action<string, string>? _onNameChanged;
    private readonly Action<string, string>? _onNameCommitted;

    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _colorHex = "#6366F1";

    [ObservableProperty]
    private int _utteranceCount;

    [ObservableProperty]
    private TimeSpan _totalSpeakingDuration = TimeSpan.Zero;

    [ObservableProperty]
    private string _speakingTimeFormatted = "00:00";

    private ObservableCollection<string> _availableRealParticipants = new();

    public ObservableCollection<string> AvailableOptions { get; } = new();

    private string? _selectedRealParticipant;
    public string? SelectedRealParticipant
    {
        get => _selectedRealParticipant;
        set
        {
            if (SetProperty(ref _selectedRealParticipant, value))
            {
                if (!string.IsNullOrWhiteSpace(value) && value != DisplayName)
                {
                    DisplayName = value;
                    CommitDisplayName();
                }
            }
        }
    }

    partial void OnTotalSpeakingDurationChanged(TimeSpan value)
    {
        SpeakingTimeFormatted = $"{(int)value.TotalMinutes:D2}:{value.Seconds:D2}";
    }

    public void AddSpeakingTime(TimeSpan duration)
    {
        TotalSpeakingDuration += duration;
    }

    public SpeakerViewModel() { }

    public SpeakerViewModel(
        string id,
        string displayName,
        string colorHex,
        Action<string, string>? onNameChanged = null,
        Action<string, string>? onNameCommitted = null,
        ObservableCollection<string>? availableRealParticipants = null)
    {
        _id = id;
        _displayName = displayName;
        _colorHex = colorHex;
        _onNameChanged = onNameChanged;
        _onNameCommitted = onNameCommitted;

        if (availableRealParticipants != null)
        {
            _availableRealParticipants = availableRealParticipants;
            _availableRealParticipants.CollectionChanged += OnRealParticipantsChanged;
        }

        RefreshAvailableOptions();
        _selectedRealParticipant = !string.IsNullOrWhiteSpace(displayName) ? displayName : id;
    }

    private void OnRealParticipantsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshAvailableOptions();
    }

    public void RefreshAvailableOptions()
    {
        var current = SelectedRealParticipant ?? DisplayName;
        AvailableOptions.Clear();

        if (!string.IsNullOrEmpty(Id))
        {
            AvailableOptions.Add(Id);
        }

        foreach (var p in _availableRealParticipants)
        {
            if (!string.IsNullOrWhiteSpace(p) &&
                !string.Equals(p, Id, StringComparison.OrdinalIgnoreCase) &&
                !AvailableOptions.Contains(p, StringComparer.OrdinalIgnoreCase))
            {
                AvailableOptions.Add(p);
            }
        }

        if (!string.IsNullOrWhiteSpace(current) && !AvailableOptions.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            AvailableOptions.Add(current);
        }

        _selectedRealParticipant = current;
        OnPropertyChanged(nameof(SelectedRealParticipant));
    }

    partial void OnDisplayNameChanged(string value)
    {
        if (_selectedRealParticipant != value)
        {
            _selectedRealParticipant = value;
            OnPropertyChanged(nameof(SelectedRealParticipant));
        }
        _onNameChanged?.Invoke(Id, value);
    }

    /// <summary>
    /// Confirma la edición del nombre del participante recortando espacios sobrantes al inicio y final.
    /// Si el nombre queda vacío o solo contiene espacios, revierte al identificador (Id).
    /// </summary>
    public void CommitDisplayName()
    {
        var trimmed = DisplayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            trimmed = Id;
        }

        if (DisplayName != trimmed)
        {
            DisplayName = trimmed;
        }

        var commitCallback = _onNameCommitted ?? _onNameChanged;
        commitCallback?.Invoke(Id, trimmed);
    }
}
