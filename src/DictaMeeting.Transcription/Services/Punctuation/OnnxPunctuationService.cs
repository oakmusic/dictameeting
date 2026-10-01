using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DictaMeeting.Meetings.Models;
using DictaMeeting.Transcription.Interfaces;
using DictaMeeting.Transcription.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DictaMeeting.Transcription.Services.Punctuation;

/// <summary>
/// Implementación de IPunctuationService basada en ONNX Runtime y XLM-RoBERTa-base
/// (onnx-community/punctuate-all-ONNX / kredor/punctuate-all).
/// Restaura puntuación (., ,, ?, -, :) y capitalización localmente y sin conexión,
/// procesando textos largos por ventanas deslizantes y preservando la integridad de timestamps y speakers.
/// </summary>
public sealed class OnnxPunctuationService : IPunctuationService
{
    private static readonly string[] PunctuationLabels = { "0", ".", ",", "?", "-", ":" };
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromMinutes(15) };

    private readonly PunctuationModelConfig _config;
    private readonly ILogger<OnnxPunctuationService>? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private InferenceSession? _session;
    private XlmRobertaTokenizer? _tokenizer;
    private string? _resolvedModelPath;
    private long _modelSizeBytes;
    private bool _disposed;

    public PunctuationStatistics? LastStatistics { get; private set; }

    public bool IsEnabled
    {
        get => _config.Enabled;
        set => _config.Enabled = value;
    }

    public OnnxPunctuationService(
        PunctuationModelConfig? config = null,
        ILogger<OnnxPunctuationService>? logger = null)
    {
        _config = config ?? new PunctuationModelConfig();
        _logger = logger;
    }

    public async Task<IReadOnlyList<TranscriptSegment>> RestorePunctuationAsync(
        IReadOnlyList<TranscriptSegment> segments,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || segments == null || segments.Count == 0)
        {
            return segments ?? Array.Empty<TranscriptSegment>();
        }

        await EnsureModelLoadedAsync(cancellationToken);
        Debug.Assert(_session != null && _tokenizer != null);

        var totalStopwatch = Stopwatch.StartNew();
        var inferenceStopwatch = new Stopwatch();

        // 1. Extraer palabras asociadas a su segmento de origen
        var allWords = new List<WordToken>();
        for (int segIdx = 0; segIdx < segments.Count; segIdx++)
        {
            var seg = segments[segIdx];
            if (string.IsNullOrWhiteSpace(seg.Text)) continue;

            var rawWords = seg.Text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int wIdx = 0; wIdx < rawWords.Length; wIdx++)
            {
                var rawWord = rawWords[wIdx];
                // Limpiar puntuación periférica previa sin alterar números decimales
                var cleanWord = CleanPunctuationMarkers(rawWord);
                if (string.IsNullOrEmpty(cleanWord))
                {
                    cleanWord = rawWord;
                }

                allWords.Add(new WordToken
                {
                    SegmentIndex = segIdx,
                    WordIndexInSegment = wIdx,
                    OriginalWord = rawWord,
                    CleanWord = cleanWord
                });
            }
        }

        if (allWords.Count == 0)
        {
            return segments;
        }

        int totalTokensProcessed = 0;
        int totalWindowsProcessed = 0;

        // 2. Procesamiento por ventanas deslizantes (sliding window) para textos de cualquier longitud
        int windowSize = _config.MaxWordsPerWindow;
        int overlap = allWords.Count <= windowSize ? 0 : _config.OverlapWords;
        int currentStart = 0;

        while (currentStart < allWords.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int takeCount = Math.Min(windowSize, allWords.Count - currentStart);
            var windowTokens = allWords.Skip(currentStart).Take(takeCount).ToList();
            var windowCleanWords = windowTokens.Select(w => w.CleanWord).ToList();

            // Tokenizar ventana
            var (subtokens, inputIds, attentionMask) = _tokenizer.EncodeWords(windowCleanWords);

            // Reducir dinámicamente si los subtokens superan el límite duro del modelo (500 tokens)
            while (inputIds.Length > _config.HardTokenLimit && takeCount > 20)
            {
                takeCount -= 20;
                windowTokens = allWords.Skip(currentStart).Take(takeCount).ToList();
                windowCleanWords = windowTokens.Select(w => w.CleanWord).ToList();
                (subtokens, inputIds, attentionMask) = _tokenizer.EncodeWords(windowCleanWords);
            }

            totalTokensProcessed += inputIds.Length;
            totalWindowsProcessed++;

            // Ejecución de inferencia ONNX
            var inputTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
            var maskTensor = new DenseTensor<long>(attentionMask, new[] { 1, attentionMask.Length });

            var inputs = new List<NamedOnnxValue>(2)
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor)
            };

            inferenceStopwatch.Start();
            using var results = _session.Run(inputs);
            inferenceStopwatch.Stop();

            var logits = results.First(r => r.Name == "logits").AsTensor<float>();

            // Para cada subtokén en la ventana, determinar la clase de puntuación predicha (argmax)
            // Recordar que el subtokén i corresponde a la posición i + 1 en logits (posición 0 es <s>)
            var subtokenPredictions = new List<string>(subtokens.Count);
            for (int i = 0; i < subtokens.Count; i++)
            {
                int tensorIndex = i + 1;
                int bestClass = 0;
                float bestLogit = float.NegativeInfinity;

                for (int c = 0; c < PunctuationLabels.Length; c++)
                {
                    float logit = logits[0, tensorIndex, c];
                    if (logit > bestLogit)
                    {
                        bestLogit = logit;
                        bestClass = c;
                    }
                }

                subtokenPredictions.Add(PunctuationLabels[bestClass]);
            }

            // Asignar puntuación a cada palabra de la ventana
            for (int w = 0; w < windowTokens.Count; w++)
            {
                string bestPunctuation = "0";

                for (int i = 0; i < subtokens.Count; i++)
                {
                    if (subtokens[i].WordIndex == w)
                    {
                        var p = subtokenPredictions[i];
                        if (p == "." || p == "?")
                        {
                            bestPunctuation = p; // Fin de oración tiene máxima prioridad
                            break;
                        }
                        else if (p != "0")
                        {
                            bestPunctuation = p;
                        }
                    }
                }

                windowTokens[w].PredictedPunctuation = bestPunctuation;
            }

            bool isLastWindow = (currentStart + takeCount >= allWords.Count);
            if (isLastWindow)
            {
                break;
            }

            // Desplazar ventana manteniendo solapamiento para contexto
            int stride = Math.Max(1, takeCount - overlap);
            currentStart += stride;

            progress?.Report((double)currentStart / allWords.Count);
        }

        // 3. Reconstrucción fiel de los segmentos originales aplicando capitalización y puntuación
        var resultSegments = new List<TranscriptSegment>(segments.Count);
        var wordsBySegment = allWords.GroupBy(w => w.SegmentIndex).ToDictionary(g => g.Key, g => g.ToList());

        for (int segIdx = 0; segIdx < segments.Count; segIdx++)
        {
            var originalSeg = segments[segIdx];
            if (!wordsBySegment.TryGetValue(segIdx, out var segWords) || segWords.Count == 0)
            {
                resultSegments.Add(CloneSegmentWithNewText(originalSeg, originalSeg.Text));
                continue;
            }

            var punctuatedWords = new List<string>(segWords.Count);
            bool capitalizeNext = true; // El inicio de cada segmento/intervención siempre se capitaliza

            for (int i = 0; i < segWords.Count; i++)
            {
                var wordToken = segWords[i];
                var raw = wordToken.OriginalWord;

                // Truncar cualquier puntuación previa al final de la palabra para evitar duplicados como ".." o ",,"
                var baseWord = raw.TrimEnd('.', ',', '?', '!', ':', ';', '-');
                if (string.IsNullOrEmpty(baseWord))
                {
                    baseWord = raw;
                }

                // Restaurar capitalización si corresponde, respetando mayúsculas internas existentes (ej. Kubernetes, RAM)
                string formattedWord;
                if (capitalizeNext)
                {
                    formattedWord = CapitalizeFirstLetter(baseWord);
                    capitalizeNext = false;
                }
                else
                {
                    formattedWord = baseWord;
                }

                // Añadir signo de puntuación predicho
                if (wordToken.PredictedPunctuation != "0")
                {
                    formattedWord += wordToken.PredictedPunctuation;

                    // Si finaliza la oración, la siguiente palabra debe comenzar en mayúscula
                    if (wordToken.PredictedPunctuation == "." || wordToken.PredictedPunctuation == "?")
                    {
                        capitalizeNext = true;
                    }
                }

                punctuatedWords.Add(formattedWord);
            }

            var punctuatedText = string.Join(" ", punctuatedWords);
            // Sanitización preventiva adicional: colapsar cualquier signo duplicado consecutivo (ej. ".." -> ".", ",," -> ",")
            punctuatedText = Regex.Replace(punctuatedText, @"([.,?!:;])\1+", "$1");
            punctuatedText = Regex.Replace(punctuatedText, @"([.,?!:;])(?=[.,?!:;])", "");
            resultSegments.Add(CloneSegmentWithNewText(originalSeg, punctuatedText));
        }

        totalStopwatch.Stop();

        // 4. Registrar estadísticas y telemetría
        LastStatistics = new PunctuationStatistics
        {
            ModelPath = _resolvedModelPath ?? string.Empty,
            ModelSizeBytes = _modelSizeBytes,
            TotalInferenceDuration = inferenceStopwatch.Elapsed,
            TotalDuration = totalStopwatch.Elapsed,
            TotalTokensProcessed = totalTokensProcessed,
            TotalWordsProcessed = allWords.Count,
            TotalSegmentsProcessed = segments.Count,
            TotalWindowsProcessed = totalWindowsProcessed
        };

        _logger?.LogInformation(
            "Restauración de puntuación completada: {Segments} segmentos, {Words} palabras, {Tokens} tokens en {InferenceMs:F0} ms (tiempo total {TotalMs:F0} ms).",
            segments.Count, allWords.Count, totalTokensProcessed,
            inferenceStopwatch.Elapsed.TotalMilliseconds, totalStopwatch.Elapsed.TotalMilliseconds);

        progress?.Report(1.0);
        return resultSegments;
    }

    public async Task<string> RestorePunctuationAsync(
        string text,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        var dummySegment = new TranscriptSegment
        {
            Text = text,
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(1)
        };

        var result = await RestorePunctuationAsync(new[] { dummySegment }, progress, cancellationToken);
        return result.Count > 0 ? result[0].Text : text;
    }

    public async Task EnsureModelLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_session != null && _tokenizer != null) return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_session != null && _tokenizer != null) return;

            var sw = Stopwatch.StartNew();
            var (modelPath, spModelPath) = await ResolveOrDownloadModelFilesAsync(cancellationToken);

            _resolvedModelPath = modelPath;
            _modelSizeBytes = File.Exists(modelPath) ? new FileInfo(modelPath).Length : 0L;

            var sessionOptions = new SessionOptions
            {
                IntraOpNumThreads = _config.IntraOpNumThreads,
                InterOpNumThreads = _config.InterOpNumThreads,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            _session = new InferenceSession(modelPath, sessionOptions);
            _tokenizer = new XlmRobertaTokenizer(spModelPath);
            sw.Stop();

            _logger?.LogInformation(
                "Modelo de puntuación ONNX inicializado con éxito: {Path} ({SizeMB:F1} MB) en {ElapsedMs} ms.",
                modelPath, _modelSizeBytes / (1024.0 * 1024.0), sw.ElapsedMilliseconds);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<(string ModelPath, string SpModelPath)> ResolveOrDownloadModelFilesAsync(CancellationToken cancellationToken)
    {
        string? resolvedModel = ResolveLocalPath(_config.ModelPath, "model_int8.onnx", "model.onnx");
        string? resolvedSp = ResolveLocalPath(_config.SentencePieceModelPath, "sentencepiece.bpe.model");

        if (resolvedModel != null && resolvedSp != null)
        {
            return (resolvedModel, resolvedSp);
        }

        // Descarga automática resiliente desde Hugging Face si no se encuentra localmente
        var targetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DictaMeeting", "models", "punctuation", "punctuate-all");

        Directory.CreateDirectory(targetDir);

        resolvedModel ??= Path.Combine(targetDir, "model_int8.onnx");
        resolvedSp ??= Path.Combine(targetDir, "sentencepiece.bpe.model");

        if (!File.Exists(resolvedModel))
        {
            _logger?.LogInformation("Descargando modelo ONNX cuantizado (model_int8.onnx) desde Hugging Face...");
            var url = "https://huggingface.co/onnx-community/punctuate-all-ONNX/resolve/main/onnx/model_int8.onnx";
            await DownloadFileAsync(url, resolvedModel, cancellationToken);
        }

        if (!File.Exists(resolvedSp))
        {
            _logger?.LogInformation("Descargando modelo SentencePiece (sentencepiece.bpe.model) desde Hugging Face...");
            var url = "https://huggingface.co/xlm-roberta-base/resolve/main/sentencepiece.bpe.model";
            await DownloadFileAsync(url, resolvedSp, cancellationToken);
        }

        return (resolvedModel, resolvedSp);
    }

    private static string? ResolveLocalPath(string? explicitPath, params string[] candidateFileNames)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        var baseDirs = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "models", "punctuation", "punctuate-all"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "punctuation", "punctuate-all"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictaMeeting", "models", "punctuation", "punctuate-all"),
            Path.Combine(Directory.GetCurrentDirectory(), "models", "punctuation", "punctuate-all"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models", "punctuation", "punctuate-all"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "temp")
        };

        foreach (var dir in baseDirs)
        {
            try
            {
                var fullDir = Path.GetFullPath(dir);
                if (!Directory.Exists(fullDir)) continue;

                foreach (var fileName in candidateFileNames)
                {
                    var file = Path.Combine(fullDir, fileName);
                    if (File.Exists(file) && new FileInfo(file).Length > 0)
                    {
                        return file;
                    }
                }
            }
            catch
            {
                // Ignorar paths mal formateados
            }
        }

        return null;
    }

    private static async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        var tempFile = destinationPath + ".downloading";
        try
        {
            using var response = await SharedHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await contentStream.CopyToAsync(fileStream, cancellationToken);

            fileStream.Close();
            File.Move(tempFile, destinationPath, true);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    private static string CleanPunctuationMarkers(string text)
    {
        // Eliminar signos aislados dejando puntos decimales entre números (ej. 3.14 o 10.5)
        return Regex.Replace(text, @"(?<!\d)[.,;:!?](?!\d)", "").Trim();
    }

    private static string CapitalizeFirstLetter(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        // Buscar el primer carácter alfanumérico por si hay signos de apertura como '¿' o '('
        int index = 0;
        while (index < word.Length && !char.IsLetter(word[index]))
        {
            index++;
        }

        if (index >= word.Length) return word;

        if (char.IsUpper(word[index]))
        {
            return word; // Ya está en mayúscula (o es acrónimo como RAM, API)
        }

        var chars = word.ToCharArray();
        chars[index] = char.ToUpperInvariant(chars[index]);
        return new string(chars);
    }

    private static TranscriptSegment CloneSegmentWithNewText(TranscriptSegment original, string newText)
    {
        return new TranscriptSegment
        {
            Id = original.Id,
            StartTime = original.StartTime,
            EndTime = original.EndTime,
            SpeakerId = original.SpeakerId,
            SpeakerDisplayName = original.SpeakerDisplayName,
            Text = newText,
            Confidence = original.Confidence,
            IsFinal = original.IsFinal
        };
    }

    private sealed class WordToken
    {
        public int SegmentIndex { get; set; }
        public int WordIndexInSegment { get; set; }
        public string OriginalWord { get; set; } = string.Empty;
        public string CleanWord { get; set; } = string.Empty;
        public string PredictedPunctuation { get; set; } = "0";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.Dispose();
        _tokenizer?.Dispose();
        _lock.Dispose();
    }
}
