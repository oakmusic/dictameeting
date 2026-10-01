using DictaMeeting.Audio.Events;
using DictaMeeting.Audio.Models;

namespace DictaMeeting.Audio.Interfaces;

public interface IAudioCaptureService : IDisposable
{
    bool IsCapturing { get; }
    bool IsMonitoring { get; }
    bool CaptureMicrophone { get; set; }
    bool CaptureSystemAudio { get; set; }

    bool IsMicrophoneMuted { get; set; }
    bool IsSystemAudioMuted { get; set; }

    AudioDevice? SelectedMicrophone { get; set; }
    AudioDevice? SelectedSystemDevice { get; set; }

    event EventHandler<AudioLevelEventArgs>? AudioLevelUpdated;
    event EventHandler<byte[]>? MixedAudioChunkAvailable;
    event EventHandler<AudioChunkEventArgs>? AudioChunkAvailable;
    event EventHandler<string>? CaptureErrorOccurred;
    event EventHandler<bool>? MicrophoneMuteChanged;
    event EventHandler<bool>? SystemAudioMuteChanged;
    event EventHandler? SpeakingWhileMutedDetected;

    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    Task StopMonitoringAsync();

    Task StartCaptureAsync(string outputAudioFilePath, CancellationToken cancellationToken = default);
    Task StopCaptureAsync(CancellationToken cancellationToken = default);
}
