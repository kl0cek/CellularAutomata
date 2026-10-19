namespace CellularAutomata;

// Pomiary czasu z wnętrza symulacji: alokacja pamięci, stan początkowy i każdy krok osobno.
public sealed class SimulationLog
{
    public double AllocationMs { get; set; }
    public double InitMs { get; set; }                     // CA: zarodkowanie, MC: losowanie orientacji
    public List<StepTime> Steps { get; } = new();          // kroki CA
    public List<McStepTime> McSteps { get; } = new();      // kroki MCS (pozycja 0 = stan początkowy)

    public double GrowthMs => Steps.Sum(s => s.Ms) + McSteps.Sum(s => s.Ms);
}

// Jeden krok CA: ile komórek się zapełniło, ile zostało pustych i ile trwał (delta czasu).
public readonly record struct StepTime(int Step, int Changed, int EmptyLeft, double Ms);

// Jeden krok MCS: ile zmian orientacji przyjęto, energia siatki po kroku i ile trwał.
public readonly record struct McStepTime(int Step, int Accepted, double Energy, double Ms);
