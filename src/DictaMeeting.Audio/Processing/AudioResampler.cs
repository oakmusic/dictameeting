namespace DictaMeeting.Audio.Processing;

/// <summary>
/// Convertidor y remuestreador de audio de alta fidelidad optimizado para voz humana.
/// Convierte cualquier formato de entrada (estéreo/mono, 44.1 kHz, 48 kHz, etc.) al formato estándar de 16,000 Hz Mono.
/// </summary>
public static class AudioResampler
{
    public const int TargetSampleRate = 16000;

    /// <summary>
    /// Convierte muestras de audio multicanal a mono promediando los canales.
    /// </summary>
    public static float[] DownmixToMono(ReadOnlySpan<float> interleavedSamples, int channels)
    {
        if (channels <= 1) return interleavedSamples.ToArray();

        int frameCount = interleavedSamples.Length / channels;
        var mono = new float[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            float sum = 0f;
            int offset = i * channels;
            for (int c = 0; c < channels; c++)
            {
                sum += interleavedSamples[offset + c];
            }
            mono[i] = sum / channels;
        }

        return mono;
    }

    /// <summary>
    /// Remuestrea una señal mono desde la frecuencia de origen a la frecuencia objetivo (16,000 Hz)
    /// mediante interpolación lineal continua con preservación de fase y banda base de voz.
    /// </summary>
    public static float[] ResampleMono(ReadOnlySpan<float> inputSamples, int sourceSampleRate, int targetSampleRate = TargetSampleRate)
    {
        if (inputSamples.IsEmpty) return Array.Empty<float>();
        if (sourceSampleRate == targetSampleRate) return inputSamples.ToArray();

        double ratio = (double)sourceSampleRate / targetSampleRate;
        int outputLength = (int)Math.Floor(inputSamples.Length / ratio);
        if (outputLength <= 0) return Array.Empty<float>();

        var output = new float[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            double srcPos = i * ratio;
            int idx0 = (int)srcPos;
            int idx1 = Math.Min(idx0 + 1, inputSamples.Length - 1);
            float frac = (float)(srcPos - idx0);

            output[i] = (1f - frac) * inputSamples[idx0] + frac * inputSamples[idx1];
        }

        return output;
    }

    /// <summary>
    /// Pipeline unificado: Convierte muestras intercaladas a 16 kHz Mono en un solo paso.
    /// </summary>
    public static float[] ConvertToStandard16kMono(ReadOnlySpan<float> interleavedSamples, int sourceSampleRate, int channels)
    {
        var mono = DownmixToMono(interleavedSamples, channels);
        return ResampleMono(mono, sourceSampleRate, TargetSampleRate);
    }

    /// <summary>
    /// Convierte muestras flotantes [-1.0, 1.0] a datos PCM de 16 bits little-endian.
    /// </summary>
    public static byte[] FloatTo16BitPcm(ReadOnlySpan<float> floatSamples)
    {
        var bytes = new byte[floatSamples.Length * 2];
        for (int i = 0; i < floatSamples.Length; i++)
        {
            float clamped = Math.Clamp(floatSamples[i], -1.0f, 1.0f);
            short val = (short)(clamped * 32767f);
            bytes[i * 2] = (byte)(val & 0xFF);
            bytes[i * 2 + 1] = (byte)((val >> 8) & 0xFF);
        }
        return bytes;
    }
}
