using System.Collections.ObjectModel;
using System.Globalization;
using DictaMeeting.AI.Services;
using DictaMeeting.App.Services;
using DictaMeeting.App.ViewModels;
using DictaMeeting.Audio.Services;
using DictaMeeting.Diarization.Pipeline;
using DictaMeeting.Diarization.Services;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Services;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class SpeakerFirstUtteranceNavigationTests
{
    private sealed class TestAudioPlayer : IAudioPlayerService
    {
        public bool HasAudio { get; set; } = true;
        public bool IsPlaying { get; set; }
        public string? CurrentAudioFilePath { get; set; } = "C:\\test\\meeting.wav";
        public TimeSpan Position { get; set; } = TimeSpan.Zero;
        public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(10);
        public double Volume { get; set; } = 1.0;
        public bool IsMuted { get; set; }

        public event EventHandler? PositionChanged;
        public event EventHandler? PlaybackStateChanged;
        public event EventHandler? MediaOpened;
        public event EventHandler? MediaEnded;

        public void Load(string? audioFilePath, TimeSpan? fallbackDuration = null)
        {
            CurrentAudioFilePath = audioFilePath;
            HasAudio = !string.IsNullOrEmpty(audioFilePath);
            if (fallbackDuration.HasValue) Duration = fallbackDuration.Value;
            MediaOpened?.Invoke(this, EventArgs.Empty);
        }

        public void Play()
        {
            IsPlaying = true;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            IsPlaying = false;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void TogglePlayPause()
        {
            if (IsPlaying) Pause();
            else Play();
        }

        public void Stop()
        {
            IsPlaying = false;
            Position = TimeSpan.Zero;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Seek(TimeSpan position)
        {
            if (!HasAudio) return;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (Duration > TimeSpan.Zero && position > Duration) position = Duration;

            Position = position;
            PositionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SeekRelative(TimeSpan offset)
        {
            Seek(Position + offset);
        }

        public void Close()
        {
            HasAudio = false;
            IsPlaying = false;
        }

        public void Dispose()
        {
            Close();
        }
    }

    private static (MainViewModel vm, TestAudioPlayer player, TranscriptAudioSyncService syncService) CreateFixture()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"vm_spk_nav_{Guid.NewGuid():N}.json");
        var userSettings = new UserSettingsService(tempFile);
        var repo = new MeetingWorkspaceNavigationTests.InMemoryRepo();
        var meetingService = new MeetingService(repo);
        var docExporter = new DocumentExporter();
        var audioDeviceService = new AudioDeviceService();
        var audioCaptureService = new MockAudioCaptureService();
        var modelManager = new TwoPassModelSelectionTests.DummyModelManager();
        var transcriptionService = new MockTranscriptionService();
        var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
        var diarizationService = new PyAnnoteCommunity1DiarizationService();
        var speakerAligner = new SpeakerTranscriptAligner();
        var secureStorage = new WindowsDpapiSecureStorageService();
        var aiActaService = new OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

        var player = new TestAudioPlayer();
        var syncService = new TranscriptAudioSyncService();

        var vm = new MainViewModel(
            meetingService,
            docExporter,
            repo,
            audioDeviceService,
            audioCaptureService,
            modelManager,
            transcriptionService,
            livePipeline,
            diarizationService,
            speakerAligner,
            aiActaService,
            secureStorage,
            userSettings,
            audioPlayerService: player,
            transcriptAudioSyncService: syncService);

        return (vm, player, syncService);
    }

    [Fact]
    public void Test1_SpeakerWithSingleUtterance_NavigatesSelectsAndSeeks()
    {
        var (vm, player, syncService) = CreateFixture();

        var spk = new SpeakerViewModel("SPEAKER_00", "Carlos", "#0071E3");
        vm.Participants.Add(spk);

        var seg = new TranscriptSegmentViewModel
        {
            Id = "seg-1",
            SpeakerId = "SPEAKER_00",
            SpeakerDisplayName = "Carlos",
            StartTime = TimeSpan.FromSeconds(25),
            EndTime = TimeSpan.FromSeconds(30),
            Text = "Hola a todos, bienvenidos."
        };
        vm.TranscriptSegments.Add(seg);
        syncService.SetSegments(vm.TranscriptSegments);

        TranscriptSegmentViewModel? requestedScrollSeg = null;
        vm.RequestScrollToSegment += (_, s) => requestedScrollSeg = s;

        // Act
        vm.NavigateToFirstSpeakerUtterance(spk);

        // Assert
        Assert.True(seg.IsActive);
        Assert.True(spk.IsSelected);
        Assert.Equal(seg, syncService.ActiveSegment);
        Assert.Equal(seg, requestedScrollSeg);
        Assert.Equal(TimeSpan.FromSeconds(25), player.Position);
        Assert.Equal(25.0, vm.AudioSliderValue);
    }

    [Fact]
    public void Test2_SpeakerWithMultipleUtterances_SelectsChronologicallyFirst()
    {
        var (vm, player, syncService) = CreateFixture();

        var spk = new SpeakerViewModel("SPEAKER_01", "Elena", "#10B981");
        vm.Participants.Add(spk);

        var seg1 = new TranscriptSegmentViewModel
        {
            Id = "seg-1",
            SpeakerId = "SPEAKER_01",
            StartTime = TimeSpan.FromSeconds(15),
            EndTime = TimeSpan.FromSeconds(20),
            Text = "Primera intervención."
        };
        var seg2 = new TranscriptSegmentViewModel
        {
            Id = "seg-2",
            SpeakerId = "SPEAKER_01",
            StartTime = TimeSpan.FromSeconds(45),
            EndTime = TimeSpan.FromSeconds(50),
            Text = "Segunda intervención."
        };
        var seg3 = new TranscriptSegmentViewModel
        {
            Id = "seg-3",
            SpeakerId = "SPEAKER_01",
            StartTime = TimeSpan.FromSeconds(90),
            EndTime = TimeSpan.FromSeconds(95),
            Text = "Tercera intervención."
        };

        vm.TranscriptSegments.Add(seg1);
        vm.TranscriptSegments.Add(seg2);
        vm.TranscriptSegments.Add(seg3);
        syncService.SetSegments(vm.TranscriptSegments);

        // Act
        vm.NavigateToFirstSpeakerUtterance(spk);

        // Assert: la primera cronológicamente es seg1 (15s)
        Assert.True(seg1.IsActive);
        Assert.False(seg2.IsActive);
        Assert.False(seg3.IsActive);
        Assert.Equal(TimeSpan.FromSeconds(15), player.Position);
    }

    [Fact]
    public void Test3_UnorderedCollection_SelectsChronologicallyEarliest_NotFirstInCollection()
    {
        var (vm, player, syncService) = CreateFixture();

        var spk = new SpeakerViewModel("SPEAKER_00", "Carlos", "#0071E3");
        vm.Participants.Add(spk);

        // Insertadas desordenadas intencionadamente
        var lateSeg = new TranscriptSegmentViewModel
        {
            Id = "seg-late",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(120),
            EndTime = TimeSpan.FromSeconds(130),
            Text = "Intervención tardía pero primera en la colección."
        };
        var earliestSeg = new TranscriptSegmentViewModel
        {
            Id = "seg-early",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(12),
            EndTime = TimeSpan.FromSeconds(18),
            Text = "Intervención más antigua cronológicamente."
        };
        var midSeg = new TranscriptSegmentViewModel
        {
            Id = "seg-mid",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(60),
            EndTime = TimeSpan.FromSeconds(70),
            Text = "Intervención intermedia."
        };

        // Orden en la colección: 120s, 12s, 60s
        vm.TranscriptSegments.Add(lateSeg);
        vm.TranscriptSegments.Add(earliestSeg);
        vm.TranscriptSegments.Add(midSeg);
        syncService.SetSegments(vm.TranscriptSegments);

        // Act
        vm.NavigateToFirstSpeakerUtterance(spk);

        // Assert: DEBE seleccionar earliestSeg (12s), ignorando que lateSeg está primero en la lista
        Assert.True(earliestSeg.IsActive);
        Assert.False(lateSeg.IsActive);
        Assert.False(midSeg.IsActive);
        Assert.Equal(TimeSpan.FromSeconds(12), player.Position);
    }

    [Fact]
    public void Test4_IdentificationUsesInternalSpeakerId_NotDisplayName()
    {
        var (vm, player, syncService) = CreateFixture();

        // El speaker tiene Id interno "SPEAKER_00", pero display name "Beatriz"
        var spk0 = new SpeakerViewModel("SPEAKER_00", "Beatriz", "#0071E3");
        // Otro speaker tiene Id "SPEAKER_01", pero displayName "Carlos"
        var spk1 = new SpeakerViewModel("SPEAKER_01", "Carlos", "#10B981");

        vm.Participants.Add(spk0);
        vm.Participants.Add(spk1);

        var segSpk0 = new TranscriptSegmentViewModel
        {
            Id = "seg-0",
            SpeakerId = "SPEAKER_00", // Coincide con Id interno de spk0
            SpeakerDisplayName = "Nombre Desactualizado",
            StartTime = TimeSpan.FromSeconds(35),
            Text = "Intervención de SPEAKER_00"
        };
        var segSpk1 = new TranscriptSegmentViewModel
        {
            Id = "seg-1",
            SpeakerId = "SPEAKER_01",
            SpeakerDisplayName = "Nombre Desactualizado",
            StartTime = TimeSpan.FromSeconds(10),
            Text = "Intervención de SPEAKER_01"
        };

        vm.TranscriptSegments.Add(segSpk0);
        vm.TranscriptSegments.Add(segSpk1);
        syncService.SetSegments(vm.TranscriptSegments);

        // Act: navegar a spk0 por objeto (su Id interno es SPEAKER_00)
        vm.NavigateToFirstSpeakerUtterance(spk0);

        // Assert: debe localizar segSpk0 (35s), no segSpk1 (10s)
        Assert.True(segSpk0.IsActive);
        Assert.False(segSpk1.IsActive);
        Assert.Equal(TimeSpan.FromSeconds(35), player.Position);
        Assert.True(spk0.IsSelected);
        Assert.False(spk1.IsSelected);
    }

    [Fact]
    public void Test5_ExactAudioPosition_And_SliderAndTimeIndicatorUpdated()
    {
        var (vm, player, syncService) = CreateFixture();

        var spk = new SpeakerViewModel("SPEAKER_00", "David", "#8B5CF6");
        vm.Participants.Add(spk);

        var seg = new TranscriptSegmentViewModel
        {
            Id = "seg-precision",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(142.5),
            EndTime = TimeSpan.FromSeconds(148.0),
            Text = "Timestamp de precisión."
        };
        vm.TranscriptSegments.Add(seg);
        syncService.SetSegments(vm.TranscriptSegments);

        // Act
        vm.NavigateToFirstSpeakerUtterance(spk);

        // Assert: posición exacta y slider
        Assert.Equal(TimeSpan.FromSeconds(142.5), player.Position);
        Assert.Equal(142.5, vm.AudioSliderValue);
        Assert.Contains("02:22", vm.AudioPositionText); // 142s = 02:22
    }

    [Fact]
    public void Test6_PreservesPlaybackState_WhenPlaying_ContinuesPlaying()
    {
        var (vm, player, syncService) = CreateFixture();

        var spk = new SpeakerViewModel("SPEAKER_00", "Elena", "#10B981");
        vm.Participants.Add(spk);

        var seg = new TranscriptSegmentViewModel
        {
            Id = "seg-1",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(50),
            Text = "Prueba de reproducción activa."
        };
        vm.TranscriptSegments.Add(seg);
        syncService.SetSegments(vm.TranscriptSegments);

        // Configurar reproductor como REPRODUCIENDO
        player.Play();
        player.Volume = 0.85;
        Assert.True(player.IsPlaying);

        // Act
        vm.NavigateToFirstSpeakerUtterance(spk);

        // Assert: sigue reproduciendo, posición actualizada, volumen intacto
        Assert.True(player.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(50), player.Position);
        Assert.Equal(0.85, player.Volume);
    }

    [Fact]
    public void Test7_PreservesPlaybackState_WhenPaused_RemainsPaused()
    {
        var (vm, player, syncService) = CreateFixture();

        var spk = new SpeakerViewModel("SPEAKER_00", "Elena", "#10B981");
        vm.Participants.Add(spk);

        var seg = new TranscriptSegmentViewModel
        {
            Id = "seg-1",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(50),
            Text = "Prueba de reproducción en pausa."
        };
        vm.TranscriptSegments.Add(seg);
        syncService.SetSegments(vm.TranscriptSegments);

        // Configurar reproductor como PAUSADO
        player.Pause();
        player.Volume = 0.70;
        Assert.False(player.IsPlaying);

        // Act
        vm.NavigateToFirstSpeakerUtterance(spk);

        // Assert: sigue pausado, posición actualizada, volumen intacto
        Assert.False(player.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(50), player.Position);
        Assert.Equal(0.70, player.Volume);
    }

    [Fact]
    public void Test8_SpeakerWithoutUtterances_DoesNotChangeSelectionOrAudio()
    {
        var (vm, player, syncService) = CreateFixture();

        var spkWithUtterances = new SpeakerViewModel("SPEAKER_00", "Presente", "#0071E3");
        var spkGhost = new SpeakerViewModel("SPEAKER_GHOST", "Sin Intervenciones", "#94A3B8");

        vm.Participants.Add(spkWithUtterances);
        vm.Participants.Add(spkGhost);

        var existingSeg = new TranscriptSegmentViewModel
        {
            Id = "seg-init",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(30),
            Text = "Intervención existente."
        };
        vm.TranscriptSegments.Add(existingSeg);
        syncService.SetSegments(vm.TranscriptSegments);

        // Seleccionar inicialmente el segmento de SPEAKER_00
        syncService.SetActiveSegment(existingSeg);
        player.Seek(TimeSpan.FromSeconds(30));
        spkWithUtterances.IsSelected = true;

        bool scrollRequested = false;
        vm.RequestScrollToSegment += (_, _) => scrollRequested = true;

        // Act: intentar navegar al speaker sin intervenciones
        vm.NavigateToFirstSpeakerUtterance(spkGhost);

        // Assert: NO debe cambiar nada
        Assert.Equal(existingSeg, syncService.ActiveSegment);
        Assert.True(existingSeg.IsActive);
        Assert.True(spkWithUtterances.IsSelected);
        Assert.False(spkGhost.IsSelected);
        Assert.Equal(TimeSpan.FromSeconds(30), player.Position);
        Assert.False(scrollRequested);
    }

    [Fact]
    public void Test9_AudioPlayback_SynchronizesSpeakerCardSelection()
    {
        var (vm, player, syncService) = CreateFixture();

        var spk0 = new SpeakerViewModel("SPEAKER_00", "Hablante 1", "#0071E3");
        var spk1 = new SpeakerViewModel("SPEAKER_01", "Hablante 2", "#10B981");
        vm.Participants.Add(spk0);
        vm.Participants.Add(spk1);

        var seg0 = new TranscriptSegmentViewModel
        {
            Id = "s0",
            SpeakerId = "SPEAKER_00",
            StartTime = TimeSpan.FromSeconds(0),
            EndTime = TimeSpan.FromSeconds(10),
            Text = "Segmento 0"
        };
        var seg1 = new TranscriptSegmentViewModel
        {
            Id = "s1",
            SpeakerId = "SPEAKER_01",
            StartTime = TimeSpan.FromSeconds(10),
            EndTime = TimeSpan.FromSeconds(20),
            Text = "Segmento 1"
        };
        vm.TranscriptSegments.Add(seg0);
        vm.TranscriptSegments.Add(seg1);
        syncService.SetSegments(vm.TranscriptSegments);

        // 1. Audio en 5s -> Segmento 0 activo -> spk0 seleccionado
        syncService.UpdateActiveSegment(TimeSpan.FromSeconds(5));
        Assert.True(spk0.IsSelected);
        Assert.False(spk1.IsSelected);

        // 2. Audio avanza a 15s -> Segmento 1 activo -> spk1 seleccionado, spk0 deseleccionado
        syncService.UpdateActiveSegment(TimeSpan.FromSeconds(15));
        Assert.False(spk0.IsSelected);
        Assert.True(spk1.IsSelected);

        // 3. Audio llega a silencio a los 25s -> Ninguno seleccionado
        syncService.UpdateActiveSegment(TimeSpan.FromSeconds(25));
        Assert.False(spk0.IsSelected);
        Assert.False(spk1.IsSelected);
    }

    [Fact]
    public void Test10_SpeakerRenaming_And_DropdownInteraction_DoesNotTriggerNavigation()
    {
        var (vm, player, syncService) = CreateFixture();

        bool nameCommitted = false;
        string committedId = string.Empty;
        string committedName = string.Empty;

        var availableReal = new ObservableCollection<string> { "Carlos Santana", "Ana López" };

        var spk = new SpeakerViewModel(
            "SPEAKER_00",
            "Carlos",
            "#0071E3",
            onNameCommitted: (id, name) =>
            {
                nameCommitted = true;
                committedId = id;
                committedName = name;
            },
            availableRealParticipants: availableReal);

        vm.Participants.Add(spk);

        var seg = new TranscriptSegmentViewModel
        {
            Id = "seg-1",
            SpeakerId = "SPEAKER_00",
            SpeakerDisplayName = "Carlos",
            StartTime = TimeSpan.FromSeconds(40),
            Text = "Hola a todos."
        };
        vm.TranscriptSegments.Add(seg);
        syncService.SetSegments(vm.TranscriptSegments);

        player.Seek(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(10), player.Position);

        // Act: El usuario interactúa con el desplegable seleccionando un nombre real
        spk.SelectedRealParticipant = "Ana López";

        // Assert: El renombrado se confirma, pero la posición de audio y selección no se alteran
        Assert.True(nameCommitted);
        Assert.Equal("SPEAKER_00", committedId);
        Assert.Equal("Ana López", committedName);
        Assert.Equal("Ana López", spk.DisplayName);

        // El reproductor NO debe haberse desplazado accidentalmente a los 40s
        Assert.Equal(TimeSpan.FromSeconds(10), player.Position);
        Assert.False(seg.IsActive);
    }

    [Fact]
    public void Test11_VirtualizedList_OffsetEstimation_IsBoundedAndSafe()
    {
        var (vm, player, syncService) = CreateFixture();
        player.Duration = TimeSpan.FromHours(2);

        // Simular reunión extensa con 1000 intervenciones
        var spk = new SpeakerViewModel("SPEAKER_05", "Marcos", "#3B82F6");
        vm.Participants.Add(spk);

        for (int i = 0; i < 1000; i++)
        {
            vm.TranscriptSegments.Add(new TranscriptSegmentViewModel
            {
                Id = $"seg-{i}",
                SpeakerId = i == 750 ? "SPEAKER_05" : "SPEAKER_00",
                StartTime = TimeSpan.FromSeconds(i * 3),
                EndTime = TimeSpan.FromSeconds((i * 3) + 2),
                Text = $"Intervención {i}"
            });
        }
        syncService.SetSegments(vm.TranscriptSegments);

        TranscriptSegmentViewModel? requestedSeg = null;
        vm.RequestScrollToSegment += (_, s) => requestedSeg = s;

        // Act
        vm.NavigateToFirstSpeakerUtterance(spk);

        // Assert: localiza el segmento 750 (2250s) sin desbordamiento ni fallos
        Assert.NotNull(requestedSeg);
        Assert.Equal("seg-750", requestedSeg.Id);
        Assert.Equal(TimeSpan.FromSeconds(2250), requestedSeg.StartTime);

        int index = vm.TranscriptSegments.IndexOf(requestedSeg);
        Assert.Equal(750, index);

        // Verificar cálculo de offset estimado para virtualización
        double scrollableHeight = 50000.0;
        double estimatedOffset = ((double)index / vm.TranscriptSegments.Count) * scrollableHeight;
        Assert.Equal(37500.0, estimatedOffset);
        Assert.True(estimatedOffset >= 0 && estimatedOffset <= scrollableHeight);
    }

    [Fact]
    public void Test12_Localization_SpanishAndEnglish_TooltipAvailable()
    {
        var loc = LocalizationManager.Instance;

        // 1. Probar cultura en Español
        loc.SetLanguage("es");
        string tooltipEs = loc["Participants_Voice_Navigate_Tooltip"];
        Assert.Equal("Ir a la primera intervención", tooltipEs);

        // 2. Probar cultura en Inglés
        loc.SetLanguage("en");
        string tooltipEn = loc["Participants_Voice_Navigate_Tooltip"];
        Assert.Equal("Go to first utterance", tooltipEn);

        // Restaurar cultura predeterminada
        loc.SetLanguage("es");
    }
}
