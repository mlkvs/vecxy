using Vecxy.CLI.Commands;
using Vecxy.Diagnostics;
using Vecxy.Native;

namespace Vecxy.CLI;


public static class Program
{
    public static void Main(string[] args)
    {
        CLIParser.Parse(args, [
            new BuildCommand(),
            new NewCommand(),
            new LinkCommand(),
        ]);

        /*Console.WriteLine("Запуск окна из DLL...");

        var window = new Window(new WindowConfig
        {
            Title = "Demo",
            Width = 800,
            Height = 600
        });

        var i = 0;

        while (window.ProcessEvents())
        {
            i++;
            Logger.Info(i.ToString());
        }

        Console.WriteLine("asdasd");*/
    }
}