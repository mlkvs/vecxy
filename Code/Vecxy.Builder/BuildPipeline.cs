using System.Diagnostics;
using Vecxy.Engine;

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

                    CopyBuildResults(Path.Combine(project.TempDir, "Debug", "Bin"), project.BuildDir);
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


    /// <summary>
    /// Копирует все файлы из исходной директории в целевую.
    /// </summary>
    private static void CopyBuildResults(string sourcePath, string destinationPath)
    {
        Console.WriteLine($"\nCopying build results from '{sourcePath}' to '{destinationPath}'...");

        if (!Directory.Exists(sourcePath))
        {
            Console.Error.WriteLine($"Source directory '{sourcePath}' does not exist for copying.");
            return;
        }

        Directory.CreateDirectory(destinationPath); // Убедимся, что целевая папка существует

        // Копируем все файлы (dll, exe, pdb, xml и т.п.) из временной директории
        foreach (var file in Directory.GetFiles(sourcePath, "*.*", SearchOption.TopDirectoryOnly))
        {
            string destFileName = Path.Combine(destinationPath, Path.GetFileName(file));

            try
            {
                File.Copy(file, destFileName, true); // true для перезаписи существующих файлов
                Console.WriteLine($"  Copied: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Failed to copy '{Path.GetFileName(file)}': {ex.Message}");
            }
        }
        Console.WriteLine("Build results copy complete.");
    }
}