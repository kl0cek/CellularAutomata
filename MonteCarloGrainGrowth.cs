using System.Diagnostics;

namespace CellularAutomata;

public enum McProposal
{
    Random,
    Neighbour,
}

// Rozrost ziaren Monte Carlo.
public static class MonteCarloGrainGrowth
{
    public static int[] Generate(int sizeX, int sizeY, int sizeZ, int states, int mcSteps,
                                 Neighbourhood neighbourhood, Boundary boundary,
                                 double j, McProposal proposal, int seed,
                                 SimulationLog? log = null, Action<int, int[], double>? onStep = null)
    {
        // onStep (opcjonalny) dostaje po każdym kroku: numer MCS, siatkę i energię – używa go tryb demo.
        if (states < 2)
            throw new ArgumentException("Liczba stanów początkowych (States) musi wynosić co najmniej 2.");
        CaRules.Validate(sizeX, sizeY, sizeZ, states);
        if (mcSteps < 1)
            throw new ArgumentException("Liczba kroków MCS musi być dodatnia.");
        if (j <= 0)
            throw new ArgumentException("J musi być > 0.");

        var rng = new Random(seed);

        // Alokacja pamięci.
        long t = Stopwatch.GetTimestamp();
        var cells = new int[sizeX * sizeY * sizeZ];
        var order = new int[cells.Length];
        if (log != null)
            log.AllocationMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        // Stan początkowy: każda komórka dostaje losową orientację 1..Q.
        t = Stopwatch.GetTimestamp();
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = rng.Next(1, states + 1);
            order[i] = i;
        }
        if (log != null)
            log.InitMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        var offsets = CaRules.BuildOffsets(neighbourhood, is3D: sizeZ > 1);
        var neighbours = new int[offsets.Length]; // orientacje sąsiadów aktualnie rozpatrywanej komórki
        double energy = TotalEnergy(cells, sizeX, sizeY, sizeZ, offsets, boundary, j);
        log?.McSteps.Add(new McStepTime(0, 0, energy, 0));
        onStep?.Invoke(0, cells, energy);

        for (int step = 1; step <= mcSteps; step++)
        {
            long stepStart = Stopwatch.GetTimestamp();
            int accepted = 0;

            Shuffle(order, rng);

            foreach (int i in order)
            {
                int current = cells[i];
                int count = GetNeighbours(cells, i, sizeX, sizeY, sizeZ, offsets, boundary, neighbours);

                double energyBefore = j * CountDifferent(neighbours, count, current);

                if (energyBefore == 0)
                    continue;

                int candidate = proposal == McProposal.Random
                    ? rng.Next(1, states + 1)
                    : neighbours[rng.Next(count)];

                double energyAfter = j * CountDifferent(neighbours, count, candidate);
                double deltaE = energyAfter - energyBefore;

                if (deltaE <= 0 && candidate != current)
                {
                    cells[i] = candidate;
                    energy += deltaE;
                    accepted++;
                }
            }

            log?.McSteps.Add(new McStepTime(step, accepted, energy, Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds));
            onStep?.Invoke(step, cells, energy);
        }

        return cells;
    }

    private static double TotalEnergy(int[] cells, int sizeX, int sizeY, int sizeZ,
                                      (int Dx, int Dy, int Dz)[] offsets, Boundary boundary, double j)
    {
        var neighbours = new int[offsets.Length];
        long different = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            int count = GetNeighbours(cells, i, sizeX, sizeY, sizeZ, offsets, boundary, neighbours);
            different += CountDifferent(neighbours, count, cells[i]);
        }
        return j * different / 2;
    }

    // Wpisuje orientacje sąsiadów komórki i do tablicy neighbours; zwraca, ilu ich jest.
    private static int GetNeighbours(int[] cells, int i, int sizeX, int sizeY, int sizeZ,
                                     (int Dx, int Dy, int Dz)[] offsets, Boundary boundary, int[] neighbours)
    {
        int x = i % sizeX;
        int y = i / sizeX % sizeY;
        int z = i / (sizeX * sizeY);
        int count = 0;

        foreach (var (dx, dy, dz) in offsets)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (CaRules.ApplyBoundary(ref nx, sizeX, boundary) &&
                CaRules.ApplyBoundary(ref ny, sizeY, boundary) &&
                CaRules.ApplyBoundary(ref nz, sizeZ, boundary))
                neighbours[count++] = cells[nx + sizeX * (ny + sizeY * nz)];
        }
        return count;
    }

    private static int CountDifferent(int[] neighbours, int count, int state)
    {
        int different = 0;
        for (int k = 0; k < count; k++)
            if (neighbours[k] != state)
                different++;
        return different;
    }

    // Tasowanie Fishera–Yatesa: losowa kolejność odwiedzania komórek w każdym kroku MCS.
    private static void Shuffle(int[] array, Random rng)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int k = rng.Next(i + 1);
            (array[i], array[k]) = (array[k], array[i]);
        }
    }
}
