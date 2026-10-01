using System.IO;
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
        lock (_lock)
        {
            _pipeline?.Dispose();
            _pipeline = null;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _knownSpeakers.Clear();
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
        float[] samples = await Task.Run(() => LoadAudioAs16kMono(audioFilePath), cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        // 2. Ejecutar Diarización
        return await DiarizeAsync(samples, 16000, options, progress, cancellationToken);
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

            lock (_lock)
            {
                EnsurePipelineInitialized(options?.PreferredExecutionProvider);
            }

            var result = _pipeline!.ProcessAudio(resampled, options, progress, cancellationToken);

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

    private void EnsurePipelineInitialized(string? preferredProvider)
    {
        if (_pipeline != null) return;

        string modelsDir = ResolveModelsDirectory();
        string segPath = Path.Combine(modelsDir, "community1-segmentation.onnx");
        string embPath = Path.Combine(modelsDir, "community1-embedding.onnx");
        string pldaPath = Path.Combine(modelsDir, "plda_community1.bin");

        _logger?.LogInformation("Inicializando PyAnnote Community-1 Pipeline desde: {Dir}", modelsDir);
        _pipeline = new Community1DiarizationPipeline(segPath, embPath, pldaPath, preferredProvider, _logger);
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

        var sampleList = new List<float>();
        float[] buffer = new float[16384];
        ISampleProvider sampleProvider = reader;
        int read;
        while ((read = sampleProvider.Read(buffer.AsSpan())) > 0)
        {
            if (channels == 1)
            {
                for (int i = 0; i < read; i++)
                {
                    sampleList.Add(buffer[i]);
                }
            }
            else
            {
                for (int i = 0; i < read; i += channels)
                {
                    float sum = 0f;
                    int count = Math.Min(channels, read - i);
                    for (int ch = 0; ch < count; ch++)
                    {
                        sum += buffer[i + ch];
                    }
                    sampleList.Add(sum / count);
                }
            }
        }

        float[] samples = sampleList.ToArray();
        if (sampleRate != 16000)
        {
            samples = ResampleLinear(samples, sampleRate, 16000);
        }

        return samples;
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
        _pipeline?.Dispose();
    }
}
