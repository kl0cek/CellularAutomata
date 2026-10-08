using System.Diagnostics;

namespace CellularAutomata;

// Rozrost ziaren CA – siatka jako wektor (jednowymiarowa tablica int[]).
// Komórka (x, y, z) leży pod indeksem x + sizeX * (y + sizeY * z).
public static class VectorGrainGrowth
{
    // log (opcjonalny) dostaje czasy: alokacji, zarodkowania i każdego kroku.
    public static (int[] Cells, int Steps) Generate(int sizeX, int sizeY, int sizeZ, int nuclei,
                                                     Neighbourhood neighbourhood, Boundary boundary, int seed,
                                                     SimulationLog? log = null)
    {
        CaRules.Validate(sizeX, sizeY, sizeZ, nuclei);
        var rng = new Random(seed);

        // Alokacja pamięci na dwie siatki (stan bieżący i następny).
        long t = Stopwatch.GetTimestamp();
        var cells = new int[sizeX * sizeY * sizeZ];
        var next = new int[cells.Length];
        if (log != null)
            log.AllocationMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        // Zarodkowanie: zarodek n dostaje ID n + 1.
        t = Stopwatch.GetTimestamp();
        var positions = CaRules.PickNucleiPositions(cells.Length, nuclei, rng);
        for (int n = 0; n < nuclei; n++)
            cells[positions[n]] = n + 1;
        if (log != null)
            log.NucleationMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        // Rozrost: krok po kroku, aż nie zostanie żadna pusta komórka.
        var offsets = CaRules.BuildOffsets(neighbourhood, is3D: sizeZ > 1);
        var ids = new int[offsets.Length];
        var hits = new int[offsets.Length];
        int emptyCells = cells.Length - nuclei;
        int steps = 0;

        while (emptyCells > 0)
        {
            long stepStart = Stopwatch.GetTimestamp();
            int changed = 0;

            for (int z = 0; z < sizeZ; z++)
                for (int y = 0; y < sizeY; y++)
                    for (int x = 0; x < sizeX; x++)
                    {
                        int i = x + sizeX * (y + sizeY * z);
                        if (cells[i] != CaRules.Empty)
                        {
                            next[i] = cells[i];
                            continue;
                        }

                        // Pusta komórka przejmuje ID najczęstsze wśród sąsiadów.
                        int distinct = 0;
                        foreach (var (dx, dy, dz) in offsets)
                        {
                            int nx = x + dx, ny = y + dy, nz = z + dz;
                            if (CaRules.ApplyBoundary(ref nx, sizeX, boundary) &&
                                CaRules.ApplyBoundary(ref ny, sizeY, boundary) &&
                                CaRules.ApplyBoundary(ref nz, sizeZ, boundary))
                                CaRules.CountNeighbour(cells[nx + sizeX * (ny + sizeY * nz)], ids, hits, ref distinct);
                        }

                        next[i] = CaRules.PickMostFrequent(ids, hits, distinct, rng);
                        if (next[i] != CaRules.Empty)
                            changed++;
                    }

            // Aktualizacja synchroniczna: nowy stan liczony ze starego, potem zamiana tablic.
            (cells, next) = (next, cells);
            emptyCells -= changed;
            steps++;
            log?.Steps.Add(new StepTime(steps, changed, emptyCells, Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds));

            if (changed == 0)
                break; // zabezpieczenie: nic już nie może urosnąć
        }

        return (cells, steps);
    }
}
