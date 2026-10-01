using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DictaMeeting.Meetings.Vocabulary;

/// <summary>
/// Contrato para el servicio de gestión de vocabulario, palabras clave y reemplazo fonético.
/// </summary>
public interface IVocabularyService
{
    /// <summary>
    /// Evento desencadenado cuando la colección de vocabulario ha cambiado (añadido, editado, borrado, importado).
    /// </summary>
    event EventHandler? VocabularyChanged;

    /// <summary>
    /// Obtiene todos los términos registrados.
    /// </summary>
    IReadOnlyList<VocabularyTerm> GetTerms();

    /// <summary>
    /// Obtiene una lista plana de todas las palabras oficiales registradas.
    /// </summary>
    IReadOnlyList<string> GetOfficialWords();

    /// <summary>
    /// Añade o actualiza un término oficial con sus variantes/fonéticas.
    /// </summary>
    void AddOrUpdateTerm(string word, IEnumerable<string>? aliases = null);

    /// <summary>
    /// Elimina un término por su palabra oficial o ID.
    /// </summary>
    bool RemoveTerm(string wordOrId);

    /// <summary>
    /// Elimina todos los términos de la colección.
    /// </summary>
    void ClearAll();

    /// <summary>
    /// Carga el vocabulario desde el almacenamiento local persistente (JSON).
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Guarda el vocabulario actual en el almacenamiento local persistente (JSON).
    /// </summary>
    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Genera la cadena de prompt formateada como glosario para Whisper (ej. "Glosario: Aritz.").
    /// Si no hay términos, devuelve string vacío.
    /// </summary>
    string FormatWhisperGlossaryPrompt();

    /// <summary>
    /// Genera la lista separada por comas de términos para el parámetro Hotwords de Sherpa-ONNX (Qwen3-ASR),
    /// acotada opcionalmente para no saturar el límite de tokens de prompt del scaffold.
    /// </summary>
    string FormatSherpaHotwords(int maxTerms = 20);

    /// <summary>
    /// Reemplaza en el texto transcrito cualquier variante o alias conocido por su palabra oficial,
    /// respetando límites de palabra (\b) para no corromper subcadenas.
    /// </summary>
    string ReplaceAliases(string text);

    /// <summary>
    /// Exporta el vocabulario completo a un archivo JSON en la ruta indicada.
    /// </summary>
    Task ExportToFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Importa términos desde un archivo JSON. Si merge es true, combina con los existentes evitando duplicados.
    /// </summary>
    Task ImportFromFileAsync(string filePath, bool merge = true, CancellationToken cancellationToken = default);
}
