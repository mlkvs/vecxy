using System.Diagnostics;
using Vecxy.Builder;
using Vecxy.Diagnostics;
using Vecxy.Engine;
using Vecxy.IO;

namespace Vecxy.CLI;

/*
 *  vecxy new
 *      -p | --project ./some/path/folder (default: .)
 *      -t | --type GAME | LIB (default: GAME)
 *      -n | --name
 *      -d | --description (optional)
 *      -a | --author (optional)
 */

public struct NewProjectParameters
{
    [CLIParameter(Name = "project", Alias = 'p', Default = ".")]
    public string ProjectDir { get; set; }

    [CLIParameter(Name = "type", Alias = 't', Default = PROJECT_TYPE.GAME)]
    public PROJECT_TYPE Type { get; set; }

    [CLIParameter(Name = "name", Alias = 'n')]
    public string Name { get; set; }

    [CLIParameter(Name = "description", Alias = 'd', Default = "No Description")]
    public string Description { get; set; }

    [CLIParameter(Name = "author", Alias = 'a', Default = "No Author")]
    public string Author { get; set; }
}

public class NewCommand : CLICommandBase<NewProjectParameters>
{
    public override string Name => "new";

    public override void Execute(NewProjectParameters parameters)
    {
        var projectDir = parameters.ProjectDir == "." ? Directory.GetCurrentDirectory() : parameters.ProjectDir;

        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        var exeFolder = Path.GetDirectoryName(exePath);

        if (exeFolder == null)
        {
            Logger.Error("Not found .exe directory");
            return;
        }

        var gameTemplateDir = Path.Combine(exeFolder, "Templates/Game");

        DirectoryUtils.CopyDirectory(gameTemplateDir, projectDir);

        DirectoryUtils.RenameAllNamesAndContent(projectDir, "VECXY_GAME_NAME", parameters.Name);
        DirectoryUtils.RenameAllNamesAndContent(projectDir, "VECXY_DESCRIPTION", parameters.Description);
        DirectoryUtils.RenameAllNamesAndContent(projectDir, "VECXY_AUTHOR", parameters.Author);
        DirectoryUtils.RenameAllNamesAndContent(projectDir, "VECXY_GAME_TYPE", parameters.Type.ToString());
        DirectoryUtils.RenameAllNamesAndContent(projectDir, "VECXY_ENGINE_PATH", exeFolder);
        DirectoryUtils.RenameAllNamesAndContent(projectDir, "VECXY_VERSION", "0.0.1");

        Logger.Error("Success. Project created.");
    }
}