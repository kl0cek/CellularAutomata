namespace CellularAutomata;

// Rozrost ziaren CA – siatka jako macierz trójwymiarowa int[z, y, x].
public static class MatrixGrainGrowth
{
    public static (int[,,] Cells, int Steps) Generate(int sizeX, int sizeY, int sizeZ, int nuclei,
                                                       Neighbourhood neighbourhood, Boundary boundary, int seed)
    {
        CaRules.Validate(sizeX, sizeY, sizeZ, nuclei);
        var rng = new Random(seed);
        var cells = new int[sizeZ, sizeY, sizeX];
        var next = new int[sizeZ, sizeY, sizeX];

        // Zarodkowanie: te same pozycje co w wersji wektorowej, przeliczone na (x, y, z).
        var positions = CaRules.PickNucleiPositions(cells.Length, nuclei, rng);
        for (int n = 0; n < nuclei; n++)
        {
            int index = positions[n];
            int x = index % sizeX;
            int y = index / sizeX % sizeY;
            int z = index / (sizeX * sizeY);
            cells[z, y, x] = n + 1;
        }

        // Rozrost: krok po kroku, aż nie zostanie żadna pusta komórka.
        var offsets = CaRules.BuildOffsets(neighbourhood, is3D: sizeZ > 1);
        var ids = new int[offsets.Length];
        var hits = new int[offsets.Length];
        int emptyCells = cells.Length - nuclei;
        int steps = 0;

        while (emptyCells > 0)
        {
            int changed = 0;

            for (int z = 0; z < sizeZ; z++)
                for (int y = 0; y < sizeY; y++)
                    for (int x = 0; x < sizeX; x++)
                    {
                        if (cells[z, y, x] != CaRules.Empty)
                        {
                            next[z, y, x] = cells[z, y, x];
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
                                CaRules.CountNeighbour(cells[nz, ny, nx], ids, hits, ref distinct);
                        }

                        next[z, y, x] = CaRules.PickMostFrequent(ids, hits, distinct, rng);
                        if (next[z, y, x] != CaRules.Empty)
                            changed++;
                    }

            // Aktualizacja synchroniczna: nowy stan liczony ze starego, potem zamiana tablic.
            (cells, next) = (next, cells);
            emptyCells -= changed;
            steps++;

            if (changed == 0)
                break; // zabezpieczenie: nic już nie może urosnąć
        }

        return (cells, steps);
    }
}
