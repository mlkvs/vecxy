using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Rendering;

public sealed class MeshRenderer : AComponent, ILocalBoundsProvider
{
    private Mesh? _mesh;
    private Material? _material;

    public ERenderPhase Phase { get; set; } =
        ERenderPhase.Opaque;

    public bool IsConfigured =>
        _mesh is not null &&
        _material is not null;

    public Mesh Mesh =>
        _mesh ??
        throw new InvalidOperationException(
            "MeshRenderer has no mesh.");

    public Material Material =>
        _material ??
        throw new InvalidOperationException(
            "MeshRenderer has no material.");

    public Vector3 LocalBoundsMin => Mesh.BoundsMin;

    public Vector3 LocalBoundsMax => Mesh.BoundsMax;

    public Vector3 LocalBoundsSize => Mesh.BoundsSize;

    public Vector3 LocalBoundsCenter => Mesh.BoundsCenter;

    public void SetMesh(
        Mesh mesh,
        Material material)
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(material);

        _material?.Dispose();
        _mesh = mesh;
        _material = material;
    }

    public void SetMesh(Mesh mesh)
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        ArgumentNullException.ThrowIfNull(mesh);

        _mesh = mesh;
    }

    public void SetMaterial(Material material)
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        ArgumentNullException.ThrowIfNull(material);

        _material?.Dispose();
        _material = material;
    }

    public override void OnDestroy()
    {
        _material?.Dispose();
        _mesh = null;
        _material = null;
    }
}
