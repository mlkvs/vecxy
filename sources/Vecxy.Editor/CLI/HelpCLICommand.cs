using System.Reflection;
using System.Text;
using Vecxy.CLI;

namespace Vecxy.Editor;

public class HelpCLICommand() : CLICommand<HelpCLICommand.Options>("help", "h", "List all commands")
{
    public struct Options
    {
        [CLIOption(LongName = "list", ShortName = "l", Required = false, Default = false)]
        public bool IsList { get; set; }
    }

    protected override void OnExecute(Options options)
    {
        if (options.IsList)
        {
            PrintList();
            return;
        }
    }

    private static void PrintList()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var types = assembly
            .GetTypes()
            .Where(t => typeof(ICLICommand)
                .IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .ToList();

        var list = new StringBuilder();

        for (int index = 0, count = types.Count; index < count; ++index)
        {
            var type =  types[index];
            
            var commandTemp = (ICLICommand)Activator.CreateInstance(type)!;
            
            var commandInfo = $"{index + 1}. --{commandTemp.LongName} (--{commandTemp.ShortName}) -> {commandTemp.Description}";

            list.AppendLine(commandInfo);
        }
        
        Console.WriteLine(list);
    }
}