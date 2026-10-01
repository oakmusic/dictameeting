using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.App.Services;

/// <summary>
/// Implementación nativa y ligera de <see cref="IAudioPlayerService"/> utilizando System.Windows.Media.MediaPlayer.
/// Adecuada para reproducir archivos de audio locales (.wav, .mp3, .m4a, etc.) sin dependencias externas pesadas.
/// </summary>
public sealed class AudioPlayerService : IAudioPlayerService
{
    private readonly MediaPlayer _player;
    private readonly DispatcherTimer _timer;
    private readonly ILogger<AudioPlayerService>? _logger;

    private string? _currentAudioFilePath;
    private TimeSpan _fallbackDuration = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;
    private TimeSpan _position = TimeSpan.Zero;
    private bool _hasAudio;
    private bool _isPlaying;
    private bool _disposed;

    public bool HasAudio
    {
        get => _hasAudio;
        private set => _hasAudio = value;
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => _isPlaying = value;
    }

    public string? CurrentAudioFilePath => _currentAudioFilePath;

    public TimeSpan Position => _position;

    public TimeSpan Duration => _duration;

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0.0, 1.0);
    }

    public bool IsMuted
    {
        get => _player.IsMuted;
        set => _player.IsMuted = value;
    }

    public event EventHandler? PositionChanged;
    public event EventHandler? PlaybackStateChanged;
    public event EventHandler? MediaOpened;
    public event EventHandler? MediaEnded;

    public AudioPlayerService(ILogger<AudioPlayerService>? logger = null)
    {
        _logger = logger;
        _player = new MediaPlayer { Volume = 1.0 };
        _player.MediaOpened += OnMediaOpened;
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timer.Tick += OnTimerTick;
    }

    public void Load(string? audioFilePath, TimeSpan? fallbackDuration = null)
    {
        Close();

        _currentAudioFilePath = audioFilePath;
        _fallbackDuration = fallbackDuration ?? TimeSpan.Zero;
        _duration = _fallbackDuration;
        _position = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(audioFilePath) || !File.Exists(audioFilePath))
        {
            HasAudio = false;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            HasAudio = true;
            _player.Open(new Uri(audioFilePath, UriKind.Absolute));
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al abrir archivo de audio local: {Path}", audioFilePath);
            HasAudio = false;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Play()
    {
        if (!HasAudio || _isPlaying) return;

        try
        {
            _player.Play();
            IsPlaying = true;
            _timer.Start();
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al iniciar reproducción de audio");
        }
    }

    public void Pause()
    {
        if (!_isPlaying) return;

        try
        {
            _player.Pause();
            IsPlaying = false;
            _timer.Stop();
            // Actualizar la última posición precisa
            _position = _player.Position;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al pausar reproducción de audio");
        }
    }

    public void TogglePlayPause()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Stop()
    {
        try
        {
            _player.Stop();
        }
        catch { }

        IsPlaying = false;
        _timer.Stop();
        _position = TimeSpan.Zero;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Seek(TimeSpan position)
    {
        if (!HasAudio) return;

        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (_duration > TimeSpan.Zero && position > _duration) position = _duration;

        try
        {
            _player.Position = position;
            _position = position;
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al posicionar cursor de audio en {Pos}", position);
        }
    }

    public void SeekRelative(TimeSpan offset)
    {
        Seek(_position + offset);
    }

    public void Close()
    {
        try
        {
            if (_isPlaying)
            {
                _player.Pause();
            }
            _player.Stop();
            _player.Close();
        }
        catch { }

        _timer.Stop();
        _isPlaying = false;
        _hasAudio = false;
        _currentAudioFilePath = null;
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;

        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_isPlaying || !HasAudio) return;

        try
        {
            var newPos = _player.Position;
            if (newPos != _position)
            {
                _position = newPos;
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch { }
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        try
        {
            if (_player.NaturalDuration.HasTimeSpan && _player.NaturalDuration.TimeSpan > TimeSpan.Zero)
            {
                _duration = _player.NaturalDuration.TimeSpan;
            }
            else if (_fallbackDuration > TimeSpan.Zero)
            {
                _duration = _fallbackDuration;
            }
        }
        catch
        {
            if (_fallbackDuration > TimeSpan.Zero)
            {
                _duration = _fallbackDuration;
            }
        }

        MediaOpened?.Invoke(this, EventArgs.Empty);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        Pause();
        Seek(TimeSpan.Zero);
        MediaEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaFailed(object? sender, ExceptionEventArgs e)
    {
        _logger?.LogWarning(e.ErrorException, "Fallo al procesar o decodificar audio con MediaPlayer");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Close();
        _timer.Tick -= OnTimerTick;
        _player.MediaOpened -= OnMediaOpened;
        _player.MediaEnded -= OnMediaEnded;
        _player.MediaFailed -= OnMediaFailed;
    }
}
