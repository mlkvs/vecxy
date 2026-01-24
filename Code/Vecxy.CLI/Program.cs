namespace Vecxy.CLI;


public static class Program
{
    public static void Main(string[] args)
    {
        ICLICommand[] commands =
        [
            new BuildCommand(),
            new NewCommand()
        ];

        CLIParser.Execute(args, commands);
    }
}