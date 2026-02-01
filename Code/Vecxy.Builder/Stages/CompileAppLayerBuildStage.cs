using JetBrains.Annotations;
using System.Diagnostics;
using Vecxy.Diagnostics;
using Vecxy.Engine;
using Vecxy.IO;

namespace Vecxy.Builder;

[UsedImplicitly]
public class CompileStage : BuildStage
{
    public override string Name => "Compiling Game Project";
    public override int Priority => 100;

    public override void Execute(BuildContext ctx)
    {
        var project = new Project(ctx.ProjectDir);

        var slnFiles = Directory.GetFiles(ctx.ProjectDir, "*.sln");
        if (slnFiles.Length == 0) throw new Exception("Solution file (*.sln) not found.");

        string configName = ctx.Configuration == BUILD_CONFIGURATION.DEBUG ? "Debug" : "Release";

        var args = new List<string>
        {
            "build", $"\"{slnFiles[0]}\"",
            "-c", configName,
            $"-p:OutDir=\"{ctx.OutputDir}/\"",
            "-p:NoWarn=NETSDK1194",
            "--nologo" // Убираем приветствие dotnet, чтобы логи были чище
        };

        Logger.Info($"Executing: dotnet {string.Join(" ", args)}");
        RunDotnet(ctx.ProjectDir, args);
    }

    private void RunDotnet(string workingDir, List<string> args)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = string.Join(" ", args),
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.OutputDataReceived += (s, e) => { if (e.Data != null) Logger.Info(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) Logger.Error(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0) throw new Exception("Compilation failed.");
    }
}

[UsedImplicitly]
public class BundleStage : BuildStage
{
    public override string Name => "Bundling Assets & Runner";
    public override int Priority => 50;

    public override void Execute(BuildContext ctx)
    {
        var project = new Project(ctx.ProjectDir);
        string configName = ctx.Configuration.ToString();

        // Находим корень движка относительно CLI
        string cliFolder = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string engineRoot = Path.GetDirectoryName(cliFolder)!;

        string runnerSource = Path.Combine(engineRoot, "Runner", configName);
        if (!Directory.Exists(runnerSource))
            throw new Exception($"Runner template not found at {runnerSource}");

        // Копируем файлы движка
        Logger.Info($"Copying runner from {configName}...");
        DirectoryUtils.CopyDirectory(runnerSource, ctx.OutputDir);

        // Копируем ассеты
        if (Directory.Exists(project.AssetsDir))
        {
            DirectoryUtils.CopyDirectory(project.AssetsDir, Path.Combine(ctx.OutputDir, "Assets"));
        }

        SetupExecutable(ctx, project.Info.Name);
    }

    private void SetupExecutable(BuildContext ctx, string gameName)
    {
        string runner = "Vecxy.Executable";
        string output = ctx.OutputDir;

        // Все эти файлы должны иметь одинаковое имя для корректного запуска
        string[] extensions = { ".exe"};

        foreach (var ext in extensions)
        {
            string oldPath = Path.Combine(output, runner + ext);
            string newPath = Path.Combine(output, gameName + ext);

            if (File.Exists(oldPath))
            {
                File.Move(oldPath, newPath, true);
            }
        }
        Logger.Info($"Runner rebranded to {gameName}");
    }
}