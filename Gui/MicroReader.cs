using System.Runtime.InteropServices;

namespace CellularAutomata.Gui;

// Zamienia Micro.txt (nagłówek "X Y Z xSize ySize zSize", potem linie "x y z id") na zwarty format binarny
// dla przeglądarki: 3 × int32 (X, Y, Z), potem X·Y·Z × int32 z ID, indeks = x + X · (y + Y · z).
public static class MicroReader
{
    public static byte[] ReadBinary(string path)
    {
        byte[] text = File.ReadAllBytes(path);
        int pos = 0;

        int sizeX = NextInt(text, ref pos);
        int sizeY = NextInt(text, ref pos);
        int sizeZ = NextInt(text, ref pos);
        SkipLine(text, ref pos); // reszta nagłówka: "xSize ySize zSize"

        var result = new int[3 + sizeX * sizeY * sizeZ];
        result[0] = sizeX;
        result[1] = sizeY;
        result[2] = sizeZ;

        while (SkipWhitespace(text, ref pos))
        {
            int x = NextInt(text, ref pos);
            int y = NextInt(text, ref pos);
            int z = NextInt(text, ref pos);
            int id = NextInt(text, ref pos);
            result[3 + x + sizeX * (y + sizeY * z)] = id;
        }

        return MemoryMarshal.AsBytes(result.AsSpan()).ToArray();
    }

    // Prosty parser liczb bez tworzenia napisów – Micro.txt potrafi mieć miliony linii.
    private static int NextInt(byte[] text, ref int pos)
    {
        if (!SkipWhitespace(text, ref pos))
            throw new FormatException("Nieoczekiwany koniec pliku Micro.txt.");

        int value = 0;
        int start = pos;
        while (pos < text.Length && text[pos] >= '0' && text[pos] <= '9')
            value = value * 10 + (text[pos++] - '0');

        if (pos == start)
            throw new FormatException("Niepoprawna liczba w pliku Micro.txt.");
        return value;
    }

    // Przesuwa się do następnego znaku innego niż biały; false = koniec pliku.
    private static bool SkipWhitespace(byte[] text, ref int pos)
    {
        while (pos < text.Length && text[pos] is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t')
            pos++;
        return pos < text.Length;
    }

    private static void SkipLine(byte[] text, ref int pos)
    {
        while (pos < text.Length && text[pos] != '\n')
            pos++;
    }
}
