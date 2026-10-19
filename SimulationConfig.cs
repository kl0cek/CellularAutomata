using System.Text.Json;
using System.Text.Json.Serialization;

namespace CellularAutomata;

public enum SimulationAlgorithm
{
    CA, // naiwny rozrost ziaren automatem komórkowym (Lab 2)
    MC, // naiwny rozrost ziaren Monte Carlo (Lab 3)
}

public sealed class SimulationConfig
{
    public SimulationAlgorithm Algorithm { get; init; } = SimulationAlgorithm.CA;

    public required string MorphologyPath { get; init; }
    public string TimePath { get; init; } = "OutputTXT/Time.txt";
    public string StepTimePath { get; init; } = "OutputTXT/StepTimes.txt";

    [JsonPropertyName("FullSpaceSizeX")] public required int SizeX { get; init; }
    [JsonPropertyName("FullSpaceSizeY")] public required int SizeY { get; init; }
    [JsonPropertyName("FullSpaceSizeZ")] public required int SizeZ { get; init; }

    public required Neighbourhood Neighbourhood { get; init; }
    [JsonPropertyName("BC")] public required Boundary Boundary { get; init; }

    public int SplitInZdim { get; init; } = 1;
    public int Seed { get; init; } = 1;

    // CA: liczba zarodków.
    public int Nucleons { get; init; }

    // MC: liczba stanów początkowych (orientacji Q), liczba kroków MCS, energia granicy J
    // i sposób losowania nowego stanu.
    public int States { get; init; }
    public int McSteps { get; init; }
    public double J { get; init; } = 1.0;
    public McProposal McProposal { get; init; } = McProposal.Random;

    // Liczba ziaren/stanów na start – do wypisywania i pliku z czasami.
    [JsonIgnore] public int InitialGrains => Algorithm == SimulationAlgorithm.CA ? Nucleons : States;

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
