using System;
using System.IO;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using Xunit;
using Xunit.Abstractions;

namespace DictaMeeting.Tests;

public class SileroRealAudioTests
{
    private readonly ITestOutputHelper _output;

    public SileroRealAudioTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TestSileroOnRealAudio()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string wavPath = Path.Combine(localAppData, "DictaMeeting", "models", "qwen3-asr-0.6b-int8", "test_wavs", "es1.wav");

        if (!File.Exists(wavPath))
        {
            _output.WriteLine($"El archivo de prueba no existe: {wavPath}. Prueba omitida.");
            return;
        }

        using var vad = new SileroVoiceActivityDetector();
        var regions = await vad.DetectSpeechRegionsAsync(wavPath);

        _output.WriteLine($"Regiones detectadas: {regions.Count}");
        foreach (var r in regions)
        {
            _output.WriteLine($"Region: {r.StartTime.TotalSeconds:F2}s -> {r.EndTime.TotalSeconds:F2}s, AvgProb: {r.AverageProbability:F2}");
        }

        Assert.NotEmpty(regions);
        Assert.NotNull(vad.LastStatistics);
        _output.WriteLine($"Stats: Audio = {vad.LastStatistics.TotalAudioDuration.TotalSeconds:F2}s, Speech = {vad.LastStatistics.TotalSpeechDuration.TotalSeconds:F2}s, Time = {vad.LastStatistics.ProcessingTime.TotalMilliseconds:F1}ms");
    }
}
