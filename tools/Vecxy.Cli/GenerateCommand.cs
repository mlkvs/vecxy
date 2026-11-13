using System.Diagnostics;

internal static class GenerateCommand
{
    // Asset handles are generated before compilation; authoring schemas are finalized
    // from that compilation by the shared Platforms build target. No stale DLL scan.
    public static async Task<int> RunAsync(string project)
    {
        var projectFile = Directory.EnumerateFiles(project, "*.csproj").Single();
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (var argument in new[] { "build", projectFile, "--nologo", "-p:VecxyPlatform=Desktop", "-p:VecxyBuildVPack=false", "-p:VecxySkipAssetPipeline=true", "-p:VecxyGenerateAuthoring=true" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the project compiler.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
