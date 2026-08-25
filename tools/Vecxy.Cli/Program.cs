using System.Diagnostics;
using Vecxy.AssetPipeline;
using Pipeline = Vecxy.AssetPipeline.AssetPipeline;

var arguments = args.ToList();

try
{
    var project = ResolveProject(GetOption(arguments, "--project"));
    var platformOption = GetOption(arguments, "--platform");
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
    if (arguments is ["assets", "packages"])
        return ListPackages(project);
    if (arguments is ["assets", "pack"])
    {
        if (Prepare(project) != 0) return 1;
        ReportPackages(await VPackPipeline.BuildAsync(project, ParsePlatform(platformOption ?? "windows")));
        return 0;
    }
    if (arguments is ["packages", "manifest"])
    {
        if (Prepare(project) != 0) return 1;
        var platform = ParsePlatform(platformOption ?? "windows");
        await VPackPipeline.BuildAsync(project, platform);
        Console.WriteLine($"Generated {Path.Combine(project, "Build", platform.ToString(), "packages.json")}");
        return 0;
    }
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
        var platform = ParsePlatform(platformOption ?? "windows");
        var packages = await VPackPipeline.BuildAsync(project, platform);
        ReportPackages(packages);
        var projectFile = Directory.EnumerateFiles(project, "*.csproj", SearchOption.TopDirectoryOnly).SingleOrDefault();
        if (projectFile is null) throw new FileNotFoundException($"Expected one .csproj in {project}");
        var platformProperty = platform == Vecxy.Assets.VPackPlatform.Android ? "Android" : "Desktop";
        var packagesDirectory = Path.Combine(project, "Build", platform.ToString());
        using var process = Process.Start(new ProcessStartInfo("dotnet", $"build \"{projectFile}\" -p:VecxyPlatform={platformProperty} -p:VecxySkipAssetPipeline=true -p:VecxyPackagesDirectory=\"{packagesDirectory}\"") { UseShellExecute = false });
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

static int Validate(string project)
{
    var missing = Pipeline.Validate(project);
    var manifest = Vecxy.Assets.AssetManifest.Load(Path.Combine(project, "Assets.manifest"));
    var packageErrors = VPackPipeline.ValidatePackageDependencies(manifest);
    var result = ReportMissing(missing);
    foreach (var error in packageErrors) Console.Error.WriteLine($"\n{error}");
    return result != 0 || packageErrors.Count > 0 ? 1 : 0;
}
static int ListPackages(string project)
{
    var manifest = Pipeline.Scan(project);
    var definitions = VPackPipeline.DiscoverPackages(project);
    Console.WriteLine("Packages\n");
    foreach (var package in definitions)
    {
        Console.WriteLine(package.Name);
        Console.WriteLine($"  Load: {(package.Load == Vecxy.Assets.PackageLoadMode.Startup ? "startup" : "on-demand")}");
        Console.WriteLine($"  Assets: {manifest.Assets.Count(x => x.Package == package.Id)}");
        if (package.Dependencies.Count > 0) Console.WriteLine($"  Dependencies: {string.Join(", ", package.Dependencies)}");
        foreach (var platform in new[] { Vecxy.Assets.VPackPlatform.Windows, Vecxy.Assets.VPackPlatform.Linux, Vecxy.Assets.VPackPlatform.Android })
        { var compression = VPackPipeline.ResolveCompression(package, platform); Console.WriteLine($"  Compression[{platform.ToString().ToLowerInvariant()}]: {compression.Algorithm.ToString().ToLowerInvariant()}{(compression.Algorithm == Vecxy.Assets.VPackCompressionAlgorithm.Zstd ? $"/{compression.Level}" : "")}"); }
        Console.WriteLine();
    }
    foreach (var error in VPackPipeline.ValidatePackageDependencies(manifest)) Console.Error.WriteLine(error);
    return 0;
}
static Vecxy.Assets.VPackPlatform ParsePlatform(string value) => value.ToLowerInvariant() switch
{ "windows" => Vecxy.Assets.VPackPlatform.Windows, "linux" => Vecxy.Assets.VPackPlatform.Linux, "android" => Vecxy.Assets.VPackPlatform.Android, _ => throw new ArgumentException($"Unsupported platform '{value}'. Use windows, linux, or android.") };
static void ReportPackages(IReadOnlyList<VPackPackageBuild> packages)
{
    foreach (var package in packages)
    { var stats=package.Statistics; var ratio=stats.RawSize==0?100d:100d*stats.PackedSize/stats.RawSize; Console.WriteLine($"Built {package.File}\nAssets: {stats.AssetCount}\nRaw size: {stats.RawSize} bytes\nPacked size: {stats.PackedSize} bytes\nCompression ratio: {ratio:F1}%\nBlocks: {stats.BlockCount}"); }
}
static int Prepare(string project)
{
    var manifest = Pipeline.Scan(project);
    Pipeline.Generate(project);
    var references = Pipeline.Analyze(project);
    var result = ReportMissing(Pipeline.Validate(project, references));
    var packageErrors = VPackPipeline.ValidatePackageDependencies(manifest);
    foreach (var error in packageErrors) Console.Error.WriteLine($"\n{error}");
    return result != 0 || packageErrors.Count > 0 ? 1 : 0;
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
static void PrintUsage() => Console.WriteLine("Usage: vecxy --project <game-directory|csproj> assets <scan|generate|analyze|validate|packages|pack|prepare|build> [--platform <windows|linux|android>]\n       vecxy --project <game-directory|csproj> packages manifest --platform <windows|linux|android>\n       vecxy --project <game-directory|csproj> build --platform <windows|linux|android>\n\n--project may be omitted when the current directory contains exactly one .csproj.");
