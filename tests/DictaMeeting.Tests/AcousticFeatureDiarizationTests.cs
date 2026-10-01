using DictaMeeting.Diarization.Models;
using DictaMeeting.Diarization.Pipeline;
using DictaMeeting.Diarization.Services;
using Xunit;

namespace DictaMeeting.Tests;

public class PyAnnoteCommunity1DiarizationTests
{
    [Fact]
    public void KaldiFbankExtractor_ComputesExpectedDimensions()
    {
        // 10 segundos de audio a 16 kHz = 160000 muestras
        var extractor = new KaldiFbankExtractor();
        float[] audio = new float[160000];

        // Rellenar con señal senoidal suave
        for (int i = 0; i < audio.Length; i++)
        {
            audio[i] = 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / 16000f);
        }

        float[,] fbank = extractor.ComputeFbank(audio);

        // Frame shift = 10ms (160 samples), frame length = 25ms (400 samples)
        // (160000 - 400) / 160 + 1 = 998 frames
        Assert.Equal(998, fbank.GetLength(0));
        Assert.Equal(80, fbank.GetLength(1));

        // Verificar que no hay NaN ni Infinitos
        for (int f = 0; f < fbank.GetLength(0); f++)
        {
            for (int b = 0; b < 80; b++)
            {
                Assert.False(float.IsNaN(fbank[f, b]));
                Assert.False(float.IsInfinity(fbank[f, b]));
            }
        }
    }

    [Fact]
    public void KaldiFbankExtractor_Silence_ProducesValidCmvnNormalizedFeatures()
    {
        var extractor = new KaldiFbankExtractor();
        float[] silence = new float[32000]; // 2 segundos de silencio

        float[,] fbank = extractor.ComputeFbank(silence);

        Assert.Equal(198, fbank.GetLength(0));
        Assert.Equal(80, fbank.GetLength(1));

        // Con CMVN, la media de cada canal debe ser aproximadamente 0.0
        for (int b = 0; b < 80; b++)
        {
            double sum = 0.0;
            for (int f = 0; f < fbank.GetLength(0); f++)
            {
                sum += fbank[f, b];
            }
            double mean = sum / fbank.GetLength(0);
            Assert.True(Math.Abs(mean) < 0.01, $"Canal {b} CMVN mean debería ser ~0 pero fue {mean}");
        }
    }

    [Fact]
    public void AhcClusterer_GroupsDistinctClustersAccurately()
    {
        // Crear 4 vectores: 2 cercanos entre sí (grupo 0) y 2 cercanos entre sí (grupo 1)
        double[][] embeddings = new double[][]
        {
            new double[] { 1.0, 0.0, 0.0 },
            new double[] { 0.99, 0.01, 0.0 },
            new double[] { 0.0, 1.0, 0.0 },
            new double[] { 0.01, 0.99, 0.0 }
        };

        int[] labels = AhcClusterer.Cluster(embeddings, threshold: 0.6);

        Assert.Equal(4, labels.Length);
        Assert.Equal(labels[0], labels[1]);
        Assert.Equal(labels[2], labels[3]);
        Assert.NotEqual(labels[0], labels[2]);
    }

    [Fact]
    public void HungarianSolver_SolvesSquareMatrixOptimally()
    {
        // Matriz de coste 3x3 para maximización
        double[,] cost = new double[,]
        {
            { 10, 2, 3 },
            { 5, 15, 2 },
            { 1, 4, 20 }
        };

        int[] assignment = HungarianSolver.Maximize(cost);

        Assert.Equal(3, assignment.Length);
        Assert.Equal(0, assignment[0]); // fila 0 -> columna 0 (valor 10)
        Assert.Equal(1, assignment[1]); // fila 1 -> columna 1 (valor 15)
        Assert.Equal(2, assignment[2]); // fila 2 -> columna 2 (valor 20)
    }

    [Fact]
    public void SpeakerSmoothingFilter_FiltersMicroSegments_AndMergesContiguousGaps()
    {
        var filter = new SpeakerSmoothingFilter();

        var input = new List<ExclusiveSpeakerSegment>
        {
            // Segmento válido de 2s
            new() { StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(2), SpeakerId = "SPEAKER_00" },
            // Micro-glitch de 0.1s de otro hablante (debe ser filtrado si minDuration = 0.3s)
            new() { StartTime = TimeSpan.FromSeconds(2.05), EndTime = TimeSpan.FromSeconds(2.15), SpeakerId = "SPEAKER_01" },
            // Mismo hablante de nuevo con pausa de 0.2s (debe fusionarse si mergeGap = 0.5s)
            new() { StartTime = TimeSpan.FromSeconds(2.35), EndTime = TimeSpan.FromSeconds(5), SpeakerId = "SPEAKER_00" },
            // Hablante 2 válido
            new() { StartTime = TimeSpan.FromSeconds(6), EndTime = TimeSpan.FromSeconds(9), SpeakerId = "SPEAKER_01" }
        };

        var smoothed = filter.Smooth(input, minSegmentDuration: TimeSpan.FromSeconds(0.3), mergeGap: TimeSpan.FromSeconds(0.5));

        // Debe haber filtrado el micro-segmento de SPEAKER_01 y fusionado los dos de SPEAKER_00
        Assert.Equal(2, smoothed.Count);
        Assert.Equal("SPEAKER_00", smoothed[0].SpeakerId);
        Assert.Equal(TimeSpan.Zero, smoothed[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(5), smoothed[0].EndTime);

        Assert.Equal("SPEAKER_01", smoothed[1].SpeakerId);
        Assert.Equal(TimeSpan.FromSeconds(6), smoothed[1].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(9), smoothed[1].EndTime);
    }

    [Fact]
    public void PldaTransformer_InitializesAndTransformsBatch()
    {
        string pldaPath = Path.Combine(AppContext.BaseDirectory, "models", "diarization", "plda_community1.bin");
        if (!File.Exists(pldaPath))
        {
            // Si corre en directorio de test sin copia directa, buscar en carpeta raíz
            pldaPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models", "diarization", "plda_community1.bin"));
        }

        if (!File.Exists(pldaPath))
        {
            // Si el archivo no está presente en el entorno de pruebas, omitir
            return;
        }

        var transformer = PldaTransformer.LoadFromFile(pldaPath);
        Assert.Equal(128, PldaTransformer.LdaDimension);
        Assert.Equal(128, transformer.Phi.Count);

        // Vector 256-D de prueba
        float[][] testEmbeddings = new float[][]
        {
            new float[256],
            new float[256]
        };
        for (int i = 0; i < 256; i++)
        {
            testEmbeddings[0][i] = 0.1f * MathF.Sin(i);
            testEmbeddings[1][i] = 0.1f * MathF.Cos(i);
        }

        double[,] projected = transformer.TransformBatch(testEmbeddings);

        Assert.Equal(2, projected.GetLength(0));
        Assert.Equal(128, projected.GetLength(1));
    }

    [Fact]
    public async Task PyAnnoteCommunity1_EndToEnd_SyntheticAudio_ExecutesFullPipelineSuccessfully()
    {
        string modelsDir = Path.Combine(AppContext.BaseDirectory, "models", "diarization");
        if (!File.Exists(Path.Combine(modelsDir, "community1-segmentation.onnx")))
        {
            modelsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "models", "diarization"));
        }

        if (!File.Exists(Path.Combine(modelsDir, "community1-segmentation.onnx")) ||
            !File.Exists(Path.Combine(modelsDir, "community1-embedding.onnx")) ||
            !File.Exists(Path.Combine(modelsDir, "plda_community1.bin")))
        {
            // Omitir si los modelos no están descargados aún en la ruta
            return;
        }

        using var service = new PyAnnoteCommunity1DiarizationService(modelsDir);

        // Generar 12 segundos de audio sintético con dos frecuencias diferentes
        int sampleRate = 16000;
        int totalSamples = sampleRate * 12;
        float[] samples = new float[totalSamples];

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            if (t >= 0.5 && t < 4.5)
            {
                // Hablante A: 200 Hz
                samples[i] = 0.5f * MathF.Sin(2f * MathF.PI * 200f * (float)t) + 0.2f * MathF.Sin(2f * MathF.PI * 400f * (float)t);
            }
            else if (t >= 5.5 && t < 10.5)
            {
                // Hablante B: 450 Hz
                samples[i] = 0.5f * MathF.Sin(2f * MathF.PI * 450f * (float)t) + 0.2f * MathF.Sin(2f * MathF.PI * 900f * (float)t);
            }
            else
            {
                // Silencio
                samples[i] = 0.0f;
            }
        }

        var progress = new Progress<double>();
        var result = await service.DiarizeAsync(samples, sampleRate, options: null, progress: progress);

        Assert.NotNull(result);
        Assert.True(result.Duration.TotalSeconds >= 11.5, "La duración reportada debe corresponder a la del audio analizado.");

        // Si el modelo neuronal de segmentación detecta actividad de voz en el audio
        if (result.ExclusiveSegments.Count > 0)
        {
            Assert.True(result.DetectedSpeakerCount >= 1, "Debe detectar al menos un interlocutor.");

            // Validar propiedad de Diarización Exclusiva: NO solapamiento entre segmentos
            for (int i = 0; i < result.ExclusiveSegments.Count - 1; i++)
            {
                var curr = result.ExclusiveSegments[i];
                var next = result.ExclusiveSegments[i + 1];
                Assert.True(next.StartTime >= curr.EndTime,
                    $"Diarización Exclusiva violada: segmento {i} ({curr.EndTime:ss\\.ff}) solapa con {i + 1} ({next.StartTime:ss\\.ff})");
            }
        }
    }
}
