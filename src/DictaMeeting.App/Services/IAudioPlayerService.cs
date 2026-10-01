namespace DictaMeeting.App.Services;

/// <summary>
/// Contrato para el reproductor de audio local nativo de WPF/.NET.
/// </summary>
public interface IAudioPlayerService : IDisposable
{
    /// <summary>
    /// Indica si hay un archivo de audio cargado y disponible para reproducir.
    /// </summary>
    bool HasAudio { get; }

    /// <summary>
    /// Indica si la reproducción está activa en este momento.
    /// </summary>
    bool IsPlaying { get; }

    /// <summary>
    /// Ruta completa al archivo de audio local actual.
    /// </summary>
    string? CurrentAudioFilePath { get; }

    /// <summary>
    /// Posición actual de reproducción en el audio.
    /// </summary>
    TimeSpan Position { get; }

    /// <summary>
    /// Duración total del audio cargado.
    /// </summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Volumen actual de reproducción (rango normalizado 0.0 a 1.0).
    /// </summary>
    double Volume { get; set; }

    /// <summary>
    /// Indica si el sonido del reproductor está silenciado.
    /// </summary>
    bool IsMuted { get; set; }

    /// <summary>
    /// Carga un archivo de audio local para su reproducción.
    /// </summary>
    /// <param name="audioFilePath">Ruta al archivo de audio.</param>
    /// <param name="fallbackDuration">Duración de respaldo si el archivo tarda en reportarla.</param>
    void Load(string? audioFilePath, TimeSpan? fallbackDuration = null);

    /// <summary>
    /// Inicia o reanuda la reproducción.
    /// </summary>
    void Play();

    /// <summary>
    /// Pausa la reproducción en la posición actual.
    /// </summary>
    void Pause();

    /// <summary>
    /// Alterna entre reproducir y pausar.
    /// </summary>
    void TogglePlayPause();

    /// <summary>
    /// Detiene la reproducción y reinicia la posición a cero.
    /// </summary>
    void Stop();

    /// <summary>
    /// Desplaza la reproducción a una posición absoluta.
    /// </summary>
    void Seek(TimeSpan position);

    /// <summary>
    /// Desplaza la reproducción una cantidad relativa de tiempo (ej. -5s o +5s).
    /// </summary>
    void SeekRelative(TimeSpan offset);

    /// <summary>
    /// Detiene y libera los recursos del reproductor y el archivo de audio.
    /// </summary>
    void Close();

    /// <summary>
    /// Se dispara periódicamente durante la reproducción o tras un salto (seek) de posición.
    /// </summary>
    event EventHandler? PositionChanged;

    /// <summary>
    /// Se dispara cuando cambia el estado de reproducción (Play/Pause/Stop).
    /// </summary>
    event EventHandler? PlaybackStateChanged;

    /// <summary>
    /// Se dispara cuando el archivo de audio se ha abierto correctamente y su duración está disponible.
    /// </summary>
    event EventHandler? MediaOpened;

    /// <summary>
    /// Se dispara cuando el audio alcanza el final del archivo.
    /// </summary>
    event EventHandler? MediaEnded;
}
