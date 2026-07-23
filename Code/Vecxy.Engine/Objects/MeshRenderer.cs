using Vecxy.Assets;
using Vecxy.Rendering;

namespace Vecxy.Engine.Objects;

public sealed class MeshRenderer(ModelPrimitive meshData) : Script
{
    public ModelPrimitive MeshData { get; } = meshData ?? throw new ArgumentNullException(nameof(meshData));
    public bool IsVisible { get; set; } = true;
    public bool IsStatic { get; set; } = true;
    internal Mesh? Mesh { get; private set; }

    internal void Prepare(Func<ModelPrimitive, Mesh> getMesh)
    {
        if (Mesh is not null) return;
        Mesh = getMesh(MeshData);
    }

    public override void OnDestroy()
    {
        Mesh = null;
    }
}
