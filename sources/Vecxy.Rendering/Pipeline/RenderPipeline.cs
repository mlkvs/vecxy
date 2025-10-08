namespace Vecxy.Rendering;

public class RenderPipeline(IRenderWindow window) : IDisposable
{
    private readonly Dictionary<RENDER_PHASE_TYPE, IRenderPhase> _phases = new();
    
    public void RegisterRenderable(IRenderable renderable)
    {
        if (_phases.TryGetValue(renderable.RenderPhase, out var phase) == false)
        {
            throw new InvalidOperationException($"Render phase '{renderable.RenderPhase}' is not registered.");
        }
        
        phase.RegisterRenderable(renderable);
    }
    
    public void RegisterPhase(IRenderPhase phase)
    {
        phase.OnInitialize();
        
        _phases[phase.Type] = phase;
    }

    public void Render()
    {
        window.Clear();
        
        foreach (var (_, phase) in _phases)
        {
            phase.OnRender();
        }
        
        window.SwapBuffers();
    }

    public void Dispose()
    {
        foreach (var (_, phase) in _phases)
        {
            phase.Dispose();
        }
        
        _phases.Clear();
    }
}