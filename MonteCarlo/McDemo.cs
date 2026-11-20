namespace CellularAutomata;

// Podgląd MC na małej siatce 2D: każda litera to inna orientacja (A–E).
// Widać, jak losowy „szum” po kilku krokach MCS układa się w ziarna.
public static class McDemo
{
    public static void Run()
    {
        const int sizeX = 40, sizeY = 12, states = 5;
        int[] show = { 0, 1, 2, 5, 10, 30 };

        Console.WriteLine($"Demo MC: siatka {sizeX}x{sizeY} (2D), Q = {states} orientacji, Moore, Periodic, J = 1\n");

        MonteCarloGrainGrowth.Generate(sizeX, sizeY, 1, states, show[^1], Neighbourhood.Moore, Boundary.Periodic,
            j: 1.0, McProposal.Random, seed: 3, onStep: (step, cells, energy) =>
            {
                if (!show.Contains(step))
                    return;

                Console.WriteLine($"MCS {step}, energia {energy}:");
                for (int y = 0; y < sizeY; y++)
                {
                    var line = new char[sizeX];
                    for (int x = 0; x < sizeX; x++)
                        line[x] = (char)('A' + cells[x + sizeX * y] - 1);
                    Console.WriteLine("  " + new string(line));
                }
                Console.WriteLine();
            });
    }
}
