using System.Diagnostics;

internal static class GenerateCommand
{
    // Asset handles are generated before compilation; authoring schemas are finalized
    // from that compilation by the shared Platforms build target. No stale DLL scan.
    public static Task<int> RunAsync(string project, CancellationToken cancellationToken)
    {
        var projectFile = Directory.EnumerateFiles(project, "*.csproj").Single();
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        start.Environment["VecxyCliAssembly"] = typeof(GenerateCommand).Assembly.Location;
        foreach (var argument in new[] { "build", projectFile, "--nologo", "-tl:off", "-p:VecxyPlatform=Desktop", "-p:VecxyBuildVPack=false", "-p:VecxySkipAssetPipeline=true", "-p:VecxyGenerateAuthoring=true" })
            start.ArgumentList.Add(argument);
        return CliProcess.RunAsync(start, cancellationToken);
    }
}
