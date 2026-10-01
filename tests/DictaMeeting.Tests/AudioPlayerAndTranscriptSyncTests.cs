using System.Collections.ObjectModel;
using System.Diagnostics;
using DictaMeeting.App.Services;
using DictaMeeting.App.ViewModels;
using Xunit;

namespace DictaMeeting.Tests;

public class AudioPlayerAndTranscriptSyncTests
{
    [Fact]
    public void TranscriptAudioSyncService_SequentialPlayback_ActivatesCorrectSegments()
    {
        var syncService = new TranscriptAudioSyncService();
        var segments = new ObservableCollection<TranscriptSegmentViewModel>
        {
            new TranscriptSegmentViewModel { StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(5), Text = "Hola a todos" },
            new TranscriptSegmentViewModel { StartTime = TimeSpan.FromSeconds(5), EndTime = TimeSpan.FromSeconds(10), Text = "Buenos días" },
            new TranscriptSegmentViewModel { StartTime = TimeSpan.FromSeconds(10), EndTime = TimeSpan.FromSeconds(15), Text = "Comenzamos la reunión" }
        };
        syncService.SetSegments(segments);

        // En 2 segundos -> debe activar segmento 0
        bool changed0 = syncService.UpdateActiveSegment(TimeSpan.FromSeconds(2));
        Assert.True(changed0);
        Assert.NotNull(syncService.ActiveSegment);
        Assert.Equal("Hola a todos", syncService.ActiveSegment.Text);
        Assert.True(segments[0].IsActive);
        Assert.False(segments[1].IsActive);
        Assert.False(segments[2].IsActive);

        // En 3 segundos -> mismo segmento, UpdateActiveSegment retorna false y no cambia de segmento
        bool sameSegment = syncService.UpdateActiveSegment(TimeSpan.FromSeconds(3));
        Assert.False(sameSegment);
        Assert.Equal(segments[0], syncService.ActiveSegment);

        // En 7 segundos -> debe activar segmento 1 y desactivar segmento 0
        bool changed1 = syncService.UpdateActiveSegment(TimeSpan.FromSeconds(7));
        Assert.True(changed1);
        Assert.NotNull(syncService.ActiveSegment);
        Assert.Equal("Buenos días", syncService.ActiveSegment.Text);
        Assert.False(segments[0].IsActive);
        Assert.True(segments[1].IsActive);
        Assert.False(segments[2].IsActive);

        // En 12 segundos -> debe activar segmento 2
        bool changed2 = syncService.UpdateActiveSegment(TimeSpan.FromSeconds(12));
        Assert.True(changed2);
        Assert.NotNull(syncService.ActiveSegment);
        Assert.Equal("Comenzamos la reunión", syncService.ActiveSegment.Text);
        Assert.False(segments[1].IsActive);
        Assert.True(segments[2].IsActive);
    }

    [Fact]
    public void TranscriptAudioSyncService_Seeking_BinarySearch_JumpBackAndForward()
    {
        var syncService = new TranscriptAudioSyncService();
        var segments = new ObservableCollection<TranscriptSegmentViewModel>
        {
            new TranscriptSegmentViewModel { StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(30), Text = "Intro" },
            new TranscriptSegmentViewModel { StartTime = TimeSpan.FromSeconds(30), EndTime = TimeSpan.FromSeconds(60), Text = "Tema 1" },
            new TranscriptSegmentViewModel { StartTime = TimeSpan.FromSeconds(60), EndTime = TimeSpan.FromSeconds(90), Text = "Tema 2" },
            new TranscriptSegmentViewModel { StartTime = TimeSpan.FromSeconds(90), EndTime = TimeSpan.FromSeconds(120), Text = "Conclusiones" }
        };
        syncService.SetSegments(segments);

        // Salto hacia adelante al minuto 1:40 (100 segundos)
        var forwardSeg = syncService.FindSegmentAt(TimeSpan.FromSeconds(100));
        Assert.NotNull(forwardSeg);
        Assert.Equal("Conclusiones", forwardSeg.Text);

        syncService.UpdateActiveSegment(TimeSpan.FromSeconds(100));
        Assert.True(forwardSeg.IsActive);

        // Salto hacia atrás a los 45 segundos
        var backwardSeg = syncService.FindSegmentAt(TimeSpan.FromSeconds(45));
        Assert.NotNull(backwardSeg);
        Assert.Equal("Tema 1", backwardSeg.Text);

        syncService.UpdateActiveSegment(TimeSpan.FromSeconds(45));
        Assert.True(backwardSeg.IsActive);
        Assert.False(forwardSeg.IsActive);
    }

    [Fact]
    public void TranscriptAudioSyncService_SilenceGap_ReturnsNullAndClearsHighlight()
    {
        var syncService = new TranscriptAudioSyncService();
        var segments = new ObservableCollection<TranscriptSegmentViewModel>
        {
            new TranscriptSegmentViewModel { StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(5), Text = "Primera parte" },
            // Silencio de 5s a 10s
            new TranscriptSegmentViewModel { StartTime = TimeSpan.FromSeconds(10), EndTime = TimeSpan.FromSeconds(15), Text = "Segunda parte" }
        };
        syncService.SetSegments(segments);

        // Activar primera parte
        syncService.UpdateActiveSegment(TimeSpan.FromSeconds(3));
        Assert.NotNull(syncService.ActiveSegment);
        Assert.True(segments[0].IsActive);

        // Caer en el hueco de silencio (7 segundos)
        var silenceSeg = syncService.FindSegmentAt(TimeSpan.FromSeconds(7));
        Assert.Null(silenceSeg);

        bool changedToSilence = syncService.UpdateActiveSegment(TimeSpan.FromSeconds(7));
        Assert.True(changedToSilence);
        Assert.Null(syncService.ActiveSegment);
        Assert.False(segments[0].IsActive);
        Assert.False(segments[1].IsActive);

        // Pasar a la segunda parte
        syncService.UpdateActiveSegment(TimeSpan.FromSeconds(12));
        Assert.NotNull(syncService.ActiveSegment);
        Assert.True(segments[1].IsActive);
    }

