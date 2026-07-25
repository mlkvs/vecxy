using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Rendering;

public enum ERenderPhase : byte
{
    Background,
    Opaque,
    Transparent,
    Overlay
}

public interface IRenderTarget
{
    int Width { get; }
    int Height { get; }

    void Bind(GraphicsDevice device);
    void Present();
}

public sealed class GameView
{
    private readonly List<RenderItem> _items = [];

    public IRenderTarget Target { get; }
    public Vector4 ClearColor { get; set; } = new(0.02f, 0.08f, 0.04f, 1f);
    public bool Enabled { get; set; } = true;

    internal IReadOnlyList<RenderItem> Items => _items;

    internal GameView(IRenderTarget target)
    {
        Target = target;
    }

    public RenderItem Submit(
        ERenderPhase phase,
        Mesh mesh,
        AssetRef<MaterialAsset> material,
        Matrix4x4 transform)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);

        var item = new RenderItem(phase, mesh, material.Acquire(), transform);
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

public sealed class RenderItem
{
    private bool _materialReleased;

    public ERenderPhase Phase { get; set; }
    public Mesh Mesh { get; }
    public AssetRef<MaterialAsset> Material { get; }
    public Matrix4x4 Transform { get; set; }
    public bool Enabled { get; set; } = true;

    internal RenderItem(
        ERenderPhase phase,
        Mesh mesh,
        AssetRef<MaterialAsset> material,
        Matrix4x4 transform)
    {
        Phase = phase;
        Mesh = mesh;
        Material = material;
        Transform = transform;
    }

    internal void ReleaseMaterial()
    {
        if (_materialReleased)
        {
            return;
        }

        _materialReleased = true;
        Material.Dispose();
    }
}
