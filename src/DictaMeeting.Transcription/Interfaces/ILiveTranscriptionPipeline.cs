using DictaMeeting.Meetings.Models;

namespace DictaMeeting.Transcription.Interfaces;

/// <summary>
/// Contrato para el pipeline de transcripción en tiempo real progresiva.
/// Recibe audio continuo de la reunión, detecta actividad de voz (VAD),
/// divide en segmentos naturales de habla y transcribe asíncronamente en segundo plano.
/// </summary>
public interface ILiveTranscriptionPipeline : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Indica si el pipeline está actualmente procesando audio de una reunión activa.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Evento disparado cada vez que se produce y reconoce un nuevo segmento de transcripción.
    /// </summary>
    event EventHandler<TranscriptSegment>? SegmentProduced;

    /// <summary>
    /// Inicia el pipeline en segundo plano para escuchar y transcribir fragmentos de voz.
    /// </summary>
    /// <param name="startOffset">Desfase temporal relativo al inicio de la reunión.</param>
    /// <param name="cancellationToken">Token de cancelación opcional.</param>
    Task StartAsync(TimeSpan startOffset = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detiene el pipeline, procesa cualquier residuo de habla pendiente en el buffer y libera recursos activos.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Encola un fragmento de audio PCM (16 kHz, 16-bit Mono) para análisis VAD y transcripción.
    /// </summary>
    /// <param name="pcmAudioData">Bytes PCM de 16-bit a 16.000 Hz.</param>
    void EnqueueAudioChunk(byte[] pcmAudioData);

    /// <summary>
    /// Encola un fragmento de audio PCM indicando si proviene primordialmente del micrófono y los niveles RMS.
    /// </summary>
    void EnqueueAudioChunk(byte[] pcmAudioData, bool isMicrophoneDominant, float micRms, float sysRms);
}
