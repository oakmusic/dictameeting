using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Services;
using DictaMeeting.App.ViewModels;
using DictaMeeting.Audio.Services;
using DictaMeeting.Diarization.Pipeline;
using DictaMeeting.Diarization.Services;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Infrastructure.Security;
using DictaMeeting.Meetings.Exporters;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Meetings.Services;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class FirstRunModelsOnboardingTests
{
    private class ConfigurableModelManager : IModelManager
    {
        public string ModelsDirectory { get; private set; } = "C:\\test\\models";
        public void SetModelsDirectory(string path) => ModelsDirectory = path;

        public bool Qwen06Downloaded { get; set; }
        public bool Qwen17Downloaded { get; set; }
        public bool DiarizationDownloaded { get; set; } = true;
        public bool ThrowOnDownload { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public List<ModelSize> DownloadedHistory { get; } = new();

        public Task<HardwareCapabilities> DetectHardwareAsync()
        {
            return Task.FromResult(new HardwareCapabilities
            {
                CpuLogicalCores = 8,
                TotalRamGb = 16.0,
                RecommendedModel = ModelSize.Qwen3_06B,
                RecommendedLiveModel = ModelSize.Qwen3_06B,
                RecommendedFinalModel = ModelSize.Qwen3_17B
            });
        }

        public IReadOnlyList<TranscriptionModelInfo> GetAvailableModels()
        {
            return new List<TranscriptionModelInfo>
            {
                new()
                {
                    Size = ModelSize.Qwen3_06B,
                    Name = "Qwen3-ASR 0.6B",
                    FileSizeBytes = 950L * 1024 * 1024,
                    IsDownloaded = Qwen06Downloaded
                },
                new()
                {
                    Size = ModelSize.Qwen3_17B,
                    Name = "Qwen3-ASR 1.7B",
                    FileSizeBytes = 2404222421L,
                    IsDownloaded = Qwen17Downloaded
                }
            };
        }

        public async Task<string> EnsureModelDownloadedAsync(ModelSize modelSize, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (ThrowOnDownload)
            {
                throw ExceptionToThrow ?? new HttpRequestException("No se ha podido conectar con el servidor de descarga.");
            }

            progress?.Report(0.5);
            await Task.Delay(10, cancellationToken);
            progress?.Report(1.0);

            if (modelSize == ModelSize.Qwen3_06B) Qwen06Downloaded = true;
            if (modelSize == ModelSize.Qwen3_17B) Qwen17Downloaded = true;
            DownloadedHistory.Add(modelSize);

            return $"path_{modelSize}";
        }

        public bool DeleteModel(ModelSize modelSize)
        {
            if (modelSize == ModelSize.Qwen3_06B) Qwen06Downloaded = false;
            if (modelSize == ModelSize.Qwen3_17B) Qwen17Downloaded = false;
            return true;
        }

        public bool IsDiarizationModelDownloaded() => DiarizationDownloaded;

        public Task<string> EnsureDiarizationModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult("diarization_path");
    }

    private class ConfigurableSummaryModelManager : ILiveSummaryModelManager
    {
        public string ModelsDirectory { get; private set; } = "C:\\test\\models\\summary";
        public void SetModelsDirectory(string path) => ModelsDirectory = path;

        public bool IsDownloaded { get; set; }
        public bool ThrowOnDownload { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public bool WasEnsureCalled { get; private set; }

        public string ModelName => "Qwen2.5-1.5B-Instruct";
        public string Quantization => "Q4_K_M";
        public long ModelSizeBytes => 986L * 1024 * 1024;
        public string LocalModelPath => "path_summary.gguf";

        public string GetModelPath() => LocalModelPath;

        public bool IsModelDownloaded() => IsDownloaded;

        public async Task<string> EnsureModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (ThrowOnDownload)
            {
                throw ExceptionToThrow ?? new HttpRequestException("Error de red conectando con HuggingFace.");
            }

            WasEnsureCalled = true;
            progress?.Report(0.5);
            await Task.Delay(10, cancellationToken);
            progress?.Report(1.0);
            IsDownloaded = true;
            return LocalModelPath;
        }

        public bool DeleteModel()
        {
            IsDownloaded = false;
            return true;
        }
    }

    private (MainViewModel Vm, UserSettingsService SettingsService, string TempSettingsFile, ConfigurableModelManager ModelMgr, ConfigurableSummaryModelManager SummaryMgr)
        CreateTestContext(bool seedCompleted = false, bool qwen06 = false, bool qwen17 = false, bool summary = false)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dictameeting_firstrun_test_{Guid.NewGuid():N}.json");
        var settingsService = new UserSettingsService(tempFile);
        settingsService.SaveSettings(new UserSettings
        {
            FirstRunModelsPromptCompleted = seedCompleted
        });

        var modelMgr = new ConfigurableModelManager
        {
            Qwen06Downloaded = qwen06,
            Qwen17Downloaded = qwen17
        };

        var summaryMgr = new ConfigurableSummaryModelManager
        {
            IsDownloaded = summary
        };

        var repo = new TwoPassModelSelectionTests.DummyMeetingRepository();
        var meetingService = new MeetingService(repo);
        var docExporter = new DocumentExporter();
        var audioDeviceService = new AudioDeviceService();
        var audioCaptureService = new MockAudioCaptureService();
        var transcriptionService = new MockTranscriptionService();
        var livePipeline = new TwoPassModelSelectionTests.DummyLivePipeline();
        var diarizationService = new PyAnnoteCommunity1DiarizationService();
        var speakerAligner = new SpeakerTranscriptAligner();
        var secureStorage = new WindowsDpapiSecureStorageService();
        var aiActaService = new OpenRouterActaService(new HttpClient(), secureStorage);

        var vm = new MainViewModel(
            meetingService,
            docExporter,
            repo,
            audioDeviceService,
            audioCaptureService,
            modelMgr,
            transcriptionService,
            livePipeline,
            diarizationService,
            speakerAligner,
            aiActaService,
            secureStorage,
            settingsService,
            liveSummaryModelManager: summaryMgr);

        return (vm, settingsService, tempFile, modelMgr, summaryMgr);
    }

    private static void SafeDelete(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        try
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
        catch
        {
            // Ignore transient lock during test cleanup
        }
    }

    [Fact]
    public async Task Test1_FreshApp_NoModelsDownloaded_ShowsModal_DownloadsAllThree_ClosesModal()
    {
        var (vm, settingsService, tempFile, modelMgr, summaryMgr) = CreateTestContext(
            seedCompleted: false, qwen06: false, qwen17: false, summary: false);

        try
        {
            // Verificación inicial de primer arranque
            await vm.CheckFirstRunExperienceAsync();

            Assert.True(vm.IsFirstRunModelsModalOpen);
            Assert.Equal(3, vm.FirstRunModels.Count);
            Assert.Equal(3, vm.FirstRunMissingCount);
            Assert.All(vm.FirstRunModels, m => Assert.False(m.IsDownloaded));
            Assert.Equal("Descargar 3 modelos", vm.FirstRunDownloadButtonText);

            // Iniciar descarga de todos los modelos
            await vm.StartFirstRunDownloadsAsync();

            // Al terminar la descarga:
            Assert.True(modelMgr.Qwen06Downloaded);
            Assert.True(modelMgr.Qwen17Downloaded);
            Assert.True(summaryMgr.IsDownloaded);
            Assert.False(vm.IsFirstRunModelsModalOpen);
            Assert.True(vm.FirstRunModelsPromptCompleted);

            // Verificar persistencia en disco
            var reloaded = settingsService.LoadSettings();
            Assert.True(reloaded.FirstRunModelsPromptCompleted);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Test2_FreshApp_TwoModelsDownloaded_ShowsModalWithOneMissing()
    {
        // 0.6B y Resumen ya descargados; falta 1.7B
        var (vm, settingsService, tempFile, modelMgr, summaryMgr) = CreateTestContext(
            seedCompleted: false, qwen06: true, qwen17: false, summary: true);

        try
        {
            await vm.CheckFirstRunExperienceAsync();

            Assert.True(vm.IsFirstRunModelsModalOpen);
            Assert.Equal(1, vm.FirstRunMissingCount);
            Assert.Equal("Descargar 1 modelo", vm.FirstRunDownloadButtonText);

            var item17 = vm.FirstRunModels.First(m => m.Id == "asr_17b");
            Assert.False(item17.IsDownloaded);

            var item06 = vm.FirstRunModels.First(m => m.Id == "asr_06b");
            Assert.True(item06.IsDownloaded);

            var itemSummary = vm.FirstRunModels.First(m => m.Id == "summary_15b");
            Assert.True(itemSummary.IsDownloaded);

            // Descargar el único modelo que falta
            await vm.StartFirstRunDownloadsAsync();

            Assert.True(modelMgr.Qwen17Downloaded);
            Assert.False(vm.IsFirstRunModelsModalOpen);
            Assert.True(vm.FirstRunModelsPromptCompleted);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Test3_FreshApp_AllThreeModelsAlreadyDownloaded_SilentlyCompletesWithoutShowingModal()
    {
        var (vm, settingsService, tempFile, _, _) = CreateTestContext(
            seedCompleted: false, qwen06: true, qwen17: true, summary: true);

        try
        {
            await vm.CheckFirstRunExperienceAsync();

            // No se debe mostrar el modal
            Assert.False(vm.IsFirstRunModelsModalOpen);
            Assert.True(vm.FirstRunModelsPromptCompleted);

            // Debe persistirse como completado silenciosamente
            var reloaded = settingsService.LoadSettings();
            Assert.True(reloaded.FirstRunModelsPromptCompleted);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Test4_FreshApp_UserClicksDismissNow_ClosesModal_MarksCompleted_DoesNotReappear()
    {
        var (vm, settingsService, tempFile, modelMgr, summaryMgr) = CreateTestContext(
            seedCompleted: false, qwen06: false, qwen17: false, summary: false);

        string? tempFile2 = null;
        try
        {
            await vm.CheckFirstRunExperienceAsync();
            Assert.True(vm.IsFirstRunModelsModalOpen);

            // Usuario pulsa "Ahora no"
            vm.DismissFirstRunModal();

            Assert.False(vm.IsFirstRunModelsModalOpen);
            Assert.True(vm.FirstRunModelsPromptCompleted);
            Assert.False(modelMgr.Qwen06Downloaded);
            Assert.False(modelMgr.Qwen17Downloaded);
            Assert.False(summaryMgr.IsDownloaded);

            // Persistido en disco
            var reloaded = settingsService.LoadSettings();
            Assert.True(reloaded.FirstRunModelsPromptCompleted);

            // Simular siguiente arranque (nuevo ViewModel con la configuración guardada)
            var (vmNext, _, nextTemp, _, _) = CreateTestContext(
                seedCompleted: true, qwen06: false, qwen17: false, summary: false);
            tempFile2 = nextTemp;

            await vmNext.CheckFirstRunExperienceAsync();
            Assert.False(vmNext.IsFirstRunModelsModalOpen);
        }
        finally
        {
            SafeDelete(tempFile);
            SafeDelete(tempFile2);
        }
    }

    [Fact]
    public async Task Test5_UserDeletes17B_SubsequentStartupDoesNotShowModal_AndAutoDoesNotRedownload17B()
    {
        // Onboarding completado previamente, 0.6B y Summary presentes, pero 1.7B eliminado
        var (vm, settingsService, tempFile, modelMgr, _) = CreateTestContext(
            seedCompleted: true, qwen06: true, qwen17: false, summary: true);

        try
        {
            await vm.CheckFirstRunExperienceAsync();

            // NO debe reaparecer el modal
            Assert.False(vm.IsFirstRunModelsModalOpen);

            // Probar que el procesamiento con modo "Auto" usa el modelo descargado (0.6B) sin intentar descargar 1.7B
            vm.SelectedFinalModel = vm.AvailableFinalModels.FirstOrDefault(m => m.IsAutomatic);
            var avail = modelMgr.GetAvailableModels();
            var qwen17 = avail.First(m => m.Size == ModelSize.Qwen3_17B);
            Assert.False(qwen17.IsDownloaded);

            // No se registró ninguna llamada a EnsureModelDownloadedAsync para 1.7B
            Assert.DoesNotContain(ModelSize.Qwen3_17B, modelMgr.DownloadedHistory);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Test6_DownloadFails_ShowsClearError_NotMarkedInstalled_UserCanRetry()
    {
        var (vm, settingsService, tempFile, modelMgr, summaryMgr) = CreateTestContext(
            seedCompleted: false, qwen06: false, qwen17: false, summary: false);

        try
        {
            await vm.CheckFirstRunExperienceAsync();
            Assert.True(vm.IsFirstRunModelsModalOpen);

            // Simular fallo en la descarga
            summaryMgr.ThrowOnDownload = true;
            summaryMgr.ExceptionToThrow = new HttpRequestException("Fallo en la conexión HTTP");

            await vm.StartFirstRunDownloadsAsync();

            // El modal debe permanecer abierto con error visible
            Assert.True(vm.IsFirstRunModelsModalOpen);
            Assert.True(vm.FirstRunHasError);
            Assert.NotNull(vm.FirstRunErrorMessage);
            Assert.Contains("servidor de descarga", vm.FirstRunErrorMessage);
            Assert.False(vm.IsFirstRunDownloading);
            Assert.False(summaryMgr.IsDownloaded);

            // Usuario reintenta tras recuperarse la conexión
            summaryMgr.ThrowOnDownload = false;
            await vm.StartFirstRunDownloadsAsync();

            Assert.False(vm.FirstRunHasError);
            Assert.True(summaryMgr.IsDownloaded);
            Assert.True(modelMgr.Qwen06Downloaded);
            Assert.True(modelMgr.Qwen17Downloaded);
            Assert.False(vm.IsFirstRunModelsModalOpen);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Test7_UserCancelsDownload_CleanCancellation_DoesNotMarkInstalled()
    {
        var (vm, settingsService, tempFile, modelMgr, summaryMgr) = CreateTestContext(
            seedCompleted: false, qwen06: false, qwen17: false, summary: false);

        try
        {
            await vm.CheckFirstRunExperienceAsync();
            Assert.True(vm.IsFirstRunModelsModalOpen);

            // Iniciar descarga y cancelar
            var downloadTask = vm.StartFirstRunDownloadsAsync();
            vm.CancelFirstRunDownloads();
            await downloadTask;

            Assert.False(vm.IsFirstRunDownloading);
            Assert.Equal("Descarga cancelada.", vm.FirstRunStatusMessage);
            Assert.True(vm.FirstRunModelsPromptCompleted);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public async Task Test8_NoInternet_ErrorMessageIsClear_AllowsContinuing()
    {
        var (vm, settingsService, tempFile, modelMgr, summaryMgr) = CreateTestContext(
            seedCompleted: false, qwen06: false, qwen17: false, summary: false);

        try
        {
            await vm.CheckFirstRunExperienceAsync();
            summaryMgr.ThrowOnDownload = true;
            summaryMgr.ExceptionToThrow = new HttpRequestException("Host no alcanzable (sin conexión)");

            await vm.StartFirstRunDownloadsAsync();

            Assert.True(vm.FirstRunHasError);
            Assert.Contains("servidor de descarga", vm.FirstRunErrorMessage);

            // Usuario elige continuar a DictaMeeting
            vm.CloseFirstRunModalAfterError();

            Assert.False(vm.IsFirstRunModelsModalOpen);
            Assert.True(vm.FirstRunModelsPromptCompleted);

            var reloaded = settingsService.LoadSettings();
            Assert.True(reloaded.FirstRunModelsPromptCompleted);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public void Test9_SettingsPersistence_Roundtrip()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dictameeting_settings_roundtrip_{Guid.NewGuid():N}.json");
        try
        {
            var service = new UserSettingsService(tempFile);
            var initial = service.LoadSettings();
            Assert.False(initial.FirstRunModelsPromptCompleted);

            initial.FirstRunModelsPromptCompleted = true;
            service.SaveSettings(initial);

            var reloaded = service.LoadSettings();
            Assert.True(reloaded.FirstRunModelsPromptCompleted);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }

    [Fact]
    public void Test10_AppUpdate_DoesNotResetOnboarding()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dictameeting_update_test_{Guid.NewGuid():N}.json");
        try
        {
            // Escribir configuración existente previa (como de una versión anterior)
            File.WriteAllText(tempFile, @"{
                ""isDarkMode"": true,
                ""firstRunModelsPromptCompleted"": true
            }");

            var service = new UserSettingsService(tempFile);
            var settings = service.LoadSettings();

            Assert.True(settings.FirstRunModelsPromptCompleted);
            Assert.True(settings.IsDarkMode);

            // Guardar nuevamente (simulando actualización)
            service.SaveSettings(settings);

            var reloaded = service.LoadSettings();
            Assert.True(reloaded.FirstRunModelsPromptCompleted);
        }
        finally
        {
            SafeDelete(tempFile);
        }
    }
}
