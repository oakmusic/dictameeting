using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Models;

namespace DictaMeeting.Audio.Processing;

public record MixedAudioChunk(float[] Samples, AudioDeviceType DominantSource, float MicRms, float SysRms);

/// <summary>
/// Mezclador en tiempo real de flujos de audio independientes (micrófono y audio del sistema).
/// Sincroniza búferes continuos, aplica ganancia independiente y limitador suave para evitar clipping digital.
/// </summary>
public class AudioStreamMixer
{
    private readonly IAudioMixer _mixer;
    private readonly object _lock = new();

    private readonly List<float> _micQueue = new();
    private readonly List<float> _sysQueue = new();

    public const int ChunkSize = 1600; // 100 ms a 16,000 Hz

    public event EventHandler<float[]>? MixedSamplesAvailable;
    public event EventHandler<MixedAudioChunk>? MixedChunkAvailable;

    public AudioStreamMixer(IAudioMixer mixer)
    {
        _mixer = mixer;
    }

    public void EnqueueMicrophoneSamples(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                _micQueue.Add(samples[i]);
            }
            ProcessAvailableChunks();
        }
    }

    public void EnqueueSystemSamples(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            for (int i = 0; i < samples.Length; i++)
            {
                _sysQueue.Add(samples[i]);
            }
            ProcessAvailableChunks();
        }
    }

    public void Flush(bool micActive, bool sysActive)
    {
        lock (_lock)
        {
            // Primero procesar todos los chunks completos que haya
            while (CanProcessChunk())
            {
                EmitChunk();
            }

            // Si quedan muestras residuales, emitir un chunk final
            if (_micQueue.Count > 0 || _sysQueue.Count > 0)
            {
                EmitChunk();
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _micQueue.Clear();
            _sysQueue.Clear();
        }
    }

    private void ProcessAvailableChunks()
    {
        while (CanProcessChunk())
        {
            EmitChunk();
        }
    }

    private bool CanProcessChunk()
    {
        // Si ambas colas tienen datos suficientes para un chunk completo
        if (_micQueue.Count >= ChunkSize && _sysQueue.Count >= ChunkSize)
        {
            return true;
        }

        // Si una de las colas está acumulando mucho retraso respecto a la otra (desincronización)
        // procesamos para evitar lag de streaming
        if (_micQueue.Count >= ChunkSize * 3 || _sysQueue.Count >= ChunkSize * 3)
        {
            return true;
        }

        // Si una cola tiene datos y la otra está vacía pero solo una fuente está activa
        // (gestionado por Flush o modo exclusivo)
        return false;
    }

    private void EmitChunk()
    {
        var micChunk = new float[ChunkSize];
        var sysChunk = new float[ChunkSize];
        var outputChunk = new float[ChunkSize];

        if (_micQueue.Count > 0)
        {
            int count = Math.Min(_micQueue.Count, ChunkSize);
            _micQueue.CopyTo(0, micChunk, 0, count);
            _micQueue.RemoveRange(0, count);
        }

        if (_sysQueue.Count > 0)
        {
            int count = Math.Min(_sysQueue.Count, ChunkSize);
            _sysQueue.CopyTo(0, sysChunk, 0, count);
            _sysQueue.RemoveRange(0, count);
        }

        _mixer.MixSamples(micChunk, sysChunk, outputChunk);

        float micSum = 0f;
        for (int i = 0; i < micChunk.Length; i++) micSum += micChunk[i] * micChunk[i];
        float micRms = (float)Math.Sqrt(micSum / micChunk.Length);

        float sysSum = 0f;
        for (int i = 0; i < sysChunk.Length; i++) sysSum += sysChunk[i] * sysChunk[i];
        float sysRms = (float)Math.Sqrt(sysSum / sysChunk.Length);

        var dominant = micRms >= sysRms ? AudioDeviceType.Microphone : AudioDeviceType.SystemLoopback;

        MixedSamplesAvailable?.Invoke(this, outputChunk);
        MixedChunkAvailable?.Invoke(this, new MixedAudioChunk(outputChunk, dominant, micRms, sysRms));
    }
}
