using System.Diagnostics;
using System.Security.Cryptography;
using System.Xml.Linq;
using Vecxy.AssetPipeline;
using Pipeline = Vecxy.AssetPipeline.AssetPipeline;

/// <summary>Refresh runtime data without invoking MSBuild, the compiler or the weaver.</summary>
internal static class AssetRefreshCommand
{
    public static async Task<bool> RunAsync(string project, string platform, string output)
    {
        var engine = Environment.GetEnvironmentVariable("VECXY_ENGINE_PATH")
            ?? throw new InvalidOperationException("Select the engine through vecxy before refreshing assets.");
        var assets = Path.Combine(project, "Assets");
        var sidecars = Directory.GetFiles(assets, "*.usdscene", SearchOption.AllDirectories);
        if (sidecars.Length > 0)
        {
            var python = FindPython(project);
            foreach (var sidecar in sidecars)
            {
                var source = sidecar[..^".usdscene".Length];
                if (!File.Exists(source)) throw new FileNotFoundException("USD source is missing.", source);
                var start = new ProcessStartInfo(python) { UseShellExecute = false };
                foreach (var argument in new[] { Path.Combine(engine, "Code", "Vecxy.Usd", "Tools", "cook_usd.py"), source, "--assets", assets })
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start USD cooking.");
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) throw new InvalidDataException("USD cooking failed. The game was not started.");
            }
        }
        var manifest = Pipeline.Scan(project);
        var generated = Path.Combine(project, "Generated", "Assets.g.cs");
        // Content-only edits preserve IDs/types/generated API. Any API change
        // falls back to normal compilation, even when file names stayed the same.
        if (!File.Exists(generated) || File.ReadAllText(generated) != Pipeline.GenerateSource(manifest))
            return false;
        var missing = Pipeline.Validate(project, Pipeline.Analyze(project));
        var packageErrors = VPackPipeline.ValidatePackageDependencies(manifest);
        if (missing.Count > 0 || packageErrors.Count > 0)
            throw new InvalidDataException("Asset validation failed. The game was not started.");
        var target = platform == "windows" ? Vecxy.Assets.VPackPlatform.Windows : Vecxy.Assets.VPackPlatform.Linux;
        await VPackPipeline.BuildAsync(project, target);
        var packages = Path.Combine(project, "Build", target.ToString());
        var files = Directory.GetFiles(packages).Where(IsPackageFile).ToArray();
        var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var old in Directory.GetFiles(output, "*.vpack"))
            if (!names.Contains(Path.GetFileName(old))) File.Delete(old);
        foreach (var file in files) CopyChanged(file, Path.Combine(output, Path.GetFileName(file)));
        CopyChanged(Path.Combine(project, "Assets.manifest"), Path.Combine(output, "Assets.manifest"));
        Console.WriteLine("Runtime assets refreshed; code binaries were not compiled or copied.");
        return true;
    }

    internal static bool IsPackageFile(string path) =>
        Path.GetExtension(path).Equals(".vpack", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(path) is "packages.manifest" or "packages.json";

    private static string FindPython(string project)
    {
        var configured = Environment.GetEnvironmentVariable("VecxyUsdPython");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var local = Path.Combine(project, ".vecxy", "usd-env", OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python");
        if (File.Exists(local)) return local;
        var document = XDocument.Load(Directory.GetFiles(project, "*.csproj").Single());
        foreach (var value in document.Descendants().Where(item => item.Name.LocalName == "VecxyUsdPython").Select(item => item.Value))
        {
            var path = value.Replace("$(MSBuildProjectDirectory)", project);
            if (!path.Contains("$(") && File.Exists(path)) return path;
        }
        return OperatingSystem.IsWindows() ? "python" : "python3";
    }
    private static void CopyChanged(string source, string destination)
    {
        if (File.Exists(destination) && SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(destination)))) return;
        File.Copy(source, destination, overwrite: true);
    }
}
