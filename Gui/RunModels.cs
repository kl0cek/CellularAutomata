namespace CellularAutomata.Gui;

// Pozycja na liście uruchomień.
public sealed record RunSummary(string Id, string Status, string? Algorithm, string? Size, string? Series,
                                string? GrainsEnd, string? SimulationMs);

// Szczegóły jednego uruchomienia: config.json, ostatnia linia Time.txt i wyjście konsoli.
public sealed record RunDetails(
    string Id, string Status, string Folder, double? ElapsedSeconds,
    Dictionary<string, object>? Config, Dictionary<string, string>? Summary, string Log);

// Wynik zakończonego uruchomienia – wiersz zestawienia i pliku CSV.
public sealed record RunResult(
    string Id, string? Series, string Algorithm, int? SizeX, int? SizeY, int? SizeZ, long Cells,
    string Neighbourhood, string Boundary, int? Nucleons, int? States, int? McSteps, string? McProposal, int? Seed,
    int Steps, int GrainsEnd, double SimulationMs, double GrowthMs, double FileWriteMs, double FileMb, double TotalMs);
