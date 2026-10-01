namespace DictaMeeting.Audio.Interfaces;

public interface IAudioRecordingService : IDisposable
{
    bool IsRecording { get; }
    string? CurrentOutputFilePath { get; }

    void StartRecording(string destinationCompressedFilePath);
    void WriteChunk(ReadOnlySpan<float> floatSamples);
    Task<string> StopRecordingAsync(CancellationToken cancellationToken = default);
}
