using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CellularAutomata;

// Czasy zapisu pliku: utworzenie (otwarcie pliku + bufor) i zapis danych (z opróżnieniem bufora i zamknięciem).
public readonly record struct FileWriteTime(double CreateMs, double WriteMs, long Bytes);

public static class ResultWriter
{
    // Mikrostruktura końcowa w txt
    public static FileWriteTime WriteMorphology(string path, int sizeX, int sizeY, int sizeZ, int[] cells)
    {
        // Utworzenie pliku: folder, plik na dysku i bufor zapisu 1 MB w pamięci.
        long t = Stopwatch.GetTimestamp();
        CreateDirectoryFor(path);
        var writer = new StreamWriter(path, append: false, new UTF8Encoding(false), bufferSize: 1 << 20);
        double createMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        // Zapis danych; koniec bloku using opróżnia bufor na dysk i zamyka plik – to też wliczamy.
        t = Stopwatch.GetTimestamp();
        using (writer)
        {
            writer.WriteLine($"{sizeX} {sizeY} {sizeZ} xSize ySize zSize");
            for (int z = 0; z < sizeZ; z++)
                for (int y = 0; y < sizeY; y++)
                    for (int x = 0; x < sizeX; x++)
                    {
                        writer.Write(x);
                        writer.Write(' ');
                        writer.Write(y);
                        writer.Write(' ');
                        writer.Write(z);
                        writer.Write(' ');
                        writer.Write(cells[x + sizeX * (y + sizeY * z)]);
                        writer.Write('\n');
                    }
        }
        double writeMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        return new FileWriteTime(createMs, writeMs, new FileInfo(path).Length);
    }

    // Czas każdego kroku (nadpisywany przy każdym uruchomieniu).
    public static void WriteStepTimes(string path, SimulationLog log)
    {
        CreateDirectoryFor(path);
        using var writer = new StreamWriter(path, append: false);

        writer.WriteLine("step;changed;empty_left;step_ms;cumulative_ms");
        double cumulative = 0;
        foreach (var s in log.Steps)
        {
            cumulative += s.Ms;
            writer.WriteLine(string.Join(';', s.Step, s.Changed, s.EmptyLeft, Format(s.Ms), Format(cumulative)));
        }
    }

    // Czas symulacji – każde uruchomienie dopisuje jedną linię.
    public static void AppendTime(string path, SimulationConfig c, int steps, SimulationLog log,
                                  double simulationMs, FileWriteTime file, double totalMs)
    {
        CreateDirectoryFor(path);
        bool newFile = !File.Exists(path);
        using var writer = new StreamWriter(path, append: true);

        if (newFile)
            writer.WriteLine("date;sizeX;sizeY;sizeZ;cells;nucleons;neighbourhood;bc;steps;" +
                             "alloc_ms;nucleation_ms;growth_ms;simulation_ms;file_create_ms;file_write_ms;file_mb;total_ms");

        writer.WriteLine(string.Join(';',
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            c.SizeX, c.SizeY, c.SizeZ, (long)c.SizeX * c.SizeY * c.SizeZ, c.Nucleons,
            c.Neighbourhood, c.Boundary, steps,
            Format(log.AllocationMs), Format(log.NucleationMs), Format(log.GrowthMs), Format(simulationMs),
            Format(file.CreateMs), Format(file.WriteMs), Format(file.Bytes / 1e6), Format(totalMs)));
    }

    // Kropka dziesiętna niezależnie od ustawień regionalnych (łatwiej wczytać do Excela/Pythona).
    private static string Format(double value) => value.ToString("F3", CultureInfo.InvariantCulture);

    private static void CreateDirectoryFor(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}
