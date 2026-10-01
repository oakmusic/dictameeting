#pragma warning disable CS0618
using System.Runtime.InteropServices;
using DictaMeeting.Audio.Events;
using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Models;
using DictaMeeting.Audio.Processing;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DictaMeeting.Audio.Services;

public class AudioCaptureService : IAudioCaptureService
{
    private readonly IAudioMixer _mixer;
    private readonly IAudioRecordingService _recordingService;
    private readonly ILogger<AudioCaptureService>? _logger;
    private readonly AudioStreamMixer _streamMixer;

    private WasapiCapture? _micCapture;
    private WasapiLoopbackCapture? _sysCapture;

    private bool _isCapturing;
    private bool _isMonitoring;
    private bool _disposed;

    public bool IsCapturing => _isCapturing;
    public bool IsMonitoring => _isMonitoring;
    public bool CaptureMicrophone { get; set; } = true;
    public bool CaptureSystemAudio { get; set; } = true;

    private bool _isMicrophoneMuted;
    public bool IsMicrophoneMuted
    {
        get => _isMicrophoneMuted;
        set
        {
            if (_isMicrophoneMuted != value)
            {
                _isMicrophoneMuted = value;
                _logger?.LogInformation("Micrófono de captura {State}.", value ? "silenciado" : "reactivado");
                MicrophoneMuteChanged?.Invoke(this, value);
                if (value)
                {
                    AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(AudioDeviceType.Microphone, 0f, 0f));
                }
            }
        }
    }

    public AudioDevice? SelectedMicrophone { get; set; }
    public AudioDevice? SelectedSystemDevice { get; set; }

    private bool _isSystemAudioMuted;
    public bool IsSystemAudioMuted
    {
        get => _isSystemAudioMuted;
        set
        {
            if (_isSystemAudioMuted != value)
            {
                _isSystemAudioMuted = value;
                _logger?.LogInformation("Audio del sistema {State}.", value ? "silenciado" : "reactivado");
                SystemAudioMuteChanged?.Invoke(this, value);
                if (value)
                {
                    AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(AudioDeviceType.SystemLoopback, 0f, 0f));
                }
            }
        }
    }

    public event EventHandler<AudioLevelEventArgs>? AudioLevelUpdated;
    public event EventHandler<byte[]>? MixedAudioChunkAvailable;
    public event EventHandler<AudioChunkEventArgs>? AudioChunkAvailable;
    public event EventHandler<string>? CaptureErrorOccurred;
    public event EventHandler<bool>? MicrophoneMuteChanged;
    public event EventHandler<bool>? SystemAudioMuteChanged;
    public event EventHandler? SpeakingWhileMutedDetected;

    public AudioCaptureService(
        IAudioMixer mixer,
        IAudioRecordingService recordingService,
        ILogger<AudioCaptureService>? logger = null)
    {
        _mixer = mixer;
        _recordingService = recordingService;
        _logger = logger;
        _streamMixer = new AudioStreamMixer(_mixer);

        _streamMixer.MixedSamplesAvailable += OnMixedSamplesAvailable;
        _streamMixer.MixedChunkAvailable += OnMixedChunkAvailable;
    }

    private void OnMixedChunkAvailable(object? sender, MixedAudioChunk chunk)
    {
        var pcmBytes = AudioResampler.FloatTo16BitPcm(chunk.Samples);
        AudioChunkAvailable?.Invoke(this, new AudioChunkEventArgs(pcmBytes, chunk.DominantSource, chunk.MicRms, chunk.SysRms));
    }

    private void OnMixedSamplesAvailable(object? sender, float[] mixedSamples)
    {
        var pcmBytes = AudioResampler.FloatTo16BitPcm(mixedSamples);

        // Emitir a los suscriptores (transcriptor en tiempo real)
        MixedAudioChunkAvailable?.Invoke(this, pcmBytes);

        // Si estamos en modo grabación activa, persistir al archivo comprimido en streaming
        if (_isCapturing)
        {
            _recordingService.WriteChunk(mixedSamples);
        }
    }

    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_isCapturing || _isMonitoring) return Task.CompletedTask;

        try
        {
            _streamMixer.Clear();
            StartInternalCapture();
            _isMonitoring = true;
            _logger?.LogInformation("Modo de monitorización / prueba de audio iniciado.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al iniciar la monitorización de audio.");
            CaptureErrorOccurred?.Invoke(this, GetUserFriendlyErrorMessage(ex));
        }

        return Task.CompletedTask;
    }

    public Task StopMonitoringAsync()
    {
        if (!_isMonitoring) return Task.CompletedTask;

        StopInternalCapture();
        _isMonitoring = false;
        _streamMixer.Clear();

        // Resetear vúmetros a cero
        AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(AudioDeviceType.Microphone, 0f, 0f));
        AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(AudioDeviceType.SystemLoopback, 0f, 0f));

        _logger?.LogInformation("Modo de monitorización de audio detenido.");
        return Task.CompletedTask;
    }

    public Task StartCaptureAsync(string outputAudioFilePath, CancellationToken cancellationToken = default)
    {
        if (_isMonitoring)
        {
            StopInternalCapture();
            _isMonitoring = false;
        }

        try
        {
            _isMicrophoneMuted = false;
            _isSystemAudioMuted = false;
            _streamMixer.Clear();
            _recordingService.StartRecording(outputAudioFilePath);
            StartInternalCapture();
            _isCapturing = true;
            _logger?.LogInformation("Captura y grabación dual iniciada hacia: {Path}", outputAudioFilePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al iniciar la captura de audio.");
            CaptureErrorOccurred?.Invoke(this, GetUserFriendlyErrorMessage(ex));
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task StopCaptureAsync(CancellationToken cancellationToken = default)
    {
        if (!_isCapturing) return;

        StopInternalCapture();
        _isCapturing = false;
        _isMicrophoneMuted = false;
        _isSystemAudioMuted = false;

        _streamMixer.Flush(CaptureMicrophone, CaptureSystemAudio);
        _streamMixer.Clear();

        // Finalizar y comprimir la grabación
        await _recordingService.StopRecordingAsync(cancellationToken);

        // Resetear vúmetros
        AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(AudioDeviceType.Microphone, 0f, 0f));
        AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(AudioDeviceType.SystemLoopback, 0f, 0f));

        _logger?.LogInformation("Captura y grabación finalizada.");
    }

    private void StartInternalCapture()
    {
        using var enumerator = new MMDeviceEnumerator();

        // 1. Iniciar captura de micrófono si está habilitada
        if (CaptureMicrophone)
        {
            MMDevice? micDevice = null;
            if (SelectedMicrophone != null && !string.IsNullOrEmpty(SelectedMicrophone.Id))
            {
                try { micDevice = enumerator.GetDevice(SelectedMicrophone.Id); } catch { }
            }

            micDevice ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                          ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

            if (micDevice != null)
            {
                _micCapture = new WasapiCapture(micDevice);
                _micCapture.DataAvailable += OnMicDataAvailable;
                _micCapture.RecordingStopped += OnMicRecordingStopped;
                _micCapture.StartRecording();
            }
            else
            {
                _logger?.LogWarning("No se encontró micrófono disponible.");
            }
        }

        // 2. Iniciar captura de loopback de audio del sistema si está habilitada
        if (CaptureSystemAudio)
        {
            MMDevice? renderDevice = null;
            if (SelectedSystemDevice != null && !string.IsNullOrEmpty(SelectedSystemDevice.Id))
            {
                try { renderDevice = enumerator.GetDevice(SelectedSystemDevice.Id); } catch { }
            }

            renderDevice ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            if (renderDevice != null)
            {
                _sysCapture = new WasapiLoopbackCapture(renderDevice);
                _sysCapture.DataAvailable += OnSystemDataAvailable;
                _sysCapture.RecordingStopped += OnSystemRecordingStopped;
                _sysCapture.StartRecording();
            }
            else
            {
                _logger?.LogWarning("No se encontró dispositivo de reproducción para WASAPI Loopback.");
            }
        }
    }

    private void StopInternalCapture()
    {
        try
        {
            if (_micCapture != null)
            {
                _micCapture.DataAvailable -= OnMicDataAvailable;
                _micCapture.RecordingStopped -= OnMicRecordingStopped;
                _micCapture.StopRecording();
                _micCapture.Dispose();
                _micCapture = null;
            }

            if (_sysCapture != null)
            {
                _sysCapture.DataAvailable -= OnSystemDataAvailable;
                _sysCapture.RecordingStopped -= OnSystemRecordingStopped;
                _sysCapture.StopRecording();
                _sysCapture.Dispose();
                _sysCapture = null;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Excepción menor al liberar clientes WASAPI.");
        }
    }

    private void OnMicDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _micCapture == null) return;

        var rawFloatSamples = ExtractFloatSamples(e.Buffer, e.BytesRecorded, _micCapture.WaveFormat);
        var samples16k = AudioResampler.ConvertToStandard16kMono(rawFloatSamples, _micCapture.WaveFormat.SampleRate, _micCapture.WaveFormat.Channels);

        if (_isMicrophoneMuted)
        {
            // Notificar vúmetro con 0 para que no muestre actividad
            AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(AudioDeviceType.Microphone, 0f, 0f));

            // Remuestrear y generar silencio para mantener la sincronización temporal exacta en AudioStreamMixer
            Array.Clear(samples16k, 0, samples16k.Length);
            _streamMixer.EnqueueMicrophoneSamples(samples16k);

            // Detectar si el usuario está hablando mientras está silenciado (estilo Teams/Meet)
            var (mutedPeak, mutedRms) = _mixer.CalculateLevels(rawFloatSamples);
            if (mutedPeak > 0.08f || mutedRms > 0.02f)
            {
                SpeakingWhileMutedDetected?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        var (peak, rms) = _mixer.CalculateLevels(rawFloatSamples);

        // Notificar vúmetro
        AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(AudioDeviceType.Microphone, peak, rms));

        // Remuestrear a 16 kHz Mono e inyectar al mezclador de flujos
        _streamMixer.EnqueueMicrophoneSamples(samples16k);
    }

    private void OnSystemDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _sysCapture == null) return;

        var rawFloatSamples = ExtractFloatSamples(e.Buffer, e.BytesRecorded, _sysCapture.WaveFormat);
        var samples16k = AudioResampler.ConvertToStandard16kMono(rawFloatSamples, _sysCapture.WaveFormat.SampleRate, _sysCapture.WaveFormat.Channels);

        if (_isSystemAudioMuted)
        {
            // Notificar vúmetro con 0 para que no muestre actividad
            AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(AudioDeviceType.SystemLoopback, 0f, 0f));

            // Remuestrear y generar silencio para mantener la sincronización temporal exacta en AudioStreamMixer
            Array.Clear(samples16k, 0, samples16k.Length);
            _streamMixer.EnqueueSystemSamples(samples16k);
            return;
        }

        var (peak, rms) = _mixer.CalculateLevels(rawFloatSamples);

        // Notificar vúmetro
        AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs(AudioDeviceType.SystemLoopback, peak, rms));

        // Remuestrear a 16 kHz Mono e inyectar al mezclador de flujos
        _streamMixer.EnqueueSystemSamples(samples16k);
    }

    private static float[] ExtractFloatSamples(byte[] buffer, int bytesRecorded, WaveFormat waveFormat)
    {
        if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            var span = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytesRecorded));
            return span.ToArray();
        }
        else if (waveFormat.BitsPerSample == 16)
        {
            int sampleCount = bytesRecorded / 2;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                short val = BitConverter.ToInt16(buffer, i * 2);
                samples[i] = val / 32768f;
            }
            return samples;
        }

        return Array.Empty<float>();
    }

    private void OnMicRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _logger?.LogError(e.Exception, "Error en captura de micrófono.");
            CaptureErrorOccurred?.Invoke(this, "El micrófono seleccionado ha dejado de responder. Compruebe que el cable sigue conectado.");
        }
    }

    private void OnSystemRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _logger?.LogError(e.Exception, "Error en captura de audio del sistema.");
            CaptureErrorOccurred?.Invoke(this, "La captura de audio del sistema se ha detenido.");
        }
    }

    private static string GetUserFriendlyErrorMessage(Exception ex)
    {
        if (ex.Message.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("0x80070005"))
        {
            return "Windows ha denegado el acceso al micrófono. Por favor, active los permisos en Configuración de Privacidad de Windows.";
        }
        return $"No se puede acceder al dispositivo de audio seleccionado: {ex.Message}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopInternalCapture();
        _streamMixer.Clear();
        _recordingService.Dispose();
    }
}
