using System.Diagnostics;
using System.Text;
using DictaMeeting.AI.Interfaces;
using DictaMeeting.AI.Models;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using DictaMeeting.Meetings.Vocabulary;

namespace DictaMeeting.AI.Services;

/// <summary>
/// Motor local de inferencia de resumen basado en llama.cpp embebido mediante LLamaSharp.
/// Optimizado para CPU x64 sin dependencias externas (sin Ollama, sin Docker, sin Python).
/// </summary>
public class LocalLlamaCppSummaryService : ILiveSummaryService
{
    private readonly ILiveSummaryModelManager _modelManager;
    private readonly LiveSummaryConfig _config;
    private readonly IVocabularyService? _vocabularyService;
    private readonly ILogger<LocalLlamaCppSummaryService>? _logger;

    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private LLamaWeights? _weights;
    private ModelParams? _modelParams;
    private CancellationTokenSource? _currentGenerationCts;
    private bool _isGenerating;
    private bool _disposed;
    private SummaryMetrics? _lastMetrics;

    public bool IsModelLoaded => _weights != null && !_weights.NativeHandle.IsClosed;
    public bool IsGenerating => _isGenerating;
    public SummaryMetrics? LastMetrics => _lastMetrics;

    public LocalLlamaCppSummaryService(
        ILiveSummaryModelManager modelManager,
        LiveSummaryConfig? config = null,
        IVocabularyService? vocabularyService = null,
        ILogger<LocalLlamaCppSummaryService>? logger = null)
    {
        _modelManager = modelManager;
        _config = config ?? new LiveSummaryConfig();
        _vocabularyService = vocabularyService;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsModelLoaded) return;

        await _inferenceLock.WaitAsync(cancellationToken);
        try
        {
            if (IsModelLoaded) return;

            var modelPath = _modelManager.GetModelPath();
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"El modelo GGUF no se encuentra en el disco: {modelPath}");
            }

            var fileInfo = new FileInfo(modelPath);
            int threadCount = _config.ThreadCount ?? Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));

            _logger?.LogInformation(
                "Inicializando motor llama.cpp para resumen con modelo: {Model} ({Quantization}, {SizeMB} MB) usando {Threads} hilos CPU...",
                _modelManager.ModelName,
                _modelManager.Quantization,
                fileInfo.Length / (1024 * 1024),
                threadCount);

            var sw = Stopwatch.StartNew();

            _modelParams = new ModelParams(modelPath)
            {
                ContextSize = _config.ContextSizeTokens,
                GpuLayerCount = _config.EnableGpuIfAvailable ? 20 : 0,
                Threads = threadCount,
                BatchSize = 512
            };

            // Cargar pesos en memoria de forma asíncrona para no bloquear el hilo de llamada
            _weights = await Task.Run(() => LLamaWeights.LoadFromFile(_modelParams), cancellationToken);

            sw.Stop();
            _logger?.LogInformation("Modelo llama.cpp cargado con éxito en {DurationMs} ms.", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error al inicializar el modelo llama.cpp para resumen en vivo.");
            throw;
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    public async Task<string> GenerateSummaryAsync(
        string previousSummary,
        string newTranscriptWindow,
        string language = "Spanish",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newTranscriptWindow))
        {
            return previousSummary;
        }

        if (!IsModelLoaded)
        {
            await InitializeAsync(cancellationToken);
        }

        if (_weights == null || _modelParams == null)
        {
            throw new InvalidOperationException("El modelo llama.cpp no está inicializado.");
        }

        await _inferenceLock.WaitAsync(cancellationToken);
        _isGenerating = true;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentGenerationCts = linkedCts;

        var prompt = LiveSummaryPromptBuilder.BuildPrompt(
            previousSummary,
            newTranscriptWindow,
            language,
            _vocabularyService?.GetOfficialWords());
        var sw = Stopwatch.StartNew();
        int inputTokens = 0;
        int generatedTokens = 0;
        var sb = new StringBuilder();

        try
        {
            // StatelessExecutor crea un contexto limpio de 2048 tokens y lo libera al terminar la inferencia,
            // garantizando que entre resúmenes (cada 35s) DictaMeeting consuma la menor RAM posible.
            var executor = new StatelessExecutor(_weights, _modelParams);

            var inferenceParams = new InferenceParams
            {
                MaxTokens = _config.MaxTokensToGenerate,
                AntiPrompts = new[] { "<|im_end|>", "<|endoftext|>", "<|im_start|>" },
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = _config.Temperature,
                    TopP = _config.TopP
                }
            };

            await foreach (var token in executor.InferAsync(prompt, inferenceParams, linkedCts.Token))
            {
                sb.Append(token);
                generatedTokens++;
            }

            sw.Stop();

            var rawResult = sb.ToString();
            var sanitizedSummary = LiveSummaryPromptBuilder.SanitizeSummaryOutput(rawResult, previousSummary);

            double tokensPerSecond = sw.Elapsed.TotalSeconds > 0 ? generatedTokens / sw.Elapsed.TotalSeconds : 0;

            _lastMetrics = new SummaryMetrics(
                ModelName: _modelManager.ModelName,
                Quantization: _modelManager.Quantization,
                ModelSizeBytes: _modelManager.ModelSizeBytes,
                LoadDuration: TimeSpan.Zero,
                InferenceDuration: sw.Elapsed,
                InputTokens: inputTokens,
                GeneratedTokens: generatedTokens,
                TokensPerSecond: Math.Round(tokensPerSecond, 1),
                Success: true
            );

            _logger?.LogInformation(
                "Resumen en vivo generado en {ElapsedMs} ms ({Tokens} tokens, {Speed} t/s).",
                sw.ElapsedMilliseconds,
                generatedTokens,
                _lastMetrics.TokensPerSecond);

            return sanitizedSummary;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Generación de resumen en vivo cancelada.");
            return previousSummary;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _lastMetrics = new SummaryMetrics(
                ModelName: _modelManager.ModelName,
                Quantization: _modelManager.Quantization,
                ModelSizeBytes: _modelManager.ModelSizeBytes,
                LoadDuration: TimeSpan.Zero,
                InferenceDuration: sw.Elapsed,
                InputTokens: inputTokens,
                GeneratedTokens: generatedTokens,
                TokensPerSecond: 0,
                Success: false,
                ErrorMessage: ex.Message
            );

            _logger?.LogError(ex, "Error durante la inferencia del resumen en vivo.");
            return previousSummary; // Fallback: mantener el resumen previo sin interrumpir la reunión
        }
        finally
        {
            _isGenerating = false;
            _currentGenerationCts = null;
            _inferenceLock.Release();
        }
    }

    public void CancelCurrentGeneration()
    {
        try
        {
            _currentGenerationCts?.Cancel();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error al cancelar la generación actual.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancelCurrentGeneration();

        try
        {
            _weights?.Dispose();
            _weights = null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Excepción al liberar pesos de llama.cpp.");
        }

        _inferenceLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
