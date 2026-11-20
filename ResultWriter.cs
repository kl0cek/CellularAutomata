using System.Buffers.Text;
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

    // To samo co WriteMorphology (ten sam plik co do bajtu), ale szybciej (Lab 8): liczby formatujemy
    // od razu do bufora bajtów (Utf8Formatter), zamiast wołać StreamWriter.Write osiem razy na linię.
    public static FileWriteTime WriteMorphologyFast(string path, int sizeX, int sizeY, int sizeZ, int[] cells)
    {
        long t = Stopwatch.GetTimestamp();
        CreateDirectoryFor(path);
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1);
        var buffer = new byte[1 << 20];
        double createMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        t = Stopwatch.GetTimestamp();
        using (stream)
        {
            int used = Encoding.UTF8.GetBytes($"{sizeX} {sizeY} {sizeZ} xSize ySize zSize{Environment.NewLine}", buffer);
            const int maxLine = 4 * 11 + 4; // cztery liczby int i separatory

            for (int z = 0; z < sizeZ; z++)
                for (int y = 0; y < sizeY; y++)
                    for (int x = 0; x < sizeX; x++)
                    {
                        if (buffer.Length - used < maxLine)
                        {
                            stream.Write(buffer, 0, used);
                            used = 0;
                        }
                        used += WriteInt(buffer, used, x, (byte)' ');
                        used += WriteInt(buffer, used, y, (byte)' ');
                        used += WriteInt(buffer, used, z, (byte)' ');
                        used += WriteInt(buffer, used, cells[x + sizeX * (y + sizeY * z)], (byte)'\n');
                    }
            stream.Write(buffer, 0, used);
        }
        double writeMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;

        return new FileWriteTime(createMs, writeMs, new FileInfo(path).Length);
    }

    // Zapisuje liczbę i znak po niej; zwraca liczbę zapisanych bajtów.
    private static int WriteInt(byte[] buffer, int at, int value, byte separator)
    {
        Utf8Formatter.TryFormat(value, buffer.AsSpan(at), out int written);
        buffer[at + written] = separator;
        return written + 1;
    }

    // Czas każdego kroku (nadpisywany przy każdym uruchomieniu).
    public static void WriteStepTimes(string path, SimulationLog log)
    {
        CreateDirectoryFor(path);
        using var writer = new StreamWriter(path, append: false);

        double cumulative = 0;
        if (log.McSteps.Count > 0)
        {
            writer.WriteLine("mcs;accepted;energy;step_ms;cumulative_ms");
            foreach (var s in log.McSteps)
            {
                cumulative += s.Ms;
                writer.WriteLine(string.Join(';', s.Step, s.Accepted, Format(s.Energy), Format(s.Ms), Format(cumulative)));
            }
            return;
        }

        writer.WriteLine("step;changed;empty_left;step_ms;cumulative_ms");
        foreach (var s in log.Steps)
        {
            cumulative += s.Ms;
            writer.WriteLine(string.Join(';', s.Step, s.Changed, s.EmptyLeft, Format(s.Ms), Format(cumulative)));
        }
    }

    private const string TimeHeader =
        "date;algorithm;sizeX;sizeY;sizeZ;cells;nucleons_or_states;grains_end;neighbourhood;bc;steps;" +
        "alloc_ms;init_ms;growth_ms;simulation_ms;file_create_ms;file_write_ms;file_mb;total_ms";

    // Czas symulacji – każde uruchomienie dopisuje jedną linię.
    public static void AppendTime(string path, SimulationConfig c, int steps, int grainsEnd, SimulationLog log,
                                  double simulationMs, FileWriteTime file, double totalMs)
    {
        CreateDirectoryFor(path);
        MoveAsideIfOldFormat(path);
        bool newFile = !File.Exists(path);
        using var writer = new StreamWriter(path, append: true);

        if (newFile)
            writer.WriteLine(TimeHeader);

        writer.WriteLine(string.Join(';',
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            c.Algorithm, c.SizeX, c.SizeY, c.SizeZ, (long)c.SizeX * c.SizeY * c.SizeZ, c.InitialGrains, grainsEnd,
            c.Neighbourhood, c.Boundary, steps,
            Format(log.AllocationMs), Format(log.InitMs), Format(log.GrowthMs), Format(simulationMs),
            Format(file.CreateMs), Format(file.WriteMs), Format(file.Bytes / 1e6), Format(totalMs)));
    }

    // Plik z poprzedniej wersji programu ma inne kolumny – nie dopisujemy do niego, tylko zmieniamy mu nazwę.
    private static void MoveAsideIfOldFormat(string path)
    {
        if (!File.Exists(path) || File.ReadLines(path).FirstOrDefault() == TimeHeader)
            return;

        string oldPath = Path.Combine(Path.GetDirectoryName(path) ?? "",
            $"{Path.GetFileNameWithoutExtension(path)}_old_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(path)}");
        File.Move(path, oldPath);
        Console.WriteLine($"Stary plik czasów ma inne kolumny – przeniesiony do {oldPath}");
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
