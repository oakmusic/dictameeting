#pragma warning disable CS0067
using DictaMeeting.Audio.Events;
using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Models;

namespace DictaMeeting.Audio.Services;

public class MockAudioCaptureService : IAudioCaptureService
{
    public bool IsCapturing { get; private set; }
    public bool IsMonitoring { get; private set; }
    public bool CaptureMicrophone { get; set; } = true;
    public bool CaptureSystemAudio { get; set; } = true;
    public bool IsMicrophoneMuted { get; set; }
    public bool IsSystemAudioMuted { get; set; }

    public AudioDevice? SelectedMicrophone { get; set; }
    public AudioDevice? SelectedSystemDevice { get; set; }

    public event EventHandler<AudioLevelEventArgs>? AudioLevelUpdated;
    public event EventHandler<byte[]>? MixedAudioChunkAvailable;
    public event EventHandler<AudioChunkEventArgs>? AudioChunkAvailable;
    public event EventHandler<string>? CaptureErrorOccurred;
    public event EventHandler<bool>? MicrophoneMuteChanged;
    public event EventHandler<bool>? SystemAudioMuteChanged;
    public event EventHandler? SpeakingWhileMutedDetected;

    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        IsMonitoring = true;
        return Task.CompletedTask;
    }

    public Task StopMonitoringAsync()
    {
        IsMonitoring = false;
        AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(AudioDeviceType.Microphone, 0f, 0f));
        AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(AudioDeviceType.SystemLoopback, 0f, 0f));
        return Task.CompletedTask;
    }

    public Task StartCaptureAsync(string outputAudioFilePath, CancellationToken cancellationToken = default)
    {
        IsCapturing = true;
        try
        {
            var dir = Path.GetDirectoryName(outputAudioFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            if (!File.Exists(outputAudioFilePath))
            {
                File.WriteAllBytes(outputAudioFilePath, new byte[100]);
            }
        }
        catch { }
        return Task.CompletedTask;
    }

    public Task StopCaptureAsync(CancellationToken cancellationToken = default)
    {
        IsCapturing = false;
        return Task.CompletedTask;
    }

    public void SimulateAudioLevel(AudioDeviceType type, float peak, float rms)
    {
        AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(type, peak, rms));
    }

    public void SimulateError(string errorMessage)
    {
        CaptureErrorOccurred?.Invoke(this, errorMessage);
    }

    public void SimulateSpeakingWhileMuted()
    {
        SpeakingWhileMutedDetected?.Invoke(this, EventArgs.Empty);
    }

    public void SimulateMicrophoneMute(bool muted)
    {
        IsMicrophoneMuted = muted;
        MicrophoneMuteChanged?.Invoke(this, muted);
    }

    public void SimulateSystemAudioMute(bool muted)
    {
        IsSystemAudioMuted = muted;
        SystemAudioMuteChanged?.Invoke(this, muted);
    }

    public void Dispose()
    {
        IsCapturing = false;
        IsMonitoring = false;
    }
}
