using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using DictaMeeting.Audio.Events;
using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Models;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Vocabulary;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.Transcription.Services;

/// <summary>
/// Configuración de sensibilidad y umbrales para Silero VAD en tiempo real y segmentación.
/// </summary>
public record LiveTranscriptionConfig
{
    /// <summary>
    /// Umbral de probabilidad de voz para Silero VAD (0.0 a 1.0). Por defecto 0.50.
    /// </summary>
    public float SpeechThreshold { get; init; } = 0.50f;

    /// <summary>
    /// Umbral inferior de salida de voz para Silero VAD (histeresis). Por defecto 0.35.
    /// </summary>
    public float NegativeSpeechThreshold { get; init; } = 0.35f;

    /// <summary>
    /// Compatibilidad histórica: mapea al umbral de voz.
    /// </summary>
    public float EnergyThreshold
    {
        get => SpeechThreshold;
        init => SpeechThreshold = value;
    }

    /// <summary>
    /// Duración de silencio continuo necesaria para marcar el fin de una intervención (hangover).
    /// 1.3s permite pausas naturales de respiración sin cortar las frases prematuramente.
    /// </summary>
    public TimeSpan SilenceHangover { get; init; } = TimeSpan.FromMilliseconds(1300);

    /// <summary>
    /// Duración mínima que debe durar un fragmento de voz para ser enviado a transcripción (descarta chasquidos y ruidos breves).
    /// </summary>
    public TimeSpan MinSpeechDuration { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Duración máxima de una intervención continua antes de forzar un corte intermedio para no retrasar la transcripción (15s = ~35-50 palabras).
    /// </summary>
    public TimeSpan MaxUtteranceDuration { get; init; } = TimeSpan.FromSeconds(15.0);

    /// <summary>
    /// Duración de audio previo al inicio de la voz conservado en buffer para no cortar la primera sílaba.
    /// </summary>
    public TimeSpan PreSpeechBufferDuration { get; init; } = TimeSpan.FromMilliseconds(200);
}

public class LiveTranscriptionPipeline : ILiveTranscriptionPipeline
{
    private record UtteranceItem(byte[] PcmAudio, TimeSpan StartTime, TimeSpan EndTime, bool IsMicrophoneDominant = true);

    private readonly ITranscriptionService _transcriptionService;
    private readonly IAudioCaptureService? _audioCaptureService;
    private readonly IVoiceActivityDetector _vad;
    private readonly IVocabularyService? _vocabularyService;
    private readonly ILogger<LiveTranscriptionPipeline>? _logger;
    private readonly LiveTranscriptionConfig _config;

    private readonly object _syncLock = new();
    private readonly MemoryStream _activeSpeechBuffer = new();
    private readonly Queue<byte[]> _preBufferQueue = new();
    private readonly Queue<float> _vadSampleQueue = new();

    private ISileroStreamingSession? _vadSession;
    private Channel<UtteranceItem>? _channel;
    private Task? _processingTask;
    private CancellationTokenSource? _cts;

    private bool _isRunning;
    private bool _isSpeaking;
    private bool _disposed;
    private bool _receivedAdvancedAudio;

    private float _currentUtteranceMicEnergy;
    private float _currentUtteranceSysEnergy;
    private long _currentUtteranceTotalSamples;

    private long _totalSamplesReceived;
    private long _speechStartSample;
    private int _preBufferBytesTotal;
    private readonly int _maxPreBufferBytes;
    private TimeSpan _meetingStartOffset;

    public bool IsRunning => _isRunning;
    public event EventHandler<TranscriptSegment>? SegmentProduced;

