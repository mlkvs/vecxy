using System.Reflection;
using Vecxy.Reflection;

namespace Vecxy.Rendering;

public class RenderPipeline
{
    public void Start()
    {
        var options = new RenderingWindowOptions
        {
            Width = 800,
            Height = 600,
            Title = "Vecxy.Rendering",
        };

        using var window = new RenderingWindow(options);

        window.Load += OnLoad;
        
        window.Run();
    }

    private void OnLoad()
    {
        var assembly = Assembly.GetExecutingAssembly();
        
        var fragment = assembly
            .GetEmbeddedResource("base.frag")
            !.Text();

        var vertex = assembly
            .GetEmbeddedResource("base.vert")
            !.Text();

        var shader = new ShaderProgram(vertex, fragment);
        
        shader.Initialize();
        shader.Compile();
        shader.Link();
    }
}