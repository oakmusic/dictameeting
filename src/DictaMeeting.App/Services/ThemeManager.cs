using System.Windows;
using System.Windows.Media;

namespace DictaMeeting.App.Services;

/// <summary>
/// Gestor de temas de la aplicación (Modo Claro / Modo Oscuro).
/// Permite alternar dinámicamente entre la paleta clara estilo Apple (por defecto) y oscura.
/// </summary>
public static class ThemeManager
{
    public static bool IsDarkMode { get; private set; } = false;

    public static event Action<bool>? ThemeChanged;

    public static void ApplyTheme(bool darkMode)
    {
        IsDarkMode = darkMode;
        var app = Application.Current;
        if (app != null)
        {
            if (darkMode)
            {
                // Modo Oscuro (Obsidian / Slate / Apple Dark)
                SetBrush("BgPrimaryBrush", Color.FromRgb(0x0F, 0x17, 0x2A));
                SetBrush("BgSecondaryBrush", Color.FromRgb(0x1E, 0x29, 0x3B));
                SetBrush("BgTertiaryBrush", Color.FromRgb(0x33, 0x41, 0x55));
                SetBrush("BgInputBrush", Color.FromRgb(0x16, 0x20, 0x33));
                SetBrush("BorderSubtleBrush", Color.FromRgb(0x33, 0x41, 0x55));
                SetBrush("BorderFocusBrush", Color.FromRgb(0x38, 0xBD, 0xF8));
                SetBrush("TextPrimaryBrush", Color.FromRgb(0xF8, 0xFA, 0xFC));
                SetBrush("TextSecondaryBrush", Color.FromRgb(0x94, 0xA3, 0xB8));
                SetBrush("TextMutedBrush", Color.FromRgb(0x64, 0x74, 0x8B));
                SetBrush("AccentPrimaryBrush", Color.FromRgb(0x3B, 0x82, 0xF6));
                SetBrush("AccentHoverBrush", Color.FromRgb(0x60, 0xA5, 0xFA));
                SetBrush("AccentPressedBrush", Color.FromRgb(0x25, 0x63, 0xEB));
                SetBrush("AccentLightBrush", Color.FromRgb(0x1E, 0x3A, 0x8A));
                SetBrush("BrandTitleBrush", Color.FromRgb(0xF8, 0xFA, 0xFC));
                SetBrush("BrandSubtitleBrush", Color.FromRgb(0x94, 0xA3, 0xB8));
                SetBrush("PopupBgBrush", Color.FromRgb(0x1E, 0x29, 0x3B));
                SetBrush("ListItemHoverBrush", Color.FromRgb(0x33, 0x41, 0x55));
                SetBrush("CardBgBrush", Color.FromRgb(0x1E, 0x29, 0x3B));
                SetBrush("CardItemBgBrush", Color.FromRgb(0x16, 0x20, 0x33));
                SetBrush("HeaderBgBrush", Color.FromRgb(0x13, 0x1F, 0x37));
                SetBrush("SegmentBgBrush", Color.FromRgb(0x16, 0x20, 0x33));
                SetBrush("SegmentSelectedBrush", Color.FromRgb(0x33, 0x41, 0x55));
                SetBrush("TranscriptBubbleBgBrush", Color.FromRgb(0x16, 0x20, 0x33));
                SetBrush("ModalOverlayBrush", Color.FromArgb(0xDD, 0x08, 0x0E, 0x1C));
            }
            else
            {
                // Modo Claro por Defecto (Alto contraste, sin brillos excesivos, bordes definidos)
                SetBrush("BgPrimaryBrush", Color.FromRgb(0xE6, 0xEB, 0xF2));      // Fondo lienzo pizarra suave
                SetBrush("BgSecondaryBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));    // Tarjetas blancas puras
                SetBrush("BgTertiaryBrush", Color.FromRgb(0xD9, 0xE2, 0xEC));     // Contenedores secundarios
                SetBrush("BgInputBrush", Color.FromRgb(0xF8, 0xFA, 0xFC));        // Superficie inputs
                SetBrush("BorderSubtleBrush", Color.FromRgb(0xB8, 0xC5, 0xD4));   // Bordes nítidos y visibles
                SetBrush("BorderFocusBrush", Color.FromRgb(0x00, 0x71, 0xE3));    // Azul Apple
                SetBrush("TextPrimaryBrush", Color.FromRgb(0x0F, 0x17, 0x2A));    // Texto principal nítido
                SetBrush("TextSecondaryBrush", Color.FromRgb(0x33, 0x41, 0x55));  // Texto secundario
                SetBrush("TextMutedBrush", Color.FromRgb(0x64, 0x74, 0x8B));      // Texto atenuado legible
                SetBrush("AccentPrimaryBrush", Color.FromRgb(0x00, 0x71, 0xE3));  // Azul Apple
                SetBrush("AccentHoverBrush", Color.FromRgb(0x00, 0x77, 0xED));
                SetBrush("AccentPressedBrush", Color.FromRgb(0x00, 0x5B, 0xB5));
                SetBrush("AccentLightBrush", Color.FromRgb(0xEB, 0xF5, 0xFF));
                SetBrush("BrandTitleBrush", Color.FromRgb(0x00, 0x7A, 0xFF));     // Azul principal DictaMeeting
                SetBrush("BrandSubtitleBrush", Color.FromRgb(0x6B, 0x72, 0x80));  // Gris secundaria byAritz
                SetBrush("PopupBgBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
                SetBrush("ListItemHoverBrush", Color.FromRgb(0xEA, 0xF0, 0xF7));
                SetBrush("CardBgBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
                SetBrush("CardItemBgBrush", Color.FromRgb(0xF4, 0xF7, 0xFA));     // Sub-tarjetas con contraste
                SetBrush("HeaderBgBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
                SetBrush("SegmentBgBrush", Color.FromRgb(0xD4, 0xDE, 0xEB));      // Contenedor segmentado
                SetBrush("SegmentSelectedBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
                SetBrush("TranscriptBubbleBgBrush", Color.FromRgb(0xF1, 0xF5, 0xF9)); // Bocadillos contrastados
                SetBrush("ModalOverlayBrush", Color.FromArgb(0xAA, 0x0F, 0x17, 0x2A));
            }
        }

        ThemeChanged?.Invoke(darkMode);
    }

    private static void SetBrush(string key, Color color)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush)
        {
            if (brush.IsFrozen)
            {
                Application.Current.Resources[key] = new SolidColorBrush(color);
            }
            else
            {
                brush.Color = color;
            }
        }
        else
        {
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
    }
}
