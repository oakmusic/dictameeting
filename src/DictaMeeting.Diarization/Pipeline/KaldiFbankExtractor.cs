namespace DictaMeeting.Diarization.Pipeline;

/// <summary>
/// Extractor de características Kaldi FBank (Filterbank) exacto para el modelo WeSpeaker ResNet34
/// de PyAnnote Community-1.
/// Especificación: 80 bins Mel, 25ms ventana, 10ms paso, Hamming window, escalado x32768, CMVN por canal.
/// </summary>
public sealed class KaldiFbankExtractor
{
    public const int SampleRate = 16000;
    public const int NumMelBins = 80;
    public const float FrameLengthMs = 25.0f;
    public const float FrameShiftMs = 10.0f;
    public const int FrameLengthSamples = 400; // 25ms * 16kHz
    public const int FrameShiftSamples = 160;   // 10ms * 16kHz
    public const int FftSize = 512;             // Next power of two >= 400
    public const int NumSpectrumBins = 257;      // (FftSize / 2) + 1

    private readonly float[] _window;
    private readonly float[][] _melFilterbank;
    private readonly float[] _cosTwiddle;
    private readonly float[] _sinTwiddle;

    public KaldiFbankExtractor()
    {
        // 1. Precomputar ventana Hamming de 400 puntos
        _window = new float[FrameLengthSamples];
        for (int i = 0; i < FrameLengthSamples; i++)
        {
            _window[i] = 0.54f - 0.46f * MathF.Cos(2.0f * MathF.PI * i / (FrameLengthSamples - 1));
        }

        // 2. Precomputar twiddle factors para Radix-2 FFT de 512 puntos
        _cosTwiddle = new float[FftSize / 2];
        _sinTwiddle = new float[FftSize / 2];
        for (int i = 0; i < FftSize / 2; i++)
        {
            double angle = -2.0 * Math.PI * i / FftSize;
            _cosTwiddle[i] = (float)Math.Cos(angle);
            _sinTwiddle[i] = (float)Math.Sin(angle);
        }

        // 3. Precomputar banco de filtros Mel (80 filtros triangulares, 20 Hz - 8000 Hz)
        _melFilterbank = BuildKaldiMelFilterbank(SampleRate, FftSize, NumMelBins, 20.0f, 8000.0f);
    }

    /// <summary>
    /// Extrae las características Kaldi FBank normalizadas (CMVN) de un audio mono de 16 kHz.
    /// Retorna una matriz float[num_frames, 80] row-major.
    /// Para un chunk de 10s (160.000 muestras) produce exactamente 998 frames de 80 coeficientes.
    /// </summary>
    public float[,] ComputeFbank(ReadOnlySpan<float> waveform)
    {
        int numSamples = waveform.Length;
        if (numSamples < FrameLengthSamples)
        {
            return new float[0, NumMelBins];
        }

        int numFrames = (numSamples - FrameLengthSamples) / FrameShiftSamples + 1;
        float[,] fbank = new float[numFrames, NumMelBins];

        Span<float> realBuffer = stackalloc float[FftSize];
        Span<float> imagBuffer = stackalloc float[FftSize];
        Span<float> powerSpectrum = stackalloc float[NumSpectrumBins];

        const float scale = 32768.0f; // (1 << 15) requerido por Kaldi/WeSpeaker
        const float epsilon = 1.1920929e-7f;

        for (int t = 0; t < numFrames; t++)
        {
            int startSample = t * FrameShiftSamples;

            // A. Copiar frame, aplicar escala x32768 y ventana Hamming; rellenar a 512 con ceros
            realBuffer.Clear();
            imagBuffer.Clear();

            for (int i = 0; i < FrameLengthSamples; i++)
            {
                realBuffer[i] = waveform[startSample + i] * scale * _window[i];
            }

            // B. Ejecutar FFT 512 puntos
            ComputeFft512(realBuffer, imagBuffer);

            // C. Espectro de potencia P[k] = Re[k]^2 + Im[k]^2 para k = 0..256
            for (int k = 0; k < NumSpectrumBins; k++)
            {
                float r = realBuffer[k];
                float im = imagBuffer[k];
                powerSpectrum[k] = r * r + im * im;
            }

            // D. Multiplicación por los 80 filtros Mel y logaritmo natural
            for (int m = 0; m < NumMelBins; m++)
            {
                float[] weights = _melFilterbank[m];
                float melEnergy = 0.0f;

                for (int k = 0; k < NumSpectrumBins; k++)
                {
                    float w = weights[k];
                    if (w > 0.0f)
                    {
                        melEnergy += powerSpectrum[k] * w;
                    }
                }

                fbank[t, m] = MathF.Log(MathF.Max(melEnergy, epsilon));
            }
        }

        // E. Cepstral Mean Normalization (CMVN) en el tiempo para cada canal Mel
        for (int m = 0; m < NumMelBins; m++)
        {
            float sum = 0.0f;
            for (int t = 0; t < numFrames; t++)
            {
                sum += fbank[t, m];
            }
            float mean = sum / numFrames;

            for (int t = 0; t < numFrames; t++)
            {
                fbank[t, m] -= mean;
            }
        }

        return fbank;
    }

