using System.Diagnostics;
using Pipeline = Vecxy.AssetPipeline.AssetPipeline;

var arguments = args.ToList();

try
{
    var project = ResolveProject(GetOption(arguments, "--project"));
    if (arguments is ["assets", "scan"])
    {
        var manifest = Pipeline.Scan(project);
        Console.WriteLine($"Scanned {manifest.Assets.Count} assets -> {Path.Combine(project, "Assets.manifest")}");
        return 0;
    }
    if (arguments is ["assets", "generate"])
    {
        Pipeline.Scan(project);
        Console.WriteLine($"Generated {Pipeline.Generate(project)}");
        return 0;
    }
    if (arguments is ["assets", "validate"])
        return Validate(project);
    if (arguments is ["assets", "analyze"])
    {
        Pipeline.Analyze(project);
        Console.WriteLine("Asset references analyzed.");
        return 0;
    }
    if (arguments is ["assets", "prepare"])
        return Prepare(project);
    if (arguments is ["build"] or ["assets", "build"])
    {
        if (Prepare(project) != 0) return 1;
        var projectFile = Directory.EnumerateFiles(project, "*.csproj", SearchOption.TopDirectoryOnly).SingleOrDefault();
        if (projectFile is null) throw new FileNotFoundException($"Expected one .csproj in {project}");
        using var process = Process.Start(new ProcessStartInfo("dotnet", $"build \"{projectFile}\"") { UseShellExecute = false });
        process!.WaitForExit();
        return process.ExitCode;
    }
    PrintUsage();
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"BUILD FAILED\n\n{exception.Message}");
    return 1;
}

static int Validate(string project) => ReportMissing(Pipeline.Validate(project));
static int Prepare(string project)
{
    Pipeline.Scan(project);
    Pipeline.Generate(project);
    var references = Pipeline.Analyze(project);
    return ReportMissing(Pipeline.Validate(project, references));
}
static int ReportMissing(IReadOnlyList<Vecxy.AssetPipeline.MissingAsset> missing)
{
    if (missing.Count == 0) { Console.WriteLine("Asset validation succeeded."); return 0; }
    Console.Error.WriteLine("BUILD FAILED");
    foreach (var item in missing)
    {
        Console.Error.WriteLine($"\nMissing Asset:\n{item.Asset.Id:D}\n{item.Asset.Path}\n\nReferences:");
        foreach (var reference in item.References) Console.Error.WriteLine($"{reference.File}:{reference.Line}");
        if (item.References.Count == 0) Console.Error.WriteLine("(none)");
    }
    return 1;
}
static string? GetOption(List<string> values, string name)
{
    var index = values.IndexOf(name);
    if (index < 0) return null;
    if (index + 1 >= values.Count) throw new ArgumentException($"Missing value for {name}");
    var result = values[index + 1]; values.RemoveRange(index, 2); return result;
}
static string ResolveProject(string? value)
{
    var path = Path.GetFullPath(value ?? Directory.GetCurrentDirectory());
    if (File.Exists(path) && string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase))
        path = Path.GetDirectoryName(path)!;
    if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Game project directory was not found: {path}");
    var projects = Directory.EnumerateFiles(path, "*.csproj", SearchOption.TopDirectoryOnly).ToArray();
    if (projects.Length == 1) return path;
    if (projects.Length == 0)
        throw new InvalidOperationException($"No .csproj found in '{path}'. Select a game: --project HardCore.Cultivation");
    throw new InvalidOperationException($"More than one .csproj found in '{path}'. Select one with --project <path>.");
}
static void PrintUsage() => Console.WriteLine("Usage: vecxy --project <game-directory|csproj> assets <scan|generate|analyze|validate|prepare|build>\n       vecxy --project <game-directory|csproj> build\n\n--project may be omitted when the current directory contains exactly one .csproj.");
