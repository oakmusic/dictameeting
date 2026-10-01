using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;
using DictaMeeting.Infrastructure.Persistence;

namespace DictaMeeting.App.Services;

/// <summary>
/// Gestor centralizado de localización dinámica para WPF.
/// Permite alternar entre Español e Inglés en tiempo real
/// sin reiniciar la aplicación ni recargar ventanas.
/// Inicializa la cultura según la detección de idioma del sistema operativo.
/// </summary>
public class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    public static LocalizationManager Instance => _instance.Value;

    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture = new(UserSettingsService.DetectWindowsLanguage());

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<CultureInfo>? CultureChanged;

    public LocalizationManager()
    {
        // Detectar automáticamente el nombre base exacto del manifiesto de recursos
        var asm = typeof(LocalizationManager).Assembly;
        var resourceNames = asm.GetManifestResourceNames();
        var baseName = resourceNames
            .FirstOrDefault(n => n.EndsWith(".Strings.resources", StringComparison.OrdinalIgnoreCase) ||
                                 n.Equals("Strings.resources", StringComparison.OrdinalIgnoreCase))
            ?.Replace(".resources", "") ?? "DictaMeeting.Resources.Strings";

        _resourceManager = new ResourceManager(baseName, asm);
    }

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (!Equals(_currentCulture, value))
            {
                _currentCulture = value;

                // Solo modificar CurrentUICulture para textos de la interfaz,
                // respetando formatos regionales del sistema para fechas, números y monedas.
                CultureInfo.CurrentUICulture = value;
                CultureInfo.DefaultThreadCurrentUICulture = value;

                CultureChanged?.Invoke(_currentCulture);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            }
        }
    }

    public CultureInfo CurrentUICulture
    {
        get => CurrentCulture;
        set => CurrentCulture = value;
    }

    public string CurrentLanguageCode => _currentCulture.TwoLetterISOLanguageName;

    public void SetLanguage(string languageCode)
    {
        var normalized = (languageCode ?? "es").Trim().ToLowerInvariant();
        if (normalized != "en" && normalized != "es")
        {
            normalized = "es";
        }

        CurrentCulture = new CultureInfo(normalized);
    }

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        try
        {
            var value = _resourceManager.GetString(key, _currentCulture);
            return value ?? key;
        }
        catch
        {
            return key;
        }
    }

    public string GetString(string key, params object[] args)
    {
        var raw = GetString(key);
        if (args == null || args.Length == 0) return raw;
        try
        {
            return string.Format(_currentCulture, raw, args);
        }
        catch
        {
            return raw;
        }
    }
}
