namespace Vecxy.Editor;

public class EditorOptions
{
    [CommandLine.Option('p', "project", Required = true, HelpText = "The project to use.")]
    public string Project { get; set; }
}