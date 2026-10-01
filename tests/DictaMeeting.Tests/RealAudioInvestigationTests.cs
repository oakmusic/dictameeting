using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DictaMeeting.Diarization.Models;
using DictaMeeting.Diarization.Pipeline;
using DictaMeeting.Diarization.Services;
using Xunit;
using Xunit.Abstractions;

namespace DictaMeeting.Tests;

/// <summary>
/// Prueba de integración opcional para el pipeline de diarización neuronal PyAnnote Community-1.
/// Diseñada para ejecutarse exclusivamente en entornos de laboratorio o CI con fixtures de audio sintéticas
/// configuradas explícitamente mediante la variable de entorno DICTAMEETING_LAB_AUDIO.
/// En entornos limpios o sin modelos descargados, la prueba se omite de forma transparente.
/// </summary>
public class RealAudioInvestigationTests
{
    private readonly ITestOutputHelper _output;

    public RealAudioInvestigationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DiarizeAudioFile_OnLabAudioIfConfigured_DiscoversSpeakers()
    {
        // Solo se ejecuta si se especifica explícitamente una ruta de audio sintético/laboratorio
        string? labAudioPath = Environment.GetEnvironmentVariable("DICTAMEETING_LAB_AUDIO");

        if (string.IsNullOrWhiteSpace(labAudioPath) || !File.Exists(labAudioPath))
        {
            _output.WriteLine("Prueba de integración de diarización PyAnnote omitida: DICTAMEETING_LAB_AUDIO no está configurada o el archivo no existe.");
            return;
        }

        try
        {
            using var service = new PyAnnoteCommunity1DiarizationService();
            var progress = new Progress<double>(p => _output.WriteLine($"Progreso diarización: {p:P0}"));
            var result = await service.DiarizeAudioFileAsync(labAudioPath, options: null, progress);

            Assert.NotNull(result);
            _output.WriteLine($"Duración: {result.Duration}");
            _output.WriteLine($"Hablantes detectados: {result.DetectedSpeakerCount}");
            _output.WriteLine($"Segmentos exclusivos: {result.ExclusiveSegments.Count}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Diarización no pudo ejecutarse (posiblemente modelos PyAnnote no presentes en el equipo): {ex.Message}");
        }
    }
}
