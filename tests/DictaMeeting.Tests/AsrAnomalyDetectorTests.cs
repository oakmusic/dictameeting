using System;
using System.Collections.Generic;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class AsrAnomalyDetectorTests
{
    private static readonly string[] SampleVocabulary = new[]
    {
        "Aritz", "Bego", "Kaixo", "Aupa", "Aupi", "Egunon", "Arratsaldeon",
        "Hazitek", "Elkartek", "Aitor", "Asier", "Amaia", "Artzai", "Itsaso", "Txasti"
    };

    [Fact]
    public void DetectAnomaly_GhostVocabularyInShortBlip_FlagsSpeechRateAndVocabEcho()
    {
        // 15 palabras en 356 ms (el caso real del incidente en 01:06:04)
        string text = "Aritz Bego Kaixo Aupa Aupi Egunon Arratsaldeon Hazitek Elkartek Aitor Asier Amaia Artzai Itsaso Txasti.";
        var duration = TimeSpan.FromMilliseconds(356);

        var result = AsrAnomalyDetector.DetectAnomaly(text, duration, SampleVocabulary);

        Assert.True(result.IsAnomalous);
        Assert.True(result.AnomalyType == AsrAnomalyType.SpeechRateExceeded ||
                    result.AnomalyType == AsrAnomalyType.VocabularyEchoing);
    }

    [Fact]
    public void DetectAnomaly_NormalHumanSpeech_DoesNotFlag()
    {
        string text = "Buenos días a todos, empezamos la reunión de seguimiento.";
        var duration = TimeSpan.FromSeconds(3.0);

        var result = AsrAnomalyDetector.DetectAnomaly(text, duration, SampleVocabulary);

        Assert.False(result.IsAnomalous);
        Assert.Equal(AsrAnomalyType.None, result.AnomalyType);
    }

    [Fact]
    public void DetectAnomaly_DegenerateNgramLoop_FlagsRepetition()
    {
        // El caso real del incidente en 01:06:06: bucle de AudioSet repetido 16 veces
        string text = "instrument Bass Guitar, Electric Guitar, Guitar, Bass Guitar, Electric Guitar, Guitar, " +
                      "Bass Guitar, Electric Guitar, Guitar, Bass Guitar, Electric Guitar, Guitar, " +
                      "Bass Guitar, Electric Guitar, Guitar, Bass Guitar, Electric Guitar, Guitar.";
        var duration = TimeSpan.FromSeconds(10.0);

        var result = AsrAnomalyDetector.DetectAnomaly(text, duration, SampleVocabulary);

        Assert.True(result.IsAnomalous);
        Assert.Equal(AsrAnomalyType.NgramRepetitionLoop, result.AnomalyType);
    }

    [Fact]
    public void DetectAnomaly_NaturalHumanRepetition_PreservedWithoutFlag()
    {
        // Repeticiones coloquiales humanas legítimas que nunca deben rechazarse
        string text1 = "Sí, sí, sí, totalmente de acuerdo.";
        var result1 = AsrAnomalyDetector.DetectAnomaly(text1, TimeSpan.FromSeconds(2.0), SampleVocabulary);
        Assert.False(result1.IsAnomalous);

        string text2 = "No, no, eso no lo veo claro.";
        var result2 = AsrAnomalyDetector.DetectAnomaly(text2, TimeSpan.FromSeconds(1.8), SampleVocabulary);
        Assert.False(result2.IsAnomalous);

        string text3 = "Vale, vale, de acuerdo.";
        var result3 = AsrAnomalyDetector.DetectAnomaly(text3, TimeSpan.FromSeconds(1.5), SampleVocabulary);
        Assert.False(result3.IsAnomalous);
    }

    [Fact]
    public void DetectAnomaly_VocabularyEchoingWithoutGrammar_FlagsAnomaly()
    {
        // Cadena de vocabulario emitida consecutivamente sin nexos ni preposiciones
        string text = "Aritz Bego Kaixo Aupa Hazitek Elkartek Aitor Asier";
        var duration = TimeSpan.FromSeconds(4.0); // Incluso con duración holgada

        var result = AsrAnomalyDetector.DetectAnomaly(text, duration, SampleVocabulary);

        Assert.True(result.IsAnomalous);
        Assert.Equal(AsrAnomalyType.VocabularyEchoing, result.AnomalyType);
    }

    [Fact]
    public void DetectAnomaly_LegitimateSentenceUsingVocabulary_DoesNotFlag()
    {
        // Oración gramatical correcta que usa términos del vocabulario
        string text = "Aritz y Bego presentaron la propuesta técnica de Hazitek en el consorcio.";
        var duration = TimeSpan.FromSeconds(3.5);

        var result = AsrAnomalyDetector.DetectAnomaly(text, duration, SampleVocabulary);

        Assert.False(result.IsAnomalous);
        Assert.Equal(AsrAnomalyType.None, result.AnomalyType);
    }

    [Fact]
    public void DetectAnomaly_EmptyOrWhitespace_ReturnsNone()
    {
        var result1 = AsrAnomalyDetector.DetectAnomaly("", TimeSpan.FromSeconds(1.0), SampleVocabulary);
        Assert.False(result1.IsAnomalous);

        var result2 = AsrAnomalyDetector.DetectAnomaly("   ", TimeSpan.FromSeconds(1.0), SampleVocabulary);
        Assert.False(result2.IsAnomalous);
    }

    [Fact]
    public void DetectAnomaly_ShortBlipWithMultipleWords_FlagsSpeechRate()
    {
        // 4 palabras en 200 ms (20 palabras/segundo)
        string text = "palabra uno dos tres";
        var duration = TimeSpan.FromMilliseconds(200);

        var result = AsrAnomalyDetector.DetectAnomaly(text, duration, null);

        Assert.True(result.IsAnomalous);
        Assert.Equal(AsrAnomalyType.SpeechRateExceeded, result.AnomalyType);
    }
}
