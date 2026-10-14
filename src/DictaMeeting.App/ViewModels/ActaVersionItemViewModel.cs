using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DictaMeeting.App.Services;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;

namespace DictaMeeting.App.ViewModels;

/// <summary>
/// ViewModel que representa una versión individual de acta en el selector de la cabecera.
/// Proporciona formateo localizado y dinámico de idioma, nivel de detalle y fecha.
/// </summary>
public partial class ActaVersionItemViewModel : ObservableObject
{
    public MeetingActaVersion Version { get; }

    public ActaVersionItemViewModel(MeetingActaVersion version)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
        LocalizationManager.Instance.PropertyChanged += (s, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "Item[]")
            {
                NotifyChanged();
            }
        };
    }

    public string Id => Version.Id;
    public string MeetingId => Version.MeetingId;
    public string FileName => Version.FileName;
    public LanguageMode Language => Version.Language;
    public ActaDetailLevel DetailLevel => Version.DetailLevel;
    public DateTimeOffset CreatedAt => Version.CreatedAt;
    public DateTimeOffset? LastModifiedAt => Version.LastModifiedAt;
    public string? ModelUsed => Version.ModelUsed;
    public bool IsLegacyMigrated => Version.IsLegacyMigrated;

    public string FormattedLanguage => GetLanguageLabel(Version.Language, LocalizationManager.Instance);
    public string FormattedDetailLevel => GetDetailLabel(Version.DetailLevel, LocalizationManager.Instance);
    public string FormattedDate => Version.CreatedAt.LocalDateTime.ToString("dd/MM/yyyy HH:mm");

    public string MarkdownContent
    {
        get => Version.MarkdownContent;
        set
        {
            if (Version.MarkdownContent != value)
            {
                Version.MarkdownContent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string? OriginalAiMarkdownContent => Version.OriginalAiMarkdownContent;

    /// <summary>
    /// Texto formateado para el selector desplegable y el panel de historial del modal:
    /// Ejemplos:
    /// - Español · Normal · 14/10/2026 10:34
    /// - English · Comprehensive · 14/10/2026 10:41
    /// - Versión anterior · 14/10/2026 10:34
    /// </summary>
    public string DisplayName
    {
        get
        {
            var loc = LocalizationManager.Instance;
            var dateStr = FormattedDate;

            if (Version.IsLegacyMigrated)
            {
                var legacyLabel = GetSafeLoc(loc, "Acta_Version_Legacy", "Versión anterior");
                return $"{legacyLabel} · {dateStr}";
            }

            string langLabel = FormattedLanguage;
            string detailLabel = FormattedDetailLevel;

            var modIndicator = Version.LastModifiedAt.HasValue ? " *" : "";
            return $"{langLabel} · {detailLabel} · {dateStr}{modIndicator}";
        }
    }

    public static string GetLanguageLabel(LanguageMode mode, LocalizationManager loc)
    {
        if (mode == LanguageMode.English)
        {
            var val = loc["Language_English"];
            if (!string.IsNullOrWhiteSpace(val) && val != "Language_English") return val;
            val = loc["System_Language_English"];
            if (!string.IsNullOrWhiteSpace(val) && val != "System_Language_English") return val;
            return "English";
        }
        else if (mode == LanguageMode.Spanish)
        {
            var val = loc["Language_Spanish"];
            if (!string.IsNullOrWhiteSpace(val) && val != "Language_Spanish") return val;
            val = loc["System_Language_Spanish"];
            if (!string.IsNullOrWhiteSpace(val) && val != "System_Language_Spanish") return val;
            return "Español";
        }
        else if (mode == LanguageMode.Auto)
        {
            var val = loc["Meeting_Language_Auto"];
            if (!string.IsNullOrWhiteSpace(val) && val != "Meeting_Language_Auto") return val;
            return "Auto";
        }
        else
        {
            return mode.ToString();
        }
    }

    public static string GetDetailLabel(ActaDetailLevel detail, LocalizationManager loc)
    {
        return detail switch
        {
            ActaDetailLevel.Breve => GetSafeLoc(loc, "Acta_Detail_Breve", "Breve"),
            ActaDetailLevel.Normal => GetSafeLoc(loc, "Acta_Detail_Normal", "Normal"),
            ActaDetailLevel.Detallada => GetSafeLoc(loc, "Acta_Detail_Detallada", "Detallada"),
            ActaDetailLevel.Exhaustiva => GetSafeLoc(loc, "Acta_Detail_Exhaustiva", "Exhaustiva"),
            _ => detail.ToString()
        };
    }

    private static string GetSafeLoc(LocalizationManager loc, string key, string fallback)
    {
        var val = loc[key];
        return (!string.IsNullOrWhiteSpace(val) && val != key) ? val : fallback;
    }

    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(FormattedLanguage));
        OnPropertyChanged(nameof(FormattedDetailLevel));
        OnPropertyChanged(nameof(FormattedDate));
        OnPropertyChanged(nameof(MarkdownContent));
        OnPropertyChanged(nameof(LastModifiedAt));
    }

    public override string ToString() => DisplayName;
}
