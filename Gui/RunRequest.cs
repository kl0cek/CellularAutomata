namespace CellularAutomata.Gui;

// Parametry symulacji przysłane z formularza w przeglądarce.
public sealed record RunRequest(
    string Algorithm,
    int SizeX, int SizeY, int SizeZ,
    string Neighbourhood,
    string Boundary,
    int Nucleons,
    int States, int McSteps, double J, string McProposal,
    int Seed)
{
    // Większe siatki przeglądarka wyświetla już bardzo wolno (200 x 200 x 200).
    public const long MaxCells = 8_000_000;
    public const int MaxMcSteps = 10_000;

    public long CellCount => (long)SizeX * SizeY * SizeZ;

    // Lista błędów do pokazania w formularzu; pusta = parametry poprawne.
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (Algorithm is not ("CA" or "MC"))
            errors.Add("Algorytm musi być CA albo MC.");
        if (SizeX < 3 || SizeY < 3)
            errors.Add("X i Y muszą wynosić co najmniej 3.");
        if (SizeZ != 1 && SizeZ < 3)
            errors.Add("Z musi wynosić 1 (mikrostruktura 2D) albo co najmniej 3 (3D).");
        if (CellCount > MaxCells)
            errors.Add($"Za duża siatka: {CellCount:N0} komórek, maksimum to {MaxCells:N0} (200 × 200 × 200).");
        if (Neighbourhood is not ("Moore" or "VonNeumann"))
            errors.Add("Sąsiedztwo musi być Moore albo VonNeumann.");
        if (Boundary is not ("Periodic" or "Absorbing"))
            errors.Add("Warunki brzegowe muszą być Periodic albo Absorbing.");

        if (Algorithm == "CA" && (Nucleons < 1 || Nucleons > CellCount))
            errors.Add("Liczba zarodków musi być od 1 do liczby komórek.");

        if (Algorithm == "MC")
        {
            if (States < 2 || States > CellCount)
                errors.Add("Liczba stanów Q musi być od 2 do liczby komórek.");
            if (McSteps < 1 || McSteps > MaxMcSteps)
                errors.Add($"Liczba kroków MCS musi być od 1 do {MaxMcSteps}.");
            if (J <= 0)
                errors.Add("J musi być większe od 0.");
            if (McProposal is not ("Random" or "Neighbour"))
                errors.Add("Sposób losowania nowego stanu musi być Random albo Neighbour.");
        }

        return errors;
    }

    // Konfiguracja w formacie, który czyta symulator (SimulationConfig).
    // Ścieżki są względne – symulator startuje w folderze danego uruchomienia.
    public Dictionary<string, object> ToSimulatorConfig()
    {
        var config = new Dictionary<string, object>
        {
            ["Algorithm"] = Algorithm,
            ["MorphologyPath"] = RunFiles.Micro,
            ["TimePath"] = RunFiles.Time,
            ["StepTimePath"] = RunFiles.Steps,
            ["FullSpaceSizeX"] = SizeX,
            ["FullSpaceSizeY"] = SizeY,
            ["FullSpaceSizeZ"] = SizeZ,
            ["Neighbourhood"] = Neighbourhood,
            ["BC"] = Boundary,
            ["SplitInZdim"] = 1,
            ["Seed"] = Seed,
        };

        if (Algorithm == "CA")
        {
            config["Nucleons"] = Nucleons;
        }
        else
        {
            config["States"] = States;
            config["McSteps"] = McSteps;
            config["J"] = J;
            config["McProposal"] = McProposal;
        }
        return config;
    }
}

// Nazwy plików w folderze jednego uruchomienia.
public static class RunFiles
{
    public const string Config = "config.json";
    public const string Micro = "Micro.txt";
    public const string Time = "Time.txt";
    public const string Steps = "StepTimes.txt";
    public const string Console = "console.txt";
}
