using System.Globalization;
using DictaMeeting.App.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class LocalizationManagerTests
{
    [Fact]
    public void LocalizationManager_DefaultCulture_IsSpanish()
    {
        var manager = LocalizationManager.Instance;
        manager.SetLanguage("es");

        Assert.Equal("es", manager.CurrentLanguageCode);
        Assert.Equal("es", manager.CurrentCulture.TwoLetterISOLanguageName);
        Assert.Equal("DictaMeeting", manager.GetString("App_Title"));
        Assert.Equal("Sistema", manager.GetString("Settings_Card_System_Title"));
    }

    [Fact]
    public void LocalizationManager_SwitchToEnglish_UpdatesCultureAndNotifies()
    {
        var manager = LocalizationManager.Instance;
        manager.SetLanguage("es");

        bool notified = false;
        bool itemNotified = false;

        manager.PropertyChanged += (s, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName)) notified = true;
            if (e.PropertyName == "Item[]") itemNotified = true;
        };

        manager.SetLanguage("en");

        Assert.Equal("en", manager.CurrentLanguageCode);
        Assert.Equal("System", manager.GetString("Settings_Card_System_Title"));
        Assert.True(notified);
        Assert.True(itemNotified);

        // Volver a español
        manager.SetLanguage("es");
        Assert.Equal("es", manager.CurrentLanguageCode);
        Assert.Equal("Sistema", manager.GetString("Settings_Card_System_Title"));
    }

    [Fact]
    public void LocalizationManager_GetString_ReturnsFallbackForUnknownKey()
    {
        var manager = LocalizationManager.Instance;
        var result = manager.GetString("NonExistent_Test_Key_12345");
        Assert.Equal("NonExistent_Test_Key_12345", result);
    }
}
