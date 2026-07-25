using Vecxy.Assets;
using Vecxy.Scene;

namespace Vecxy.Rendering;

public sealed class MeshRenderer : AComponent
{
    private AssetRef<ModelAsset>? _model;
    private AssetRef<MaterialAsset>? _material;

    public int MeshIndex { get; private set; } = -1;

    public ERenderPhase Phase { get; set; } =
        ERenderPhase.Opaque;

    public bool IsConfigured =>
        _model is not null &&
        _material is not null &&
        MeshIndex >= 0;

    internal AssetRef<ModelAsset> Model =>
        _model ??
        throw new InvalidOperationException(
            "MeshRenderer has no model.");

    internal AssetRef<MaterialAsset> Material =>
        _material ??
        throw new InvalidOperationException(
            "MeshRenderer has no material.");

    public void SetMesh(
        AssetRef<ModelAsset> model,
        int meshIndex,
        AssetRef<MaterialAsset> material)
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(material);

        if (model.HasError)
            throw new InvalidOperationException(
                $"Cannot configure MeshRenderer with failed model '{model.Metadata.Path}'.",
                model.Error);

        if (meshIndex < 0 || meshIndex >= model.Value.Meshes.Count)
            throw new ArgumentOutOfRangeException(nameof(meshIndex));

        var nextModel = model.Acquire();
        AssetRef<MaterialAsset>? nextMaterial = null;

        try
        {
            nextMaterial = material.Acquire();
        }
        catch
        {
            nextModel.Dispose();
            throw;
        }

        var previousModel = _model;
        var previousMaterial = _material;

        _model = nextModel;
        _material = nextMaterial;
        MeshIndex = meshIndex;

        previousModel?.Dispose();
        previousMaterial?.Dispose();
    }

    protected override void OnDestroy()
    {
        _model?.Dispose();
        _material?.Dispose();
        _model = null;
        _material = null;
        MeshIndex = -1;
    }
}