    public LiveTranscriptionPipeline(
        ITranscriptionService transcriptionService,
        IAudioCaptureService? audioCaptureService = null,
        IVoiceActivityDetector? vad = null,
        LiveTranscriptionConfig? config = null,
        IVocabularyService? vocabularyService = null,
        ILogger<LiveTranscriptionPipeline>? logger = null)
    {
        _transcriptionService = transcriptionService;
        _audioCaptureService = audioCaptureService;
        _vad = vad ?? new SileroVoiceActivityDetector(logger: null);
        _config = config ?? new LiveTranscriptionConfig();
        _vocabularyService = vocabularyService;
        _logger = logger;

        // 16.000 Hz, 16-bit Mono = 32.000 bytes/segundo
        _maxPreBufferBytes = (int)(32000 * _config.PreSpeechBufferDuration.TotalSeconds);
    }

    public Task StartAsync(TimeSpan startOffset = default, CancellationToken cancellationToken = default)
    {
        lock (_syncLock)
        {
            if (_isRunning) return Task.CompletedTask;

            _isRunning = true;
            _meetingStartOffset = startOffset;
            _totalSamplesReceived = 0;
            _speechStartSample = 0;
            _isSpeaking = false;
            _preBufferBytesTotal = 0;
            _preBufferQueue.Clear();
            _vadSampleQueue.Clear();
            _activeSpeechBuffer.SetLength(0);

            try
            {
                _vadSession = _vad.CreateStreamingSession(new SileroStreamingVadConfig
                {
                    SpeechThreshold = _config.SpeechThreshold,
                    NegativeSpeechThreshold = _config.NegativeSpeechThreshold,
                    MinSpeechDuration = _config.MinSpeechDuration,
                    MinSilenceDuration = _config.SilenceHangover,
                    SpeechPadDuration = _config.PreSpeechBufferDuration,
                    MaxUtteranceDuration = _config.MaxUtteranceDuration
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "No se pudo inicializar la sesión streaming de Silero VAD.");
            }

            _channel = Channel.CreateUnbounded<UtteranceItem>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = true
            });

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _processingTask = Task.Run(() => ProcessUtterancesAsync(_channel.Reader, _cts.Token));

            if (_audioCaptureService != null)
            {
                _audioCaptureService.AudioChunkAvailable += OnAudioChunkAvailable;
                _audioCaptureService.MixedAudioChunkAvailable += OnMixedAudioChunkAvailable;
            }

            _logger?.LogInformation("Pipeline de transcripción en tiempo real iniciado con offset {Offset}.", startOffset);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Task? taskToWait = null;

        lock (_syncLock)
        {
            if (!_isRunning) return;
            _isRunning = false;

            if (_audioCaptureService != null)
            {
                _audioCaptureService.AudioChunkAvailable -= OnAudioChunkAvailable;
                _audioCaptureService.MixedAudioChunkAvailable -= OnMixedAudioChunkAvailable;
            }

            // Si quedó voz pendiente en el buffer de habla activa, flashearla al canal
            if (_isSpeaking && _activeSpeechBuffer.Length > 0)
            {
                FlushCurrentUtterance(_totalSamplesReceived);
            }

            _vadSession?.Dispose();
            _vadSession = null;
            _vadSampleQueue.Clear();

            _channel?.Writer.TryComplete();
            taskToWait = _processingTask;
        }

        if (taskToWait != null)
        {
            try
            {
                await taskToWait;
            }
            catch (OperationCanceledException)
            {
                // Cancelación normal
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error durante la finalización del procesador de transcripción.");
            }
        }

        _cts?.Dispose();
        _cts = null;
        _logger?.LogInformation("Pipeline de transcripción en tiempo real detenido con éxito.");
    }

    public void EnqueueAudioChunk(byte[] pcmAudioData)
    {
        EnqueueAudioChunk(pcmAudioData, true, 0.05f, 0f);
    }

