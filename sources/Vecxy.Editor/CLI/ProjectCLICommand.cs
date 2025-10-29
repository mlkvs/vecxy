using Vecxy.CLI;
using Vecxy.Projects;

namespace Vecxy.Editor;

public class ProjectCLICommand(Action<string, string, PROJECT_TYPE> onGenerate, Action<string> onOpen) 
    : CLICommand<ProjectCLICommand.Option>("project", "p", "Project command")
{
    public struct Option
    {
        [CLIOption(LongName = "projectPath", ShortName = "pPath", Required = true)]
        public string ProjectPath { get; set; }
        
        [CLIOption(LongName = "projectName", ShortName = "pName", Required = true)]
        public string ProjectName { get; set; }
        
        [CLIOption(LongName = "projectType", ShortName = "pType", Required = false, Default = PROJECT_TYPE.GAME)]
        public PROJECT_TYPE ProjectType { get; set; }
        
        [CLIOption(LongName = "generate", ShortName = "g", Required = false, Default = false)]
        public bool IsGenerate { get; set; }
    }

    protected override void OnExecute(Option options)
    {
        if (options.IsGenerate)
        {
            onGenerate(options.ProjectName, options.ProjectPath, options.ProjectType);
            return;
        }
        
        onOpen(options.ProjectPath);
    }
}