using System.Buffers;
using System.IO;
using DictaMeeting.Diarization.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DictaMeeting.Diarization.Pipeline;

/// <summary>
/// Pipeline completo de diarización PyAnnote Community-1 ejecutado localmente con ONNX Runtime.
/// Implementa fielmente la arquitectura de pyannote.audio 4.x:
/// Segmentación (10s/1s hop) + Kaldi FBank 80-mel + Enmascaramiento local + Embeddings 256-D +
/// Proyección PLDA + AHC + VBx + Algoritmo Húngaro + Reconstrucción Exclusiva.
/// </summary>
public sealed class Community1DiarizationPipeline : IDisposable
{
    public const int SampleRate = 16000;
    public const double ChunkDurationSec = 10.0;
    public const double ChunkStepSec = 1.0;
    public const int ChunkSamples = 160000; // 10s * 16kHz
    public const int ChunkStepSamples = 16000; // 1s * 16kHz
    public const int NumLocalSpeakers = 3;
    public const int NumSegmentationFrames = 589;
    public const double FrameStepSec = 0.016875;
    public const double FrameDurationSec = 0.0619375;
    public const int FbankFramesPerChunk = 998;
    public const int EmbeddingDimension = 256;

    private readonly InferenceSession _segmentationSession;
    private readonly InferenceSession _embeddingSession;
    private readonly PldaTransformer _pldaTransformer;
    private readonly KaldiFbankExtractor _fbankExtractor;
    private readonly SpeakerSmoothingFilter _smoothingFilter;
    private readonly ILogger? _logger;
    private readonly string _executionProvider;
    private bool _disposed;

    public string ExecutionProvider => _executionProvider;

    public Community1DiarizationPipeline(
        string segmentationModelPath,
        string embeddingModelPath,
        string pldaPath,
        string? preferredProvider = null,
        ILogger? logger = null)
    {
        _logger = logger;
        _fbankExtractor = new KaldiFbankExtractor();
        _smoothingFilter = new SpeakerSmoothingFilter();

        if (!File.Exists(segmentationModelPath))
        {
            throw new FileNotFoundException($"No se encontró el modelo de segmentación en: {segmentationModelPath}");
        }
        if (!File.Exists(embeddingModelPath))
        {
            throw new FileNotFoundException($"No se encontró el modelo de embeddings en: {embeddingModelPath}");
        }
        if (!File.Exists(pldaPath))
        {
            throw new FileNotFoundException($"No se encontró el archivo de parámetros PLDA en: {pldaPath}");
        }

        _pldaTransformer = PldaTransformer.LoadFromFile(pldaPath);

        // Configuración de proveedores de ONNX Runtime con detección inteligente y fallback a CPU
        int threadCount = Math.Clamp(Environment.ProcessorCount / 4, 2, 8);
        var segOptions = new SessionOptions
        {
            IntraOpNumThreads = threadCount,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };
        var embOptions = new SessionOptions
        {
            IntraOpNumThreads = threadCount,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };

        string providerUsed = "CPUExecutionProvider";
        bool tryGpu = string.Equals(preferredProvider, "CUDA", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(preferredProvider, "GPU", StringComparison.OrdinalIgnoreCase);

        if (tryGpu)
        {
            try
            {
                segOptions.AppendExecutionProvider_CUDA(0);
                embOptions.AppendExecutionProvider_CUDA(0);
                providerUsed = "CUDAExecutionProvider";
                _logger?.LogInformation("PyAnnote Community-1 configurado con aceleración GPU CUDA.");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "No se pudo inicializar CUDAExecutionProvider para Diarización. Conmutando a CPU.");
                providerUsed = "CPUExecutionProvider";
            }
        }

        _executionProvider = providerUsed;

        try
        {
            _segmentationSession = new InferenceSession(segmentationModelPath, segOptions);
            _embeddingSession = new InferenceSession(embeddingModelPath, embOptions);
        }
        catch (Exception ex) when (providerUsed != "CPUExecutionProvider")
        {
            _logger?.LogWarning(ex, "Fallo al crear sesión ONNX con proveedor {Provider}. Reintentando con CPU puro.", providerUsed);
            segOptions = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            embOptions = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            _segmentationSession = new InferenceSession(segmentationModelPath, segOptions);
            _embeddingSession = new InferenceSession(embeddingModelPath, embOptions);
            _executionProvider = "CPUExecutionProvider";
        }
    }

