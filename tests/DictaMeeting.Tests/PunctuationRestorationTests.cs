using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services.Punctuation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace DictaMeeting.Tests;

public class PunctuationRestorationTests
{
    private readonly ITestOutputHelper _output;

    public PunctuationRestorationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static IPunctuationService CreateService(bool enabled = true)
    {
        return new OnnxPunctuationService(new PunctuationModelConfig
        {
            Enabled = enabled,
            MaxWordsPerWindow = 150,
            OverlapWords = 25
        }, NullLogger<OnnxPunctuationService>.Instance);
    }

    private static string[] ExtractWordsWithoutPunctuation(string text)
    {
        return text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(w => Regex.Replace(w, @"[.,;:!?\-]", "").Trim())
                   .Where(w => !string.IsNullOrEmpty(w))
                   .ToArray();
    }

    [Fact]
    public async Task SpanishText_RestoresPunctuationAndCapitalization()
    {
        // 1. Texto español sin puntuación representativo de reunión
        using var service = CreateService();
        var rawText = "buenos dias a todos comenzamos la reunion de arquitectura para revisar el estado del proyecto tenemos varios puntos pendientes";

        var result = await service.RestorePunctuationAsync(rawText);
        _output.WriteLine($"[ES Input] : {rawText}");
        _output.WriteLine($"[ES Output]: {result}");

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.True(char.IsUpper(result[0]), "El texto resultante debe comenzar en mayúscula.");
        Assert.Contains(".", result);

        // Comprobar que no se perdieron ni cambiaron las palabras
        var inputWords = rawText.Split(' ');
        var outputWords = ExtractWordsWithoutPunctuation(result);

        Assert.Equal(inputWords.Length, outputWords.Length);
        for (int i = 0; i < inputWords.Length; i++)
        {
            Assert.Equal(inputWords[i].ToLowerInvariant(), outputWords[i].ToLowerInvariant());
        }
    }

    [Fact]
    public async Task EnglishText_RestoresPunctuationAndCapitalization()
    {
        // 2. Texto inglés sin puntuación representativo de reunión técnica
        using var service = CreateService();
        var rawText = "hello everyone welcome to the sprint review we have completed the database migration and the api endpoints are ready for testing";

        var result = await service.RestorePunctuationAsync(rawText);
        _output.WriteLine($"[EN Input] : {rawText}");
        _output.WriteLine($"[EN Output]: {result}");

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.True(char.IsUpper(result[0]), "El texto resultante debe comenzar en mayúscula.");
        Assert.Contains(".", result);

        var inputWords = rawText.Split(' ');
        var outputWords = ExtractWordsWithoutPunctuation(result);

        Assert.Equal(inputWords.Length, outputWords.Length);
        for (int i = 0; i < inputWords.Length; i++)
        {
            Assert.Equal(inputWords[i].ToLowerInvariant(), outputWords[i].ToLowerInvariant());
        }
    }

    [Fact]
    public async Task TechnicalText_PreservesTechnicalTerminologyAndAcronyms()
    {
        // 3. Texto técnico con acrónimos y nombres técnicos (Kubernetes, RAM, GitHub, dotnet, PostgreSQL)
        using var service = CreateService();
        var rawText = "el despliegue en Kubernetes falló porque el cluster no tenía suficiente memoria RAM configurada en los pods de PostgreSQL y dotnet";

        var result = await service.RestorePunctuationAsync(rawText);
        _output.WriteLine($"[Tech Input] : {rawText}");
        _output.WriteLine($"[Tech Output]: {result}");

        // Verificar que no destruye términos técnicos
        Assert.Contains("Kubernetes", result);
        Assert.Contains("RAM", result);
        Assert.Contains("PostgreSQL", result);
        Assert.Contains("dotnet", result);

        var inputWords = rawText.Split(' ');
        var outputWords = ExtractWordsWithoutPunctuation(result);

        Assert.Equal(inputWords.Length, outputWords.Length);
        for (int i = 0; i < inputWords.Length; i++)
        {
            Assert.Equal(inputWords[i].ToLowerInvariant(), outputWords[i].ToLowerInvariant());
        }
    }

