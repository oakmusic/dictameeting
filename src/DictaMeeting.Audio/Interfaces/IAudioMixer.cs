namespace DictaMeeting.Audio.Interfaces;

public interface IAudioMixer
{
    float MicrophoneGain { get; set; }
    float SystemAudioGain { get; set; }

    /// <summary>
    /// Mezcla dos buffers de muestras de audio en punto flotante [-1.0, 1.0] aplicando ganancia,
    /// prevención de clipping suave (soft-knee limiter) y nivelación de volumen.
    /// </summary>
    void MixSamples(ReadOnlySpan<float> micSamples, ReadOnlySpan<float> sysSamples, Span<float> outputSamples);

    /// <summary>
    /// Calcula el nivel de pico (Peak) y el valor eficaz (RMS) para un conjunto de muestras.
    /// </summary>
    (float Peak, float Rms) CalculateLevels(ReadOnlySpan<float> samples);
}
