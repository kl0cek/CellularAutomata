namespace CellularAutomata;

// Indeksy sąsiadów komórki w płaskim wektorze (Lab 8 – optymalizacja wersji sekwencyjnej).
// Komórka z dala od brzegu ma sąsiadów pod stałymi przesunięciami: i + Linear[k], bez dzielenia
// i bez warunków brzegowych. Pełne CaRules.ApplyBoundary zostaje tylko dla komórek przy brzegu.
// Kolejność sąsiadów jest ta sama co w CaRules.BuildOffsets, więc wyniki się nie zmieniają.
public sealed class NeighbourIndex
{
    private readonly int _sizeX, _sizeY, _sizeZ;
    private readonly bool _is3D;
    private readonly Boundary _boundary;
    private readonly (int Dx, int Dy, int Dz)[] _offsets;

    public NeighbourIndex(int sizeX, int sizeY, int sizeZ, Neighbourhood neighbourhood, Boundary boundary)
    {
        _sizeX = sizeX;
        _sizeY = sizeY;
        _sizeZ = sizeZ;
        _is3D = sizeZ > 1;
        _boundary = boundary;
        _offsets = CaRules.BuildOffsets(neighbourhood, _is3D);

        Linear = new int[_offsets.Length];
        for (int k = 0; k < _offsets.Length; k++)
        {
            var (dx, dy, dz) = _offsets[k];
            Linear[k] = dx + sizeX * (dy + sizeY * dz);
        }
    }

    // Przesunięcia indeksu do sąsiadów komórki wewnętrznej.
    public int[] Linear { get; }

    // Największa możliwa liczba sąsiadów (26, 6, 8 albo 4) – rozmiar bufora dla Fill.
    public int Count => Linear.Length;

    // Wpisuje do result indeksy sąsiadów komórki i; zwraca, ilu ich jest
    // (przy brzegach pochłaniających komórka brzegowa ma ich mniej).
    public int Fill(int i, int[] result)
    {
        int x = i % _sizeX;
        int rest = i / _sizeX;
        int y = rest % _sizeY;
        int z = rest / _sizeY;

        bool interior = x > 0 && x < _sizeX - 1 &&
                        y > 0 && y < _sizeY - 1 &&
                        (!_is3D || (z > 0 && z < _sizeZ - 1));
        if (interior)
        {
            var linear = Linear;
            for (int k = 0; k < linear.Length; k++)
                result[k] = i + linear[k];
            return linear.Length;
        }

        return FillAtBoundary(x, y, z, result);
    }

    // Wpisuje do result stany sąsiadów komórki i (a nie ich indeksy); zwraca, ilu ich jest.
    // Dla komórki wewnętrznej czyta siatkę od razu, bez pośredniej tablicy indeksów.
    public int FillStates(int[] cells, int i, int[] result)
    {
        int x = i % _sizeX;
        int rest = i / _sizeX;
        int y = rest % _sizeY;
        int z = rest / _sizeY;

        bool interior = x > 0 && x < _sizeX - 1 &&
                        y > 0 && y < _sizeY - 1 &&
                        (!_is3D || (z > 0 && z < _sizeZ - 1));
        if (interior)
        {
            var linear = Linear;
            for (int k = 0; k < linear.Length; k++)
                result[k] = cells[i + linear[k]];
            return linear.Length;
        }

        int count = FillAtBoundary(x, y, z, result);
        for (int k = 0; k < count; k++)
            result[k] = cells[result[k]];
        return count;
    }

    // Komórka przy brzegu: jak w wersji naiwnej, z zawijaniem albo pominięciem sąsiada.
    private int FillAtBoundary(int x, int y, int z, int[] result)
    {
        int count = 0;
        foreach (var (dx, dy, dz) in _offsets)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (CaRules.ApplyBoundary(ref nx, _sizeX, _boundary) &&
                CaRules.ApplyBoundary(ref ny, _sizeY, _boundary) &&
                CaRules.ApplyBoundary(ref nz, _sizeZ, _boundary))
                result[count++] = nx + _sizeX * (ny + _sizeY * nz);
        }
        return count;
    }
}
