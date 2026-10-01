namespace DictaMeeting.Audio.Models;

public enum AudioDeviceType
{
    Microphone,
    SystemLoopback
}

public class AudioDevice
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AudioDeviceType DeviceType { get; set; }
    public bool IsDefault { get; set; }

    public override string ToString() => Name;
}
