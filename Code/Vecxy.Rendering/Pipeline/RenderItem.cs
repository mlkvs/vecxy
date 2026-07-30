using System.Numerics;

namespace Vecxy.Rendering;

public sealed class RenderItem
{
    public ERenderPhase Phase { get; set; }
    public Mesh Mesh { get; }
    public Material Material { get; }
    public Matrix4x4 Transform { get; set; }
    public bool Enabled { get; set; } = true;

    internal RenderItem(
        ERenderPhase phase,
        Mesh mesh,
        Material material,
        Matrix4x4 transform)
    {
        Phase = phase;
        Mesh = mesh;
        Material = material;
        Transform = transform;
    }

    internal void ReleaseMaterial()
    {
    }
}