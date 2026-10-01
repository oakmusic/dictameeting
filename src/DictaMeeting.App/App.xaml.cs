using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.AI.Services;
using DictaMeeting.App.Services;
using DictaMeeting.App.ViewModels;
using DictaMeeting.App.Views;
using DictaMeeting.Audio.Interfaces;
using DictaMeeting.Audio.Processing;
using DictaMeeting.Audio.Recording;
using DictaMeeting.Audio.Services;
using DictaMeeting.Diarization.Interfaces;
using DictaMeeting.Diarization.Pipeline;
using DictaMeeting.Diarization.Services;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Services;
using DictaMeeting.Meetings.Vocabulary;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Services;
using DictaMeeting.Transcription.Services.Punctuation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DictaMeeting.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LogBoot($"[App] Bootstrap iniciado. DictaMeeting v1.5.2 (.NET {Environment.Version}, OS: {Environment.OSVersion}, 64-bit: {Environment.Is64BitProcess})");

        // Prevenir cierre prematuro de la aplicación durante la transición SplashWindow -> MainWindow
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        SplashWindow? splashWindow = null;
        try
        {
            // 1. Mostrar SplashWindow inmediatamente (fondo y ondas activas; elementos aún no visibles)
            splashWindow = new SplashWindow();
            splashWindow.Show();
            LogBoot("[App] SplashWindow desplegada.");

            // Ceder al despachador para asegurar que el fondo se pinte en pantalla
            await Dispatcher.Yield(DispatcherPriority.Render);

            // 2. PRECARGA: Inicializar DI, servicios y MainWindow
            // Al hacer toda la precarga antes de la animación, el hilo de UI queda totalmente libre
            // y la animación no sufre ningún tirón o bloqueo.
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
            LogBoot("[App] Inyección de dependencias inicializada.");

            SetupExceptionHandling();

            var processingService = _serviceProvider.GetRequiredService<IMeetingProcessingService>();
            _ = processingService.RecoverInterruptedJobsAsync();

            var vocabularyService = _serviceProvider.GetRequiredService<IVocabularyService>();
            _ = vocabularyService.LoadAsync();

            // Instanciar MainWindow (precarga en memoria del árbol visual y ViewModel)
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            LogBoot("[App] MainWindow instanciada con éxito.");

            // Ceder un ciclo al despachador para procesar cualquier render o evento residual de precarga
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            // 3. ANIMACIÓN DE ENTRADA: Arrancar la animación fluida ahora que los elementos están precargados
            await splashWindow.PlayIntroAnimationAsync();

            // 4. PERMANENCIA: Mantener 1 segundo en pantalla tras finalizar la animación
            await Task.Delay(1000);

            // 5. CIERRE SUAVE: Fade-out de salida
            await splashWindow.FadeOutAndCloseAsync();

            // 6. ACTIVAR VENTANA PRINCIPAL
            MainWindow = mainWindow;
            mainWindow.Show();
            mainWindow.Activate();
            LogBoot("[App] MainWindow visible. Arranque completado con éxito.");

            // Restaurar el modo de apagado para que la app se cierre al cerrar MainWindow
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex)
        {
            try
            {
                splashWindow?.Close();
            }
            catch
            {
                // Ignorar error al cerrar splash en caso de fallo crítico
            }

            LogBoot($"[App] ERROR CRÍTICO EN BOOTSTRAP: {ex.GetType().FullName} - {ex.Message}\n{ex.StackTrace}");

            var logger = _serviceProvider?.GetService<ILogger<App>>();
            logger?.LogCritical(ex, "Error crítico durante el inicio de la aplicación.");

            MessageBox.Show(
                $"Ha ocurrido un error inesperado al iniciar DictaMeeting:\n\n{ex.Message}\n\nPuede consultar los detalles en los logs locales.",
                "DictaMeeting — Error de inicio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    private void SetupExceptionHandling()
    {
        DispatcherUnhandledException += (s, args) =>
        {
            LogBoot($"[App] ERROR NO CONTROLADO EN DISPATCHER: {args.Exception?.GetType().FullName} - {args.Exception?.Message}\n{args.Exception?.StackTrace}");
            var logger = _serviceProvider?.GetService<ILogger<App>>();
            logger?.LogCritical(args.Exception, "Excepción no controlada en el despachador de la interfaz de usuario.");
            MessageBox.Show(
                $"Ha ocurrido un error inesperado:\n\n{args.Exception?.Message}\n\nPuede consultar los detalles en los logs locales.",
                "DictaMeeting — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            LogBoot($"[App] ERROR NO CONTROLADO EN APPDOMAIN: {ex?.GetType().FullName} - {ex?.Message}\n{ex?.StackTrace}");
            var logger = _serviceProvider?.GetService<ILogger<App>>();
            logger?.LogCritical(ex, "Excepción no controlada en el dominio de la aplicación.");
            MessageBox.Show(
                $"Ha ocurrido un error inesperado:\n\n{ex?.Message}\n\nPuede consultar los detalles en los logs locales.",
                "DictaMeeting — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };
    }

    private static void LogBoot(string message)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDir = System.IO.Path.Combine(localAppData, "DictaMeeting", "logs");
            if (!System.IO.Directory.Exists(logDir))
            {
                System.IO.Directory.CreateDirectory(logDir);
            }
            var logFile = System.IO.Path.Combine(logDir, "boot.log");

            // Rotación simple si supera 2 MB
            if (System.IO.File.Exists(logFile) && new System.IO.FileInfo(logFile).Length > 2 * 1024 * 1024)
            {
                var oldFile = System.IO.Path.Combine(logDir, "boot.old.log");
                System.IO.File.Delete(oldFile);
                System.IO.File.Move(logFile, oldFile);
            }

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            System.IO.File.AppendAllText(logFile, line);
        }
        catch
        {
            // El logging de diagnóstico nunca debe propagar excepciones
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Dominio y Persistencia
        services.AddSingleton<IDocumentExporter, DocumentExporter>();
        services.AddSingleton<IMeetingRepository, LocalFileMeetingRepository>();
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<IVocabularyService, VocabularyService>();
        services.AddSingleton<IMeetingService, MeetingService>();

        // Audio
        services.AddSingleton<IAudioMixer, AudioMixer>();
        services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
        services.AddSingleton<IAudioRecordingService, AudioRecordingService>();
        services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
        services.AddSingleton<IAudioPlayerService, AudioPlayerService>();
        services.AddSingleton<ITranscriptAudioSyncService, TranscriptAudioSyncService>();

        // Seguridad
        services.AddSingleton<ISecureStorageService, WindowsDpapiSecureStorageService>();

        // Transcripción ASR Local y Voice Activity Detection (VAD)
        services.AddSingleton<IVoiceActivityDetector, SileroVoiceActivityDetector>();
        services.AddSingleton<IModelManager>(sp =>
        {
            var settingsService = sp.GetRequiredService<IUserSettingsService>();
            var settings = settingsService.LoadSettings();
            var logger = sp.GetService<ILogger<ModelManager>>();
            var effectiveDir = settings.EffectiveModelsDirectory;
            return new ModelManager(effectiveDir, logger);
        });
        services.AddSingleton<WhisperTranscriptionService>();
        services.AddSingleton<SherpaQwenTranscriptionService>();
        services.AddSingleton<ITranscriptionService, CompositeTranscriptionService>();
        services.AddSingleton<ILiveTranscriptionPipeline, LiveTranscriptionPipeline>();

        // Diarización de Hablantes (PyAnnote Community-1 ONNX)
        services.AddSingleton<ISpeakerDiarizationService>(sp =>
        {
            var modelManager = sp.GetRequiredService<IModelManager>();
            var logger = sp.GetService<ILogger<PyAnnoteCommunity1DiarizationService>>();
            var diarFolder = Path.Combine(modelManager.ModelsDirectory, "diarization");
            return new PyAnnoteCommunity1DiarizationService(diarFolder, logger);
        });
        services.AddSingleton<ISpeakerTranscriptAligner, SpeakerTranscriptAligner>();
        services.AddSingleton<ISpeakerSmoothingFilter, SpeakerSmoothingFilter>();

        // Restauración de Puntuación y Capitalización Local (Postprocesamiento)
        services.AddSingleton<IPunctuationService, OnnxPunctuationService>();

        // Procesamiento en Segundo Plano
        services.AddSingleton<IMeetingProcessingService, MeetingProcessingService>();

        // IA Actas (OpenRouter y Servidor Personalizado OpenAI-compatible)
        services.AddSingleton<HttpClient>();
        services.AddSingleton<OpenRouterActaService>();
        services.AddSingleton<OpenAiCompatibleActaService>();
        services.AddSingleton<IAiActaProvider>(sp => sp.GetRequiredService<OpenRouterActaService>());
        services.AddSingleton<IAiActaProvider>(sp => sp.GetRequiredService<OpenAiCompatibleActaService>());
        services.AddSingleton<IAiActaServiceFactory, AiActaServiceFactory>();
        services.AddSingleton<IAiActaService, CompositeAiActaService>();

        // IA Resumen en Vivo Local (llama.cpp / Qwen2.5 1.5B Instruct GGUF)
        services.AddSingleton<LiveSummaryConfig>();
        services.AddSingleton<ILiveSummaryModelManager>(sp =>
        {
            var modelManager = sp.GetRequiredService<IModelManager>();
            var httpClient = sp.GetRequiredService<HttpClient>();
            var logger = sp.GetService<ILogger<LocalLiveSummaryModelManager>>();
            var summaryFolder = Path.Combine(modelManager.ModelsDirectory, "summary");
            return new LocalLiveSummaryModelManager(summaryFolder, httpClient, logger);
        });
        services.AddSingleton<ILiveSummaryService, LocalLlamaCppSummaryService>();
        services.AddSingleton<ILiveSummaryCoordinator, LiveSummaryCoordinator>();

        // ViewModels y Vistas
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
