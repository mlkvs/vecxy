/*
 *  All CLI Commands:
 * 
 *  vecxy run
 *      -p | --project ./some/path/folder (default: .)
 *
 *  vecxy build
 *      -p | --project ./some/path/folder (default: .)
 *      -m | --mode DEBUG | RELEASE (default DEBUG)
 *      -o | --output ./some/path/folder (default: ./build)
 *      -c | --config ./some/config.json (optional)
 *
 *  vecxy new
 *      -p | --project ./some/path/folder (default: .)
 *      -t | --type GAME | LIB (default: GAME)
 *      -n | --name (optional)
 *      -d | --description (optional)
 */

namespace Vecxy.CLI;

public struct BuildParameters
{
    [CLIParameter(Name = "project", Alias = "p")]
    public string ProjectPath;

    [CLIParameter(Name = "mode", Alias = "m", Default = "DEBUG")]
    public string Mode;

    [CLIParameter(Name = "output", Alias = "o")]
    public string OutputPath;

    [CLIParameter(Name = "config", Alias = "c")]
    public string ConfigPath;
}

public class Program
{
    public static void Main(string[] args)
    {
        var commands = new[]
        {
            CLIParser.Command<BuildParameters>("build", parameters =>
            {
                Console.WriteLine(parameters.Mode);
            })
        };

        CLIParser.Parse(args, commands);
    }
}