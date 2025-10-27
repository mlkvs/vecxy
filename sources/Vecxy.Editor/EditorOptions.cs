using Vecxy.Projects;

namespace Vecxy.Editor;

public class EditorOptions
{
    [CommandLine.Option('p', "project", Required = true, HelpText = "The project to use.")]
    public string Project { get; set; }
    
    [CommandLine.Option("generate", Required = false, HelpText = "Generate?", Default = false)]
    public bool IsGenerate { get; set; }
    
    [CommandLine.Option("projectType", Required = false, HelpText = "Project Type", Default = PROJECT_TYPE.GAME)]
    public PROJECT_TYPE Type { get; set; }
}