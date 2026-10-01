using DictaMeeting.Audio.Processing;
using Xunit;

namespace DictaMeeting.Tests;

public class AudioResamplerTests
{
    [Fact]
    public void DownmixToMono_AveragesStereoChannels()
    {
        // Arrange - 2 samples stereo: Left=1.0, Right=0.0; Left=0.4, Right=0.6
        float[] stereo = new[] { 1.0f, 0.0f, 0.4f, 0.6f };

        // Act
        var mono = AudioResampler.DownmixToMono(stereo, channels: 2);

        // Assert
        Assert.Equal(2, mono.Length);
        Assert.Equal(0.5f, mono[0], precision: 4);
        Assert.Equal(0.5f, mono[1], precision: 4);
    }

    [Fact]
    public void ResampleMono_From48kHzTo16kHz_ReducesSampleCountByFactorOf3()
    {
        // Arrange - 480 muestras a 48 kHz (10 ms)
        float[] samples48k = new float[480];
        for (int i = 0; i < samples48k.Length; i++)
        {
            samples48k[i] = MathF.Sin(2 * MathF.PI * 440 * i / 48000);
        }

        // Act - Remuestreo a 16 kHz
        var samples16k = AudioResampler.ResampleMono(samples48k, sourceSampleRate: 48000, targetSampleRate: 16000);

        // Assert - 10 ms a 16 kHz deben ser 160 muestras
        Assert.Equal(160, samples16k.Length);
        Assert.All(samples16k, s => Assert.InRange(s, -1.05f, 1.05f));
    }

    [Fact]
    public void FloatTo16BitPcm_EncodesCorrectLittleEndianBytes()
    {
        // Arrange
        float[] floatSamples = new[] { 0.0f, 1.0f, -1.0f };

        // Act
        byte[] pcm = AudioResampler.FloatTo16BitPcm(floatSamples);

        // Assert - 3 muestras * 2 bytes = 6 bytes
        Assert.Equal(6, pcm.Length);

        // 0.0 -> 0
        short zero = BitConverter.ToInt16(pcm, 0);
        Assert.Equal(0, zero);

        // 1.0 -> 32767
        short max = BitConverter.ToInt16(pcm, 2);
        Assert.Equal(32767, max);

        // -1.0 -> -32767
        short min = BitConverter.ToInt16(pcm, 4);
        Assert.Equal(-32767, min);
    }
}
