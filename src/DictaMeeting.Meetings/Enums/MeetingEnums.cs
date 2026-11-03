namespace DictaMeeting.Meetings.Enums;

public enum MeetingState
{
    Idle,
    Preparing,
    Recording,
    Processing,
    Finalizing,
    Completed,
    Error
}

public enum ActaDetailLevel
{
    Breve,
    Normal,
    Detallada,
    Exhaustiva
}

public enum LanguageMode
{
    Auto,
    Spanish,
    English
}

public enum ProcessingStatus
{
    Pending,
    Processing,
    Completed,
    Cancelled,
    Error
}

/// <summary>
/// Estados específicos del ciclo de vida del reconocimiento automático del habla (ASR).
/// </summary>
public enum TranscriptionStatus
{
    None,            // Sin transcripción
    Transcribing,    // Transcribiendo
    Completed,       // Transcripción completada
    Error            // Error de transcripción
}

/// <summary>
/// Estados específicos del ciclo de vida de la separación de interlocutores (Diarización).
/// </summary>
public enum DiarizationStatus
{
    None,            // Sin diarización
    Pending,         // Diarización pendiente
    Diarizing,       // Diarizando
    Completed,       // Diarización completada
    Error            // Error de diarización
}

/// <summary>
/// Modalidad de procesamiento para la reunión o reprocesamiento de audio.
/// </summary>
public enum ProcessingMode
{
    TranscriptionOnly,          // Solo transcripción
    DiarizationOnly,            // Solo diarización
    TranscriptionAndDiarization // Transcripción + diarización
}
