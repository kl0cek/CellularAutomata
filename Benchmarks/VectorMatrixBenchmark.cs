using System.Diagnostics;

namespace CellularAutomata;

// Porównanie dwóch implementacji rozrostu ziaren CA w 3D: siatka jako wektor int[] i jako macierz int[,,].
public static class VectorMatrixBenchmark
{
    public static void Run()
    {
        const int repetitions = 5;
        const int seed = 1;
        const Neighbourhood neighbourhood = Neighbourhood.Moore;
        const Boundary boundary = Boundary.Periodic;
        int[] sizes = { 10, 25, 50, 75, 100 };

        // Rozgrzewka: pierwsze wywołanie zawiera kompilację JIT, więc nie wchodzi do pomiarów.
        VectorGrainGrowth.Generate(25, 25, 25, 20, neighbourhood, boundary, seed);
        MatrixGrainGrowth.Generate(25, 25, 25, 20, neighbourhood, boundary, seed);

        Console.WriteLine($"Sąsiedztwo: {neighbourhood}, brzegi: {boundary}, seed: {seed}, czas = średnia z {repetitions} uruchomień\n");
        Console.WriteLine($"{"Siatka",-12} {"Komórki",10} {"Ziarna",7} {"Kroki",6} {"Wektor [ms]",12} {"Macierz [ms]",13} {"Mac/Wek",8} {"Zgodne",7}");
        Console.WriteLine(new string('-', 82));

        foreach (int n in sizes)
        {
            int cellCount = n * n * n;
            int nuclei = Math.Max(10, cellCount / 1000); // ok. 1 zarodek na 1000 komórek

            var vectorTimes = new double[repetitions];
            var matrixTimes = new double[repetitions];
            (int[] Cells, int Steps) vector = (Array.Empty<int>(), 0);
            (int[,,] Cells, int Steps) matrix = (new int[0, 0, 0], 0);

            for (int r = 0; r < repetitions; r++)
            {
                long start = Stopwatch.GetTimestamp();
                vector = VectorGrainGrowth.Generate(n, n, n, nuclei, neighbourhood, boundary, seed);
                vectorTimes[r] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

                start = Stopwatch.GetTimestamp();
                matrix = MatrixGrainGrowth.Generate(n, n, n, nuclei, neighbourhood, boundary, seed);
                matrixTimes[r] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            bool same = vector.Steps == matrix.Steps && SameCells(vector.Cells, matrix.Cells, n);
            double vectorMs = vectorTimes.Average();
            double matrixMs = matrixTimes.Average();

            Console.WriteLine($"{$"{n}x{n}x{n}",-12} {cellCount,10:N0} {nuclei,7} {vector.Steps,6} " +
                              $"{vectorMs,12:F2} {matrixMs,13:F2} {matrixMs / vectorMs,8:F2} {(same ? "TAK" : "NIE"),7}");
        }
    }

    // Porównuje komórka po komórce: wektor[x + n*(y + n*z)] z macierzą[z, y, x].
    private static bool SameCells(int[] vector, int[,,] matrix, int n)
    {
        for (int z = 0; z < n; z++)
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
            if (vector[x + n * (y + n * z)] != matrix[z, y, x])
                return false;
        return true;
    }
}
