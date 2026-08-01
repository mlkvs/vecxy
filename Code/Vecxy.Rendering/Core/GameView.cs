using System.Numerics;
namespace Vecxy.Rendering;

public sealed class GameView
{
    private readonly List<RenderItem> _items = [];

    public IRenderTarget Target { get; }
    public Vector4 ClearColor { get; set; } = new(0.02f, 0.08f, 0.04f, 1f);
    public bool Enabled { get; set; } = true;

    internal IReadOnlyList<RenderItem> Items => _items;
    internal bool UsesGameOutputTarget { get; }

    internal GameView(
        IRenderTarget target,
        bool usesGameOutputTarget = false)
    {
        Target = target;
        UsesGameOutputTarget = usesGameOutputTarget;
    }

    public RenderItem Submit(
        ERenderPhase phase,
        Mesh mesh,
        Material material,
        Matrix4x4 transform)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);

        var item = new RenderItem(phase, mesh, material, transform);
        _items.Add(item);
        return item;
    }

    public bool Remove(RenderItem item)
    {
        if (!_items.Remove(item))
        {
            return false;
        }

        item.ReleaseMaterial();
        return true;
    }

    public void Clear()
    {
        foreach (var item in _items)
        {
            item.ReleaseMaterial();
        }

        _items.Clear();
    }
}
