using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;

namespace DictaMeeting.Transcription.Services;

/// <summary>
/// Implementación de IVoiceActivityDetector basada en Silero VAD (ONNX Runtime).
/// Soporta modo Streaming para reuniones en directo y modo Offline/Secuencial para procesamiento final.
/// </summary>
public class SileroVoiceActivityDetector : IVoiceActivityDetector
{
    private readonly ILogger<SileroVoiceActivityDetector>? _logger;
    private readonly string? _customStreamingModelPath;
    private readonly string? _customSequenceModelPath;
    private bool _disposed;

    public VadStatistics? LastStatistics { get; private set; }

    public SileroVoiceActivityDetector(
        string? streamingModelPath = null,
        string? sequenceModelPath = null,
        ILogger<SileroVoiceActivityDetector>? logger = null)
    {
        _customStreamingModelPath = streamingModelPath;
        _customSequenceModelPath = sequenceModelPath;
        _logger = logger;
    }

    public ISileroStreamingSession CreateStreamingSession(SileroStreamingVadConfig? config = null)
    {
        var effectiveConfig = config ?? new SileroStreamingVadConfig();
        var modelPath = ResolveModelPath(effectiveConfig.ModelPath ?? _customStreamingModelPath, "silero_vad.onnx");

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"No se encontró el modelo ONNX de Silero VAD en la ruta: {modelPath}");
        }

        return new SileroStreamingSession(modelPath, effectiveConfig, _logger);
    }

    public async Task<IReadOnlyList<SpeechRegion>> DetectSpeechRegionsAsync(
        string audioFilePath,
        SileroOfflineVadConfig? config = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(audioFilePath))
        {
            _logger?.LogWarning("Archivo de audio no encontrado para VAD offline: {Path}", audioFilePath);
            return Array.Empty<SpeechRegion>();
        }

        var effectiveConfig = config ?? new SileroOfflineVadConfig();

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Cargar y convertir audio a float[] mono a 16 kHz
            float[] samples = LoadAudioAsPcmFloat16k(audioFilePath);
            cancellationToken.ThrowIfCancellationRequested();

            return DetectSpeechRegions(samples, effectiveConfig, progress);
        }, cancellationToken);
    }

    public IReadOnlyList<SpeechRegion> DetectSpeechRegions(
        float[] audioSamples,
        SileroOfflineVadConfig? config = null,
        IProgress<double>? progress = null)
    {
        if (audioSamples == null || audioSamples.Length == 0)
        {
            return Array.Empty<SpeechRegion>();
        }

        var effectiveConfig = config ?? new SileroOfflineVadConfig();
        var sw = Stopwatch.StartNew();

        string sequenceModelPath = ResolveModelPath(
            effectiveConfig.SequenceModelPath ?? _customSequenceModelPath,
            "silero_vad_16k_sequence.onnx");

        IReadOnlyList<SpeechRegion> regions;

        if (File.Exists(sequenceModelPath))
        {
            regions = DetectWithSequenceModel(audioSamples, sequenceModelPath, effectiveConfig, progress);
        }
        else
        {
            _logger?.LogWarning("Modelo secuencial '{Path}' no encontrado. Utilizando fallback streaming.", sequenceModelPath);
            string streamingModelPath = ResolveModelPath(_customStreamingModelPath, "silero_vad.onnx");
            regions = DetectWithStreamingModelFallback(audioSamples, streamingModelPath, effectiveConfig, progress);
        }

        sw.Stop();

        var totalAudio = TimeSpan.FromSeconds((double)audioSamples.Length / effectiveConfig.SampleRate);
        var totalSpeech = TimeSpan.FromSeconds(regions.Sum(r => r.Duration.TotalSeconds));
        var avgDuration = regions.Count > 0
            ? TimeSpan.FromSeconds(regions.Average(r => r.Duration.TotalSeconds))
            : TimeSpan.Zero;

        LastStatistics = new VadStatistics(
            TotalAudioDuration: totalAudio,
            TotalSpeechDuration: totalSpeech,
            SpeechRegionCount: regions.Count,
            AverageSpeechDuration: avgDuration,
            ProcessingTime: sw.Elapsed);

        _logger?.LogInformation(
            "VAD Offline completado: Audio={Audio:F2}s, Voz={Speech:F2}s ({Ratio:P1}), Regiones={Count}, Media={Avg:F2}s, Tiempo VAD={Time}ms",
            totalAudio.TotalSeconds,
            totalSpeech.TotalSeconds,
            LastStatistics.SpeechRatio,
            regions.Count,
            avgDuration.TotalSeconds,
            sw.ElapsedMilliseconds);

        return regions;
    }

    private IReadOnlyList<SpeechRegion> DetectWithSequenceModel(
        float[] audio,
        string modelPath,
        SileroOfflineVadConfig config,
        IProgress<double>? progress)
    {
        const int frameSamples = 512;
        const int contextSamples = 64;
        int maxFramesPerBatch = Math.Max(1, config.MaxFramesPerBatch);

        var opts = new SessionOptions
        {
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1
        };

        using var session = new InferenceSession(modelPath, opts);

        int totalFrames = (audio.Length + frameSamples - 1) / frameSamples;
        var speechProbs = new List<float>(totalFrames);

        float[] previousContext = new float[contextSamples];
        float[] hState = new float[1 * 1 * 128];
        float[] cState = new float[1 * 1 * 128];

        for (int firstFrame = 0; firstFrame < totalFrames; firstFrame += maxFramesPerBatch)
        {
            int blockFrames = Math.Min(maxFramesPerBatch, totalFrames - firstFrame);
            float[] blockInput = new float[blockFrames * (contextSamples + frameSamples)];

            for (int f = 0; f < blockFrames; f++)
            {
                int frameIdx = firstFrame + f;
                int startSample = frameIdx * frameSamples;
                int blockOffset = f * (contextSamples + frameSamples);

                // 1. Contexto de 64 muestras
                if (f == 0)
                {
                    Array.Copy(previousContext, 0, blockInput, blockOffset, contextSamples);
                }
                else
                {
                    // Contexto del frame anterior en este mismo bloque
                    int prevFrameStart = (frameIdx - 1) * frameSamples;
                    for (int c = 0; c < contextSamples; c++)
                    {
                        int srcIdx = prevFrameStart + frameSamples - contextSamples + c;
                        blockInput[blockOffset + c] = srcIdx < audio.Length ? audio[srcIdx] : 0f;
                    }
                }

                // 2. Muestras del frame actual (512)
                for (int s = 0; s < frameSamples; s++)
                {
                    int srcIdx = startSample + s;
                    blockInput[blockOffset + contextSamples + s] = srcIdx < audio.Length ? audio[srcIdx] : 0f;
                }
            }

            // Guardar contexto final del bloque para el próximo bloque
            int lastFrameStart = (firstFrame + blockFrames - 1) * frameSamples;
            for (int c = 0; c < contextSamples; c++)
            {
                int srcIdx = lastFrameStart + frameSamples - contextSamples + c;
                previousContext[c] = srcIdx < audio.Length ? audio[srcIdx] : 0f;
            }

            // Ejecutar inferencia ONNX por lote
            var inputTensor = new DenseTensor<float>(blockInput, new[] { blockFrames, contextSamples + frameSamples });
            var hTensor = new DenseTensor<float>(hState, new[] { 1, 1, 128 });
            var cTensor = new DenseTensor<float>(cState, new[] { 1, 1, 128 });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", inputTensor),
                NamedOnnxValue.CreateFromTensor("h", hTensor),
                NamedOnnxValue.CreateFromTensor("c", cTensor)
            };

            using var results = session.Run(inputs);
            var probsTensor = results.First(r => r.Name == "speech_probs").AsTensor<float>();
            var hnTensor = results.First(r => r.Name == "hn").AsTensor<float>();
            var cnTensor = results.First(r => r.Name == "cn").AsTensor<float>();

            for (int i = 0; i < blockFrames; i++)
            {
                speechProbs.Add(probsTensor[i]);
            }

            // Actualizar estados recurrentes hn -> h, cn -> c
            for (int i = 0; i < 128; i++)
            {
                hState[i] = hnTensor[0, 0, i];
                cState[i] = cnTensor[0, 0, i];
            }

            progress?.Report(Math.Min(1.0, (double)(firstFrame + blockFrames) / totalFrames));
        }

        return ConvertProbsToSpeechRegions(speechProbs, audio.Length, config);
    }

    private IReadOnlyList<SpeechRegion> DetectWithStreamingModelFallback(
        float[] audio,
        string modelPath,
        SileroOfflineVadConfig config,
        IProgress<double>? progress)
    {
        const int frameSamples = 512;
        int totalFrames = (audio.Length + frameSamples - 1) / frameSamples;
        var speechProbs = new List<float>(totalFrames);

        using var session = CreateStreamingSession(new SileroStreamingVadConfig
        {
            ModelPath = modelPath,
            SampleRate = config.SampleRate,
            SpeechThreshold = config.SpeechThreshold,
            NegativeSpeechThreshold = config.NegativeSpeechThreshold
        });

        float[] frameBuffer = new float[frameSamples];
        for (int i = 0; i < totalFrames; i++)
        {
            int start = i * frameSamples;
            for (int s = 0; s < frameSamples; s++)
            {
                int idx = start + s;
                frameBuffer[s] = idx < audio.Length ? audio[idx] : 0f;
            }

            session.ProcessFrame(frameBuffer);
            speechProbs.Add(session.LastProbability);

            if (i % 100 == 0)
            {
                progress?.Report(Math.Min(1.0, (double)i / totalFrames));
            }
        }

        return ConvertProbsToSpeechRegions(speechProbs, audio.Length, config);
    }

    /// <summary>
    /// Algoritmo oficial de Silero VAD para convertir la secuencia de probabilidades por frame en intervalos de voz.
    /// Implementa histeresis, filtrado de duración mínima, búsqueda de cortes limpios y padding simétrico.
    /// </summary>
    public static IReadOnlyList<SpeechRegion> ConvertProbsToSpeechRegions(
        IReadOnlyList<float> speechProbs,
        int audioLengthSamples,
        SileroOfflineVadConfig config)
    {
        const int windowSizeSamples = 512;
        int samplingRate = config.SampleRate;

        int minSpeechSamples = (int)(samplingRate * config.MinSpeechDuration.TotalSeconds);
        int speechPadSamples = (int)(samplingRate * config.SpeechPadDuration.TotalSeconds);
        int maxSpeechSamples = (int)(samplingRate * config.MaxSpeechDuration.TotalSeconds) - windowSizeSamples - (2 * speechPadSamples);
        int minSilenceSamples = (int)(samplingRate * config.MinSilenceDuration.TotalSeconds);
        int minSilenceAtMaxSpeechSamples = (int)(samplingRate * 0.098); // 98 ms

        float threshold = config.SpeechThreshold;
        float negThreshold = config.NegativeSpeechThreshold;

        bool triggered = false;
        var rawSpeeches = new List<(int Start, int End, float SumProbs, int FrameCount)>();
        int currentStart = 0;
        float currentSumProbs = 0f;
        int currentFrameCount = 0;

        int tempEnd = 0;
        int prevEnd = 0;
        int nextStart = 0;
        var possibleEnds = new List<(int TempEnd, int Duration)>();

        for (int i = 0; i < speechProbs.Count; i++)
        {
            float prob = speechProbs[i];
            int curSample = i * windowSizeSamples;

            if (prob >= threshold && tempEnd > 0)
            {
                int silDur = curSample - tempEnd;
                if (silDur > minSilenceAtMaxSpeechSamples)
                {
                    possibleEnds.Add((tempEnd, silDur));
                }
                tempEnd = 0;
                if (nextStart < prevEnd)
                {
                    nextStart = curSample;
                }
            }

            if (prob >= threshold && !triggered)
            {
                triggered = true;
                currentStart = curSample;
                currentSumProbs = prob;
                currentFrameCount = 1;
                continue;
            }

            if (triggered)
            {
                currentSumProbs += prob;
                currentFrameCount++;
            }

            // Límite máximo de duración alcanzado: buscar el mejor punto de corte en silencio
            if (triggered && (curSample - currentStart > maxSpeechSamples))
            {
                if (possibleEnds.Count > 0)
                {
                    var longest = possibleEnds.OrderByDescending(x => x.Duration).First();
                    int end = longest.TempEnd;
                    rawSpeeches.Add((currentStart, end, currentSumProbs, currentFrameCount));

                    currentStart = longest.TempEnd + longest.Duration;
                    currentSumProbs = 0f;
                    currentFrameCount = 0;

                    if (currentStart >= curSample)
                    {
                        triggered = false;
                    }
                    prevEnd = nextStart = tempEnd = 0;
                    possibleEnds.Clear();
                }
                else if (prevEnd > 0)
                {
                    rawSpeeches.Add((currentStart, prevEnd, currentSumProbs, currentFrameCount));
                    currentStart = nextStart > prevEnd ? nextStart : curSample;
                    currentSumProbs = 0f;
                    currentFrameCount = 0;
                    triggered = nextStart >= prevEnd;
                    prevEnd = nextStart = tempEnd = 0;
                    possibleEnds.Clear();
                }
                else
                {
                    rawSpeeches.Add((currentStart, curSample, currentSumProbs, currentFrameCount));
                    currentStart = curSample;
                    currentSumProbs = 0f;
                    currentFrameCount = 0;
                    prevEnd = nextStart = tempEnd = 0;
                    triggered = false;
                    possibleEnds.Clear();
                    continue;
                }
            }

            // Detección de silencio mientras se está en habla
            if (prob < negThreshold && triggered)
            {
                if (tempEnd == 0)
                {
                    tempEnd = curSample;
                }
                int silDurNow = curSample - tempEnd;
                if (silDurNow > minSilenceAtMaxSpeechSamples)
                {
                    prevEnd = tempEnd;
                }

                if (silDurNow < minSilenceSamples)
                {
                    continue;
                }
                else
                {
                    int end = tempEnd;
                    if (end - currentStart > minSpeechSamples)
                    {
                        rawSpeeches.Add((currentStart, end, currentSumProbs, currentFrameCount));
                    }
                    currentStart = 0;
                    currentSumProbs = 0f;
                    currentFrameCount = 0;
                    prevEnd = nextStart = tempEnd = 0;
                    triggered = false;
                    possibleEnds.Clear();
                    continue;
                }
            }
        }

        // Caso final: habla que llega hasta el final del audio
        if (triggered && (audioLengthSamples - currentStart > minSpeechSamples))
        {
            rawSpeeches.Add((currentStart, audioLengthSamples, currentSumProbs, currentFrameCount));
        }

        if (rawSpeeches.Count == 0)
        {
            return Array.Empty<SpeechRegion>();
        }

        // Aplicar márgenes/padding alrededor de las regiones
        var padded = new List<(int Start, int End, float AvgProb)>(rawSpeeches.Count);
        for (int i = 0; i < rawSpeeches.Count; i++)
        {
            var cur = rawSpeeches[i];
            int start = cur.Start;
            int end = cur.End;
            float avgProb = cur.FrameCount > 0 ? cur.SumProbs / cur.FrameCount : 1.0f;

            if (i == 0)
            {
                start = Math.Max(0, start - speechPadSamples);
            }

            if (i != rawSpeeches.Count - 1)
            {
                int nextSpeechStart = rawSpeeches[i + 1].Start;
                int silenceDuration = nextSpeechStart - end;
                if (silenceDuration < 2 * speechPadSamples)
                {
                    end += silenceDuration / 2;
                }
                else
                {
                    end = Math.Min(audioLengthSamples, end + speechPadSamples);
                }
            }
            else
            {
                end = Math.Min(audioLengthSamples, end + speechPadSamples);
            }

            padded.Add((start, end, avgProb));
        }

        // Ajustar inicios del siguiente tramo si el padding los solapó
        for (int i = 1; i < padded.Count; i++)
        {
            var prev = padded[i - 1];
            var cur = padded[i];
            int adjustedStart = Math.Max(prev.End, Math.Max(0, rawSpeeches[i].Start - speechPadSamples));
            padded[i] = (adjustedStart, Math.Max(adjustedStart, cur.End), cur.AvgProb);
        }

        var result = new List<SpeechRegion>(padded.Count);
        foreach (var item in padded)
        {
            if (item.End <= item.Start) continue;

            result.Add(new SpeechRegion(
                StartTime: TimeSpan.FromSeconds((double)item.Start / samplingRate),
                EndTime: TimeSpan.FromSeconds((double)item.End / samplingRate),
                StartSample: item.Start,
                EndSample: item.End,
                AverageProbability: item.AvgProb));
        }

        return result;
    }

    private static float[] LoadAudioAsPcmFloat16k(string audioPath)
    {
        var tempWav = Path.Combine(Path.GetTempPath(), $"dictameeting_vad_{Guid.NewGuid():N}.wav");
        try
        {
            using (var reader = new AudioFileReader(audioPath))
            {
                var outFormat = new WaveFormat(16000, 16, 1);
                using var resampler = new MediaFoundationResampler(reader, outFormat);
                WaveFileWriter.CreateWaveFile(tempWav, resampler);
            }

            using var waveReader = new WaveFileReader(tempWav);
            byte[] buffer = new byte[waveReader.Length];
            int bytesRead = waveReader.Read(buffer, 0, buffer.Length);
            int sampleCount = bytesRead / 2;
            float[] samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short s = BitConverter.ToInt16(buffer, i * 2);
                samples[i] = s / 32768f;
            }
            return samples;
        }
        finally
        {
            if (File.Exists(tempWav))
            {
                try { File.Delete(tempWav); } catch { }
            }
        }
    }

    public static string ResolveModelPath(string? explicitPath, string defaultFileName)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "models", "silero", defaultFileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "silero", defaultFileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models", "silero", defaultFileName),
            Path.Combine(Directory.GetCurrentDirectory(), "models", "silero", defaultFileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models", "silero", defaultFileName)
        };

        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch
            {
                // Ignorar paths mal formateados
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "models", "silero", defaultFileName);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

