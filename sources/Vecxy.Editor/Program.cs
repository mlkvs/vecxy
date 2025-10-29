using Vecxy.CLI;
using Vecxy.Diagnostics;
using Vecxy.Projects;

namespace Vecxy.Editor;

internal static class Program
{
    private static Editor? _editor;
    
    public static void Main(string[] args)
    {
        var cli = new CLIProgram
        {
            Commands =
            {
                new HelpCLICommand(),
                new ProjectCLICommand(OnGenerateProject, OnOpenProject)
            }
        };

        var result = cli.Parse(args);

        if (result.Status == CLI.CLIProgram.Result.STATUS.NOT_PARSED)
        {
            foreach (var exception in result.Exceptions)
            {
                Logger.Error(exception);
            }

            Console.ReadLine();
            
            return;
        }
        
        _editor = new Editor();
        
        cli.Execute();
    }

    private static void OnOpenProject(string path)
    {
        if (_editor == null)
        {
            throw new Exception("Editor not initialized");
        }
        
        _editor.OpenProject(path);
        
        _editor.Run();
    }
    
    private static void OnGenerateProject(string name, string path, PROJECT_TYPE type)
    {
        if (_editor == null)
        {
            throw new Exception("Editor not initialized");
        }
        
        _editor.GenerateProject(name, path, type);
    }
}