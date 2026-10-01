using DictaMeeting.Audio.Models;

namespace DictaMeeting.Audio.Interfaces;

public interface IAudioDeviceService : IDisposable
{
    event EventHandler? DevicesChanged;

    Task<IReadOnlyList<AudioDevice>> GetCaptureDevicesAsync();
    Task<IReadOnlyList<AudioDevice>> GetRenderDevicesAsync();
    AudioDevice? GetDefaultCaptureDevice();
    AudioDevice? GetDefaultRenderDevice();
}
