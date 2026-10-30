using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CellularAutomata.Gui;

// Foldery uruchomień na dysku: nazwy run_…, odczyt config.json i Time.txt.
public sealed partial class RunFolders
{
    private readonly object _idLock = new();
    private DateTime _lastIdTime = DateTime.MinValue;

    public RunFolders(string root) => Root = root;

    public string Root { get; }

    public string PathOf(string id) => Path.Combine(Root, id);

    // Folder istniejącego uruchomienia albo null. Identyfikator musi mieć dokładnie format run_… –
    // inaczej z adresu dałoby się wyjść poza folder wyników.
    public string? Find(string id)
    {
        if (!IdPattern().IsMatch(id))
            return null;
        string folder = PathOf(id);
        return Directory.Exists(folder) ? folder : null;
    }

    // Tworzy folder nowego uruchomienia: run_RRRRMMDD_GGMMSS_mmm. Przy serii kilka folderów powstaje
    // w tej samej milisekundzie, więc każdy kolejny identyfikator jest o co najmniej 1 ms późniejszy.
    public string Create()
    {
        string id;
        lock (_idLock)
        {
            var now = DateTime.Now;
            _lastIdTime = now > _lastIdTime.AddMilliseconds(1) ? now : _lastIdTime.AddMilliseconds(1);
            id = "run_" + _lastIdTime.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        }
        Directory.CreateDirectory(PathOf(id));
        return id;
    }

    // Identyfikatory wszystkich uruchomień, od najnowszego.
    public IEnumerable<string> Ids()
    {
        if (!Directory.Exists(Root))
            return Enumerable.Empty<string>();

        return Directory.GetDirectories(Root)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => IdPattern().IsMatch(name))
            .OrderByDescending(name => name, StringComparer.Ordinal);
    }

    // Stan uruchomienia z poprzedniej sesji serwera – oceniany po plikach w folderze.
    public static string StatusFromFiles(string folder)
    {
        if (File.Exists(Path.Combine(folder, RunFiles.Micro)))
            return "done";
        return File.Exists(Path.Combine(folder, RunFiles.Console)) ? "failed" : "unknown";
    }

    public static Dictionary<string, object>? ReadConfig(string folder)
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
    public static Dictionary<string, string>? ReadTimeSummary(string folder)
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
}

// Odczyt pojedynczych wartości z config.json (słownik z JSON-a) i z Time.txt (same napisy).
public static class RunValues
{
    public static string? Text(this Dictionary<string, object> config, string key) =>
        config.TryGetValue(key, out var value) ? value?.ToString() : null;

    public static int? Int(this Dictionary<string, object> config, string key) =>
        int.TryParse(config.Text(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    public static double Number(this Dictionary<string, string> summary, string key) =>
        double.TryParse(summary.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
}
