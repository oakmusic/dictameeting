namespace DictaMeeting.Diarization.Pipeline;

/// <summary>
/// Implementación de clustering Variational Bayes x-vector (VBx) de PyAnnote Community-1.
/// Ejecuta inferencia variacional EM con modelo GMM diagonal sobre características PLDA,
/// refinando los clusters iniciales del AHC.
/// </summary>
public static class VbxClusterer
{
    public const double DefaultFa = 0.07;
    public const double DefaultFb = 0.8;
    public const int MaxIterations = 20;
    public const double ElboConvergenceThreshold = 1e-4;

    public record VbxResult(
        double[,] Responsibilities, // N x K
        double[] Priors,           // K
        double[][] Centroids);      // K x EmbeddingDim

    /// <summary>
    /// Ejecuta el algoritmo VBx completo a partir de las inicializaciones AHC.
    /// </summary>
    /// <param name="trainPldaFeatures">Características N x 128 transformadas con PLDA.</param>
    /// <param name="trainRawEmbeddings">Embeddings originales N x 256 para computar centroides.</param>
    /// <param name="phi">Vector Phi de varianzas de clase PLDA (128 dimensiones).</param>
    /// <param name="ahcClusterLabels">Etiquetas 0..K-1 generadas por AHC.</param>
    /// <param name="fa">Parámetro de escala acústica Fa (por defecto 0.07).</param>
    /// <param name="fb">Parámetro de escala de regularización Fb (por defecto 0.8).</param>
    public static VbxResult Cluster(
        double[,] trainPldaFeatures,
        float[][] trainRawEmbeddings,
        IReadOnlyList<double> phi,
        int[] ahcClusterLabels,
        double fa = DefaultFa,
        double fb = DefaultFb)
    {
        int numSamples = trainPldaFeatures.GetLength(0);
        int dim = trainPldaFeatures.GetLength(1); // 128
        int rawDim = trainRawEmbeddings[0].Length; // 256

        if (numSamples < 2)
        {
            double[][] singleCentroid = new[] { ComputeMean(trainRawEmbeddings) };
            return new VbxResult(new double[numSamples, 1], new[] { 1.0 }, singleCentroid);
        }

        // 1. Determinar número de clusters iniciales K desde AHC
        int initialK = 0;
        for (int i = 0; i < ahcClusterLabels.Length; i++)
        {
            if (ahcClusterLabels[i] + 1 > initialK)
            {
                initialK = ahcClusterLabels[i] + 1;
            }
        }
        if (initialK == 0) initialK = 1;

        // 2. Construir matriz one-hot inicial y aplicar softmax escalado x 7.0
        double[,] responsibilities = new double[numSamples, initialK];
        for (int i = 0; i < numSamples; i++)
        {
            int c = Math.Clamp(ahcClusterLabels[i], 0, initialK - 1);
            responsibilities[i, c] = 1.0;
        }

        for (int i = 0; i < numSamples; i++)
        {
            double maxVal = -double.MaxValue;
            for (int k = 0; k < initialK; k++)
            {
                double v = responsibilities[i, k] * 7.0;
                if (v > maxVal) maxVal = v;
            }

            double sumExp = 0.0;
            for (int k = 0; k < initialK; k++)
            {
                responsibilities[i, k] = Math.Exp(responsibilities[i, k] * 7.0 - maxVal);
                sumExp += responsibilities[i, k];
            }
            for (int k = 0; k < initialK; k++)
            {
                responsibilities[i, k] /= sumExp;
            }
        }

        // 3. Inicializar priors uniformes
        double[] priors = new double[initialK];
        double initialPrior = 1.0 / initialK;
        for (int k = 0; k < initialK; k++) priors[k] = initialPrior;

        // 4. Precomputar constantes cuadráticas y rho = features * sqrt(phi)
        double[] constant = new double[numSamples];
        double[,] rho = new double[numSamples, dim];
        double dimLog2Pi = dim * Math.Log(2.0 * Math.PI);

        double[] sqrtPhi = new double[dim];
        for (int d = 0; d < dim; d++) sqrtPhi[d] = Math.Sqrt(Math.Max(phi[d], 1e-12));

        for (int i = 0; i < numSamples; i++)
        {
            double sumSq = 0.0;
            for (int d = 0; d < dim; d++)
            {
                double f = trainPldaFeatures[i, d];
                sumSq += f * f;
                rho[i, d] = f * sqrtPhi[d];
            }
            constant[i] = -0.5 * (sumSq + dimLog2Pi);
        }

        // 5. Bucle de optimización variacional EM
        double? previousElbo = null;
        double[,] gamma = responsibilities;
        double[,] invPrecision = new double[initialK, dim];
        double[,] alpha = new double[initialK, dim];

        for (int iter = 0; iter < MaxIterations; iter++)
        {
            // A. gamma.sum(axis=0)
            double[] gammaSum = new double[initialK];
            for (int k = 0; k < initialK; k++)
            {
                double s = 0.0;
                for (int i = 0; i < numSamples; i++) s += gamma[i, k];
                gammaSum[k] = s;
            }

            // B. inverse_precision y alpha
            for (int k = 0; k < initialK; k++)
            {
                double factor = (fa / fb) * gammaSum[k];
                for (int d = 0; d < dim; d++)
                {
                    double ip = 1.0 / (1.0 + factor * phi[d]);
                    invPrecision[k, d] = ip;

                    double dotGammaRho = 0.0;
                    for (int i = 0; i < numSamples; i++)
                    {
                        dotGammaRho += gamma[i, k] * rho[i, d];
                    }
                    alpha[k, d] = (fa / fb) * ip * dotGammaRho;
                }
            }

            // C. log_probability
            double[,] logProb = new double[numSamples, initialK];
            for (int k = 0; k < initialK; k++)
            {
                double term2 = 0.0;
                for (int d = 0; d < dim; d++)
                {
                    double a = alpha[k, d];
                    term2 += (invPrecision[k, d] + a * a) * phi[d];
                }
                term2 *= 0.5;

                for (int i = 0; i < numSamples; i++)
                {
                    double term1 = 0.0;
                    for (int d = 0; d < dim; d++)
                    {
                        term1 += rho[i, d] * alpha[k, d];
                    }
                    logProb[i, k] = fa * (term1 - term2 + constant[i]);
                }
            }

            // D. Marginalización y actualización de gamma y priors
            double[] logPi = new double[initialK];
            for (int k = 0; k < initialK; k++) logPi[k] = Math.Log(priors[k] + 1e-8);

            double[] logMarginal = new double[numSamples];
            double elboSumMarginal = 0.0;

            for (int i = 0; i < numSamples; i++)
            {
                // logsumexp(log_probability + log_pi)
                double maxJoint = -double.MaxValue;
                for (int k = 0; k < initialK; k++)
                {
                    double v = logProb[i, k] + logPi[k];
                    if (v > maxJoint) maxJoint = v;
                }

                double sumExp = 0.0;
                for (int k = 0; k < initialK; k++)
                {
                    sumExp += Math.Exp(logProb[i, k] + logPi[k] - maxJoint);
                }
                double lse = maxJoint + Math.Log(Math.Max(sumExp, 1e-12));
                logMarginal[i] = lse;
                elboSumMarginal += lse;

                for (int k = 0; k < initialK; k++)
                {
                    gamma[i, k] = Math.Exp(logProb[i, k] + logPi[k] - lse);
                }
            }

            // E. Nuevos priors normalizados
            double sumAllGamma = 0.0;
            for (int k = 0; k < initialK; k++)
            {
                double s = 0.0;
                for (int i = 0; i < numSamples; i++) s += gamma[i, k];
                priors[k] = s;
                sumAllGamma += s;
            }
            if (sumAllGamma > 1e-12)
            {
                for (int k = 0; k < initialK; k++) priors[k] /= sumAllGamma;
            }

            // F. Cómputo de ELBO para convergencia
            double elboReg = 0.0;
            for (int k = 0; k < initialK; k++)
            {
                for (int d = 0; d < dim; d++)
                {
                    double ip = invPrecision[k, d];
                    double a = alpha[k, d];
                    elboReg += Math.Log(Math.Max(ip, 1e-12)) - ip - (a * a) + 1.0;
                }
            }
            double elbo = elboSumMarginal + fb * 0.5 * elboReg;

            if (previousElbo.HasValue && (elbo - previousElbo.Value < ElboConvergenceThreshold))
            {
                break;
            }
            previousElbo = elbo;
        }

        // 6. Filtrar clusters activos (priors > 1e-7) y calcular centroides sobre raw embeddings (256-D)
        var activeClusterIndices = new List<int>();
        for (int k = 0; k < initialK; k++)
        {
            if (priors[k] > 1e-7)
            {
                activeClusterIndices.Add(k);
            }
        }
        if (activeClusterIndices.Count == 0) activeClusterIndices.Add(0);

        int finalK = activeClusterIndices.Count;
        double[][] centroids = new double[finalK][];
        double[,] finalResponsibilities = new double[numSamples, finalK];
        double[] finalPriors = new double[finalK];

        for (int outIdx = 0; outIdx < finalK; outIdx++)
        {
            int origK = activeClusterIndices[outIdx];
            finalPriors[outIdx] = priors[origK];

            double[] centroid = new double[rawDim];
            double weightSum = 0.0;

            for (int i = 0; i < numSamples; i++)
            {
                double w = gamma[i, origK];
                finalResponsibilities[i, outIdx] = w;
                weightSum += w;

                for (int d = 0; d < rawDim; d++)
                {
                    centroid[d] += w * trainRawEmbeddings[i][d];
                }
            }

            if (weightSum > 1e-12)
            {
                for (int d = 0; d < rawDim; d++) centroid[d] /= weightSum;
            }
            centroids[outIdx] = centroid;
        }

        return new VbxResult(finalResponsibilities, finalPriors, centroids);
    }

