using System.Diagnostics;

namespace CellularAutomata;

// Lab 8: porównanie wersji naiwnej i zoptymalizowanej w jednym procesie.
//   dotnet run -c Release -- bench-opt            → siatki 50³, 100³, 150³
//   dotnet run -c Release -- bench-opt 100 200    → wybrane rozmiary
// Wyniki trafiają też do OutputTXT/bench-opt.csv (do wykresów: scripts/plot_lab8.py).
// Każdy pomiar to średnia z kilku powtórzeń po rozgrzewce (kompilacja JIT nie wchodzi do czasu).
// Kolumna „Zgodne” sprawdza, że obie wersje dają identyczny wynik (siatka, liczba kroków, liczba ziaren, plik).
public static class OptimizationBenchmark
{
    private const int Repetitions = 3;
    private const int Seed = 1;
    private const Neighbourhood Moore = Neighbourhood.Moore;
    private const Boundary Periodic = Boundary.Periodic;
    private const string CsvPath = "OutputTXT/bench-opt.csv";

    // Wiersze CSV: część;siatka;naiwna_ms;zopt_ms;przyspieszenie;zgodne
    private static readonly List<string> Csv = new() { "part;n;naive_ms;fast_ms;speedup;same" };

    public static void Run(string[] args)
    {
        int[] sizes = args.Length > 0 ? args.Select(int.Parse).ToArray() : new[] { 50, 100, 150 };

        Warmup();
        Console.WriteLine($"Sąsiedztwo Moore'a, brzegi periodyczne, seed {Seed}; CA: 100 zarodków; MC: Q = 50, 20 MCS, nowy stan losowy.");
        Console.WriteLine($"Czas = średnia z {Repetitions} powtórzeń.\n");
        Console.WriteLine($"{"Część",-26} {"Siatka",7} {"Naiwna [ms]",12} {"Zopt. [ms]",11} {"Przyspieszenie",15} {"Zgodne",7}");
        Console.WriteLine(new string('-', 83));

        foreach (int n in sizes)
        {
            // CA: rozrost
            (int[] Cells, int Steps) caNaive = (Array.Empty<int>(), 0), caFast = (Array.Empty<int>(), 0);
            double caNaiveMs = Measure(() => caNaive = VectorGrainGrowth.Generate(n, n, n, 100, Moore, Periodic, Seed));
            double caFastMs = Measure(() => caFast = FrontierGrainGrowth.Generate(n, n, n, 100, Moore, Periodic, Seed));
            Row("CA – rozrost", n, caNaiveMs, caFastMs,
                caNaive.Steps == caFast.Steps && caNaive.Cells.AsSpan().SequenceEqual(caFast.Cells));

            // MC: rozrost
            int[] mcNaive = Array.Empty<int>(), mcFast = Array.Empty<int>();
            double mcNaiveMs = Measure(() => mcNaive = MonteCarloGrainGrowth.Generate(n, n, n, 50, 20, Moore, Periodic, 1.0, McProposal.Random, Seed));
            double mcFastMs = Measure(() => mcFast = FastMonteCarloGrainGrowth.Generate(n, n, n, 50, 20, Moore, Periodic, 1.0, McProposal.Random, Seed));
            Row("MC – rozrost (20 MCS)", n, mcNaiveMs, mcFastMs, mcNaive.AsSpan().SequenceEqual(mcFast));

            // Liczenie ziaren – na mikrostrukturze MC (dużo małych ziaren, więc więcej pracy niż dla CA)
            int grainsNaive = 0, grainsFast = 0;
            double gNaiveMs = Measure(() => grainsNaive = GrainAnalysis.CountGrains(mcFast, n, n, n, Moore, Periodic));
            double gFastMs = Measure(() => grainsFast = GrainAnalysis.CountGrainsFast(mcFast, n, n, n, Moore, Periodic));
            Row("Liczenie ziaren (MC)", n, gNaiveMs, gFastMs, grainsNaive == grainsFast);

            // Zapis Micro.txt – do plików tymczasowych, potem porównanie bajt po bajcie
            string naivePath = Path.GetTempFileName(), fastPath = Path.GetTempFileName();
            double wNaiveMs = Measure(() => ResultWriter.WriteMorphology(naivePath, n, n, n, caFast.Cells));
            double wFastMs = Measure(() => ResultWriter.WriteMorphologyFast(fastPath, n, n, n, caFast.Cells));
            Row("Zapis Micro.txt", n, wNaiveMs, wFastMs, File.ReadAllBytes(naivePath).AsSpan().SequenceEqual(File.ReadAllBytes(fastPath)));
            File.Delete(naivePath);
            File.Delete(fastPath);

            Console.WriteLine();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(CsvPath)!);
        File.WriteAllLines(CsvPath, Csv);
        Console.WriteLine($"Wyniki: {Path.GetFullPath(CsvPath)}");
    }

    // Pierwsze wywołania kompilują kod (JIT) – robimy je na małej siatce, poza pomiarem.
    private static void Warmup()
    {
        for (int r = 0; r < 3; r++)
        {
            var ca = VectorGrainGrowth.Generate(30, 30, 30, 20, Moore, Periodic, Seed).Cells;
            FrontierGrainGrowth.Generate(30, 30, 30, 20, Moore, Periodic, Seed);
            MonteCarloGrainGrowth.Generate(30, 30, 30, 50, 5, Moore, Periodic, 1.0, McProposal.Random, Seed);
            FastMonteCarloGrainGrowth.Generate(30, 30, 30, 50, 5, Moore, Periodic, 1.0, McProposal.Random, Seed);
            GrainAnalysis.CountGrains(ca, 30, 30, 30, Moore, Periodic);
            GrainAnalysis.CountGrainsFast(ca, 30, 30, 30, Moore, Periodic);
            string path = Path.GetTempFileName();
            ResultWriter.WriteMorphology(path, 30, 30, 30, ca);
            ResultWriter.WriteMorphologyFast(path, 30, 30, 30, ca);
            File.Delete(path);
        }
    }

    private static double Measure(Action action)
    {
        double total = 0;
        for (int r = 0; r < Repetitions; r++)
        {
            long start = Stopwatch.GetTimestamp();
            action();
            total += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        return total / Repetitions;
    }

    private static void Row(string part, int n, double naiveMs, double fastMs, bool same)
    {
        Console.WriteLine($"{part,-26} {$"{n}³",7} {naiveMs,12:F1} {fastMs,11:F1} {naiveMs / fastMs,14:F2}× {(same ? "TAK" : "NIE"),7}");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Csv.Add(string.Join(';', part, n, naiveMs.ToString("F2", inv), fastMs.ToString("F2", inv),
                            (naiveMs / fastMs).ToString("F3", inv), same ? "TAK" : "NIE"));
    }
}
