using DictaMeeting.Audio.Recording;
using Xunit;

namespace DictaMeeting.Tests;

public class AudioRecordingTests : IDisposable
{
    private readonly string _testOutputDir;

    public AudioRecordingTests()
    {
        _testOutputDir = Path.Combine(Path.GetTempPath(), "DictaMeeting_Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testOutputDir);
    }

    [Fact]
    public async Task StartAndStopRecording_GeneratesAudioFile()
    {
        // Arrange
        using var recorder = new AudioRecordingService();
        var destinationPath = Path.Combine(_testOutputDir, "test_meeting_audio.mp3");

        // Act
        recorder.StartRecording(destinationPath);
        Assert.True(recorder.IsRecording);

        // Escribir 1 segundo de audio (16,000 muestras a 16 kHz)
        var samples = new float[16000];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / 16000);
        }
        recorder.WriteChunk(samples);

        var finalPath = await recorder.StopRecordingAsync();
        Assert.False(recorder.IsRecording);

        // Assert
        Assert.True(File.Exists(finalPath), $"El archivo final '{finalPath}' debe existir.");
        var fileInfo = new FileInfo(finalPath);
        Assert.True(fileInfo.Length > 0, "El archivo de audio generado no debe estar vacío.");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testOutputDir))
            {
                Directory.Delete(_testOutputDir, recursive: true);
            }
        }
        catch { }
    }
}
