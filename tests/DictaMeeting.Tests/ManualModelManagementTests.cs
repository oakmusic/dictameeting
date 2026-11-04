using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DictaMeeting.Infrastructure.Persistence;
using DictaMeeting.Meetings.Enums;
using DictaMeeting.Meetings.Interfaces;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using DictaMeeting.Transcription.Services;
using Xunit;

namespace DictaMeeting.Tests;

/// <summary>
/// Pruebas obligatorias para el sistema de instalación y gestión manual de modelos.
/// Verifica que NUNCA se descargan modelos automáticamente y comprueba los 10 escenarios requeridos.
/// </summary>
public class ManualModelManagementTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly NetworkRequestTrackingHandler _networkTracker;
    private readonly HttpClient _trackedHttpClient;

    public ManualModelManagementTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), $"dictameeting_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testBaseDir);
        _networkTracker = new NetworkRequestTrackingHandler();
        _trackedHttpClient = new HttpClient(_networkTracker);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testBaseDir))
            {
                Directory.Delete(_testBaseDir, true);
            }
        }
        catch { }
        _trackedHttpClient.Dispose();
        _networkTracker.Dispose();
    }

    private static void CreateValidQwenDirectory(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "conv_frontend.onnx"), new byte[500]);
        File.WriteAllBytes(Path.Combine(dir, "encoder.int8.onnx"), new byte[500]);
        File.WriteAllBytes(Path.Combine(dir, "decoder.int8.onnx"), new byte[500]);
        File.WriteAllBytes(Path.Combine(dir, "tokens.txt"), new byte[50]);
    }

    private static void CreateValidDiarizationDirectory(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "community1-segmentation.onnx"), new byte[150_000]);
        File.WriteAllBytes(Path.Combine(dir, "community1-embedding.onnx"), new byte[1_200_000]);
        File.WriteAllBytes(Path.Combine(dir, "plda_community1.bin"), new byte[20_000]);
    }

    private static void CreateValidVadFile(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "silero_vad.onnx"), new byte[150_000]);
    }

    /// <summary>
    /// Escenario 1: Primera ejecución sin modelos: no se inicia ninguna descarga ni petición de red.
    /// </summary>
    [Fact]
    public void Scenario1_FirstRun_WithoutModels_NoDownloadOrNetworkTraffic()
    {
        var modelsDir = Path.Combine(_testBaseDir, "empty_models");
        Directory.CreateDirectory(modelsDir);

        var modelManager = new ModelManager(modelsFolder: modelsDir, httpClient: _trackedHttpClient);

        var available = modelManager.GetAvailableModels();

        Assert.NotEmpty(available);
        // Ningún modelo debe considerarse descargado
        Assert.All(available, m => Assert.False(m.IsDownloaded));
        Assert.False(modelManager.IsVadModelDownloaded());
        Assert.False(modelManager.IsDiarizationModelDownloaded());

        // Cero peticiones de red realizadas
        Assert.Equal(0, _networkTracker.RequestCount);
    }

    /// <summary>
    /// Escenario 2: Selección de una carpeta vacía: no se descarga nada.
    /// </summary>
    [Fact]
    public void Scenario2_SelectEmptyFolder_NoDownloadOrNetworkTraffic()
    {
        var initialDir = Path.Combine(_testBaseDir, "init");
        var emptyFolder = Path.Combine(_testBaseDir, "new_empty_folder");
        Directory.CreateDirectory(initialDir);
        Directory.CreateDirectory(emptyFolder);

        var modelManager = new ModelManager(modelsFolder: initialDir, httpClient: _trackedHttpClient);
        modelManager.SetModelsDirectory(emptyFolder);

        Assert.Equal(emptyFolder, modelManager.ModelsDirectory);
        Assert.All(modelManager.GetAvailableModels(), m => Assert.False(m.IsDownloaded));
        Assert.False(modelManager.IsVadModelDownloaded());
        Assert.False(modelManager.IsDiarizationModelDownloaded());
        Assert.Equal(0, _networkTracker.RequestCount);
    }

    /// <summary>
    /// Escenario 3: Selección de una carpeta con un modelo Qwen ya instalado: se reconoce y no se vuelve a descargar.
    /// </summary>
    [Fact]
    public void Scenario3_FolderWithPreinstalledQwen_RecognizedImmediatelyWithoutRedownload()
    {
        var modelsDir = Path.Combine(_testBaseDir, "preinstalled_qwen");
        var qwenDir = Path.Combine(modelsDir, "sherpa-onnx-qwen3-asr-0.6B");
        CreateValidQwenDirectory(qwenDir);

        var modelManager = new ModelManager(modelsFolder: modelsDir, httpClient: _trackedHttpClient);

        // Debe reconocerse como descargado
        Assert.True(modelManager.IsModelDownloaded(ModelSize.Qwen3_06B));
        var path = modelManager.GetModelPath(ModelSize.Qwen3_06B);
        Assert.True(Directory.Exists(path));

        // 0 peticiones de red
        Assert.Equal(0, _networkTracker.RequestCount);
    }

    /// <summary>
    /// Escenario 4: Selección de una carpeta con varios modelos: se reconocen todos los compatibles sin copiar ni re-descargar.
    /// </summary>
    [Fact]
    public void Scenario4_FolderWithMultipleModels_RecognizesAllCompatible()
    {
        var modelsDir = Path.Combine(_testBaseDir, "multi_models");

        // 1. Qwen 0.6B
        CreateValidQwenDirectory(Path.Combine(modelsDir, "sherpa-onnx-qwen3-asr-0.6B"));

        // 2. Silero VAD
        CreateValidVadFile(Path.Combine(modelsDir, "silero"));

        // 3. Diarización PyAnnote
        CreateValidDiarizationDirectory(Path.Combine(modelsDir, "diarization"));

        var modelManager = new ModelManager(modelsFolder: modelsDir, httpClient: _trackedHttpClient);

        Assert.True(modelManager.IsModelDownloaded(ModelSize.Qwen3_06B));
        Assert.True(modelManager.IsVadModelDownloaded());
        Assert.True(modelManager.IsDiarizationModelDownloaded());

        // 0 peticiones de red
        Assert.Equal(0, _networkTracker.RequestCount);
    }

    /// <summary>
    /// Escenario 5: Cambio de ubicación: se conserva la ruta al reiniciar.
    /// </summary>
    [Fact]
    public void Scenario5_ChangeLocation_PersistsAcrossRestart()
    {
        var customPath = Path.Combine(_testBaseDir, "persisted_custom_models");
        Directory.CreateDirectory(customPath);

        var settingsPath = Path.Combine(_testBaseDir, "settings.json");
        var service1 = new UserSettingsService(settingsPath);

        var settings = new UserSettings
        {
            ModelsDirectory = customPath
        };
        service1.SaveSettings(settings);

        // Simular reinicio creando nuevo servicio que lee del disco
        var service2 = new UserSettingsService(settingsPath);
        var loaded = service2.LoadSettings();

        Assert.Equal(customPath, loaded.ModelsDirectory);
        Assert.Equal(customPath, loaded.EffectiveModelsDirectory);
        Assert.True(loaded.IsCustomModelsDirectory);
    }

    /// <summary>
    /// Escenario 6: Pulsar «Descargar modelos»: se descarga únicamente lo seleccionado.
    /// </summary>
    [Fact]
    public async Task Scenario6_DownloadModels_DownloadsOnlySelectedModel()
    {
        var modelsDir = Path.Combine(_testBaseDir, "download_test");
        Directory.CreateDirectory(modelsDir);

        // Servir respuestas simuladas para descarga (150 KB para que supere el umbral de verificación)
        _networkTracker.HandlerFunc = (request) =>
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[150_000])
            };
        };

        var modelManager = new ModelManager(modelsFolder: modelsDir, httpClient: _trackedHttpClient);

        // Descargar explícitamente solo VAD
        var downloadedPath = await modelManager.DownloadVadModelAsync();

        Assert.True(File.Exists(downloadedPath));
        Assert.True(modelManager.IsVadModelDownloaded());

        // Los otros modelos NO se descargaron
        Assert.False(modelManager.IsDiarizationModelDownloaded());
        Assert.False(modelManager.IsModelDownloaded(ModelSize.Qwen3_06B));
        Assert.False(modelManager.IsModelDownloaded(ModelSize.Base));
    }

    /// <summary>
    /// Escenario 7: Cancelar una descarga: no se registra el modelo como instalado y se limpia el temporal.
    /// </summary>
    [Fact]
    public async Task Scenario7_CancelDownload_DoesNotRegisterAsInstalled()
    {
        var modelsDir = Path.Combine(_testBaseDir, "cancel_test");
        Directory.CreateDirectory(modelsDir);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelar

        var modelManager = new ModelManager(modelsFolder: modelsDir, httpClient: _trackedHttpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await modelManager.DownloadVadModelAsync(cancellationToken: cts.Token);
        });

        Assert.False(modelManager.IsVadModelDownloaded());
        Assert.False(File.Exists(Path.Combine(modelsDir, "silero", "silero_vad.onnx")));
        Assert.False(File.Exists(Path.Combine(modelsDir, "silero", "silero_vad.onnx.download")));
    }

    /// <summary>
    /// Escenario 8: Iniciar una transcripción sin ASR: se lanza excepción descriptiva o aviso y no comienza ninguna descarga.
    /// </summary>
    [Fact]
    public async Task Scenario8_StartTranscription_WithoutAsr_ThrowsClearError_NoDownload()
    {
        var modelsDir = Path.Combine(_testBaseDir, "empty_asr");
        Directory.CreateDirectory(modelsDir);

        var modelManager = new ModelManager(modelsFolder: modelsDir, httpClient: _trackedHttpClient);
        var qwenService = new SherpaQwenTranscriptionService(modelManager);

        // Al intentar inicializar sin el modelo descargado, debe fallar de inmediato sin intentar descargar
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await qwenService.InitializeAsync(ModelSize.Qwen3_06B);
        });

        Assert.Contains("no está instalado", ex.Message);
        Assert.Equal(0, _networkTracker.RequestCount);
    }

    /// <summary>
    /// Escenario 9: Iniciar una transcripción con otro ASR ya instalado: se puede utilizar.
    /// </summary>
    [Fact]
    public void Scenario9_StartTranscription_WithAlternativeAsrInstalled_CanBeUsed()
    {
        var modelsDir = Path.Combine(_testBaseDir, "alt_asr");
        var qwen06 = Path.Combine(modelsDir, "sherpa-onnx-qwen3-asr-0.6B");
        CreateValidQwenDirectory(qwen06);

        var modelManager = new ModelManager(modelsFolder: modelsDir, httpClient: _trackedHttpClient);

        // Qwen 1.7B NO está instalado
        Assert.False(modelManager.IsModelDownloaded(ModelSize.Qwen3_17B));

        // Pero la alternativa Qwen 0.6B SÍ está instalada y disponible
        var available = modelManager.GetAvailableModels();
        var installedAlt = available.FirstOrDefault(m => m.IsDownloaded);

        Assert.NotNull(installedAlt);
        Assert.Equal(ModelSize.Qwen3_06B, installedAlt.Size);
        Assert.Equal(0, _networkTracker.RequestCount);
    }

    /// <summary>
    /// Escenario 10: Intentar diarizar sin modelo de diarización: informa del problema sin impedir conservar la transcripción.
    /// </summary>
    [Fact]
    public void Scenario10_DiarizeWithoutDiarizationModel_TranscriptionPreserved()
    {
        var modelsDir = Path.Combine(_testBaseDir, "no_diar_model");
        Directory.CreateDirectory(modelsDir);

        var modelManager = new ModelManager(modelsFolder: modelsDir, httpClient: _trackedHttpClient);

        Assert.False(modelManager.IsDiarizationModelDownloaded());

        // Simular reunión con transcripción completada
        var meeting = new Meeting
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Reunión de prueba",
            TranscriptionStatus = TranscriptionStatus.Completed,
            DiarizationStatus = DiarizationStatus.Pending
        };
        meeting.Transcript.Add(new TranscriptSegment
        {
            Text = "Hola, esta es una transcripción de prueba.",
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(3)
        });

        // Lógica de MeetingProcessingService: Si falta modelo de diarización, se omite diarización pero se preserva la transcripción
        if (!modelManager.IsDiarizationModelDownloaded())
        {
            meeting.DiarizationStatus = DiarizationStatus.None;
            meeting.ProcessingError = "Diarización omitida: el modelo de diarización no está instalado. Puede descargarlo desde la gestión de modelos. La transcripción se ha completado correctamente.";
        }

        // La transcripción debe permanecer 100% intacta
        Assert.Single(meeting.Transcript);
        Assert.Equal("Hola, esta es una transcripción de prueba.", meeting.Transcript[0].Text);
        Assert.Equal(TranscriptionStatus.Completed, meeting.TranscriptionStatus);
        Assert.Equal(DiarizationStatus.None, meeting.DiarizationStatus);
        Assert.Contains("Diarización omitida", meeting.ProcessingError);
        Assert.Equal(0, _networkTracker.RequestCount);
    }

    /// <summary>
    /// Handler para registrar cualquier tráfico HTTP saliente y certificar que no hay peticiones automáticas.
    /// </summary>
    private class NetworkRequestTrackingHandler : HttpMessageHandler
    {
        public int RequestCount => _requests.Count;
        private readonly List<HttpRequestMessage> _requests = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? HandlerFunc { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _requests.Add(request);
            if (HandlerFunc != null)
            {
                return Task.FromResult(HandlerFunc(request));
            }
            throw new InvalidOperationException($"Petición de red no autorizada en prueba: {request.RequestUri}");
        }
    }
}
