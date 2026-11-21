using System.Diagnostics;
using System.Text.Json.Nodes;

internal static class RestoreGraphCache
{
    public static bool CanReuseDesktop(string project, string configuration, string runtime, string packages)
    {
        var graphName = Path.GetFileName(project) + ".nuget.dgspec.json";
        var intermediate = Path.Combine(Path.GetDirectoryName(project)!, "obj");
        if (!File.Exists(Path.Combine(intermediate, "Desktop", graphName)) &&
            !File.Exists(Path.Combine(intermediate, graphName)))
            return false;

        var temporary = Path.Combine(Path.GetTempPath(), "vecxy-restore-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[]
            {
                "msbuild", project, "-nologo", "-t:GenerateRestoreGraphFile", $"-p:RestoreGraphOutputPath={temporary}",
                $"-p:Configuration={configuration}", "-p:TargetFramework=net10.0", $"-p:RuntimeIdentifier={runtime}",
                "-p:SelfContained=true", "-p:VecxyPlatform=Desktop", "-p:VecxySkipAssetPipeline=true",
                $"-p:VecxyPackagesDirectory={packages}", "-p:ErrorOnDuplicatePublishOutputFiles=false"
            })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null)
                return false;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                return false;

            var current = Read(temporary);
            var projects = current["projects"]!.AsObject();
            var root = projects[project]!["restore"]!;
            var restored = Read(Path.Combine(root["outputPath"]!.GetValue<string>(), graphName));
            var previous = restored["projects"]!.AsObject();
            foreach (var (_, entry) in projects)
                entry!["restore"]!.AsObject().Remove("restoreAuditProperties");
            foreach (var (_, entry) in previous)
                entry!["restore"]!.AsObject().Remove("restoreAuditProperties");
            if (!JsonNode.DeepEquals(current["format"], restored["format"]) || !JsonNode.DeepEquals(projects, previous))
                return false;

            foreach (var (path, entry) in projects)
            {
                var directory = entry!["restore"]!["outputPath"]!.GetValue<string>();
                var cache = Read(Path.Combine(directory, "project.nuget.cache"));
                if (cache["success"]?.GetValue<bool>() != true || cache["projectFilePath"]?.GetValue<string>() != path ||
                    cache["expectedPackageFiles"] is not JsonArray files ||
                    files.Any(file => !File.Exists(file?.GetValue<string>())) ||
                    !File.Exists(Path.Combine(directory, "project.assets.json")) ||
                    !File.Exists(Path.Combine(directory, Path.GetFileName(path) + ".nuget.g.props")) ||
                    !File.Exists(Path.Combine(directory, Path.GetFileName(path) + ".nuget.g.targets")))
                    return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or
            InvalidOperationException or NullReferenceException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static JsonNode Read(string path) => JsonNode.Parse(File.ReadAllText(path))
        ?? throw new InvalidDataException("Missing restore metadata.");
}