    [Fact]
    public void TranscriptAudioSyncService_EmptyOrNullList_ReturnsNullGracefully()
    {
        var syncService = new TranscriptAudioSyncService();
        var empty = new ObservableCollection<TranscriptSegmentViewModel>();
        syncService.SetSegments(empty);

        Assert.Null(syncService.FindSegmentAt(TimeSpan.FromSeconds(5)));
        Assert.False(syncService.UpdateActiveSegment(TimeSpan.FromSeconds(5)));

        syncService.SetSegments(null!);
        Assert.Null(syncService.FindSegmentAt(TimeSpan.FromSeconds(5)));
        Assert.False(syncService.UpdateActiveSegment(TimeSpan.FromSeconds(5)));

        syncService.Clear();
        Assert.Null(syncService.ActiveSegment);
    }

    [Fact]
    public void TranscriptAudioSyncService_SetActiveSegment_ExplicitlyControlsHighlight()
    {
        var syncService = new TranscriptAudioSyncService();
        var seg1 = new TranscriptSegmentViewModel { Text = "Uno", StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(10) };
        var seg2 = new TranscriptSegmentViewModel { Text = "Dos", StartTime = TimeSpan.FromSeconds(10), EndTime = TimeSpan.FromSeconds(20) };
        syncService.SetSegments(new[] { seg1, seg2 });

        syncService.SetActiveSegment(seg1);
        Assert.Equal(seg1, syncService.ActiveSegment);
        Assert.True(seg1.IsActive);
        Assert.False(seg2.IsActive);

        syncService.SetActiveSegment(seg2);
        Assert.Equal(seg2, syncService.ActiveSegment);
        Assert.False(seg1.IsActive);
        Assert.True(seg2.IsActive);

        syncService.SetActiveSegment(null);
        Assert.Null(syncService.ActiveSegment);
        Assert.False(seg2.IsActive);
    }

    [Fact]
    public void TranscriptAudioSyncService_HighVolumePerformance_ExecutesInstantly()
    {
        var syncService = new TranscriptAudioSyncService();
        var segments = new List<TranscriptSegmentViewModel>();

        // Crear 3.000 segmentos (reunión de más de 2 horas con frases cada 2-3 segundos)
        for (int i = 0; i < 3000; i++)
        {
            segments.Add(new TranscriptSegmentViewModel
            {
                StartTime = TimeSpan.FromSeconds(i * 3.0),
                EndTime = TimeSpan.FromSeconds((i * 3.0) + 2.5),
                Text = $"Intervención número {i}"
            });
        }
        syncService.SetSegments(segments);

        var sw = Stopwatch.StartNew();

        // Realizar 10.000 consultas mezclando reproducción secuencial y saltos aleatorios
        var random = new Random(42);
        for (int i = 0; i < 10000; i++)
        {
            if (i % 10 == 0)
            {
                // Salto aleatorio
                double sec = random.NextDouble() * 9000.0;
                syncService.FindSegmentAt(TimeSpan.FromSeconds(sec));
            }
            else
            {
                // Avance secuencial
                double sec = (i % 3000) * 3.0 + 1.0;
                syncService.FindSegmentAt(TimeSpan.FromSeconds(sec));
            }
        }

        sw.Stop();

        // 10.000 búsquedas en 3.000 segmentos deben tardar menos de 200 milisegundos
        Assert.True(sw.ElapsedMilliseconds < 200, $"Expected < 200ms, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void AudioPlayerService_InitialState_And_VolumeMuteLogic()
    {
        var player = new AudioPlayerService();

        Assert.Equal(TimeSpan.Zero, player.Position);
        Assert.Equal(TimeSpan.Zero, player.Duration);
        Assert.Equal(1.0, player.Volume);
        Assert.False(player.IsMuted);
        Assert.False(player.IsPlaying);

        // Clamping del volumen
        player.Volume = 1.5;
        Assert.Equal(1.0, player.Volume);

        player.Volume = -0.5;
        Assert.Equal(0.0, player.Volume);

        player.Volume = 0.65;
        Assert.Equal(0.65, player.Volume);

        // Mute
        player.IsMuted = true;
        Assert.True(player.IsMuted);

        // Unmute
        player.IsMuted = false;
        Assert.False(player.IsMuted);
        Assert.Equal(0.65, player.Volume);

        player.Dispose();
    }

    [Fact]
    public void AudioPlayerService_Load_NonExistentFile_DoesNotThrow()
    {
        var player = new AudioPlayerService();

        // Archivo que no existe debe manejarse limpiamente sin lanzar excepciones
        player.Load("C:\\NonExistentPath\\FakeAudio12345.wav");

        Assert.False(player.HasAudio);
        Assert.False(player.IsPlaying);
        Assert.Equal(TimeSpan.Zero, player.Duration);

        // Operaciones de Play/Pause/Seek no deben lanzar excepciones
        player.Play();
        player.Pause();
        player.Seek(TimeSpan.FromSeconds(10));
        player.Stop();
        player.Close();

        player.Dispose();
    }
}
