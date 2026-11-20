namespace CellularAutomata;

public static class GrainAnalysis
{
    // Liczba ziaren = spójnych obszarów o tym samym ID (flood fill po sąsiedztwie i warunkach brzegowych).
    // W MC dwa oddzielne ziarna mogą mieć tę samą orientację, więc liczba różnych ID to nie to samo.
    public static int CountGrains(int[] cells, int sizeX, int sizeY, int sizeZ,
                                  Neighbourhood neighbourhood, Boundary boundary)
    {
        var offsets = CaRules.BuildOffsets(neighbourhood, is3D: sizeZ > 1);
        var visited = new bool[cells.Length];
        var stack = new Stack<int>();
        int grains = 0;

        for (int start = 0; start < cells.Length; start++)
        {
            if (visited[start])
                continue;

            // Nowe ziarno: zalewamy wszystkie połączone komórki o tym samym ID.
            grains++;
            int id = cells[start];
            visited[start] = true;
            stack.Push(start);

            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % sizeX;
                int y = i / sizeX % sizeY;
                int z = i / (sizeX * sizeY);

                foreach (var (dx, dy, dz) in offsets)
                {
                    int nx = x + dx, ny = y + dy, nz = z + dz;
                    if (!CaRules.ApplyBoundary(ref nx, sizeX, boundary) ||
                        !CaRules.ApplyBoundary(ref ny, sizeY, boundary) ||
                        !CaRules.ApplyBoundary(ref nz, sizeZ, boundary))
                        continue;

                    int n = nx + sizeX * (ny + sizeY * nz);
                    if (!visited[n] && cells[n] == id)
                    {
                        visited[n] = true;
                        stack.Push(n);
                    }
                }
            }
        }
        return grains;
    }

    // To samo co CountGrains, ale z NeighbourIndex (bez warunków brzegowych dla komórek wewnętrznych)
    // i stosem w zwykłej tablicy zamiast Stack<int> (Lab 8). Wynik jest identyczny.
    public static int CountGrainsFast(int[] cells, int sizeX, int sizeY, int sizeZ,
                                      Neighbourhood neighbourhood, Boundary boundary)
    {
        var index = new NeighbourIndex(sizeX, sizeY, sizeZ, neighbourhood, boundary);
        var neighbours = new int[index.Count];
        var visited = new bool[cells.Length];
        var stack = new int[cells.Length]; // każda komórka trafia na stos co najwyżej raz
        int grains = 0;

        for (int start = 0; start < cells.Length; start++)
        {
            if (visited[start])
                continue;

            grains++;
            int id = cells[start];
            visited[start] = true;
            int top = 0;
            stack[top++] = start;

            while (top > 0)
            {
                int i = stack[--top];
                int count = index.Fill(i, neighbours);
                for (int k = 0; k < count; k++)
                {
                    int n = neighbours[k];
                    if (!visited[n] && cells[n] == id)
                    {
                        visited[n] = true;
                        stack[top++] = n;
                    }
                }
            }
        }
        return grains;
    }
}
