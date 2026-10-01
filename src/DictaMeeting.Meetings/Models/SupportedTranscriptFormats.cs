namespace DictaMeeting.Meetings.Models;

/// <summary>
/// Define los formatos de texto plano admitidos para la importación de transcripciones.
/// </summary>
public static class SupportedTranscriptFormats
{
    private static readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
        ".markdown",
        ".text",
        ".log",
        ".rst",
        ".org",
        ".adoc",
        ".asciidoc",
        ".csv",   // Permite importar exports con separadores simples
        ".tsv",
        ".srt",   // Subtítulos SRT (formato muy común para transcripciones con timestamps)
        ".vtt",   // WebVTT (subtítulos web)
    };

    /// <summary>Devuelve true si la extensión o ruta de archivo pertenece a un formato de texto admitido.</summary>
    public static bool IsSupported(string? pathOrExtension)
    {
        if (string.IsNullOrWhiteSpace(pathOrExtension))
            return false;

        var ext = Path.GetExtension(pathOrExtension);
        if (string.IsNullOrEmpty(ext))
        {
            ext = pathOrExtension.StartsWith('.') ? pathOrExtension : "." + pathOrExtension;
        }
        return _extensions.Contains(ext);
    }

    /// <summary>Filtro para el diálogo OpenFileDialog.</summary>
    public static string FileDialogFilter =>
        "Archivos de texto|*.txt;*.md;*.markdown;*.text;*.log;*.rst;*.org;*.adoc;*.asciidoc;*.srt;*.vtt;*.csv;*.tsv|" +
        "Texto plano (*.txt)|*.txt|" +
        "Markdown (*.md;*.markdown)|*.md;*.markdown|" +
        "Subtítulos SRT (*.srt)|*.srt|" +
        "WebVTT (*.vtt)|*.vtt|" +
        "Todos los archivos|*.*";

    /// <summary>Lista de extensiones principales para mostrar en UI.</summary>
    public static string DisplayList => "TXT, MD, SRT, VTT, LOG, RST, ORG";
}
