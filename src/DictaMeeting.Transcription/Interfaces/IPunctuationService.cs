using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Transcription.Models;

namespace DictaMeeting.Transcription.Interfaces;

/// <summary>
/// Contrato para el servicio de restauración de puntuación y capitalización
/// en el procesamiento final de transcripciones de reuniones.
/// </summary>
public interface IPunctuationService : IDisposable
{
    /// <summary>
    /// Indica si el servicio de restauración de puntuación está habilitado.
    /// Si es false, RestorePunctuationAsync devuelve los segmentos sin modificar.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Restaura la puntuación y capitalización de una colección de segmentos de transcripción,
    /// preservando estrictamente marcas de tiempo (StartTime, EndTime), interlocutores (SpeakerId,
    /// SpeakerDisplayName) e identificadores de segmento sin omitir ni duplicar palabras.
    /// </summary>
    /// <param name="segments">Segmentos originales producidos por ASR y reconciliación de diarización.</param>
    /// <param name="progress">Notificador de progreso opcional (0.0 a 1.0).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Colección de segmentos con texto puntuado y capitalizado.</returns>
    Task<IReadOnlyList<TranscriptSegment>> RestorePunctuationAsync(
        IReadOnlyList<TranscriptSegment> segments,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restaura la puntuación y capitalización de un texto plano continuo.
    /// </summary>
    /// <param name="text">Texto sin puntuación.</param>
    /// <param name="progress">Notificador de progreso opcional (0.0 a 1.0).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Texto puntuado y capitalizado.</returns>
    Task<string> RestorePunctuationAsync(
        string text,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estadísticas de la última ejecución del modelo.
    /// </summary>
    PunctuationStatistics? LastStatistics { get; }
}
