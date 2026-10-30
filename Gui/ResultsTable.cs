using System.Globalization;
using System.Text;

namespace CellularAutomata.Gui;

// Zestawienie wyników wszystkich zakończonych uruchomień – dane do wykresów porównawczych i pliku CSV.
public static class ResultsTable
{
    public static List<RunResult> Collect(RunFolders folders, Func<string, string> statusOf)
    {
        var results = new List<RunResult>();
        foreach (string id in folders.Ids())
        {
            if (statusOf(id) != "done")
                continue;

            string folder = folders.PathOf(id);
            var c = RunFolders.ReadConfig(folder);
            var s = RunFolders.ReadTimeSummary(folder);
            if (c == null || s == null)
                continue;

            results.Add(new RunResult(
                Id: id,
                Series: c.Text("Series"),
                Algorithm: c.Text("Algorithm") ?? "CA",
                SizeX: c.Int("FullSpaceSizeX"), SizeY: c.Int("FullSpaceSizeY"), SizeZ: c.Int("FullSpaceSizeZ"),
                Cells: (long)s.Number("cells"),
                Neighbourhood: c.Text("Neighbourhood") ?? "",
                Boundary: c.Text("BC") ?? "",
                Nucleons: c.Int("Nucleons"),
                States: c.Int("States"),
                McSteps: c.Int("McSteps"),
                McProposal: c.Text("McProposal"),
                Seed: c.Int("Seed"),
                Steps: (int)s.Number("steps"),
                GrainsEnd: (int)s.Number("grains_end"),
                SimulationMs: s.Number("simulation_ms"),
                GrowthMs: s.Number("growth_ms"),
                FileWriteMs: s.Number("file_write_ms"),
                FileMb: s.Number("file_mb"),
                TotalMs: s.Number("total_ms")));
        }
        return results;
    }

    // CSV ze średnikami i kropką dziesiętną – do Excela albo Pythona.
    public static string ToCsv(IEnumerable<RunResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("id;series;algorithm;sizeX;sizeY;sizeZ;cells;neighbourhood;bc;nucleons;states;mc_steps;mc_proposal;seed;" +
                      "steps;grains_end;simulation_ms;growth_ms;file_write_ms;file_mb;total_ms");
        foreach (var r in results)
        {
            sb.AppendLine(string.Join(';',
                r.Id, r.Series, r.Algorithm, r.SizeX, r.SizeY, r.SizeZ, r.Cells, r.Neighbourhood, r.Boundary,
                r.Nucleons, r.States, r.McSteps, r.McProposal, r.Seed, r.Steps, r.GrainsEnd,
                Invariant(r.SimulationMs), Invariant(r.GrowthMs), Invariant(r.FileWriteMs), Invariant(r.FileMb), Invariant(r.TotalMs)));
        }
        return sb.ToString();
    }

    private static string Invariant(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
