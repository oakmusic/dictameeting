using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictaMeeting.Transcription.Models;

namespace DictaMeeting.Transcription.Interfaces;

/// <summary>
/// Sesión incremental para análisis de voz en streaming con Silero VAD.
/// </summary>
public interface ISileroStreamingSession : IDisposable
{
    /// <summary>
    /// Procesa exactamente un frame de 512 muestras float32 normalizadas [-1, 1] a 16 kHz.
    /// </summary>
    VadFrameResult ProcessFrame(ReadOnlySpan<float> frame512);

    /// <summary>
    /// Reinicia los estados internos recurrentes y contadores de la sesión.
    /// </summary>
    void Reset();

    /// <summary>
    /// Indica si el detector se encuentra actualmente en un intervalo de voz activa.
    /// </summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Última probabilidad de voz calculada [0.0 - 1.0].
    /// </summary>
    float LastProbability { get; }
}

/// <summary>
/// Detector de actividad de voz (VAD) basado en redes neuronales ONNX Silero VAD.
/// Soporta modo Streaming en tiempo real y modo Offline secuencial de alta precisión.
/// </summary>
public interface IVoiceActivityDetector : IDisposable
{
    /// <summary>
    /// Crea una nueva sesión streaming para procesamiento en tiempo real durante la reunión.
    /// </summary>
    ISileroStreamingSession CreateStreamingSession(SileroStreamingVadConfig? config = null);

    /// <summary>
    /// Detecta regiones de voz precisas sobre un archivo de audio completo (modo offline).
    /// </summary>
    Task<IReadOnlyList<SpeechRegion>> DetectSpeechRegionsAsync(
        string audioFilePath,
        SileroOfflineVadConfig? config = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detecta regiones de voz sobre un array de muestras PCM float32 normalizadas a 16 kHz (modo offline).
    /// </summary>
    IReadOnlyList<SpeechRegion> DetectSpeechRegions(
        float[] audioSamples,
        SileroOfflineVadConfig? config = null,
        IProgress<double>? progress = null);

    /// <summary>
    /// Últimas estadísticas diagnósticas registradas tras un procesamiento offline.
    /// </summary>
    VadStatistics? LastStatistics { get; }
}
