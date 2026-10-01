using System;
using System.Collections.Generic;

namespace DictaMeeting.Meetings.Vocabulary;

/// <summary>
/// Representa un término de vocabulario oficial con sus variantes fonéticas o alias conocidos.
/// </summary>
public class VocabularyTerm
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Palabra o acrónimo oficial tal como debe aparecer en la transcripción y resúmenes (ej. "Aritz").
    /// </summary>
    public string Word { get; set; } = string.Empty;

    /// <summary>
    /// Lista de variantes fonéticas o transcripciones erróneas típicas que deben ser reemplazadas por la palabra oficial (ej. ["Arich"]).
    /// </summary>
    public List<string> Aliases { get; set; } = new();

    /// <summary>
    /// Fecha de creación del término.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// Contenedor serializable para persistencia e importación/exportación de vocabulario en formato JSON.
/// </summary>
public class VocabularyData
{
    public int Version { get; set; } = 1;
    public List<VocabularyTerm> Terms { get; set; } = new();
}