    [Fact]
    public async Task ExceedingTokenLimit_ProcessesMultipleWindows_NoWordsLostOrDuplicated()
    {
        // 4, 5, 6, 7. Texto largo (>400 palabras) que supera el límite de ventana única
        using var service = CreateService();

        var baseSentence = "en la sesion de diseno arquitectonico analizamos la infraestructura de microservicios distribuidos para optimizar la latencia de red y mejorar la resiliencia del sistema ";
        // Repetir para generar más de 300 palabras
        var longText = string.Join(" ", Enumerable.Repeat(baseSentence.Trim(), 15));
        var inputWords = longText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.True(inputWords.Length > 300, $"El texto debe tener más de 300 palabras (actual: {inputWords.Length})");

        var result = await service.RestorePunctuationAsync(longText);
        _output.WriteLine($"[Long Input words]: {inputWords.Length}");
        _output.WriteLine($"[Long Output preview]: {result.Substring(0, Math.Min(300, result.Length))}...");

        var outputWords = ExtractWordsWithoutPunctuation(result);

        // 6. Que no se pierdan palabras
        // 7. Que no se dupliquen palabras
        Assert.Equal(inputWords.Length, outputWords.Length);

        for (int i = 0; i < inputWords.Length; i++)
        {
            Assert.Equal(inputWords[i].ToLowerInvariant(), outputWords[i].ToLowerInvariant());
        }

        // 5. Comprobar que se procesaron múltiples ventanas
        Assert.NotNull(service.LastStatistics);
        Assert.True(service.LastStatistics.TotalWindowsProcessed >= 2,
            $"Debe haber procesado múltiples ventanas (actual: {service.LastStatistics.TotalWindowsProcessed})");
    }

    [Fact]
    public async Task TranscriptSegments_PreservesTimestampsAndSpeakers()
    {
        // 8, 9. Que se mantengan los timestamps y los speaker IDs
        using var service = CreateService();

        var segment1 = new TranscriptSegment
        {
            Id = "seg-001",
            SpeakerId = "SPEAKER_00",
            SpeakerDisplayName = "Aritz Villodas",
            StartTime = TimeSpan.FromSeconds(10.5),
            EndTime = TimeSpan.FromSeconds(15.2),
            Text = "hola a todos bienvenidos a la sesion tecnica de hoy",
            Confidence = 0.95f,
            IsFinal = true
        };

        var segment2 = new TranscriptSegment
        {
            Id = "seg-002",
            SpeakerId = "SPEAKER_01",
            SpeakerDisplayName = "Invitado Externo",
            StartTime = TimeSpan.FromSeconds(16.0),
            EndTime = TimeSpan.FromSeconds(22.4),
            Text = "gracias aritz podemos comenzar revisando las metricas de rendimiento del backend",
            Confidence = 0.91f,
            IsFinal = true
        };

        var segment3 = new TranscriptSegment
        {
            Id = "seg-003",
            SpeakerId = "SPEAKER_00",
            SpeakerDisplayName = "Aritz Villodas",
            StartTime = TimeSpan.FromSeconds(23.0),
            EndTime = TimeSpan.FromSeconds(28.0),
            Text = "por supuesto tenemos los graficos de consumo de cpu preparados",
            Confidence = 0.98f,
            IsFinal = true
        };

        var segments = new[] { segment1, segment2, segment3 };

        var results = await service.RestorePunctuationAsync(segments);

        Assert.Equal(3, results.Count);

        for (int i = 0; i < segments.Length; i++)
        {
            var original = segments[i];
            var updated = results[i];

            _output.WriteLine($"Segmento {i} [{updated.SpeakerDisplayName} {updated.FormattedStartTime}]: {updated.Text}");

            // Validar que se preservan exactamente los timestamps y metadatos
            Assert.Equal(original.Id, updated.Id);
            Assert.Equal(original.SpeakerId, updated.SpeakerId);
            Assert.Equal(original.SpeakerDisplayName, updated.SpeakerDisplayName);
            Assert.Equal(original.StartTime, updated.StartTime);
            Assert.Equal(original.EndTime, updated.EndTime);
            Assert.Equal(original.Confidence, updated.Confidence);
            Assert.Equal(original.IsFinal, updated.IsFinal);

            // Validar que el texto tiene mayúsculas al inicio
            Assert.True(char.IsUpper(updated.Text[0]), $"El segmento {i} debe comenzar en mayúscula.");

            // Validar que no se pierden palabras en el segmento
            var origWords = original.Text.Split(' ');
            var updWords = ExtractWordsWithoutPunctuation(updated.Text);
            Assert.Equal(origWords.Length, updWords.Length);
            for (int w = 0; w < origWords.Length; w++)
            {
                Assert.Equal(origWords[w].ToLowerInvariant(), updWords[w].ToLowerInvariant());
            }
        }
    }