/// <summary>
/// Sesión incremental para streaming de audio en tiempo real usando silero_vad.onnx.
/// </summary>
public class SileroStreamingSession : ISileroStreamingSession
{
    private readonly InferenceSession _session;
    private readonly SileroStreamingVadConfig _config;
    private readonly ILogger? _logger;

    private readonly float[] _state = new float[2 * 1 * 128];
    private readonly float[] _context = new float[64];
    private readonly float[] _inputBuffer = new float[64 + 512]; // 576 muestras
    private readonly long[] _sr = new long[] { 16000 };

    private bool _isSpeaking;
    private float _lastProbability;
    private long _currentSample;
    private long _candidateSilenceStart;
    private bool _disposed;

    public bool IsSpeaking => _isSpeaking;
    public float LastProbability => _lastProbability;

    public SileroStreamingSession(
        string modelPath,
        SileroStreamingVadConfig config,
        ILogger? logger = null)
    {
        _config = config;
        _logger = logger;

        var options = new SessionOptions
        {
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1
        };

        _session = new InferenceSession(modelPath, options);
    }

    public VadFrameResult ProcessFrame(ReadOnlySpan<float> frame512)
    {
        if (frame512.Length != 512)
        {
            throw modernArgumentException("El frame de audio debe contener exactamente 512 muestras float a 16 kHz.");
        }

        // Construir tensor [1, 576] = 64 contexto anterior + 512 muestras actuales
        Array.Copy(_context, 0, _inputBuffer, 0, 64);
        frame512.CopyTo(_inputBuffer.AsSpan(64, 512));

        var inputTensor = new DenseTensor<float>(_inputBuffer, new[] { 1, 576 });
        var stateTensor = new DenseTensor<float>(_state, new[] { 2, 1, 128 });
        var srTensor = new DenseTensor<long>(_sr, Array.Empty<int>());

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("state", stateTensor),
            NamedOnnxValue.CreateFromTensor("sr", srTensor)
        };

