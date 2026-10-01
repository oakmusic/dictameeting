using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.Infrastructure.Persistence;

/// <summary>
/// Implementación basada en archivo JSON local en AppData para la configuración de usuario.
/// Gestiona la detección inicial del idioma de Windows en el primer arranque y la persistencia
/// inmutable de las preferencias seleccionadas por el usuario.
/// </summary>
public class UserSettingsService : IUserSettingsService
{
    private readonly string _settingsFilePath;
    private readonly ILogger<UserSettingsService>? _logger;
    private readonly Func<CultureInfo>? _systemCultureProvider;
    private readonly object _fileLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public UserSettingsService(
        string? customFilePath = null,
        ILogger<UserSettingsService>? logger = null,
        Func<CultureInfo>? systemCultureProvider = null)
    {
        _logger = logger;
        _systemCultureProvider = systemCultureProvider;

        if (!string.IsNullOrWhiteSpace(customFilePath))
        {
            _settingsFilePath = customFilePath;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(localAppData, "DictaMeeting");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            _settingsFilePath = Path.Combine(folder, "user_settings.json");
        }
    }

    /// <summary>
    /// Detecta el idioma inicial según la cultura de interfaz de Windows.
    /// Reglas:
    /// - Cualquier variante de español ("es", "es-ES", "es-MX", "es-AR", etc.) -> "es" (Español).
    /// - Cualquier otro idioma ("en-US", "fr-FR", "de-DE", "it-IT", "pt-PT", "eu-ES", etc.) -> "en" (English).
    /// </summary>
    public static string DetectWindowsLanguage(CultureInfo? culture = null)
    {
        var targetCulture = culture ?? GetEffectiveWindowsCulture();
        return string.Equals(targetCulture.TwoLetterISOLanguageName, "es", StringComparison.OrdinalIgnoreCase)
            ? "es"
            : "en";
    }

    private static CultureInfo GetEffectiveWindowsCulture()
    {
        // Vía de depuración / prueba manual durante desarrollo sin alterar la configuración del SO
        var overrideEnv = Environment.GetEnvironmentVariable("DICTAMEETING_OVERRIDE_WINDOWS_CULTURE");
        if (!string.IsNullOrWhiteSpace(overrideEnv))
        {
            try
            {
                return CultureInfo.GetCultureInfo(overrideEnv.Trim());
            }
            catch
            {
                // Ignorar valor no válido y continuar con la cultura real
            }
        }

        return CultureInfo.CurrentUICulture;
    }

    private string GetInitialLanguage()
    {
        var culture = _systemCultureProvider?.Invoke();
        return DetectWindowsLanguage(culture);
    }

    public UserSettings LoadSettings()
    {
        lock (_fileLock)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        // Si ya existe una preferencia explícita y válida de idioma, se respeta sin consultar Windows
                        if (!string.IsNullOrWhiteSpace(settings.UiLanguage))
                        {
                            return settings;
                        }

                        // Migración desde una versión previa sin UiLanguage guardado:
                        // Se detecta el idioma de Windows y se persiste de inmediato.
                        settings.UiLanguage = GetInitialLanguage();
                        SaveSettingsInternal(settings);
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "No se pudo leer el archivo de configuración '{FilePath}'. Se usarán valores por defecto.", _settingsFilePath);
            }

            // Primer arranque (no existe el archivo o falló la lectura):
            // Se detecta el idioma de Windows y se persiste como preferencia inicial.
            var newSettings = new UserSettings
            {
                UiLanguage = GetInitialLanguage()
            };
            SaveSettingsInternal(newSettings);
            return newSettings;
        }
    }

    public void SaveSettings(UserSettings settings)
    {
        lock (_fileLock)
        {
            SaveSettingsInternal(settings);
        }
    }

    private void SaveSettingsInternal(UserSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
            _logger?.LogDebug("Configuración de usuario guardada con éxito en '{FilePath}'.", _settingsFilePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al guardar la configuración de usuario en '{FilePath}'.", _settingsFilePath);
        }
    }
}