    [Fact]
    public async Task DeterministicOutput_ProducesIdenticalResultsForSameInput()
    {
        // 10. Que el resultado sea determinista para la misma entrada
        using var service = CreateService();
        var rawText = "la base de datos relacional almacena las transacciones con aislamiento serializable mientras que redis cachea las sesiones activas";

        var run1 = await service.RestorePunctuationAsync(rawText);
        var run2 = await service.RestorePunctuationAsync(rawText);

        _output.WriteLine($"Run 1: {run1}");
        _output.WriteLine($"Run 2: {run2}");

        Assert.Equal(run1, run2);
    }

    [Fact]
    public async Task QuestionsAndSentenceChanges_RestoresQuestionMarkAndCapitalization()
    {
        using var service = CreateService();
        var rawText = "cuando tenemos la proxima entrega del cliente creo que el proximo viernes por la tarde";

        var result = await service.RestorePunctuationAsync(rawText);
        _output.WriteLine($"[Question Input] : {rawText}");
        _output.WriteLine($"[Question Output]: {result}");

        Assert.True(char.IsUpper(result[0]));
        // Debe contener signos de puntuación y al menos un punto o signo de interrogación
        Assert.True(result.Contains("?") || result.Contains("."), "Debe detectar cambio de oración o pregunta.");
    }

    [Fact]
    public async Task CpuInference_VerifiesTelemetryAndPerformance()
    {
        // 11. Que el modelo funcione mediante ONNX Runtime en CPU con telemetría
        using var service = CreateService();
        var rawText = "probando la inferencia en cpu mediante onnx runtime con modelo cuantizado int8";

        var result = await service.RestorePunctuationAsync(rawText);

        var stats = service.LastStatistics;
        Assert.NotNull(stats);
        Assert.True(stats.ModelSizeBytes > 0, "ModelSizeBytes debe ser mayor a cero.");
        Assert.True(stats.TotalTokensProcessed > 0, "TotalTokensProcessed debe ser mayor a cero.");
        Assert.True(stats.TotalInferenceDuration > TimeSpan.Zero, "TotalInferenceDuration debe ser mayor a cero.");
        Assert.True(stats.TotalWordsProcessed > 0, "TotalWordsProcessed debe ser mayor a cero.");

        _output.WriteLine($"Telemetry: Model={stats.ModelName}, Size={stats.ModelSizeBytes / (1024.0 * 1024.0):F1} MB, Tokens={stats.TotalTokensProcessed}, InferenceTime={stats.TotalInferenceDuration.TotalMilliseconds:F1} ms");
    }

    [Fact]
    public async Task AlreadyPunctuatedText_DoesNotProduceDuplicatePunctuation()
    {
        // Verifica que un texto que ya contiene comas o puntos (como la salida de Qwen3-ASR)
        // no produzca signos dobles como ".." o ",,"
        using var service = CreateService(enabled: true);
        var inputWithPunctuation = "Buenos días a todos, comenzamos la reunión. Hoy revisaremos el estado del proyecto, ¿de acuerdo? Sí, perfecto.";

        var result = await service.RestorePunctuationAsync(inputWithPunctuation);
        _output.WriteLine($"[Original With Punctuation]: {inputWithPunctuation}");
        _output.WriteLine($"[Restored Output]          : {result}");

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("..", result);
        Assert.DoesNotContain(",,", result);
        Assert.DoesNotContain(".,", result);
        Assert.DoesNotContain(",.", result);
        Assert.DoesNotContain("??", result);
        Assert.DoesNotContain("--", result);
    }

    [Fact]
    public async Task WhenDisabled_ReturnsOriginalSegmentsVerbatimWithoutLoadingModel()
    {
        // Verifica que cuando IsEnabled = false, los segmentos se devuelven 100% inalterados
        // y no se incurre en consumo de memoria ni carga del modelo ONNX
        using var service = CreateService(enabled: false);
        var originalSegments = new List<TranscriptSegment>
        {
            new TranscriptSegment
            {
                Id = "seg-1",
                StartTime = TimeSpan.FromSeconds(0),
                EndTime = TimeSpan.FromSeconds(5),
                SpeakerId = "spk-1",
                SpeakerDisplayName = "Carlos",
                Text = "Transcripción original perfecta generada directamente por Qwen3-ASR, con comas y puntos."
            }
        };

        var result = await service.RestorePunctuationAsync(originalSegments);

        Assert.Single(result);
        Assert.Equal(originalSegments[0].Text, result[0].Text);
        Assert.Equal(originalSegments[0].StartTime, result[0].StartTime);
        Assert.Equal(originalSegments[0].EndTime, result[0].EndTime);
        Assert.Equal(originalSegments[0].SpeakerId, result[0].SpeakerId);
        Assert.Null(service.LastStatistics);
    }
}

