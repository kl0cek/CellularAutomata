using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace CellularAutomata.Gui;

// Uruchomienia symulatora: każde dostaje własny folder (config.json + wyniki),
// a symulator startuje jako osobny proces – dokładnie tak, jak wersja serwerowa z terminala.
// Zlecenia trafiają do kolejki i liczą się po jednym, żeby czasy z serii były porównywalne.
public sealed partial class RunManager
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private readonly ConcurrentDictionary<string, ActiveRun> _active = new();
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
    private readonly object _idLock = new();
    private DateTime _lastIdTime = DateTime.MinValue;

    public string SimulatorDll { get; }
    public string RunsDirectory { get; }

    public RunManager(string simulatorDll, string runsDirectory)
    {
        SimulatorDll = simulatorDll;
        RunsDirectory = runsDirectory;
        _ = Task.Run(ProcessQueueAsync);
    }

    public bool SimulatorFound => File.Exists(SimulatorDll);

    // Tworzy folder z config.json i dopisuje uruchomienie do kolejki. series łączy uruchomienia jednej serii.
    public string Enqueue(RunRequest request, string? series = null)
    {
        string id = NewId();
        string folder = Path.Combine(RunsDirectory, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, RunFiles.Config),
            JsonSerializer.Serialize(request.ToSimulatorConfig(series), Indented));

        _active[id] = new ActiveRun();
        _queue.Writer.TryWrite(id);
        return id;
    }

    // Wszystkie uruchomienia z dysku, od najnowszego.
    public List<RunSummary> List() =>
        RunIds().Take(200).Select(Summarize).ToList();

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

    // Wyniki wszystkich zakończonych uruchomień – dane do zbiorczych wykresów i pliku CSV.
    public List<RunResult> Results()
    {
        var results = new List<RunResult>();
        foreach (string id in RunIds())
        {
            string folder = Path.Combine(RunsDirectory, id);
            _active.TryGetValue(id, out var active);
            if (StatusOf(folder, active) != "done")
                continue;

            var c = ReadConfig(folder);
            var s = ReadTimeSummary(folder);
            if (c == null || s == null)
                continue;

            results.Add(new RunResult(
                Id: id,
                Series: Text(c, "Series"),
                Algorithm: Text(c, "Algorithm") ?? "CA",
                SizeX: Int(c, "FullSpaceSizeX"), SizeY: Int(c, "FullSpaceSizeY"), SizeZ: Int(c, "FullSpaceSizeZ"),
                Cells: (long)Number(s, "cells"),
                Neighbourhood: Text(c, "Neighbourhood") ?? "",
                Boundary: Text(c, "BC") ?? "",
                Nucleons: Int(c, "Nucleons"),
                States: Int(c, "States"),
                McSteps: Int(c, "McSteps"),
                McProposal: Text(c, "McProposal"),
                Seed: Int(c, "Seed"),
                Steps: (int)Number(s, "steps"),
                GrainsEnd: (int)Number(s, "grains_end"),
                SimulationMs: Number(s, "simulation_ms"),
                GrowthMs: Number(s, "growth_ms"),
                FileWriteMs: Number(s, "file_write_ms"),
                FileMb: Number(s, "file_mb"),
                TotalMs: Number(s, "total_ms")));
        }
        return results;
    }

    // Te same wyniki jako CSV (średniki, kropka dziesiętna) – do Excela albo Pythona.
    public string ResultsCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("id;series;algorithm;sizeX;sizeY;sizeZ;cells;neighbourhood;bc;nucleons;states;mc_steps;mc_proposal;seed;" +
                      "steps;grains_end;simulation_ms;growth_ms;file_write_ms;file_mb;total_ms");
        foreach (var r in Results())
        {
            sb.AppendLine(string.Join(';',
                r.Id, r.Series, r.Algorithm, r.SizeX, r.SizeY, r.SizeZ, r.Cells, r.Neighbourhood, r.Boundary,
                r.Nucleons, r.States, r.McSteps, r.McProposal, r.Seed, r.Steps, r.GrainsEnd,
                Invariant(r.SimulationMs), Invariant(r.GrowthMs), Invariant(r.FileWriteMs), Invariant(r.FileMb), Invariant(r.TotalMs)));
        }
        return sb.ToString();
    }

    // Ścieżka do pliku wynikowego danego uruchomienia albo null, gdy nie istnieje.
    public string? FileOf(string id, string fileName)
    {
        string? folder = FolderOf(id);
        string? path = folder == null ? null : Path.Combine(folder, fileName);
        return path != null && File.Exists(path) ? path : null;
    }

    // Kolejka: zlecenia liczą się po kolei, jedno po drugim.
    private async Task ProcessQueueAsync()
    {
        await foreach (string id in _queue.Reader.ReadAllAsync())
        {
            var run = _active[id];
            string folder = Path.Combine(RunsDirectory, id);
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

    // Folder na wyniki: run_RRRRMMDD_GGMMSS_mmm. Przy serii kilka folderów powstaje w tej samej
    // milisekundzie, więc każdy kolejny identyfikator jest o co najmniej 1 ms późniejszy.
    private string NewId()
    {
        lock (_idLock)
        {
            var now = DateTime.Now;
            _lastIdTime = now > _lastIdTime.AddMilliseconds(1) ? now : _lastIdTime.AddMilliseconds(1);
            return "run_" + _lastIdTime.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        }
    }

    private IEnumerable<string> RunIds()
    {
        if (!Directory.Exists(RunsDirectory))
            return Enumerable.Empty<string>();

        return Directory.GetDirectories(RunsDirectory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => IdPattern().IsMatch(name))
            .OrderByDescending(name => name, StringComparer.Ordinal);
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
            Algorithm: config == null ? null : Text(config, "Algorithm"),
            Size: config == null ? null
                : $"{Int(config, "FullSpaceSizeX")}×{Int(config, "FullSpaceSizeY")}×{Int(config, "FullSpaceSizeZ")}",
            Series: config == null ? null : Text(config, "Series"),
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

    private static string? Text(Dictionary<string, object> config, string key) =>
        config.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static int? Int(Dictionary<string, object> config, string key) =>
        int.TryParse(Text(config, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    private static double Number(Dictionary<string, string> summary, string key) =>
        double.TryParse(summary.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;

    private static string Invariant(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^run_\d{8}_\d{6}_\d{3}$")]
    private static partial Regex IdPattern();

    // Stan uruchomienia zleconego w tej sesji serwera: w kolejce → trwa → gotowe / błąd.
    private sealed class ActiveRun
    {
        private readonly StringBuilder _log = new();
        private readonly Stopwatch _clock = new();
        private double? _finishedSeconds;

        public string Status { get; private set; } = "queued";
        public double? ElapsedSeconds => Status == "queued" ? null : _finishedSeconds ?? _clock.Elapsed.TotalSeconds;

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

        public void MarkStarted()
        {
            Status = "running";
            _clock.Start();
        }

        public void Finish(bool ok)
        {
            _finishedSeconds = _clock.Elapsed.TotalSeconds;
            Status = ok ? "done" : "failed";
        }
    }
}

public sealed record RunSummary(string Id, string Status, string? Algorithm, string? Size, string? Series,
                                string? GrainsEnd, string? SimulationMs);

public sealed record RunDetails(
    string Id, string Status, string Folder, double? ElapsedSeconds,
    Dictionary<string, object>? Config, Dictionary<string, string>? Summary, string Log);

public sealed record RunResult(
    string Id, string? Series, string Algorithm, int? SizeX, int? SizeY, int? SizeZ, long Cells,
    string Neighbourhood, string Boundary, int? Nucleons, int? States, int? McSteps, string? McProposal, int? Seed,
    int Steps, int GrainsEnd, double SimulationMs, double GrowthMs, double FileWriteMs, double FileMb, double TotalMs);