    /// <summary>
    /// Ejecuta el pipeline completo de diarización sobre el buffer de audio a 16 kHz.
    /// </summary>
    public DiarizationResult ProcessAudio(
        float[] samples,
        DiarizationOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var opt = options ?? new DiarizationOptions();
        int totalSamples = samples.Length;
        double audioDurationSec = (double)totalSamples / SampleRate;

        if (totalSamples == 0)
        {
            return new DiarizationResult
            {
                AudioDuration = TimeSpan.Zero,
                ExecutionTime = sw.Elapsed,
                ExecutionProvider = _executionProvider
            };
        }

        progress?.Report(0.05);
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Fragmentación en ventanas deslizantes de 10s con paso configurado
        double stepSec = opt.StepSeconds > 0 ? opt.StepSeconds : ChunkStepSec;
        int stepSamples = (int)(stepSec * SampleRate);
        var chunkStarts = GenerateChunkStarts(totalSamples, stepSamples);
        int numChunks = chunkStarts.Count;

        _logger?.LogInformation("Iniciando Diarización Community-1: {Duration:F1}s de audio, {Chunks} ventanas de 10s (paso {Step:F1}s).",
            audioDurationSec, numChunks, stepSec);

        // 2. Inferencia de segmentación: [numChunks, 589, 3]
        var segSw = System.Diagnostics.Stopwatch.StartNew();
        float[,,] segmentations = RunSegmentationBatched(samples, chunkStarts, opt.BatchSize, progress, cancellationToken);
        segSw.Stop();
        progress?.Report(0.40);
        cancellationToken.ThrowIfCancellationRequested();

        // 3. Extracción de Kaldi FBank y embeddings con enmascaramiento local
        // Cada chunk produce hasta 3 embeddings (uno por hablante local activo)
        var embExtractResult = ExtractChunkEmbeddings(samples, chunkStarts, segmentations, opt.BatchSize, progress, cancellationToken);
        var embeddings = embExtractResult.Embeddings;
        var activeMask = embExtractResult.ActiveMask;
        progress?.Report(0.70);
        cancellationToken.ThrowIfCancellationRequested();

        // 4. Filtrar embeddings activos para clustering (hablantes con habla limpia suficiente)
        var trainEmbeddings = new List<float[]>();
        var trainChunkIndices = new List<int>();
        var trainSpeakerIndices = new List<int>();

        for (int c = 0; c < numChunks; c++)
        {
            for (int s = 0; s < NumLocalSpeakers; s++)
            {
                if (activeMask[c, s])
                {
                    float[] vec = new float[EmbeddingDimension];
                    for (int d = 0; d < EmbeddingDimension; d++)
                    {
                        vec[d] = embeddings[c, s, d];
                    }
                    trainEmbeddings.Add(vec);
                    trainChunkIndices.Add(c);
                    trainSpeakerIndices.Add(s);
                }
            }
        }

        _logger?.LogInformation("Embeddings activos filtrados para clustering: {Count} de {Total}.",
            trainEmbeddings.Count, numChunks * NumLocalSpeakers);

        // 5. Clustering PLDA + AHC + VBx
        double[][] globalCentroids;
        int[,] chunkSpeakerAssignments; // [numChunks, NumLocalSpeakers] -> globalClusterIndex (-1 si inactivo)
        TimeSpan ahcTime = TimeSpan.Zero;
        TimeSpan vbxTime = TimeSpan.Zero;
        TimeSpan assignmentTime = TimeSpan.Zero;

        if (trainEmbeddings.Count < 2)
        {
            // Caso trivial: 1 único interlocutor o audio marginal
            globalCentroids = trainEmbeddings.Count == 1
                ? new[] { trainEmbeddings[0].Select(x => (double)x).ToArray() }
                : new[] { new double[EmbeddingDimension] };

            chunkSpeakerAssignments = new int[numChunks, NumLocalSpeakers];
            for (int c = 0; c < numChunks; c++)
            {
                for (int s = 0; s < NumLocalSpeakers; s++)
                {
                    chunkSpeakerAssignments[c, s] = 0;
                }
            }
        }
        else
        {
            float[][] trainArray = trainEmbeddings.ToArray();

            // A. Normalización L2 para AHC
            double[][] trainNorm = new double[trainArray.Length][];
            for (int i = 0; i < trainArray.Length; i++)
            {
                trainNorm[i] = new double[EmbeddingDimension];
                double norm = 0.0;
                for (int d = 0; d < EmbeddingDimension; d++) norm += trainArray[i][d] * trainArray[i][d];
                norm = Math.Sqrt(Math.Max(norm, 1e-12));
                for (int d = 0; d < EmbeddingDimension; d++) trainNorm[i][d] = trainArray[i][d] / norm;
            }

            // B. Inicialización con AHC (corte a 0.6)
            var ahcSw = System.Diagnostics.Stopwatch.StartNew();
            int[] ahcLabels = AhcClusterer.Cluster(trainNorm, threshold: 0.6);
            ahcSw.Stop();

            // C. Transformación PLDA a 128 dimensiones
            var vbxSw = System.Diagnostics.Stopwatch.StartNew();
            double[,] trainPlda = _pldaTransformer.TransformBatch(trainArray);

            // D. VBx Clustering
            var vbxResult = VbxClusterer.Cluster(trainPlda, trainArray, _pldaTransformer.Phi, ahcLabels);
            globalCentroids = vbxResult.Centroids;

            int detectedK = globalCentroids.Length;
            int forcedK = opt.NumSpeakers ?? (detectedK < opt.MinSpeakers ? opt.MinSpeakers.Value : (detectedK > opt.MaxSpeakers ? opt.MaxSpeakers.Value : detectedK));

            if (forcedK != detectedK && forcedK > 0)
            {
                _logger?.LogInformation("Forzando recuento de interlocutores a K={ForcedK} (detectado VBx={DetectedK}).", forcedK, detectedK);
                globalCentroids = VbxClusterer.KMeansCluster(trainArray, forcedK);
            }
            vbxSw.Stop();

            // E. Asignación óptima de cada interlocutor local de cada chunk al centroide global con el algoritmo Húngaro
            var assignSw = System.Diagnostics.Stopwatch.StartNew();
            chunkSpeakerAssignments = AssignChunksToGlobalCentroids(embeddings, segmentations, globalCentroids);
            assignmentTime = assignSw.Elapsed;
            ahcTime = ahcSw.Elapsed;
            vbxTime = vbxSw.Elapsed;
        }

        progress?.Report(0.85);
        cancellationToken.ThrowIfCancellationRequested();

        // 6. Reconstrucción temporal de pistas y Diarización Exclusiva
        var reconstructSw = System.Diagnostics.Stopwatch.StartNew();
        var (regularSpans, exclusiveSpans) = ReconstructDiarizationSpans(
            segmentations,
            chunkSpeakerAssignments,
            globalCentroids.Length,
            totalSamples,
            chunkStarts);

        // 7. Suavizado temporal de los segmentos exclusivos
        var smoothedExclusive = _smoothingFilter.Smooth(exclusiveSpans, opt.MinSegmentDuration, opt.MergeGap);
        reconstructSw.Stop();
        assignmentTime += reconstructSw.Elapsed;

        sw.Stop();
        _logger?.LogInformation("Diarización completada en {Elapsed:F2}s: {Speakers} interlocutores, {Regular} segmentos estándar, {Exclusive} exclusivos.",
            sw.Elapsed.TotalSeconds, globalCentroids.Length, regularSpans.Count, smoothedExclusive.Count);

        var centroidsDict = new Dictionary<string, float[]>();
        for (int k = 0; k < globalCentroids.Length; k++)
        {
            string id = SpeakerId.FromIndex(k);
            centroidsDict[id] = globalCentroids[k].Select(x => (float)x).ToArray();
        }

        progress?.Report(1.0);

        var timings = new DiarizationTimings
        {
            AudioDuration = TimeSpan.FromSeconds(audioDurationSec),
            TotalTime = sw.Elapsed,
            SegmentationInferenceTime = segSw.Elapsed,
            FbankExtractionTime = embExtractResult.FbankTime,
            EmbeddingInferenceTime = embExtractResult.EmbeddingTime,
            ClusteringAhcTime = ahcTime,
            ClusteringVbxTime = vbxTime,
            AssignmentAndReconstructionTime = assignmentTime
        };

        _logger?.LogInformation("{Timings}", timings);

        return new DiarizationResult
        {
            RegularSegments = regularSpans,
            ExclusiveSegments = smoothedExclusive,
            SpeakerCentroids = centroidsDict,
            DetectedSpeakerCount = globalCentroids.Length,
            AudioDuration = TimeSpan.FromSeconds(audioDurationSec),
            ExecutionTime = sw.Elapsed,
            ExecutionProvider = _executionProvider,
            Timings = timings
        };
    }

