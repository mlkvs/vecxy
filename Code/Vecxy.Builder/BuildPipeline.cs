using System.Diagnostics;
using Vecxy.Engine;
using Vecxy.IO;

namespace Vecxy.Builder;

public static class BuildPipeline
{
    public static void Build(BuildConfig config)
    {
        const string PROJECT_PATH = @"D:\Projects\vecxy\vecxy.game.flappy-bird";

        var project = new Project(PROJECT_PATH);

        if (!Directory.Exists(project.BuildDir))
        {
            Directory.CreateDirectory(project.BuildDir);
        }
        else
        {
            Directory.Delete(project.BuildDir, true);
            Directory.CreateDirectory(project.BuildDir);
        }

        var assetFiles = Directory
            .GetFiles(project.AssetsDir, "*.*", SearchOption.AllDirectories)
            .ToList();

        foreach (var file in assetFiles)
        {
            Console.WriteLine(file);
        }

        Console.WriteLine("\n--- Starting external MSBuild process ---");

        var slnPath = Path.Combine(PROJECT_PATH, "FlappyBird.sln");

        const string DOTNET_CLI = "dotnet";

        var msBuildArguments = new List<string>
        {
            "build",
            $"\"{slnPath}\"",
            "-c", "Debug",
        };

        var arguments = string.Join(" ", msBuildArguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = DOTNET_CLI,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var process = new Process())
        {
            process.StartInfo = startInfo;

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Console.WriteLine($"[MSBuild stdout]: {e.Data}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Console.Error.WriteLine($"[MSBuild stderr]: {e.Data}");
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    Console.WriteLine("--- External MSBuild process completed successfully. ---");

                    DirectoryUtils.CopyDirectory(Path.Combine(project.TempDir, "Debug", "Bin"), project.BuildDir);
                }
                else
                {
                    Console.Error.WriteLine($"--- External MSBuild process failed with exit code: {process.ExitCode}. ---");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"--- Failed to start/run external MSBuild process: {ex.Message} ---");
            }
        }

        Console.WriteLine("\n--- Build process finished ---");
    }
}