    /// <summary>
    /// K-Means clustering de respaldo determinista (utilizado cuando el usuario fuerza un recuento específico
    /// de hablantes o el resultado de VBx cae fuera de [min_speakers, max_speakers]).
    /// </summary>
    public static double[][] KMeansCluster(float[][] normalizedEmbeddings, int k, int maxIterations = 50)
    {
        int n = normalizedEmbeddings.Length;
        int dim = normalizedEmbeddings[0].Length;

        if (n <= k)
        {
            var res = new double[n][];
            for (int i = 0; i < n; i++)
            {
                res[i] = new double[dim];
                for (int d = 0; d < dim; d++) res[i][d] = normalizedEmbeddings[i][d];
            }
            return res;
        }

        // Inicialización determinista k-means++
        double[][] centroids = new double[k][];
        centroids[0] = new double[dim];
        for (int d = 0; d < dim; d++) centroids[0][d] = normalizedEmbeddings[0][d];

        for (int c = 1; c < k; c++)
        {
            double maxDistSq = -1.0;
            int bestIdx = 0;

            for (int i = 0; i < n; i++)
            {
                double minDistToCenters = double.MaxValue;
                for (int prev = 0; prev < c; prev++)
                {
                    double dist = 0.0;
                    for (int d = 0; d < dim; d++)
                    {
                        double diff = normalizedEmbeddings[i][d] - centroids[prev][d];
                        dist += diff * diff;
                    }
                    if (dist < minDistToCenters) minDistToCenters = dist;
                }

                if (minDistToCenters > maxDistSq)
                {
                    maxDistSq = minDistToCenters;
                    bestIdx = i;
                }
            }

            centroids[c] = new double[dim];
            for (int d = 0; d < dim; d++) centroids[c][d] = normalizedEmbeddings[bestIdx][d];
        }

        int[] assignments = new int[n];
        for (int iter = 0; iter < maxIterations; iter++)
        {
            bool changed = false;

            for (int i = 0; i < n; i++)
            {
                double bestDist = double.MaxValue;
                int bestC = 0;

                for (int c = 0; c < k; c++)
                {
                    double dist = 0.0;
                    for (int d = 0; d < dim; d++)
                    {
                        double diff = normalizedEmbeddings[i][d] - centroids[c][d];
                        dist += diff * diff;
                    }
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestC = c;
                    }
                }

                if (assignments[i] != bestC)
                {
                    assignments[i] = bestC;
                    changed = true;
                }
            }

            if (!changed) break;

            // Recalcular centroides
            int[] counts = new int[k];
            double[][] newCentroids = new double[k][];
            for (int c = 0; c < k; c++) newCentroids[c] = new double[dim];

            for (int i = 0; i < n; i++)
            {
                int c = assignments[i];
                counts[c]++;
                for (int d = 0; d < dim; d++)
                {
                    newCentroids[c][d] += normalizedEmbeddings[i][d];
                }
            }

            for (int c = 0; c < k; c++)
            {
                if (counts[c] > 0)
                {
                    for (int d = 0; d < dim; d++)
                    {
                        centroids[c][d] = newCentroids[c][d] / counts[c];
                    }
                }
            }
        }

        return centroids;
    }

    private static double[] ComputeMean(float[][] vectors)
    {
        int dim = vectors[0].Length;
        double[] mean = new double[dim];
        for (int i = 0; i < vectors.Length; i++)
        {
            for (int d = 0; d < dim; d++) mean[d] += vectors[i][d];
        }
        for (int d = 0; d < dim; d++) mean[d] /= vectors.Length;
        return mean;
    }
}
