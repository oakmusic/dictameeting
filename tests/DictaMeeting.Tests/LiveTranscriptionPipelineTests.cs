using DictaMeeting.Meetings.Models;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class LiveTranscriptionPipelineTests
{
    [Fact]
    public void CalculateRms_SilenceBuffer_ReturnsLowRms()
    {
        // Buffer de silencio absoluto
        byte[] silence = new byte[3200]; // 100 ms
        float rms = LiveTranscriptionPipeline.CalculateRms(silence);

        Assert.Equal(0f, rms);
    }

    [Fact]
    public void CalculateRms_ActiveSpeechBuffer_ReturnsHighRms()
    {
        // Generar onda sinusoidal de 100 ms a 440 Hz con amplitud ~0.25 (8192)
        byte[] speech = GenerateSinePcm(durationMs: 100, frequency: 440, amplitude: 8192);
        float rms = LiveTranscriptionPipeline.CalculateRms(speech);

        Assert.True(rms > 0.15f, $"Se esperaba RMS > 0.15 pero fue {rms}");
    }

    [Theory]
    [InlineData("[Música]", "")]
    [InlineData("(risas)", "")]
    [InlineData("[Aplausos]", "")]
    [InlineData("Subtítulos realizados por la comunidad de Amara.org", "")]
    [InlineData("Muchas gracias por ver el video.", "")]
    [InlineData("Thank you for watching!", "")]
    [InlineData("...", "")]
    [InlineData("----", "")]
    [InlineData("a", "")]
    [InlineData("Buenos días a todos.", "Buenos días a todos.")]
    [InlineData("Hola mundo [Música] hoy revisamos el plan", "Hola mundo hoy revisamos el plan")]
    public void SanitizeTranscriptText_FiltersHallucinationsAndNoise(string input, string expected)
    {
        string actual = LiveTranscriptionPipeline.SanitizeTranscriptText(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task LivePipeline_EnqueuesSpeechThenSilence_ProducesTranscriptSegment()
    {
        // Arrange
        using var mockTranscriber = new MockTranscriptionService();
        using var mockVad = new MockVoiceActivityDetector();
        var config = new LiveTranscriptionConfig
        {
            SpeechThreshold = 0.50f,
            NegativeSpeechThreshold = 0.35f,
            MinSpeechDuration = TimeSpan.FromMilliseconds(150),
            SilenceHangover = TimeSpan.FromMilliseconds(80),
            PreSpeechBufferDuration = TimeSpan.FromMilliseconds(50)
        };

        var pipeline = new LiveTranscriptionPipeline(mockTranscriber, vad: mockVad, config: config);

        var segmentsReceived = new List<TranscriptSegment>();
        pipeline.SegmentProduced += (_, segment) =>
        {
            lock (segmentsReceived)
            {
                segmentsReceived.Add(segment);
            }
        };

        await pipeline.StartAsync(TimeSpan.FromSeconds(10));

        // Act: 200 ms de voz activa (> MinSpeechDuration)
        byte[] speechChunk = GenerateSinePcm(durationMs: 200, frequency: 300, amplitude: 10000);
        pipeline.EnqueueAudioChunk(speechChunk);

        // Seguido de 120 ms de silencio (> SilenceHangover para disparar cierre de intervención)
        byte[] silenceChunk = new byte[16000 * 2 * 120 / 1000];
        pipeline.EnqueueAudioChunk(silenceChunk);

        // Esperar brevemente a que el consumidor en segundo plano procese el fragmento
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < timeout)
        {
            lock (segmentsReceived)
            {
                if (segmentsReceived.Count > 0) break;
            }
            await Task.Delay(50);
        }

        await pipeline.StopAsync();

        // Assert
        Assert.NotEmpty(segmentsReceived);
        var first = segmentsReceived[0];
        Assert.True(first.StartTime >= TimeSpan.FromSeconds(10), "StartTime debe incluir el offset inicial de la reunión.");
        Assert.True(string.IsNullOrEmpty(first.SpeakerId), "Durante la transcripción en vivo no debe asignarse SpeakerId.");
        Assert.True(string.IsNullOrEmpty(first.SpeakerDisplayName), "Durante la transcripción en vivo no debe mostrarse etiqueta de interlocutor.");
        Assert.False(string.IsNullOrWhiteSpace(first.Text));
    }

    [Fact]
    public async Task LivePipeline_OnlySilenceEnqueued_DoesNotProduceSegment()
    {
        // Arrange
        using var mockTranscriber = new MockTranscriptionService();
        using var mockVad = new MockVoiceActivityDetector();
        var config = new LiveTranscriptionConfig
        {
            MinSpeechDuration = TimeSpan.FromMilliseconds(150),
            SilenceHangover = TimeSpan.FromMilliseconds(80)
        };

        var pipeline = new LiveTranscriptionPipeline(mockTranscriber, vad: mockVad, config: config);
        var segmentsReceived = new List<TranscriptSegment>();
        pipeline.SegmentProduced += (_, s) => segmentsReceived.Add(s);

        await pipeline.StartAsync();

        // Act: 500 ms de silencio
        byte[] silence = new byte[16000 * 2 * 500 / 1000];
        pipeline.EnqueueAudioChunk(silence);

        await Task.Delay(150);
        await pipeline.StopAsync();

        // Assert
        Assert.Empty(segmentsReceived);
    }

    [Fact]
    public async Task LivePipeline_NoiseBurstBelowMinDuration_IsDiscarded()
    {
        // Arrange
        using var mockTranscriber = new MockTranscriptionService();
        using var mockVad = new MockVoiceActivityDetector();
        var config = new LiveTranscriptionConfig
        {
            MinSpeechDuration = TimeSpan.FromMilliseconds(300), // Exige 300 ms mínimo
            SilenceHangover = TimeSpan.FromMilliseconds(50)
        };

        var pipeline = new LiveTranscriptionPipeline(mockTranscriber, vad: mockVad, config: config);
        var segmentsReceived = new List<TranscriptSegment>();
        pipeline.SegmentProduced += (_, s) => segmentsReceived.Add(s);

        await pipeline.StartAsync();

        // Act: Chasquido o golpe breve de solo 80 ms (menor que MinSpeechDuration)
        byte[] shortNoise = GenerateSinePcm(durationMs: 80, frequency: 1000, amplitude: 15000);
        pipeline.EnqueueAudioChunk(shortNoise);

        // Silencio para provocar el hangover
        byte[] silence = new byte[16000 * 2 * 100 / 1000];
        pipeline.EnqueueAudioChunk(silence);

        await Task.Delay(150);
        await pipeline.StopAsync();

        // Assert
        Assert.Empty(segmentsReceived);
    }

    [Fact]
    public async Task LivePipeline_RealSileroVad_WithRealHumanSpeech_ProducesSegment()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string wavPath = Path.Combine(localAppData, "DictaMeeting", "models", "qwen3-asr-0.6b-int8", "test_wavs", "es1.wav");

        if (!File.Exists(wavPath))
        {
            // Omitir si el audio de prueba aún no está descargado localmente
            return;
        }

        using var mockTranscriber = new MockTranscriptionService();
        using var realVad = new SileroVoiceActivityDetector();
        var config = new LiveTranscriptionConfig
        {
            SpeechThreshold = 0.50f,
            NegativeSpeechThreshold = 0.35f,
            MinSpeechDuration = TimeSpan.FromMilliseconds(250),
            SilenceHangover = TimeSpan.FromMilliseconds(500)
        };

        var pipeline = new LiveTranscriptionPipeline(mockTranscriber, vad: realVad, config: config);
        var segmentsReceived = new List<TranscriptSegment>();
        pipeline.SegmentProduced += (_, s) =>
        {
            lock (segmentsReceived)
            {
                segmentsReceived.Add(s);
            }
        };

        await pipeline.StartAsync(TimeSpan.Zero);

        // Transmitir chunks de 100 ms simulando la entrada de audio en directo
        byte[] audioBytes = File.ReadAllBytes(wavPath);
        // Descartar encabezado WAV de 44 bytes para leer PCM crudo
        int headerOffset = 44;
        int chunkBytes = 16000 * 2 * 100 / 1000; // 100 ms = 3200 bytes

        for (int offset = headerOffset; offset + chunkBytes <= audioBytes.Length; offset += chunkBytes)
        {
            byte[] chunk = new byte[chunkBytes];
            Array.Copy(audioBytes, offset, chunk, 0, chunkBytes);
            pipeline.EnqueueAudioChunk(chunk);
        }

        // Agregar 1 segundo de silencio al final para permitir que VAD cierre la frase
        byte[] silence = new byte[16000 * 2];
        pipeline.EnqueueAudioChunk(silence);

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            lock (segmentsReceived)
            {
                if (segmentsReceived.Count > 0) break;
            }
            await Task.Delay(50);
        }

        await pipeline.StopAsync();

        Assert.NotEmpty(segmentsReceived);
        Assert.All(segmentsReceived, seg =>
        {
            Assert.False(string.IsNullOrWhiteSpace(seg.Text));
            Assert.True(seg.EndTime > seg.StartTime);
        });

    }

    private static byte[] GenerateSinePcm(int durationMs, int frequency, short amplitude)
    {
        int sampleRate = 16000;
        int totalSamples = sampleRate * durationMs / 1000;
        byte[] buffer = new byte[totalSamples * 2];

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            short sample = (short)(amplitude * Math.Sin(2.0 * Math.PI * frequency * t));
            buffer[i * 2] = (byte)(sample & 0xFF);
            buffer[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return buffer;
    }
}
