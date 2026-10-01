using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Vocabulary;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using NAudio.Wave;
using Xunit;
using Xunit.Abstractions;

namespace DictaMeeting.Tests;

/// <summary>
/// Batería de pruebas automatizadas que verifican la prevención de alucinaciones
/// de vocabulario durante la transcripción LIVE y FINAL con Qwen3-ASR.
/// </summary>
public class LiveVocabularyHallucinationPreventionTests
{
    private readonly ITestOutputHelper _output;

    public LiveVocabularyHallucinationPreventionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string GetModelsDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var localModels = Path.Combine(localAppData, "DictaMeeting", "models");
        if (Directory.Exists(localModels))
        {
            return localModels;
        }

        var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models"));
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        return localModels;
    }

    private static VocabularyService CreateTestVocabularyService(string? customFolder = null)
    {
        string folder = customFolder ?? Path.Combine(Path.GetTempPath(), $"dm_vocab_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var vocabService = new VocabularyService(folder);

        // Cargar los términos de ejemplo
        string[] terms =
        {
            "Aritz", "Bego", "Kaixo", "Aupa", "Aupi", "Egunon", "Arratsaldeon", "DevGrant", "OpenResearch",
            "Aitor", "Asier", "Amaia", "Artzai", "Itsaso", "Txasti", "Gorka", "Iñaki", "Begoña",
            "TechLab", "DeepTech", "Luismi", "Joseba", "Igone", "Iraide", "Iker", "Ander", "Saioa",
            "Itziar", "Zurik", "Aitzol", "Meta", "Google", "Apple", "Miñano", "Donosti", "Donostia",
            "Zamudio", "Derio", "Miramón", "Amagoia", "Arkaitz", "Esti", "Estibaliz", "Ibai",
            "CustomGPT", "LeadDeveloper", "ChatGPT", "Qwen", "Gemini", "Claude"
        };

        foreach (var t in terms)
        {
            vocabService.AddOrUpdateTerm(t, new[] { "H" + t.ToLowerInvariant() });
        }

        return vocabService;
    }

    private static byte[] LoadWavAudioPcm(string wavPath)
    {
        using var waveReader = new WaveFileReader(wavPath);
        byte[] buffer = new byte[waveReader.Length];
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = waveReader.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read <= 0) break;
            totalRead += read;
        }
        return buffer;
    }

    [Fact]
    public async Task TEST_1_SpeechWithConfiguredVocabulary_TranscribesOnlySpeech()
    {
        string modelsDir = GetModelsDirectory();
        string qwen06b = Path.Combine(modelsDir, "qwen3-asr-0.6b-int8");
        if (!Directory.Exists(qwen06b)) return;

        var modelManager = new ModelManager(modelsDir);
        var vocabService = CreateTestVocabularyService();
        using var service = new SherpaQwenTranscriptionService(modelManager, vocabularyService: vocabService);
        await service.InitializeAsync(ModelSize.Qwen3_06B);

        string wavPath = Path.Combine(qwen06b, "test_wavs", "es1.wav");
        if (!File.Exists(wavPath)) return;

        byte[] pcmData = LoadWavAudioPcm(wavPath);
        var result = await service.TranscribeAudioChunkAsync(pcmData, TimeSpan.Zero);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Text));

        _output.WriteLine($"TEST 1 Result: '{result.Text}'");

        // El audio real de es1.wav es: "Esta prenda es amplia, recomiendo elegir una talla menor a la habitual."
        Assert.Contains("prenda", result.Text, StringComparison.OrdinalIgnoreCase);

        // No debe contener la lista de vocabulario
        Assert.DoesNotContain("Aritz", result.Text);
        Assert.DoesNotContain("Bego", result.Text);
        Assert.DoesNotContain("Kaixo", result.Text);
        Assert.DoesNotContain("DevGrant", result.Text);
        Assert.DoesNotContain("OpenResearch", result.Text);
    }

    [Fact]
    public async Task TEST_2_SilenceAudio_DoesNotProduceVocabularyList()
    {
        string modelsDir = GetModelsDirectory();
        string qwen06b = Path.Combine(modelsDir, "qwen3-asr-0.6b-int8");
        if (!Directory.Exists(qwen06b)) return;

        var modelManager = new ModelManager(modelsDir);
        var vocabService = CreateTestVocabularyService();
        using var service = new SherpaQwenTranscriptionService(modelManager, vocabularyService: vocabService);
        await service.InitializeAsync(ModelSize.Qwen3_06B);

        // 2 segundos de silencio absoluto (16 kHz, 16 bits = 64000 bytes)
        byte[] silence = new byte[16000 * 2 * 2];
        var result = await service.TranscribeAudioChunkAsync(silence, TimeSpan.Zero);

        // Debe descartarse (null) por el pre-filtro RMS y decodificación greedy
        Assert.Null(result);
    }

    [Fact]
    public async Task TEST_3_VeryShortAudio_ReturnsNullWithoutVocabulary()
    {
        string modelsDir = GetModelsDirectory();
        string qwen06b = Path.Combine(modelsDir, "qwen3-asr-0.6b-int8");
        if (!Directory.Exists(qwen06b)) return;

        var modelManager = new ModelManager(modelsDir);
        var vocabService = CreateTestVocabularyService();
        using var service = new SherpaQwenTranscriptionService(modelManager, vocabularyService: vocabService);
        await service.InitializeAsync(ModelSize.Qwen3_06B);

        // Audio de 50 ms (menos de 200 ms)
        byte[] shortPcm = new byte[16000 * 2 * 50 / 1000];
        var result = await service.TranscribeAudioChunkAsync(shortPcm, TimeSpan.Zero);

        Assert.Null(result);
    }

    [Fact]
    public async Task TEST_4_SpeechWithoutVocabularyTerms_DoesNotSpontaneouslyProduceVocabulary()
    {
        string modelsDir = GetModelsDirectory();
        string qwen06b = Path.Combine(modelsDir, "qwen3-asr-0.6b-int8");
        if (!Directory.Exists(qwen06b)) return;

        var modelManager = new ModelManager(modelsDir);
        var vocabService = CreateTestVocabularyService();
        using var service = new SherpaQwenTranscriptionService(modelManager, vocabularyService: vocabService);
        await service.InitializeAsync(ModelSize.Qwen3_06B);

        string wavPath = Path.Combine(qwen06b, "test_wavs", "es1.wav");
        if (!File.Exists(wavPath)) return;

        byte[] pcmData = LoadWavAudioPcm(wavPath);
        var result = await service.TranscribeAudioChunkAsync(pcmData, TimeSpan.Zero);

        Assert.NotNull(result);
        var officialWords = vocabService.GetOfficialWords();

        // Ningún término del glosario debe haber aparecido de la nada
        foreach (var word in officialWords)
        {
            Assert.DoesNotContain(word, result.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TEST_5_SpeechWithSingleVocabularyTerm_TranscribesOnlySpokenTermNotFullList()
    {
        string modelsDir = GetModelsDirectory();
        string qwen06b = Path.Combine(modelsDir, "qwen3-asr-0.6b-int8");
        if (!Directory.Exists(qwen06b)) return;

        var modelManager = new ModelManager(modelsDir);
        var vocabService = CreateTestVocabularyService();
        // Agregar "prenda" al vocabulario con un alias
        vocabService.AddOrUpdateTerm("Prenda", new[] { "prenda" });

        using var service = new SherpaQwenTranscriptionService(modelManager, vocabularyService: vocabService);
        await service.InitializeAsync(ModelSize.Qwen3_06B);

        string wavPath = Path.Combine(qwen06b, "test_wavs", "es1.wav");
        if (!File.Exists(wavPath)) return;

        byte[] pcmData = LoadWavAudioPcm(wavPath);
        var result = await service.TranscribeAudioChunkAsync(pcmData, TimeSpan.Zero);

        Assert.NotNull(result);
        _output.WriteLine($"TEST 5 Result: '{result.Text}'");

        // Debe contener "Prenda" porque fue hablada en el audio
        Assert.Contains("Prenda", result.Text, StringComparison.OrdinalIgnoreCase);

        // NO debe contener los demás términos del vocabulario
        Assert.DoesNotContain("Aritz", result.Text);
        Assert.DoesNotContain("Bego", result.Text);
        Assert.DoesNotContain("DevGrant", result.Text);
        Assert.DoesNotContain("OpenResearch", result.Text);
        Assert.DoesNotContain("TechLab", result.Text);
    }

    [Fact]
    public async Task TEST_6_SpeechWithMultipleVocabularyTerms_TranscribesOnlySpokenTerms()
    {
        string modelsDir = GetModelsDirectory();
        string qwen06b = Path.Combine(modelsDir, "qwen3-asr-0.6b-int8");
        if (!Directory.Exists(qwen06b)) return;

        var modelManager = new ModelManager(modelsDir);
        var vocabService = CreateTestVocabularyService();
        // Añadir "Prenda" y "Talla" (ambas presentes en es1.wav)
        vocabService.AddOrUpdateTerm("Prenda", new[] { "prenda" });
        vocabService.AddOrUpdateTerm("Talla", new[] { "talla" });

        using var service = new SherpaQwenTranscriptionService(modelManager, vocabularyService: vocabService);
        await service.InitializeAsync(ModelSize.Qwen3_06B);

        string wavPath = Path.Combine(qwen06b, "test_wavs", "es1.wav");
        if (!File.Exists(wavPath)) return;

        byte[] pcmData = LoadWavAudioPcm(wavPath);
        var result = await service.TranscribeAudioChunkAsync(pcmData, TimeSpan.Zero);

        Assert.NotNull(result);
        _output.WriteLine($"TEST 6 Result: '{result.Text}'");

        Assert.Contains("Prenda", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Talla", result.Text, StringComparison.OrdinalIgnoreCase);

        // Ningún término no hablado debe aparecer
        Assert.DoesNotContain("Kaixo", result.Text);
        Assert.DoesNotContain("Aupa", result.Text);
        Assert.DoesNotContain("ChatGPT", result.Text);
    }

    [Fact]
    public async Task TEST_7_SilenceChunkFollowedBySpeechChunk_SilenceProducesNothingAndSpeechTranscribesNormally()
    {
        string modelsDir = GetModelsDirectory();
        string qwen06b = Path.Combine(modelsDir, "qwen3-asr-0.6b-int8");
        if (!Directory.Exists(qwen06b)) return;

        var modelManager = new ModelManager(modelsDir);
        var vocabService = CreateTestVocabularyService();
        using var service = new SherpaQwenTranscriptionService(modelManager, vocabularyService: vocabService);
        await service.InitializeAsync(ModelSize.Qwen3_06B);

        string wavPath = Path.Combine(qwen06b, "test_wavs", "es1.wav");
        if (!File.Exists(wavPath)) return;

        byte[] silence = new byte[16000 * 2 * 2]; // 2s silencio
        byte[] speech = LoadWavAudioPcm(wavPath);

        // 1. Chunk de silencio
        var silenceResult = await service.TranscribeAudioChunkAsync(silence, TimeSpan.Zero);
        Assert.Null(silenceResult);

        // 2. Chunk de voz inmediatamente posterior
        var speechResult = await service.TranscribeAudioChunkAsync(speech, TimeSpan.FromSeconds(2));
        Assert.NotNull(speechResult);
        Assert.Contains("prenda", speechResult.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Aritz", speechResult.Text);
    }

    [Fact]
    public async Task TEST_8_ConsecutiveChunks_VocabularyDoesNotAccumulateOrBecomeGenerative()
    {
        string modelsDir = GetModelsDirectory();
        string qwen06b = Path.Combine(modelsDir, "qwen3-asr-0.6b-int8");
        if (!Directory.Exists(qwen06b)) return;

        var modelManager = new ModelManager(modelsDir);
        var vocabService = CreateTestVocabularyService();
        using var service = new SherpaQwenTranscriptionService(modelManager, vocabularyService: vocabService);
        await service.InitializeAsync(ModelSize.Qwen3_06B);

        string wavPath = Path.Combine(qwen06b, "test_wavs", "es1.wav");
        if (!File.Exists(wavPath)) return;

        byte[] speech = LoadWavAudioPcm(wavPath);
        byte[] silence = new byte[16000 * 2]; // 1s silencio

        // 5 llamadas consecutivas alternadas
        for (int i = 0; i < 5; i++)
        {
            if (i % 2 == 0)
            {
                var res = await service.TranscribeAudioChunkAsync(speech, TimeSpan.FromSeconds(i * 3));
                Assert.NotNull(res);
                Assert.Contains("prenda", res.Text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Aritz", res.Text);
            }
            else
            {
                var res = await service.TranscribeAudioChunkAsync(silence, TimeSpan.FromSeconds(i * 3));
                Assert.Null(res);
            }
        }
    }

    [Fact]
    public async Task TEST_9_RepeatedLiveCalls_PreviousCallDoesNotContaminateNext()
    {
        string modelsDir = GetModelsDirectory();
        string qwen06b = Path.Combine(modelsDir, "qwen3-asr-0.6b-int8");
        if (!Directory.Exists(qwen06b)) return;

        var modelManager = new ModelManager(modelsDir);
        var vocabService = CreateTestVocabularyService();
        using var service = new SherpaQwenTranscriptionService(modelManager, vocabularyService: vocabService);
        await service.InitializeAsync(ModelSize.Qwen3_06B);

        string wavPath = Path.Combine(qwen06b, "test_wavs", "es1.wav");
        if (!File.Exists(wavPath)) return;

        byte[] speech = LoadWavAudioPcm(wavPath);

        var firstResult = await service.TranscribeAudioChunkAsync(speech, TimeSpan.Zero);
        Assert.NotNull(firstResult);

        var secondResult = await service.TranscribeAudioChunkAsync(speech, TimeSpan.FromSeconds(5));
        Assert.NotNull(secondResult);

        // Ambas llamadas deben ser deterministas e idénticas (debido a Temperature = 0.0f)
        Assert.Equal(firstResult.Text, secondResult.Text);
        Assert.DoesNotContain("Aritz", secondResult.Text);
    }

    [Fact]
    public async Task TEST_10_FinalTranscription_VocabularyBehaviorRemainsCorrect()
    {
        string modelsDir = GetModelsDirectory();
        string qwen06b = Path.Combine(modelsDir, "qwen3-asr-0.6b-int8");
        if (!Directory.Exists(qwen06b)) return;

        var modelManager = new ModelManager(modelsDir);
        var vocabService = CreateTestVocabularyService();
        // Agregar "Prenda"
        vocabService.AddOrUpdateTerm("Prenda", new[] { "prenda" });

        using var service = new SherpaQwenTranscriptionService(modelManager, vocabularyService: vocabService);
        await service.InitializeAsync(ModelSize.Qwen3_06B);

        string wavPath = Path.Combine(qwen06b, "test_wavs", "es1.wav");
        if (!File.Exists(wavPath)) return;

        var segments = await service.TranscribeAudioFileAsync(wavPath);

        Assert.NotEmpty(segments);
        string fullText = string.Join(" ", segments.Select(s => s.Text));
        _output.WriteLine($"TEST 10 Final Result: '{fullText}'");

        Assert.Contains("Prenda", fullText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Aritz", fullText);
        Assert.DoesNotContain("Kaixo", fullText);
        Assert.DoesNotContain("DevGrant", fullText);
    }

    [Fact]
    public void SecondLineDefense_AnomalousWordRate_IsDetectedAndRejected()
    {
        // 47 palabras en 2 segundos = 23.5 palabras/segundo (imposible para habla humana)
        string hallucination = "Aritz Bego Kaixo Aupa Aupi Egunon Arratsaldeon DevGrant OpenResearch Aitor Asier Amaia Artzai Itsaso Txasti Gorka Iñaki Begoña TechLab DeepTech Luismi Joseba Igone Iraide Iker Ander Saioa Itziar Zurik Aitzol Meta Google Apple Miñano Donosti Donostia Zamudio Derio Miramón Amagoia Arkaitz Esti Estibaliz Ibai CustomGPT LeadDeveloper ChatGPT";
        var duration = TimeSpan.FromSeconds(2.0);

        var vocabService = CreateTestVocabularyService();
        bool isAnomalous = LiveTranscriptionPipeline.IsAnomalousLiveResult(hallucination, duration, vocabService);

        Assert.True(isAnomalous, "Debe detectar la ráfaga anómala de palabras como alucinación.");
    }

    [Fact]
    public void SecondLineDefense_SequentialVocabularySpill_IsDetectedAndRejected()
    {
        // 10 palabras consecutivas del vocabulario
        string spill = "Aritz Bego Kaixo Aupa Aupi Egunon Arratsaldeon DevGrant OpenResearch Aitor";
        var duration = TimeSpan.FromSeconds(4.0); // Tasa normal de 2.5 palabras/s pero 100% términos secuenciales

        var vocabService = CreateTestVocabularyService();
        bool isAnomalous = LiveTranscriptionPipeline.IsAnomalousLiveResult(spill, duration, vocabService);

        Assert.True(isAnomalous, "Debe detectar el vertido secuencial de vocabulario.");
    }

    [Fact]
    public void SecondLineDefense_NormalConversationalSpeech_IsAllowed()
    {
        string normal = "Hola buenos días a todos, comenzamos la reunión para revisar las tareas del sprint.";
        var duration = TimeSpan.FromSeconds(3.5);

        var vocabService = CreateTestVocabularyService();
        bool isAnomalous = LiveTranscriptionPipeline.IsAnomalousLiveResult(normal, duration, vocabService);

        Assert.False(isAnomalous, "El habla normal debe permitirse.");
    }

    [Fact]
    public void SecondLineDefense_NormalSpeechWithVocabWord_IsAllowed()
    {
        string withVocab = "Hola Aritz, buenos días, ¿cómo estás hoy?";
        var duration = TimeSpan.FromSeconds(2.5);

        var vocabService = CreateTestVocabularyService();
        bool isAnomalous = LiveTranscriptionPipeline.IsAnomalousLiveResult(withVocab, duration, vocabService);

        Assert.False(isAnomalous, "El habla normal que contiene un término del glosario debe permitirse.");
    }
}
