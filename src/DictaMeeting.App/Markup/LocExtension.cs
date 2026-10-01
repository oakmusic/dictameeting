using System.Windows.Data;
using System.Windows.Markup;
using DictaMeeting.App.Services;

namespace DictaMeeting.App.Markup;

/// <summary>
/// Extensión de marcado XAML que enlaza una clave de recurso al LocalizationManager en tiempo real.
/// Uso en XAML: Text="{loc:Loc Key_Name}"
/// </summary>
[ContentProperty(nameof(Key))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return string.Empty;
        }

        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
