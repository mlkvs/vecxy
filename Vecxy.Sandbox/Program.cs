using Vecxy.Rendering;

namespace Vecxy.Sandbox;

internal static class Program
{
    public static void Main(string[] args)
    {
        var render = new RenderPipeline();
        
        render.Start();
    }
}