using DictaMeeting.Audio.Models;

namespace DictaMeeting.Audio.Events;

public class AudioLevelEventArgs : EventArgs
{
    public AudioDeviceType DeviceType { get; }
    public float PeakLevel { get; } // 0.0 to 1.0
    public float RmsLevel { get; }  // 0.0 to 1.0

    public AudioLevelEventArgs(AudioDeviceType deviceType, float peakLevel, float rmsLevel)
    {
        DeviceType = deviceType;
        PeakLevel = Math.Clamp(peakLevel, 0f, 1f);
        RmsLevel = Math.Clamp(rmsLevel, 0f, 1f);
    }
}
