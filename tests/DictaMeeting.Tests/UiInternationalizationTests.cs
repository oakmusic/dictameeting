using System.Globalization;
using System.Resources;
using System.Xml.Linq;
using DictaMeeting.App.Services;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Meetings.Models;
using Xunit;

namespace DictaMeeting.Tests;

/// <summary>
/// Pruebas exhaustivas para la internacionalización y detección inicial de idioma en DictaMeeting (Español / English),
/// cubriendo primer arranque según idioma de Windows, persistencia, prioridad absoluta de preferencias existentes,
/// migración sin pérdida de configuración previa e independencia de transcripción.
/// </summary>
public class UiInternationalizationTests
{
    private readonly string _solutionDir;
    private readonly string _resDir;

    public UiInternationalizationTests()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "DictaMeeting.sln")))
        {
            current = Path.GetDirectoryName(current);
        }

        _solutionDir = current ?? throw new InvalidOperationException("DictaMeeting.sln no encontrado.");
        _resDir = Path.Combine(_solutionDir, "src", "DictaMeeting.App", "Resources");
    }

    [Fact]
    public void Test1_FirstRun_WindowsSpanish_SelectsSpanish()
    {
        // TEST 1 — Primera ejecución + Windows español (es-ES)
        // No existe preferencia de idioma previa -> Esperado: UiLanguage = es
        var tempFile = Path.Combine(Path.GetTempPath(), $"i18n_test_1_{Guid.NewGuid():N}.json");
        try
        {
            var service = new UserSettingsService(tempFile, systemCultureProvider: () => new CultureInfo("es-ES"));
            var settings = service.LoadSettings();

            Assert.Equal("es", settings.UiLanguage);
            // Comprobar que además se persistió inmediatamente en disco
            Assert.True(File.Exists(tempFile));

            var reloaded = service.LoadSettings();
            Assert.Equal("es", reloaded.UiLanguage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("es-MX")]
    [InlineData("es-AR")]
    [InlineData("es-CO")]
    [InlineData("es-419")]
    public void Test2_FirstRun_LatinAmericanSpanish_SelectsSpanish(string cultureCode)
    {
        // TEST 2 — Primera ejecución + español latinoamericano (es-MX, es-AR, es-CO, etc.)
        // Esperado: UiLanguage = es
        var tempFile = Path.Combine(Path.GetTempPath(), $"i18n_test_2_{Guid.NewGuid():N}.json");
        try
        {
            var service = new UserSettingsService(tempFile, systemCultureProvider: () => new CultureInfo(cultureCode));
            var settings = service.LoadSettings();

            Assert.Equal("es", settings.UiLanguage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test3_FirstRun_WindowsEnglish_SelectsEnglish()
    {
        // TEST 3 — Primera ejecución + Windows inglés (en-US)
        // Esperado: UiLanguage = en
        var tempFile = Path.Combine(Path.GetTempPath(), $"i18n_test_3_{Guid.NewGuid():N}.json");
        try
        {
            var service = new UserSettingsService(tempFile, systemCultureProvider: () => new CultureInfo("en-US"));
            var settings = service.LoadSettings();

            Assert.Equal("en", settings.UiLanguage);
            Assert.True(File.Exists(tempFile));

            var reloaded = service.LoadSettings();
            Assert.Equal("en", reloaded.UiLanguage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test4_FirstRun_WindowsFrench_SelectsEnglish()
    {
        // TEST 4 — Primera ejecución + Windows francés (fr-FR)
        // Esperado: UiLanguage = en
        var tempFile = Path.Combine(Path.GetTempPath(), $"i18n_test_4_{Guid.NewGuid():N}.json");
        try
        {
            var service = new UserSettingsService(tempFile, systemCultureProvider: () => new CultureInfo("fr-FR"));
            var settings = service.LoadSettings();

            Assert.Equal("en", settings.UiLanguage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test5_FirstRun_WindowsGerman_SelectsEnglish()
    {
        // TEST 5 — Primera ejecución + Windows alemán (de-DE)
        // Esperado: UiLanguage = en
        var tempFile = Path.Combine(Path.GetTempPath(), $"i18n_test_5_{Guid.NewGuid():N}.json");
        try
        {
            var service = new UserSettingsService(tempFile, systemCultureProvider: () => new CultureInfo("de-DE"));
            var settings = service.LoadSettings();

            Assert.Equal("en", settings.UiLanguage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test6_ExistingPreference_Spanish_OverridesWindowsEnglish()
    {
        // TEST 6 — Preferencia existente
        // Windows: en-US
        // Preferencia guardada: es
        // Esperado: UiLanguage = es (NO debe utilizar Windows para cambiarlo)
        var tempFile = Path.Combine(Path.GetTempPath(), $"i18n_test_6_{Guid.NewGuid():N}.json");
        try
        {
            // Simular preferencia ya guardada previamente en español
            var initialService = new UserSettingsService(tempFile);
            initialService.SaveSettings(new UserSettings { UiLanguage = "es" });

            // Ahora arranca la app en un Windows en-US
            var service = new UserSettingsService(tempFile, systemCultureProvider: () => new CultureInfo("en-US"));
            var settings = service.LoadSettings();

            // La preferencia guardada "es" tiene prioridad absoluta
            Assert.Equal("es", settings.UiLanguage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test7_ExistingPreference_English_OverridesWindowsSpanish()
    {
        // TEST 7 — Preferencia existente en inglés
        // Windows: es-ES
        // Preferencia guardada: en
        // Esperado: UiLanguage = en (NO debe utilizar Windows para cambiarlo)
        var tempFile = Path.Combine(Path.GetTempPath(), $"i18n_test_7_{Guid.NewGuid():N}.json");
        try
        {
            // Simular preferencia ya guardada previamente en inglés
            var initialService = new UserSettingsService(tempFile);
            initialService.SaveSettings(new UserSettings { UiLanguage = "en" });

            // Ahora arranca la app en un Windows es-ES
            var service = new UserSettingsService(tempFile, systemCultureProvider: () => new CultureInfo("es-ES"));
            var settings = service.LoadSettings();

            // La preferencia guardada "en" tiene prioridad absoluta
            Assert.Equal("en", settings.UiLanguage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test8_ManualChange_FromEnglishToSpanish_PersistsOnRestart()
    {
        // TEST 8 — Cambio manual
        // Windows: en-US
        // Primera ejecución: English
        // Usuario cambia manualmente a: Español
        // Reiniciar -> Esperado: Español
        var tempFile = Path.Combine(Path.GetTempPath(), $"i18n_test_8_{Guid.NewGuid():N}.json");
        try
        {
            // 1. Primera ejecución en Windows inglés -> English
            var service1 = new UserSettingsService(tempFile, systemCultureProvider: () => new CultureInfo("en-US"));
            var settings1 = service1.LoadSettings();
            Assert.Equal("en", settings1.UiLanguage);

            // 2. El usuario entra en Ajustes -> Sistema y selecciona Español
            settings1.UiLanguage = "es";
            service1.SaveSettings(settings1);

            // 3. Simular reinicio de la aplicación
            var service2 = new UserSettingsService(tempFile, systemCultureProvider: () => new CultureInfo("en-US"));
            var reloaded = service2.LoadSettings();

            // Esperado: DictaMeeting continúa en Español a pesar de que Windows sigue en inglés
            Assert.Equal("es", reloaded.UiLanguage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test9_SubsequentWindowsLanguageChange_DoesNotAffectSavedPreference()
    {
        // TEST 9 — Cambio posterior del idioma de Windows
        // Usuario tiene: UiLanguage = es
        // Windows cambia de: es-ES → en-US
        // Reiniciar -> Esperado: DictaMeeting continúa en Español
        var tempFile = Path.Combine(Path.GetTempPath(), $"i18n_test_9_{Guid.NewGuid():N}.json");
        try
        {
            // 1. Usuario arranca por primera vez en Windows en español
            var service1 = new UserSettingsService(tempFile, systemCultureProvider: () => new CultureInfo("es-ES"));
            var settings1 = service1.LoadSettings();
            Assert.Equal("es", settings1.UiLanguage);

            // 2. Posteriormente, el usuario cambia el idioma de Windows a inglés (o alemán)
            var service2 = new UserSettingsService(tempFile, systemCultureProvider: () => new CultureInfo("en-US"));
            var startupSettings = service2.LoadSettings();

            // Esperado: DictaMeeting continúa inmutable en Español
            Assert.Equal("es", startupSettings.UiLanguage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Test10_UiLanguage_IsCompletelyIndependentOfTranscriptionLanguage()
    {
        // TEST 10 — Independencia de configuración de reunión
        // Cambiar UiLanguage no debe modificar la configuración de idioma de transcripción.
        // Windows = English, UI = English, Transcripción = Spanish (o viceversa) debe convivir sin alteración.
        var settings = new UserSettings
        {
            UiLanguage = "en",
            SelectedLanguage = "Spanish" // Idioma de transcripción (ASR)
        };

        // Cambiar idioma de interfaz a Español
        settings.UiLanguage = "es";

        // El idioma de transcripción debe permanecer intacto
        Assert.Equal("Spanish", settings.SelectedLanguage);
        Assert.Equal("es", settings.UiLanguage);

        // Volver a inglés
        settings.UiLanguage = "en";
        Assert.Equal("Spanish", settings.SelectedLanguage);
    }

    [Fact]
    public void Test11_Migration_ExistingSettingsWithoutUiLanguage_DetectsWindowsAndPersists()
    {
        // TEST 11 — Migración de instalaciones existentes
        // Un archivo de versiones anteriores existe (tiene micrófonos, tema, etc., pero no uiLanguage).
        // Debe detectar el idioma de Windows, guardarlo y preservar todas las opciones anteriores.
        var tempFile = Path.Combine(Path.GetTempPath(), $"i18n_test_11_{Guid.NewGuid():N}.json");
        try
        {
            var legacyJson = @"{
                ""captureMicrophone"": true,
                ""selectedLiveModel"": ""Small"",
                ""selectedLanguage"": ""Spanish"",
                ""isDarkMode"": true
            }";
            File.WriteAllText(tempFile, legacyJson);

            // Supongamos que el usuario tiene Windows en inglés
            var service = new UserSettingsService(tempFile, systemCultureProvider: () => new CultureInfo("en-US"));
            var migrated = service.LoadSettings();

            Assert.Equal("en", migrated.UiLanguage);
            Assert.True(migrated.CaptureMicrophone);
            Assert.Equal("Small", migrated.SelectedLiveModel);
            Assert.Equal("Spanish", migrated.SelectedLanguage);
            Assert.True(migrated.IsDarkMode);

            // Comprobar que la nueva clave quedó grabada en el archivo JSON
            var reloaded = service.LoadSettings();
            Assert.Equal("en", reloaded.UiLanguage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("es-ES", "es")]
    [InlineData("es-MX", "es")]
    [InlineData("es-AR", "es")]
    [InlineData("es-CO", "es")]
    [InlineData("es-419", "es")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("fr-FR", "en")]
    [InlineData("de-DE", "en")]
    [InlineData("it-IT", "en")]
    [InlineData("pt-PT", "en")]
    [InlineData("eu-ES", "en")] // Euskera -> English
    [InlineData("ca-ES", "en")] // Catalán -> English
    [InlineData("gl-ES", "en")] // Gallego -> English
    public void Test12_DetectWindowsLanguage_CoversAllCulturesCorrectly(string cultureName, string expectedLanguage)
    {
        // TEST 12 — Verificación integral del detector de idioma
        var culture = new CultureInfo(cultureName);
        var detected = UserSettingsService.DetectWindowsLanguage(culture);
        Assert.Equal(expectedLanguage, detected);
    }

    [Fact]
    public void Test13_OverrideViaEnvironmentVariable_AllowsTestingWithoutChangingOs()
    {
        // TEST 13 — Vía de prueba y desarrollo manual: DICTAMEETING_OVERRIDE_WINDOWS_CULTURE
        var prevEnv = Environment.GetEnvironmentVariable("DICTAMEETING_OVERRIDE_WINDOWS_CULTURE");
        try
        {
            Environment.SetEnvironmentVariable("DICTAMEETING_OVERRIDE_WINDOWS_CULTURE", "fr-FR");
            var detectedFr = UserSettingsService.DetectWindowsLanguage();
            Assert.Equal("en", detectedFr);

            Environment.SetEnvironmentVariable("DICTAMEETING_OVERRIDE_WINDOWS_CULTURE", "es-ES");
            var detectedEs = UserSettingsService.DetectWindowsLanguage();
            Assert.Equal("es", detectedEs);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DICTAMEETING_OVERRIDE_WINDOWS_CULTURE", prevEnv);
        }
    }

    [Fact]
    public void Test14_SettingsTexts_AreTranslatedInBothLanguages()
    {
        var esDict = LoadResx(Path.Combine(_resDir, "Strings.es.resx"));
        var enDict = LoadResx(Path.Combine(_resDir, "Strings.en.resx"));

        string[] settingsKeys =
        {
            "Settings_Title",
            "Settings_Card_Models_Title",
            "Settings_Card_Models_Subtitle",
            "Settings_Card_Ai_Title",
            "Settings_Card_Ai_Subtitle",
            "Settings_Card_Vocab_Title",
            "Settings_Card_Vocab_Subtitle",
            "Settings_Card_Privacy_Title",
            "Settings_Card_Privacy_Subtitle",
            "Settings_Card_System_Title",
            "Settings_Card_System_Subtitle",
            "Settings_Card_About_Title",
            "Settings_Card_About_Subtitle",
            "Settings_Theme_Light",
            "Settings_Theme_Dark"
        };

        foreach (var key in settingsKeys)
        {
            Assert.True(esDict.ContainsKey(key), $"Clave {key} ausente en Strings.es.resx");
            Assert.True(enDict.ContainsKey(key), $"Clave {key} ausente en Strings.en.resx");
            Assert.False(string.IsNullOrWhiteSpace(esDict[key]), $"Clave {key} vacía en es");
            Assert.False(string.IsNullOrWhiteSpace(enDict[key]), $"Clave {key} vacía en en");
        }
    }

    [Fact]
    public void Test15_ModalTexts_AreTranslatedInBothLanguages()
    {
        var esDict = LoadResx(Path.Combine(_resDir, "Strings.es.resx"));
        var enDict = LoadResx(Path.Combine(_resDir, "Strings.en.resx"));

        string[] modalKeys =
        {
            "System_Modal_Title",
            "System_Section_Theme_Title",
            "System_Section_Language_Title",
            "System_Language_Spanish",
            "System_Language_English",
            "System_Language_Hint",
            "Models_Title",
            "Models_Location_Title",
            "AiConfig_Title",
            "AiConfig_Provider_Label",
            "Vocab_Title",
            "Vocab_Subtitle",
            "Privacy_Title",
            "About_Title",
            "Acta_Modal_Title",
            "AudioSettings_Title",
            "Notice_Title",
            "Onboarding_Title",
            "Migrate_Title"
        };

        foreach (var key in modalKeys)
        {
            Assert.True(esDict.ContainsKey(key), $"Clave modal {key} ausente en Strings.es.resx");
            Assert.True(enDict.ContainsKey(key), $"Clave modal {key} ausente en Strings.en.resx");
            Assert.False(string.IsNullOrWhiteSpace(esDict[key]), $"Clave {key} vacía en es");
            Assert.False(string.IsNullOrWhiteSpace(enDict[key]), $"Clave {key} vacía en en");
        }
    }

    [Fact]
    public void Test16_CompleteParity_AllKeysPresentInBothLanguages()
    {
        var esDict = LoadResx(Path.Combine(_resDir, "Strings.es.resx"));
        var enDict = LoadResx(Path.Combine(_resDir, "Strings.en.resx"));

        Assert.NotEmpty(esDict);
        Assert.NotEmpty(enDict);
        Assert.Equal(esDict.Count, enDict.Count);

        foreach (var (key, esVal) in esDict)
        {
            Assert.True(enDict.ContainsKey(key), $"La clave '{key}' está en Strings.es.resx pero falta en Strings.en.resx");
            var enVal = enDict[key];
            Assert.False(string.IsNullOrWhiteSpace(esVal), $"El valor en español para '{key}' está vacío");
            Assert.False(string.IsNullOrWhiteSpace(enVal), $"El valor en inglés para '{key}' está vacío");
        }
    }

    private static Dictionary<string, string> LoadResx(string filePath)
    {
        var dict = new Dictionary<string, string>();
        var doc = XDocument.Load(filePath);
        foreach (var data in doc.Descendants("data"))
        {
            var name = data.Attribute("name")?.Value;
            var val = data.Element("value")?.Value;
            if (!string.IsNullOrEmpty(name))
            {
                dict[name] = val ?? string.Empty;
            }
        }
        return dict;
    }
}
