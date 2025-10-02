using System.Numerics;

namespace Vecxy.Rendering;

public interface ID2RenderContext : IRenderContext
{
    public void BeginBatch();
    public void EndBatch();
    public void Flush();

    public void AddSpriteToBatch(Sprite sprite);
}