    private static List<int> GenerateChunkStarts(int totalSamples, int stepSamples)
    {
        var starts = new List<int>();
        int current = 0;

        while (current + ChunkSamples <= totalSamples)
        {
            starts.Add(current);
            current += stepSamples;
        }

        if (starts.Count == 0 || (totalSamples > ChunkSamples && (totalSamples - ChunkSamples) % stepSamples != 0))
        {
            if (starts.Count == 0 || starts[^1] != current)
            {
                starts.Add(current);
            }
        }

        return starts;
    }

    private float[,,] RunSegmentationBatched(
        float[] waveform,
        List<int> chunkStarts,
        int batchSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        int numChunks = chunkStarts.Count;
        float[,,] result = new float[numChunks, NumSegmentationFrames, NumLocalSpeakers];

        for (int i = 0; i < numChunks; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int currentBatch = Math.Min(batchSize, numChunks - i);

            var batchTensor = new DenseTensor<float>(new[] { currentBatch, 1, ChunkSamples });

            for (int b = 0; b < currentBatch; b++)
            {
                int start = chunkStarts[i + b];
                int copyLen = Math.Min(ChunkSamples, waveform.Length - start);

                for (int s = 0; s < copyLen; s++)
                {
                    batchTensor[b, 0, s] = waveform[start + s];
                }
            }

            var inputs = new[] { NamedOnnxValue.CreateFromTensor("waveforms", batchTensor) };
            using var outputs = _segmentationSession.Run(inputs);
            var outputTensor = outputs[0].AsTensor<float>();

            for (int b = 0; b < currentBatch; b++)
            {
                int chunkIdx = i + b;
                for (int f = 0; f < NumSegmentationFrames; f++)
                {
                    for (int s = 0; s < NumLocalSpeakers; s++)
                    {
                        result[chunkIdx, f, s] = outputTensor[b, f, s];
                    }
                }
            }

            double p = 0.05 + 0.35 * ((double)(i + currentBatch) / numChunks);
            progress?.Report(p);
        }

        return result;
    }

