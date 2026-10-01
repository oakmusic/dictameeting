using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace DictaMeeting.Audio.Services;

public class AudioDeviceService : IAudioDeviceService
{
    private readonly ILogger<AudioDeviceService>? _logger;
    private readonly MMDeviceEnumerator _enumerator;
    private bool _disposed;

    public event EventHandler? DevicesChanged;

    public AudioDeviceService(ILogger<AudioDeviceService>? logger = null)
    {
        _logger = logger;
        _enumerator = new MMDeviceEnumerator();
    }

    public Task<IReadOnlyList<AudioDevice>> GetCaptureDevicesAsync()
    {
        return Task.Run<IReadOnlyList<AudioDevice>>(() =>
        {
            var list = new List<AudioDevice>();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var defaultDevice = GetDefaultCaptureDevice();
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

                foreach (var ep in endpoints)
                {
                    list.Add(new AudioDevice
                    {
                        Id = ep.ID,
                        Name = ep.FriendlyName,
                        DeviceType = AudioDeviceType.Microphone,
                        IsDefault = defaultDevice != null && defaultDevice.Id == ep.ID
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error al enumerar dispositivos de captura (micrófonos).");
            }

            return list;
        });
    }

    public Task<IReadOnlyList<AudioDevice>> GetRenderDevicesAsync()
    {
        return Task.Run<IReadOnlyList<AudioDevice>>(() =>
        {
            var list = new List<AudioDevice>();
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var defaultDevice = GetDefaultRenderDevice();
                var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                foreach (var ep in endpoints)
                {
                    list.Add(new AudioDevice
                    {
                        Id = ep.ID,
                        Name = ep.FriendlyName,
                        DeviceType = AudioDeviceType.SystemLoopback,
                        IsDefault = defaultDevice != null && defaultDevice.Id == ep.ID
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error al enumerar dispositivos de reproducción para loopback.");
            }

            return list;
        });
    }

    public AudioDevice? GetDefaultCaptureDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var ep = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            if (ep == null) return null;

            return new AudioDevice
            {
                Id = ep.ID,
                Name = ep.FriendlyName,
                DeviceType = AudioDeviceType.Microphone,
                IsDefault = true
            };
        }
        catch
        {
            return null;
        }
    }

    public AudioDevice? GetDefaultRenderDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var ep = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (ep == null) return null;

            return new AudioDevice
            {
                Id = ep.ID,
                Name = ep.FriendlyName,
                DeviceType = AudioDeviceType.SystemLoopback,
                IsDefault = true
            };
        }
        catch
        {
            return null;
        }
    }

    public void NotifyDevicesChanged()
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _enumerator.Dispose();
        }
        catch { }
    }
}
