using CellularAutomata.Gui;

// Część użytkowa (Lab 5): strona w przeglądarce + małe API.
// Formularz → config.json w nowym folderze → uruchomienie symulatora → wizualizacja wyników 2D i 3D.
//   cd Gui && dotnet run        → http://localhost:5080

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

string root = app.Environment.ContentRootPath;
var runs = new RunManager(
    simulatorDll: Path.GetFullPath(Path.Combine(root, app.Configuration["Simulator:Dll"]!)),
    runsDirectory: Path.GetFullPath(Path.Combine(root, app.Configuration["Simulator:RunsDirectory"]!)));

app.UseDefaultFiles();
app.UseStaticFiles();

// Czy symulator jest zbudowany – strona pokazuje ostrzeżenie, jeśli nie.
app.MapGet("/api/health", () => new { runs.SimulatorFound, runs.SimulatorDll, runs.RunsDirectory });

app.MapGet("/api/runs", () => runs.List());

app.MapPost("/api/runs", (RunRequest request) =>
{
    var errors = request.Validate();
    if (errors.Count > 0)
        return Results.BadRequest(new { errors });
    if (!runs.SimulatorFound)
        return Results.BadRequest(new { errors = new[] { $"Nie znaleziono symulatora: {runs.SimulatorDll}. Zbuduj go: dotnet build -c Release." } });

    string id = runs.Enqueue(request);
    return Results.Ok(runs.Describe(id));
});

// Seria (Lab 6): najpierw powstają wszystkie pliki config.json, potem obliczenia idą po kolei w kolejce.
app.MapPost("/api/series", (SeriesRequest request) =>
{
    var errors = request.Validate();
    if (errors.Count > 0)
        return Results.BadRequest(new { errors });
    if (!runs.SimulatorFound)
        return Results.BadRequest(new { errors = new[] { $"Nie znaleziono symulatora: {runs.SimulatorDll}. Zbuduj go: dotnet build -c Release." } });

    string series = "series_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var ids = request.Expand().Select(run => runs.Enqueue(run, series)).ToList();
    return Results.Ok(new { series, ids });
});

// Zbiorcze wyniki zakończonych uruchomień: do wykresów porównawczych i jako plik CSV.
app.MapGet("/api/results", () => runs.Results());
app.MapGet("/api/results.csv", () =>
    Results.File(System.Text.Encoding.UTF8.GetBytes(ResultsTable.ToCsv(runs.Results())), "text/csv; charset=utf-8", "wyniki.csv"));

app.MapGet("/api/runs/{id}", (string id) =>
    runs.Describe(id) is { } details ? Results.Ok(details) : Results.NotFound());

// Mikrostruktura w formacie binarnym (opis w MicroReader).
app.MapGet("/api/runs/{id}/micro", (string id) =>
    runs.FileOf(id, RunFiles.Micro) is { } path
        ? Results.Bytes(MicroReader.ReadBinary(path), "application/octet-stream")
        : Results.NotFound());

// Czasy i energia kolejnych kroków (StepTimes.txt, kolumny rozdzielone średnikami).
app.MapGet("/api/runs/{id}/steps", (string id) =>
    runs.FileOf(id, RunFiles.Steps) is { } path
        ? Results.Text(File.ReadAllText(path), "text/plain; charset=utf-8")
        : Results.NotFound());

app.Lifetime.ApplicationStarted.Register(() =>
{
    Console.WriteLine($"Symulator: {runs.SimulatorDll} ({(runs.SimulatorFound ? "OK" : "BRAK – zbuduj: dotnet build -c Release")})");
    Console.WriteLine($"Wyniki:    {runs.RunsDirectory}");
});

app.Run();
