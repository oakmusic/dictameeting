using System.Windows;
using System.Windows.Media;
using DictaMeeting.App.Converters;
using DictaMeeting.App.Services;
using DictaMeeting.App.ViewModels;
using DictaMeeting.Audio.Services;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class ThemeAndUiViewModelTests
{
    [Fact]
    public void ThemeManager_DefaultsToLightMode()
    {
        // El requerimiento del usuario pide que por defecto sea Modo Claro (IsDarkMode == false)
        Assert.False(ThemeManager.IsDarkMode);
    }

    [Fact]
    public void ThemeManager_ApplyTheme_ChangesStateAndFiresEvent()
    {
        bool eventFired = false;
        bool receivedTheme = false;
        Action<bool> handler = isDark =>
        {
            eventFired = true;
            receivedTheme = isDark;
        };

        try
        {
            ThemeManager.ThemeChanged += handler;
            ThemeManager.ApplyTheme(true);

            Assert.True(ThemeManager.IsDarkMode);
            Assert.True(eventFired);
            Assert.True(receivedTheme);

            // Volver a modo claro
            ThemeManager.ApplyTheme(false);
            Assert.False(ThemeManager.IsDarkMode);
        }
        finally
        {
            ThemeManager.ThemeChanged -= handler;
            ThemeManager.ApplyTheme(false);
        }
    }

    [Fact]
    public void WhisperModelCardViewModel_FormatsRecommendationBadge_Correctly()
    {
        var cardRec = new WhisperModelCardViewModel
        {
            Size = ModelSize.Base,
            Name = "Base",
            IsRecommended = true
        };

        Assert.Equal("★ RECOMENDADO PARA TU EQUIPO", cardRec.RecommendationBadgeText);

        var cardNormal = new WhisperModelCardViewModel
        {
            Size = ModelSize.LargeV3Turbo,
            Name = "Large",
            IsRecommended = false
        };

        Assert.Equal(string.Empty, cardNormal.RecommendationBadgeText);
    }

    [Fact]
    public void MeetingHistoryItemViewModel_ComputesProperties_Correctly()
    {
        var meeting = new Meeting
        {
            Id = "test-hist-123",
            Title = "Reunión de Estrategia",
            Date = new DateTime(2026, 9, 21, 15, 30, 0),
            StartTime = DateTimeOffset.Now,
            EndTime = DateTimeOffset.Now.AddMinutes(45).AddSeconds(12)
        };

        meeting.Participants.Add(new Speaker { Id = "SPK_00", DisplayName = "Ana" });
        meeting.Participants.Add(new Speaker { Id = "SPK_01", DisplayName = "Carlos" });

        meeting.Transcript.Add(new TranscriptSegment { Text = "Hola a todos" });
        meeting.Transcript.Add(new TranscriptSegment { Text = "Comenzamos" });

        var vm = new MeetingHistoryItemViewModel(meeting);

        Assert.Equal("test-hist-123", vm.Id);
        Assert.Equal("Reunión de Estrategia", vm.Title);
        Assert.Equal(2, vm.ParticipantCount);
        Assert.Equal(2, vm.SegmentCount);
        Assert.False(vm.HasActa);
        Assert.Contains("2 participantes", vm.SummaryText);
        Assert.Equal("21/09/2026 15:30", vm.FormattedDate);
    }

    [Fact]
    public void MeetingHistoryItemViewModel_WithMidnightDate_UsesStartTime()
    {
        var meeting = new Meeting
        {
            Id = "legacy-1",
            Title = "Reunión Antigua",
            Date = new DateTime(2026, 9, 21), // Midnight 00:00:00
            StartTime = new DateTimeOffset(2026, 9, 21, 11, 45, 0, TimeSpan.FromHours(2))
        };

        var vm = new MeetingHistoryItemViewModel(meeting);

        Assert.Equal("21/09/2026 11:45", vm.FormattedDate);
    }

    [Fact]
    public void EqualityToVisibilityConverter_ConvertsValuesProperly()
    {
        var converter = new EqualityToVisibilityConverter();

        var vis = converter.Convert(0, typeof(Visibility), 0, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Visible, vis);

        var coll = converter.Convert(0, typeof(Visibility), 1, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, coll);
    }

    [Fact]
    public void NullToVisibilityConverter_ConvertsProperly()
    {
        var converter = new NullToVisibilityConverter();

        var visForNull = converter.Convert(null, typeof(Visibility), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Visible, visForNull);

        var collForObject = converter.Convert("No nulo", typeof(Visibility), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, collForObject);
    }

    [Fact]
    public void MainViewModel_TitleEditing_CommandsBehaveCorrectly()
    {
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vm_title_test_{Guid.NewGuid():N}.json");
        try
        {
            var userSettingsService = new DictaMeeting.Infrastructure.Persistence.UserSettingsService(tempFile);
            var repo = new TwoPassModelSelectionTests.DummyMeetingRepository();
            var meetingService = new DictaMeeting.Meetings.Services.MeetingService(repo);
            var docExporter = new DictaMeeting.Meetings.Exporters.DocumentExporter();
            using var audioDeviceService = new AudioDeviceService();
            var audioCaptureService = new MockAudioCaptureService();
            var modelManager = new TwoPassModelSelectionTests.DummyModelManager();
            var transcriptionService = new MockTranscriptionService();
            var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
            var diarizationService = new DictaMeeting.Diarization.Services.PyAnnoteCommunity1DiarizationService();
            var speakerAligner = new DictaMeeting.Diarization.Pipeline.SpeakerTranscriptAligner();
            var secureStorage = new DictaMeeting.Infrastructure.Security.WindowsDpapiSecureStorageService();
            var aiActaService = new DictaMeeting.AI.Services.OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

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
                userSettingsService);

            // Estado inicial
            Assert.Equal("Reunión de Equipo", vm.MeetingTitle);
            Assert.False(vm.IsEditingMeetingTitle);

            // Iniciar edición
            vm.StartEditMeetingTitleCommand.Execute(null);
            Assert.True(vm.IsEditingMeetingTitle);

            // Modificar título y guardar
            vm.MeetingTitle = "Comité de Dirección";
            vm.FinishEditMeetingTitleCommand.Execute(null);
            Assert.False(vm.IsEditingMeetingTitle);
            Assert.Equal("Comité de Dirección", vm.MeetingTitle);

            // Iniciar edición y cancelar
            vm.StartEditMeetingTitleCommand.Execute(null);
            Assert.True(vm.IsEditingMeetingTitle);
            vm.CancelEditMeetingTitleCommand.Execute(null);
            Assert.False(vm.IsEditingMeetingTitle);

            // Guardar con valor vacío debe restaurar el título por defecto
            vm.StartEditMeetingTitleCommand.Execute(null);
            vm.MeetingTitle = "   ";
            vm.FinishEditMeetingTitleCommand.Execute(null);
            Assert.Equal("Reunión de Equipo", vm.MeetingTitle);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                try { System.IO.File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public void MainViewModel_RedesignedCentralStripProperties_WorkCorrectly()
    {
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vm_central_strip_test_{Guid.NewGuid():N}.json");
        try
        {
            var userSettingsService = new DictaMeeting.Infrastructure.Persistence.UserSettingsService(tempFile);
            var repo = new TwoPassModelSelectionTests.DummyMeetingRepository();
            var meetingService = new DictaMeeting.Meetings.Services.MeetingService(repo);
            var docExporter = new DictaMeeting.Meetings.Exporters.DocumentExporter();
            using var audioDeviceService = new AudioDeviceService();
            var audioCaptureService = new MockAudioCaptureService();
            var modelManager = new TwoPassModelSelectionTests.DummyModelManager();
            var transcriptionService = new MockTranscriptionService();
            var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
            var diarizationService = new DictaMeeting.Diarization.Services.PyAnnoteCommunity1DiarizationService();
            var speakerAligner = new DictaMeeting.Diarization.Pipeline.SpeakerTranscriptAligner();
            var secureStorage = new DictaMeeting.Infrastructure.Security.WindowsDpapiSecureStorageService();
            var aiActaService = new DictaMeeting.AI.Services.OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

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
                userSettingsService);

            // 1. Participantes dinámicos (sólo número)
            Assert.Equal("0", vm.ParticipantsSummaryText);
            vm.RealParticipants.Add("Aritz");
            Assert.Equal("1", vm.ParticipantsSummaryText);
            vm.RealParticipants.Add("Elena");
            Assert.Equal("2", vm.ParticipantsSummaryText);
            vm.RealParticipants.Remove("Aritz");
            Assert.Equal("1", vm.ParticipantsSummaryText);

            // 2. Fuentes de audio dinámicas
            vm.CaptureMicrophone = true;
            vm.CaptureSystemAudio = true;
            Assert.Equal("Micrófono · Sistema", vm.AudioSourcesSummary);

            vm.CaptureMicrophone = true;
            vm.CaptureSystemAudio = false;
            Assert.Equal("Micrófono", vm.AudioSourcesSummary);

            vm.CaptureMicrophone = false;
            vm.CaptureSystemAudio = true;
            Assert.Equal("Audio del sistema", vm.AudioSourcesSummary);

            vm.CaptureMicrophone = false;
            vm.CaptureSystemAudio = false;
            Assert.Equal("Sin audio", vm.AudioSourcesSummary);

            // 3. Modelos y lenguaje
            vm.CaptureMicrophone = true;
            vm.CaptureSystemAudio = true;
            Assert.Contains("En vivo (", vm.ModelsSummary);
            Assert.Contains("Procesado (", vm.ModelsSummary);
            Assert.NotNull(vm.LanguageDisplayShort);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                try { System.IO.File.Delete(tempFile); } catch { }
            }
        }
    }

    private class DelayedMockTranscriptionService : MockTranscriptionService
    {
        public TaskCompletionSource<bool> StartedTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ResumeTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task InitializeAsync(ModelSize modelSize, CancellationToken cancellationToken = default)
        {
            StartedTcs.TrySetResult(true);
            await ResumeTcs.Task;
            await base.InitializeAsync(modelSize, cancellationToken);
        }
    }

    [Fact]
    public async Task StartMeeting_ProvidesImmediateVisualFeedback_DuringInitialization()
    {
        var tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vm_start_feedback_test_{Guid.NewGuid():N}.json");
        try
        {
            var userSettingsService = new DictaMeeting.Infrastructure.Persistence.UserSettingsService(tempFile);
            userSettingsService.SaveSettings(new DictaMeeting.Infrastructure.Persistence.UserSettings { ShowRecordingNotice = false });
            var repo = new TwoPassModelSelectionTests.DummyMeetingRepository();
            var meetingService = new DictaMeeting.Meetings.Services.MeetingService(repo);
            var docExporter = new DictaMeeting.Meetings.Exporters.DocumentExporter();
            using var audioDeviceService = new AudioDeviceService();
            var audioCaptureService = new MockAudioCaptureService();
            var modelManager = new TwoPassModelSelectionTests.DummyModelManager();
            var transcriptionService = new DelayedMockTranscriptionService();
            var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
            var diarizationService = new DictaMeeting.Diarization.Services.PyAnnoteCommunity1DiarizationService();
            var speakerAligner = new DictaMeeting.Diarization.Pipeline.SpeakerTranscriptAligner();
            var secureStorage = new DictaMeeting.Infrastructure.Security.WindowsDpapiSecureStorageService();
            var aiActaService = new DictaMeeting.AI.Services.OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

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
                userSettingsService);

            // 1. Estado inicial antes de pulsar Iniciar
            Assert.False(vm.IsStartingMeeting);
            Assert.Equal("Iniciar", vm.StartButtonText);
            Assert.Equal("Preparado", vm.PrepStatusTitle);
            Assert.Equal("Listo para iniciar la grabación", vm.PrepStatusSubtitle);
            Assert.True(vm.CanStartMeeting);
            Assert.True(vm.IsIdle);
            Assert.False(vm.IsRecording);

            // 2. Pulsar botón Iniciar (se ejecuta en segundo plano)
            var startTask = vm.StartMeetingCommand.ExecuteAsync(null);

            // Esperar a que entre en la fase de inicialización del motor de voz
            await transcriptionService.StartedTcs.Task;

            // Verificar que durante la inicialización hay feedback inmediato para el usuario
            Assert.True(vm.IsStartingMeeting);
            Assert.Equal("Iniciando...", vm.StartButtonText);
            Assert.Equal("Iniciando...", vm.PrepStatusTitle);
            Assert.Equal("Cargando motor de voz y dispositivos...", vm.PrepStatusSubtitle);
            Assert.Equal(MeetingState.Preparing, vm.CurrentState);
            Assert.False(vm.CanStartMeeting);
            Assert.False(vm.IsIdle);
            Assert.False(vm.IsRecording);

            // Intentar re-pulsar Iniciar mientras inicializa no debe duplicar el proceso
            await vm.StartMeetingCommand.ExecuteAsync(null);
            Assert.True(vm.IsStartingMeeting);

            // 3. Desbloquear inicialización para que concluya
            transcriptionService.ResumeTcs.TrySetResult(true);
            await startTask;

            // 4. Estado final una vez iniciada la reunión y grabación activa
            Assert.False(vm.IsStartingMeeting);
            Assert.True(vm.IsRecording);
            Assert.Equal(MeetingState.Recording, vm.CurrentState);
            Assert.False(vm.CanStartMeeting);
            Assert.False(vm.IsIdle);

            // Detener reunión
            await vm.StopMeetingCommand.ExecuteAsync(null);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                try { System.IO.File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public void TranscriptSegmentViewModel_HasSpeakerLabel_HidesWhenEmptyOrConsecutive()
    {
        var seg1 = new TranscriptSegmentViewModel
        {
            SpeakerId = "spk1",
            SpeakerDisplayName = "Pablo",
            Text = "Tengo que decir."
        };

        var seg2 = new TranscriptSegmentViewModel
        {
            SpeakerId = "spk1",
            SpeakerDisplayName = "Pablo",
            Text = "Qué pruebas tortillas..."
        };

        var seg3 = new TranscriptSegmentViewModel
        {
            SpeakerId = "spk2",
            SpeakerDisplayName = "Monica",
            Text = "esta patata es un panorámica..."
        };

        var seg4 = new TranscriptSegmentViewModel
        {
            SpeakerId = "spk2",
            SpeakerDisplayName = "Monica",
            Text = "que precisamente por la falta de aceite..."
        };

        var seg5 = new TranscriptSegmentViewModel
        {
            SpeakerId = "spk3",
            SpeakerDisplayName = "SPEAKER_02",
            Text = "De acuerdo con vosotros."
        };

        var list = new List<TranscriptSegmentViewModel> { seg1, seg2, seg3, seg4, seg5 };
        TranscriptSegmentViewModel.UpdateSpeakerInterventionHeaders(list);

        // Pablo habla en seg1 y seg2: solo el primer fragmento muestra la etiqueta
        Assert.True(seg1.HasSpeakerLabel);
        Assert.True(seg1.ShowSpeakerLabel);
        Assert.False(seg2.HasSpeakerLabel);
        Assert.False(seg2.ShowSpeakerLabel);

        // Monica habla en seg3 y seg4: solo el primer fragmento muestra la etiqueta
        Assert.True(seg3.HasSpeakerLabel);
        Assert.True(seg3.ShowSpeakerLabel);
        Assert.False(seg4.HasSpeakerLabel);
        Assert.False(seg4.ShowSpeakerLabel);

        // SPEAKER_02 es un nuevo interlocutor: muestra la etiqueta
        Assert.True(seg5.HasSpeakerLabel);
        Assert.True(seg5.ShowSpeakerLabel);
    }

    [Fact]
    public void TranscriptSegmentViewModel_EmptySpeaker_NeverShowsLabel()
    {
        var liveSeg1 = new TranscriptSegmentViewModel
        {
            SpeakerId = "",
            SpeakerDisplayName = "",
            Text = "Hola a todos en la llamada."
        };

        var liveSeg2 = new TranscriptSegmentViewModel
        {
            SpeakerId = "",
            SpeakerDisplayName = "",
            Text = "Empezamos la reunión."
        };

        var list = new List<TranscriptSegmentViewModel> { liveSeg1, liveSeg2 };
        TranscriptSegmentViewModel.UpdateSpeakerInterventionHeaders(list);

        Assert.False(liveSeg1.HasSpeakerLabel);
        Assert.False(liveSeg2.HasSpeakerLabel);
    }

    [Fact]
    public void TranscriptSegmentViewModel_SameSpeakerReturnsLater_ShowsLabelAtNewTurn()
    {
        var seg1 = new TranscriptSegmentViewModel { SpeakerId = "spk1", SpeakerDisplayName = "Pablo", Text = "Frase 1" };
        var seg2 = new TranscriptSegmentViewModel { SpeakerId = "spk2", SpeakerDisplayName = "Monica", Text = "Frase 2" };
        var seg3 = new TranscriptSegmentViewModel { SpeakerId = "spk1", SpeakerDisplayName = "Pablo", Text = "Frase 3" };
        var seg4 = new TranscriptSegmentViewModel { SpeakerId = "spk1", SpeakerDisplayName = "Pablo", Text = "Frase 4" };

        var list = new List<TranscriptSegmentViewModel> { seg1, seg2, seg3, seg4 };
        TranscriptSegmentViewModel.UpdateSpeakerInterventionHeaders(list);

        Assert.True(seg1.HasSpeakerLabel);
        Assert.True(seg2.HasSpeakerLabel);
        Assert.True(seg3.HasSpeakerLabel); // Pablo vuelve a intervenir tras hablar Mónica
        Assert.False(seg4.HasSpeakerLabel); // Misma intervención que seg3, se oculta
    }
}

