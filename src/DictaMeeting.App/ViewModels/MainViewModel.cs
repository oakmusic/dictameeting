using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.AI.Services;
using DictaMeeting.Audio.Events;
using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Models;
using DictaMeeting.Diarization.Interfaces;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Vocabulary;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using Microsoft.Extensions.Logging;
using DictaMeeting.App.Services;

namespace DictaMeeting.App.ViewModels;

public class TranscriptionModelOption
{
    public bool IsAutomatic { get; set; }
    public bool IsSameAsLive { get; set; }
    public ModelSize? ModelSize { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public override string ToString() => DisplayName;
}

public class LanguageOption
{
    public LanguageMode Mode { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public override string ToString() => DisplayName;
}

public partial class MainViewModel : ObservableObject
{
    private readonly IMeetingService _meetingService;
    private readonly IDocumentExporter _documentExporter;
    private readonly IMeetingRepository _repository;
    private readonly IAudioDeviceService _audioDeviceService;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly IModelManager _modelManager;
    private readonly ITranscriptionService _transcriptionService;
    private readonly ILiveTranscriptionPipeline _livePipeline;
    private readonly ISpeakerDiarizationService _diarizationService;
    private readonly ISpeakerTranscriptAligner _speakerAligner;
    private readonly IAiActaService _aiActaService;
    private readonly ISecureStorageService _secureStorage;
    private readonly IUserSettingsService _userSettingsService;
    private readonly IMeetingProcessingService _meetingProcessingService;
    private readonly ILiveSummaryCoordinator _liveSummaryCoordinator;
    private readonly ILiveSummaryModelManager _liveSummaryModelManager;
    private readonly IVocabularyService _vocabularyService;
    private readonly ILogger<MainViewModel>? _logger;
    private readonly IAudioPlayerService _audioPlayerService;
    private readonly ITranscriptAudioSyncService _transcriptAudioSyncService;
    private readonly DispatcherTimer _durationTimer;
    private DateTimeOffset _meetingStartTime;
    private bool _isLoadingSettings;
    private bool _isUserSeekingAudio;
    private bool _suppressNextAutoScroll;

    public VocabularyManagerViewModel VocabularyManager { get; }
    public int VocabularyCount => _vocabularyService?.GetTerms().Count ?? 0;

    public Task? CurrentProcessingTask { get; internal set; }

    private static readonly string[] SpeakerColors = Meeting.SpeakerColorPalette;
    private static string Loc(string key) => LocalizationManager.Instance.GetString(key);
    private static string LocFmt(string key, params object[] args) => string.Format(LocalizationManager.Instance.GetString(key), args);

    [ObservableProperty]
    private MeetingState _currentState = MeetingState.Idle;

    partial void OnCurrentStateChanged(MeetingState value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(CanStartMeeting));
        OnPropertyChanged(nameof(CanStopMeeting));
        OnPropertyChanged(nameof(CanExportMeeting));
        OnPropertyChanged(nameof(CanOpenMeetingFolder));
        OnPropertyChanged(nameof(PrepStatusTitle));
        OnPropertyChanged(nameof(PrepStatusSubtitle));
        OnPropertyChanged(nameof(TranscriptHeaderTitle));
        OnPropertyChanged(nameof(SummaryHeaderTitle));
        OnPropertyChanged(nameof(IsOpenMeetingCompleted));
        OnPropertyChanged(nameof(IsPreparedNewMeeting));
        OnPropertyChanged(nameof(CanReprocessMeeting));
        OnPropertyChanged(nameof(CanEditMeetingTitle));
        OnPropertyChanged(nameof(OpenMeetingSubtitle));
        OnPropertyChanged(nameof(OpenMeetingAudioStatusText));
        OnPropertyChanged(nameof(HasPlayerAudio));
        OnPropertyChanged(nameof(IsAudioPlaying));
    }

    [ObservableProperty]
    private bool _isStartingMeeting;

