using System.IO;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Services;
using DictaMeeting.Meetings.Vocabulary;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SherpaOnnx;

namespace DictaMeeting.Transcription.Services;

/// <summary>
/// Servicio de transcripción local basado en la familia de modelos Qwen3-ASR (Alibaba)
/// ejecutados mediante el motor nativo ONNX Runtime (sherpa-onnx).
/// </summary>
public class SherpaQwenTranscriptionService : ITranscriptionService
{
    private readonly IModelManager _modelManager;
    private readonly IVoiceActivityDetector? _vad;
    private readonly IVocabularyService? _vocabularyService;
    private readonly ILogger<SherpaQwenTranscriptionService>? _logger;
    private readonly object _recognizerLock = new();

    private OfflineRecognizer? _recognizer;
    private string? _lastModelPath;
    private ModelSize _currentModel = ModelSize.Qwen3_06B;
    private LanguageMode _currentLanguage = LanguageMode.Auto;
    private bool _isInitialized;
    private bool _disposed;

    public bool IsInitialized => _isInitialized;
    public ModelSize CurrentModel => _currentModel;
    public LanguageMode CurrentLanguage
    {
        get => _currentLanguage;
        set => _currentLanguage = value;
    }

    public SherpaQwenTranscriptionService(
        IModelManager modelManager,
        IVoiceActivityDetector? vad = null,
        IVocabularyService? vocabularyService = null,
        ILogger<SherpaQwenTranscriptionService>? logger = null)
    {
        _modelManager = modelManager;
        _vad = vad;
        _vocabularyService = vocabularyService;
        _logger = logger;

        if (_vocabularyService != null)
        {
            _vocabularyService.VocabularyChanged += OnVocabularyChanged;
        }
    }

    private void OnVocabularyChanged(object? sender, EventArgs e)
    {
        lock (_recognizerLock)
        {
            if (_isInitialized && !string.IsNullOrEmpty(_lastModelPath))
            {
                try
                {
                    DisposeRecognizer();
                    _recognizer = CreateRecognizer(_lastModelPath, _currentLanguage);
                    _isInitialized = true;
                    _logger?.LogDebug("Motor Qwen3-ASR actualizado con nuevo glosario de Hotwords.");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error al actualizar Hotwords en motor Qwen3-ASR tras cambio de vocabulario.");
                }
            }
        }
    }

