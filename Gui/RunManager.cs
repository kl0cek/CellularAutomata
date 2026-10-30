using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CellularAutomata.Gui;

// Uruchomienia symulatora: każde dostaje własny folder (config.json + wyniki),
// a symulator startuje jako osobny proces – dokładnie tak, jak wersja serwerowa z terminala.
public sealed partial class RunManager
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private readonly ConcurrentDictionary<string, ActiveRun> _active = new();

    public string SimulatorDll { get; }
    public string RunsDirectory { get; }

    public RunManager(string simulatorDll, string runsDirectory)
    {
        SimulatorDll = simulatorDll;
        RunsDirectory = runsDirectory;
    }

    public bool SimulatorFound => File.Exists(SimulatorDll);

    public string Start(RunRequest request)
    {
        // Folder na wyniki: run_RRRRMMDD_GGMMSS_mmm, sortuje się chronologicznie.
        string id = "run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        string folder = Path.Combine(RunsDirectory, id);
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, RunFiles.Config),
            JsonSerializer.Serialize(request.ToSimulatorConfig(), Indented));

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

        var run = new ActiveRun();
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => run.AppendLine(e.Data);
        process.ErrorDataReceived += (_, e) => run.AppendLine(e.Data);
        process.Exited += (_, _) =>
        {
            process.WaitForExit(); // czeka, aż dojdą ostatnie linie wyjścia
            bool ok = process.ExitCode == 0 && File.Exists(Path.Combine(folder, RunFiles.Micro));
            run.Finish(ok);
            File.WriteAllText(Path.Combine(folder, RunFiles.Console), run.Log);
            process.Dispose();
        };

        _active[id] = run;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return id;
    }

    // Wszystkie uruchomienia z dysku, od najnowszego.
    public List<RunSummary> List()
    {
        if (!Directory.Exists(RunsDirectory))
            return new List<RunSummary>();

        return Directory.GetDirectories(RunsDirectory)
            .Select(Path.GetFileName)
            .Where(name => name != null && IdPattern().IsMatch(name))
            .OrderByDescending(name => name, StringComparer.Ordinal)
            .Take(50)
            .Select(id => Summarize(id!))
            .ToList();
    }

    public RunDetails? Describe(string id)
    {
        string? folder = FolderOf(id);
        if (folder == null)
            return null;

        _active.TryGetValue(id, out var active);
        string logFile = Path.Combine(folder, RunFiles.Console);
        string log = active?.Log ?? (File.Exists(logFile) ? File.ReadAllText(logFile) : "");

        return new RunDetails(
            Id: id,
            Status: StatusOf(folder, active),
            Folder: folder,
            ElapsedSeconds: active?.ElapsedSeconds,
            Config: ReadConfig(folder),
            Summary: ReadTimeSummary(folder),
            Log: log);
    }

    // Ścieżka do pliku wynikowego danego uruchomienia albo null, gdy nie istnieje.
    public string? FileOf(string id, string fileName)
    {
        string? folder = FolderOf(id);
        string? path = folder == null ? null : Path.Combine(folder, fileName);
        return path != null && File.Exists(path) ? path : null;
    }

    private RunSummary Summarize(string id)
    {
        string folder = Path.Combine(RunsDirectory, id);
        _active.TryGetValue(id, out var active);
        var config = ReadConfig(folder);
        var summary = ReadTimeSummary(folder);

        return new RunSummary(
            Id: id,
            Status: StatusOf(folder, active),
            Algorithm: config?.GetValueOrDefault("Algorithm")?.ToString(),
            Size: config == null ? null
                : $"{config.GetValueOrDefault("FullSpaceSizeX")}×{config.GetValueOrDefault("FullSpaceSizeY")}×{config.GetValueOrDefault("FullSpaceSizeZ")}",
            GrainsEnd: summary?.GetValueOrDefault("grains_end"),
            SimulationMs: summary?.GetValueOrDefault("simulation_ms"));
    }

    // Identyfikator z adresu musi mieć dokładnie format run_… – inaczej dałoby się wyjść poza folder wyników.
    private string? FolderOf(string id)
    {
        if (!IdPattern().IsMatch(id))
            return null;
        string folder = Path.Combine(RunsDirectory, id);
        return Directory.Exists(folder) ? folder : null;
    }

    private static string StatusOf(string folder, ActiveRun? active)
    {
        if (active != null)
            return active.Status;
        // Uruchomienie z poprzedniej sesji serwera: oceniamy po plikach.
        if (File.Exists(Path.Combine(folder, RunFiles.Micro)))
            return "done";
        return File.Exists(Path.Combine(folder, RunFiles.Console)) ? "failed" : "unknown";
    }

    private static Dictionary<string, object>? ReadConfig(string folder)
    {
        string path = Path.Combine(folder, RunFiles.Config);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Ostatnia linia Time.txt jako słownik nazwa kolumny → wartość.
    private static Dictionary<string, string>? ReadTimeSummary(string folder)
    {
        string path = Path.Combine(folder, RunFiles.Time);
        if (!File.Exists(path))
            return null;

        var lines = File.ReadAllLines(path).Where(l => l.Length > 0).ToArray();
        if (lines.Length < 2)
            return null;

        var header = lines[0].Split(';');
        var values = lines[^1].Split(';');
        return header.Zip(values).ToDictionary(p => p.First, p => p.Second);
    }

    [GeneratedRegex(@"^run_\d{8}_\d{6}_\d{3}$")]
    private static partial Regex IdPattern();

    // Stan uruchomienia, które wystartowało w tej sesji serwera.
    private sealed class ActiveRun
    {
        private readonly StringBuilder _log = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double? _finishedSeconds;

        public string Status { get; private set; } = "running";
        public double ElapsedSeconds => _finishedSeconds ?? _clock.Elapsed.TotalSeconds;

        public string Log
        {
            get { lock (_log) return _log.ToString(); }
        }

        public void AppendLine(string? line)
        {
            if (line == null)
                return;
            lock (_log)
                _log.AppendLine(line);
        }

        public void Finish(bool ok)
        {
            _finishedSeconds = _clock.Elapsed.TotalSeconds;
            Status = ok ? "done" : "failed";
        }
    }
}

public sealed record RunSummary(string Id, string Status, string? Algorithm, string? Size, string? GrainsEnd, string? SimulationMs);

public sealed record RunDetails(
    string Id, string Status, string Folder, double? ElapsedSeconds,
    Dictionary<string, object>? Config, Dictionary<string, string>? Summary, string Log);
