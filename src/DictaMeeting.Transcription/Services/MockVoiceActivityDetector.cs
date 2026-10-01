using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;

namespace DictaMeeting.Transcription.Services;

/// <summary>
/// Implementación simulada de IVoiceActivityDetector para pruebas unitarias y entornos de desarrollo.
/// </summary>
public class MockVoiceActivityDetector : IVoiceActivityDetector
{
    private readonly Func<ReadOnlySpan<float>, float>? _probabilityProvider;
    private readonly IReadOnlyList<SpeechRegion>? _presetRegions;

    public VadStatistics? LastStatistics { get; private set; }

    public MockVoiceActivityDetector(
        Func<ReadOnlySpan<float>, float>? probabilityProvider = null,
        IReadOnlyList<SpeechRegion>? presetRegions = null)
    {
        _probabilityProvider = probabilityProvider;
        _presetRegions = presetRegions;
    }

    public ISileroStreamingSession CreateStreamingSession(SileroStreamingVadConfig? config = null)
    {
        return new MockSileroStreamingSession(config ?? new SileroStreamingVadConfig(), _probabilityProvider);
    }

    public Task<IReadOnlyList<SpeechRegion>> DetectSpeechRegionsAsync(
        string audioFilePath,
        SileroOfflineVadConfig? config = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(1.0);
        var regions = _presetRegions ?? new List<SpeechRegion>
        {
            new(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(3.5), 8000, 56000, 0.95f)
        };
        return Task.FromResult<IReadOnlyList<SpeechRegion>>(regions);
    }

    public IReadOnlyList<SpeechRegion> DetectSpeechRegions(
        float[] audioSamples,
        SileroOfflineVadConfig? config = null,
        IProgress<double>? progress = null)
    {
        progress?.Report(1.0);
        return _presetRegions ?? new List<SpeechRegion>
        {
            new(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(3.5), 8000, 56000, 0.95f)
        };
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Sesión streaming simulada para pruebas unitarias.
/// </summary>
public class MockSileroStreamingSession : ISileroStreamingSession
{
    private readonly SileroStreamingVadConfig _config;
    private readonly Func<ReadOnlySpan<float>, float>? _probabilityProvider;
    private bool _isSpeaking;
    private float _lastProbability;
    private long _currentSample;
    private long _silenceStartSample;

    public bool IsSpeaking => _isSpeaking;
    public float LastProbability => _lastProbability;

    public MockSileroStreamingSession(
        SileroStreamingVadConfig config,
        Func<ReadOnlySpan<float>, float>? probabilityProvider = null)
    {
        _config = config;
        _probabilityProvider = probabilityProvider;
    }

    public VadFrameResult ProcessFrame(ReadOnlySpan<float> frame512)
    {
        float prob;
        if (_probabilityProvider != null)
        {
            prob = _probabilityProvider(frame512);
        }
        else
        {
            // Detector heurístico por defecto para tests: energía RMS
            double sumSquares = 0;
            for (int i = 0; i < frame512.Length; i++)
            {
                sumSquares += frame512[i] * frame512[i];
            }
            float rms = (float)Math.Sqrt(sumSquares / frame512.Length);
            prob = rms >= 0.01f ? 0.95f : 0.05f;
        }

        _lastProbability = prob;
        _currentSample += frame512.Length;

        var eventType = VadEventType.None;
        int minSilenceSamples = (int)(_config.SampleRate * _config.MinSilenceDuration.TotalSeconds);

        if (_lastProbability >= _config.SpeechThreshold)
        {
            _silenceStartSample = 0;
            if (!_isSpeaking)
            {
                _isSpeaking = true;
                eventType = VadEventType.SpeechStart;
            }
        }
        else if (_lastProbability < _config.NegativeSpeechThreshold && _isSpeaking)
        {
            if (_silenceStartSample == 0)
            {
                _silenceStartSample = _currentSample;
            }

            if (_currentSample - _silenceStartSample >= minSilenceSamples)
            {
                _isSpeaking = false;
                _silenceStartSample = 0;
                eventType = VadEventType.SpeechEnd;
            }
        }

        return new VadFrameResult(eventType, _lastProbability, _currentSample);
    }

    public void Reset()
    {
        _isSpeaking = false;
        _lastProbability = 0f;
        _currentSample = 0;
        _silenceStartSample = 0;
    }

    public void Dispose()
    {
    }
}
