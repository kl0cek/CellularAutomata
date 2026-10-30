using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace CellularAutomata.Gui;

// Zlecanie obliczeń: każde uruchomienie dostaje własny folder z config.json, trafia do kolejki,
// a symulator startuje jako osobny proces – dokładnie tak, jak wersja serwerowa z terminala.
// Kolejka liczy zlecenia po jednym, żeby czasy z serii były porównywalne.
public sealed partial class RunManager
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private readonly ConcurrentDictionary<string, ActiveRun> _active = new();
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
    private readonly RunFolders _folders;

    public RunManager(string simulatorDll, string runsDirectory)
    {
        SimulatorDll = simulatorDll;
        _folders = new RunFolders(runsDirectory);
        _ = Task.Run(ProcessQueueAsync);
    }

    public string SimulatorDll { get; }
    public string RunsDirectory => _folders.Root;
    public bool SimulatorFound => File.Exists(SimulatorDll);

    // Tworzy folder z config.json i dopisuje uruchomienie do kolejki. series łączy uruchomienia jednej serii.
    public string Enqueue(RunRequest request, string? series = null)
    {
        string id = _folders.Create();
        File.WriteAllText(Path.Combine(_folders.PathOf(id), RunFiles.Config),
            JsonSerializer.Serialize(request.ToSimulatorConfig(series), Indented));

        _active[id] = new ActiveRun();
        _queue.Writer.TryWrite(id);
        return id;
    }

    public List<RunSummary> List() => _folders.Ids().Take(200).Select(Summarize).ToList();

    public RunDetails? Describe(string id)
    {
        string? folder = _folders.Find(id);
        if (folder == null)
            return null;

        _active.TryGetValue(id, out var active);
        string logFile = Path.Combine(folder, RunFiles.Console);
        string log = active?.Log ?? (File.Exists(logFile) ? File.ReadAllText(logFile) : "");

        return new RunDetails(
            Id: id,
            Status: StatusOf(id),
            Folder: folder,
            ElapsedSeconds: active?.ElapsedSeconds,
            Config: RunFolders.ReadConfig(folder),
            Summary: RunFolders.ReadTimeSummary(folder),
            Log: log);
    }

    public List<RunResult> Results() => ResultsTable.Collect(_folders, StatusOf);

    // Ścieżka do pliku wynikowego danego uruchomienia albo null, gdy nie istnieje.
    public string? FileOf(string id, string fileName)
    {
        string? folder = _folders.Find(id);
        string? path = folder == null ? null : Path.Combine(folder, fileName);
        return path != null && File.Exists(path) ? path : null;
    }

    private string StatusOf(string id) =>
        _active.TryGetValue(id, out var active) ? active.Status : RunFolders.StatusFromFiles(_folders.PathOf(id));

    private RunSummary Summarize(string id)
    {
        string folder = _folders.PathOf(id);
        var config = RunFolders.ReadConfig(folder);
        var summary = RunFolders.ReadTimeSummary(folder);

        return new RunSummary(
            Id: id,
            Status: StatusOf(id),
            Algorithm: config?.Text("Algorithm"),
            Size: config == null ? null : $"{config.Int("FullSpaceSizeX")}×{config.Int("FullSpaceSizeY")}×{config.Int("FullSpaceSizeZ")}",
            Series: config?.Text("Series"),
            GrainsEnd: summary?.GetValueOrDefault("grains_end"),
            SimulationMs: summary?.GetValueOrDefault("simulation_ms"));
    }

    // Kolejka: zlecenia liczą się po kolei, jedno po drugim.
    private async Task ProcessQueueAsync()
    {
        await foreach (string id in _queue.Reader.ReadAllAsync())
        {
            var run = _active[id];
            string folder = _folders.PathOf(id);
            try
            {
                await RunSimulatorAsync(folder, run);
            }
            catch (Exception ex)
            {
                run.AppendLine($"Nie udało się uruchomić symulatora: {ex.Message}");
                run.Finish(ok: false);
                File.WriteAllText(Path.Combine(folder, RunFiles.Console), run.Log);
            }
        }
    }

    // Odpowiednik polecenia `dotnet CellularAutomata.dll config.json` wpisanego w folderze uruchomienia.
    private async Task RunSimulatorAsync(string folder, ActiveRun run)
    {
        // DOTNET_HOST_PATH ustawia polecenie `dotnet run`; bez niego bierzemy `dotnet` z PATH.
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(SimulatorDll);
        startInfo.ArgumentList.Add(RunFiles.Config);

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => run.AppendLine(e.Data);
        process.ErrorDataReceived += (_, e) => run.AppendLine(e.Data);

        run.MarkStarted();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        process.WaitForExit(); // czeka, aż dojdą ostatnie linie wyjścia

        bool ok = process.ExitCode == 0 && File.Exists(Path.Combine(folder, RunFiles.Micro));
        run.Finish(ok);
        File.WriteAllText(Path.Combine(folder, RunFiles.Console), run.Log);
    }
}
