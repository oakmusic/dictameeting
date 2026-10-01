using System;
using System.IO;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using Xunit;
using Xunit.Abstractions;

namespace DictaMeeting.Tests;

public class SileroDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public SileroDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TestSineWaveVAD()
    {
        using var vad = new SileroVoiceActivityDetector();
        using var session = vad.CreateStreamingSession(new SileroStreamingVadConfig
        {
            SpeechThreshold = 0.50f,
            NegativeSpeechThreshold = 0.35f,
            MinSilenceDuration = TimeSpan.FromMilliseconds(100)
        });

        // Generate 200 ms of sine wave (300 Hz)
        var sinePcm = GenerateSine(200, 300, 10000);
        float[] samples = new float[sinePcm.Length / 2];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(sinePcm, i * 2) / 32768f;
        }

        _output.WriteLine($"Total sine samples: {samples.Length}");
        for (int i = 0; i + 512 <= samples.Length; i += 512)
        {
            var res = session.ProcessFrame(samples.AsSpan(i, 512));
            _output.WriteLine($"Sine frame {i / 512}: Prob = {res.Probability}, Event = {res.EventType}");
        }
    }

    private static byte[] GenerateSine(int durationMs, int frequency, short amplitude)
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