        using var results = _session.Run(inputs);
        var probTensor = results.First(r => r.Name == "output").AsTensor<float>();
        var stateOutTensor = results.First(r => r.Name == "stateN").AsTensor<float>();

        _lastProbability = probTensor[0, 0];

        // Actualizar estado recurrente y contexto
        for (int i = 0; i < 256; i++)
        {
            _state[i] = stateOutTensor.GetValue(i);
        }

        // Los últimos 64 floats del frame actual se convierten en el contexto del siguiente
        frame512.Slice(512 - 64, 64).CopyTo(_context);

        _currentSample += 512;

        int minSilenceSamples = (int)(_config.SampleRate * _config.MinSilenceDuration.TotalSeconds);
        var eventType = VadEventType.None;

        if (_lastProbability >= _config.SpeechThreshold)
        {
            _candidateSilenceStart = 0;

            if (!_isSpeaking)
            {
                _isSpeaking = true;
                eventType = VadEventType.SpeechStart;
                _logger?.LogDebug("VAD: SPEECH START (prob: {Prob:F2}, sample: {Sample})", _lastProbability, _currentSample);
            }
        }
        else if (_lastProbability < _config.NegativeSpeechThreshold && _isSpeaking)
        {
            if (_candidateSilenceStart == 0)
            {
                _candidateSilenceStart = _currentSample;
            }

            if (_currentSample - _candidateSilenceStart >= minSilenceSamples)
            {
                _isSpeaking = false;
                _candidateSilenceStart = 0;
                eventType = VadEventType.SpeechEnd;
                _logger?.LogDebug("VAD: SPEECH END (prob: {Prob:F2}, sample: {Sample})", _lastProbability, _currentSample);
            }
        }

        return new VadFrameResult(eventType, _lastProbability, _currentSample);
    }

    private static ArgumentException modernArgumentException(string message) => new(message);

    public void Reset()
    {
        Array.Clear(_state, 0, _state.Length);
        Array.Clear(_context, 0, _context.Length);
        _isSpeaking = false;
        _lastProbability = 0f;
        _currentSample = 0;
        _candidateSilenceStart = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }
}
