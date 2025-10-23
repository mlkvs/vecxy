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
            var projectPath = options.Project.Trim();
            
            var editor = new Editor();

            editor.OpenProject(projectPath);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}