    public void EnqueueAudioChunk(byte[] pcmAudioData, bool isMicrophoneDominant, float micRms, float sysRms)
    {
        if (!_isRunning || pcmAudioData.Length < 2) return;

        lock (_syncLock)
        {
            int chunkSamples = pcmAudioData.Length / 2;
            float rms = CalculateRms(pcmAudioData);

            // 1. Alimentar cola de muestras float para Silero VAD (frames de 512 muestras = 32 ms)
            for (int i = 0; i < pcmAudioData.Length - 1; i += 2)
            {
                short sample = BitConverter.ToInt16(pcmAudioData, i);
                _vadSampleQueue.Enqueue(sample / 32768f);
            }

            while (_vadSampleQueue.Count >= 512 && _vadSession != null)
            {
                float[] frame = new float[512];
                for (int i = 0; i < 512; i++)
                {
                    frame[i] = _vadSampleQueue.Dequeue();
                }

                var vadResult = _vadSession.ProcessFrame(frame);

                if (vadResult.EventType == VadEventType.SpeechStart)
                {
                    _logger?.LogInformation("VAD: SPEECH START (probabilidad: {Prob:F2})", vadResult.Probability);

                    if (!_isSpeaking)
                    {
                        _isSpeaking = true;
                        _speechStartSample = Math.Max(0, _totalSamplesReceived - (_preBufferBytesTotal / 2));

                        // Volcar el pre-buffer que contiene el inicio de la primera sílaba
                        while (_preBufferQueue.Count > 0)
                        {
                            var preChunk = _preBufferQueue.Dequeue();
                            _activeSpeechBuffer.Write(preChunk, 0, preChunk.Length);
                        }
                        _preBufferBytesTotal = 0;
                    }
                }
                else if (vadResult.EventType == VadEventType.SpeechEnd)
                {
                    _logger?.LogInformation("VAD: SPEECH END (probabilidad: {Prob:F2})", vadResult.Probability);

                    if (_isSpeaking)
                    {
                        FlushCurrentUtterance(_totalSamplesReceived + chunkSamples);
                        _isSpeaking = false;
                    }
                }
            }

            if (_isSpeaking)
            {
                float effectiveMic = micRms > 0 ? micRms : (isMicrophoneDominant ? rms : 0f);
                float effectiveSys = sysRms > 0 ? sysRms : (!isMicrophoneDominant ? rms : 0f);
                _currentUtteranceMicEnergy += effectiveMic * chunkSamples;
                _currentUtteranceSysEnergy += effectiveSys * chunkSamples;
                _currentUtteranceTotalSamples += chunkSamples;

                _activeSpeechBuffer.Write(pcmAudioData, 0, pcmAudioData.Length);

                // Comprobar si se ha alcanzado la duración máxima por fragmento para forzar corte natural
                int currentSamplesInBuffer = (int)(_activeSpeechBuffer.Length / 2);
                if (TimeSpan.FromSeconds((double)currentSamplesInBuffer / 16000.0) >= _config.MaxUtteranceDuration)
                {
                    FlushCurrentUtterance(_totalSamplesReceived + chunkSamples);
                    _speechStartSample = _totalSamplesReceived + chunkSamples;
                }
            }
            else
            {
                // Silencio / ruido: mantener ventana deslizante de pre-buffer
                _preBufferQueue.Enqueue(pcmAudioData);
                _preBufferBytesTotal += pcmAudioData.Length;

                while (_preBufferBytesTotal > _maxPreBufferBytes && _preBufferQueue.Count > 0)
                {
                    var dropped = _preBufferQueue.Dequeue();
                    _preBufferBytesTotal -= dropped.Length;
                }
            }

            _totalSamplesReceived += chunkSamples;
        }
    }

    private void OnAudioChunkAvailable(object? sender, AudioChunkEventArgs e)
    {
        _receivedAdvancedAudio = true;
        EnqueueAudioChunk(e.PcmData, e.DominantSource == AudioDeviceType.Microphone, e.MicRms, e.SysRms);
    }

    private void OnMixedAudioChunkAvailable(object? sender, byte[] pcmAudioData)
    {
        if (_receivedAdvancedAudio) return;
        EnqueueAudioChunk(pcmAudioData, false, 0f, 0.05f);
    }

