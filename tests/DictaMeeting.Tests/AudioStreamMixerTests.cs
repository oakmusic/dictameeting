using DictaMeeting.Audio.Processing;
using Xunit;

namespace DictaMeeting.Tests;

public class AudioStreamMixerTests
{
    [Fact]
    public void EnqueueSamples_EmitsMixedChunksOfStandardSize()
    {
        // Arrange
        var mixer = new AudioMixer();
        var streamMixer = new AudioStreamMixer(mixer);
        var emittedChunks = new List<float[]>();

        streamMixer.MixedSamplesAvailable += (_, chunk) => emittedChunks.Add(chunk);

        // Act - Encolar 3200 muestras (2 chunks de 1600 muestras = 200 ms a 16 kHz)
        var micSamples = new float[3200];
        Array.Fill(micSamples, 0.4f);

        var sysSamples = new float[3200];
        Array.Fill(sysSamples, 0.3f);

        streamMixer.EnqueueMicrophoneSamples(micSamples);
        streamMixer.EnqueueSystemSamples(sysSamples);

        // Assert
        Assert.True(emittedChunks.Count >= 2);
        Assert.Equal(AudioStreamMixer.ChunkSize, emittedChunks[0].Length);
        // La suma debe ser aproximadamente 0.7f (dentro del umbral lineal sin clipping)
        Assert.Equal(0.7f, emittedChunks[0][0], precision: 2);
    }

    [Fact]
    public void Flush_ProcessesLeftoverSamples()
    {
        // Arrange
        var mixer = new AudioMixer();
        var streamMixer = new AudioStreamMixer(mixer);
        var emittedChunks = new List<float[]>();

        streamMixer.MixedSamplesAvailable += (_, chunk) => emittedChunks.Add(chunk);

        // Encolar solo 800 muestras (menos de 1 chunk de 1600)
        var micSamples = new float[800];
        Array.Fill(micSamples, 0.5f);

        streamMixer.EnqueueMicrophoneSamples(micSamples);
        Assert.Empty(emittedChunks); // Aún no llega al tamaño de chunk

        // Act
        streamMixer.Flush(micActive: true, sysActive: false);

        // Assert
        Assert.Single(emittedChunks);
        Assert.Equal(AudioStreamMixer.ChunkSize, emittedChunks[0].Length);
    }
}
