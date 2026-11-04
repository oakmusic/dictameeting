using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictaMeeting.Diarization.Interfaces;
using DictaMeeting.Diarization.Models;
using DictaMeeting.Diarization.Pipeline;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace DictaMeeting.Diarization.Services;

/// <summary>
/// Servicio de Diarización de Interlocutores PyAnnote Community-1 (C# / .NET / ONNX Runtime).
/// Sustituye por completo el antiguo AcousticFeatureDiarizationService.
/// No utiliza Python ni procesos externos. Funciona 100% offline tras la descarga de modelos.
/// </summary>
public sealed class PyAnnoteCommunity1DiarizationService : ISpeakerDiarizationService
{
    private string? _customModelsFolder;
    private readonly ILogger<PyAnnoteCommunity1DiarizationService>? _logger;
    private readonly object _lock = new();

    private Community1DiarizationPipeline? _pipeline;
    private readonly List<string> _knownSpeakers = new();
    private int _activeInferences;
    private bool _disposed;

    public IReadOnlyList<string> KnownSpeakers
    {
        get
        {
            lock (_lock)
            {
                return _knownSpeakers.ToList();
            }
        }
    }

    public PyAnnoteCommunity1DiarizationService()
        : this(customModelsFolder: null, logger: null)
    {
    }

    public PyAnnoteCommunity1DiarizationService(ILogger<PyAnnoteCommunity1DiarizationService>? logger)
        : this(customModelsFolder: null, logger)
    {
    }

    public PyAnnoteCommunity1DiarizationService(
        string? customModelsFolder,
        ILogger<PyAnnoteCommunity1DiarizationService>? logger = null)
    {
        _customModelsFolder = customModelsFolder;
        _logger = logger;
    }

    public void SetModelsFolder(string? folder)
    {
        _customModelsFolder = !string.IsNullOrWhiteSpace(folder) ? Path.GetFullPath(folder) : null;
        UnloadPipeline();
    }

    public void Reset()
    {
        lock (_lock)
        {
            _knownSpeakers.Clear();
        }
    }

    /// <summary>
    /// Libera las sesiones ONNX del pipeline de diarización si no hay inferencias en curso.
    /// Permite recuperar memoria nativa tras finalizar una diarización pesada.
    /// </summary>
    public void UnloadPipeline()
    {
        lock (_lock)
        {
            if (_activeInferences > 0)
            {
                _logger?.LogDebug("No se puede descargar el pipeline de diarización: hay {Count} inferencia(s) en curso.", _activeInferences);
                return;
            }

            if (_pipeline != null)
            {
                _logger?.LogInformation("Descargando pipeline de diarización PyAnnote Community-1 para liberar recursos de memoria.");
                try
                {
                    _pipeline.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Excepción menor al liberar pipeline de diarización.");
                }
                finally
                {
                    _pipeline = null;
                }
            }
        }
    }

