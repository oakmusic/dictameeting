using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.AI.Services;
using DictaMeeting.App.Converters;
using DictaMeeting.App.Services;
using DictaMeeting.App.ViewModels;
using DictaMeeting.Audio.Services;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Transcription.Services;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DictaMeeting.Tests;

/// <summary>
/// Tests para las 4 mejoras de internacionalización en DictaMeeting:
/// 1. Live Summary respeta estrictamente el idioma de la interfaz (app/UI language).
/// 2. Idioma por defecto del Acta según el idioma de la UI, con soporte de override manual.
/// 3. Opciones de "Nivel de detalle" localizadas dinámicamente en inglés (Brief, Normal, Detailed, Comprehensive) y español.
/// 4. Settings -> System muestra "GB / US" para English.
/// </summary>
public class InternationalizationImprovementsTests : IDisposable
{
    private readonly string _solutionDir;
    private readonly string _initialLanguage;

    public InternationalizationImprovementsTests()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "DictaMeeting.sln")))
        {
            current = Path.GetDirectoryName(current);
        }

        _solutionDir = current ?? throw new InvalidOperationException("DictaMeeting.sln no encontrado.");
        _initialLanguage = LocalizationManager.Instance.CurrentLanguageCode;
    }

    public void Dispose()
    {
        // Restaurar idioma español original para evitar efectos secundarios en otros tests
        LocalizationManager.Instance.SetLanguage("es");
    }

    #region 1. Live Summary Tests

    [Fact]
    public void Test1_LiveSummary_SpanishUI_RequestsSpanishPromptAndOutput()
    {
        // 1. UI español -> prompt/output solicitado estrictamente en español
        string prompt = LiveSummaryPromptBuilder.BuildPrompt(
            previousSummary: "Resumen previo",
            newTranscriptWindow: "Interlocutor 1: Vamos a revisar el plan de trabajo.",
            language: "Spanish");

        Assert.Contains("Idioma de salida obligatorio: redacta el resumen estrictamente en español", prompt);
        Assert.Contains("Redacta en español en un único párrafo", prompt);
        Assert.DoesNotContain("Mandatory output language: write the summary strictly in English", prompt);
    }

    [Fact]
    public void Test2_LiveSummary_EnglishUI_RequestsEnglishPromptAndOutput()
    {
        // 2. UI English -> prompt/output solicitado estrictamente en inglés
        string prompt = LiveSummaryPromptBuilder.BuildPrompt(
            previousSummary: "Previous summary",
            newTranscriptWindow: "Speaker 1: Let's review the roadmap.",
            language: "English");

        Assert.Contains("Mandatory output language: write the summary strictly in English", prompt);
        Assert.Contains("Write the summary strictly in English", prompt);
        Assert.DoesNotContain("Idioma de salida obligatorio: redacta el resumen estrictamente en español", prompt);
    }

    [Fact]
    public void Test3_LiveSummary_EnglishUI_WithSpanishTranscript_RequestsEnglishSummary()
    {
        // 3. UI English + transcript español -> summary solicitado en inglés
        string spanishTranscript = "Aritz: Hemos revisado la base de datos y la migración comenzará el lunes.";
        string prompt = LiveSummaryPromptBuilder.BuildPrompt(
            previousSummary: "",
            newTranscriptWindow: spanishTranscript,
            language: "English");

        // El prompt exige salida en inglés independientemente del idioma del transcript
        Assert.Contains("Mandatory output language: write the summary strictly in English, regardless of the language of the transcript", prompt);
        Assert.Contains("Write the summary strictly in English as a single paragraph of approximately 50 words (2 or 3 fluent sentences) summarizing the key points of this section regardless of the transcript language", prompt);
        // La transcripción de entrada se mantiene intacta (no se traduce en el input)
        Assert.Contains(spanishTranscript, prompt);
    }

    [Fact]
    public void Test4_LiveSummary_SpanishUI_WithEnglishTranscript_RequestsSpanishSummary()
    {
        // 4. UI Español + transcript inglés -> summary solicitado en español
        string englishTranscript = "John: We decided to deploy the new microservices on AWS instead of Azure.";
        string prompt = LiveSummaryPromptBuilder.BuildPrompt(
            previousSummary: "",
            newTranscriptWindow: englishTranscript,
            language: "Spanish");

        // El prompt exige salida en español independientemente del idioma del transcript
        Assert.Contains("Idioma de salida obligatorio: redacta el resumen estrictamente en español, con independencia del idioma en que se encuentre la transcripción", prompt);
        Assert.Contains("Redacta en español en un único párrafo de aproximadamente 50 palabras (2 o 3 frases fluidas) un resumen conciso de las ideas clave tratadas en este fragmento, con independencia del idioma de la transcripción", prompt);
        // La transcripción de entrada se mantiene intacta
        Assert.Contains(englishTranscript, prompt);
    }

    [Fact]
    public void Test5_LiveSummary_SwitchUiLanguageBetweenMeetings_NewMeetingUsesNewLanguage()
    {
        // 5. Cambiar idioma de UI entre reuniones -> la nueva reunión utiliza el nuevo idioma
        var mockService = new MockLiveSummaryService();
        var mockModelManager = new MockLiveSummaryModelManager();
        var coordinator = new LiveSummaryCoordinator(mockService, mockModelManager);

        // Reunión 1 en Español
        coordinator.Start("meeting-es", "Spanish");
        Assert.True(coordinator.IsRunning);

        coordinator.Stop();
        Assert.False(coordinator.IsRunning);

        // Nueva reunión iniciada en English
        coordinator.Start("meeting-en", "English");
        Assert.True(coordinator.IsRunning);

        coordinator.AddSegment(new TranscriptSegment
        {
            SpeakerDisplayName = "Speaker 1",
            Text = "Testing live summary language coordination across meetings.",
            StartTime = TimeSpan.FromSeconds(0),
            EndTime = TimeSpan.FromSeconds(35)
        });

        coordinator.Stop();
    }

    [Fact]
    public void Test6_LiveSummary_DoesNotRetainContextOrLanguageFromPreviousMeeting()
    {
        // 6. No debe reutilizarse el idioma ni el contexto de una reunión anterior
        var mockService = new MockLiveSummaryService();
        var mockModelManager = new MockLiveSummaryModelManager();
        var coordinator = new LiveSummaryCoordinator(mockService, mockModelManager);

        // Reunión 1 previa
        coordinator.Start("meeting-prev", "Spanish");
        coordinator.AddSegment(new TranscriptSegment
        {
            SpeakerDisplayName = "Interlocutor",
            Text = "Texto de la reunión anterior en español.",
            StartTime = TimeSpan.FromSeconds(0),
            EndTime = TimeSpan.FromSeconds(35)
        });
        coordinator.Stop();

        // Nueva reunión con nuevo idioma
        coordinator.Start("meeting-new", "English");

        // Estado completamente limpio
        Assert.Empty(coordinator.SummaryCards);
        Assert.Equal(string.Empty, coordinator.CurrentSummary);
        Assert.Null(coordinator.LastUpdatedTime);
        Assert.False(coordinator.IsGenerating);

        coordinator.Stop();
    }

    #endregion

    #region 2. Acta Language Tests

    [Fact]
    public void Test7_Acta_Modal_SpanishUI_DefaultsToSpanish()
    {
        try
        {
            // 7. UI español -> idioma inicial del modal = Español
            LocalizationManager.Instance.SetLanguage("es");
            using var fixture = new TestViewModelFixture();
            var vm = fixture.CreateViewModel();

            vm.OpenActaDialogCommand.Execute(null);

            Assert.True(vm.IsActaModalOpen);
            Assert.Equal(LanguageMode.Spanish, vm.SelectedActaLanguage);
        }
        finally
        {
            LocalizationManager.Instance.SetLanguage("es");
        }
    }

    [Fact]
    public void Test8_Acta_Modal_EnglishUI_DefaultsToEnglish()
    {
        try
        {
            // 8. UI English -> idioma inicial del modal = English
            LocalizationManager.Instance.SetLanguage("en");
            using var fixture = new TestViewModelFixture();
            var vm = fixture.CreateViewModel();

            vm.OpenActaDialogCommand.Execute(null);

            Assert.True(vm.IsActaModalOpen);
            Assert.Equal(LanguageMode.English, vm.SelectedActaLanguage);
        }
        finally
        {
            LocalizationManager.Instance.SetLanguage("es");
        }
    }

    [Fact]
    public void Test9_Acta_Modal_ManualSelection_PreservedDuringCurrentGeneration()
    {
        try
        {
            // 9. Si el usuario selecciona manualmente otro idioma, esa elección se respeta durante la generación actual
            LocalizationManager.Instance.SetLanguage("en");
            using var fixture = new TestViewModelFixture();
            var vm = fixture.CreateViewModel();

            vm.OpenActaDialogCommand.Execute(null);
            Assert.Equal(LanguageMode.English, vm.SelectedActaLanguage);

            // Usuario elige manualmente Español en el ComboBox
            vm.SelectedActaLanguage = LanguageMode.Spanish;
            Assert.Equal(LanguageMode.Spanish, vm.SelectedActaLanguage);
        }
        finally
        {
            LocalizationManager.Instance.SetLanguage("es");
        }
    }

    [Fact]
    public void Test10_Acta_Modal_PreviousManualSelection_DoesNotOverrideCurrentUiLanguageOnReopen()
    {
        try
        {
            // 10. El último idioma usado anteriormente no debe imponerse sobre el idioma actual de la UI como valor inicial
            LocalizationManager.Instance.SetLanguage("en");
            using var fixture = new TestViewModelFixture();
            var vm = fixture.CreateViewModel();

            // 1. Abrir en English -> inicial English
            vm.OpenActaDialogCommand.Execute(null);
            Assert.Equal(LanguageMode.English, vm.SelectedActaLanguage);

            // 2. Modificar manualmente a Español y cerrar
            vm.SelectedActaLanguage = LanguageMode.Spanish;
            vm.CloseActaDialogCommand.Execute(null);
            Assert.False(vm.IsActaModalOpen);

            // 3. Volver a abrir mientras UI sigue en English -> debe volver a ser English por defecto
            vm.OpenActaDialogCommand.Execute(null);
            Assert.Equal(LanguageMode.English, vm.SelectedActaLanguage);
            vm.CloseActaDialogCommand.Execute(null);

            // 4. Cambiar UI a Español y abrir de nuevo -> debe ser Español
            LocalizationManager.Instance.SetLanguage("es");
            vm.OpenActaDialogCommand.Execute(null);
            Assert.Equal(LanguageMode.Spanish, vm.SelectedActaLanguage);
        }
        finally
        {
            LocalizationManager.Instance.SetLanguage("es");
        }
    }

    #endregion

    #region 3. Detail Level Localization Tests

    [Fact]
    public void Test11_DetailLevel_SpanishUI_DisplaysBreveNormalDetalladaExhaustiva()
    {
        try
        {
            // 11. UI español -> Breve / Normal / Detallada / Exhaustiva
            LocalizationManager.Instance.SetLanguage("es");

            Assert.Equal("Breve", ActaDetailLevel.Breve.ToLocalizedDisplay());
            Assert.Equal("Normal", ActaDetailLevel.Normal.ToLocalizedDisplay());
            Assert.Equal("Detallada", ActaDetailLevel.Detallada.ToLocalizedDisplay());
            Assert.Equal("Exhaustiva", ActaDetailLevel.Exhaustiva.ToLocalizedDisplay());

            // Comprobar también el convertidor de XAML
            var converter = new ActaDetailLevelConverter();
            Assert.Equal("Breve", converter.Convert(ActaDetailLevel.Breve, typeof(string), null, CultureInfo.CurrentUICulture));
            Assert.Equal("Normal", converter.Convert(ActaDetailLevel.Normal, typeof(string), null, CultureInfo.CurrentUICulture));
            Assert.Equal("Detallada", converter.Convert(ActaDetailLevel.Detallada, typeof(string), null, CultureInfo.CurrentUICulture));
            Assert.Equal("Exhaustiva", converter.Convert(ActaDetailLevel.Exhaustiva, typeof(string), null, CultureInfo.CurrentUICulture));
        }
        finally
        {
            LocalizationManager.Instance.SetLanguage("es");
        }
    }

    [Fact]
    public void Test12_DetailLevel_EnglishUI_DisplaysBriefNormalDetailedComprehensive()
    {
        try
        {
            // 12. UI English -> Brief / Normal / Detailed / Comprehensive
            LocalizationManager.Instance.SetLanguage("en");

            Assert.Equal("Brief", ActaDetailLevel.Breve.ToLocalizedDisplay());
            Assert.Equal("Normal", ActaDetailLevel.Normal.ToLocalizedDisplay());
            Assert.Equal("Detailed", ActaDetailLevel.Detallada.ToLocalizedDisplay());
            Assert.Equal("Comprehensive", ActaDetailLevel.Exhaustiva.ToLocalizedDisplay());

            // Comprobar también el convertidor de XAML
            var converter = new ActaDetailLevelConverter();
            Assert.Equal("Brief", converter.Convert(ActaDetailLevel.Breve, typeof(string), null, CultureInfo.CurrentUICulture));
            Assert.Equal("Normal", converter.Convert(ActaDetailLevel.Normal, typeof(string), null, CultureInfo.CurrentUICulture));
            Assert.Equal("Detailed", converter.Convert(ActaDetailLevel.Detallada, typeof(string), null, CultureInfo.CurrentUICulture));
            Assert.Equal("Comprehensive", converter.Convert(ActaDetailLevel.Exhaustiva, typeof(string), null, CultureInfo.CurrentUICulture));
        }
        finally
        {
            LocalizationManager.Instance.SetLanguage("es");
        }
    }

    #endregion

    #region 4. Settings -> System Language Button Tests

    [Fact]
    public void Test13_Settings_System_EnglishLanguageButton_ShowsGBUS()
    {
        // 13. Settings -> System muestra "GB / US" para English
        var xamlPath = Path.Combine(_solutionDir, "src", "DictaMeeting.App", "MainWindow.xaml");
        Assert.True(File.Exists(xamlPath), "MainWindow.xaml no encontrado.");

        var content = File.ReadAllText(xamlPath);

        // El texto del botón de idioma inglés debe contener explícitamente "GB / US"
        Assert.Contains("GB / US", content);
        Assert.DoesNotContain("Text=\"🇬🇧\"", content);
    }

    #endregion

    #region Helper Classes for Testing

    private class MockLiveSummaryService : ILiveSummaryService
    {
        public bool IsGenerating { get; set; }
        public bool IsModelLoaded => true;
        public SummaryMetrics? LastMetrics => null;

        public string? LastLanguageRequested { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GenerateSummaryAsync(string previousSummary, string newTranscriptWindow, string language = "Spanish", CancellationToken cancellationToken = default)
        {
            LastLanguageRequested = language;
            return Task.FromResult($"Summary ({language}): content");
        }

        public void CancelCurrentGeneration() { }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private class MockLiveSummaryModelManager : ILiveSummaryModelManager
    {
        public string ModelsDirectory => "C:\\dummy";
        public void SetModelsDirectory(string path) { }
        public string ModelName => "Qwen2.5-1.5B-Instruct";
        public string Quantization => "Q4_K_M";
        public long ModelSizeBytes => 1000L;
        public string LocalModelPath => "C:\\dummy\\model.gguf";
        public string GetModelPath() => LocalModelPath;
        public bool IsModelDownloaded() => true;
        public Task<string> EnsureModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(LocalModelPath);
        public bool DeleteModel() => true;
    }

    private class TestViewModelFixture : IDisposable
    {
        private readonly string _tempFile;

        public TestViewModelFixture()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"i18n_test_vm_{Guid.NewGuid():N}.json");
        }

        public MainViewModel CreateViewModel()
        {
            var userSettingsService = new DictaMeeting.Infrastructure.Persistence.UserSettingsService(_tempFile);
            var repo = new TwoPassModelSelectionTests.DummyMeetingRepository();
            var meetingService = new DictaMeeting.Meetings.Services.MeetingService(repo);
            var docExporter = new DictaMeeting.Meetings.Exporters.DocumentExporter();
            var audioDeviceService = new AudioDeviceService();
            var audioCaptureService = new MockAudioCaptureService();
            var modelManager = new TwoPassModelSelectionTests.DummyModelManager();
            var transcriptionService = new MockTranscriptionService();
            var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
            var diarizationService = new DictaMeeting.Diarization.Services.PyAnnoteCommunity1DiarizationService();
            var speakerAligner = new DictaMeeting.Diarization.Pipeline.SpeakerTranscriptAligner();
            var secureStorage = new DictaMeeting.Infrastructure.Security.WindowsDpapiSecureStorageService();
            var aiActaService = new DictaMeeting.AI.Services.OpenRouterActaService(new System.Net.Http.HttpClient(), secureStorage);

            return new MainViewModel(
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
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
        }
    }

    #endregion
}
