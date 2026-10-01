namespace DictaMeeting.Diarization.Pipeline;

/// <summary>
/// Implementación del algoritmo Húngaro (Kuhn-Munkres) para el problema de asignación lineal óptima.
/// Resuelve la correspondencia óptima 1-a-1 entre los interlocutores locales de cada chunk
/// y los centroides globales de la reunión, maximizando la similitud acústica.
/// </summary>
public static class HungarianSolver
{
    /// <summary>
    /// Resuelve la asignación que MAXIMIZA la puntuación total entre filas y columnas.
    /// Retorna un array donde array[row] = col asignada (o -1 si no se asignó).
    /// </summary>
    public static int[] Maximize(double[,] scores)
    {
        int rows = scores.GetLength(0);
        int cols = scores.GetLength(1);

        if (rows == 0 || cols == 0) return Array.Empty<int>();

        // Encontrar valor máximo para invertir el problema en minimización de costes
        double maxScore = -double.MaxValue;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (scores[r, c] > maxScore) maxScore = scores[r, c];
            }
        }

        int n = Math.Max(rows, cols);
        double[,] cost = new double[n, n];

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (r < rows && c < cols)
                {
                    cost[r, c] = maxScore - scores[r, c];
                }
                else
                {
                    cost[r, c] = maxScore + 1.0; // Relleno neutral
                }
            }
        }

        int[] minAssignment = SolveMinCost(cost, n);
        int[] result = new int[rows];

        for (int r = 0; r < rows; r++)
        {
            int assignedCol = minAssignment[r];
            result[r] = assignedCol < cols ? assignedCol : -1;
        }

        return result;
    }

    /// <summary>
    /// Algoritmo clásico Jonker-Volgenant / Kuhn-Munkres en O(N^3) para matriz cuadrada n x n.
    /// </summary>
    private static int[] SolveMinCost(double[,] cost, int n)
    {
        double[] u = new double[n + 1];
        double[] v = new double[n + 1];
        int[] p = new int[n + 1];
        int[] way = new int[n + 1];

        for (int i = 1; i <= n; i++)
        {
            p[0] = i;
            int j0 = 0;
            double[] minv = new double[n + 1];
            bool[] used = new bool[n + 1];
            for (int j = 0; j <= n; j++) minv[j] = double.MaxValue;

            do
            {
                used[j0] = true;
                int i0 = p[j0];
                double delta = double.MaxValue;
                int j1 = 0;

                for (int j = 1; j <= n; j++)
                {
                    if (!used[j])
                    {
                        double cur = cost[i0 - 1, j - 1] - u[i0] - v[j];
                        if (cur < minv[j])
                        {
                            minv[j] = cur;
                            way[j] = j0;
                        }
                        if (minv[j] < delta)
                        {
                            delta = minv[j];
                            j1 = j;
                        }
                    }
                }

                for (int j = 0; j <= n; j++)
                {
                    if (used[j])
                    {
                        u[p[j]] += delta;
                        v[j] -= delta;
                    }
                    else
                    {
                        minv[j] -= delta;
                    }
                }

                j0 = j1;
            } while (p[j0] != 0);

            do
            {
                int j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }

        int[] result = new int[n];
        for (int j = 1; j <= n; j++)
        {
            if (p[j] != 0)
            {
                result[p[j] - 1] = j - 1;
            }
        }

        return result;
    }
}
