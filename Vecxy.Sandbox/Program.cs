using Vecxy.Engine;

namespace Vecxy.Sandbox;

internal static class Program
{
    public static void Main(string[] args)
    {
        using var engine = new VecxyEngine();
        
        engine.Run();
    }
}