using CommandLine;

namespace Vecxy.Editor;

internal static class Program
{
    public static void Main(string[] args)
    {
        var result = Parser.Default.ParseArguments<EditorOptions>(args);

        if (result.Tag == ParserResultType.NotParsed)
        {
            foreach (var error in result.Errors)
            {
                Console.Error.WriteLine(error);
            }

            Console.ReadLine();
            
            return;
        }
        
        var options = result.Value;
        
        try
        {
            var editor = new Editor();
            
            var projectPath = options.Project.Trim();
            
            if (options.IsGenerate)
            {
                var type = options.Type;
                
                var project = editor.GenerateProject(projectPath, type);
                
                return;
            }

            editor.OpenProject(projectPath);
            
            editor.Run();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}