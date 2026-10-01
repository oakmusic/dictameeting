namespace DictaMeeting.Meetings.Models;

/// <summary>
/// Define los formatos de audio admitidos por la plataforma DictaMeeting.
/// Centraliza extensiones, filtros de explorador y validación para evitar duplicaciones.
/// </summary>
public static class SupportedAudioFormats
{
    /// <summary>
    /// Lista de extensiones de archivo admitidas con punto inicial y en minúsculas.
    /// </summary>
    public static readonly string[] Extensions = new[]
    {
        ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac", ".ogg", ".mp4"
    };

    /// <summary>
    /// Cadena legible para mostrar en la interfaz de usuario.
    /// </summary>
    public static string DisplayList => "MP3, WAV, M4A, AAC, WMA, FLAC, OGG, MP4";

    /// <summary>
    /// Filtro estándar para diálogos de selección de archivos (OpenFileDialog).
    /// </summary>
    public static string FileDialogFilter =>
        "Archivos de Audio (*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.ogg;*.mp4)|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.ogg;*.mp4|Todos los archivos (*.*)|*.*";

    /// <summary>
    /// Comprueba si la ruta o extensión proporcionada corresponde a un formato de audio admitido.
    /// </summary>
    public static bool IsSupported(string? pathOrExtension)
    {
        if (string.IsNullOrWhiteSpace(pathOrExtension)) return false;

        var ext = Path.GetExtension(pathOrExtension);
        if (string.IsNullOrEmpty(ext))
        {
            ext = pathOrExtension.StartsWith('.') ? pathOrExtension : "." + pathOrExtension;
        }

        return Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }
}