    public async Task InitializeAsync(ModelSize modelSize, CancellationToken cancellationToken = default)
    {
        try
        {
            _currentModel = modelSize;
            var modelPath = await _modelManager.EnsureModelDownloadedAsync(modelSize, null, cancellationToken);

            lock (_recognizerLock)
            {
                DisposeRecognizer();
                _lastModelPath = modelPath;
                _recognizer = CreateRecognizer(modelPath, _currentLanguage);
                _isInitialized = true;
            }

            _logger?.LogInformation("Motor Qwen3-ASR inicializado con éxito. Modelo: {Model}, Idioma: {Language}, Ruta: {Path}",
                modelSize, _currentLanguage, modelPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al inicializar el motor Qwen3-ASR para el modelo {Model}.", modelSize);
            throw;
        }
    }

    private OfflineRecognizer CreateRecognizer(string modelPath, LanguageMode language)
    {
        string convFrontend = Path.Combine(modelPath, "conv_frontend.onnx");
        if (!File.Exists(convFrontend))
        {
            throw new FileNotFoundException($"No se encontró 'conv_frontend.onnx' en {modelPath}");
        }

        string encoder = Path.Combine(modelPath, "encoder.int8.onnx");
        if (!File.Exists(encoder))
        {
            encoder = Path.Combine(modelPath, "encoder.onnx");
        }
        if (!File.Exists(encoder))
        {
            throw new FileNotFoundException($"No se encontró 'encoder.int8.onnx' ni 'encoder.onnx' en {modelPath}");
        }

        string decoder = Path.Combine(modelPath, "decoder.int8.onnx");
        if (!File.Exists(decoder))
        {
            decoder = Path.Combine(modelPath, "decoder.onnx");
        }
        if (!File.Exists(decoder))
        {
            throw new FileNotFoundException($"No se encontró 'decoder.int8.onnx' ni 'decoder.onnx' en {modelPath}");
        }

        string tokenizerDir = Path.Combine(modelPath, "tokenizer");
        if (!Directory.Exists(tokenizerDir))
        {
            tokenizerDir = modelPath;
        }

        string defaultHotwords = language switch
        {
            LanguageMode.Spanish => "reunión, acta, acuerdo, proyecto",
            LanguageMode.English => "meeting, action item, project",
            _ => string.Empty
        };

        // Acotamos hotwords a un máximo prudente (ej. 15 términos prioritarios de usuario) para no saturar
        // el scaffold de prompt de sherpa-onnx (límite de 48 tokens) ni restar espacio al KV-cache de 512 tokens.
        // La corrección exhaustiva de todos los términos y variantes se ejecuta de forma determinista con ReplaceAliases().
        string userHotwords = _vocabularyService?.FormatSherpaHotwords(15) ?? string.Empty;
        string hotwords = string.IsNullOrWhiteSpace(userHotwords)
            ? defaultHotwords
            : (string.IsNullOrWhiteSpace(defaultHotwords) ? userHotwords : $"{userHotwords}, {defaultHotwords}");

        var qwen = new OfflineQwen3AsrModelConfig
        {
            ConvFrontend = convFrontend,
            Encoder = encoder,
            Decoder = decoder,
            Tokenizer = tokenizerDir,
            MaxTotalLen = 512,
            MaxNewTokens = 128,
            Temperature = 0.0f,
            TopP = 1.0f,
            Seed = 42,
            Hotwords = hotwords
        };

        int threads = Math.Max(1, Math.Min(Environment.ProcessorCount - 2, 8));
        var modelConfig = new OfflineModelConfig
        {
            Qwen3Asr = qwen,
            NumThreads = threads,
            Provider = "cpu"
        };

        var config = new OfflineRecognizerConfig
        {
            ModelConfig = modelConfig
        };

        var recognizer = new OfflineRecognizer(config);

        using var testStream = recognizer.CreateStream();
        if (testStream.Handle == IntPtr.Zero)
        {
            recognizer.Dispose();
            throw new InvalidOperationException($"No se pudo inicializar el reconocedor nativo sherpa-onnx para Qwen3-ASR en {modelPath}. Verifique los archivos del modelo.");
        }

        return recognizer;
    }

    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(
        string audioFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return TranscribeAudioFileAsync(audioFilePath, _currentModel, _currentLanguage, progress, cancellationToken);
    }

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAudioFileAsync(
        string audioFilePath,
        ModelSize modelSize,
        LanguageMode language,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(audioFilePath))
        {
            return Array.Empty<TranscriptSegment>();
        }

        var modelPath = await _modelManager.EnsureModelDownloadedAsync(modelSize, null, cancellationToken);
        var segments = new List<TranscriptSegment>();
        var tempWavPath = Path.Combine(Path.GetTempPath(), $"dictameeting_qwen_asr_{Guid.NewGuid():N}.wav");

        try
        {
            await Task.Run(() =>
            {
                // 1. Resamplear a WAV 16 kHz Mono de 16 bits para el reconocedor y Silero VAD
                using (var reader = new AudioFileReader(audioFilePath))
                {
                    var outFormat = new WaveFormat(16000, 16, 1);
                    using var resampler = new MediaFoundationResampler(reader, outFormat);
                    WaveFileWriter.CreateWaveFile(tempWavPath, resampler);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 2. Detección de regiones de voz precisas con Silero VAD (modo offline)
                IReadOnlyList<SpeechRegion> speechRegions = Array.Empty<SpeechRegion>();
                if (_vad != null)
                {
                    var vadProgress = new Progress<double>(p => progress?.Report(p * 0.20));
                    speechRegions = _vad.DetectSpeechRegionsAsync(tempWavPath, null, vadProgress, cancellationToken).GetAwaiter().GetResult();
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 3. Crear instancia local para decodificación de archivo
                using var recognizer = CreateRecognizer(modelPath, language);

                if (_vad != null && speechRegions.Count > 0)
                {
                    // Decodificación de alta precisión basada en regiones de voz delimitadas por Silero VAD
                    float[] allSamples;
                    using (var waveReader = new WaveFileReader(tempWavPath))
                    {
                        int sampleCount = (int)waveReader.SampleCount;
                        allSamples = new float[sampleCount];
                        byte[] byteBuffer = new byte[waveReader.Length];
                        int bytesRead = waveReader.Read(byteBuffer, 0, byteBuffer.Length);
                        int samplesRead = bytesRead / 2;
                        for (int i = 0; i < samplesRead; i++)
                        {
                            short s = BitConverter.ToInt16(byteBuffer, i * 2);
                            allSamples[i] = s / 32768.0f;
                        }
                    }

                    for (int r = 0; r < speechRegions.Count; r++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var region = speechRegions[r];

                        int startSample = Math.Clamp(region.StartSample, 0, allSamples.Length);
                        int endSample = Math.Clamp(region.EndSample, startSample, allSamples.Length);
                        int sampleCount = endSample - startSample;
                        if (sampleCount <= 0) continue;

                        float[] regionSamples = new float[sampleCount];
                        Array.Copy(allSamples, startSample, regionSamples, 0, sampleCount);

                        using var stream = recognizer.CreateStream();
                        if (stream.Handle != IntPtr.Zero)
                        {
                            stream.AcceptWaveform(16000, regionSamples);
                            recognizer.Decode(stream);

                            string? text = null;
                            try
                            {
                                text = stream.Result?.Text?.Trim();
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogWarning(ex, "Error al obtener texto de Qwen3-ASR para la región {Start} a {End}.", region.StartTime, region.EndTime);
                            }

                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                segments.Add(new TranscriptSegment
                                {
                                    StartTime = region.StartTime,
                                    EndTime = region.EndTime,
                                    Text = text,
                                    Confidence = region.AverageProbability,
                                    IsFinal = true
                                });
                            }
                        }

                        double p = 0.20 + (0.80 * ((double)(r + 1) / speechRegions.Count));
                        progress?.Report(p);
                    }
                }
                else if (_vad != null && speechRegions.Count == 0)
                {
                    _logger?.LogInformation("Silero VAD no detectó actividad de voz en el audio '{Path}'.", audioFilePath);
                    progress?.Report(1.0);
                }
                else
                {
                    // Fallback por ventanas de 20s si VAD no está presente
                    using var waveReader = new WaveFileReader(tempWavPath);
                    long totalBytes = waveReader.Length;
                    if (totalBytes <= 0) totalBytes = 1;

                    byte[] byteBuffer = new byte[16000 * 2 * 20];
                    long bytesReadTotal = 0;
                    TimeSpan currentOffset = TimeSpan.Zero;
                    int bytesRead;

                    while ((bytesRead = waveReader.Read(byteBuffer, 0, byteBuffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int sampleCount = bytesRead / 2;
                        float[] activeSamples = new float[sampleCount];
                        for (int i = 0; i < sampleCount; i++)
                        {
                            short sample = BitConverter.ToInt16(byteBuffer, i * 2);
                            activeSamples[i] = sample / 32768.0f;
                        }

                        float energy = 0f;
                        for (int i = 0; i < activeSamples.Length; i++)
                        {
                            energy += activeSamples[i] * activeSamples[i];
                        }
                        energy = (float)Math.Sqrt(energy / activeSamples.Length);

                        TimeSpan chunkDuration = TimeSpan.FromSeconds((double)sampleCount / 16000.0);
                        TimeSpan segmentStart = currentOffset;
                        TimeSpan segmentEnd = currentOffset + chunkDuration;

                        if (energy > 0.001f)
                        {
                            using var stream = recognizer.CreateStream();
                            if (stream.Handle != IntPtr.Zero)
                            {
                                stream.AcceptWaveform(16000, activeSamples);
                                recognizer.Decode(stream);

                                string? text = null;
                                try
                                {
                                    text = stream.Result?.Text?.Trim();
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogWarning(ex, "Error al obtener texto del resultado de decodificación.");
                                }

                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    segments.Add(new TranscriptSegment
                                    {
                                        StartTime = segmentStart,
                                        EndTime = segmentEnd,
                                        Text = text,
                                        Confidence = 0.95f,
                                        IsFinal = true
                                    });
                                }
                            }
                        }

                        currentOffset = segmentEnd;
                        bytesReadTotal += bytesRead;

                        double p = Math.Min(1.0, (double)bytesReadTotal / totalBytes);
                        progress?.Report(p);
                    }
                }
            }, cancellationToken);
        }
        finally
        {
            try
            {
                if (File.Exists(tempWavPath))
                {
                    File.Delete(tempWavPath);
                }
            }
            catch { }
        }

        var consolidated = TranscriptSegmentConsolidator.ConsolidateSegments(segments);
        if (_vocabularyService != null)
        {
            foreach (var seg in consolidated)
            {
                seg.Text = _vocabularyService.ReplaceAliases(seg.Text);
            }
        }
        return consolidated;
    }

    public async Task<TranscriptSegment?> TranscribeAudioChunkAsync(
        byte[] pcmAudioData,
        TimeSpan offset,
        CancellationToken cancellationToken = default)
    {
        // 1. Descartar fragmentos sin inicializar o demasiado cortos (< 200 ms = 3200 muestras a 16 kHz 16-bit mono = 6400 bytes)
        if (!_isInitialized || _recognizer == null || pcmAudioData.Length < 6400)
        {
            return null;
        }

        return await Task.Run(() =>
        {
            int sampleCount = pcmAudioData.Length / 2;
            float[] samples = new float[sampleCount];

            float energySum = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = BitConverter.ToInt16(pcmAudioData, i * 2);
                float s = sample / 32768.0f;
                samples[i] = s;
                energySum += s * s;
            }

            // 2. Pre-filtro de energía acústica: si el nivel RMS es inferior a 0.003f, es silencio
            // o ruido ambiente de fondo sin energía vocal real. Se descarta sin cargar el modelo ONNX.
            float rms = (float)Math.Sqrt(energySum / sampleCount);
            if (rms < 0.003f)
            {
                return null;
            }

            string? detectedText = null;

            lock (_recognizerLock)
            {
                if (_recognizer == null) return null;

                using var stream = _recognizer.CreateStream();
                if (stream.Handle == IntPtr.Zero) return null;

                stream.AcceptWaveform(16000, samples);
                _recognizer.Decode(stream);

                try
                {
                    detectedText = stream.Result?.Text?.Trim();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Error al obtener texto de decodificación en chunk en vivo.");
                }
            }

            if (string.IsNullOrWhiteSpace(detectedText))
            {
                return null;
            }

            if (_vocabularyService != null)
            {
                detectedText = _vocabularyService.ReplaceAliases(detectedText);
            }

            TimeSpan duration = TimeSpan.FromSeconds((double)sampleCount / 16000.0);
            return new TranscriptSegment
            {
                StartTime = offset,
                EndTime = offset + duration,
                Text = detectedText,
                Confidence = 0.95f,
                IsFinal = false
            };
        }, cancellationToken);
    }

    private void DisposeRecognizer()
    {
        _recognizer?.Dispose();
        _recognizer = null;
        _isInitialized = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_vocabularyService != null)
        {
            _vocabularyService.VocabularyChanged -= OnVocabularyChanged;
        }

        lock (_recognizerLock)
        {
            DisposeRecognizer();
        }
    }
}
