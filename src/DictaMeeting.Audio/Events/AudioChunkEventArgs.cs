using DictaMeeting.Audio.Models;

namespace DictaMeeting.Audio.Events;

/// <summary>
/// Proporciona datos de audio PCM junto con la identificación de la fuente física dominante (Micrófono vs Sistema).
/// </summary>
public class AudioChunkEventArgs : EventArgs
{
    public byte[] PcmData { get; }
    public AudioDeviceType DominantSource { get; }
    public bool IsMicrophoneDominant => DominantSource == AudioDeviceType.Microphone;
    public float MicRms { get; }
    public float SysRms { get; }

    public AudioChunkEventArgs(byte[] pcmData, AudioDeviceType dominantSource, float micRms, float sysRms)
    {
        PcmData = pcmData;
        DominantSource = dominantSource;
        MicRms = micRms;
        SysRms = sysRms;
    }
}
