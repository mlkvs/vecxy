using Vecxy.Builder;
using Vecxy.Diagnostics;
using Vecxy.Engine;

namespace Vecxy.CLI;

/*
 *  vecxy build
 *      -p | --project ./some/path/folder (default: .)
 *      -e | --env DEBUG | RELEASE (default DEBUG)
 *      -o | --output ./some/path/folder (default: ./build)
 *      -c | --config ./some/config.json (optional)
 */

public struct BuildParameters
{
    [CLIParameter(Name = "project", Alias = 'p')]
    public string ProjectDir { get; set; }

    [CLIParameter(Name = "output", Alias = 'o')]
    public string OutputDir { get; set; }

    [CLIParameter(Name = "configuration", Alias = 'c', Default = BUILD_CONFIGURATION.RELEASE)]
    public BUILD_CONFIGURATION Configuration { get; set; }
}

public class BuildCommand : CLICommandBase<BuildParameters>
{
    public override string Name => "build";

    public override void Execute(BuildParameters parameters)
    {
        // 1. Разрешаем пути
        var absoluteProjectDir = Path.GetFullPath(parameters.ProjectDir);

        // Используем логику из класса Project: если папка не задана, берем путь из .vecxy/project.json или "Build"
        string absoluteOutputDir;
        if (string.IsNullOrEmpty(parameters.OutputDir))
        {
            var project = new Project(absoluteProjectDir);
            absoluteOutputDir = project.BuildDir; // Путь к /Build в папке проекта
        }
        else
        {
            absoluteOutputDir = Path.GetFullPath(parameters.OutputDir);
        }

        // 2. Создаем конфиг для пайплайна
        var config = new BuildConfig
        {
            ProjectDir = absoluteProjectDir,
            OutputDir = absoluteOutputDir,
            Configuration = parameters.Configuration
        };

        Logger.Info($"Starting build for project: {absoluteProjectDir}");
        Logger.Info($"Configuration: {parameters.Configuration}");
        Logger.Info($"Output path: {absoluteOutputDir}");

        // 3. Запуск
        BuildPipeline.Build(config);

        Logger.Info("Build success!");
    }
}