    public async Task<DiarizationResult> DiarizeAudioFileAsync(
        string audioFilePath,
        DiarizationOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath) || !File.Exists(audioFilePath))
        {
            throw new FileNotFoundException($"Archivo de audio para diarización no encontrado: {audioFilePath}");
        }

        progress?.Report(0.02);

        // 1. Decodificar y remuestrear audio a 16 kHz Mono float[] en memoria
        var loadAudioSw = System.Diagnostics.Stopwatch.StartNew();
        float[] samples = await Task.Run(() => LoadAudioAs16kMono(audioFilePath), cancellationToken);
        loadAudioSw.Stop();

        cancellationToken.ThrowIfCancellationRequested();

        // 2. Ejecutar Diarización
        var result = await DiarizeAsync(samples, 16000, options, progress, cancellationToken);
        result.Timings.AudioLoadingTime = loadAudioSw.Elapsed;
        return result;
    }

    public Task<DiarizationResult> DiarizeAsync(
        float[] samples,
        int sampleRate = 16000,
        DiarizationOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            float[] resampled = samples;
            if (sampleRate != 16000)
            {
                resampled = ResampleLinear(samples, sampleRate, 16000);
            }

            bool wasCold;
            TimeSpan initTime;
            Community1DiarizationPipeline pipeline;

            lock (_lock)
            {
                (wasCold, initTime) = EnsurePipelineInitialized(options?.PreferredExecutionProvider);
                pipeline = _pipeline!;
                _activeInferences++;
            }

            DiarizationResult result;
            try
            {
                result = pipeline.ProcessAudio(resampled, options, progress, cancellationToken);
            }
            finally
            {
                lock (_lock)
                {
                    _activeInferences--;
                }
            }

            result.Timings.IsWarmRun = !wasCold;
            result.Timings.ModelInitializationTime = initTime;

            lock (_lock)
            {
                _knownSpeakers.Clear();
                for (int i = 0; i < result.DetectedSpeakerCount; i++)
                {
                    _knownSpeakers.Add(SpeakerId.FromIndex(i));
                }
            }

            return result;
        }, cancellationToken);
    }

    private (bool WasColdStart, TimeSpan InitTime) EnsurePipelineInitialized(string? preferredProvider)
    {
        if (_pipeline != null) return (false, TimeSpan.Zero);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string modelsDir = ResolveModelsDirectory();
        string segPath = Path.Combine(modelsDir, "community1-segmentation.onnx");
        string embPath = Path.Combine(modelsDir, "community1-embedding.onnx");
        string pldaPath = Path.Combine(modelsDir, "plda_community1.bin");

        _logger?.LogInformation("Inicializando PyAnnote Community-1 Pipeline desde: {Dir}", modelsDir);
        _pipeline = new Community1DiarizationPipeline(segPath, embPath, pldaPath, preferredProvider, _logger);
        sw.Stop();
        return (true, sw.Elapsed);
    }

    private string ResolveModelsDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_customModelsFolder) && Directory.Exists(_customModelsFolder))
        {
            return _customModelsFolder;
        }

        var candidateFolders = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "models", "diarization"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "diarization"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models", "diarization"),
            Path.Combine(Directory.GetCurrentDirectory(), "models", "diarization"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models", "diarization"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "models", "diarization")
        };

        foreach (var folder in candidateFolders)
        {
            try
            {
                var full = Path.GetFullPath(folder);
                if (Directory.Exists(full) &&
                    File.Exists(Path.Combine(full, "community1-segmentation.onnx")) &&
                    File.Exists(Path.Combine(full, "community1-embedding.onnx")) &&
                    File.Exists(Path.Combine(full, "plda_community1.bin")))
                {
                    return full;
                }
            }
            catch { }
        }

        // Si no existen los 3 archivos, retornar la ruta estándar de AppData o BaseDirectory
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models", "diarization");
    }

    private static float[] LoadAudioAs16kMono(string audioFilePath)
    {
        using var reader = new AudioFileReader(audioFilePath);
        int channels = reader.WaveFormat.Channels;
        int sampleRate = reader.WaveFormat.SampleRate;
        int bitsPerSample = reader.WaveFormat.BitsPerSample;

        // Estimar el número de muestras mono esperadas para evitar la duplicación de pico de List<float>.ToArray()
        long totalInputSamples = bitsPerSample > 0
            ? reader.Length / (bitsPerSample / 8)
            : 0L;
        long estimatedMonoSamples = channels > 0 && totalInputSamples > 0
            ? totalInputSamples / channels
            : 0L;

        float[] result;
        int writePos = 0;
        float[] buffer = new float[16384];
        ISampleProvider sampleProvider = reader;
        int read;

        if (estimatedMonoSamples > 0 && estimatedMonoSamples <= int.MaxValue)
        {
            // Asignación inicial con el tamaño estimado (evita reasignaciones para WAV)
            result = new float[(int)estimatedMonoSamples];

            while ((read = sampleProvider.Read(buffer.AsSpan())) > 0)
            {
                if (channels == 1)
                {
                    if (writePos + read > result.Length)
                    {
                        Array.Resize(ref result, Math.Max(result.Length * 2, writePos + read));
                    }
                    buffer.AsSpan(0, read).CopyTo(result.AsSpan(writePos));
                    writePos += read;
                }
                else
                {
                    int framesInRead = (read + channels - 1) / channels;
                    if (writePos + framesInRead > result.Length)
                    {
                        Array.Resize(ref result, Math.Max(result.Length * 2, writePos + framesInRead));
                    }

                    for (int i = 0; i < read && writePos < result.Length; i += channels)
                    {
                        float sum = 0f;
                        int chCount = Math.Min(channels, read - i);
                        for (int ch = 0; ch < chCount; ch++)
                        {
                            sum += buffer[i + ch];
                        }
                        result[writePos++] = sum / chCount;
                    }
                }
            }

            if (writePos < result.Length)
            {
                Array.Resize(ref result, writePos);
            }
        }
        else
        {
            // Fallback dinámico si no se pudo estimar la longitud
            var sampleList = new List<float>();
            while ((read = sampleProvider.Read(buffer.AsSpan())) > 0)
            {
                if (channels == 1)
                {
                    for (int i = 0; i < read; i++) sampleList.Add(buffer[i]);
                }
                else
                {
                    for (int i = 0; i < read; i += channels)
                    {
                        float sum = 0f;
                        int count = Math.Min(channels, read - i);
                        for (int ch = 0; ch < count; ch++) sum += buffer[i + ch];
                        sampleList.Add(sum / count);
                    }
                }
            }
            result = sampleList.ToArray();
        }

        if (sampleRate != 16000)
        {
            result = ResampleLinear(result, sampleRate, 16000);
        }

        return result;
    }

    private static float[] ResampleLinear(float[] input, int inRate, int outRate)
    {
        if (inRate == outRate) return input;
        double ratio = (double)inRate / outRate;
        int outLength = (int)(input.Length / ratio);
        float[] output = new float[outLength];

        for (int i = 0; i < outLength; i++)
        {
            double srcIdx = i * ratio;
            int idx0 = (int)srcIdx;
            int idx1 = Math.Min(idx0 + 1, input.Length - 1);
            double frac = srcIdx - idx0;
            output[i] = (float)((1.0 - frac) * input[idx0] + frac * input[idx1]);
        }

        return output;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnloadPipeline();
    }
}