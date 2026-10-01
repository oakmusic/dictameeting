using DictaMeeting.Audio.Processing;
using Xunit;

namespace DictaMeeting.Tests;

public class AudioMixerTests
{
    [Fact]
    public void MixSamples_WithBothSignalsHigh_LimitsWithoutClipping()
    {
        // Arrange
        var mixer = new AudioMixer
        {
            MicrophoneGain = 1.0f,
            SystemAudioGain = 1.0f
        };

        // Dos señales que sumadas superarían 1.0 (clipping digital severo)
        float[] mic = new[] { 0.8f, 0.9f, -0.9f };
        float[] sys = new[] { 0.7f, 0.9f, -0.9f };
        float[] output = new float[3];

        // Act
        mixer.MixSamples(mic, sys, output);

        // Assert
        for (int i = 0; i < output.Length; i++)
        {
            Assert.InRange(output[i], -1.0f, 1.0f);
        }
    }

    [Fact]
    public void MixSamples_WithOnlyMicrophone_PreservesSignalAccurately()
    {
        // Arrange
        var mixer = new AudioMixer();
        float[] mic = new[] { 0.2f, 0.4f, -0.3f };
        float[] sys = Array.Empty<float>();
        float[] output = new float[3];

        // Act
        mixer.MixSamples(mic, sys, output);

        // Assert
        Assert.Equal(0.2f, output[0], precision: 4);
        Assert.Equal(0.4f, output[1], precision: 4);
        Assert.Equal(-0.3f, output[2], precision: 4);
    }

    [Fact]
    public void CalculateLevels_ReturnsCorrectPeakAndRms()
    {
        // Arrange
        var mixer = new AudioMixer();
        float[] samples = new[] { 0.5f, -0.5f, 0.5f, -0.5f };

        // Act
        var (peak, rms) = mixer.CalculateLevels(samples);

        // Assert
        Assert.Equal(0.5f, peak, precision: 3);
        Assert.Equal(0.5f, rms, precision: 3);
    }
}
