namespace Vecxy.CLI;

public static class Program
{
    public static void Main(string[] args)
    {
        var commands = new ICLICommand[]
        {
            new BuildCommand()
        };

        CLIParser.Parse(args, commands);
    }
}