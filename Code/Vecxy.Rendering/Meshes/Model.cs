using Vecxy.Assets;

namespace Vecxy.Rendering;

public sealed class Model : IDisposable
{
    private readonly AssetRef<ModelAsset> _source;
    private bool _disposed;

    internal Model(AssetRef<ModelAsset> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source.Acquire();
    }

    internal AssetRef<ModelAsset> Source => _source;

    public IReadOnlyList<ModelNode> Nodes => _source.Value.Nodes;

    public IReadOnlyList<ModelMesh> Meshes => _source.Value.Meshes;

    public IReadOnlyList<ModelMaterial> Materials => _source.Value.Materials;

    public IReadOnlyList<ModelLight> Lights => _source.Value.Lights;

    public IReadOnlyList<int> RootNodes => _source.Value.RootNodes;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _source.Dispose();
    }
}
