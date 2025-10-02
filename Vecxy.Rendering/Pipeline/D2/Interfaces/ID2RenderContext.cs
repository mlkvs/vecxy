using System.Numerics;

namespace Vecxy.Rendering;

public interface ID2RenderContext : IRenderContext
{
    public void BeginBatch();
    public void EndBatch();
    public void Flush();
    
    public void DrawSprite(Texture texture, Vector2 position, Vector2 size, Vector4 color);
}