    private (float[,,] Embeddings, bool[,] ActiveMask, TimeSpan FbankTime, TimeSpan EmbeddingTime) ExtractChunkEmbeddings(
        float[] waveform,
        List<int> chunkStarts,
        float[,,] segmentations,
        int batchSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        int numChunks = chunkStarts.Count;
        float[,,] embeddings = new float[numChunks, NumLocalSpeakers, EmbeddingDimension];
        bool[,] activeMask = new bool[numChunks, NumLocalSpeakers];

        // 1. Extraer FBank globalmente sobre toda la forma de onda en paralelo y reutilizar frames con CMVN
        var fbankSw = System.Diagnostics.Stopwatch.StartNew();
        float[,] globalSpectrogram = _fbankExtractor.ComputeRawLogMelSpectrogram(waveform);
        float[][,] chunkFbanks = new float[numChunks][,];

        Parallel.For(0, numChunks, c =>
        {
            int startSample = chunkStarts[c];
            int startFrame = startSample / KaldiFbankExtractor.FrameShiftSamples;
            chunkFbanks[c] = KaldiFbankExtractor.SliceAndApplyCmvn(globalSpectrogram, startFrame, FbankFramesPerChunk);
        });
        fbankSw.Stop();

        // 2. Construir máscaras de enmascaramiento local de interlocutores
        var candidateChunks = new List<(int ChunkIdx, int SpkIdx)>();

        for (int c = 0; c < numChunks; c++)
        {
            for (int s = 0; s < NumLocalSpeakers; s++)
            {
                float activeFramesSum = 0.0f;
                float cleanFramesSum = 0.0f;
                float totalSpkEnergy = 0.0f;

                for (int f = 0; f < NumSegmentationFrames; f++)
                {
                    float spkVal = segmentations[c, f, s];
                    totalSpkEnergy += spkVal;
                    float totalFrameActive = 0.0f;
                    for (int other = 0; other < NumLocalSpeakers; other++) totalFrameActive += segmentations[c, f, other];

                    if (spkVal > 0.5f)
                    {
                        activeFramesSum += 1.0f;
                        if (totalFrameActive < 1.5f) // Habla limpia sin solapamiento
                        {
                            cleanFramesSum += 1.0f;
                        }
                    }
                }

                // Activo para clustering si tiene al menos el 20% de frames activos limpios
                if (cleanFramesSum >= 0.20f * NumSegmentationFrames)
                {
                    activeMask[c, s] = true;
                }

                // Sólo calcular embedding si hay actividad de voz medible (al menos 3 frames activos o candidato limpio)
                if (activeFramesSum >= 3.0f || activeMask[c, s])
                {
                    candidateChunks.Add((c, s));
                }
            }
        }

        // 3. Inferencia de embeddings por lotes
        var embSw = System.Diagnostics.Stopwatch.StartNew();
        int effectiveBatchSize = Math.Max(batchSize, 32);
        for (int i = 0; i < candidateChunks.Count; i += effectiveBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int currentBatch = Math.Min(effectiveBatchSize, candidateChunks.Count - i);

            var fbankTensor = new DenseTensor<float>(new[] { currentBatch, FbankFramesPerChunk, KaldiFbankExtractor.NumMelBins });
            var maskTensor = new DenseTensor<float>(new[] { currentBatch, NumSegmentationFrames });

            for (int b = 0; b < currentBatch; b++)
            {
                var (c, s) = candidateChunks[i + b];
                var fbank = chunkFbanks[c];
                int framesToCopy = Math.Min(FbankFramesPerChunk, fbank.GetLength(0));

                for (int f = 0; f < framesToCopy; f++)
                {
                    for (int m = 0; m < KaldiFbankExtractor.NumMelBins; m++)
                    {
                        fbankTensor[b, f, m] = fbank[f, m];
                    }
                }

                // Rellenar máscara local
                for (int f = 0; f < NumSegmentationFrames; f++)
                {
                    maskTensor[b, f] = segmentations[c, f, s];
                }
            }

            var inputs = new[]
            {
                NamedOnnxValue.CreateFromTensor("fbank", fbankTensor),
                NamedOnnxValue.CreateFromTensor("weights", maskTensor)
            };

            using var outputs = _embeddingSession.Run(inputs);
            var embTensor = outputs[0].AsTensor<float>();

            for (int b = 0; b < currentBatch; b++)
            {
                var (c, s) = candidateChunks[i + b];
                for (int d = 0; d < EmbeddingDimension; d++)
                {
                    embeddings[c, s, d] = embTensor[b, d];
                }
            }

            double p = 0.40 + 0.30 * ((double)(i + currentBatch) / candidateChunks.Count);
            progress?.Report(p);
        }
        embSw.Stop();

        return (embeddings, activeMask, fbankSw.Elapsed, embSw.Elapsed);
    }

