namespace DictaMeeting.Infrastructure.Persistence;

/// <summary>
/// Modelo de configuración de preferencias de usuario persistidas entre sesiones locales.
/// </summary>
public class UserSettings
{
    public bool CaptureMicrophone { get; set; } = true;
    public string? SelectedMicrophoneId { get; set; }
    public string? SelectedMicrophoneName { get; set; }

    public bool CaptureSystemAudio { get; set; } = true;
    public string? SelectedSystemDeviceId { get; set; }
    public string? SelectedSystemDeviceName { get; set; }

    public string? SelectedLiveModel { get; set; }
    public string? SelectedFinalModel { get; set; }
    public string? SelectedLanguage { get; set; }
    public string? UiLanguage { get; set; }
    public bool IsDarkMode { get; set; } = false;

    // Configuración de Servidor Personalizado de IA (OpenAI-compatible)
    public string? CustomAiEndpoint { get; set; }
    public string? CustomAiModel { get; set; }
    public string? SelectedAiModelId { get; set; }
    public string? SelectedAiConfigProvider { get; set; } = "OpenRouter";

    // Privacidad y grabación
    public bool ShowRecordingNotice { get; set; } = true;
    public bool HasSeenRecordingNotice { get; set; } = false;

    // Primer arranque - Asistente de descarga de modelos básicos
    public bool FirstRunModelsPromptCompleted { get; set; } = false;

    // Ubicación personalizada para el almacenamiento de modelos locales (null o vacío = ubicación predeterminada en AppData)
    public string? ModelsDirectory { get; set; }

    /// <summary>
    /// Ruta predeterminada en %LOCALAPPDATA%\DictaMeeting\models
    /// </summary>
    public static string DefaultModelsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models");

    /// <summary>
    /// Ruta efectiva resolviendo a la predeterminada si ModelsDirectory no está configurado.
    /// </summary>
    public string EffectiveModelsDirectory =>
        !string.IsNullOrWhiteSpace(ModelsDirectory) ? ModelsDirectory : DefaultModelsDirectory;

    /// <summary>
    /// Indica si el usuario ha configurado una ruta distinta a la predeterminada.
    /// </summary>
    public bool IsCustomModelsDirectory =>
        !string.IsNullOrWhiteSpace(ModelsDirectory) &&
        !string.Equals(
            Path.GetFullPath(ModelsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(DefaultModelsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
