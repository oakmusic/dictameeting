using DictaMeeting.Audio.Interfaces;

namespace DictaMeeting.Audio.Processing;

/// <summary>
/// Mezclador de audio digital diseñado para preservar la inteligibilidad de la voz en reuniones.
/// Implementa suma balanceada, ganancia independiente y limitador suave (soft-knee limiter) basado en tangente hiperbólica
/// para prevenir clipping digital estricto sin introducir distorsión armónica audible severa.
/// </summary>
public class AudioMixer : IAudioMixer
{
    private const float Threshold = 0.90f;

    public float MicrophoneGain { get; set; } = 1.0f;
    public float SystemAudioGain { get; set; } = 1.0f;

    public void MixSamples(ReadOnlySpan<float> micSamples, ReadOnlySpan<float> sysSamples, Span<float> outputSamples)
    {
        var length = outputSamples.Length;
        var micLen = micSamples.Length;
        var sysLen = sysSamples.Length;

        for (int i = 0; i < length; i++)
        {
            float m = i < micLen ? micSamples[i] * MicrophoneGain : 0f;
            float s = i < sysLen ? sysSamples[i] * SystemAudioGain : 0f;

            float combined = m + s;

            // Limitador suave si excede el umbral seguro
            if (combined > Threshold)
            {
                combined = Threshold + (1f - Threshold) * MathF.Tanh((combined - Threshold) / (1f - Threshold));
            }
            else if (combined < -Threshold)
            {
                combined = -Threshold + (1f - Threshold) * MathF.Tanh((combined + Threshold) / (1f - Threshold));
            }

            outputSamples[i] = Math.Clamp(combined, -1.0f, 1.0f);
        }
    }

    public (float Peak, float Rms) CalculateLevels(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return (0f, 0f);

        float peak = 0f;
        double sumSquares = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            float absVal = MathF.Abs(samples[i]);
            if (absVal > peak) peak = absVal;
            sumSquares += samples[i] * samples[i];
        }

        float rms = MathF.Sqrt((float)(sumSquares / samples.Length));
        return (Math.Clamp(peak, 0f, 1f), Math.Clamp(rms, 0f, 1f));
    }
}
