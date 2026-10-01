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
    /// </summary>
    /// <param name="normalizedEmbeddings">Array de N vectores normalizados L2 (e.g. 256 dimensiones).</param>
    /// <param name="threshold">Umbral de distancia Euclidiana de corte (por defecto 0.6 en Community-1).</param>
    public static int[] Cluster(double[][] normalizedEmbeddings, double threshold = 0.6)
    {
        int n = normalizedEmbeddings.Length;
        if (n == 0) return Array.Empty<int>();
        if (n == 1) return new[] { 0 };

        int dim = normalizedEmbeddings[0].Length;
        var activeClusters = new List<AhcCluster>(n);

        for (int i = 0; i < n; i++)
        {
            activeClusters.Add(new AhcCluster(i, normalizedEmbeddings[i], i));
        }

        while (activeClusters.Count > 1)
        {
            double minDistanceSq = double.MaxValue;
            int bestI = -1;
            int bestJ = -1;

            for (int i = 0; i < activeClusters.Count; i++)
            {
                var cI = activeClusters[i];
                for (int j = i + 1; j < activeClusters.Count; j++)
                {
                    var cJ = activeClusters[j];
                    double distSq = EuclideanDistanceSquared(cI.Centroid, cJ.Centroid);
                    if (distSq < minDistanceSq)
                    {
                        minDistanceSq = distSq;
                        bestI = i;
                        bestJ = j;
                    }
                }
            }

            double minDistance = Math.Sqrt(minDistanceSq);
            if (minDistance > threshold)
            {
                // Ningún par está a una distancia menor o igual al umbral
                break;
            }

            // Fusionar bestI y bestJ
            var clusterA = activeClusters[bestI];
            var clusterB = activeClusters[bestJ];

            int newCount = clusterA.Count + clusterB.Count;
            double[] newCentroid = new double[dim];

            for (int d = 0; d < dim; d++)
            {
                newCentroid[d] = (clusterA.Centroid[d] * clusterA.Count + clusterB.Centroid[d] * clusterB.Count) / newCount;
            }

            clusterA.Centroid = newCentroid;
            clusterA.Count = newCount;
            clusterA.MemberIndices.AddRange(clusterB.MemberIndices);

            activeClusters.RemoveAt(bestJ);
        }

        // Asignar IDs contiguos 0..K-1
        int[] result = new int[n];
        for (int k = 0; k < activeClusters.Count; k++)
        {
            foreach (int memberIdx in activeClusters[k].MemberIndices)
            {
                result[memberIdx] = k;
            }
        }

        return result;
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