    /// <summary>
    /// Construye el banco de 80 filtros triangulares Mel según la fórmula exacta de Kaldi.
    /// mel(f) = 1127 * ln(1 + f / 700)
    /// </summary>
    private static float[][] BuildKaldiMelFilterbank(int sampleRate, int fftSize, int numBins, float lowFreq, float highFreq)
    {
        static float HzToMel(float hz) => 1127.0f * MathF.Log(1.0f + hz / 700.0f);
        static float MelToHz(float mel) => 700.0f * (MathF.Exp(mel / 1127.0f) - 1.0f);

        float melLow = HzToMel(lowFreq);
        float melHigh = HzToMel(highFreq);
        float melDelta = (melHigh - melLow) / (numBins + 1);

        int numSpectrogramBins = (fftSize / 2) + 1;
        float[][] filters = new float[numBins][];

        for (int b = 0; b < numBins; b++)
        {
            filters[b] = new float[numSpectrogramBins];
            float leftMel = melLow + b * melDelta;
            float centerMel = melLow + (b + 1) * melDelta;
            float rightMel = melLow + (b + 2) * melDelta;

            float leftHz = MelToHz(leftMel);
            float centerHz = MelToHz(centerMel);
            float rightHz = MelToHz(rightMel);

            for (int k = 0; k < numSpectrogramBins; k++)
            {
                float freq = k * (float)sampleRate / fftSize;

                if (freq >= leftHz && freq <= centerHz)
                {
                    float denom = centerHz - leftHz;
                    filters[b][k] = denom > 1e-6f ? (freq - leftHz) / denom : 0.0f;
                }
                else if (freq > centerHz && freq <= rightHz)
                {
                    float denom = rightHz - centerHz;
                    filters[b][k] = denom > 1e-6f ? (rightHz - freq) / denom : 0.0f;
                }
                else
                {
                    filters[b][k] = 0.0f;
                }
            }
        }

        return filters;
    }

    /// <summary>
    /// Radix-2 Cooley-Tukey FFT in-place para tamaño 512.
    /// </summary>
    private void ComputeFft512(Span<float> real, Span<float> imag)
    {
        // 1. Bit-reversal permutation (512 puntos, 9 bits)
        for (int i = 1, j = 0; i < FftSize; i++)
        {
            int bit = FftSize >> 1;
            while (j >= bit)
            {
                j -= bit;
                bit >>= 1;
            }
            j += bit;

            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        // 2. Butterfly stages (9 etapas para N=512)
        for (int len = 2; len <= FftSize; len <<= 1)
        {
            int halfLen = len >> 1;
            int step = FftSize / len;

            for (int i = 0; i < FftSize; i += len)
            {
                for (int k = 0; k < halfLen; k++)
                {
                    int twiddleIdx = k * step;
                    float uReal = real[i + k];
                    float uImag = imag[i + k];

                    float vReal = real[i + k + halfLen];
                    float vImag = imag[i + k + halfLen];

                    float cosVal = _cosTwiddle[twiddleIdx];
                    float sinVal = _sinTwiddle[twiddleIdx];

                    // Twiddle product: (vReal + j*vImag) * (cosVal + j*sinVal)
                    float tReal = vReal * cosVal - vImag * sinVal;
                    float tImag = vReal * sinVal + vImag * cosVal;

                    real[i + k] = uReal + tReal;
                    imag[i + k] = uImag + tImag;

                    real[i + k + halfLen] = uReal - tReal;
                    imag[i + k + halfLen] = uImag - tImag;
                }
            }
        }
    }
}
