namespace CellularAutomata;

public enum Neighbourhood
{
    VonNeumann, // sąsiedzi przez ścianę: 6 w 3D
    Moore,      // przez ścianę, krawędź i narożnik: 26 w 3D
}

public enum Boundary
{
    Periodic,   // siatka się zawija – komórka przy prawym brzegu sąsiaduje z lewym
    Absorbing,  // za brzegiem nie ma sąsiadów
}

// Elementy wspólne dla obu wersji algorytmu (wektor i macierz), żeby różniły się
// wyłącznie sposobem przechowywania siatki, a nie regułami.
public static class CaRules
{
    public const int Empty = 0;

    public static void Validate(int sizeX, int sizeY, int sizeZ, int nuclei)
    {
        if (sizeX < 3 || sizeY < 3 || (sizeZ != 1 && sizeZ < 3))
            throw new ArgumentException("Rozmiar: sizeX, sizeY >= 3, a sizeZ = 1 (2D) albo >= 3 (3D).");
        if (nuclei < 1 || nuclei > (long)sizeX * sizeY * sizeZ)
            throw new ArgumentException("Liczba zarodków musi być od 1 do liczby komórek.");
    }

    // Zarodkowanie: losuje N różnych komórek (jako indeksy liniowe x + sizeX * (y + sizeY * z)).
    // Obie wersje dostają te same pozycje, więc startują z identycznego stanu.
    public static int[] PickNucleiPositions(int cellCount, int nuclei, Random rng)
    {
        var taken = new bool[cellCount];
        var positions = new int[nuclei];
        for (int n = 0; n < nuclei; n++)
        {
            int index;
            do index = rng.Next(cellCount);
            while (taken[index]);
            taken[index] = true;
            positions[n] = index;
        }
        return positions;
    }

    // Przesunięcia (dx, dy, dz) do sąsiadów. W 2D pomijamy oś z.
    public static (int Dx, int Dy, int Dz)[] BuildOffsets(Neighbourhood neighbourhood, bool is3D)
    {
        var offsets = new List<(int, int, int)>();
        int zRange = is3D ? 1 : 0;

        for (int dz = -zRange; dz <= zRange; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int distance = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
            if (distance == 0)
                continue; // sama komórka
            if (neighbourhood == Neighbourhood.VonNeumann && distance > 1)
                continue; // von Neumann: tylko sąsiedzi przez ścianę
            offsets.Add((dx, dy, dz));
        }
        return offsets.ToArray();
    }

    // Dopisuje ID sąsiada do listy różnych ID (ids) i zwiększa jego licznik (hits).
    public static void CountNeighbour(int id, int[] ids, int[] hits, ref int distinct)
    {
        if (id == Empty)
            return;

        int j = Array.IndexOf(ids, id, 0, distinct);
        if (j < 0)
        {
            j = distinct++;
            ids[j] = id;
            hits[j] = 0;
        }
        hits[j]++;
    }

    // Najczęstsze ID; przy remisie losujemy spośród remisujących. Brak sąsiadów-ziaren → Empty.
    public static int PickMostFrequent(int[] ids, int[] hits, int distinct, Random rng)
    {
        if (distinct == 0)
            return Empty;

        int best = 0, ties = 0;
        for (int j = 0; j < distinct; j++)
        {
            if (hits[j] > best) { best = hits[j]; ties = 1; }
            else if (hits[j] == best) ties++;
        }

        int pick = rng.Next(ties);
        for (int j = 0; j < distinct; j++)
            if (hits[j] == best && pick-- == 0)
                return ids[j];

        return Empty; // nieosiągalne
    }

    // Periodyczne: zawija współrzędną. Pochłaniające: false, jeśli sąsiad wypada poza siatkę.
    public static bool ApplyBoundary(ref int c, int size, Boundary boundary)
    {
        if (c >= 0 && c < size)
            return true;
        if (boundary == Boundary.Absorbing)
            return false;
        c = (c + size) % size;
        return true;
    }
}
