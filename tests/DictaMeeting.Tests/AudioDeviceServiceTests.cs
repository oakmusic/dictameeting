using DictaMeeting.Audio.Models;
using DictaMeeting.Audio.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class AudioDeviceServiceTests
{
    [Fact]
    public async Task GetCaptureDevicesAsync_DoesNotThrowAndReturnsDevices()
    {
        // Arrange
        using var service = new AudioDeviceService();

        // Act
        var devices = await service.GetCaptureDevicesAsync();

        // Assert
        Assert.NotNull(devices);
        // Si hay dispositivos físicos de captura en la máquina, deben tener tipo Microphone
        Assert.All(devices, d => Assert.Equal(AudioDeviceType.Microphone, d.DeviceType));
    }

    [Fact]
    public async Task GetRenderDevicesAsync_DoesNotThrowAndReturnsDevices()
    {
        // Arrange
        using var service = new AudioDeviceService();

        // Act
        var devices = await service.GetRenderDevicesAsync();

        // Assert
        Assert.NotNull(devices);
        Assert.All(devices, d => Assert.Equal(AudioDeviceType.SystemLoopback, d.DeviceType));
    }

    [Fact]
    public void GetDefaultDevices_DoesNotThrow()
    {
        // Arrange
        using var service = new AudioDeviceService();

        // Act
        var defaultMic = service.GetDefaultCaptureDevice();
        var defaultRender = service.GetDefaultRenderDevice();

        // Assert
        if (defaultMic != null)
        {
            Assert.True(defaultMic.IsDefault);
            Assert.Equal(AudioDeviceType.Microphone, defaultMic.DeviceType);
        }

        if (defaultRender != null)
        {
            Assert.True(defaultRender.IsDefault);
            Assert.Equal(AudioDeviceType.SystemLoopback, defaultRender.DeviceType);
        }
    }
}
