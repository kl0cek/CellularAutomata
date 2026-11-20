using System.Diagnostics;

namespace CellularAutomata;

// Rozrost ziaren CA – wersja zoptymalizowana (Lab 8): liczymy tylko front, czyli puste komórki,
// które stykają się z ziarnem. Wersja naiwna w każdym kroku sprawdza wszystkich sąsiadów każdej pustej
// komórki, także tych daleko od ziaren, które i tak zostaną puste.
//
// Pusta komórka z sąsiadem-ziarnem zawsze zapełnia się w najbliższym kroku, więc front w kroku s to puste
// komórki sąsiadujące z komórkami zapełnionymi w kroku s - 1 (w kroku 1 – z zarodkami).
//
// Mikrostruktura jest identyczna jak w VectorGrainGrowth: front liczymy w kolejności indeksów, więc
// losowanie przy remisie (PickMostFrequent) dostaje liczby z generatora w tej samej kolejności.
// Aktualizacja zostaje synchroniczna: nowe ID zbieramy w liście i wpisujemy do siatki dopiero po kroku,
// więc nie trzeba drugiej tablicy ani kopiowania zajętych komórek.
public static class FrontierGrainGrowth
{
    public static (int[] Cells, int Steps) Generate(int sizeX, int sizeY, int sizeZ, int nuclei,
                                                     Neighbourhood neighbourhood, Boundary boundary, int seed,
                                                     SimulationLog? log = null)
    {
        CaRules.Validate(sizeX, sizeY, sizeZ, nuclei);
        var rng = new Random(seed);

        // Alokacja pamięci: siatka i znaczniki frontu (1 B na komórkę zamiast drugiej siatki 4 B).
        long t = Stopwatch.GetTimestamp();
        var cells = new int[sizeX * sizeY * sizeZ];
        var inFront = new bool[cells.Length];
        if (log != null)
            log.AllocationMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        // Zarodkowanie – te same pozycje co w wersji naiwnej.
        t = Stopwatch.GetTimestamp();
        var positions = CaRules.PickNucleiPositions(cells.Length, nuclei, rng);
        for (int n = 0; n < nuclei; n++)
            cells[positions[n]] = n + 1;
        if (log != null)
            log.InitMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        var index = new NeighbourIndex(sizeX, sizeY, sizeZ, neighbourhood, boundary);
        var neighbours = new int[index.Count];
        var ids = new int[index.Count];
        var hits = new int[index.Count];

        // Komórki zapełnione w poprzednim kroku i zmiany z bieżącego kroku (indeks + nowe ID).
        var filled = new List<int>(positions);
        var changedCells = new List<int>();
        var changedIds = new List<int>();

        int emptyCells = cells.Length - nuclei;
        int steps = 0;

        while (emptyCells > 0)
        {
            long stepStart = Stopwatch.GetTimestamp();

            // 1. Front: puste komórki obok świeżo zapełnionych. Zakres [first, last] ogranicza przegląd znaczników.
            int first = int.MaxValue, last = -1;
            foreach (int c in filled)
            {
                int count = index.Fill(c, neighbours);
                for (int k = 0; k < count; k++)
                {
                    int n = neighbours[k];
                    if (cells[n] == CaRules.Empty && !inFront[n])
                    {
                        inFront[n] = true;
                        if (n < first) first = n;
                        if (n > last) last = n;
                    }
                }
            }

            // 2. Nowe ID dla komórek frontu, w kolejności indeksów (jak pętla z, y, x w wersji naiwnej).
            changedCells.Clear();
            changedIds.Clear();
            for (int i = first; i <= last; i++)
            {
                if (!inFront[i])
                    continue;
                inFront[i] = false;

                int distinct = 0;
                int count = index.Fill(i, neighbours);
                for (int k = 0; k < count; k++)
                    CaRules.CountNeighbour(cells[neighbours[k]], ids, hits, ref distinct);

                changedCells.Add(i);
                changedIds.Add(CaRules.PickMostFrequent(ids, hits, distinct, rng));
            }

            // 3. Aktualizacja synchroniczna: wpisujemy wszystkie zmiany naraz.
            for (int k = 0; k < changedCells.Count; k++)
                cells[changedCells[k]] = changedIds[k];

            int changed = changedCells.Count;
            (filled, changedCells) = (changedCells, filled);
            emptyCells -= changed;
            steps++;
            log?.Steps.Add(new StepTime(steps, changed, emptyCells, Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds));

            if (changed == 0)
                break; // zabezpieczenie: nic już nie może urosnąć
        }

        return (cells, steps);
    }
}
