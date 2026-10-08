using System.Text.Json;
using System.Text.Json.Serialization;

namespace CellularAutomata;

public sealed class SimulationConfig
{
    public required string MorphologyPath { get; init; }
    public string TimePath { get; init; } = "OutputTXT/Time.txt";
    public string StepTimePath { get; init; } = "OutputTXT/StepTimes.txt";

    [JsonPropertyName("FullSpaceSizeX")] public required int SizeX { get; init; }
    [JsonPropertyName("FullSpaceSizeY")] public required int SizeY { get; init; }
    [JsonPropertyName("FullSpaceSizeZ")] public required int SizeZ { get; init; }

    public required Neighbourhood Neighbourhood { get; init; }
    [JsonPropertyName("BC")] public required Boundary Boundary { get; init; }

    public required int Nucleons { get; init; }
    public int SplitInZdim { get; init; } = 1;
    public int Seed { get; init; } = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() }, // "Moore" zamiast liczby
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static SimulationConfig Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<SimulationConfig>(File.ReadAllText(path), Options)
                   ?? throw new FormatException($"Plik {path} jest pusty.");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Niepoprawny plik konfiguracyjny {path}: {ex.Message}");
        }
    }
}
