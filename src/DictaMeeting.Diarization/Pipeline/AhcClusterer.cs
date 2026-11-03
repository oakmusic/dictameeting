namespace DictaMeeting.Diarization.Pipeline;

/// <summary>
/// Clustering Jerárquico Aglomerativo (AHC) con enlace por centroide (Centroid Linkage)
/// y métrica Euclidiana sobre embeddings normalizados L2.
/// Se utiliza en PyAnnote Community-1 como etapa de inicialización para el clustering VBx.
/// </summary>
public static class AhcClusterer
{
    private class AhcCluster
    {
        public int Id { get; set; }
        public double[] Centroid { get; set; }
        public int Count { get; set; }
        public List<int> MemberIndices { get; }

        public AhcCluster(int id, double[] initialVector, int memberIndex)
        {
            Id = id;
            Centroid = (double[])initialVector.Clone();
            Count = 1;
            MemberIndices = new List<int> { memberIndex };
        }
    }

    /// <summary>
    /// Ejecuta el clustering aglomerativo por centroide con corte por umbral de distancia.
    /// Retorna un array con el ID de cluster (0 a K-1) asignado a cada vector de entrada.
    /// Optimizado a O(N^2) mediante matriz simétrica de distancias y seguimiento de vecinos más próximos.
    /// </summary>
    /// <param name="normalizedEmbeddings">Array de N vectores normalizados L2 (e.g. 256 dimensiones).</param>
    /// <param name="threshold">Umbral de distancia Euclidiana de corte (por defecto 0.6 en Community-1).</param>
    public static int[] Cluster(double[][] normalizedEmbeddings, double threshold = 0.6)
    {
        int n = normalizedEmbeddings.Length;
        if (n == 0) return Array.Empty<int>();
        if (n == 1) return new[] { 0 };

        int dim = normalizedEmbeddings[0].Length;
        double thresholdSq = threshold * threshold;

        // Representación compacta de clusters activos
        int[] counts = new int[n];
        double[][] centroids = new double[n][];
        List<int>[] members = new List<int>[n];
        bool[] isActive = new bool[n];

        for (int i = 0; i < n; i++)
        {
            counts[i] = 1;
            centroids[i] = (double[])normalizedEmbeddings[i].Clone();
            members[i] = new List<int> { i };
            isActive[i] = true;
        }

        // Matriz simétrica de distancias al cuadrado D[i, j]
        double[,] distSq = new double[n, n];
        int[] nearest = new int[n];
        double[] nearestDistSq = new double[n];

        for (int i = 0; i < n; i++)
        {
            double bestD = double.MaxValue;
            int bestJ = -1;
            for (int j = 0; j < n; j++)
            {
                if (i == j)
                {
                    distSq[i, j] = double.MaxValue;
                    continue;
                }
                if (j > i)
                {
                    double d = EuclideanDistanceSquared(centroids[i], centroids[j]);
                    distSq[i, j] = d;
                    distSq[j, i] = d;
                }
                double curD = distSq[i, j];
                if (curD < bestD)
                {
                    bestD = curD;
                    bestJ = j;
                }
            }
            nearest[i] = bestJ;
            nearestDistSq[i] = bestD;
        }

        int activeCount = n;

        while (activeCount > 1)
        {
            // Encontrar el par activo con menor distancia en O(N)
            double globalMinDistSq = double.MaxValue;
            int bestI = -1;

            for (int i = 0; i < n; i++)
            {
                if (!isActive[i]) continue;
                if (nearestDistSq[i] < globalMinDistSq)
                {
                    globalMinDistSq = nearestDistSq[i];
                    bestI = i;
                }
            }

            if (bestI < 0 || globalMinDistSq > thresholdSq)
            {
                // Ningún par está a una distancia menor o igual al umbral
                break;
            }

            int bestJ = nearest[bestI];
            if (bestJ < 0 || !isActive[bestJ])
            {
                RecalculateNearest(bestI, n, isActive, distSq, nearest, nearestDistSq);
                continue;
            }

            int iIdx = Math.Min(bestI, bestJ);
            int jIdx = Math.Max(bestI, bestJ);

            // Fusionar jIdx en iIdx
            int countI = counts[iIdx];
            int countJ = counts[jIdx];
            int newCount = countI + countJ;
            var centroidI = centroids[iIdx];
            var centroidJ = centroids[jIdx];

            for (int d = 0; d < dim; d++)
            {
                centroidI[d] = (centroidI[d] * countI + centroidJ[d] * countJ) / newCount;
            }
            counts[iIdx] = newCount;
            members[iIdx].AddRange(members[jIdx]);

            // Desactivar jIdx
            isActive[jIdx] = false;
            activeCount--;

            // Actualizar distancias entre iIdx y todos los clusters activos k en O(N)
            for (int k = 0; k < n; k++)
            {
                if (!isActive[k] || k == iIdx) continue;

                double d = EuclideanDistanceSquared(centroidI, centroids[k]);
                distSq[iIdx, k] = d;
                distSq[k, iIdx] = d;

                if (nearest[k] == jIdx || nearest[k] == iIdx || d < nearestDistSq[k])
                {
                    RecalculateNearest(k, n, isActive, distSq, nearest, nearestDistSq);
                }
            }

            // Recalcular nearest para iIdx
            RecalculateNearest(iIdx, n, isActive, distSq, nearest, nearestDistSq);
        }

        // Asignar IDs contiguos 0..K-1
        int[] result = new int[n];
        int clusterId = 0;
        for (int i = 0; i < n; i++)
        {
            if (isActive[i])
            {
                foreach (int memberIdx in members[i])
                {
                    result[memberIdx] = clusterId;
                }
                clusterId++;
            }
        }

        return result;
    }

    private static void RecalculateNearest(int i, int n, bool[] isActive, double[,] distSq, int[] nearest, double[] nearestDistSq)
    {
        double bestD = double.MaxValue;
        int bestJ = -1;
        for (int j = 0; j < n; j++)
        {
            if (i == j || !isActive[j]) continue;
            double d = distSq[i, j];
            if (d < bestD)
            {
                bestD = d;
                bestJ = j;
            }
        }
        nearest[i] = bestJ;
        nearestDistSq[i] = bestD;
    }

    private static double EuclideanDistanceSquared(double[] a, double[] b)
    {
        double sum = 0.0;
        int len = a.Length;
        for (int i = 0; i < len; i++)
        {
            double diff = a[i] - b[i];
            sum += diff * diff;
        }
        return sum;
    }
}
