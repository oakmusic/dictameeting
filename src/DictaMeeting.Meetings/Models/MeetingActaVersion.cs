using DictaMeeting.Meetings.Enums;

namespace DictaMeeting.Meetings.Models;

/// <summary>
/// Representa una versión individual persistida de un acta de reunión generada o editada.
/// </summary>
public class MeetingActaVersion
{
    /// <summary>
    /// Identificador único de la versión.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Identificador de la reunión a la que pertenece esta versión.
    /// </summary>
    public string MeetingId { get; set; } = string.Empty;

    /// <summary>
    /// Nombre de archivo único dentro de la subcarpeta 'actas/'.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Idioma con el que fue generada el acta.
    /// </summary>
    public LanguageMode Language { get; set; } = LanguageMode.Spanish;

    /// <summary>
    /// Nivel de detalle solicitado en la generación.
    /// </summary>
    public ActaDetailLevel DetailLevel { get; set; } = ActaDetailLevel.Normal;

    /// <summary>
    /// Fecha y hora exacta de generación.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// Fecha y hora de la última modificación manual, si se ha editado.
    /// </summary>
    public DateTimeOffset? LastModifiedAt { get; set; }

    /// <summary>
    /// Modelo de IA utilizado (por ejemplo, 'anthropic/claude-3.5-sonnet' o 'Servidor personalizado').
    /// </summary>
    public string? ModelUsed { get; set; }

    /// <summary>
    /// Contenido Markdown del acta en su estado actual (o modificado).
    /// </summary>
    public string MarkdownContent { get; set; } = string.Empty;

    /// <summary>
    /// Copia recuperable del contenido original generado por la IA antes de cualquier edición manual.
    /// </summary>
    public string? OriginalAiMarkdownContent { get; set; }

    /// <summary>
    /// Indica si esta versión procede de la migración de un acta previa no versionada.
    /// </summary>
    public bool IsLegacyMigrated { get; set; }
}

/// <summary>
/// Contenedor serializable para el archivo index.json dentro de la carpeta actas/.
/// </summary>
public class MeetingActaIndex
{
    public string MeetingId { get; set; } = string.Empty;
    public string? ActiveVersionId { get; set; }
    public List<MeetingActaVersionMetadata> Versions { get; set; } = new();
}

/// <summary>
/// Metadatos compactos de una versión para almacenar en index.json.
/// </summary>
public class MeetingActaVersionMetadata
{
    public string Id { get; set; } = string.Empty;
    public string MeetingId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public LanguageMode Language { get; set; } = LanguageMode.Spanish;
    public ActaDetailLevel DetailLevel { get; set; } = ActaDetailLevel.Normal;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }
    public string? ModelUsed { get; set; }
    public bool IsLegacyMigrated { get; set; }
    public string? OriginalAiMarkdownContent { get; set; }
}
