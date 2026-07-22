namespace Vecxy.CLI;


public static class Program
{
    public static void Main(string[] args)
    {
        ICLICommand[] commands = [];

        CLIParser.Execute(args, commands);
    }
}