    private void FlushCurrentUtterance(long endSample)
    {
        if (_activeSpeechBuffer.Length == 0) return;

        var audioData = _activeSpeechBuffer.ToArray();
        _activeSpeechBuffer.SetLength(0);

        float avgMicRms = _currentUtteranceTotalSamples > 0 ? (_currentUtteranceMicEnergy / _currentUtteranceTotalSamples) : 0f;
        float avgSysRms = _currentUtteranceTotalSamples > 0 ? (_currentUtteranceSysEnergy / _currentUtteranceTotalSamples) : 0f;
        _currentUtteranceMicEnergy = 0f;
        _currentUtteranceSysEnergy = 0f;
        _currentUtteranceTotalSamples = 0;

        // Atribuir exclusivamente al micrófono local sólo si el sistema está en silencio
        // y el micrófono tiene volumen de voz real. Si hay audio del sistema, debe diarizarse.
        bool isMic = avgSysRms < 0.005f && avgMicRms >= 0.012f;

        int totalSamples = audioData.Length / 2;
        var duration = TimeSpan.FromSeconds((double)totalSamples / 16000.0);

        float chunkRms = CalculateRms(audioData);
        if (duration >= _config.MinSpeechDuration && chunkRms >= 0.003f)
        {
            var startTime = _meetingStartOffset + TimeSpan.FromSeconds((double)_speechStartSample / 16000.0);
            var endTime = _meetingStartOffset + TimeSpan.FromSeconds((double)endSample / 16000.0);
            if (endTime <= startTime)
            {
                endTime = startTime + duration;
            }

            var utterance = new UtteranceItem(audioData, startTime, endTime, isMic);
            _channel?.Writer.TryWrite(utterance);
        }
    }

    private async Task ProcessUtterancesAsync(ChannelReader<UtteranceItem> reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                while (reader.TryRead(out var utterance))
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        var segment = await _transcriptionService.TranscribeAudioChunkAsync(
                            utterance.PcmAudio,
                            utterance.StartTime,
                            cancellationToken);

                        if (segment != null)
                        {
                            var utteranceDuration = utterance.EndTime - utterance.StartTime;
                            if (IsAnomalousLiveResult(segment.Text, utteranceDuration, _vocabularyService))
                            {
                                _logger?.LogWarning("Descartada transcripción anómala en vivo ({Duration:F1}s): '{Text}'", utteranceDuration.TotalSeconds, segment.Text);
                                continue;
                            }

                            var cleanedText = SanitizeTranscriptText(segment.Text);
                            if (!string.IsNullOrWhiteSpace(cleanedText))
                            {
                                if (_vocabularyService != null)
                                {
                                    cleanedText = _vocabularyService.ReplaceAliases(cleanedText);
                                }

                                segment.Text = cleanedText;
                                segment.StartTime = utterance.StartTime;
                                segment.EndTime = utterance.EndTime;

                                // Durante la grabación en vivo NO se ejecuta diarización ni se asignan etiquetas de hablante provisionales.
                                // La identificación de participantes se ejecuta exclusivamente al final durante el procesamiento post-reunión.
                                segment.SpeakerId = string.Empty;
                                segment.SpeakerDisplayName = string.Empty;

                                SegmentProduced?.Invoke(this, segment);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Error al transcribir fragmento de audio en segundo plano.");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Salida limpia
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Excepción no controlada en el bucle consumidor de transcripción.");
        }
    }

    /// <summary>
    /// Limpia artefactos comunes de Whisper producidos por ruidos de fondo o pausas (ej. subtítulos fantasma, corchetes, repeticiones).
    /// </summary>
    public static string SanitizeTranscriptText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

        var text = rawText.Trim();

        // 1. Filtrar etiquetas entre corchetes o paréntesis como [Música], (Risas), [Aplausos], [Sonido], (Campana)
        if ((text.StartsWith('[') && text.EndsWith(']')) || (text.StartsWith('(') && text.EndsWith(')')))
        {
            return string.Empty;
        }

        // Eliminar ocurrencias internas de [Música], [Sonido], etc. y normalizar espacios múltiples
        text = Regex.Replace(text, @"\[.*?\]|\(.*?\)", string.Empty);
        text = Regex.Replace(text, @"\s{2,}", " ").Trim();

        // 2. Filtrar alucinaciones típicas de Whisper de YouTube / subtitulado
        var hallucinations = new[]
        {
            "subtítulos realizados",
            "subtitulos realizados",
            "comunidad de amara.org",
            "gracias por ver el video",
            "gracias por ver este video",
            "gracias por ver",
            "thank you for watching",
            "suscríbete al canal",
            "suscribete al canal",
            "suscríbete",
            "suscribete",
            "subtitles by"
        };

        foreach (var phrase in hallucinations)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
        }