    partial void OnIsStartingMeetingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanStartMeeting));
        OnPropertyChanged(nameof(StartButtonText));
        OnPropertyChanged(nameof(StartButtonToolTip));
        OnPropertyChanged(nameof(PrepStatusTitle));
        OnPropertyChanged(nameof(PrepStatusSubtitle));
    }

    [ObservableProperty]
    private bool _isStoppingMeeting;

    partial void OnIsStoppingMeetingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStopMeeting));
        OnPropertyChanged(nameof(StopButtonText));
        OnPropertyChanged(nameof(StopButtonToolTip));
    }

    [ObservableProperty]
    private string _meetingTitle = "Reunión de Equipo";

    [ObservableProperty]
    private bool _isEditingMeetingTitle;

    [RelayCommand]
    private void StartEditMeetingTitle()
    {
        if (!IsIdle && !IsOpenMeetingCompleted) return;
        IsEditingMeetingTitle = true;
    }

    [RelayCommand]
    private void FinishEditMeetingTitle()
    {
        if (string.IsNullOrWhiteSpace(MeetingTitle))
        {
            MeetingTitle = "Reunión de Equipo";
        }
        MeetingTitle = MeetingTitle.Trim();
        IsEditingMeetingTitle = false;
        if (_meetingService.CurrentMeeting != null)
        {
            _meetingService.CurrentMeeting.Title = MeetingTitle;
            if (CurrentState == MeetingState.Completed || _meetingService.CurrentMeeting.State == MeetingState.Completed)
            {
                _ = _repository.SaveMeetingAsync(_meetingService.CurrentMeeting, CancellationToken.None);
                _ = LoadSavedMeetingsAsync();
            }
        }
        RefreshWorkspaceState();
    }

    [RelayCommand]
    private void CancelEditMeetingTitle()
    {
        IsEditingMeetingTitle = false;
    }

    [ObservableProperty]
    private string _organizer = string.Empty;

    [ObservableProperty]
    private string _company = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Listo para iniciar la reunión";

    #region Live Summary (llama.cpp Local) Properties & Commands

    [ObservableProperty]
    private int _liveStreamTab = 0; // 0: Transcripción, 1: Resumen en Vivo

    [RelayCommand]
    private void SelectLiveStreamTab0() => LiveStreamTab = 0;

    [RelayCommand]
    private void SelectLiveStreamTab1() => LiveStreamTab = 1;

    [ObservableProperty]
    private string _liveSummaryText = string.Empty;

    public ObservableCollection<LiveSummaryCardViewModel> LiveSummaryCards { get; } = new();

    [ObservableProperty]
    private string _liveSummaryStatusText = "A la espera de transcripción...";

    [ObservableProperty]
    private DateTimeOffset? _liveSummaryLastUpdated;

    [ObservableProperty]
    private bool _isLiveSummaryGenerating;

    [ObservableProperty]
    private bool _isLiveSummaryModelDownloaded;

    [ObservableProperty]
    private bool _isDownloadingSummaryModel;

    [ObservableProperty]
    private double _summaryModelDownloadProgress;

    [RelayCommand]
    private async Task DownloadSummaryModelAsync()
    {
        if (IsDownloadingSummaryModel) return;

        try
        {
            IsDownloadingSummaryModel = true;
            SummaryModelDownloadProgress = 0.0;
            StatusMessage = "Descargando modelo local de resumen Qwen2.5-1.5B (GGUF Q4_K_M)...";

            var progress = new Progress<double>(p =>
            {
                SummaryModelDownloadProgress = p;
            });

            await _liveSummaryModelManager.EnsureModelDownloadedAsync(progress);
            IsLiveSummaryModelDownloaded = true;
            IsDownloadingSummaryModel = false;
            StatusMessage = "Modelo de resumen en vivo descargado y listo.";
            UpdateLiveSummaryRelativeTime();
        }
        catch (Exception ex)
        {
            IsDownloadingSummaryModel = false;
            _logger?.LogError(ex, "Error al descargar el modelo local de resumen.");
            StatusMessage = $"Error al descargar modelo de resumen: {ex.Message}";
        }
    }

    #endregion

    [ObservableProperty]
    private string _durationText = "00:00:00";

    [ObservableProperty]
    private bool _captureMicrophone = true;

    [ObservableProperty]
    private bool _captureSystemAudio = true;

    [ObservableProperty]
    private bool _isMicrophoneMuted;

    [ObservableProperty]
    private bool _isSystemAudioMuted;

    [ObservableProperty]
    private bool _isSpeakingWhileMutedAlertVisible;

    public bool CanMuteMicrophoneDuringRecording => IsRecording && CaptureMicrophone;
    public bool CanMuteSystemAudioDuringRecording => IsRecording && CaptureSystemAudio;

    public string MuteButtonText => IsMicrophoneMuted ? "Silenciado" : "Silenciar";

    public string MuteButtonToolTip => IsMicrophoneMuted
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Audio_Mic_Unmute_Tooltip"]
        : DictaMeeting.App.Services.LocalizationManager.Instance["Audio_Mic_Mute_Tooltip"];

    public string MicrophoneLiveLabelText => IsMicrophoneMuted
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Audio_Mic_Muted_Label"]
        : DictaMeeting.App.Services.LocalizationManager.Instance["Audio_Mic_Label"];

    public string SystemAudioLiveLabelText => IsSystemAudioMuted
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Audio_System_Muted_Label"]
        : DictaMeeting.App.Services.LocalizationManager.Instance["Audio_System_Label"];

    public string SystemAudioToolTip => IsSystemAudioMuted
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Audio_System_Unmute_Tooltip"]
        : DictaMeeting.App.Services.LocalizationManager.Instance["Audio_System_Mute_Tooltip"];

    [ObservableProperty]
    private float _micLevel = 0.0f;

    [ObservableProperty]
    private float _systemLevel = 0.0f;

    [ObservableProperty]
    private bool _isMonitoringAudio;

    [ObservableProperty]
    private bool _isDownloadingModel;

    [ObservableProperty]
    private double _modelDownloadProgress;

    [ObservableProperty]
    private string _modelDownloadStatusText = string.Empty;

    [ObservableProperty]
    private bool _isPostProcessing;

    [ObservableProperty]
    private bool _canCancelPostProcessing = true;

    [ObservableProperty]
    private double _postProcessingProgress;

    [ObservableProperty]
    private string _postProcessingStatusText = string.Empty;

    public string TranscriptHeaderTitle => (CurrentState, IsPostProcessing) switch
    {
        (_, true) => DictaMeeting.App.Services.LocalizationManager.Instance["Transcript_Header_Processing"],
        (MeetingState.Processing or MeetingState.Finalizing, _) => DictaMeeting.App.Services.LocalizationManager.Instance["Transcript_Header_Processing"],
        (MeetingState.Recording, _) => DictaMeeting.App.Services.LocalizationManager.Instance["Transcript_Header_Live"],
        (MeetingState.Completed, _) => DictaMeeting.App.Services.LocalizationManager.Instance["Transcript_Header_Final"],
        _ => DictaMeeting.App.Services.LocalizationManager.Instance["Transcript_Header_Default"]
    };

    public string SummaryHeaderTitle => (CurrentState, IsPostProcessing) switch
    {
        (_, true) => DictaMeeting.App.Services.LocalizationManager.Instance["Summary_Header_Processing"],
        (MeetingState.Processing or MeetingState.Finalizing, _) => DictaMeeting.App.Services.LocalizationManager.Instance["Summary_Header_Processing"],
        (MeetingState.Recording, _) => DictaMeeting.App.Services.LocalizationManager.Instance["Summary_Header_Live"],
        (MeetingState.Completed, _) => DictaMeeting.App.Services.LocalizationManager.Instance["Summary_Header_Final"],
        _ => DictaMeeting.App.Services.LocalizationManager.Instance["Summary_Header_Live"]
    };

    public string TranscriptHeaderSubtitle => CurrentState switch
    {
        MeetingState.Completed => IsPostProcessing ? DictaMeeting.App.Services.LocalizationManager.Instance["Transcript_Subtitle_Processing"] : DictaMeeting.App.Services.LocalizationManager.Instance["Transcript_Subtitle_Completed"],
        MeetingState.Recording => DictaMeeting.App.Services.LocalizationManager.Instance["Transcript_Subtitle_Live"],
        _ => DictaMeeting.App.Services.LocalizationManager.Instance["Transcript_Subtitle_Default"]
    };

    partial void OnIsPostProcessingChanged(bool value)
    {
        OnPropertyChanged(nameof(TranscriptHeaderTitle));
        OnPropertyChanged(nameof(SummaryHeaderTitle));
        OnPropertyChanged(nameof(TranscriptHeaderSubtitle));
        OnPropertyChanged(nameof(CanExportMeeting));
        OnPropertyChanged(nameof(HasPlayerAudio));
        OnPropertyChanged(nameof(CanReprocessMeeting));
        OnPropertyChanged(nameof(IsOpenMeetingCompleted));
        OnPropertyChanged(nameof(OpenMeetingAudioStatusText));
        OnPropertyChanged(nameof(CanEditMeetingTitle));
    }

    [ObservableProperty]
    private string _meetingFolderPath = string.Empty;

    [ObservableProperty]
    private AudioDevice? _selectedMicrophone;

    [ObservableProperty]
    private AudioDevice? _selectedSystemDevice;

    [ObservableProperty]
    private TranscriptionModelOption? _selectedModel;

    [ObservableProperty]
    private TranscriptionModelOption? _selectedLiveModel;

    [ObservableProperty]
    private TranscriptionModelOption? _selectedFinalModel;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    public ObservableCollection<AudioDevice> AvailableMicrophones { get; } = new();
    public ObservableCollection<AudioDevice> AvailableSystemDevices { get; } = new();
    public ObservableCollection<TranscriptionModelOption> AvailableLiveModels { get; } = new();
    public ObservableCollection<TranscriptionModelOption> AvailableFinalModels { get; } = new();
    public ObservableCollection<TranscriptionModelOption> AvailableModels { get; } = new();
    public ObservableCollection<LanguageOption> AvailableLanguages { get; } = new();
    public ObservableCollection<SpeakerViewModel> Participants { get; } = new();
    public ObservableCollection<string> RealParticipants { get; } = new();
    public ObservableCollection<TranscriptSegmentViewModel> TranscriptSegments { get; } = new();

    [ObservableProperty]
    private string _newRealParticipantName = string.Empty;

    [RelayCommand]
    private void AddRealParticipant()
    {
        var name = NewRealParticipantName?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        if (!RealParticipants.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            RealParticipants.Add(name);
            if (_meetingService.CurrentMeeting != null)
            {
                if (!_meetingService.CurrentMeeting.ExpectedParticipants.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    _meetingService.CurrentMeeting.ExpectedParticipants.Add(name);
                }
                if (CurrentState == MeetingState.Completed)
                {
                    _ = _repository.SaveMeetingAsync(_meetingService.CurrentMeeting, CancellationToken.None);
                }
            }
        }
        NewRealParticipantName = string.Empty;
    }

    [RelayCommand]
    private void RemoveRealParticipant(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var existing = RealParticipants.FirstOrDefault(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            RealParticipants.Remove(existing);
            if (_meetingService.CurrentMeeting != null)
            {
                _meetingService.CurrentMeeting.ExpectedParticipants.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
                if (CurrentState == MeetingState.Completed)
                {
                    _ = _repository.SaveMeetingAsync(_meetingService.CurrentMeeting, CancellationToken.None);
                }
            }
        }
    }

    [ObservableProperty]
    private bool _isMeetingSettingsModalOpen;

    public string MicrophoneSummary
    {
        get => !CaptureMicrophone ? "Micro: Desactivado" :
               SelectedMicrophone != null ? $"Micro: {SelectedMicrophone.Name}" : "Micro: No detectado";
        set { }
    }

    public string SystemAudioSummary
    {
        get => !CaptureSystemAudio ? "Sistema: Desactivado" :
               SelectedSystemDevice != null ? $"Sistema: {SelectedSystemDevice.Name}" : "Sistema: No detectado";
        set { }
    }

    public string LiveModelSummary
    {
        get => $"En vivo: {SelectedLiveModel?.DisplayName ?? "Auto"}";
        set { }
    }

    public string FinalModelSummary
    {
        get => $"Final: {SelectedFinalModel?.DisplayName ?? "Auto"}";
        set { }
    }

    public string LanguageSummary
    {
        get => $"Idioma: {SelectedLanguage?.DisplayName ?? "Auto"}";
        set { }
    }

    public int TotalParticipantsCount => Math.Max(RealParticipants.Count, Participants.Count);

    public string ParticipantsSummaryText => TotalParticipantsCount.ToString();

    public string AudioSourcesSummary
    {
        get
        {
            var loc = DictaMeeting.App.Services.LocalizationManager.Instance;
            if (CaptureMicrophone && CaptureSystemAudio) return loc["AudioSource_MicSys"];
            if (CaptureMicrophone) return loc["AudioSource_Mic"];
            if (CaptureSystemAudio) return loc["AudioSource_Sys"];
            return loc["AudioSource_None"];
        }
    }

    public string AudioSourcesToolTip
    {
        get
        {
            var loc = DictaMeeting.App.Services.LocalizationManager.Instance;
            var micStatus = CaptureMicrophone
                ? (SelectedMicrophone != null ? string.Format(loc["AudioSource_Mic_Enabled"], SelectedMicrophone.Name) : string.Format(loc["AudioSource_Mic_Enabled"], loc["AudioSource_Activated"]))
                : loc["AudioSource_Mic_Disabled"];
            var sysStatus = CaptureSystemAudio
                ? (SelectedSystemDevice != null ? string.Format(loc["AudioSource_Sys_Enabled"], SelectedSystemDevice.Name) : string.Format(loc["AudioSource_Sys_Enabled"], loc["AudioSource_Activated"]))
                : loc["AudioSource_Sys_Disabled"];
            return string.Format(loc["Meeting_Audio_Sources_Tooltip"], micStatus, sysStatus);
        }
    }

    public string ModelsSummary
    {
        get
        {
            var loc = DictaMeeting.App.Services.LocalizationManager.Instance;
            string live = GetModelShortName(SelectedLiveModel);
            string final = GetModelShortName(SelectedFinalModel);
            return string.Format(loc["Meeting_Models_Summary"], live, final);
        }
    }

    public string LanguageDisplayShort => SelectedLanguage?.Mode switch
    {
        LanguageMode.Spanish => "Español",
        LanguageMode.English => "English",
        LanguageMode.Auto => "Auto",
        _ => SelectedLanguage?.DisplayName ?? "Español"
    };

    private static string GetModelShortName(TranscriptionModelOption? model)
    {
        if (model == null || model.IsAutomatic) return "Auto";
        if (model.IsSameAsLive) return "Auto";
        if (model.ModelSize.HasValue)
        {
            return model.ModelSize.Value switch
            {
                ModelSize.Tiny => "Tiny",
                ModelSize.Base => "Base",
                ModelSize.Small => "Small",
                ModelSize.Medium => "Medium",
                ModelSize.LargeV3Turbo => "Large-v3",
                ModelSize.Qwen3_06B => "Qwen3 0.6B",
                ModelSize.Qwen3_17B => "Qwen3 1.7B",
                _ => model.ModelSize.Value.ToString()
            };
        }
        return model.DisplayName;
    }

    [RelayCommand]
    private async Task OpenMeetingSettingsAsync()
    {
        IsMeetingSettingsModalOpen = true;
        await LoadAudioDevicesAsync();
    }

    [RelayCommand]
    private void CloseMeetingSettings()
    {
        if (IsMonitoringAudio)
        {
            _ = ToggleAudioMonitoringAsync();
        }
        IsMeetingSettingsModalOpen = false;
        SaveCurrentUserSettings();
    }

    public void SaveCurrentUserSettings()
    {
        if (_isLoadingSettings)
        {
            return;
        }

        try
        {
            var previousSettings = _userSettingsService?.LoadSettings() ?? new UserSettings();

            var settings = new UserSettings
            {
                CaptureMicrophone = CaptureMicrophone,
                SelectedMicrophoneId = SelectedMicrophone?.Id ?? (AvailableMicrophones.Count == 0 ? previousSettings.SelectedMicrophoneId : null),
                SelectedMicrophoneName = SelectedMicrophone?.Name ?? (AvailableMicrophones.Count == 0 ? previousSettings.SelectedMicrophoneName : null),
                CaptureSystemAudio = CaptureSystemAudio,
                SelectedSystemDeviceId = SelectedSystemDevice?.Id ?? (AvailableSystemDevices.Count == 0 ? previousSettings.SelectedSystemDeviceId : null),
                SelectedSystemDeviceName = SelectedSystemDevice?.Name ?? (AvailableSystemDevices.Count == 0 ? previousSettings.SelectedSystemDeviceName : null),
                SelectedLiveModel = SelectedLiveModel?.ModelSize?.ToString() ?? (SelectedLiveModel?.IsAutomatic == true ? "Auto" : null),
                SelectedFinalModel = SelectedFinalModel?.IsSameAsLive == true ? "SameAsLive" : (SelectedFinalModel?.ModelSize?.ToString() ?? (SelectedFinalModel?.IsAutomatic == true ? "Auto" : null)),
                SelectedLanguage = SelectedLanguage?.Mode.ToString(),
                UiLanguage = UiLanguage,
                IsDarkMode = IsDarkMode,
                CustomAiEndpoint = CustomAiEndpoint,
                CustomAiModel = CustomAiModel,
                SelectedAiModelId = SelectedAiModel?.Id,
                SelectedAiConfigProvider = SelectedAiConfigProvider,
                ShowRecordingNotice = ShowRecordingNotice,
                HasSeenRecordingNotice = HasSeenRecordingNotice,
                FirstRunModelsPromptCompleted = FirstRunModelsPromptCompleted,
                ModelsDirectory = IsCustomModelsDirectory ? CurrentModelsDirectory : null
            };
            _userSettingsService?.SaveSettings(settings);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al guardar preferencias de usuario.");
        }
    }

    partial void OnSelectedLiveModelChanged(TranscriptionModelOption? value)
    {
        if (_selectedModel != value)
        {
            _selectedModel = value;
            OnPropertyChanged(nameof(SelectedModel));
        }
        OnPropertyChanged(nameof(LiveModelSummary));
        OnPropertyChanged(nameof(ModelsSummary));
        SaveCurrentUserSettings();
    }

    partial void OnSelectedModelChanged(TranscriptionModelOption? value)
    {
        if (_selectedLiveModel != value)
        {
            _selectedLiveModel = value;
            OnPropertyChanged(nameof(SelectedLiveModel));
        }
    }

    partial void OnSelectedFinalModelChanged(TranscriptionModelOption? value)
    {
        OnPropertyChanged(nameof(FinalModelSummary));
        OnPropertyChanged(nameof(ModelsSummary));
        SaveCurrentUserSettings();
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        OnPropertyChanged(nameof(LanguageSummary));
        OnPropertyChanged(nameof(LanguageDisplayShort));
        SaveCurrentUserSettings();
    }

    // Propiedades de IA y Generación de Actas (OpenRouter)
    public ObservableCollection<AiModelOption> AvailableAiModels { get; } = new();
    public ObservableCollection<ActaDetailLevel> AvailableDetailLevels { get; } = new();

    [ObservableProperty]
    private AiModelOption? _selectedAiModel;

    [ObservableProperty]
    private string _customModelName = string.Empty;

    [ObservableProperty]
    private bool _isCustomModelSelected;

    [ObservableProperty]
    private ActaDetailLevel _selectedDetailLevel = ActaDetailLevel.Normal;

    [ObservableProperty]
    private LanguageMode _selectedActaLanguage = LanguageMode.Spanish;

    [ObservableProperty]
    private bool _includeParticipants = true;

    [ObservableProperty]
    private bool _includeDecisions = true;

    [ObservableProperty]
    private bool _includeActionItems = true;

    [ObservableProperty]
    private bool _includePendingQuestions = true;

    [ObservableProperty]
    private bool _isImpersonalActa = true;

    [ObservableProperty]
    private bool _isGeneratingActa;

    [ObservableProperty]
    private string _actaGenerationStatus = string.Empty;

    [ObservableProperty]
    private string _actaErrorMessage = string.Empty;

    [ObservableProperty]
    private string _generatedActaMarkdown = string.Empty;

    [ObservableProperty]
    private string _actaTokensUsedText = string.Empty;

    [ObservableProperty]
    private bool _hasGeneratedActa;

    [ObservableProperty]
    private bool _isActaModalOpen;

    [ObservableProperty]
    private bool _isAiConfigModalOpen;

    // Proveedor seleccionado en la configuración de IA ("OpenRouter" o "CustomServer")
    [ObservableProperty]
    private string _selectedAiConfigProvider = "OpenRouter";

    public bool IsOpenRouterConfigSelected => SelectedAiConfigProvider == "OpenRouter";
    public bool IsCustomServerConfigSelected => SelectedAiConfigProvider == "CustomServer";

    public string PrivacyAiProviderSummary => SelectedAiConfigProvider == "CustomServer"
        ? "Servidor personalizado"
        : "OpenRouter";

    partial void OnSelectedAiConfigProviderChanged(string value)
    {
        OnPropertyChanged(nameof(IsOpenRouterConfigSelected));
        OnPropertyChanged(nameof(IsCustomServerConfigSelected));
        OnPropertyChanged(nameof(PrivacyAiProviderSummary));
        SaveCurrentUserSettings();
    }

    // Preferencias de Privacidad y Recordatorio previo a la grabación
    [ObservableProperty]
    private bool _showRecordingNotice = true;

    [ObservableProperty]
    private bool _hasSeenRecordingNotice = false;

    [ObservableProperty]
    private bool _isRecordingNoticeModalOpen;

    [ObservableProperty]
    private bool _isPrivacySettingsModalOpen;

    partial void OnShowRecordingNoticeChanged(bool value)
    {
        if (!_isLoadingSettings)
        {
            SaveCurrentUserSettings();
        }
    }

    // Configuración OpenRouter AI
    [ObservableProperty]
    private string _apiKeyInput = string.Empty;

    [ObservableProperty]
    private bool _hasConfiguredApiKey;

    [ObservableProperty]
    private string _apiKeyStatusText = string.Empty;

    [ObservableProperty]
    private bool _isOpenRouterTestingConnection;

    [ObservableProperty]
    private string _openRouterConnectionTestResult = string.Empty;

    [ObservableProperty]
    private bool? _isOpenRouterTestSuccess;

    // Configuración Servidor Personalizado OpenAI-Compatible
    [ObservableProperty]
    private string _customAiEndpoint = string.Empty;

    [ObservableProperty]
    private string _customAiModel = string.Empty;

    [ObservableProperty]
    private string _customServerApiKeyInput = string.Empty;

    [ObservableProperty]
    private bool _hasConfiguredCustomServerApiKey;

    [ObservableProperty]
    private string _customServerApiKeyStatusText = string.Empty;

    [ObservableProperty]
    private bool _isCustomServerTestingConnection;

    [ObservableProperty]
    private string _customServerConnectionTestResult = string.Empty;

    [ObservableProperty]
    private bool? _isCustomServerTestSuccess;

    // Tema (Modo Claro por Defecto / Modo Oscuro)
    [ObservableProperty]
    private bool _isDarkMode;

    // Idioma de la interfaz de usuario ("es" o "en")
    [ObservableProperty]
    private string _uiLanguage = UserSettingsService.DetectWindowsLanguage();

    partial void OnUiLanguageChanged(string value)
    {
        DictaMeeting.App.Services.LocalizationManager.Instance.SetLanguage(value);
        OnPropertyChanged(nameof(SystemSummaryBadge));
        OnPropertyChanged(nameof(ThemeToggleToolTip));
        OnPropertyChanged(nameof(CurrentThemeName));
        SaveCurrentUserSettings();
    }

    public string ThemeToggleIcon => IsDarkMode ? "☀" : "🌙";
    public string ThemeToggleToolTip => IsDarkMode 
        ? DictaMeeting.App.Services.LocalizationManager.Instance.GetString("Theme_Toggle_Dark_Tooltip") 
        : DictaMeeting.App.Services.LocalizationManager.Instance.GetString("Theme_Toggle_Light_Tooltip");
    public string CurrentThemeName => IsDarkMode 
        ? DictaMeeting.App.Services.LocalizationManager.Instance.GetString("Settings_Theme_Dark") 
        : DictaMeeting.App.Services.LocalizationManager.Instance.GetString("Settings_Theme_Light");

    public string SystemSummaryBadge => $"{CurrentThemeName} · {(UiLanguage == "en" ? "English" : "Español")}";

    [ObservableProperty]
    private bool _isSystemSettingsModalOpen;

    [RelayCommand]
    private void OpenSystemSettingsFromSettings()
    {
        IsOpenedFromGlobalSettings = true;
        IsGlobalSettingsModalOpen = false;
        IsSystemSettingsModalOpen = true;
    }

    [RelayCommand]
    private void OpenSystemSettings()
    {
        IsSystemSettingsModalOpen = true;
    }

    [RelayCommand]
    private void CloseSystemSettingsModal()
    {
        IsSystemSettingsModalOpen = false;
        SaveCurrentUserSettings();
        if (IsOpenedFromGlobalSettings)
        {
            IsOpenedFromGlobalSettings = false;
            IsGlobalSettingsModalOpen = true;
        }
    }

    [RelayCommand]
    private void SelectLanguage(string languageCode)
    {
        UiLanguage = string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "es";
    }

    [RelayCommand]
    private void SelectTheme(string theme)
    {
        IsDarkMode = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        DictaMeeting.App.Services.ThemeManager.ApplyTheme(value);
        OnPropertyChanged(nameof(ThemeToggleIcon));
        OnPropertyChanged(nameof(ThemeToggleToolTip));
        OnPropertyChanged(nameof(CurrentThemeName));
        OnPropertyChanged(nameof(SystemSummaryBadge));
        SaveCurrentUserSettings();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
    }

    // Navegación Principal (0: Nueva Reunión, 1: Historial de Reuniones)
    [ObservableProperty]
    private int _selectedNavigationTab;

    partial void OnSelectedNavigationTabChanged(int value)
    {
        if (value == 1)
        {
            _ = LoadSavedMeetingsAsync();
        }
    }

    [RelayCommand]
    private void CreateNewMeeting()
    {
        if (IsRecording || IsStartingMeeting)
        {
            MessageBox.Show(Loc("Msg_Meeting_Active_NewSession"),
                Loc("Msg_Meeting_Active_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 1. Resetear servicios
        _meetingService.Reset();
        _diarizationService.Reset();
        _audioPlayerService.Stop();
        _audioPlayerService.Close();
        _transcriptAudioSyncService.Clear();

        // 2. Limpiar datos y colecciones
        Participants.Clear();
        RealParticipants.Clear();
        TranscriptSegments.Clear();
        LiveSummaryCards.Clear();
        SelectedHistoricalMeeting = null;
        DurationText = "00:00:00";
        MicLevel = 0;
        SystemLevel = 0;

        // 3. Resetear metadatos con reloj actual del sistema
        MeetingTitle = $"Reunión {DateTime.Now:yyyy-MM-dd HH:mm}";
        Organizer = string.Empty;
        Company = string.Empty;
        GeneratedActaMarkdown = string.Empty;
        HasGeneratedActa = false;
        WorkspaceRightTab = 0;
        HistoricalDetailTab = 0;

        // 4. Asegurar estado Idle y desbloqueo de controles de audio/whisper
        CurrentState = MeetingState.Idle;
        IsPostProcessing = false;
        CanCancelPostProcessing = true;
        PostProcessingProgress = 0.0;
        PostProcessingStatusText = string.Empty;
        RefreshWorkspaceState();

        // 5. Navegar a pestaña de reunión
        SelectedNavigationTab = 0;
        StatusMessage = "Nueva sesión lista. Configure los dispositivos y pulse 'Iniciar'.";
    }

    [RelayCommand]
    private void SelectNewMeetingTab() => SelectedNavigationTab = 0;

    [RelayCommand]
    private void SelectHistoryTab()
    {
        SelectedNavigationTab = 1;
        _ = LoadSavedMeetingsAsync();
    }

    // =========================================================================
    // IMPORTACIÓN DE AUDIO (DRAG & DROP)
    // =========================================================================
    [ObservableProperty]
    private bool _isImportAudioModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedImportAudioFile))]
    private string? _selectedImportAudioFilePath;

    [ObservableProperty]
    private string _selectedImportAudioFileName = string.Empty;

    [ObservableProperty]
    private string _selectedImportAudioFileSizeText = string.Empty;

    [ObservableProperty]
    private string _selectedImportAudioDurationText = string.Empty;

    [ObservableProperty]
    private string? _importAudioErrorMessage;

    public bool HasSelectedImportAudioFile => !string.IsNullOrWhiteSpace(SelectedImportAudioFilePath) && System.IO.File.Exists(SelectedImportAudioFilePath);

    [RelayCommand]
    private void OpenImportAudioModal()
    {
        if (IsRecording || IsStartingMeeting)
        {
            MessageBox.Show(Loc("Msg_Meeting_Active_ImportAudio"),
                Loc("Msg_Meeting_Active_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ResetImportAudioState();
        IsImportAudioModalOpen = true;
    }

    [RelayCommand]
    private void CloseImportAudioModal()
    {
        IsImportAudioModalOpen = false;
        ResetImportAudioState();
    }

    private void ResetImportAudioState(bool clearError = true)
    {
        SelectedImportAudioFilePath = null;
        SelectedImportAudioFileName = string.Empty;
        SelectedImportAudioFileSizeText = string.Empty;
        SelectedImportAudioDurationText = string.Empty;
        if (clearError)
        {
            ImportAudioErrorMessage = null;
        }
    }

    [RelayCommand]
    private void BrowseAudioFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Seleccionar archivo de audio para importar",
            Filter = SupportedAudioFormats.FileDialogFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            HandleDroppedAudioFile(dialog.FileName);
        }
    }

    public void HandleDroppedAudioFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            ResetImportAudioState(clearError: false);
            ImportAudioErrorMessage = "El archivo especificado no existe o no se puede acceder a él.";
            return;
        }

        if (!SupportedAudioFormats.IsSupported(filePath))
        {
            ResetImportAudioState(clearError: false);
            ImportAudioErrorMessage = $"Formato no compatible ({System.IO.Path.GetExtension(filePath)}). Formatos admitidos: {SupportedAudioFormats.DisplayList}.";
            return;
        }

        ImportAudioErrorMessage = null;
        SelectedImportAudioFilePath = filePath;
        SelectedImportAudioFileName = System.IO.Path.GetFileName(filePath);

        try
        {
            var fileInfo = new System.IO.FileInfo(filePath);
            SelectedImportAudioFileSizeText = FormatFileSize(fileInfo.Length);
        }
        catch
        {
            SelectedImportAudioFileSizeText = string.Empty;
        }

        SelectedImportAudioDurationText = TryGetAudioDurationString(filePath);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{(bytes / 1024.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} KB";
        return $"{(bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} MB";
    }

    private static string TryGetAudioDurationString(string filePath)
    {
        try
        {
            using var reader = new NAudio.Wave.AudioFileReader(filePath);
            var time = reader.TotalTime;
            if (time > TimeSpan.Zero)
            {
                return time.TotalHours >= 1
                    ? $"Duración: {(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}"
                    : $"Duración: {time.Minutes:D2}:{time.Seconds:D2}";
            }
        }
        catch
        {
            // Metadatos de duración no críticos
        }
        return string.Empty;
    }

    private static TimeSpan? TryGetAudioDuration(string filePath)
    {
        try
        {
            using var reader = new NAudio.Wave.AudioFileReader(filePath);
            return reader.TotalTime;
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private async Task ConfirmImportAudioAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedImportAudioFilePath) || !System.IO.File.Exists(SelectedImportAudioFilePath))
        {
            ImportAudioErrorMessage = "Seleccione un archivo de audio válido antes de continuar.";
            return;
        }

        var sourcePath = SelectedImportAudioFilePath;
        var duration = TryGetAudioDuration(sourcePath);
        var fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(sourcePath);

        // Cerrar modal
        IsImportAudioModalOpen = false;
        ResetImportAudioState();

        try
        {
            // 1. Crear reunión a través de MeetingService
            var meeting = await _meetingService.CreateImportedMeetingAsync(
                title: fileNameWithoutExt,
                sourceAudioFilePath: sourcePath,
                audioDuration: duration,
                language: SelectedLanguage?.Mode.ToString());

            // 2. Registrar en la lista de reuniones guardadas inmediatamente
            var historyItem = SavedMeetings.FirstOrDefault(m => m.Id == meeting.Id);
            if (historyItem == null)
            {
                historyItem = new MeetingHistoryItemViewModel(meeting);
                SavedMeetings.Insert(0, historyItem);
            }
            SelectedHistoricalMeeting = historyItem;

            // 3. Mostrar inmediatamente en la vista de historial
            SelectedNavigationTab = 1;

            OnPropertyChanged(nameof(HasCurrentMeeting));
            OnPropertyChanged(nameof(CanOpenMeetingFolder));
            OnPropertyChanged(nameof(CanExportMeeting));

            // 4. Determinar modelo definitivo (Two-Pass)
            string? finalModelToUse = null;
            var finalOption = SelectedFinalModel;
            if (finalOption != null)
            {
                if (finalOption.IsSameAsLive)
                {
                    finalModelToUse = _transcriptionService.CurrentModel.ToString();
                }
                else if (finalOption.IsAutomatic)
                {
                    var hw = await _modelManager.DetectHardwareAsync();
                    finalModelToUse = hw.RecommendedFinalModel.ToString();
                }
                else if (finalOption.ModelSize.HasValue)
                {
                    finalModelToUse = finalOption.ModelSize.Value.ToString();
                }
            }

            // 5. Lanzar procesamiento final en segundo plano
            StatusMessage = $"Reunión '{meeting.Title}' importada. Procesando transcripción en segundo plano...";
            var bgTask = _meetingProcessingService.EnqueueOrProcessAsync(meeting.Id, finalModelToUse);
            CurrentProcessingTask = bgTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al importar el archivo de audio '{Path}'.", sourcePath);
            MessageBox.Show(LocFmt("Msg_Import_Audio_Error", ex.Message),
                Loc("Msg_Import_Audio_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =========================================================================
    // IMPORTACIÓN DE TRANSCRIPCIÓN (DRAG & DROP)
    // =========================================================================
    [ObservableProperty]
    private bool _isImportTranscriptModalOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedImportTranscriptFile))]
    private string? _selectedImportTranscriptFilePath;

    [ObservableProperty]
    private string _selectedImportTranscriptFileName = string.Empty;

    [ObservableProperty]
    private string _selectedImportTranscriptFileSizeText = string.Empty;

    [ObservableProperty]
    private string? _importTranscriptErrorMessage;

    public bool HasSelectedImportTranscriptFile => !string.IsNullOrWhiteSpace(SelectedImportTranscriptFilePath) && System.IO.File.Exists(SelectedImportTranscriptFilePath);

    [RelayCommand]
    private void OpenImportTranscriptModal()
    {
        if (IsRecording || IsStartingMeeting)
        {
            MessageBox.Show(Loc("Msg_Meeting_Active_ImportTranscript"),
                Loc("Msg_Meeting_Active_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ResetImportTranscriptState();
        IsImportTranscriptModalOpen = true;
    }

    [RelayCommand]
    private void CloseImportTranscriptModal()
    {
        IsImportTranscriptModalOpen = false;
        ResetImportTranscriptState();
    }

    private void ResetImportTranscriptState(bool clearError = true)
    {
        SelectedImportTranscriptFilePath = null;
        SelectedImportTranscriptFileName = string.Empty;
        SelectedImportTranscriptFileSizeText = string.Empty;
        if (clearError)
        {
            ImportTranscriptErrorMessage = null;
        }
    }

    [RelayCommand]
    private void BrowseTranscriptFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Seleccionar archivo de transcripción para importar",
            Filter = DictaMeeting.Meetings.Models.SupportedTranscriptFormats.FileDialogFilter,
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            HandleDroppedTranscriptFile(dialog.FileName);
        }
    }

    public void HandleDroppedTranscriptFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            ResetImportTranscriptState(clearError: false);
            ImportTranscriptErrorMessage = "El archivo especificado no existe o no se puede acceder a él.";
            return;
        }

        if (!DictaMeeting.Meetings.Models.SupportedTranscriptFormats.IsSupported(filePath))
        {
            ResetImportTranscriptState(clearError: false);
            ImportTranscriptErrorMessage = $"Formato no compatible ({System.IO.Path.GetExtension(filePath)}). Formatos admitidos: {DictaMeeting.Meetings.Models.SupportedTranscriptFormats.DisplayList}.";
            return;
        }

        ImportTranscriptErrorMessage = null;
        SelectedImportTranscriptFilePath = filePath;
        SelectedImportTranscriptFileName = System.IO.Path.GetFileName(filePath);

        try
        {
            var fileInfo = new System.IO.FileInfo(filePath);
            SelectedImportTranscriptFileSizeText = FormatFileSize(fileInfo.Length);
        }
        catch
        {
            SelectedImportTranscriptFileSizeText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ConfirmImportTranscriptAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedImportTranscriptFilePath) || !System.IO.File.Exists(SelectedImportTranscriptFilePath))
        {
            ImportTranscriptErrorMessage = "Seleccione un archivo de transcripción válido antes de continuar.";
            return;
        }

        var sourcePath = SelectedImportTranscriptFilePath;
        var fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(sourcePath);

        // Cerrar modal
        IsImportTranscriptModalOpen = false;
        ResetImportTranscriptState();

        try
        {
            // 1. Crear reunión a partir de la transcripción (sin audio, sin Whisper)
            var meeting = await _meetingService.CreateMeetingFromTranscriptAsync(
                title: fileNameWithoutExt,
                sourceTranscriptFilePath: sourcePath);

            // 2. Registrar en la lista de reuniones guardadas inmediatamente
            var historyItem = SavedMeetings.FirstOrDefault(m => m.Id == meeting.Id);
            if (historyItem == null)
            {
                historyItem = new MeetingHistoryItemViewModel(meeting);
                SavedMeetings.Insert(0, historyItem);
            }
            SelectedHistoricalMeeting = historyItem;

            // 3. Mostrar inmediatamente en la vista de historial
            SelectedNavigationTab = 1;

            OnPropertyChanged(nameof(HasCurrentMeeting));
            OnPropertyChanged(nameof(CanOpenMeetingFolder));
            OnPropertyChanged(nameof(CanExportMeeting));

            StatusMessage = $"Transcripción '{meeting.Title}' importada correctamente. {meeting.Transcript.Count} segmentos cargados.";
        }
        catch (System.IO.InvalidDataException ex)
        {
            _logger?.LogWarning(ex, "Archivo de transcripción inválido o vacío: '{Path}'.", sourcePath);
            MessageBox.Show(ex.Message,
                Loc("Msg_Import_Transcript_Invalid_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al importar la transcripción '{Path}'.", sourcePath);
            MessageBox.Show(LocFmt("Msg_Import_Transcript_Error", ex.Message),
                Loc("Msg_Import_Audio_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Ajustes Globales (Whisper, OpenRouter AI, Tema)

    [ObservableProperty]
    private bool _isGlobalSettingsModalOpen;

    [ObservableProperty]
    private bool _isOpenedFromGlobalSettings;

    [RelayCommand]
    private void OpenGlobalSettings()
    {
        CheckConfiguredApiKey();
        IsGlobalSettingsModalOpen = true;
    }

    [RelayCommand]
    private void CloseGlobalSettings()
    {
        IsGlobalSettingsModalOpen = false;
    }

    [RelayCommand]
    private void OpenModelManagerFromSettings()
    {
        IsOpenedFromGlobalSettings = true;
        IsGlobalSettingsModalOpen = false;
        OpenModelManager();
    }

    [RelayCommand]
    private void OpenAiConfigFromSettings()
    {
        IsOpenedFromGlobalSettings = true;
        IsGlobalSettingsModalOpen = false;
        OpenAiConfig();
    }

    [ObservableProperty]
    private bool _isVocabularyModalOpen;

    [RelayCommand]
    private void OpenVocabularyFromSettings()
    {
        IsOpenedFromGlobalSettings = true;
        IsGlobalSettingsModalOpen = false;
        VocabularyManager.RefreshTerms();
        IsVocabularyModalOpen = true;
    }

    [RelayCommand]
    private void CloseVocabularyModal()
    {
        IsVocabularyModalOpen = false;
        if (IsOpenedFromGlobalSettings)
        {
            IsOpenedFromGlobalSettings = false;
            IsGlobalSettingsModalOpen = true;
        }
    }

    [RelayCommand]
    private void OpenPrivacySettingsFromSettings()
    {
        IsOpenedFromGlobalSettings = true;
        IsGlobalSettingsModalOpen = false;
        IsPrivacySettingsModalOpen = true;
    }

    [RelayCommand]
    private void OpenPrivacySettings()
    {
        IsPrivacySettingsModalOpen = true;
    }

    [RelayCommand]
    private void ClosePrivacySettingsModal()
    {
        IsPrivacySettingsModalOpen = false;
        if (IsOpenedFromGlobalSettings)
        {
            IsOpenedFromGlobalSettings = false;
            IsGlobalSettingsModalOpen = true;
        }
    }

    // Catálogo y Gestión Explícita de Modelos Whisper
    public ObservableCollection<WhisperModelCardViewModel> ModelCatalog { get; } = new();

    [ObservableProperty]
    private bool _isModelManagerModalOpen;

    [RelayCommand]
    private void OpenModelManager()
    {
        if (string.IsNullOrWhiteSpace(FirstRunHardwareSummary))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var hw = await _modelManager.DetectHardwareAsync();
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        FirstRunHardwareSummary = $"CPU con {hw.CpuLogicalCores} núcleos y {hw.TotalRamGb:F0} GB de RAM. Recomendado en vivo: {hw.RecommendedLiveModel} | Final: {hw.RecommendedFinalModel}.";
                    });
                }
                catch
                {
                    // ignored
                }
            });
        }
        RefreshModelCatalog();
        IsModelManagerModalOpen = true;
    }

    [RelayCommand]
    private void CloseModelManager()
    {
        IsModelManagerModalOpen = false;
        if (IsOpenedFromGlobalSettings)
        {
            IsOpenedFromGlobalSettings = false;
            IsGlobalSettingsModalOpen = true;
        }
    }

    // Modal: Acerca de DictaMeeting
    [ObservableProperty]
    private bool _isAboutModalOpen;

    public string AppVersionText
    {
        get
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return version != null ? $"Versión {version.Major}.{version.Minor}.{version.Build}" : "Versión 1.5.2";
        }
    }

    public string AppVersionShortText
    {
        get
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v1.5.2";
        }
    }

    [RelayCommand]
    private void OpenAboutFromSettings()
    {
        IsOpenedFromGlobalSettings = true;
        IsGlobalSettingsModalOpen = false;
        IsAboutModalOpen = true;
    }

    [RelayCommand]
    private void CloseAboutModal()
    {
        IsAboutModalOpen = false;
        if (IsOpenedFromGlobalSettings)
        {
            IsOpenedFromGlobalSettings = false;
            IsGlobalSettingsModalOpen = true;
        }
    }

    [RelayCommand]
    private void OpenGitHubUrl()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/oakmusic/dictameeting",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudo abrir el enlace de GitHub.");
        }
    }

    // Historial de Reuniones Guardadas
    public ObservableCollection<MeetingHistoryItemViewModel> SavedMeetings { get; } = new();
    public ObservableCollection<TranscriptSegmentViewModel> HistoricalTranscriptSegments { get; } = new();
    public ObservableCollection<LiveSummaryCardViewModel> HistoricalSummaryCards { get; } = new();
    public ObservableCollection<string> HistoricalRealParticipants { get; } = new();
    public ObservableCollection<SpeakerViewModel> HistoricalParticipants { get; } = new();

    [ObservableProperty]
    private string _newHistoricalRealParticipantName = string.Empty;

    [RelayCommand]
    private void AddHistoricalRealParticipant()
    {
        var name = NewHistoricalRealParticipantName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || SelectedHistoricalMeeting == null) return;

        if (!HistoricalRealParticipants.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            HistoricalRealParticipants.Add(name);
            if (!SelectedHistoricalMeeting.Meeting.ExpectedParticipants.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                SelectedHistoricalMeeting.Meeting.ExpectedParticipants.Add(name);
            }
            _ = _repository.SaveMeetingAsync(SelectedHistoricalMeeting.Meeting, CancellationToken.None);
        }
        NewHistoricalRealParticipantName = string.Empty;
    }

    [RelayCommand]
    private void RemoveHistoricalRealParticipant(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || SelectedHistoricalMeeting == null) return;
        var existing = HistoricalRealParticipants.FirstOrDefault(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            HistoricalRealParticipants.Remove(existing);
            SelectedHistoricalMeeting.Meeting.ExpectedParticipants.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
            _ = _repository.SaveMeetingAsync(SelectedHistoricalMeeting.Meeting, CancellationToken.None);
        }
    }

    [ObservableProperty]
    private MeetingHistoryItemViewModel? _selectedHistoricalMeeting;

    [ObservableProperty]
    private string _historySearchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoadingHistory;

    [ObservableProperty]
    private string _selectedHistoryActaMarkdown = string.Empty;

    [ObservableProperty]
    private int _historicalDetailTab; // 0 = Transcripción, 1 = Resumen, 2 = Acta

    [RelayCommand]
    private void SelectHistoricalTab0() => HistoricalDetailTab = 0;

    [RelayCommand]
    private void SelectHistoricalTab1() => HistoricalDetailTab = 1;

    [RelayCommand]
    private void SelectHistoricalTab2() => HistoricalDetailTab = 2;

    [ObservableProperty]
    private bool _isEditingHistoricalTitle;

    [ObservableProperty]
    private string _editingHistoricalTitleText = string.Empty;

    [RelayCommand]
    private void StartEditHistoricalTitle()
    {
        if (SelectedHistoricalMeeting == null) return;
        EditingHistoricalTitleText = SelectedHistoricalMeeting.Title;
        IsEditingHistoricalTitle = true;
    }

    [RelayCommand]
    private async Task FinishEditHistoricalTitleAsync()
    {
        if (SelectedHistoricalMeeting == null)
        {
            IsEditingHistoricalTitle = false;
            return;
        }

        var newTitle = string.IsNullOrWhiteSpace(EditingHistoricalTitleText)
            ? SelectedHistoricalMeeting.Title
            : EditingHistoricalTitleText.Trim();

        SelectedHistoricalMeeting.Title = newTitle;
        try
        {
            await _repository.SaveMeetingAsync(SelectedHistoricalMeeting.Meeting, CancellationToken.None);
            StatusMessage = $"Título de la reunión actualizado a '{newTitle}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al guardar el nuevo título: {ex.Message}";
        }
        finally
        {
            IsEditingHistoricalTitle = false;
        }
    }

    [RelayCommand]
    private void CancelEditHistoricalTitle()
    {
        IsEditingHistoricalTitle = false;
    }

    private Meeting? _actaTargetMeeting;

    // Primer Arranque y Recomendación de Hardware
    [ObservableProperty]
    private bool _firstRunModelsPromptCompleted = false;

    [ObservableProperty]
    private bool _isFirstRunModelsModalOpen;

    [ObservableProperty]
    private bool _isFirstRunDownloading;

    [ObservableProperty]
    private double _firstRunTotalProgress;

    [ObservableProperty]
    private string _firstRunTotalProgressPercentText = "0%";

    [ObservableProperty]
    private string _firstRunStatusMessage = string.Empty;

    [ObservableProperty]
    private string? _firstRunErrorMessage;

    [ObservableProperty]
    private bool _firstRunHasError;

    [ObservableProperty]
    private string _firstRunDownloadButtonText = "Descargar modelos";

    [ObservableProperty]
    private int _firstRunMissingCount;

    public System.Collections.ObjectModel.ObservableCollection<FirstRunModelItemViewModel> FirstRunModels { get; } = new();

    private CancellationTokenSource? _firstRunCts;
    private bool _hasCheckedFirstRun;

    [ObservableProperty]
    private bool _isFirstRunOnboardingVisible;

    [ObservableProperty]
    private string _firstRunHardwareSummary = string.Empty;

    [ObservableProperty]
    private ModelSize _firstRunRecommendedModel = ModelSize.Qwen3_06B;

    // Configuración de Ubicación de Almacenamiento de Modelos Locales
    [ObservableProperty]
    private string _currentModelsDirectory = string.Empty;

    [ObservableProperty]
    private bool _isCustomModelsDirectory;

    [ObservableProperty]
    private string _modelsDirectoryTypeBadge = "Ubicación predeterminada";

    [ObservableProperty]
    private bool _isModelsDirectoryAccessible = true;

    [ObservableProperty]
    private string _modelsDirectoryWarningMessage = string.Empty;

    // Modal de Confirmación y Progreso de Migración de Modelos
    [ObservableProperty]
    private bool _isMoveModelsModalOpen;

    [ObservableProperty]
    private string? _pendingModelsDirectory;

    [ObservableProperty]
    private string _moveModelsSourceDirectory = string.Empty;

    [ObservableProperty]
    private string _moveModelsTargetDirectory = string.Empty;

    [ObservableProperty]
    private int _moveModelsFoundCount;

    [ObservableProperty]
    private string _moveModelsFoundFormattedSize = string.Empty;

    [ObservableProperty]
    private bool _isMovingModels;

    [ObservableProperty]
    private string _movingModelsCurrentItem = string.Empty;

    [ObservableProperty]
    private string _movingModelsCurrentOperation = string.Empty;

    [ObservableProperty]
    private double _movingModelsTotalProgress;

    [ObservableProperty]
    private string _movingModelsTotalProgressPercentText = "0%";

    [ObservableProperty]
    private string? _moveModelsErrorMessage;

    [ObservableProperty]
    private bool _moveModelsHasError;

    private CancellationTokenSource? _moveModelsCts;

    public bool IsIdle => CurrentState == MeetingState.Idle && !IsStartingMeeting;
    public bool IsRecording => CurrentState == MeetingState.Recording;
    public bool IsProcessing => CurrentState == MeetingState.Processing || CurrentState == MeetingState.Finalizing;
    public bool IsCompleted => CurrentState == MeetingState.Completed;
    public bool CanStartMeeting => !IsStartingMeeting && CurrentState == MeetingState.Idle && !IsOpenMeetingCompleted;
    public bool IsPreparedNewMeeting => !IsRecording && !IsOpenMeetingCompleted;
    public string StartButtonText => IsStartingMeeting
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Meeting_Starting"]
        : DictaMeeting.App.Services.LocalizationManager.Instance["Meeting_Start"];
    public string StartButtonToolTip => IsStartingMeeting
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Meeting_Starting_Tooltip"]
        : DictaMeeting.App.Services.LocalizationManager.Instance["Meeting_Start_Tooltip"];
    public bool CanStopMeeting => !IsStoppingMeeting && IsRecording;
    public string StopButtonText => IsStoppingMeeting
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Meeting_Stopping"]
        : DictaMeeting.App.Services.LocalizationManager.Instance["Meeting_Stop"];
    public string StopButtonToolTip => IsStoppingMeeting
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Meeting_Stopping_Tooltip"]
        : DictaMeeting.App.Services.LocalizationManager.Instance["Meeting_Stop_Tooltip"];
    public string PrepStatusTitle => IsStartingMeeting
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Meeting_Starting"]
        : DictaMeeting.App.Services.LocalizationManager.Instance["Meeting_Prepared"];
    public string PrepStatusSubtitle => IsStartingMeeting
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Meeting_Starting_Subtitle"]
        : DictaMeeting.App.Services.LocalizationManager.Instance["Meeting_Ready_Subtitle"];
    public bool HasCurrentMeeting => _meetingService.CurrentMeeting != null;
    public bool CanOpenMeetingFolder => _meetingService.CurrentMeeting != null;
    public bool CanExportMeeting => _meetingService.CurrentMeeting != null &&
                                    (CurrentState == MeetingState.Completed || IsOpenMeetingCompleted) &&
                                    !IsPostProcessing &&
                                    !IsRecording &&
                                    !IsProcessing;

    public Meeting? CurrentOpenMeeting => _meetingService.CurrentMeeting;

    public bool IsOpenMeetingCompleted =>
        _meetingService.CurrentMeeting != null &&
        (_meetingService.CurrentMeeting.State == MeetingState.Completed || CurrentState == MeetingState.Completed) &&
        !IsRecording && !IsProcessing;

    public bool CanEditMeetingTitle => (IsIdle || IsOpenMeetingCompleted) && !IsRecording && !IsProcessing;

    public string OpenMeetingSubtitle
    {
        get
        {
            var m = _meetingService.CurrentMeeting;
            if (m == null) return string.Empty;
            var loc = DictaMeeting.App.Services.LocalizationManager.Instance;

            var dateStr = m.Date.ToString("dd MMM yyyy", loc.CurrentUICulture);
            var startTimeStr = m.StartTime?.ToLocalTime().ToString("HH:mm") ?? m.Date.ToString("HH:mm");
            var endTimeStr = m.EndTime?.ToLocalTime().ToString("HH:mm") ?? "";
            var timeRange = string.IsNullOrEmpty(endTimeStr) ? startTimeStr : $"{startTimeStr}–{endTimeStr}";

            var partCount = Math.Max(m.Participants.Count, m.ExpectedParticipants?.Count ?? 0);
            var partStr = partCount == 1
                ? loc["Meeting_Participant_Single"]
                : string.Format(loc["Meeting_Participant_Multiple"], partCount);

            var langStr = SelectedLanguage?.DisplayName ?? "Español";

            var stateStr = IsOpenMeetingCompleted
                ? loc["Meeting_Status_Completed"]
                : (IsRecording ? loc["Meeting_Status_Recording"] : loc["Meeting_Status_Prepared"]);

            return $"{dateStr} · {timeRange} · {partStr} · {stateStr} · {langStr}";
        }
    }

    public string OpenMeetingAudioStatusText
    {
        get
        {
            var m = _meetingService.CurrentMeeting;
            if (m == null) return string.Empty;
            var loc = DictaMeeting.App.Services.LocalizationManager.Instance;
            if (IsPostProcessing) return loc["Meeting_Audio_Processing"];
            return m.AudioFileExists() ? loc["Meeting_Audio_Available"] : loc["Meeting_Audio_None"];
        }
    }

    public bool CanReprocessMeeting =>
        IsOpenMeetingCompleted &&
        _meetingService.CurrentMeeting != null &&
        _meetingService.CurrentMeeting.AudioFileExists() &&
        !IsProcessing && !IsRecording && !IsPostProcessing;

    #region Reproductor de Audio y Sincronización

    public bool HasPlayerAudio =>
        IsOpenMeetingCompleted &&
        _meetingService.CurrentMeeting != null &&
        _meetingService.CurrentMeeting.AudioFileExists() &&
        !IsProcessing && !IsRecording && !IsPostProcessing;

    public bool IsAudioPlaying => _audioPlayerService.IsPlaying;

    public TimeSpan AudioPosition => _audioPlayerService.Position;

    public TimeSpan AudioDuration => _audioPlayerService.Duration;

    public string AudioPositionText
    {
        get
        {
            var pos = _audioPlayerService.Position;
            var dur = _audioPlayerService.Duration;

            if (dur.TotalHours >= 1)
            {
                return $"{(int)pos.TotalHours:D2}:{pos.Minutes:D2}:{pos.Seconds:D2} / {(int)dur.TotalHours:D2}:{dur.Minutes:D2}:{dur.Seconds:D2}";
            }

            return $"{pos.Minutes:D2}:{pos.Seconds:D2} / {dur.Minutes:D2}:{dur.Seconds:D2}";
        }
    }

    [ObservableProperty]
    private double _audioSliderValue;

    public double AudioSliderMaximum => Math.Max(1.0, _audioPlayerService.Duration.TotalSeconds);

    public double AudioVolume
    {
        get => _audioPlayerService.Volume;
        set
        {
            if (Math.Abs(_audioPlayerService.Volume - value) > 0.001)
            {
                _audioPlayerService.Volume = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AudioVolumePercentText));
                OnPropertyChanged(nameof(AudioVolumeToolTip));
                OnPropertyChanged(nameof(IsAudioVolumeZeroOrMuted));
            }
        }
    }

    public bool IsAudioMuted
    {
        get => _audioPlayerService.IsMuted;
        set
        {
            if (_audioPlayerService.IsMuted != value)
            {
                _audioPlayerService.IsMuted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AudioVolumeToolTip));
                OnPropertyChanged(nameof(IsAudioVolumeZeroOrMuted));
            }
        }
    }

    public bool IsAudioVolumeZeroOrMuted => IsAudioMuted || AudioVolume <= 0.01;

    public string AudioVolumePercentText => $"{(int)(AudioVolume * 100)}%";

    public string AudioVolumeToolTip => IsAudioMuted
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Audio_Player_Volume_Muted_Tooltip"]
        : string.Format(DictaMeeting.App.Services.LocalizationManager.Instance["Audio_Player_Volume_Tooltip"], AudioVolumePercentText);

    public string AudioPlayPauseButtonToolTip => IsAudioPlaying
        ? DictaMeeting.App.Services.LocalizationManager.Instance["Audio_Player_Pause_Tooltip"]
        : DictaMeeting.App.Services.LocalizationManager.Instance["Audio_Player_Play_Tooltip"];

    public bool IsUserSeekingAudio
    {
        get => _isUserSeekingAudio;
        set => _isUserSeekingAudio = value;
    }

    public event EventHandler<TranscriptSegmentViewModel>? RequestScrollToSegment;

    [RelayCommand]
    public void TogglePlayPauseAudio()
    {
        if (!HasPlayerAudio) return;
        _audioPlayerService.TogglePlayPause();
    }

    [RelayCommand]
    public void PlayAudio()
    {
        if (!HasPlayerAudio) return;
        _audioPlayerService.Play();
    }

    [RelayCommand]
    public void PauseAudio()
    {
        _audioPlayerService.Pause();
    }

    [RelayCommand]
    public void SkipBackAudio()
    {
        if (!HasPlayerAudio) return;
        _audioPlayerService.SeekRelative(TimeSpan.FromSeconds(-5));
    }

    [RelayCommand]
    public void SkipForwardAudio()
    {
        if (!HasPlayerAudio) return;
        _audioPlayerService.SeekRelative(TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    public void ToggleMuteAudio()
    {
        IsAudioMuted = !IsAudioMuted;
    }

    public void OnAudioSliderSeek(double seconds)
    {
        if (!HasPlayerAudio) return;
        var target = TimeSpan.FromSeconds(seconds);
        _audioPlayerService.Seek(target);
        _transcriptAudioSyncService.UpdateActiveSegment(target);
        if (_transcriptAudioSyncService.ActiveSegment != null)
        {
            RequestScrollToSegment?.Invoke(this, _transcriptAudioSyncService.ActiveSegment);
        }
    }

    [RelayCommand]
    public void SeekToSegment(TranscriptSegmentViewModel? segment)
    {
        if (segment == null || !HasPlayerAudio) return;

        var pos = _transcriptAudioSyncService.GetPositionForSegment(segment);
        _suppressNextAutoScroll = true;
        _audioPlayerService.Seek(pos);
        _transcriptAudioSyncService.SetActiveSegment(segment);
    }

    public void OnTranscriptSegmentClicked(TranscriptSegmentViewModel segment)
    {
        SeekToSegment(segment);
    }

    private void OnAudioPlayerPositionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AudioPosition));
        OnPropertyChanged(nameof(AudioPositionText));

        if (!_isUserSeekingAudio)
        {
            AudioSliderValue = _audioPlayerService.Position.TotalSeconds;
        }

        bool changed = _transcriptAudioSyncService.UpdateActiveSegment(_audioPlayerService.Position);
        if (changed)
        {
            if (_suppressNextAutoScroll)
            {
                _suppressNextAutoScroll = false;
            }
            else if (_transcriptAudioSyncService.ActiveSegment != null)
            {
                RequestScrollToSegment?.Invoke(this, _transcriptAudioSyncService.ActiveSegment);
            }
        }
    }

    private void OnAudioPlayerPlaybackStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsAudioPlaying));
        OnPropertyChanged(nameof(AudioPlayPauseButtonToolTip));
    }

    private void OnAudioPlayerMediaOpened(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AudioDuration));
        OnPropertyChanged(nameof(AudioSliderMaximum));
        OnPropertyChanged(nameof(AudioPositionText));
    }

    private void OnAudioPlayerMediaEnded(object? sender, EventArgs e)
    {
        _transcriptAudioSyncService.UpdateActiveSegment(TimeSpan.Zero);
    }

    #endregion

    [ObservableProperty]
    private int _workspaceRightTab = 0; // 0 = Resumen, 1 = Acta

    [RelayCommand]
    private void SelectWorkspaceRightTab(object? param)
    {
        if (param is int idx) WorkspaceRightTab = idx;
        else if (param is string str && int.TryParse(str, out var parsed)) WorkspaceRightTab = parsed;
    }

    [RelayCommand]
    private void SelectWorkspaceRightTab0() => WorkspaceRightTab = 0;

    [RelayCommand]
    private void SelectWorkspaceRightTab1() => WorkspaceRightTab = 1;

    public MainViewModel(
        IMeetingService meetingService,
        IDocumentExporter documentExporter,
        IMeetingRepository repository,
        IAudioDeviceService audioDeviceService,
        IAudioCaptureService audioCaptureService,
        IModelManager modelManager,
        ITranscriptionService transcriptionService,
        ILiveTranscriptionPipeline livePipeline,
        ISpeakerDiarizationService diarizationService,
        ISpeakerTranscriptAligner speakerAligner,
        IAiActaService aiActaService,
        ISecureStorageService secureStorage,
        IUserSettingsService? userSettingsService = null,
        ILogger<MainViewModel>? logger = null,
        IMeetingProcessingService? meetingProcessingService = null,
        ILiveSummaryCoordinator? liveSummaryCoordinator = null,
        ILiveSummaryModelManager? liveSummaryModelManager = null,
        IVocabularyService? vocabularyService = null,
        IAudioPlayerService? audioPlayerService = null,
        ITranscriptAudioSyncService? transcriptAudioSyncService = null)
    {
        _meetingService = meetingService;
        _documentExporter = documentExporter;
        _repository = repository;
        _audioDeviceService = audioDeviceService;
        _audioCaptureService = audioCaptureService;
        _modelManager = modelManager;
        _transcriptionService = transcriptionService;
        _livePipeline = livePipeline;
        _diarizationService = diarizationService;
        _speakerAligner = speakerAligner;
        _aiActaService = aiActaService;
        _secureStorage = secureStorage;
        _userSettingsService = userSettingsService ?? new UserSettingsService();
        _logger = logger;
        _vocabularyService = vocabularyService ?? new VocabularyService();
        VocabularyManager = new VocabularyManagerViewModel(_vocabularyService);
        _vocabularyService.VocabularyChanged += (s, e) => OnPropertyChanged(nameof(VocabularyCount));

        _audioPlayerService = audioPlayerService ?? new AudioPlayerService();
        _transcriptAudioSyncService = transcriptAudioSyncService ?? new TranscriptAudioSyncService();

        _audioPlayerService.PositionChanged += OnAudioPlayerPositionChanged;
        _audioPlayerService.PlaybackStateChanged += OnAudioPlayerPlaybackStateChanged;
        _audioPlayerService.MediaOpened += OnAudioPlayerMediaOpened;
        _audioPlayerService.MediaEnded += OnAudioPlayerMediaEnded;

        _meetingProcessingService = meetingProcessingService ?? new DictaMeeting.App.Services.MeetingProcessingService(
            repository,
            transcriptionService,
            modelManager,
            diarizationService,
            speakerAligner);

        _liveSummaryModelManager = liveSummaryModelManager ?? new LocalLiveSummaryModelManager();
        _liveSummaryCoordinator = liveSummaryCoordinator ?? new LiveSummaryCoordinator(
            new LocalLlamaCppSummaryService(_liveSummaryModelManager, vocabularyService: _vocabularyService),
            _liveSummaryModelManager);

        _isLiveSummaryModelDownloaded = _liveSummaryModelManager.IsModelDownloaded();
        _liveSummaryCoordinator.SummaryUpdated += OnLiveSummaryUpdated;
        _liveSummaryCoordinator.GeneratingStateChanged += OnLiveSummaryGeneratingStateChanged;

        _meetingProcessingService.ProgressChanged += OnProcessingProgressChanged;
        _meetingProcessingService.ProcessingCompleted += OnMeetingProcessingCompleted;

        _durationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _durationTimer.Tick += (_, _) =>
        {
            if (IsRecording)
            {
                var elapsed = DateTimeOffset.Now - _meetingStartTime;
                DurationText = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                UpdateLiveSummaryRelativeTime();
            }
        };

        // Eventos del servicio de reunión
        _meetingService.StateChanged += OnMeetingStateChanged;
        _meetingService.SegmentAppended += OnSegmentAppended;
        _meetingService.SegmentUpdated += OnSegmentUpdated;
        _meetingService.SpeakerUpdated += OnSpeakerUpdated;
        _meetingService.SpeakerMerged += OnSpeakerMerged;

        // Eventos del servicio de captura de audio
        _audioCaptureService.AudioLevelUpdated += OnAudioLevelUpdated;
        _audioCaptureService.CaptureErrorOccurred += OnCaptureErrorOccurred;
        _audioCaptureService.MicrophoneMuteChanged += (s, muted) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                IsMicrophoneMuted = muted;
            });
        };
        _audioCaptureService.SystemAudioMuteChanged += (s, muted) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                IsSystemAudioMuted = muted;
            });
        };
        _audioCaptureService.SpeakingWhileMutedDetected += OnSpeakingWhileMutedDetected;
        _audioDeviceService.DevicesChanged += (_, _) =>
        {
            Application.Current?.Dispatcher.InvokeAsync(LoadAudioDevicesAsync);
        };

        // Notificaciones dinámicas de cambios en participantes
        Participants.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TotalParticipantsCount));
            OnPropertyChanged(nameof(ParticipantsSummaryText));
        };
        RealParticipants.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TotalParticipantsCount));
            OnPropertyChanged(nameof(ParticipantsSummaryText));
        };

        // Eventos del pipeline de transcripción en tiempo real
        _livePipeline.SegmentProduced += OnLiveSegmentProduced;

        InitializeModelsAndLanguages();

        // Carga inicial de dispositivos reales
        _ = LoadAudioDevicesAsync();
    }

    private void InitializeModelsAndLanguages()
    {
        // 1. Modelos para transcripción en vivo (streaming ligero)
        AvailableLiveModels.Add(new TranscriptionModelOption
        {
            IsAutomatic = true,
            DisplayName = "Automático (Rápido)",
            Description = "Detecta hardware y prioriza fluidez y baja latencia"
        });
        AvailableLiveModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.Qwen3_06B,
            DisplayName = "Qwen3-ASR 0.6B (~950 MB)",
            Description = "Rápido en CPU y adecuado para reuniones técnicas."
        });
        AvailableLiveModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.Qwen3_17B,
            DisplayName = "Qwen3-ASR 1.7B (~2.4 GB)",
            Description = "Máxima fidelidad para reuniones técnicas."
        });
        AvailableLiveModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.Tiny,
            DisplayName = "Tiny (~75 MB)",
            Description = "Ultra rápido para equipos con pocos recursos."
        });
        AvailableLiveModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.Base,
            DisplayName = "Base (~142 MB)",
            Description = "Equilibrio entre velocidad y precisión en CPU."
        });
        AvailableLiveModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.Small,
            DisplayName = "Small (~466 MB)",
            Description = "Alta precisión, especialmente en nombres propios."
        });
        AvailableLiveModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.Medium,
            DisplayName = "Medium (~1.5 GB)",
            Description = "Alta precisión para reuniones técnicas complejas."
        });
        AvailableLiveModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.LargeV3Turbo,
            DisplayName = "Large-v3 Turbo (~1.6 GB)",
            Description = "Máxima calidad de transcripción."
        });

        // 2. Modelos para transcripción definitiva (post-procesado de alta fidelidad)
        AvailableFinalModels.Add(new TranscriptionModelOption
        {
            IsAutomatic = true,
            DisplayName = "Automático (Alta Fidelidad)",
            Description = "Aprovecha al máximo el hardware para la máxima fidelidad"
        });
        AvailableFinalModels.Add(new TranscriptionModelOption
        {
            IsSameAsLive = true,
            DisplayName = "Mismo que en vivo (Sin recarga)",
            Description = "Reutiliza el modelo en vivo sin demora de recarga"
        });
        AvailableFinalModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.Qwen3_06B,
            DisplayName = "Qwen3-ASR 0.6B (~950 MB)",
            Description = "Rápido en CPU y adecuado para reuniones técnicas."
        });
        AvailableFinalModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.Qwen3_17B,
            DisplayName = "Qwen3-ASR 1.7B (~2.4 GB)",
            Description = "Máxima fidelidad para reuniones técnicas."
        });
        AvailableFinalModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.Tiny,
            DisplayName = "Tiny (~75 MB)",
            Description = "Ultra rápido para equipos con pocos recursos."
        });
        AvailableFinalModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.Base,
            DisplayName = "Base (~142 MB)",
            Description = "Equilibrio entre velocidad y precisión en CPU."
        });
        AvailableFinalModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.Small,
            DisplayName = "Small (~466 MB)",
            Description = "Alta precisión, especialmente en nombres propios."
        });
        AvailableFinalModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.Medium,
            DisplayName = "Medium (~1.5 GB)",
            Description = "Alta precisión para reuniones técnicas complejas."
        });
        AvailableFinalModels.Add(new TranscriptionModelOption
        {
            ModelSize = ModelSize.LargeV3Turbo,
            DisplayName = "Large-v3 Turbo (~1.6 GB)",
            Description = "Máxima calidad de transcripción."
        });

        _isLoadingSettings = true;
        try
        {
            // Colección retrocompatible AvailableModels
            foreach (var opt in AvailableLiveModels)
            {
                AvailableModels.Add(opt);
            }

            SelectedLiveModel = AvailableLiveModels[0];
            SelectedModel = SelectedLiveModel;
            SelectedFinalModel = AvailableFinalModels[0];

            // Opciones de Idiomas
            AvailableLanguages.Add(new LanguageOption
            {
                Mode = LanguageMode.Auto,
                DisplayName = "Auto (Español + Inglés)"
            });
            AvailableLanguages.Add(new LanguageOption
            {
                Mode = LanguageMode.Spanish,
                DisplayName = "Español"
            });
            AvailableLanguages.Add(new LanguageOption
            {
                Mode = LanguageMode.English,
                DisplayName = "English"
            });

            SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Mode == LanguageMode.Spanish) ?? AvailableLanguages[0];

            // Restaurar preferencias de modelos, idioma, audio y tema guardados
            var savedSettings = _userSettingsService.LoadSettings();

            CaptureMicrophone = savedSettings.CaptureMicrophone;
            _audioCaptureService.CaptureMicrophone = savedSettings.CaptureMicrophone;

            CaptureSystemAudio = savedSettings.CaptureSystemAudio;
            _audioCaptureService.CaptureSystemAudio = savedSettings.CaptureSystemAudio;

            if (!string.IsNullOrEmpty(savedSettings.SelectedLiveModel))
            {
                var matched = AvailableLiveModels.FirstOrDefault(m =>
                    (savedSettings.SelectedLiveModel == "Auto" && m.IsAutomatic) ||
                    m.ModelSize?.ToString() == savedSettings.SelectedLiveModel);
                if (matched != null)
                {
                    SelectedLiveModel = matched;
                    SelectedModel = matched;
                }
            }

            if (!string.IsNullOrEmpty(savedSettings.SelectedFinalModel))
            {
                var matched = AvailableFinalModels.FirstOrDefault(m =>
                    (savedSettings.SelectedFinalModel == "SameAsLive" && m.IsSameAsLive) ||
                    (savedSettings.SelectedFinalModel == "Auto" && m.IsAutomatic) ||
                    m.ModelSize?.ToString() == savedSettings.SelectedFinalModel);
                if (matched != null)
                {
                    SelectedFinalModel = matched;
                }
            }

            if (!string.IsNullOrEmpty(savedSettings.SelectedLanguage))
            {
                var matched = AvailableLanguages.FirstOrDefault(l => l.Mode.ToString() == savedSettings.SelectedLanguage);
                if (matched != null)
                {
                    SelectedLanguage = matched;
                }
            }

            if (savedSettings.IsDarkMode != IsDarkMode)
            {
                IsDarkMode = savedSettings.IsDarkMode;
            }

            if (!string.IsNullOrEmpty(savedSettings.UiLanguage))
            {
                UiLanguage = savedSettings.UiLanguage;
            }
            else
            {
                UiLanguage = UserSettingsService.DetectWindowsLanguage();
            }
            DictaMeeting.App.Services.LocalizationManager.Instance.SetLanguage(UiLanguage);

            // Cargar configuración de Servidor Personalizado
            CustomAiEndpoint = savedSettings.CustomAiEndpoint ?? string.Empty;
            CustomAiModel = savedSettings.CustomAiModel ?? string.Empty;
            SelectedAiConfigProvider = !string.IsNullOrWhiteSpace(savedSettings.SelectedAiConfigProvider)
                ? savedSettings.SelectedAiConfigProvider
                : "OpenRouter";

            // Cargar preferencias de Privacidad y Recordatorio de Grabación
            ShowRecordingNotice = savedSettings.ShowRecordingNotice;
            HasSeenRecordingNotice = savedSettings.HasSeenRecordingNotice;
            FirstRunModelsPromptCompleted = savedSettings.FirstRunModelsPromptCompleted;

            // Cargar ubicación de almacenamiento de modelos
            CurrentModelsDirectory = savedSettings.EffectiveModelsDirectory;
            IsCustomModelsDirectory = savedSettings.IsCustomModelsDirectory;
            ModelsDirectoryTypeBadge = IsCustomModelsDirectory ? "Ubicación personalizada" : "Ubicación predeterminada";

            if (!string.Equals(_modelManager.ModelsDirectory, CurrentModelsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                _modelManager.SetModelsDirectory(CurrentModelsDirectory);
                _liveSummaryModelManager.SetModelsDirectory(Path.Combine(CurrentModelsDirectory, "summary"));
                _diarizationService.SetModelsFolder(Path.Combine(CurrentModelsDirectory, "diarization"));
            }

            if (!ModelStorageManager.IsDirectoryAccessible(CurrentModelsDirectory))
            {
                IsModelsDirectoryAccessible = false;
                ModelsDirectoryWarningMessage = $"La ubicación de los modelos no está disponible ({CurrentModelsDirectory}). Conecte la unidad o seleccione otra ubicación.";
                _logger?.LogWarning("La ubicación configurada de modelos '{Path}' no está accesible.", CurrentModelsDirectory);
            }
            else
            {
                IsModelsDirectoryAccessible = true;
                ModelsDirectoryWarningMessage = string.Empty;
            }

            // Opciones de Modelos de IA (OpenRouter y Servidor Personalizado)
            AvailableAiModels.Clear();
            var aiModels = _aiActaService.GetRecommendedModels();
            foreach (var model in aiModels)
            {
                AvailableAiModels.Add(model);
            }

            if (!string.IsNullOrWhiteSpace(savedSettings.SelectedAiModelId))
            {
                SelectedAiModel = AvailableAiModels.FirstOrDefault(m => m.Id == savedSettings.SelectedAiModelId)
                                  ?? AvailableAiModels.FirstOrDefault();
            }
            else
            {
                SelectedAiModel = AvailableAiModels.FirstOrDefault();
            }

            // Opciones de Niveles de Detalle del Acta
            AvailableDetailLevels.Add(ActaDetailLevel.Breve);
            AvailableDetailLevels.Add(ActaDetailLevel.Normal);
            AvailableDetailLevels.Add(ActaDetailLevel.Detallada);
            AvailableDetailLevels.Add(ActaDetailLevel.Exhaustiva);
            SelectedDetailLevel = ActaDetailLevel.Normal;

            // Comprobar estado de API Key
            CheckConfiguredApiKey();
        }
        finally
        {
            _isLoadingSettings = false;
        }

        DictaMeeting.App.Services.LocalizationManager.Instance.CultureChanged += _ =>
        {
            OnPropertyChanged(nameof(SystemSummaryBadge));
            OnPropertyChanged(nameof(ThemeToggleToolTip));
            OnPropertyChanged(nameof(CurrentThemeName));
            OnPropertyChanged(nameof(StartButtonText));
            OnPropertyChanged(nameof(StartButtonToolTip));
            OnPropertyChanged(nameof(StopButtonText));
            OnPropertyChanged(nameof(StopButtonToolTip));
            OnPropertyChanged(nameof(PrepStatusTitle));
            OnPropertyChanged(nameof(PrepStatusSubtitle));
            OnPropertyChanged(nameof(OpenMeetingSubtitle));
            OnPropertyChanged(nameof(OpenMeetingAudioStatusText));
            OnPropertyChanged(nameof(AudioSourcesSummary));
            OnPropertyChanged(nameof(AudioSourcesToolTip));
            OnPropertyChanged(nameof(ModelsSummary));
            OnPropertyChanged(nameof(AudioVolumeToolTip));
            OnPropertyChanged(nameof(AudioPlayPauseButtonToolTip));
            OnPropertyChanged(nameof(MuteButtonToolTip));
            OnPropertyChanged(nameof(MicrophoneLiveLabelText));
            OnPropertyChanged(nameof(SystemAudioLiveLabelText));
            OnPropertyChanged(nameof(SystemAudioToolTip));
            OnPropertyChanged(nameof(TranscriptHeaderTitle));
            OnPropertyChanged(nameof(SummaryHeaderTitle));
            OnPropertyChanged(nameof(TranscriptHeaderSubtitle));
            CheckConfiguredApiKey();

            foreach (var item in SavedMeetings)
            {
                item.RefreshStatus();
            }
        };
    }

    public async Task CheckFirstRunExperienceAsync()
    {
        if (_hasCheckedFirstRun) return;
        _hasCheckedFirstRun = true;

        try
        {
            var available = _modelManager.GetAvailableModels();
            var hw = await _modelManager.DetectHardwareAsync();
            FirstRunRecommendedModel = hw.RecommendedLiveModel;
            var modelInfo = available.FirstOrDefault(m => m.Size == hw.RecommendedLiveModel);
            var modelName = modelInfo?.Name ?? hw.RecommendedLiveModel.ToString();
            var modelMb = modelInfo != null && modelInfo.FileSizeBytes > 0 ? $" (~{modelInfo.FileSizeBytes / 1_000_000} MB)" : "";
            FirstRunHardwareSummary = $"CPU con {hw.CpuLogicalCores} núcleos y {hw.TotalRamGb:F0} GB de RAM. Recomendado en vivo: {modelName}{modelMb} | Final: {hw.RecommendedFinalModel}.";

            // Comprobar preferencia persistida de primer arranque
            var savedSettings = _userSettingsService?.LoadSettings() ?? new UserSettings();
            FirstRunModelsPromptCompleted = savedSettings.FirstRunModelsPromptCompleted;

            if (FirstRunModelsPromptCompleted)
            {
                _logger?.LogInformation("El asistente de primer arranque ya fue completado previamente. No se muestra el modal.");
                return;
            }

            // Comprobar cuáles de los 3 modelos básicos recomendados están descargados
            RefreshFirstRunModelsStatus();

            int missingCount = FirstRunModels.Count(m => !m.IsDownloaded);

            // Si están los 3 descargados: marcar silenciosamente el primer arranque como completado
            if (missingCount == 0)
            {
                FirstRunModelsPromptCompleted = true;
                SaveCurrentUserSettings();
                _logger?.LogInformation("Primer arranque: Todos los modelos recomendados ya están instalados. Onboarding marcado silenciosamente como completado.");
                return;
            }

            // Si falta al menos uno: mostrar el modal de Modelos básicos
            UpdateFirstRunButtonText();
            IsFirstRunModelsModalOpen = true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudo completar la verificación del asistente de primer arranque.");
        }
    }

    public void RefreshFirstRunModelsStatus()
    {
        var available = _modelManager.GetAvailableModels();
        var qwen06Info = available.FirstOrDefault(m => m.Size == ModelSize.Qwen3_06B);
        var qwen17Info = available.FirstOrDefault(m => m.Size == ModelSize.Qwen3_17B);

        bool summaryDownloaded = _liveSummaryModelManager.IsModelDownloaded();
        bool qwen06Downloaded = qwen06Info?.IsDownloaded ?? false;
        bool qwen17Downloaded = qwen17Info?.IsDownloaded ?? false;

        if (FirstRunModels.Count == 0)
        {
            // 1. Qwen2.5-1.5B-Instruct (Resumen en vivo)
            FirstRunModels.Add(new FirstRunModelItemViewModel
            {
                Id = "summary_15b",
                ModelSize = null,
                Name = _liveSummaryModelManager.ModelName,
                Role = "Resumen en vivo",
                SizeBytes = _liveSummaryModelManager.ModelSizeBytes,
                FormattedSize = FormatBytes(_liveSummaryModelManager.ModelSizeBytes, includeTilde: true),
                IsDownloaded = summaryDownloaded,
                StatusText = summaryDownloaded ? "✓ Disponible" : "↓ Pendiente"
            });

            // 2. Qwen3-ASR 0.6B (Transcripción en vivo)
            FirstRunModels.Add(new FirstRunModelItemViewModel
            {
                Id = "asr_06b",
                ModelSize = ModelSize.Qwen3_06B,
                Name = qwen06Info?.Name ?? "Qwen3-ASR 0.6B",
                Role = "Transcripción en vivo",
                SizeBytes = qwen06Info?.FileSizeBytes ?? (950L * 1024 * 1024),
                FormattedSize = FormatBytes(qwen06Info?.FileSizeBytes ?? (950L * 1024 * 1024), includeTilde: true),
                IsDownloaded = qwen06Downloaded,
                StatusText = qwen06Downloaded ? "✓ Disponible" : "↓ Pendiente"
            });

            // 3. Qwen3-ASR 1.7B (Transcripción final)
            FirstRunModels.Add(new FirstRunModelItemViewModel
            {
                Id = "asr_17b",
                ModelSize = ModelSize.Qwen3_17B,
                Name = qwen17Info?.Name ?? "Qwen3-ASR 1.7B",
                Role = "Transcripción final",
                SizeBytes = qwen17Info?.FileSizeBytes ?? 2404222421L,
                FormattedSize = FormatBytes(qwen17Info?.FileSizeBytes ?? 2404222421L, includeTilde: true),
                IsDownloaded = qwen17Downloaded,
                StatusText = qwen17Downloaded ? "✓ Disponible" : "↓ Pendiente"
            });
        }
        else
        {
            var item0 = FirstRunModels.FirstOrDefault(m => m.Id == "summary_15b");
            if (item0 != null)
            {
                item0.IsDownloaded = summaryDownloaded;
                if (!item0.IsDownloading)
                {
                    item0.StatusText = summaryDownloaded ? "✓ Disponible" : "↓ Pendiente";
                }
            }

            var item1 = FirstRunModels.FirstOrDefault(m => m.Id == "asr_06b");
            if (item1 != null)
            {
                item1.IsDownloaded = qwen06Downloaded;
                if (!item1.IsDownloading)
                {
                    item1.StatusText = qwen06Downloaded ? "✓ Disponible" : "↓ Pendiente";
                }
            }

            var item2 = FirstRunModels.FirstOrDefault(m => m.Id == "asr_17b");
            if (item2 != null)
            {
                item2.IsDownloaded = qwen17Downloaded;
                if (!item2.IsDownloading)
                {
                    item2.StatusText = qwen17Downloaded ? "✓ Disponible" : "↓ Pendiente";
                }
            }
        }

        UpdateFirstRunButtonText();
    }

    private void UpdateFirstRunButtonText()
    {
        int missing = FirstRunModels.Count(m => !m.IsDownloaded);
        FirstRunMissingCount = missing;
        if (missing == 1)
        {
            FirstRunDownloadButtonText = "Descargar 1 modelo";
        }
        else if (missing > 1)
        {
            FirstRunDownloadButtonText = $"Descargar {missing} modelos";
        }
        else
        {
            FirstRunDownloadButtonText = "Descargar modelos";
        }
    }

    private (bool hasSpace, long freeBytes, long requiredBytes) CheckDiskSpace(long bytesRequired)
    {
        try
        {
            var targetDir = !string.IsNullOrWhiteSpace(CurrentModelsDirectory)
                ? CurrentModelsDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = System.IO.Path.GetPathRoot(targetDir);
            if (string.IsNullOrEmpty(root))
            {
                return (true, 0, bytesRequired);
            }

            var drive = new System.IO.DriveInfo(root);
            if (!drive.IsReady)
            {
                return (false, 0, bytesRequired);
            }

            long free = drive.AvailableFreeSpace;
            long safetyMargin = 200L * 1024 * 1024; // 200 MB de margen de seguridad
            bool ok = free >= (bytesRequired + safetyMargin);
            return (ok, free, bytesRequired);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudo verificar el espacio en disco con DriveInfo.");
            return (true, 0, bytesRequired);
        }
    }

    private static string FormatBytes(long bytes, bool includeTilde = false)
    {
        string prefix = includeTilde ? "~" : "";
        if (bytes >= 1024L * 1024 * 1024)
        {
            double gb = bytes / (1024.0 * 1024.0 * 1024.0);
            return $"{prefix}{gb.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} GB";
        }
        if (bytes >= 1024L * 1024)
        {
            double mb = bytes / (1024.0 * 1024.0);
            return $"{prefix}{mb.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} MB";
        }
        return $"{prefix}{(bytes / 1024.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture)} KB";
    }

    [RelayCommand]
    public async Task StartFirstRunDownloadsAsync()
    {
        if (IsFirstRunDownloading) return;

        if (!IsModelsDirectoryAccessible)
        {
            FirstRunHasError = true;
            FirstRunErrorMessage = $"La ubicación de los modelos no está disponible ({CurrentModelsDirectory}). Conecte la unidad o seleccione otra ubicación antes de continuar.";
            return;
        }

        RefreshFirstRunModelsStatus();
        var missingModels = FirstRunModels.Where(m => !m.IsDownloaded).ToList();
        if (missingModels.Count == 0)
        {
            FirstRunModelsPromptCompleted = true;
            SaveCurrentUserSettings();
            IsFirstRunModelsModalOpen = false;
            return;
        }

        long totalBytesRequired = missingModels.Sum(m => m.SizeBytes);
        var (hasSpace, freeBytes, reqBytes) = CheckDiskSpace(totalBytesRequired);
        if (!hasSpace)
        {
            FirstRunHasError = true;
            FirstRunErrorMessage = $"No hay suficiente espacio disponible en disco para descargar los modelos seleccionados.\nSe necesitan aproximadamente {FormatBytes(reqBytes)} y solo hay disponibles {FormatBytes(freeBytes)}.";
            return;
        }

        FirstRunHasError = false;
        FirstRunErrorMessage = null;
        IsFirstRunDownloading = true;
        FirstRunTotalProgress = 0.0;
        FirstRunTotalProgressPercentText = "0%";
        FirstRunStatusMessage = "Iniciando descarga de modelos recomendados...";

        foreach (var model in missingModels)
        {
            model.IsWaiting = true;
            model.StatusText = "Esperando...";
            model.DownloadProgress = 0.0;
            model.ProgressText = "0%";
        }

        _firstRunCts = new CancellationTokenSource();
        var ct = _firstRunCts.Token;

        long completedBytesPreviousModels = 0;
        try
        {
            foreach (var modelItem in missingModels)
            {
                ct.ThrowIfCancellationRequested();

                modelItem.IsWaiting = false;
                modelItem.IsDownloading = true;
                modelItem.StatusText = "Descargando...";
                FirstRunStatusMessage = $"Descargando {modelItem.Name}...";

                long currentModelSize = modelItem.SizeBytes;

                var progress = new Progress<double>(p =>
                {
                    void UpdateProgress()
                    {
                        modelItem.DownloadProgress = p;
                        int percent = Math.Clamp((int)(p * 100), 0, 100);
                        long currentDownloaded = (long)(p * currentModelSize);
                        modelItem.ProgressText = $"{percent}% ({FormatBytes(currentDownloaded)} / {FormatBytes(currentModelSize)})";

                        if (totalBytesRequired > 0)
                        {
                            long totalSoFar = completedBytesPreviousModels + currentDownloaded;
                            double overall = Math.Clamp((double)totalSoFar / totalBytesRequired, 0.0, 1.0);
                            FirstRunTotalProgress = overall;
                            FirstRunTotalProgressPercentText = $"{(int)(overall * 100)}%";
                        }
                    }

                    if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                    {
                        Application.Current.Dispatcher.InvokeAsync(UpdateProgress);
                    }
                    else
                    {
                        UpdateProgress();
                    }
                });

                if (modelItem.Id == "summary_15b")
                {
                    await _liveSummaryModelManager.EnsureModelDownloadedAsync(progress, ct);
                    IsLiveSummaryModelDownloaded = true;
                }
                else if (modelItem.ModelSize.HasValue)
                {
                    await _modelManager.EnsureModelDownloadedAsync(modelItem.ModelSize.Value, progress, ct);
                }

                modelItem.IsDownloading = false;
                modelItem.IsDownloaded = true;
                modelItem.DownloadProgress = 1.0;
                modelItem.StatusText = "✓ Descargado";
                modelItem.ProgressText = "100%";

                completedBytesPreviousModels += currentModelSize;
            }

            FirstRunTotalProgress = 1.0;
            FirstRunTotalProgressPercentText = "100%";
            FirstRunStatusMessage = "¡Todos los modelos recomendados se han descargado correctamente!";

            FirstRunModelsPromptCompleted = true;
            SaveCurrentUserSettings();

            RefreshModelCatalog();
            StatusMessage = "Modelos básicos listos para usar sin conexión.";

            await Task.Delay(1200);

            IsFirstRunModelsModalOpen = false;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Descarga de modelos básicos cancelada por el usuario.");
            FirstRunStatusMessage = "Descarga cancelada.";
            FirstRunModelsPromptCompleted = true;
            SaveCurrentUserSettings();
            RefreshFirstRunModelsStatus();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error durante la descarga de modelos básicos en el primer arranque.");
            FirstRunHasError = true;
            FirstRunErrorMessage = $"No se ha podido conectar con el servidor de descarga o se produjo un fallo:\n{ex.Message}\n\nPuedes volver a intentarlo ahora o descargar los modelos más tarde desde Ajustes.";
            RefreshFirstRunModelsStatus();
        }
        finally
        {
            IsFirstRunDownloading = false;
            _firstRunCts?.Dispose();
            _firstRunCts = null;
        }
    }

    [RelayCommand]
    public void CancelFirstRunDownloads()
    {
        if (_firstRunCts != null && !_firstRunCts.IsCancellationRequested)
        {
            _firstRunCts.Cancel();
        }
    }

    [RelayCommand]
    public void DismissFirstRunModal()
    {
        IsFirstRunModelsModalOpen = false;
        FirstRunModelsPromptCompleted = true;
        SaveCurrentUserSettings();
        _logger?.LogInformation("Usuario descartó el asistente de modelos básicos ('Ahora no'). Guardado FirstRunModelsPromptCompleted = true.");
    }

    [RelayCommand]
    public void CloseFirstRunModalAfterError()
    {
        IsFirstRunModelsModalOpen = false;
        FirstRunModelsPromptCompleted = true;
        SaveCurrentUserSettings();
    }

    [RelayCommand]
    private async Task DownloadInitialModelAsync()
    {
        await StartFirstRunDownloadsAsync();
    }

    [RelayCommand]
    private void DismissOnboarding()
    {
        DismissFirstRunModal();
    }

    [RelayCommand]
    public async Task RefreshAudioDevicesAsync()
    {
        await LoadAudioDevicesAsync();
    }

    public async Task LoadAudioDevicesAsync()
    {
        try
        {
            var saved = _userSettingsService.LoadSettings();

            // Preservar selección activa si sigue presente o cargar de ajustes
            var currentMicId = SelectedMicrophone?.Id ?? saved.SelectedMicrophoneId;
            var currentMicName = SelectedMicrophone?.Name ?? saved.SelectedMicrophoneName;

            var mics = await _audioDeviceService.GetCaptureDevicesAsync();
            AvailableMicrophones.Clear();
            foreach (var mic in mics)
            {
                AvailableMicrophones.Add(mic);
            }

            var currentDevId = SelectedSystemDevice?.Id ?? saved.SelectedSystemDeviceId;
            var currentDevName = SelectedSystemDevice?.Name ?? saved.SelectedSystemDeviceName;

            var renderers = await _audioDeviceService.GetRenderDevicesAsync();
            AvailableSystemDevices.Clear();
            foreach (var dev in renderers)
            {
                AvailableSystemDevices.Add(dev);
            }

            _isLoadingSettings = true;
            try
            {
                SelectedMicrophone = AvailableMicrophones.FirstOrDefault(m => m.Id == currentMicId)
                                    ?? AvailableMicrophones.FirstOrDefault(m => m.Name == currentMicName)
                                    ?? AvailableMicrophones.FirstOrDefault(m => m.IsDefault)
                                    ?? AvailableMicrophones.FirstOrDefault();

                SelectedSystemDevice = AvailableSystemDevices.FirstOrDefault(r => r.Id == currentDevId)
                                       ?? AvailableSystemDevices.FirstOrDefault(r => r.Name == currentDevName)
                                       ?? AvailableSystemDevices.FirstOrDefault(r => r.IsDefault)
                                       ?? AvailableSystemDevices.FirstOrDefault();

                _audioCaptureService.SelectedMicrophone = SelectedMicrophone;
                _audioCaptureService.SelectedSystemDevice = SelectedSystemDevice;
                _audioCaptureService.CaptureMicrophone = CaptureMicrophone;
                _audioCaptureService.CaptureSystemAudio = CaptureSystemAudio;
            }
            finally
            {
                _isLoadingSettings = false;
            }

            SaveCurrentUserSettings();

            OnPropertyChanged(nameof(MicrophoneSummary));
            OnPropertyChanged(nameof(SystemAudioSummary));
            OnPropertyChanged(nameof(AudioSourcesToolTip));
            OnPropertyChanged(nameof(LiveModelSummary));
            OnPropertyChanged(nameof(FinalModelSummary));
            OnPropertyChanged(nameof(LanguageSummary));
            OnPropertyChanged(nameof(AudioSourcesSummary));
            OnPropertyChanged(nameof(ModelsSummary));
            OnPropertyChanged(nameof(LanguageDisplayShort));
            OnPropertyChanged(nameof(ParticipantsSummaryText));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al refrescar dispositivos de audio en ViewModel.");
        }
    }

    partial void OnCaptureMicrophoneChanged(bool value)
    {
        _audioCaptureService.CaptureMicrophone = value;
        OnPropertyChanged(nameof(MicrophoneSummary));
        OnPropertyChanged(nameof(AudioSourcesSummary));
        OnPropertyChanged(nameof(AudioSourcesToolTip));
        OnPropertyChanged(nameof(CanMuteMicrophoneDuringRecording));
        SaveCurrentUserSettings();
    }

    partial void OnCaptureSystemAudioChanged(bool value)
    {
        _audioCaptureService.CaptureSystemAudio = value;
        OnPropertyChanged(nameof(SystemAudioSummary));
        OnPropertyChanged(nameof(AudioSourcesSummary));
        OnPropertyChanged(nameof(AudioSourcesToolTip));
        OnPropertyChanged(nameof(CanMuteSystemAudioDuringRecording));
        SaveCurrentUserSettings();
    }

    partial void OnSelectedMicrophoneChanged(AudioDevice? value)
    {
        _audioCaptureService.SelectedMicrophone = value;
        OnPropertyChanged(nameof(MicrophoneSummary));
        OnPropertyChanged(nameof(AudioSourcesSummary));
        OnPropertyChanged(nameof(AudioSourcesToolTip));
        SaveCurrentUserSettings();
    }

    partial void OnSelectedSystemDeviceChanged(AudioDevice? value)
    {
        _audioCaptureService.SelectedSystemDevice = value;
        OnPropertyChanged(nameof(SystemAudioSummary));
        OnPropertyChanged(nameof(AudioSourcesSummary));
        OnPropertyChanged(nameof(AudioSourcesToolTip));
        SaveCurrentUserSettings();
    }

    private DispatcherTimer? _speakingWhileMutedTimer;

    private void OnSpeakingWhileMutedDetected(object? sender, EventArgs e)
    {
        if (!IsRecording || !IsMicrophoneMuted) return;

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (!IsRecording || !IsMicrophoneMuted) return;

            IsSpeakingWhileMutedAlertVisible = true;

            if (_speakingWhileMutedTimer == null)
            {
                _speakingWhileMutedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                _speakingWhileMutedTimer.Tick += (_, _) =>
                {
                    IsSpeakingWhileMutedAlertVisible = false;
                    _speakingWhileMutedTimer.Stop();
                };
            }
            _speakingWhileMutedTimer.Stop();
            _speakingWhileMutedTimer.Start();
        });
    }

    partial void OnIsMicrophoneMutedChanged(bool value)
    {
        _audioCaptureService.IsMicrophoneMuted = value;
        if (value)
        {
            MicLevel = 0f;
        }
        else
        {
            IsSpeakingWhileMutedAlertVisible = false;
            _speakingWhileMutedTimer?.Stop();
        }
        OnPropertyChanged(nameof(MuteButtonText));
        OnPropertyChanged(nameof(MuteButtonToolTip));
        OnPropertyChanged(nameof(MicrophoneLiveLabelText));
    }

    [RelayCommand]
    private void ToggleMuteMicrophone()
    {
        if (!CaptureMicrophone) return;
        IsMicrophoneMuted = !IsMicrophoneMuted;
        if (IsMicrophoneMuted)
        {
            MicLevel = 0f;
            StatusMessage = "Micrófono silenciado: no se grabará tu voz hasta que lo reactives.";
        }
        else
        {
            IsSpeakingWhileMutedAlertVisible = false;
            _speakingWhileMutedTimer?.Stop();
            StatusMessage = "Micrófono reactivado: grabando audio del micrófono.";
        }
    }

    partial void OnIsSystemAudioMutedChanged(bool value)
    {
        _audioCaptureService.IsSystemAudioMuted = value;
        if (value)
        {
            SystemLevel = 0f;
        }
        OnPropertyChanged(nameof(SystemAudioLiveLabelText));
        OnPropertyChanged(nameof(SystemAudioToolTip));
    }

    [RelayCommand]
    private void ToggleMuteSystemAudio()
    {
        if (!CaptureSystemAudio) return;
        IsSystemAudioMuted = !IsSystemAudioMuted;
        if (IsSystemAudioMuted)
        {
            SystemLevel = 0f;
            StatusMessage = "Audio del sistema silenciado: no se grabará el sonido de los interlocutores remotos.";
        }
        else
        {
            StatusMessage = "Audio del sistema reactivado: grabando audio del sistema.";
        }
    }

    [RelayCommand]
    private async Task ToggleAudioMonitoringAsync()
    {
        if (IsRecording || IsStartingMeeting) return;

        if (IsMonitoringAudio)
        {
            await _audioCaptureService.StopMonitoringAsync();
            IsMonitoringAudio = false;
            StatusMessage = "Prueba de audio detenida.";
        }
        else
        {
            StatusMessage = "Iniciando prueba de audio (habla al micro o reproduce sonido)...";
            await _audioCaptureService.StartMonitoringAsync();
            IsMonitoringAudio = true;
            StatusMessage = "Prueba de audio activa: observa los vúmetros de nivel.";
        }
    }

    [RelayCommand]
    private async Task StartMeetingAsync()
    {
        if (IsStartingMeeting || (CurrentState != MeetingState.Idle && CurrentState != MeetingState.Completed)) return;

        // Comprobación de aviso previo a la grabación
        if (ShowRecordingNotice)
        {
            IsRecordingNoticeModalOpen = true;
            return;
        }

        await ExecuteStartMeetingCoreAsync();
    }

    [RelayCommand]
    private async Task ConfirmRecordingNoticeAndStartAsync()
    {
        IsRecordingNoticeModalOpen = false;
        // Al aceptar el mensaje, se desactiva el check de "mostrar recordatorio ..."
        ShowRecordingNotice = false;
        HasSeenRecordingNotice = true;
        SaveCurrentUserSettings();
        await ExecuteStartMeetingCoreAsync();
    }

    [RelayCommand]
    private void CancelRecordingNotice()
    {
        IsRecordingNoticeModalOpen = false;
    }

    private async Task ExecuteStartMeetingCoreAsync()
    {
        if (IsStartingMeeting || (CurrentState != MeetingState.Idle && CurrentState != MeetingState.Completed)) return;

        IsStartingMeeting = true;
        CurrentState = MeetingState.Preparing;
        StatusMessage = "Inicializando grabación y cargando modelos...";

        try
        {
            if (IsMonitoringAudio)
            {
                await _audioCaptureService.StopMonitoringAsync();
                IsMonitoringAudio = false;
            }

            Participants.Clear();
            TranscriptSegments.Clear();
            _diarizationService.Reset();
            _audioPlayerService.Stop();
            _audioPlayerService.Close();
            _transcriptAudioSyncService.Clear();
            DurationText = "00:00:00";
            _meetingStartTime = DateTimeOffset.Now;
            IsMicrophoneMuted = false;
            IsSystemAudioMuted = false;
            IsSpeakingWhileMutedAlertVisible = false;

            // Ceder control al Dispatcher de WPF para que dibuje de inmediato el estado "Iniciando..." y el spinner
            await Task.Yield();
            await Task.Delay(30);

            var meetingTitle = MeetingTitle;
            var organizer = Organizer;
            var company = Company;
            var selectedLanguageMode = SelectedLanguage?.Mode ?? LanguageMode.Auto;
            var realParticipantsList = RealParticipants.ToList();
            var liveOption = SelectedLiveModel ?? SelectedModel;

            ModelSize liveModelToUse = ModelSize.Qwen3_06B;

            // Ejecutar la inicialización pesada (WMI, carga de modelos C++/ONNX, Wasapi) fuera del hilo de la UI
            await Task.Run(async () =>
            {
                // 1. Determinar el modelo de transcripción a utilizar en vivo
                if (liveOption != null)
                {
                    if (liveOption.IsAutomatic)
                    {
                        var hw = await _modelManager.DetectHardwareAsync();
                        liveModelToUse = hw.RecommendedLiveModel;
                        _logger?.LogInformation("Modo automático en vivo: Hardware detectó CPU con {Cores} núcleos y {Ram} GB RAM. Modelo en vivo: {Model}", hw.CpuLogicalCores, hw.TotalRamGb, liveModelToUse);
                    }
                    else if (liveOption.ModelSize.HasValue)
                    {
                        liveModelToUse = liveOption.ModelSize.Value;
                    }
                }

                // 2. Descargar modelo en vivo si no está en caché
                var availableModels = _modelManager.GetAvailableModels();
                var modelInfo = availableModels.FirstOrDefault(m => m.Size == liveModelToUse);
                if (modelInfo != null && !modelInfo.IsDownloaded)
                {
                    UpdateStatus("Descargando modelo " + modelInfo.Name + "...");
                    var progress = new Progress<double>(p =>
                    {
                        if (Application.Current?.Dispatcher != null)
                        {
                            Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                ModelDownloadProgress = p;
                                ModelDownloadStatusText = $"Descargando modelo {modelInfo.Name}: {(int)(p * 100)}%";
                                StatusMessage = ModelDownloadStatusText;
                            });
                        }
                    });

                    await _modelManager.EnsureModelDownloadedAsync(liveModelToUse, progress);
                }

                // 3. Inicializar motor de transcripción en segundo plano (pesado en CPU/RAM)
                UpdateStatus($"Inicializando motor de transcripción en vivo ({liveModelToUse})...");
                _transcriptionService.CurrentLanguage = selectedLanguageMode;
                await _transcriptionService.InitializeAsync(liveModelToUse);

                // 4. Iniciar reunión y captura
                UpdateStatus("Iniciando captura y reunión...");
                await _meetingService.StartMeetingAsync(meetingTitle, organizer, company);

                var meetingDir = _repository.GetMeetingDirectoryPath(_meetingService.CurrentMeeting!);
                if (!System.IO.Directory.Exists(meetingDir))
                {
                    System.IO.Directory.CreateDirectory(meetingDir);
                }
                var audioPath = _repository.GetAudioFilePath(_meetingService.CurrentMeeting!);
                if (_meetingService.CurrentMeeting != null)
                {
                    _meetingService.CurrentMeeting.AudioFilePath = audioPath;
                    _meetingService.CurrentMeeting.Language = selectedLanguageMode.ToString();
                    foreach (var realName in realParticipantsList)
                    {
                        if (!_meetingService.CurrentMeeting.ExpectedParticipants.Contains(realName, StringComparer.OrdinalIgnoreCase))
                        {
                            _meetingService.CurrentMeeting.ExpectedParticipants.Add(realName);
                        }
                    }
                }

                await _audioCaptureService.StartCaptureAsync(audioPath);
                await _livePipeline.StartAsync();

                // 5. Iniciar coordinador de resumen en vivo local (llama.cpp)
                _liveSummaryCoordinator.Start(selectedLanguageMode.ToString());
            });

            LiveSummaryText = string.Empty;
            LiveSummaryCards.Clear();
            LiveSummaryLastUpdated = null;
            IsLiveSummaryModelDownloaded = _liveSummaryModelManager.IsModelDownloaded();
            UpdateLiveSummaryRelativeTime();

            _durationTimer.Start();
            OnPropertyChanged(nameof(HasCurrentMeeting));
            OnPropertyChanged(nameof(CanOpenMeetingFolder));
            OnPropertyChanged(nameof(CanExportMeeting));
            StatusMessage = $"Grabando reunión con modelo en vivo {liveModelToUse}...";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al iniciar la reunión.");
            CurrentState = MeetingState.Idle;
            StatusMessage = $"Error al iniciar reunión: {ex.Message}";
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(LocFmt("Msg_Meeting_Start_Error", ex.Message), Loc("Msg_Meeting_Start_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            else
            {
                MessageBox.Show(LocFmt("Msg_Meeting_Start_Error", ex.Message), Loc("Msg_Meeting_Start_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            IsStartingMeeting = false;
        }
    }

    private void UpdateStatus(string message)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.InvokeAsync(() => StatusMessage = message);
        }
        else
        {
            StatusMessage = message;
        }
    }

    [RelayCommand]
    private async Task StopMeetingAsync()
    {
        if (CurrentState != MeetingState.Recording || IsStoppingMeeting) return;

        IsStoppingMeeting = true;
        try
        {
            // Ceder control al Dispatcher de WPF para que dibuje de inmediato el estado "Deteniendo..." y el spinner
            await Task.Yield();

            IsMicrophoneMuted = false;
            IsSystemAudioMuted = false;
            IsSpeakingWhileMutedAlertVisible = false;
            _speakingWhileMutedTimer?.Stop();

            CommitAllSpeakerNames();

            _durationTimer.Stop();
            StatusMessage = "Deteniendo captura y consolidando grabación...";

            try
            {
                await _liveSummaryCoordinator.FinalizePendingSummaryAsync();
            }
            catch { }

            _liveSummaryCoordinator.Stop();
            await _livePipeline.StopAsync();
            await _audioCaptureService.StopCaptureAsync();

            var meeting = _meetingService.CurrentMeeting;
            if (meeting != null)
            {
                meeting.SummaryText = _liveSummaryCoordinator.CurrentSummary;
                meeting.SummarySegments = _liveSummaryCoordinator.SummaryCards.ToList();

                // Marcar de inmediato post-procesamiento para que no se muestre el reproductor prematuramente
                IsPostProcessing = true;
                PostProcessingProgress = 0.0;
                PostProcessingStatusText = "Iniciando procesamiento final...";
                CanCancelPostProcessing = true;

                // 1. Finalizar reunión y guardar inmediatamente en disco con transcripción provisional y audio
                await _meetingService.StopMeetingAsync();

                var sessionParams = new MeetingSessionParameters
                {
                    LiveModel = (SelectedLiveModel ?? SelectedModel)?.DisplayName,
                    FinalModel = SelectedFinalModel?.DisplayName,
                    MicrophoneDevice = SelectedMicrophone?.Name,
                    SystemAudioDevice = SelectedSystemDevice?.Name,
                    Language = SelectedLanguage?.DisplayName
                };

                meeting.Language = SelectedLanguage?.Mode.ToString() ?? "Spanish";
                meeting.ProcessingStatus = ProcessingStatus.Pending;
                meeting.ProcessingStatusText = "Pendiente de procesamiento final...";
                await _repository.SaveMeetingAsync(meeting, sessionParams);
                MeetingFolderPath = _repository.GetMeetingDirectoryPath(meeting);

                if (!SavedMeetings.Any(m => m.Id == meeting.Id))
                {
                    SavedMeetings.Insert(0, new MeetingHistoryItemViewModel(meeting));
                }

                OnPropertyChanged(nameof(HasCurrentMeeting));
                OnPropertyChanged(nameof(CanOpenMeetingFolder));
                OnPropertyChanged(nameof(CanExportMeeting));

                // 2. Determinar modelo definitivo (Two-Pass)
                string? finalModelToUse = null;
                var finalOption = SelectedFinalModel;
                if (finalOption != null)
                {
                    if (finalOption.IsSameAsLive)
                    {
                        finalModelToUse = _transcriptionService.CurrentModel.ToString();
                    }
                    else if (finalOption.IsAutomatic)
                    {
                        var hw = await _modelManager.DetectHardwareAsync();
                        var recFinal = hw.RecommendedFinalModel;
                        var availModels = _modelManager.GetAvailableModels();
                        var recModelInfo = availModels.FirstOrDefault(m => m.Size == recFinal);
                        if (recModelInfo != null && recModelInfo.IsDownloaded)
                        {
                            finalModelToUse = recFinal.ToString();
                        }
                        else
                        {
                            // Si el modelo recomendado no está disponible (p. ej. el usuario lo eliminó deliberadamente),
                            // reutilizar el modelo en vivo o un modelo ya descargado para no forzar descargas inesperadas
                            var downloadedFallback = availModels.FirstOrDefault(m => m.IsDownloaded && m.Size == ModelSize.Qwen3_06B)
                                                    ?? availModels.FirstOrDefault(m => m.IsDownloaded);
                            finalModelToUse = downloadedFallback != null
                                ? downloadedFallback.Size.ToString()
                                : _transcriptionService.CurrentModel.ToString();
                        }
                    }
                    else if (finalOption.ModelSize.HasValue)
                    {
                        finalModelToUse = finalOption.ModelSize.Value.ToString();
                    }
                }

                // 3. Lanzar procesamiento final como tarea independiente en segundo plano
                StatusMessage = $"Reunión guardada en: {MeetingFolderPath}. Procesamiento final iniciado en segundo plano...";
                var bgTask = _meetingProcessingService.EnqueueOrProcessAsync(meeting.Id, finalModelToUse);
                CurrentProcessingTask = bgTask;
            }
        }
        finally
        {
            IsStoppingMeeting = false;
        }
    }

    [RelayCommand]
    private async Task CancelPostProcessingAsync()
    {
        if (_meetingProcessingService.IsProcessing && !string.IsNullOrEmpty(_meetingProcessingService.CurrentMeetingId))
        {
            await _meetingProcessingService.CancelProcessingAsync(_meetingProcessingService.CurrentMeetingId);
            StatusMessage = "Cancelando procesamiento en curso...";
        }
    }

    [RelayCommand]
    private Task ReprocessHistoricalMeetingAsync(string? modelName)
    {
        var meeting = _meetingService.CurrentMeeting ?? SelectedHistoricalMeeting?.Meeting;
        if (meeting == null) return Task.CompletedTask;

        _audioPlayerService.Stop();
        _audioPlayerService.Close();
        _transcriptAudioSyncService.Clear();

        if (string.IsNullOrEmpty(meeting.AudioFilePath) || !System.IO.File.Exists(meeting.AudioFilePath))
        {
            MessageBox.Show(Loc("Msg_Meeting_Reprocess_NoAudio"),
                Loc("Msg_Meeting_Reprocess_NoAudio_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return Task.CompletedTask;
        }

        var targetModel = !string.IsNullOrWhiteSpace(modelName)
            ? modelName
            : (SelectedFinalModel?.ModelSize?.ToString() ?? "Qwen3_06B");

        if (_meetingService.CurrentMeeting?.Id == meeting.Id)
        {
            IsPostProcessing = true;
            PostProcessingProgress = 0.0;
            PostProcessingStatusText = $"Reprocesando con modelo {targetModel}...";
            CanCancelPostProcessing = true;
        }

        StatusMessage = $"Reprocesando reunión '{meeting.Title}' con modelo {targetModel}...";
        var task = _meetingProcessingService.EnqueueOrProcessAsync(meeting.Id, targetModel);
        CurrentProcessingTask = task;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RetryHistoricalMeetingProcessingAsync()
    {
        var meeting = _meetingService.CurrentMeeting ?? SelectedHistoricalMeeting?.Meeting;
        if (meeting == null) return;
        await ReprocessHistoricalMeetingAsync(meeting.ProcessingModel);
    }

    private Task? _completionHandlingTask;

    public async Task WaitForCurrentProcessingAsync()
    {
        if (CurrentProcessingTask != null)
        {
            await CurrentProcessingTask;
        }

        if (_completionHandlingTask != null)
        {
            await _completionHandlingTask;
        }
    }

    private void OnProcessingProgressChanged(object? sender, MeetingProcessingProgressEventArgs e)
    {
        void Update()
        {
            if (_meetingService.CurrentMeeting?.Id == e.MeetingId)
            {
                IsPostProcessing = _meetingProcessingService.IsProcessing;
                CanCancelPostProcessing = _meetingProcessingService.IsProcessing && e.Progress < 1.0;
                PostProcessingProgress = e.Progress;
                PostProcessingStatusText = e.StatusText;
            }

            var historyItem = SavedMeetings.FirstOrDefault(m => m.Id == e.MeetingId);
            if (historyItem != null)
            {
                historyItem.Meeting.ProcessingProgress = e.Progress;
                historyItem.Meeting.ProcessingStatusText = e.StatusText;
                historyItem.RefreshStatus();
            }
        }

        if (Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            Application.Current.Dispatcher.InvokeAsync(Update);
        }
        else
        {
            Update();
        }
    }

    private void OnMeetingProcessingCompleted(object? sender, MeetingProcessingCompletedEventArgs e)
    {
        async Task HandleCompletedAsync()
        {
            var isCurrentMeeting = _meetingService.CurrentMeeting?.Id == e.MeetingId;

            if (e.Status == ProcessingStatus.Completed)
            {
                StatusMessage = "Procesamiento finalizado con éxito.";

                if (isCurrentMeeting)
                {
                    PostProcessingProgress = 1.0;
                    PostProcessingStatusText = "Procesamiento completado con éxito.";
                    CanCancelPostProcessing = false;

                    var updated = await _repository.GetMeetingAsync(e.MeetingId);
                    if (updated != null)
                    {
                        if (_meetingService.CurrentMeeting != null && !ReferenceEquals(_meetingService.CurrentMeeting, updated))
                        {
                            _meetingService.CurrentMeeting.Transcript.Clear();
                            _meetingService.CurrentMeeting.Transcript.AddRange(updated.Transcript);
                            _meetingService.CurrentMeeting.Participants.Clear();
                            _meetingService.CurrentMeeting.Participants.AddRange(updated.Participants);
                            _meetingService.CurrentMeeting.ProcessingStatus = updated.ProcessingStatus;
                            _meetingService.CurrentMeeting.ProcessingProgress = 1.0;
                            _meetingService.CurrentMeeting.ProcessingStatusText = updated.ProcessingStatusText;
                        }

                        var activeSpeakerIds = new HashSet<string>(updated.Transcript.Select(s => s.SpeakerId), StringComparer.OrdinalIgnoreCase);
                        var toRemove = Participants.Where(p => !activeSpeakerIds.Contains(p.Id)).ToList();
                        foreach (var r in toRemove)
                        {
                            Participants.Remove(r);
                        }

                        foreach (var part in updated.Participants)
                        {
                            EnsureSpeakerExists(part.Id, part.DisplayName);
                        }

                        foreach (var spVm in Participants)
                        {
                            var spSegments = updated.Transcript.Where(s => string.Equals(s.SpeakerId, spVm.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                            spVm.UtteranceCount = spSegments.Count;
                            spVm.TotalSpeakingDuration = TimeSpan.FromSeconds(spSegments.Sum(s => s.Duration.TotalSeconds));
                            spVm.RefreshAvailableOptions();
                        }

                        TranscriptSegments.Clear();
                        foreach (var seg in updated.Transcript)
                        {
                            var spVm = Participants.FirstOrDefault(p => p.Id == seg.SpeakerId);
                            TranscriptSegments.Add(new TranscriptSegmentViewModel
                            {
                                Id = seg.Id,
                                FormattedTime = seg.FormattedStartTime,
                                StartTime = seg.StartTime,
                                EndTime = seg.EndTime > seg.StartTime ? seg.EndTime : seg.StartTime + TimeSpan.FromSeconds(3),
                                SpeakerId = seg.SpeakerId,
                                SpeakerDisplayName = !string.IsNullOrWhiteSpace(spVm?.DisplayName) ? spVm.DisplayName : seg.SpeakerDisplayName,
                                Text = seg.Text,
                                ColorHex = spVm?.ColorHex ?? "#6366F1"
                            });
                        }
                        TranscriptSegmentViewModel.UpdateSpeakerInterventionHeaders(TranscriptSegments);
                        if (isCurrentMeeting && updated.AudioFileExists())
                        {
                            _audioPlayerService.Load(updated.AudioFilePath, updated.Duration);
                            _transcriptAudioSyncService.SetSegments(TranscriptSegments);
                        }
                        RefreshWorkspaceState();
                    }
                }
            }
            else if (e.Status == ProcessingStatus.Cancelled)
            {
                StatusMessage = "Procesamiento final cancelado.";
                if (isCurrentMeeting)
                {
                    PostProcessingStatusText = "Procesamiento cancelado.";
                    CanCancelPostProcessing = false;
                }
            }
            else if (e.Status == ProcessingStatus.Error)
            {
                StatusMessage = $"Error en procesamiento: {e.ErrorMessage}";
                if (isCurrentMeeting)
                {
                    PostProcessingStatusText = $"Error: {e.ErrorMessage}";
                    CanCancelPostProcessing = false;
                }
            }

            // Actualizar elemento en el historial
            var item = SavedMeetings.FirstOrDefault(m => m.Id == e.MeetingId);
            var updatedMeeting = await _repository.GetMeetingAsync(e.MeetingId);
            if (updatedMeeting != null)
            {
                if (item != null)
                {
                    var idx = SavedMeetings.IndexOf(item);
                    if (idx >= 0)
                    {
                        SavedMeetings[idx] = new MeetingHistoryItemViewModel(updatedMeeting);
                        if (SelectedHistoricalMeeting?.Id == e.MeetingId)
                        {
                            SelectedHistoricalMeeting = SavedMeetings[idx];
                        }
                    }
                }
                else
                {
                    var newItem = new MeetingHistoryItemViewModel(updatedMeeting);
                    SavedMeetings.Insert(0, newItem);
                    if (SelectedHistoricalMeeting == null)
                    {
                        SelectedHistoricalMeeting = newItem;
                    }
                }
            }

            OnPropertyChanged(nameof(TranscriptHeaderTitle));
            OnPropertyChanged(nameof(SummaryHeaderTitle));
            OnPropertyChanged(nameof(TranscriptHeaderSubtitle));
            OnPropertyChanged(nameof(CanExportMeeting));

            // Ocultar banner de progreso de forma diferida para permitir confirmación visual
            if (isCurrentMeeting)
            {
                var delayMs = Application.Current != null
                    ? (e.Status == ProcessingStatus.Completed ? 2000 : (e.Status == ProcessingStatus.Cancelled ? 1200 : 3500))
                    : 0;
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }
                if (!_meetingProcessingService.IsProcessing || _meetingProcessingService.CurrentMeetingId != e.MeetingId)
                {
                    IsPostProcessing = false;
                }

                // Cargar el reproductor y actualizar sincronización y estado si la reunión actual tiene audio grabado
                var currentMeeting = _meetingService.CurrentMeeting;
                if (currentMeeting != null && currentMeeting.AudioFileExists())
                {
                    if (_audioPlayerService.Duration == TimeSpan.Zero)
                    {
                        _audioPlayerService.Load(currentMeeting.AudioFilePath, currentMeeting.Duration);
                    }
                    _transcriptAudioSyncService.SetSegments(TranscriptSegments);
                }

                RefreshWorkspaceState();
            }
            else if (!_meetingProcessingService.IsProcessing)
            {
                IsPostProcessing = false;
                RefreshWorkspaceState();
            }
        }

        if (Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            var op = Application.Current.Dispatcher.InvokeAsync(HandleCompletedAsync);
            _completionHandlingTask = op.Task.Unwrap();
        }
        else
        {
            _completionHandlingTask = HandleCompletedAsync();
        }
    }

    [RelayCommand]
    private void OpenMeetingFolder()
    {
        var meeting = _meetingService.CurrentMeeting;
        if (meeting != null)
        {
            var dir = _repository.GetMeetingDirectoryPath(meeting);
            if (System.IO.Directory.Exists(dir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
                return;
            }
        }

        // Si la reunión aún no ha comenzado o su carpeta en disco no existe todavía, abrir la carpeta base de reuniones
        var baseDir = (_repository as DictaMeeting.Infrastructure.Persistence.LocalFileMeetingRepository)?.BaseDirectory
            ?? DictaMeeting.Infrastructure.Persistence.LocalFileMeetingRepository.ResolveDefaultMeetingsDirectory();

        if (System.IO.Directory.Exists(baseDir))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = baseDir,
                UseShellExecute = true
            });
        }
        else
        {
            if (meeting != null)
            {
                var dir = _repository.GetMeetingDirectoryPath(meeting);
                MessageBox.Show(LocFmt("Msg_Meeting_Folder_NotExists", dir), Loc("Common_Folder_NotFound_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(LocFmt("Common_Folder_NotFound_Msg", baseDir), Loc("Common_Folder_NotFound_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    [RelayCommand]
    private void ExportMarkdown()
    {
        CommitAllSpeakerNames();
        var meeting = _meetingService.CurrentMeeting;
        if (meeting == null) return;

        var baseName = _repository.GetMeetingFileBaseName(meeting);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{baseName}_transcript.md",
            Filter = "Documento Markdown (*.md)|*.md|Todos los archivos (*.*)|*.*",
            Title = "Exportar Transcripción en Markdown"
        };

        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, _documentExporter.ExportToMarkdown(meeting));
            StatusMessage = $"Documento Markdown exportado a: {dialog.FileName}";
        }
    }

    [RelayCommand]
    private void ExportPlainText()
    {
        CommitAllSpeakerNames();
        var meeting = _meetingService.CurrentMeeting;
        if (meeting == null) return;

        var baseName = _repository.GetMeetingFileBaseName(meeting);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{baseName}_transcript.txt",
            Filter = "Texto Plano (*.txt)|*.txt|Todos los archivos (*.*)|*.*",
            Title = "Exportar Transcripción en Texto Plano"
        };

        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, _documentExporter.ExportToPlainText(meeting));
            StatusMessage = $"Transcripción en texto plano exportada a: {dialog.FileName}";
        }
    }

    [RelayCommand]
    private void ExportJson()
    {
        CommitAllSpeakerNames();
        var meeting = _meetingService.CurrentMeeting;
        if (meeting == null) return;

        var baseName = _repository.GetMeetingFileBaseName(meeting);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{baseName}_meeting.json",
            Filter = "JSON Estructurado (*.json)|*.json|Todos los archivos (*.*)|*.*",
            Title = "Exportar Sesión en JSON"
        };

        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, _documentExporter.ExportToJson(meeting));
            StatusMessage = $"JSON de reunión exportado a: {dialog.FileName}";
        }
    }

    public void CheckConfiguredApiKey()
    {
        var loc = DictaMeeting.App.Services.LocalizationManager.Instance;
        var openRouterKey = _secureStorage.GetSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey);
        HasConfiguredApiKey = !string.IsNullOrWhiteSpace(openRouterKey);
        ApiKeyStatusText = HasConfiguredApiKey
            ? loc["AiConfig_KeySaved_DPAPI"]
            : loc["AiConfig_KeyNotConfigured_OpenRouter"];

        var customKey = _secureStorage.GetSecret(OpenAiCompatibleActaService.CustomServerApiKeyStorageKey);
        HasConfiguredCustomServerApiKey = !string.IsNullOrWhiteSpace(customKey);
        CustomServerApiKeyStatusText = HasConfiguredCustomServerApiKey
            ? loc["AiConfig_KeySaved_DPAPI"]
            : loc["AiConfig_KeyNotConfigured_Custom"];
    }

    partial void OnSelectedAiModelChanged(AiModelOption? value)
    {
        IsCustomModelSelected = value?.Id == "custom";
        SaveCurrentUserSettings();
    }

    [RelayCommand]
    private void OpenActaDialog()
    {
        _actaTargetMeeting = _meetingService.CurrentMeeting;
        CheckConfiguredApiKey();
        if (_actaTargetMeeting != null && !string.IsNullOrWhiteSpace(_actaTargetMeeting.ActaMarkdown))
        {
            GeneratedActaMarkdown = _actaTargetMeeting.ActaMarkdown;
            HasGeneratedActa = true;
        }
        else
        {
            GeneratedActaMarkdown = string.Empty;
            HasGeneratedActa = false;
        }
        ActaErrorMessage = string.Empty;
        IsActaModalOpen = true;
    }

    [RelayCommand]
    private void OpenHistoricalActaDialog()
    {
        if (SelectedHistoricalMeeting == null) return;
        _actaTargetMeeting = SelectedHistoricalMeeting.Meeting;
        CheckConfiguredApiKey();
        if (_actaTargetMeeting != null && !string.IsNullOrWhiteSpace(_actaTargetMeeting.ActaMarkdown))
        {
            GeneratedActaMarkdown = _actaTargetMeeting.ActaMarkdown;
            HasGeneratedActa = true;
        }
        else
        {
            GeneratedActaMarkdown = string.Empty;
            HasGeneratedActa = false;
        }
        ActaErrorMessage = string.Empty;
        IsActaModalOpen = true;
    }

    [RelayCommand]
    private void CloseActaDialog()
    {
        IsActaModalOpen = false;
    }

    [RelayCommand]
    private void OpenAiConfig()
    {
        CheckConfiguredApiKey();
        ApiKeyInput = string.Empty;
        CustomServerApiKeyInput = string.Empty;
        OpenRouterConnectionTestResult = string.Empty;
        IsOpenRouterTestSuccess = null;
        CustomServerConnectionTestResult = string.Empty;
        IsCustomServerTestSuccess = null;
        IsAiConfigModalOpen = true;
    }

    [RelayCommand]
    private void CloseAiConfig()
    {
        IsAiConfigModalOpen = false;
        if (IsOpenedFromGlobalSettings)
        {
            IsOpenedFromGlobalSettings = false;
            IsGlobalSettingsModalOpen = true;
        }
    }

    [RelayCommand]
    private void SaveApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            MessageBox.Show(Loc("Msg_OpenRouter_Empty"), Loc("Msg_OpenRouter_Empty_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _secureStorage.SaveSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey, ApiKeyInput.Trim());
        CheckConfiguredApiKey();
        ApiKeyInput = string.Empty;
        StatusMessage = "Clave de OpenRouter guardada de forma segura con Windows DPAPI.";
        MessageBox.Show(Loc("Msg_OpenRouter_Saved"), Loc("Msg_OpenRouter_Saved_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void DeleteApiKey()
    {
        var result = MessageBox.Show(
            Loc("Msg_OpenRouter_Delete_Confirm"),
            Loc("Msg_OpenRouter_Delete_Confirm_Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _secureStorage.DeleteSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey);
            CheckConfiguredApiKey();
            StatusMessage = "Clave de OpenRouter eliminada del almacenamiento seguro.";
        }
    }

    [RelayCommand]
    private async Task TestOpenRouterConnectionAsync()
    {
        IsOpenRouterTestingConnection = true;
        OpenRouterConnectionTestResult = "Probando conexión con OpenRouter...";
        IsOpenRouterTestSuccess = null;

        try
        {
            var key = !string.IsNullOrWhiteSpace(ApiKeyInput)
                ? ApiKeyInput.Trim()
                : _secureStorage.GetSecret(OpenRouterActaService.OpenRouterApiKeyStorageKey)?.Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                OpenRouterConnectionTestResult = "✕ No se pudo conectar: Debe introducir o guardar una API Key para probar la conexión.";
                IsOpenRouterTestSuccess = false;
                return;
            }

            ConnectionTestResult result;
            if (_aiActaService is CompositeAiActaService composite)
            {
                var provider = composite.Factory.GetProvider(OpenRouterActaService.ProviderIdentifier) as OpenRouterActaService;
                result = provider != null
                    ? await provider.TestConnectionAsync(key)
                    : ConnectionTestResult.Failed("Proveedor de OpenRouter no disponible.");
            }
            else if (_aiActaService is OpenRouterActaService openRouter)
            {
                result = await openRouter.TestConnectionAsync(key);
            }
            else
            {
                result = ConnectionTestResult.Failed("Servicio de OpenRouter no disponible.");
            }

            OpenRouterConnectionTestResult = result.Message;
            IsOpenRouterTestSuccess = result.Success;
        }
        catch (Exception ex)
        {
            OpenRouterConnectionTestResult = $"✕ No se pudo conectar: {ex.Message}";
            IsOpenRouterTestSuccess = false;
        }
        finally
        {
            IsOpenRouterTestingConnection = false;
        }
    }

    [RelayCommand]
    private void SaveCustomServerConfig()
    {
        if (string.IsNullOrWhiteSpace(CustomAiEndpoint))
        {
            MessageBox.Show(Loc("Msg_CustomServer_Endpoint_Required"), Loc("Msg_CustomServer_Endpoint_Required_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(CustomAiModel))
        {
            MessageBox.Show(Loc("Msg_CustomServer_Model_Required"), Loc("Msg_CustomServer_Model_Required_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(CustomServerApiKeyInput))
        {
            _secureStorage.SaveSecret(OpenAiCompatibleActaService.CustomServerApiKeyStorageKey, CustomServerApiKeyInput.Trim());
            CustomServerApiKeyInput = string.Empty;
        }

        SaveCurrentUserSettings();
        CheckConfiguredApiKey();
        StatusMessage = "Configuración del Servidor personalizado guardada de forma segura con Windows DPAPI.";
        MessageBox.Show(Loc("Msg_CustomServer_Saved"), Loc("Msg_CustomServer_Saved_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void DeleteCustomServerApiKey()
    {
        var result = MessageBox.Show(
            Loc("Msg_CustomServer_Delete_Confirm"),
            Loc("Msg_CustomServer_Delete_Confirm_Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _secureStorage.DeleteSecret(OpenAiCompatibleActaService.CustomServerApiKeyStorageKey);
            CheckConfiguredApiKey();
            StatusMessage = "Clave del Servidor personalizado eliminada del almacenamiento seguro.";
        }
    }

    [RelayCommand]
    private async Task TestCustomServerConnectionAsync()
    {
        IsCustomServerTestingConnection = true;
        CustomServerConnectionTestResult = "Probando conexión con el servidor...";
        IsCustomServerTestSuccess = null;

        try
        {
            var ep = CustomAiEndpoint?.Trim();
            var mdl = CustomAiModel?.Trim();
            var key = !string.IsNullOrWhiteSpace(CustomServerApiKeyInput)
                ? CustomServerApiKeyInput.Trim()
                : _secureStorage.GetSecret(OpenAiCompatibleActaService.CustomServerApiKeyStorageKey)?.Trim();

            if (string.IsNullOrWhiteSpace(ep))
            {
                CustomServerConnectionTestResult = "✕ No se pudo conectar: Debe indicar el Endpoint para probar la conexión.";
                IsCustomServerTestSuccess = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(mdl))
            {
                CustomServerConnectionTestResult = "✕ No se pudo conectar: Debe indicar el Modelo para probar la conexión.";
                IsCustomServerTestSuccess = false;
                return;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                CustomServerConnectionTestResult = "✕ No se pudo conectar: Debe introducir o guardar una API Key para probar la conexión.";
                IsCustomServerTestSuccess = false;
                return;
            }

            ConnectionTestResult result;
            if (_aiActaService is CompositeAiActaService composite)
            {
                var provider = composite.Factory.GetProvider(OpenAiCompatibleActaService.ProviderIdentifier) as OpenAiCompatibleActaService;
                result = provider != null
                    ? await provider.TestConnectionAsync(ep, mdl, key)
                    : ConnectionTestResult.Failed("Proveedor de Servidor personalizado no disponible.");
            }
            else
            {
                var tempService = new OpenAiCompatibleActaService(new HttpClient(), _secureStorage, _userSettingsService, _vocabularyService);
                result = await tempService.TestConnectionAsync(ep, mdl, key);
            }

            CustomServerConnectionTestResult = result.Message;
            IsCustomServerTestSuccess = result.Success;
        }
        catch (Exception ex)
        {
            CustomServerConnectionTestResult = $"✕ No se pudo conectar: {ex.Message}";
            IsCustomServerTestSuccess = false;
        }
        finally
        {
            IsCustomServerTestingConnection = false;
        }
    }

    [RelayCommand]
    private async Task GenerateActaAsync()
    {
        var meeting = _actaTargetMeeting ?? _meetingService.CurrentMeeting ?? SelectedHistoricalMeeting?.Meeting;
        if (meeting == null)
        {
            ActaErrorMessage = "No hay ninguna reunión activa o seleccionada.";
            return;
        }

        if (meeting.Transcript.Count == 0)
        {
            ActaErrorMessage = "La reunión no tiene transcripción disponible para redactar el acta.";
            return;
        }

        var isCustomServer = SelectedAiModel?.Id == OpenAiCompatibleActaService.CustomServerModelId;

        if (isCustomServer)
        {
            CheckConfiguredApiKey();
            var ep = CustomAiEndpoint?.Trim();
            var mdl = CustomAiModel?.Trim();
            if (string.IsNullOrWhiteSpace(ep) || string.IsNullOrWhiteSpace(mdl) || !HasConfiguredCustomServerApiKey)
            {
                ActaErrorMessage = "Debe configurar el Servidor personalizado (Endpoint, Modelo y API Key) en Ajustes antes de generar el acta.";
                SelectedAiConfigProvider = "CustomServer";
                OpenAiConfig();
                return;
            }
        }
        else
        {
            CheckConfiguredApiKey();
            if (!HasConfiguredApiKey)
            {
                ActaErrorMessage = "Debe configurar su API Key de OpenRouter antes de generar el acta.";
                SelectedAiConfigProvider = "OpenRouter";
                OpenAiConfig();
                return;
            }
        }

        var modelName = SelectedAiModel?.Id ?? OpenRouterActaService.DefaultModel;

        var options = new ActaGenerationOptions
        {
            ModelName = modelName,
            Language = SelectedActaLanguage,
            DetailLevel = SelectedDetailLevel,
            IncludeParticipants = IncludeParticipants,
            IncludeDecisions = IncludeDecisions,
            IncludeActionItems = IncludeActionItems,
            IncludePendingQuestions = IncludePendingQuestions,
            IsImpersonal = IsImpersonalActa,
            Endpoint = isCustomServer ? CustomAiEndpoint : null,
            CustomModelName = isCustomServer ? CustomAiModel : null
        };

        try
        {
            IsGeneratingActa = true;
            ActaErrorMessage = string.Empty;
            ActaGenerationStatus = isCustomServer
                ? $"Conectando con Servidor personalizado ({CustomAiModel})..."
                : $"Conectando con OpenRouter ({modelName})...";

            var result = await _aiActaService.GenerateActaAsync(meeting, options);

            if (result.Success && !string.IsNullOrWhiteSpace(result.MarkdownContent))
            {
                GeneratedActaMarkdown = result.MarkdownContent;
                HasGeneratedActa = true;
                meeting.ActaMarkdown = result.MarkdownContent;
                WorkspaceRightTab = 1; // switch workspace right pane to Acta view

                // Guardar reunión actualizada (incluyendo acta.md)
                await _repository.SaveMeetingAsync(meeting);

                if (SelectedHistoricalMeeting != null && SelectedHistoricalMeeting.Meeting.Id == meeting.Id)
                {
                    SelectedHistoryActaMarkdown = result.MarkdownContent;
                    HistoricalDetailTab = 2; // switch to Acta tab
                }

                _ = LoadSavedMeetingsAsync();

                ActaTokensUsedText = result.TotalTokensUsed.HasValue && result.TotalTokensUsed > 0
                    ? $"Tokens: {result.PromptTokensUsed:N0} prompt / {result.CompletionTokensUsed:N0} respuesta (Total: {result.TotalTokensUsed:N0})"
                    : string.Empty;

                StatusMessage = isCustomServer
                    ? $"Acta ejecutiva generada con éxito utilizando Servidor personalizado ({CustomAiModel})."
                    : $"Acta ejecutiva generada con éxito utilizando {modelName}.";
            }
            else
            {
                ActaErrorMessage = result.ErrorMessage ?? "Error desconocido al generar el acta.";
            }
        }
        catch (Exception ex)
        {
            ActaErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsGeneratingActa = false;
        }
    }

    [RelayCommand]
    private void CopyActaToClipboard()
    {
        if (string.IsNullOrWhiteSpace(GeneratedActaMarkdown)) return;

        try
        {
            Clipboard.SetText(GeneratedActaMarkdown);
            StatusMessage = "Acta copiada al portapapeles.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(LocFmt("Common_Clipboard_Error", ex.Message), Loc("Common_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void ExportActaMarkdown()
    {
        if (string.IsNullOrWhiteSpace(GeneratedActaMarkdown)) return;

        var meeting = _meetingService.CurrentMeeting;
        var baseName = meeting != null ? _repository.GetMeetingFileBaseName(meeting) : $"Acta_{DateTime.Now:yyyy-MM-dd_HH-mm}";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{baseName}_acta.md",
            Filter = "Documento Markdown (*.md)|*.md|Todos los archivos (*.*)|*.*",
            Title = "Guardar Acta de la Reunión"
        };

        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, GeneratedActaMarkdown);
            StatusMessage = $"Acta guardada en: {dialog.FileName}";
        }
    }

    private static string MakeValidFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var clean = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
        clean = clean.Replace(' ', '-');
        return string.IsNullOrWhiteSpace(clean) ? "Reunion" : clean;
    }

    private void EnsureSpeakerExists(string speakerId, string initialDisplayName)
    {
        if (string.IsNullOrWhiteSpace(speakerId)) return;

        var existing = Participants.FirstOrDefault(p => p.Id == speakerId);
        if (existing == null)
        {
            var color = SpeakerColors[Participants.Count % SpeakerColors.Length];
            var speakerVm = new SpeakerViewModel(
                speakerId,
                initialDisplayName,
                color,
                onNameChanged: (id, liveName) =>
                {
                    // Actualizar en tiempo real el nombre mostrado en los fragmentos visibles sin recortar espacios
                    foreach (var seg in TranscriptSegments.Where(s => s.SpeakerId == id))
                    {
                        seg.SpeakerDisplayName = liveName;
                    }
                    TranscriptSegmentViewModel.UpdateSpeakerInterventionHeaders(TranscriptSegments);
                },
                onNameCommitted: (id, finalName) =>
                {
                    // Al terminar la edición, persistir el nombre limpio en el modelo y evaluar fusiones
                    _meetingService.UpdateSpeakerDisplayName(id, finalName);
                    if (_meetingService.CurrentMeeting != null && CurrentState == MeetingState.Completed)
                    {
                        _ = _repository.SaveMeetingAsync(_meetingService.CurrentMeeting, CancellationToken.None);
                    }
                },
                availableRealParticipants: RealParticipants);
            Participants.Add(speakerVm);
        }
    }

    public void CommitAllSpeakerNames()
    {
        foreach (var sp in Participants.ToList())
        {
            sp.CommitDisplayName();
        }
    }

    private string GetSpeakerDisplayName(string speakerId)
    {
        var sp = Participants.FirstOrDefault(p => p.Id == speakerId);
        return sp != null && !string.IsNullOrWhiteSpace(sp.DisplayName) ? sp.DisplayName : speakerId;
    }

    private void OnAudioLevelUpdated(object? sender, AudioLevelEventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (e.DeviceType == AudioDeviceType.Microphone)
            {
                MicLevel = e.PeakLevel;
            }
            else if (e.DeviceType == AudioDeviceType.SystemLoopback)
            {
                SystemLevel = e.PeakLevel;
            }
        });
    }

    private void OnCaptureErrorOccurred(object? sender, string errorMessage)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            StatusMessage = errorMessage;
            MessageBox.Show(errorMessage, Loc("Common_Audio_Device_Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private void OnMeetingStateChanged(object? sender, MeetingState newState)
    {
        void UpdateState()
        {
            CurrentState = newState;
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(IsProcessing));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(HasCurrentMeeting));
            OnPropertyChanged(nameof(CanOpenMeetingFolder));
            OnPropertyChanged(nameof(CanExportMeeting));
            OnPropertyChanged(nameof(TranscriptHeaderTitle));
            OnPropertyChanged(nameof(SummaryHeaderTitle));
            OnPropertyChanged(nameof(TranscriptHeaderSubtitle));
            OnPropertyChanged(nameof(CanMuteMicrophoneDuringRecording));
            OnPropertyChanged(nameof(CanMuteSystemAudioDuringRecording));
        }

        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(UpdateState);
        }
        else
        {
            UpdateState();
        }
    }

    private void OnLiveSegmentProduced(object? sender, TranscriptSegment segment)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _meetingService.AppendSegment(segment);
        });
    }

    private void OnSegmentAppended(object? sender, TranscriptSegment segment)
    {
        if (!string.IsNullOrWhiteSpace(segment.SpeakerId))
        {
            EnsureSpeakerExists(segment.SpeakerId, segment.SpeakerDisplayName);
        }

        var speakerVm = !string.IsNullOrWhiteSpace(segment.SpeakerId)
            ? Participants.FirstOrDefault(p => p.Id == segment.SpeakerId)
            : null;
        var color = speakerVm?.ColorHex ?? "#6366F1";

        var vm = new TranscriptSegmentViewModel
        {
            Id = segment.Id,
            FormattedTime = segment.FormattedStartTime,
            StartTime = segment.StartTime,
            EndTime = segment.EndTime > segment.StartTime ? segment.EndTime : segment.StartTime + TimeSpan.FromSeconds(3),
            SpeakerId = segment.SpeakerId ?? string.Empty,
            SpeakerDisplayName = !string.IsNullOrWhiteSpace(speakerVm?.DisplayName)
                ? speakerVm.DisplayName
                : (segment.SpeakerDisplayName ?? string.Empty),
            Text = segment.Text,
            ColorHex = color
        };

        TranscriptSegments.Add(vm);
        TranscriptSegmentViewModel.UpdateSpeakerInterventionHeaders(TranscriptSegments);

        if (speakerVm != null)
        {
            speakerVm.UtteranceCount++;
            speakerVm.AddSpeakingTime(segment.Duration);
        }

        // Enrutar nuevo segmento al coordinador de resumen en vivo local
        _liveSummaryCoordinator.AddSegment(segment);
    }

    private void OnLiveSummaryUpdated(object? sender, SummaryUpdatedEventArgs e)
    {
        void UpdateAction()
        {
            LiveSummaryText = e.Summary;
            LiveSummaryLastUpdated = e.Timestamp;

            if (e.Card != null)
            {
                var existing = LiveSummaryCards.FirstOrDefault(c => c.Id == e.Card.Id);
                if (existing != null)
                {
                    existing.Text = e.Card.Text;
                    existing.FormattedTimeRange = e.Card.FormattedTimeRange;
                }
                else
                {
                    var cardVm = new LiveSummaryCardViewModel
                    {
                        Id = e.Card.Id,
                        FormattedTimeRange = e.Card.FormattedTimeRange,
                        Text = e.Card.Text,
                        Timestamp = e.Card.Timestamp
                    };
                    cardVm.UpdateRelativeTime();
                    LiveSummaryCards.Add(cardVm);
                }
            }
            else if (e.AllCards != null && e.AllCards.Count > 0)
            {
                foreach (var card in e.AllCards)
                {
                    if (!LiveSummaryCards.Any(c => c.Id == card.Id))
                    {
                        var cardVm = new LiveSummaryCardViewModel
                        {
                            Id = card.Id,
                            FormattedTimeRange = card.FormattedTimeRange,
                            Text = card.Text,
                            Timestamp = card.Timestamp
                        };
                        cardVm.UpdateRelativeTime();
                        LiveSummaryCards.Add(cardVm);
                    }
                }
            }

            if (_meetingService.CurrentMeeting != null)
            {
                _meetingService.CurrentMeeting.SummaryText = e.Summary;
                if (e.Card != null && !_meetingService.CurrentMeeting.SummarySegments.Any(s => s.Id == e.Card.Id))
                {
                    _meetingService.CurrentMeeting.SummarySegments.Add(e.Card);
                }
            }

            UpdateLiveSummaryRelativeTime();
        }

        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.InvokeAsync(UpdateAction);
        }
        else
        {
            UpdateAction();
        }
    }

    private void OnLiveSummaryGeneratingStateChanged(object? sender, bool isGenerating)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsLiveSummaryGenerating = isGenerating;
                UpdateLiveSummaryRelativeTime();
            });
        }
        else
        {
            IsLiveSummaryGenerating = isGenerating;
            UpdateLiveSummaryRelativeTime();
        }
    }

    private void UpdateLiveSummaryRelativeTime()
    {
        foreach (var card in LiveSummaryCards)
        {
            card.UpdateRelativeTime();
        }

        if (IsLiveSummaryGenerating)
        {
            LiveSummaryStatusText = "Generando resumen en segundo plano...";
            return;
        }

        if (LiveSummaryLastUpdated.HasValue)
        {
            var diff = (int)(DateTimeOffset.Now - LiveSummaryLastUpdated.Value).TotalSeconds;
            if (diff < 5)
            {
                LiveSummaryStatusText = "Actualizado hace un instante";
            }
            else if (diff < 60)
            {
                LiveSummaryStatusText = $"Actualizado hace {diff} s";
            }
            else
            {
                int mins = diff / 60;
                int secs = diff % 60;
                LiveSummaryStatusText = $"Actualizado hace {mins} min {secs} s";
            }
        }
        else if (IsRecording)
        {
            LiveSummaryStatusText = IsLiveSummaryModelDownloaded
                ? "Escuchando conversación... El primer resumen se generará en breve."
                : "Modelo de resumen pendiente de descarga para activar el panel.";
        }
        else
        {
            LiveSummaryStatusText = "A la espera de transcripción...";
        }
    }

    private void OnSegmentUpdated(object? sender, TranscriptSegment segment)
    {
        _liveSummaryCoordinator.UpdateSegment(segment);

        var vm = TranscriptSegments.LastOrDefault(s => s.Id == segment.Id);
        if (vm != null)
        {
            vm.Text = segment.Text;
            vm.FormattedTime = segment.FormattedStartTime;
        }

        if (!string.IsNullOrWhiteSpace(segment.SpeakerId))
        {
            var speakerVm = Participants.FirstOrDefault(p => p.Id == segment.SpeakerId);
            if (speakerVm != null && _meetingService.CurrentMeeting != null)
            {
                var totalDuration = TimeSpan.FromSeconds(
                    _meetingService.CurrentMeeting.Transcript
                        .Where(s => s.SpeakerId == segment.SpeakerId)
                        .Sum(s => s.Duration.TotalSeconds));
                speakerVm.TotalSpeakingDuration = totalDuration;
            }
        }
    }

    private void OnSpeakerUpdated(object? sender, Speaker speaker)
    {
        foreach (var seg in TranscriptSegments.Where(s => s.SpeakerId == speaker.Id))
        {
            seg.SpeakerDisplayName = speaker.DisplayName;
        }
        TranscriptSegmentViewModel.UpdateSpeakerInterventionHeaders(TranscriptSegments);

        var vm = Participants.FirstOrDefault(p => p.Id == speaker.Id);
        if (vm != null && vm.DisplayName != speaker.DisplayName)
        {
            vm.DisplayName = speaker.DisplayName;
        }

        if (_meetingService.CurrentMeeting != null && CurrentState == MeetingState.Completed)
        {
            _ = _repository.SaveMeetingAsync(_meetingService.CurrentMeeting, CancellationToken.None);
        }
    }

    private void OnSpeakerMerged(object? sender, (string MergedSpeakerId, string TargetSpeakerId) e)
    {
        var mergedVm = Participants.FirstOrDefault(p => string.Equals(p.Id, e.MergedSpeakerId, StringComparison.OrdinalIgnoreCase));
        var targetVm = Participants.FirstOrDefault(p => string.Equals(p.Id, e.TargetSpeakerId, StringComparison.OrdinalIgnoreCase));

        if (targetVm != null && _meetingService.CurrentMeeting != null)
        {
            // Recalcular intervenciones y tiempo de habla unificado
            var segmentsForSpeaker = _meetingService.CurrentMeeting.Transcript
                .Where(s => string.Equals(s.SpeakerId, targetVm.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            targetVm.UtteranceCount = segmentsForSpeaker.Count;
            targetVm.TotalSpeakingDuration = TimeSpan.FromSeconds(segmentsForSpeaker.Sum(s => s.Duration.TotalSeconds));

            // Actualizar todos los fragmentos en la vista en vivo
            foreach (var seg in TranscriptSegments.Where(s => string.Equals(s.SpeakerId, e.MergedSpeakerId, StringComparison.OrdinalIgnoreCase)))
            {
                seg.SpeakerId = targetVm.Id;
                seg.SpeakerDisplayName = targetVm.DisplayName;
                seg.ColorHex = targetVm.ColorHex;
            }

            // Actualizar historial si está cargado
            foreach (var seg in HistoricalTranscriptSegments.Where(s => string.Equals(s.SpeakerId, e.MergedSpeakerId, StringComparison.OrdinalIgnoreCase)))
            {
                seg.SpeakerId = targetVm.Id;
                seg.SpeakerDisplayName = targetVm.DisplayName;
                seg.ColorHex = targetVm.ColorHex;
            }

            TranscriptSegmentViewModel.UpdateSpeakerInterventionHeaders(TranscriptSegments);
            TranscriptSegmentViewModel.UpdateSpeakerInterventionHeaders(HistoricalTranscriptSegments);
        }

        // Retirar el participante fusionado de la colección visual
        if (mergedVm != null)
        {
            Participants.Remove(mergedVm);
        }
    }

    #region Model Catalog Management

    public void RefreshModelCatalog()
    {
        try
        {
            var available = _modelManager.GetAvailableModels();
            ModelCatalog.Clear();

            foreach (var m in available)
            {
                var isRec = m.Size == FirstRunRecommendedModel;
                var formattedMb = m.FileSizeBytes > 0 ? $"~{m.FileSizeBytes / 1_000_000} MB" : string.Empty;

                ModelCatalog.Add(new WhisperModelCardViewModel
                {
                    Size = m.Size,
                    Name = m.Name,
                    Description = m.Description,
                    FormattedSize = formattedMb,
                    EngineBadge = m.FamilyName,
                    IsDownloaded = m.IsDownloaded,
                    IsRecommended = isRec
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al refrescar el catálogo de modelos.");
        }
    }

    [RelayCommand]
    private async Task DownloadModelCardAsync(WhisperModelCardViewModel? card)
    {
        if (card == null || card.IsDownloading) return;

        if (!IsModelsDirectoryAccessible)
        {
            MessageBox.Show(
                LocFmt("Msg_Model_Location_Unavailable", CurrentModelsDirectory),
                Loc("Msg_Model_Location_Unavailable_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            card.IsDownloading = true;
            card.DownloadProgress = 0.0;
            StatusMessage = $"Descargando {card.Name}...";

            var progress = new Progress<double>(p =>
            {
                card.DownloadProgress = p;
            });

            await _modelManager.EnsureModelDownloadedAsync(card.Size, progress);
            card.IsDownloaded = true;
            card.IsDownloading = false;
            IsFirstRunOnboardingVisible = false;
            StatusMessage = $"Modelo {card.Name} descargado y listo para uso local.";
            MessageBox.Show(LocFmt("Msg_Model_Downloaded_Ready", card.Name), Loc("Msg_Model_Downloaded_Ready_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshModelCatalog();
        }
        catch (Exception ex)
        {
            card.IsDownloading = false;
            StatusMessage = $"Error al descargar {card.Name}: {ex.Message}";
            MessageBox.Show(LocFmt("Msg_Model_Download_Error", card.Name, ex.Message), Loc("Msg_Model_Download_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void DeleteModelCard(WhisperModelCardViewModel? card)
    {
        if (card == null) return;

        var confirm = MessageBox.Show(
            LocFmt("Msg_Model_Delete_Confirm", card.Name, card.FormattedSize),
            Loc("Msg_Model_Delete_Confirm_Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            _modelManager.DeleteModel(card.Size);
            RefreshModelCatalog();
            StatusMessage = $"Modelo '{card.Name}' eliminado.";
        }
    }

    #endregion

    #region Models Directory Management

    [RelayCommand]
    public void ChangeModelsDirectory()
    {
        try
        {
            var initialDir = Directory.Exists(CurrentModelsDirectory)
                ? CurrentModelsDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Seleccionar carpeta para almacenar los modelos de DictaMeeting",
                InitialDirectory = initialDir,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true) return;

            var selectedFolder = dialog.FolderName;
            if (string.IsNullOrWhiteSpace(selectedFolder)) return;

            var (isValid, errorMessage) = ModelStorageManager.ValidateDestination(CurrentModelsDirectory, selectedFolder);
            if (!isValid)
            {
                MessageBox.Show(
                    errorMessage ?? Loc("Msg_Model_Folder_Invalid_Default"),
                    Loc("Msg_Model_Folder_Invalid_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var currentModels = ModelStorageManager.ScanModels(CurrentModelsDirectory).Where(m => m.IsValid).ToList();
            if (currentModels.Count == 0)
            {
                // Caso A: La ubicación actual NO contiene modelos válidos
                ApplyNewModelsDirectory(selectedFolder);
                MessageBox.Show(
                    LocFmt("Msg_Model_Folder_Configured", selectedFolder),
                    Loc("Msg_Model_Folder_Configured_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                // Caso B: La ubicación actual contiene modelos. Mostrar diálogo de confirmación
                PendingModelsDirectory = Path.GetFullPath(selectedFolder);
                MoveModelsSourceDirectory = CurrentModelsDirectory;
                MoveModelsTargetDirectory = PendingModelsDirectory;
                MoveModelsFoundCount = currentModels.Count;
                long totalBytes = currentModels.Sum(m => m.TotalSizeBytes);
                MoveModelsFoundFormattedSize = FormatBytes(totalBytes);
                MoveModelsHasError = false;
                MoveModelsErrorMessage = null;
                IsMovingModels = false;
                IsMoveModelsModalOpen = true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al cambiar la ubicación de los modelos.");
            MessageBox.Show(
                LocFmt("Msg_Model_Change_Error", ex.Message),
                Loc("Msg_Model_Change_Error_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void CancelMoveModels()
    {
        if (IsMovingModels) return;
        IsMoveModelsModalOpen = false;
        PendingModelsDirectory = null;
    }

    [RelayCommand]
    public void ConfirmChangeWithoutMove()
    {
        if (IsMovingModels || string.IsNullOrWhiteSpace(PendingModelsDirectory)) return;

        var warn = MessageBox.Show(
            Loc("Msg_Model_Change_Without_Move"),
            Loc("Msg_Model_Change_Without_Move_Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (warn != MessageBoxResult.Yes) return;

        var target = PendingModelsDirectory;
        IsMoveModelsModalOpen = false;
        PendingModelsDirectory = null;
        ApplyNewModelsDirectory(target);
    }

    [RelayCommand]
    public void ConfirmMoveModels()
    {
        if (IsMovingModels || string.IsNullOrWhiteSpace(PendingModelsDirectory)) return;

        var source = CurrentModelsDirectory;
        var target = PendingModelsDirectory;

        IsMovingModels = true;
        MoveModelsHasError = false;
        MoveModelsErrorMessage = null;
        MovingModelsCurrentItem = "Iniciando migración segura...";
        MovingModelsCurrentOperation = "Comprobando requisitos previos...";
        MovingModelsTotalProgress = 0.0;
        MovingModelsTotalProgressPercentText = "0%";

        _moveModelsCts = new CancellationTokenSource();
        var ct = _moveModelsCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<ModelMigrationProgress>(p =>
                {
                    void Update()
                    {
                        MovingModelsCurrentItem = p.CurrentModelName;
                        MovingModelsCurrentOperation = p.CurrentOperation;
                        MovingModelsTotalProgress = p.OverallProgress;
                        MovingModelsTotalProgressPercentText = $"{(int)(p.OverallProgress * 100)}%";
                    }

                    if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                    {
                        Application.Current.Dispatcher.InvokeAsync(Update);
                    }
                    else
                    {
                        Update();
                    }
                });

                await ModelStorageManager.MigrateModelsAsync(source, target, progress, ct);

                void OnSuccess()
                {
                    IsMovingModels = false;
                    IsMoveModelsModalOpen = false;
                    PendingModelsDirectory = null;
                    ApplyNewModelsDirectory(target);
                    MessageBox.Show(
                        LocFmt("Msg_Model_Migrate_Success", target),
                        Loc("Msg_Model_Migrate_Success_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(OnSuccess);
                }
                else
                {
                    OnSuccess();
                }
            }
            catch (OperationCanceledException)
            {
                void OnCancel()
                {
                    IsMovingModels = false;
                    IsMoveModelsModalOpen = false;
                    PendingModelsDirectory = null;
                    MessageBox.Show(
                        Loc("Msg_Model_Migrate_Cancelled"),
                        Loc("Msg_Model_Migrate_Cancelled_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(OnCancel);
                }
                else
                {
                    OnCancel();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error durante la migración de modelos a '{Target}'.", target);

                void OnError()
                {
                    IsMovingModels = false;
                    MoveModelsHasError = true;
                    MoveModelsErrorMessage = $"No se pudo completar la migración de modelos:\n{ex.Message}\n\nLos modelos originales no han sido eliminados y permanecen en la ubicación anterior.";
                }

                if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(OnError);
                }
                else
                {
                    OnError();
                }
            }
        });
    }

    [RelayCommand]
    public void CancelMovingModelsExecution()
    {
        _moveModelsCts?.Cancel();
    }

    public void ApplyNewModelsDirectory(string newDirectory)
    {
        var fullPath = Path.GetFullPath(newDirectory);
        _modelManager.SetModelsDirectory(fullPath);
        _liveSummaryModelManager.SetModelsDirectory(Path.Combine(fullPath, "summary"));
        _diarizationService.SetModelsFolder(Path.Combine(fullPath, "diarization"));

        CurrentModelsDirectory = fullPath;
        var defaultDir = UserSettings.DefaultModelsDirectory;
        IsCustomModelsDirectory = !string.Equals(
            fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(defaultDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

        ModelsDirectoryTypeBadge = IsCustomModelsDirectory ? "Ubicación personalizada" : "Ubicación predeterminada";
        IsModelsDirectoryAccessible = ModelStorageManager.IsDirectoryAccessible(fullPath);
        ModelsDirectoryWarningMessage = IsModelsDirectoryAccessible ? string.Empty : $"La ubicación de los modelos no está disponible ({fullPath}).";

        SaveCurrentUserSettings();
        RefreshModelCatalog();
        RefreshFirstRunModelsStatus();
    }

    #endregion

    #region Meeting History Management

    public async Task LoadSavedMeetingsAsync()
    {
        try
        {
            IsLoadingHistory = true;
            var all = await _repository.GetAllMeetingsAsync();
            var query = HistorySearchQuery?.Trim() ?? string.Empty;

            var filtered = all.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(m =>
                    (!string.IsNullOrEmpty(m.Title) && m.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    m.Date.ToString("dd/MM/yyyy").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    m.Transcript.Any(t => !string.IsNullOrEmpty(t.Text) && t.Text.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }

            var sorted = filtered.OrderByDescending(m => m.Date).ToList();
            var prevId = SelectedHistoricalMeeting?.Id;

            SavedMeetings.Clear();
            foreach (var m in sorted)
            {
                SavedMeetings.Add(new MeetingHistoryItemViewModel(m));
            }

            if (!string.IsNullOrEmpty(prevId))
            {
                SelectedHistoricalMeeting = SavedMeetings.FirstOrDefault(m => m.Id == prevId) ?? SavedMeetings.FirstOrDefault();
            }
            else
            {
                SelectedHistoricalMeeting = SavedMeetings.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al cargar historial de reuniones.");
        }
        finally
        {
            IsLoadingHistory = false;
        }
    }

    partial void OnHistorySearchQueryChanged(string value)
    {
        _ = LoadSavedMeetingsAsync();
    }

    partial void OnSelectedHistoricalMeetingChanged(MeetingHistoryItemViewModel? value)
    {
        IsEditingHistoricalTitle = false;
        EditingHistoricalTitleText = string.Empty;

        if (value != null)
        {
            value.RefreshStatus();
            SelectedHistoryActaMarkdown = value.Meeting.ActaMarkdown ?? string.Empty;

            // Cargar automáticamente en la vista de Reunión la reunión seleccionada mientras se navega por el Historial
            if (!IsRecording && !IsStartingMeeting)
            {
                value.Meeting.SyncParticipantsFromTranscript();
                LoadMeetingIntoWorkspace(value.Meeting);
            }
        }
        else
        {
            SelectedHistoryActaMarkdown = string.Empty;
        }
    }

    public void LoadMeetingIntoWorkspace(Meeting meeting)
    {
        if (meeting == null) return;

        // 1. Establecer en MeetingService como reunión abierta
        _meetingService.OpenMeeting(meeting);

        // 2. Establecer estado y metadatos
        CurrentState = meeting.State == MeetingState.Recording
            ? MeetingState.Recording
            : (meeting.State == MeetingState.Processing ? MeetingState.Processing : MeetingState.Completed);

        IsPostProcessing = _meetingProcessingService.IsProcessing && _meetingProcessingService.CurrentMeetingId == meeting.Id;
        if (IsPostProcessing)
        {
            var histItem = SavedMeetings.FirstOrDefault(m => m.Id == meeting.Id);
            PostProcessingProgress = histItem?.Meeting.ProcessingProgress ?? 0.0;
            PostProcessingStatusText = histItem?.Meeting.ProcessingStatusText ?? "Procesando...";
            CanCancelPostProcessing = true;
        }

        MeetingTitle = meeting.Title;
        Organizer = meeting.Organizer ?? string.Empty;
        Company = meeting.Company ?? string.Empty;
        MeetingFolderPath = _repository.GetMeetingDirectoryPath(meeting);

        // 3. Duración
        DurationText = meeting.Duration.TotalHours >= 1
            ? $"{(int)meeting.Duration.TotalHours:D2}:{meeting.Duration.Minutes:D2}:{meeting.Duration.Seconds:D2}"
            : $"{meeting.Duration.Minutes:D2}:{meeting.Duration.Seconds:D2}";

        // 4. Participantes reales esperados
        RealParticipants.Clear();
        if (meeting.ExpectedParticipants != null)
        {
            foreach (var p in meeting.ExpectedParticipants)
            {
                if (!string.IsNullOrWhiteSpace(p) && !RealParticipants.Contains(p, StringComparer.OrdinalIgnoreCase))
                {
                    RealParticipants.Add(p);
                }
            }
        }

        // Agregar también participantes con nombre conocido no técnico
        foreach (var sp in meeting.Participants)
        {
            if (!string.IsNullOrWhiteSpace(sp.DisplayName) &&
                !sp.DisplayName.StartsWith("SPEAKER_", StringComparison.OrdinalIgnoreCase) &&
                !RealParticipants.Contains(sp.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                RealParticipants.Add(sp.DisplayName);
            }
        }

        // 5. Voces detectadas (Speakers)
        Participants.Clear();
        int colorIdx = 0;
        foreach (var sp in meeting.Participants)
        {
            var color = !string.IsNullOrWhiteSpace(sp.ColorHex) ? sp.ColorHex : SpeakerColors[colorIdx % SpeakerColors.Length];
            colorIdx++;

            var segmentsForSpeaker = meeting.Transcript
                .Where(s => string.Equals(s.SpeakerId, sp.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var speakerVm = new SpeakerViewModel(
                sp.Id,
                sp.DisplayName,
                color,
                onNameChanged: (id, liveName) =>
                {
                    foreach (var seg in TranscriptSegments.Where(s => string.Equals(s.SpeakerId, id, StringComparison.OrdinalIgnoreCase)))
                    {
                        seg.SpeakerDisplayName = liveName;
                    }
                    TranscriptSegmentViewModel.UpdateSpeakerInterventionHeaders(TranscriptSegments);
                },
                onNameCommitted: (id, finalName) =>
                {
                    meeting.UpdateSpeakerDisplayName(id, finalName);
                    _meetingService.UpdateSpeakerDisplayName(id, finalName);

                    // Si el nombre no estaba en RealParticipants y no es SPEAKER_XX, registrarlo
                    if (!string.IsNullOrWhiteSpace(finalName) &&
                        !finalName.StartsWith("SPEAKER_", StringComparison.OrdinalIgnoreCase) &&
                        !RealParticipants.Contains(finalName, StringComparer.OrdinalIgnoreCase))
                    {
                        RealParticipants.Add(finalName);
                        meeting.ExpectedParticipants ??= new();
                        if (!meeting.ExpectedParticipants.Contains(finalName, StringComparer.OrdinalIgnoreCase))
                        {
                            meeting.ExpectedParticipants.Add(finalName);
                        }
                    }

                    _ = _repository.SaveMeetingAsync(meeting, CancellationToken.None);
                    var histItem = SavedMeetings.FirstOrDefault(m => m.Id == meeting.Id);
                    histItem?.RefreshStatus();
                },
                availableRealParticipants: RealParticipants);

            speakerVm.UtteranceCount = segmentsForSpeaker.Count;
            speakerVm.TotalSpeakingDuration = TimeSpan.FromSeconds(segmentsForSpeaker.Sum(s => s.Duration.TotalSeconds));
            Participants.Add(speakerVm);
        }

        // 6. Transcripción completa
        TranscriptSegments.Clear();
        foreach (var seg in meeting.Transcript)
        {
            var sp = meeting.Participants.FirstOrDefault(s => string.Equals(s.Id, seg.SpeakerId, StringComparison.OrdinalIgnoreCase));
            var color = sp?.ColorHex ?? "#0071E3";
            TranscriptSegments.Add(new TranscriptSegmentViewModel
            {
                Id = seg.Id,
                FormattedTime = seg.FormattedStartTime,
                StartTime = seg.StartTime,
                EndTime = seg.EndTime > seg.StartTime ? seg.EndTime : seg.StartTime + TimeSpan.FromSeconds(3),
                SpeakerId = seg.SpeakerId,
                SpeakerDisplayName = !string.IsNullOrWhiteSpace(sp?.DisplayName) ? sp.DisplayName : seg.SpeakerDisplayName,
                Text = seg.Text,
                ColorHex = color
            });
        }
        TranscriptSegmentViewModel.UpdateSpeakerInterventionHeaders(TranscriptSegments);

        // Configurar reproductor y sincronización si la reunión tiene audio grabado
        _audioPlayerService.Stop();
        _audioPlayerService.Close();
        _transcriptAudioSyncService.Clear();

        if (meeting.State == MeetingState.Completed && meeting.AudioFileExists() && !IsPostProcessing)
        {
            _audioPlayerService.Load(meeting.AudioFilePath, meeting.Duration);
            _transcriptAudioSyncService.SetSegments(TranscriptSegments);
        }

        // 7. Resumen
        LiveSummaryCards.Clear();
        if (meeting.SummarySegments != null && meeting.SummarySegments.Count > 0)
        {
            foreach (var seg in meeting.SummarySegments)
            {
                LiveSummaryCards.Add(new LiveSummaryCardViewModel
                {
                    Id = seg.Id,
                    FormattedTimeRange = !string.IsNullOrWhiteSpace(seg.FormattedTimeRange)
                        ? seg.FormattedTimeRange
                        : $"{seg.StartTime:mm\\:ss} - {seg.EndTime:mm\\:ss}",
                    Text = seg.Text,
                    RelativeTimeString = string.Empty,
                    Timestamp = seg.Timestamp
                });
            }
        }
        else if (!string.IsNullOrWhiteSpace(meeting.SummaryText))
        {
            LiveSummaryCards.Add(new LiveSummaryCardViewModel
            {
                FormattedTimeRange = "Resumen de la sesión",
                Text = meeting.SummaryText,
                RelativeTimeString = string.Empty
            });
        }

        // 8. Acta
        GeneratedActaMarkdown = meeting.ActaMarkdown ?? string.Empty;
        HasGeneratedActa = !string.IsNullOrWhiteSpace(meeting.ActaMarkdown);
        if (HasGeneratedActa && LiveSummaryCards.Count == 0)
        {
            WorkspaceRightTab = 1;
        }

        // 9. Actualizar notificaciones del workspace
        RefreshWorkspaceState();
    }

    [RelayCommand]
    public async Task OpenMeetingAsync(MeetingHistoryItemViewModel? item = null)
    {
        var targetItem = item ?? SelectedHistoricalMeeting;
        if (targetItem == null) return;

        if (IsRecording || IsStartingMeeting)
        {
            var confirm = MessageBox.Show(
                Loc("Common_Active_Recording_Msg"),
                Loc("Common_Active_Recording_Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;
            await StopMeetingAsync();
        }

        var freshMeeting = await _repository.GetMeetingAsync(targetItem.Id) ?? targetItem.Meeting;
        freshMeeting.SyncParticipantsFromTranscript();

        LoadMeetingIntoWorkspace(freshMeeting);

        // Cambiar a la pestaña principal "Reunión"
        SelectedNavigationTab = 0;
        StatusMessage = $"Reunión '{freshMeeting.Title}' abierta en el espacio de trabajo.";
    }

    public void RefreshWorkspaceState()
    {
        OnPropertyChanged(nameof(CurrentOpenMeeting));
        OnPropertyChanged(nameof(IsOpenMeetingCompleted));
        OnPropertyChanged(nameof(IsPreparedNewMeeting));
        OnPropertyChanged(nameof(CanReprocessMeeting));
        OnPropertyChanged(nameof(CanEditMeetingTitle));
        OnPropertyChanged(nameof(OpenMeetingSubtitle));
        OnPropertyChanged(nameof(OpenMeetingAudioStatusText));
        OnPropertyChanged(nameof(HasCurrentMeeting));
        OnPropertyChanged(nameof(CanOpenMeetingFolder));
        OnPropertyChanged(nameof(CanExportMeeting));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(CanStartMeeting));
        OnPropertyChanged(nameof(CanStopMeeting));
        OnPropertyChanged(nameof(PrepStatusTitle));
        OnPropertyChanged(nameof(PrepStatusSubtitle));
        OnPropertyChanged(nameof(TranscriptHeaderTitle));
        OnPropertyChanged(nameof(SummaryHeaderTitle));
        OnPropertyChanged(nameof(TranscriptHeaderSubtitle));
        OnPropertyChanged(nameof(TotalParticipantsCount));
        OnPropertyChanged(nameof(ParticipantsSummaryText));
        OnPropertyChanged(nameof(MicrophoneSummary));
        OnPropertyChanged(nameof(SystemAudioSummary));
        OnPropertyChanged(nameof(LiveModelSummary));
        OnPropertyChanged(nameof(HasPlayerAudio));
        OnPropertyChanged(nameof(IsAudioPlaying));
        OnPropertyChanged(nameof(AudioPosition));
        OnPropertyChanged(nameof(AudioDuration));
        OnPropertyChanged(nameof(AudioPositionText));
        OnPropertyChanged(nameof(AudioSliderMaximum));
        OnPropertyChanged(nameof(AudioSliderValue));
        OnPropertyChanged(nameof(AudioVolume));
        OnPropertyChanged(nameof(IsAudioMuted));
        OnPropertyChanged(nameof(AudioVolumeToolTip));
        OnPropertyChanged(nameof(AudioPlayPauseButtonToolTip));
    }

    private void UpdateHistoricalSpeakerDisplayName(string speakerId, string newDisplayName)
    {
        if (SelectedHistoricalMeeting == null) return;

        var meeting = SelectedHistoricalMeeting.Meeting;
        meeting.UpdateSpeakerDisplayName(speakerId, newDisplayName);

        // Sincronizar el ViewModel del speaker correspondiente si no coincide
        var spVm = HistoricalParticipants.FirstOrDefault(p => string.Equals(p.Id, speakerId, StringComparison.OrdinalIgnoreCase));
        if (spVm != null && spVm.DisplayName != newDisplayName)
        {
            spVm.DisplayName = newDisplayName;
        }

        // Si el nombre no estaba en HistoricalRealParticipants y no es SPEAKER_XX, registrarlo
        if (!string.IsNullOrWhiteSpace(newDisplayName) &&
            !newDisplayName.StartsWith("SPEAKER_", StringComparison.OrdinalIgnoreCase) &&
            !HistoricalRealParticipants.Contains(newDisplayName, StringComparer.OrdinalIgnoreCase))
        {
            HistoricalRealParticipants.Add(newDisplayName);
            if (!meeting.ExpectedParticipants.Contains(newDisplayName, StringComparer.OrdinalIgnoreCase))
            {
                meeting.ExpectedParticipants.Add(newDisplayName);
            }
        }

        _ = _repository.SaveMeetingAsync(meeting, CancellationToken.None);
        SelectedHistoricalMeeting.RefreshStatus();
    }

    [RelayCommand]
    private async Task RefreshHistoryAsync()
    {
        await LoadSavedMeetingsAsync();
    }

    [RelayCommand]
    private void OpenHistoricalMeetingFolder()
    {
        if (SelectedHistoricalMeeting == null) return;
        var dir = _repository.GetMeetingDirectoryPath(SelectedHistoricalMeeting.Meeting);
        if (System.IO.Directory.Exists(dir))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        else
        {
            MessageBox.Show(LocFmt("Msg_Meeting_Folder_NotExists", dir), Loc("Common_Folder_NotFound_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void ExportHistoricalMarkdown()
    {
        if (SelectedHistoricalMeeting == null) return;
        var baseName = _repository.GetMeetingFileBaseName(SelectedHistoricalMeeting.Meeting);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{baseName}_transcript.md",
            Filter = "Documento Markdown (*.md)|*.md|Todos los archivos (*.*)|*.*",
            Title = "Exportar Transcripción Histórica"
        };
        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, _documentExporter.ExportToMarkdown(SelectedHistoricalMeeting.Meeting));
            StatusMessage = $"Transcripción exportada a: {dialog.FileName}";
        }
    }

    [RelayCommand]
    private void ExportHistoricalPlainText()
    {
        if (SelectedHistoricalMeeting == null) return;
        var baseName = _repository.GetMeetingFileBaseName(SelectedHistoricalMeeting.Meeting);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{baseName}_transcript.txt",
            Filter = "Texto Plano (*.txt)|*.txt|Todos los archivos (*.*)|*.*",
            Title = "Exportar Transcripción en Texto Plano"
        };
        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, _documentExporter.ExportToPlainText(SelectedHistoricalMeeting.Meeting));
            StatusMessage = $"Transcripción exportada a: {dialog.FileName}";
        }
    }

    [RelayCommand]
    private void ExportHistoricalJson()
    {
        if (SelectedHistoricalMeeting == null) return;
        var baseName = _repository.GetMeetingFileBaseName(SelectedHistoricalMeeting.Meeting);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{baseName}_meeting.json",
            Filter = "Archivo JSON (*.json)|*.json|Todos los archivos (*.*)|*.*",
            Title = "Exportar Sesión Completa en JSON"
        };
        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, _documentExporter.ExportToJson(SelectedHistoricalMeeting.Meeting));
            StatusMessage = $"Archivo JSON exportado a: {dialog.FileName}";
        }
    }

    #endregion
}
