using System.Diagnostics;
using JetBrains.Annotations;

namespace Vecxy.Builder;

[UsedImplicitly]
internal class CompileAppLayerBuildStage : BuildStage
{
    public override string Name => "DotNet Publisher";
    public override int Priority => 1000; // Самый высокий приоритет, так как это база

    public override void Execute(BuildContext ctx)
    {
        // 1. Используем пути из контекста
        var slnPath = Path.Combine(ctx.ProjectDir, "FlappyBird.sln");

        // 2. Определяем папку временной сборки
        string tempPublishDir = Path.Combine(ctx.ProjectDir, "obj", "vecxy", "publish");

        if (Directory.Exists(tempPublishDir))
            Directory.Delete(tempPublishDir, true);

        // 3. Формируем аргументы для dotnet publish
        // -o: куда сложить результат
        // -c: Debug/Release
        // --self-contained:false (т.к. у нас есть свой Runner.exe)
        var arguments = $"publish \"{slnPath}\" -c {ctx.Configuration} -o \"{tempPublishDir}\" --self-contained false";

        Console.WriteLine($"[Build] Running: dotnet {arguments}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        process!.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine($"> {e.Data}"); };
        process.BeginOutputReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Compilation failed with exit code {process.ExitCode}");
        }

        // 4. Записываем артефакты для следующих стадий
        // Теперь RunnerStage будет знать, откуда брать файлы игры
        ctx.SetArtifact("RawBuildDir", tempPublishDir);

        // Находим главную DLL игры
        string gameDllName = "FlappyBird.Game.dll"; // Потом это можно вычислять динамически
        ctx.SetArtifact("GameDllPath", Path.Combine(tempPublishDir, gameDllName));
    }
}