        // 3. Descartar cadenas compuestas únicamente por signos de puntuación o espacios
        if (text.All(c => char.IsPunctuation(c) || char.IsWhiteSpace(c)))
        {
            return string.Empty;
        }

        // 4. Mínimo 2 caracteres significativos
        if (text.Length < 2)
        {
            return string.Empty;
        }

        return text;
    }

    /// <summary>
    /// Calcula el nivel de energía RMS de un búfer PCM de 16-bit Mono.
    /// </summary>
    public static float CalculateRms(byte[] pcmBytes)
    {
        if (pcmBytes == null || pcmBytes.Length < 2) return 0f;

        int sampleCount = pcmBytes.Length / 2;
        double sumSquares = 0;

        for (int i = 0; i < pcmBytes.Length - 1; i += 2)
        {
            short sample = (short)(pcmBytes[i] | (pcmBytes[i + 1] << 8));
            double normalized = sample / 32768.0;
            sumSquares += normalized * normalized;
        }

        return (float)Math.Sqrt(sumSquares / sampleCount);
    }

    /// <summary>
    /// Segunda línea de defensa: detecta anomalías evidentes en resultados LIVE como ráfagas imposibles
    /// de palabras por segundo o emisión secuencial de términos de vocabulario del prompt (prompt spilling).
    /// </summary>
    public static bool IsAnomalousLiveResult(string? text, TimeSpan duration, IVocabularyService? vocabularyService = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        string trimmed = text.Trim();
        var words = trimmed.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return false;

        double seconds = duration.TotalSeconds;

        // 1. Ráfaga de palabras acústicamente imposible (> 6 palabras/s para fragmentos con más de 4 palabras)
        // El habla humana normal oscila entre 2.0 y 3.5 palabras/segundo. 6 palabras/s es físicamente ininteligible.
        if (words.Length > 4 && seconds > 0.05 && (words.Length / seconds) > 6.0)
        {
            return true;
        }

        // 2. Detección de vertido directo de vocabulario (prompt spilling):
        // Si el resultado consta de 4 o más palabras consecutivas donde más del 85% coinciden con
        // términos del vocabulario oficial configurado en orden secuencial sin sintaxis conversacional.
        if (vocabularyService != null && words.Length >= 4)
        {
            var officialWords = vocabularyService.GetOfficialWords();
            if (officialWords.Count >= 4)
            {
                var officialSet = new HashSet<string>(officialWords, StringComparer.OrdinalIgnoreCase);
                int vocabMatches = 0;
                int consecutiveMatches = 0;
                int maxConsecutive = 0;

                for (int i = 0; i < words.Length; i++)
                {
                    string cleanWord = Regex.Replace(words[i], @"[^\p{L}\p{Nd}]", "");
                    if (!string.IsNullOrEmpty(cleanWord) && officialSet.Contains(cleanWord))
                    {
                        vocabMatches++;
                        consecutiveMatches++;
                        if (consecutiveMatches > maxConsecutive) maxConsecutive = consecutiveMatches;
                    }
                    else
                    {
                        consecutiveMatches = 0;
                    }
                }

                if ((double)vocabMatches / words.Length >= 0.85 && maxConsecutive >= 4)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopAsync();
        _activeSpeechBuffer.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopAsync().GetAwaiter().GetResult();
        _activeSpeechBuffer.Dispose();
        GC.SuppressFinalize(this);
    }
}
