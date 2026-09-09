using Vecxy.Assets;

namespace Vecxy.Rendering;

public sealed class Model : IDisposable
{
    private readonly AssetRef<ModelAsset> _source;
    private bool _disposed;

    public Model(AssetRef<ModelAsset> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source.Acquire();
    }

    internal AssetRef<ModelAsset> Source => _source;

    public int Version => _source.Version;

    public IReadOnlyList<ModelNode> Nodes => _source.Value.Nodes;

    public IReadOnlyList<ModelMesh> Meshes => _source.Value.Meshes;

    public IReadOnlyList<ModelMaterial> Materials => _source.Value.Materials;

    public IReadOnlyList<ModelLight> Lights => _source.Value.Lights;

    public IReadOnlyList<ModelSkin> Skins => _source.Value.Skins;

    public IReadOnlyList<ModelAnimation> Animations => _source.Value.Animations;

    public IReadOnlyList<int> RootNodes => _source.Value.RootNodes;

    public ModelAnimation GetAnimation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Animations.FirstOrDefault(animation => string.Equals(
                   animation.Name,
                   name,
                   StringComparison.Ordinal))
               ?? throw new KeyNotFoundException(
                   $"Model '{_source.Metadata.Path}' has no animation '{name}'.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _source.Dispose();
    }
}
