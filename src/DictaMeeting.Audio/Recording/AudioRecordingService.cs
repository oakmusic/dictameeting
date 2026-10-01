using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Processing;
using Microsoft.Extensions.Logging;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace DictaMeeting.Audio.Recording;

public class AudioRecordingService : IAudioRecordingService
{
    private readonly ILogger<AudioRecordingService>? _logger;
    private static bool _mediaFoundationInitialized;
    private static readonly object MfLock = new();

    private WaveFileWriter? _tempWriter;
    private string? _tempWavPath;
    private string? _destinationFilePath;
    private bool _isRecording;
    private bool _disposed;

    public bool IsRecording => _isRecording;
    public string? CurrentOutputFilePath => _destinationFilePath;

    public AudioRecordingService(ILogger<AudioRecordingService>? logger = null)
    {
        _logger = logger;
        EnsureMediaFoundationInitialized();
    }

    private static void EnsureMediaFoundationInitialized()
    {
        lock (MfLock)
        {
            if (!_mediaFoundationInitialized)
            {
                try
                {
                    MediaFoundationApi.Startup();
                    _mediaFoundationInitialized = true;
                }
                catch
                {
                    // Media Foundation puede no estar disponible en ciertas ediciones Server
                }
            }
        }
    }

    public void StartRecording(string destinationCompressedFilePath)
    {
        if (_isRecording)
        {
            throw new InvalidOperationException("Ya hay una grabación en curso.");
        }

        _destinationFilePath = destinationCompressedFilePath;

        // Asegurar que el directorio de destino exista
        var destinationDir = Path.GetDirectoryName(destinationCompressedFilePath);
        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        // Crear archivo temporal en el directorio temporal del usuario
        var tempFolder = Path.Combine(Path.GetTempPath(), "DictaMeeting");
        if (!Directory.Exists(tempFolder))
        {
            Directory.CreateDirectory(tempFolder);
        }

        _tempWavPath = Path.Combine(tempFolder, $"recording_{Guid.NewGuid():N}.wav");
        var waveFormat = new WaveFormat(AudioResampler.TargetSampleRate, 16, 1);
        _tempWriter = new WaveFileWriter(_tempWavPath, waveFormat);

        _isRecording = true;
        _logger?.LogInformation("Grabación en streaming iniciada. Archivo temporal: {TempPath}", _tempWavPath);
    }

    public void WriteChunk(ReadOnlySpan<float> floatSamples)
    {
        if (!_isRecording || _tempWriter == null) return;

        var pcmBytes = AudioResampler.FloatTo16BitPcm(floatSamples);
        _tempWriter.Write(pcmBytes, 0, pcmBytes.Length);
    }

    public async Task<string> StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRecording) return _destinationFilePath ?? string.Empty;

        _isRecording = false;

        if (_tempWriter != null)
        {
            _tempWriter.Flush();
            _tempWriter.Dispose();
            _tempWriter = null;
        }

        if (string.IsNullOrEmpty(_tempWavPath) || !File.Exists(_tempWavPath) || string.IsNullOrEmpty(_destinationFilePath))
        {
            return string.Empty;
        }

        var tempWav = _tempWavPath;
        var destination = _destinationFilePath;

        await Task.Run(() =>
        {
            try
            {
                _logger?.LogInformation("Codificando audio a formato comprimido: {Destination}", destination);

                var extension = Path.GetExtension(destination).ToLowerInvariant();
                using var reader = new WaveFileReader(tempWav);

                if (extension == ".mp3")
                {
                    MediaFoundationEncoder.EncodeToMp3(reader, destination, desiredBitRate: 64000);
                }
                else
                {
                    // AAC / M4A por defecto para voz
                    MediaFoundationEncoder.EncodeToAac(reader, destination, desiredBitRate: 64000);
                }

                _logger?.LogInformation("Grabación comprimida generada con éxito ({Size} KB).", new FileInfo(destination).Length / 1024);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Fallo en la codificación Media Foundation. Copiando audio original como respaldo.");
                // Si falla la compresión, asegurar que el archivo no se pierde renombrando el WAV
                var fallbackPath = Path.ChangeExtension(destination, ".wav");
                File.Copy(tempWav, fallbackPath, overwrite: true);
                destination = fallbackPath;
            }
            finally
            {
                // Limpiar siempre el archivo temporal de grabación
                try
                {
                    if (File.Exists(tempWav))
                    {
                        File.Delete(tempWav);
                    }
                }
                catch { }
            }
        }, cancellationToken);

        return destination;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_isRecording)
        {
            _tempWriter?.Dispose();
            _tempWriter = null;
            _isRecording = false;
        }
    }
}
