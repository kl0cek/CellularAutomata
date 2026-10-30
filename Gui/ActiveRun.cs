using System.Diagnostics;
using System.Text;

namespace CellularAutomata.Gui;

// Stan uruchomienia zleconego w tej sesji serwera: w kolejce → trwa → gotowe / błąd.
// Uruchomienia z wcześniejszych sesji ocenia się po plikach (RunFolders.StatusFromFiles).
internal sealed class ActiveRun
{
    private readonly StringBuilder _log = new();
    private readonly Stopwatch _clock = new();
    private double? _finishedSeconds;

    public string Status { get; private set; } = "queued";

    public double? ElapsedSeconds =>
        Status == "queued" ? null : _finishedSeconds ?? _clock.Elapsed.TotalSeconds;

    // Wyjście konsoli symulatora zebrane do tej pory.
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
