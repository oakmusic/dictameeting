using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using DictaMeeting.AI.Services;
using DictaMeeting.Meetings.Models;
using Xunit;

namespace DictaMeeting.Tests;

public class LiveSummaryCoordinatorTests
{
    private class MockSummaryService : ILiveSummaryService
    {
        public int GenerateCount { get; private set; }
        public bool IsGenerating { get; set; }
        public bool IsModelLoaded { get; set; } = true;
        public SummaryMetrics? LastMetrics { get; set; }
        public Func<string, string, string, CancellationToken, Task<string>>? CustomGenerator { get; set; }
        public bool WasCancelled { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<string> GenerateSummaryAsync(
            string previousSummary,
            string newTranscriptWindow,
            string language = "Spanish",
            CancellationToken cancellationToken = default)
        {
            GenerateCount++;
            if (CustomGenerator != null)
            {
                return await CustomGenerator(previousSummary, newTranscriptWindow, language, cancellationToken);
            }

            return $"Resumen del tramo {GenerateCount}: {newTranscriptWindow}";
        }

        public void CancelCurrentGeneration()
        {
            WasCancelled = true;
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private class MockModelManager : ILiveSummaryModelManager
    {
        public string ModelsDirectory { get; private set; } = "C:\\test\\models\\summary";
        public void SetModelsDirectory(string path) => ModelsDirectory = path;

        public bool ModelDownloaded { get; set; } = true;
        public string ModelName => "Qwen2.5-1.5B-Instruct";
        public string Quantization => "Q4_K_M";
        public long ModelSizeBytes => 986L * 1024 * 1024;
        public string LocalModelPath => "dummy/path/model.gguf";
        public string GetModelPath() => LocalModelPath;
        public bool IsModelDownloaded() => ModelDownloaded;
        public Task<string> EnsureModelDownloadedAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(LocalModelPath);
        public bool DeleteModel() => true;
    }

    [Fact]
    public void ExtractSlidingWindowText_PreservesWholeSegmentsWithinDuration()
    {
        var segments = new List<TranscriptSegment>
        {
            new() { Id = "1", SpeakerDisplayName = "Aritz", StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(20), Text = "Punto inicial." },
            new() { Id = "2", SpeakerDisplayName = "Marta", StartTime = TimeSpan.FromSeconds(25), EndTime = TimeSpan.FromSeconds(50), Text = "Segunda intervención." },
            new() { Id = "3", SpeakerDisplayName = "Carlos", StartTime = TimeSpan.FromSeconds(55), EndTime = TimeSpan.FromSeconds(90), Text = "Tercera intervención relevante." },
            new() { Id = "4", SpeakerDisplayName = "Aritz", StartTime = TimeSpan.FromSeconds(95), EndTime = TimeSpan.FromSeconds(110), Text = "Cuarta intervención final." }
        };

        // Ventana de 60s hacia atrás desde EndTime = 110s -> Umbral = 50s.
        // Debe incluir el segmento 2 (termina en 50s), segmento 3 (termina en 90s) y segmento 4 (termina en 110s).
        var windowText = LiveSummaryCoordinator.ExtractSlidingWindowText(segments, TimeSpan.FromSeconds(60));

        Assert.Contains("Marta: Segunda intervención.", windowText);
        Assert.Contains("Carlos: Tercera intervención relevante.", windowText);
        Assert.Contains("Aritz: Cuarta intervención final.", windowText);
        Assert.DoesNotContain("Punto inicial", windowText);
    }

    [Fact]
    public async Task CumulativeSummary_UpdatesAccumulativelyWithoutInfiniteGrowth()
    {
        var mockService = new MockSummaryService();
        var mockManager = new MockModelManager();
        var config = new LiveSummaryConfig
        {
            SummaryInterval = TimeSpan.FromMilliseconds(50),
            MinimumNewWordsThreshold = 5
        };

        var coordinator = new LiveSummaryCoordinator(mockService, mockManager, config);
        coordinator.Start("Spanish");

        // Paso 1: Añadir primer tramo y disparar resumen inmediato
        coordinator.AddSegment(new TranscriptSegment
        {
            Id = "0",
            SpeakerDisplayName = "Aritz",
            StartTime = TimeSpan.FromSeconds(0),
            EndTime = TimeSpan.FromSeconds(10),
            Text = "Se discuten los requerimientos de la nueva arquitectura de microservicios."
        });

        await coordinator.TriggerImmediateSummaryAsync();
        Assert.Equal(1, mockService.GenerateCount);
        Assert.Contains("Resumen del tramo 1", coordinator.CurrentSummary);
        Assert.Single(coordinator.SummaryCards);

        // Paso 2: Añadir nuevo contenido y disparar actualización acumulativa (siguiente tarjeta)
        coordinator.AddSegment(new TranscriptSegment
        {
            Id = "1",
            SpeakerDisplayName = "Aritz",
            StartTime = TimeSpan.FromSeconds(10),
            EndTime = TimeSpan.FromSeconds(20),
            Text = "Hemos validado que la latencia en CPU es inferior a dos segundos por bloque."
        });

        await coordinator.TriggerImmediateSummaryAsync();
        Assert.Equal(2, mockService.GenerateCount);
        Assert.Contains("Resumen del tramo 1", coordinator.CurrentSummary);
        Assert.Contains("Resumen del tramo 2", coordinator.CurrentSummary);
        Assert.Equal(2, coordinator.SummaryCards.Count);

        coordinator.Stop();
    }

    [Fact]
    public async Task ConcurrencyProtection_PreventsSimultaneousGenerations()
    {
        var mockService = new MockSummaryService();
        var mockManager = new MockModelManager();
        var tcs = new TaskCompletionSource<string>();

        mockService.CustomGenerator = async (prev, win, lang, ct) =>
        {
            // Simular inferencia prolongada
            return await tcs.Task;
        };

        var config = new LiveSummaryConfig { MinimumNewWordsThreshold = 1 };
        var coordinator = new LiveSummaryCoordinator(mockService, mockManager, config);
        coordinator.Start("Spanish");

        coordinator.AddSegment(new TranscriptSegment
        {
            Id = "1",
            SpeakerDisplayName = "Aritz",
            StartTime = TimeSpan.FromSeconds(0),
            EndTime = TimeSpan.FromSeconds(10),
            Text = "Prueba de concurrencia y protección de inferencias en paralelo."
        });

        // Disparar primera generación en background
        var task1 = coordinator.TriggerImmediateSummaryAsync();
        await Task.Delay(20);

        Assert.True(coordinator.IsGenerating);

        // Intentar disparar segunda generación mientras la primera está en curso
        var task2 = coordinator.TriggerImmediateSummaryAsync();
        await task2; // Debe retornar inmediatamente sin encolar otra generación

        Assert.Equal(1, mockService.GenerateCount);

        // Completar la primera inferencia
        tcs.SetResult("Resumen completado sobre la prueba de concurrencia de inferencias en paralelo.");
        await task1;

        Assert.False(coordinator.IsGenerating);
        Assert.Contains("Resumen completado", coordinator.CurrentSummary);

        coordinator.Stop();
    }

    [Fact]
    public void StopMeeting_CancelsInferenceAndStopsCoordinator()
    {
        var mockService = new MockSummaryService();
        var mockManager = new MockModelManager();

        var coordinator = new LiveSummaryCoordinator(mockService, mockManager);
        coordinator.Start("Spanish");
        Assert.True(coordinator.IsRunning);

        coordinator.Stop();

        Assert.False(coordinator.IsRunning);
        Assert.True(mockService.WasCancelled);
    }

    [Fact]
    public async Task FallbackOnException_DoesNotCrashAndRetainsState()
    {
        var mockService = new MockSummaryService();
        var mockManager = new MockModelManager();

        mockService.CustomGenerator = (prev, win, lang, ct) =>
        {
            throw new InvalidOperationException("Fallo temporal de memoria nativa");
        };

        var coordinator = new LiveSummaryCoordinator(mockService, mockManager);
        coordinator.Start("Spanish");

        // Disparar inferencia con fallo
        await coordinator.TriggerImmediateSummaryAsync();

        // Debe mantenerse en estado consistente sin lanzar excepción al llamador
        Assert.False(coordinator.IsGenerating);
        Assert.Equal(string.Empty, coordinator.CurrentSummary);

        coordinator.Stop();
    }
}
