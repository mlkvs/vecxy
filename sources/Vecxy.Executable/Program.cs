using Vecxy.Engine;

internal class Program
{
    public static void Main(string[] args)
    {
        var project = new Project
        {
            EntryPointDLL = @"D:\Projects\vecxy.flappy-bird\sources\FlappyBird.Game\bin\Debug\net8.0\FlappyBird.Game.dll"
        };

        var vecxy = new Engine(project);
        
        vecxy.Run();
    }
}