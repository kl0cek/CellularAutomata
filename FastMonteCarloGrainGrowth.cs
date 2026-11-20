using System.Diagnostics;
using System.Runtime.Intrinsics;

namespace CellularAutomata;

// Rozrost ziaren Monte Carlo – wersja zoptymalizowana (Lab 8).
// Daje identyczną mikrostrukturę i energię jak MonteCarloGrainGrowth: ten sam generator, to samo tasowanie
// i te same losowania w tej samej kolejności. Różnice:
//   - sąsiedzi przez NeighbourIndex (komórka wewnętrzna: i + przesunięcie, bez warunków brzegowych),
//   - stany sąsiadów zbierane raz do małego bufora (dla komórki wewnętrznej wprost z siatki, bez tablicy indeksów),
//   - zliczanie wektorowe: 4 porównania naraz (SIMD), bez skoków warunkowych,
//   - gdy kandydat jest równy obecnemu stanowi, energii po zmianie nie liczymy (zmiana i tak byłaby odrzucona).
public static class FastMonteCarloGrainGrowth
{
    public static int[] Generate(int sizeX, int sizeY, int sizeZ, int states, int mcSteps,
                                 Neighbourhood neighbourhood, Boundary boundary,
                                 double j, McProposal proposal, int seed,
                                 SimulationLog? log = null)
    {
        MonteCarloGrainGrowth.Validate(sizeX, sizeY, sizeZ, states, mcSteps, j);

        var rng = new Random(seed);

        // Alokacja pamięci.
        long t = Stopwatch.GetTimestamp();
        var cells = new int[sizeX * sizeY * sizeZ];
        var order = new int[cells.Length];
        if (log != null)
            log.AllocationMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        // Stan początkowy: każda komórka dostaje losową orientację 1..Q (jak w wersji naiwnej).
        t = Stopwatch.GetTimestamp();
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = rng.Next(1, states + 1);
            order[i] = i;
        }
        if (log != null)
            log.InitMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        var index = new NeighbourIndex(sizeX, sizeY, sizeZ, neighbourhood, boundary);
        var neighbours = NewStateBuffer(index); // stany sąsiadów aktualnie rozpatrywanej komórki
        double energy = TotalEnergy(cells, index, j);
        log?.McSteps.Add(new McStepTime(0, 0, energy, 0));

        for (int step = 1; step <= mcSteps; step++)
        {
            long stepStart = Stopwatch.GetTimestamp();
            int accepted = 0;

            MonteCarloGrainGrowth.Shuffle(order, rng);

            foreach (int i in order)
            {
                int current = cells[i];
                int count = Gather(cells, index, i, neighbours);

                int diffBefore = count - CountSame(neighbours, current);
                if (diffBefore == 0)
                    continue; // komórka wewnątrz ziarna – zmiana tylko zwiększyłaby energię

                int candidate = proposal == McProposal.Random
                    ? rng.Next(1, states + 1)
                    : neighbours[rng.Next(count)];
                if (candidate == current)
                    continue; // nic się nie zmienia

                int diffAfter = count - CountSame(neighbours, candidate);
                double deltaE = j * diffAfter - j * diffBefore; // to samo wyrażenie co w wersji naiwnej

                if (deltaE <= 0)
                {
                    cells[i] = candidate;
                    energy += deltaE;
                    accepted++;
                }
            }

            log?.McSteps.Add(new McStepTime(step, accepted, energy, Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds));
        }

        return cells;
    }

    private static double TotalEnergy(int[] cells, NeighbourIndex index, double j)
    {
        var neighbours = NewStateBuffer(index);
        long different = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            int count = Gather(cells, index, i, neighbours);
            different += count - CountSame(neighbours, cells[i]);
        }
        return j * different / 2;
    }

    // Wartość w nieużytych miejscach bufora – żaden stan MC (1..Q) nie jest jej równy.
    private const int Padding = int.MinValue;

    // Bufor na stany sąsiadów zaokrąglony w górę do wielokrotności 4 (26 → 28), żeby liczyć całymi wektorami.
    // Miejsca za ostatnim sąsiadem (26, 27) nigdy nie są nadpisywane, więc Padding wpisujemy raz.
    private static int[] NewStateBuffer(NeighbourIndex index)
    {
        var buffer = new int[(index.Count + Vector128<int>.Count - 1) / Vector128<int>.Count * Vector128<int>.Count];
        Array.Fill(buffer, Padding);
        return buffer;
    }

    // Stany sąsiadów do bufora. Przy brzegu pochłaniającym sąsiadów jest mniej – resztę zakrywa Padding,
    // żeby nie liczyć stanów zostawionych przez poprzednią komórkę.
    private static int Gather(int[] cells, NeighbourIndex index, int i, int[] buffer)
    {
        int count = index.FillStates(cells, i, buffer);
        if (count < index.Count)
            buffer.AsSpan(count, index.Count - count).Fill(Padding);
        return count;
    }

    // Ilu sąsiadów ma stan state: po 4 porównania naraz (SIMD). Equals daje -1 tam, gdzie stan się zgadza,
    // więc suma po wszystkich wektorach to minus liczba zgodnych sąsiadów. Bez skoków warunkowych,
    // których procesor nie przewidzi dobrze przy losowych stanach sąsiadów.
    private static int CountSame(int[] buffer, int state)
    {
        var target = Vector128.Create(state);
        var matches = Vector128<int>.Zero;
        for (int k = 0; k < buffer.Length; k += Vector128<int>.Count)
            matches += Vector128.Equals(Vector128.Create(buffer.AsSpan(k, Vector128<int>.Count)), target);
        return -Vector128.Sum(matches);
    }
}
