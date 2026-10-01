using DictaMeeting.Audio.Events;
using DictaMeeting.Audio.Models;
using DictaMeeting.Audio.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class AudioCaptureMonitoringTests
{
    [Fact]
    public async Task StartAndStopMonitoring_RaisesLevelEventsAndResetsToZero()
    {
        // Arrange
        using var captureService = new MockAudioCaptureService();
        var receivedEvents = new List<AudioLevelEventArgs>();

        captureService.AudioLevelUpdated += (_, args) => receivedEvents.Add(args);

        // Act - Iniciar monitorización
        await captureService.StartMonitoringAsync();
        Assert.True(captureService.IsMonitoring);

        // Simular niveles de entrada
        captureService.SimulateAudioLevel(AudioDeviceType.Microphone, 0.75f, 0.45f);
        captureService.SimulateAudioLevel(AudioDeviceType.SystemLoopback, 0.60f, 0.35f);

        // Detener monitorización
        await captureService.StopMonitoringAsync();
        Assert.False(captureService.IsMonitoring);

        // Assert
        Assert.True(receivedEvents.Count >= 4);

        // Verificar que los últimos dos eventos restablecen los vúmetros a cero
        var lastMic = receivedEvents.Last(e => e.DeviceType == AudioDeviceType.Microphone);
        var lastSys = receivedEvents.Last(e => e.DeviceType == AudioDeviceType.SystemLoopback);

        Assert.Equal(0f, lastMic.PeakLevel);
        Assert.Equal(0f, lastSys.PeakLevel);
    }

    [Fact]
    public void ScenarioSelection_TogglesFlagsIndependently()
    {
        // Arrange
        using var captureService = new MockAudioCaptureService();

        // Escenario A: Videoconferencia (Ambos activos)
        captureService.CaptureMicrophone = true;
        captureService.CaptureSystemAudio = true;
        Assert.True(captureService.CaptureMicrophone);
        Assert.True(captureService.CaptureSystemAudio);

        // Escenario B: Presencial (Solo micrófono)
        captureService.CaptureMicrophone = true;
        captureService.CaptureSystemAudio = false;
        Assert.True(captureService.CaptureMicrophone);
        Assert.False(captureService.CaptureSystemAudio);

        // Escenario C: Solo sistema
        captureService.CaptureMicrophone = false;
        captureService.CaptureSystemAudio = true;
        Assert.False(captureService.CaptureMicrophone);
        Assert.True(captureService.CaptureSystemAudio);
    }
}
