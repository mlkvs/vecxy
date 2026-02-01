using Vecxy.Builder;

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
    public BUILD_CONFIGURATION Env { get; set; }
}

public class BuildCommand : CLICommandBase<BuildParameters>
{
    public override string Name => "build";

    public override void Execute(BuildParameters parameters)
    {
        var config = new BuildConfig
        {
            ProjectDir = parameters.ProjectDir,
            OutputDir = parameters.OutputDir,
            Configuration = parameters.Env
        };

        //BuildPipeline.Build(config);
    }
}