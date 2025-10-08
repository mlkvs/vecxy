namespace Vecxy.Rendering;

public abstract class RenderContextBase(IRenderWindow window) : IRenderContext
{
    public IRenderWindow Window => window;
}