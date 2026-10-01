using System.IO;

namespace DictaMeeting.Diarization.Pipeline;

/// <summary>
/// Transformador PLDA (Probabilistic Linear Discriminant Analysis) y proyección LDA
/// para embeddings x-vector de 256 dimensiones en PyAnnote Community-1.
/// Transforma embeddings brutos a un subespacio latente de 128 dimensiones desacoplado
/// para el posterior clustering con VBx.
/// </summary>
public sealed class PldaTransformer
{
    public const int InputDimension = 256;
    public const int LdaDimension = 128;

    private readonly double[] _mean1;     // 256
    private readonly float[] _mean2;      // 128
    private readonly float[,] _lda;       // 256 x 128
    private readonly double[] _pldaMu;    // 128
    private readonly double[,] _transform; // 128 x 128
    private readonly double[] _phi;       // 128

    public IReadOnlyList<double> Phi => _phi;

    private PldaTransformer(
        double[] mean1,
        float[] mean2,
        float[,] lda,
        double[] pldaMu,
        double[,] transform,
        double[] phi)
    {
        _mean1 = mean1;
        _mean2 = mean2;
        _lda = lda;
        _pldaMu = pldaMu;
        _transform = transform;
        _phi = phi;
    }

    /// <summary>
    /// Carga el modelo PLDA desde un archivo binario empaquetado (plda_community1.bin).
    /// </summary>
    public static PldaTransformer LoadFromFile(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return LoadFromStream(stream);
    }

    /// <summary>
    /// Carga el modelo PLDA desde un Stream.
    /// </summary>
    public static PldaTransformer LoadFromStream(Stream stream)
    {
        using var reader = new BinaryReader(stream);

        byte[] magic = reader.ReadBytes(8);
        string magicStr = System.Text.Encoding.ASCII.GetString(magic).TrimEnd('\0');
        if (magicStr != "PLDA2.0")
        {
            throw new InvalidDataException($"Formato PLDA no reconocido: {magicStr}. Se esperaba PLDA2.0.");
        }

        double[] mean1 = new double[InputDimension];
        for (int i = 0; i < InputDimension; i++) mean1[i] = reader.ReadDouble();

        float[] mean2 = new float[LdaDimension];
        for (int i = 0; i < LdaDimension; i++) mean2[i] = reader.ReadSingle();

        float[,] lda = new float[InputDimension, LdaDimension];
        for (int i = 0; i < InputDimension; i++)
        {
            for (int j = 0; j < LdaDimension; j++)
            {
                lda[i, j] = reader.ReadSingle();
            }
        }

        double[] mu = new double[LdaDimension];
        for (int i = 0; i < LdaDimension; i++) mu[i] = reader.ReadDouble();

        double[,] transform = new double[LdaDimension, LdaDimension];
        for (int i = 0; i < LdaDimension; i++)
        {
            for (int j = 0; j < LdaDimension; j++)
            {
                transform[i, j] = reader.ReadDouble();
            }
        }

        double[] phi = new double[LdaDimension];
        for (int i = 0; i < LdaDimension; i++) phi[i] = reader.ReadDouble();

        return new PldaTransformer(mean1, mean2, lda, mu, transform, phi);
    }

    /// <summary>
    /// Proyecta un embedding acústico de 256 dimensiones al espacio latente PLDA de 128 dimensiones.
    /// </summary>
    public double[] Transform(ReadOnlySpan<float> embedding)
    {
        if (embedding.Length != InputDimension)
        {
            throw new ArgumentException($"El embedding debe tener {InputDimension} dimensiones.", nameof(embedding));
        }

        // 1. diff1 = embedding - mean1
        Span<double> diff1 = stackalloc double[InputDimension];
        double sumSq1 = 0.0;
        for (int i = 0; i < InputDimension; i++)
        {
            double d = embedding[i] - _mean1[i];
            diff1[i] = d;
            sumSq1 += d * d;
        }

        // 2. norm1 = diff1 / ||diff1||
        double norm1 = Math.Sqrt(Math.Max(sumSq1, 1e-12));
        double sqrtDim1 = Math.Sqrt(InputDimension); // sqrt(256) = 16.0

        // 3. step1 = lda.T * (norm1 * sqrt(256)) - mean2
        Span<double> step1 = stackalloc double[LdaDimension];
        double sumSq2 = 0.0;

        for (int j = 0; j < LdaDimension; j++)
        {
            double dot = 0.0;
            for (int i = 0; i < InputDimension; i++)
            {
                dot += (diff1[i] / norm1 * sqrtDim1) * _lda[i, j];
            }
            double s = dot - _mean2[j];
            step1[j] = s;
            sumSq2 += s * s;
        }

        // 4. norm2 = step1 / ||step1|| * sqrt(128)
        double norm2 = Math.Sqrt(Math.Max(sumSq2, 1e-12));
        double sqrtDim2 = Math.Sqrt(LdaDimension);

        Span<double> z = stackalloc double[LdaDimension];
        for (int j = 0; j < LdaDimension; j++)
        {
            z[j] = (step1[j] / norm2 * sqrtDim2) - _pldaMu[j];
        }

        // 5. out = z * transform.T
        double[] output = new double[LdaDimension];
        for (int i = 0; i < LdaDimension; i++)
        {
            double dot = 0.0;
            for (int j = 0; j < LdaDimension; j++)
            {
                dot += z[j] * _transform[i, j];
            }
            output[i] = dot;
        }

        return output;
    }

    /// <summary>
    /// Transforma por lotes una matriz de embeddings N x 256.
    /// Retorna una matriz double[N, 128].
    /// </summary>
    public double[,] TransformBatch(float[][] embeddings)
    {
        int n = embeddings.Length;
        double[,] result = new double[n, LdaDimension];

        for (int i = 0; i < n; i++)
        {
            double[] transformed = Transform(embeddings[i]);
            for (int j = 0; j < LdaDimension; j++)
            {
                result[i, j] = transformed[j];
            }
        }

        return result;
    }
}
