using DictaMeeting.App.ViewModels;

namespace DictaMeeting.App.Services;

/// <summary>
/// Contrato para el servicio de sincronización bidireccional entre la posición del reproductor de audio
/// y los segmentos de la tarjeta de transcripción de la reunión.
/// </summary>
public interface ITranscriptAudioSyncService
{
    /// <summary>
    /// Segmento de transcripción actualmente activo/resaltado, o null si no hay ninguno.
    /// </summary>
    TranscriptSegmentViewModel? ActiveSegment { get; }

    /// <summary>
    /// Configura el conjunto de segmentos de transcripción para la reunión actual.
    /// </summary>
    void SetSegments(IEnumerable<TranscriptSegmentViewModel> segments);

    /// <summary>
    /// Localiza de forma eficiente el segmento correspondiente a un timestamp de audio dado.
    /// </summary>
    TranscriptSegmentViewModel? FindSegmentAt(TimeSpan position);

    /// <summary>
    /// Obtiene el timestamp de audio inicial correspondiente a un segmento de transcripción.
    /// </summary>
    TimeSpan GetPositionForSegment(TranscriptSegmentViewModel segment);

    /// <summary>
    /// Actualiza el segmento activo en función de la posición de audio actual.
    /// </summary>
    /// <param name="position">Posición actual de audio.</param>
    /// <returns>True si el segmento activo ha cambiado (entrando en un nuevo segmento o silencio); False en caso contrario.</returns>
    bool UpdateActiveSegment(TimeSpan position);

    /// <summary>
    /// Asigna de manera explícita el segmento activo (por ejemplo, tras la interacción directa del usuario al hacer clic en una tarjeta).
    /// </summary>
    void SetActiveSegment(TranscriptSegmentViewModel? segment);

    /// <summary>
    /// Limpia el estado de sincronización y los segmentos registrados.
    /// </summary>
    void Clear();

    /// <summary>
    /// Evento disparado cuando el segmento activo cambia durante la reproducción o tras una selección.
    /// </summary>
    event EventHandler<TranscriptSegmentViewModel?>? ActiveSegmentChanged;
}