    private int[,] AssignChunksToGlobalCentroids(
        float[,,] embeddings,
        float[,,] segmentations,
        double[][] globalCentroids)
    {
        int numChunks = embeddings.GetLength(0);
        int numCentroids = globalCentroids.Length;
        int[,] assignments = new int[numChunks, NumLocalSpeakers];

        for (int c = 0; c < numChunks; c++)
        {
            double[,] costMatrix = new double[NumLocalSpeakers, numCentroids];

            for (int s = 0; s < NumLocalSpeakers; s++)
            {
                float spkEnergy = 0.0f;
                for (int f = 0; f < NumSegmentationFrames; f++) spkEnergy += segmentations[c, f, s];

                if (spkEnergy <= 0.05f)
                {
                    for (int k = 0; k < numCentroids; k++) costMatrix[s, k] = -100.0;
                    continue;
                }

                // Coseno entre embedding[c, s] y cada globalCentroid[k]
                double embNorm = 0.0;
                for (int d = 0; d < EmbeddingDimension; d++) embNorm += embeddings[c, s, d] * embeddings[c, s, d];
                embNorm = Math.Sqrt(Math.Max(embNorm, 1e-12));

                for (int k = 0; k < numCentroids; k++)
                {
                    double dot = 0.0;
                    double centNorm = 0.0;
                    for (int d = 0; d < EmbeddingDimension; d++)
                    {
                        dot += embeddings[c, s, d] * globalCentroids[k][d];
                        centNorm += globalCentroids[k][d] * globalCentroids[k][d];
                    }
                    centNorm = Math.Sqrt(Math.Max(centNorm, 1e-12));

                    double cosSim = dot / (embNorm * centNorm);
                    // Puntuación = 2.0 - distancia_coseno = 1.0 + cosSim
                    costMatrix[s, k] = 1.0 + cosSim;
                }
            }

            int[] hungarianResult = HungarianSolver.Maximize(costMatrix);
            for (int s = 0; s < NumLocalSpeakers; s++)
            {
                assignments[c, s] = hungarianResult[s];
            }
        }

        return assignments;
    }

