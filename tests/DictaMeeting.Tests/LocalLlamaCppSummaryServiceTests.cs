using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.AI.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class LocalLlamaCppSummaryServiceTests
{
    private class DummyModelManager : ILiveSummaryModelManager
    {
        public string ModelsDirectory { get; private set; } = "C:\\test\\models\\summary";
        public void SetModelsDirectory(string path) => ModelsDirectory = path;

        public string ModelName => "Qwen2.5-1.5B-Instruct";
        public string Quantization => "Q4_K_M";
        public long ModelSizeBytes => 986L * 1024 * 1024;
        public string LocalModelPath => "non_existent_model.gguf";
        public string GetModelPath() => LocalModelPath;
        public bool IsModelDownloaded() => false;
        public Task<string> EnsureModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(LocalModelPath);
        public bool DeleteModel() => true;
    }

    [Fact]
    public void Service_InitialState_IsNotLoadedAndNotGenerating()
    {
        var modelManager = new DummyModelManager();
        var config = new LiveSummaryConfig();
        using var service = new LocalLlamaCppSummaryService(modelManager, config);

        Assert.False(service.IsModelLoaded);
        Assert.False(service.IsGenerating);
        Assert.Null(service.LastMetrics);
    }

    [Fact]
    public async Task InitializeAsync_ThrowsFileNotFound_WhenModelDoesNotExist()
    {
        var modelManager = new DummyModelManager();
        var config = new LiveSummaryConfig();
        using var service = new LocalLlamaCppSummaryService(modelManager, config);

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.InitializeAsync());
    }

    [Fact]
    public void CancelCurrentGeneration_DoesNotThrow_WhenIdle()
    {
        var modelManager = new DummyModelManager();
        var config = new LiveSummaryConfig();
        using var service = new LocalLlamaCppSummaryService(modelManager, config);

        var ex = Record.Exception(() => service.CancelCurrentGeneration());
        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesResourcesSafely()
    {
        var modelManager = new DummyModelManager();
        var config = new LiveSummaryConfig();
        var service = new LocalLlamaCppSummaryService(modelManager, config);

        await service.DisposeAsync();
        Assert.False(service.IsModelLoaded);
    }

    private class RealTestModelManager : ILiveSummaryModelManager
    {
        private readonly string _path;
        public RealTestModelManager(string path) => _path = path;
        public string ModelsDirectory { get; private set; } = "C:\\test\\models\\summary";
        public void SetModelsDirectory(string path) => ModelsDirectory = path;
        public string ModelName => "Qwen2.5-1.5B-Instruct";
        public string Quantization => "Q4_K_M";
        public long ModelSizeBytes => new FileInfo(_path).Length;
        public string LocalModelPath => _path;
        public string GetModelPath() => _path;
        public bool IsModelDownloaded() => File.Exists(_path);
        public Task<string> EnsureModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(_path);
        public bool DeleteModel() => true;
    }

    [Fact]
    public async Task RealModel_SummarizesUserTranscript_Correctly()
    {
        var localPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models", "summary", "qwen2.5-1.5b-instruct-q4_k_m.gguf");
        if (!File.Exists(localPath)) return;

        var modelManager = new RealTestModelManager(localPath);
        var config = new LiveSummaryConfig();
        using var service = new LocalLlamaCppSummaryService(modelManager, config);
        await service.InitializeAsync();

        string transcript = """
Pero vamos a empezar con B o tres uno y para ello vamos a utilizar este
Fijaos, anima la imagen anterior con estilo cinematográfico.
Dónde el explorador ártico camina por la nieve y ahora tengo que especificar todo.
Los datos de la generación, el modelo, quiero V tres uno fast.
El asperito dieciséis nueve duración quiero seis segundos y resuelve
Solución 720. Le vamos a enviar.
Y ya tenemos el video generado. Podemos descargarlo haciendo clic en este icono. Vamos.
Lo pantalla completa.
Fijaos cómo se aprecia aquí ese viento helado. La verdad es que no está nada.
Vamos ahora a generar una imagen de referencia para la segunda escena y des.
""";

        var result = await service.GenerateSummaryAsync(string.Empty, transcript, "Spanish");
        Assert.NotNull(result);
        Assert.DoesNotContain("Ninguno", result);
        Assert.DoesNotContain(": Pero vamos a empezar", result);
        Assert.True(result.Length >= 20, $"Summary was too short: '{result}'");

        string transcriptPart2 = """
Pues utilizaremos el modelo de vídeo en vez de Beatriz Uno Flash. Usaremos Omni.
Uno uno flash, fijaos en el pro.
Genera una imagen de referencia para la segunda escena. Mantén.
El estilo de la imagen anterior y la consistencia de los personajes en esta escena.
El explorador ártico se muestra frente a una nave espacial abandonada y congelada.
""";

        var result2 = await service.GenerateSummaryAsync(result, transcriptPart2, "Spanish");
        Assert.NotNull(result2);
        Assert.DoesNotContain("Ninguno", result2);
        Assert.True(result2.Length >= 20, $"Summary 2 was too short: '{result2}'");
    }
}
