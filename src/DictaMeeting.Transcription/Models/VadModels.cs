using System;

namespace DictaMeeting.Transcription.Models;

/// <summary>
/// Representa un intervalo continuo donde se detectó presencia de voz activa mediante VAD.
/// </summary>
public record SpeechRegion(
    TimeSpan StartTime,
    TimeSpan EndTime,
    int StartSample,
    int EndSample,
    float AverageProbability = 1.0f)
{
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Estadísticas diagnósticas del procesamiento VAD sobre un audio completo.
/// </summary>
public record VadStatistics(
    TimeSpan TotalAudioDuration,
    TimeSpan TotalSpeechDuration,
    int SpeechRegionCount,
    TimeSpan AverageSpeechDuration,
    TimeSpan ProcessingTime)
{
    public double SpeechRatio => TotalAudioDuration.TotalSeconds > 0
        ? TotalSpeechDuration.TotalSeconds / TotalAudioDuration.TotalSeconds
        : 0.0;
}

/// <summary>
/// Tipo de evento emitido por el VAD en streaming.
/// </summary>
public enum VadEventType
{
    None,
    SpeechStart,
    SpeechEnd
}

/// <summary>
/// Resultado del análisis de un frame (32 ms / 512 samples) en el VAD streaming.
/// </summary>
public readonly record struct VadFrameResult(
    VadEventType EventType,
    float Probability,
    long SamplePosition);

/// <summary>
/// Parámetros de sensibilidad y temporización para Silero VAD en tiempo real (streaming).
/// </summary>
public record SileroStreamingVadConfig
{
    public string? ModelPath { get; init; }
    public int SampleRate { get; init; } = 16000;
    public float SpeechThreshold { get; init; } = 0.50f;
    public float NegativeSpeechThreshold { get; init; } = 0.35f;
    public TimeSpan MinSpeechDuration { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan MinSilenceDuration { get; init; } = TimeSpan.FromMilliseconds(1300);
    public TimeSpan SpeechPadDuration { get; init; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan MaxUtteranceDuration { get; init; } = TimeSpan.FromSeconds(15.0);
}

/// <summary>
/// Parámetros de precisión para Silero VAD en procesamiento final (offline).
/// </summary>
public record SileroOfflineVadConfig
{
    public string? SequenceModelPath { get; init; }
    public int SampleRate { get; init; } = 16000;
    public float SpeechThreshold { get; init; } = 0.50f;
    public float NegativeSpeechThreshold { get; init; } = 0.35f;
    public TimeSpan MinSpeechDuration { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan MinSilenceDuration { get; init; } = TimeSpan.FromMilliseconds(800);
    public TimeSpan SpeechPadDuration { get; init; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan MaxSpeechDuration { get; init; } = TimeSpan.FromSeconds(20.0);
    public int MaxFramesPerBatch { get; init; } = 512;
}