    private (List<SpeakerSegment> Regular, List<ExclusiveSpeakerSegment> Exclusive) ReconstructDiarizationSpans(
        float[,,] segmentations,
        int[,] chunkSpeakerAssignments,
        int numGlobalSpeakers,
        int totalSamples,
        List<int> chunkStarts)
    {
        double audioDurationSec = (double)totalSamples / SampleRate;
        int totalFrames = (int)Math.Ceiling(audioDurationSec / FrameStepSec) + 1;

        // Matrices de acumulación en la línea de tiempo global
        float[,] globalActivations = new float[totalFrames, numGlobalSpeakers];
        float[] frameCountWeight = new float[totalFrames];
        float[] estimatedSpeakerCounts = new float[totalFrames];

        int numChunks = segmentations.GetLength(0);

        for (int c = 0; c < numChunks; c++)
        {
            double chunkStartSec = chunkStarts[c] / (double)SampleRate;
            int startFrame = (int)Math.Round(chunkStartSec / FrameStepSec);

            for (int f = 0; f < NumSegmentationFrames; f++)
            {
                int globalF = startFrame + f;
                if (globalF >= totalFrames) break;

                frameCountWeight[globalF] += 1.0f;
                float chunkFrameCount = 0.0f;

                for (int s = 0; s < NumLocalSpeakers; s++)
                {
                    float act = segmentations[c, f, s];
                    chunkFrameCount += act;

                    int globalK = chunkSpeakerAssignments[c, s];
                    if (globalK >= 0 && globalK < numGlobalSpeakers)
                    {
                        globalActivations[globalF, globalK] += act;
                    }
                }

                estimatedSpeakerCounts[globalF] += chunkFrameCount;
            }
        }

        // Normalizar activaciones por solapamiento de ventanas
        for (int f = 0; f < totalFrames; f++)
        {
            float weight = Math.Max(frameCountWeight[f], 1.0f);
            estimatedSpeakerCounts[f] = MathF.Round(estimatedSpeakerCounts[f] / weight);

            for (int k = 0; k < numGlobalSpeakers; k++)
            {
                globalActivations[f, k] /= weight;
            }
        }

        // A. Pistas regulares (permite solapamiento)
        var regularSpans = new List<SpeakerSegment>();
        for (int k = 0; k < numGlobalSpeakers; k++)
        {
            string speakerId = SpeakerId.FromIndex(k);
            bool inSegment = false;
            int segStartFrame = 0;

            for (int f = 0; f < totalFrames; f++)
            {
                bool isActive = globalActivations[f, k] >= 0.5f;

                if (!inSegment && isActive)
                {
                    inSegment = true;
                    segStartFrame = f;
                }
                else if (inSegment && !isActive)
                {
                    inSegment = false;
                    regularSpans.Add(new SpeakerSegment
                    {
                        StartTime = TimeSpan.FromSeconds(segStartFrame * FrameStepSec),
                        EndTime = TimeSpan.FromSeconds(f * FrameStepSec),
                        SpeakerId = speakerId,
                        Confidence = 0.95f
                    });
                }
            }

            if (inSegment)
            {
                regularSpans.Add(new SpeakerSegment
                {
                    StartTime = TimeSpan.FromSeconds(segStartFrame * FrameStepSec),
                    EndTime = TimeSpan.FromSeconds(totalFrames * FrameStepSec),
                    SpeakerId = speakerId,
                    Confidence = 0.95f
                });
            }
        }

        // B. Pistas exclusivas (conteo instantáneo máximo = 1, sin solapamiento)
        var exclusiveSpans = new List<ExclusiveSpeakerSegment>();
        int? currentExclusiveSpeaker = null;
        int exclusiveStartFrame = 0;

        for (int f = 0; f < totalFrames; f++)
        {
            int? bestSpeakerThisFrame = null;

            // Si hay actividad de voz estimada en este frame
            if (estimatedSpeakerCounts[f] >= 1.0f)
            {
                float maxActivation = 0.35f; // Umbral de presencia mínima
                for (int k = 0; k < numGlobalSpeakers; k++)
                {
                    if (globalActivations[f, k] > maxActivation)
                    {
                        maxActivation = globalActivations[f, k];
                        bestSpeakerThisFrame = k;
                    }
                }
            }

            if (bestSpeakerThisFrame != currentExclusiveSpeaker)
            {
                if (currentExclusiveSpeaker.HasValue)
                {
                    exclusiveSpans.Add(new ExclusiveSpeakerSegment
                    {
                        StartTime = TimeSpan.FromSeconds(exclusiveStartFrame * FrameStepSec),
                        EndTime = TimeSpan.FromSeconds(f * FrameStepSec),
                        SpeakerId = SpeakerId.FromIndex(currentExclusiveSpeaker.Value)
                    });
                }

                currentExclusiveSpeaker = bestSpeakerThisFrame;
                exclusiveStartFrame = f;
            }
        }

        if (currentExclusiveSpeaker.HasValue)
        {
            exclusiveSpans.Add(new ExclusiveSpeakerSegment
            {
                StartTime = TimeSpan.FromSeconds(exclusiveStartFrame * FrameStepSec),
                EndTime = TimeSpan.FromSeconds(totalFrames * FrameStepSec),
                SpeakerId = SpeakerId.FromIndex(currentExclusiveSpeaker.Value)
            });
        }

        regularSpans.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        exclusiveSpans.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));

        return (regularSpans, exclusiveSpans);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _segmentationSession.Dispose();
        _embeddingSession.Dispose();
    }
}
