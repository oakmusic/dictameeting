using DictaMeeting.Diarization.Models;
using DictaMeeting.Meetings.Models;

namespace DictaMeeting.Diarization.Interfaces;

/// <summary>
/// Servicio principal de diarización de interlocutores basado en PyAnnote Community-1 (ONNX Runtime).
/// Se ejecuta ÚNICAMENTE durante el procesamiento final en segundo plano.
/// </summary>
public interface ISpeakerDiarizationService : IDisposable
{
    /// <summary>
    /// Lista de hablantes técnicos conocidos en la sesión actual (SPEAKER_00, SPEAKER_01...).
    /// </summary>
    IReadOnlyList<string> KnownSpeakers { get; }

    /// <summary>
    /// Configura la carpeta personalizada donde residen los modelos de diarización.
    /// </summary>
    void SetModelsFolder(string? folder);

    /// <summary>
    /// Reinicia el estado interno del servicio para una nueva reunión.
    /// </summary>
    void Reset();

    /// <summary>
    /// Ejecuta la diarización completa offline sobre un archivo de audio grabado (WAV, MP3, etc.).
    /// </summary>
    Task<DiarizationResult> DiarizeAudioFileAsync(
        string audioFilePath,
        DiarizationOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejecuta la diarización completa sobre un buffer de muestras de audio (PCM 16 kHz Mono).
    /// </summary>
    Task<DiarizationResult> DiarizeAsync(
        float[] samples,
        int sampleRate = 16000,
        DiarizationOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reconciliador y alineador entre la transcripción del reconocedor ASR y los segmentos de diarización exclusiva.
/// </summary>
public interface ISpeakerTranscriptAligner
{
    /// <summary>
    /// Asocia cada segmento del ASR con el hablante exclusivo correspondiente según solapamiento temporal y fronteras de frase.
    /// </summary>
    TranscriptAlignmentResult Align(
        IReadOnlyList<TranscriptSegment> asrSegments,
        DiarizationResult diarizationResult,
        IReadOnlyList<Speaker>? existingParticipants = null);
}

/// <summary>
/// Filtro de suavizado temporal para mitigar micro-glitches y fusionar pausas breves del mismo hablante.
/// </summary>
public interface ISpeakerSmoothingFilter
{
    IReadOnlyList<ExclusiveSpeakerSegment> Smooth(
        IReadOnlyList<ExclusiveSpeakerSegment> segments,
        TimeSpan minSegmentDuration,
        TimeSpan mergeGap);
}
