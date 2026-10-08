namespace CellularAutomata;

// Pomiary czasu z wnętrza symulacji: alokacja pamięci, zarodkowanie i każdy krok osobno.
public sealed class SimulationLog
{
    public double AllocationMs { get; set; }
    public double NucleationMs { get; set; }
    public List<StepTime> Steps { get; } = new();

    public double GrowthMs => Steps.Sum(s => s.Ms);
}

// Jeden krok rozrostu: ile komórek się zapełniło, ile zostało pustych i ile trwał (delta czasu).
public readonly record struct StepTime(int Step, int Changed, int EmptyLeft, double Ms);
