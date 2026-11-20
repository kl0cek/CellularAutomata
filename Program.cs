using System.Diagnostics;
using CellularAutomata;

// Wersja serwerowa – bez interfejsu użytkownika, wszystkie parametry z pliku konfiguracyjnego.
//   dotnet run -c Release                  → parametry z config.json
//   dotnet run -c Release -- config-mc.json  → rozrost ziaren Monte Carlo (Lab 3)
//   dotnet run -c Release -- moj.json         → parametry z innego pliku
//   dotnet run -c Release -- bench         → porównanie czasu: wektor vs macierz
//   dotnet run -c Release -- bench-opt     → wersja naiwna vs zoptymalizowana (Lab 8)
//   dotnet run -c Release -- demo          → podgląd MC na małej siatce 2D
//
// Struktura:
//   Core/        – wspólne dla CA i MC: sąsiedztwa i brzegi (CaRules), indeksy sąsiadów, liczenie ziaren
//   Ca/          – rozrost ziaren CA: wektor, macierz, wersja zoptymalizowana (front)
//   MonteCarlo/  – rozrost ziaren MC: wersja naiwna, zoptymalizowana, tryb demo
//   IO/          – konfiguracja (config.json), log czasów, zapis wyników
//   Benchmarks/  – porównania wydajności (bench, bench-opt)
//   Gui/         – osobny projekt: interfejs WWW (Lab 5–6)

#if DEBUG
Console.WriteLine("UWAGA: build Debug – czasy będą zawyżone. Uruchom: dotnet run -c Release\n");
#endif

long appStart = Stopwatch.GetTimestamp();
string argument = args.Length > 0 ? args[0] : "config.json";

try
{
    if (argument == "bench")
    {
        VectorMatrixBenchmark.Run();
        return 0;
    }
    if (argument == "bench-opt")
    {
        OptimizationBenchmark.Run(args[1..]);
        return 0;
    }
    if (argument == "demo")
    {
        McDemo.Run();
        return 0;
    }

    //Dane początkowe z pliku konfiguracyjnego.
    var config = SimulationConfig.Load(argument);
    Console.WriteLine($"Konfiguracja: {Path.GetFullPath(argument)}");
    Console.WriteLine($"Algorytm: {config.Algorithm}, siatka {config.SizeX}x{config.SizeY}x{config.SizeZ}, " +
                      $"sąsiedztwo: {config.Neighbourhood}, BC: {config.Boundary}, SplitInZdim: {config.SplitInZdim}");
    Console.WriteLine(config.Algorithm == SimulationAlgorithm.CA
        ? $"Zarodki: {config.Nucleons}"
        : $"Stany Q: {config.States}, kroki MCS: {config.McSteps}, J: {config.J}, nowy stan: {config.McProposal}");
    Console.WriteLine($"Wersja: {(config.Optimized ? "zoptymalizowana (Lab 8)" : "naiwna")}");

    //Symulacja
    var log = new SimulationLog();
    long start = Stopwatch.GetTimestamp();
    var (cells, steps) = Simulate(config, log);
    double simulationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    Console.WriteLine($"\nAlokacja pamięci: {log.AllocationMs,10:F3} ms");
    Console.WriteLine($"Stan początkowy:  {log.InitMs,10:F3} ms\n");
    double cumulative = 0;
    if (config.Algorithm == SimulationAlgorithm.CA)
    {
        Console.WriteLine($"{"Krok",5} {"Zapełnione",11} {"Puste",10} {"Δt [ms]",10} {"Suma [ms]",10}");
        foreach (var s in log.Steps)
        {
            cumulative += s.Ms;
            Console.WriteLine($"{s.Step,5} {s.Changed,11:N0} {s.EmptyLeft,10:N0} {s.Ms,10:F3} {cumulative,10:F3}");
        }
    }
    else
    {
        Console.WriteLine($"{"MCS",5} {"Przyjęte",11} {"Energia",12} {"Δt [ms]",10} {"Suma [ms]",10}");
        foreach (var s in log.McSteps)
        {
            cumulative += s.Ms;
            Console.WriteLine($"{s.Step,5} {s.Accepted,11:N0} {s.Energy,12:N0} {s.Ms,10:F3} {cumulative,10:F3}");
        }
    }
    Console.WriteLine($"\nSymulacja razem: {simulationMs:F2} ms ({steps} kroków)");

    // Liczba ziaren na końcu (spójne obszary o tym samym ID) – poza pomiarem czasu symulacji.
    int grains = config.Optimized
        ? GrainAnalysis.CountGrainsFast(cells, config.SizeX, config.SizeY, config.SizeZ, config.Neighbourhood, config.Boundary)
        : GrainAnalysis.CountGrains(cells, config.SizeX, config.SizeY, config.SizeZ, config.Neighbourhood, config.Boundary);
    Console.WriteLine($"Ziarna na końcu: {grains:N0}, średnia wielkość: {(double)cells.Length / grains:F1} komórek");

    // Zapis mikrostruktury
    var file = config.Optimized
        ? ResultWriter.WriteMorphologyFast(config.MorphologyPath, config.SizeX, config.SizeY, config.SizeZ, cells)
        : ResultWriter.WriteMorphology(config.MorphologyPath, config.SizeX, config.SizeY, config.SizeZ, cells);
    Console.WriteLine($"Plik {Path.GetFullPath(config.MorphologyPath)} ({file.Bytes / 1e6:F1} MB):");
    Console.WriteLine($"  utworzenie: {file.CreateMs:F3} ms, zapis: {file.WriteMs:F3} ms");

    //Zapis czasu
    ResultWriter.WriteStepTimes(config.StepTimePath, log);
    double totalMs = Stopwatch.GetElapsedTime(appStart).TotalMilliseconds;
    ResultWriter.AppendTime(config.TimePath, config, steps, grains, log, simulationMs, file, totalMs);
    Console.WriteLine($"Czasy: {Path.GetFullPath(config.TimePath)}, {Path.GetFullPath(config.StepTimePath)}");
    Console.WriteLine($"Całość: {totalMs:F2} ms");
}
catch (Exception ex) when (ex is FormatException or ArgumentException or IOException)
{
    Console.Error.WriteLine($"Błąd: {ex.Message}");
    return 1;
}
return 0;

// Wybór algorytmu (CA/MC) i wersji (naiwna/zoptymalizowana); zwraca siatkę i liczbę kroków.
static (int[] Cells, int Steps) Simulate(SimulationConfig c, SimulationLog log)
{
    if (c.Algorithm == SimulationAlgorithm.CA)
        return c.Optimized
            ? FrontierGrainGrowth.Generate(c.SizeX, c.SizeY, c.SizeZ, c.Nucleons, c.Neighbourhood, c.Boundary, c.Seed, log)
            : VectorGrainGrowth.Generate(c.SizeX, c.SizeY, c.SizeZ, c.Nucleons, c.Neighbourhood, c.Boundary, c.Seed, log);

    int[] cells = c.Optimized
        ? FastMonteCarloGrainGrowth.Generate(c.SizeX, c.SizeY, c.SizeZ, c.States, c.McSteps, c.Neighbourhood, c.Boundary,
                                             c.J, c.McProposal, c.Seed, log)
        : MonteCarloGrainGrowth.Generate(c.SizeX, c.SizeY, c.SizeZ, c.States, c.McSteps, c.Neighbourhood, c.Boundary,
                                         c.J, c.McProposal, c.Seed, log);
    return (cells, c.McSteps);
}
