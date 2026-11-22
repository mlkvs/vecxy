namespace Vecxy.Rendering;

public sealed class MaterialLibrary : IDisposable
{
    private bool _disposed;
    private readonly MaterialBinder _binder;

    public MaterialLibrary(
        ShaderLibrary shaders,
        TextureLibrary textures)
    {
        _binder = new MaterialBinder(shaders, textures);
    }

    public Shader Bind(Material material) => Bind(material, null, default);

    internal Shader Bind(Material material, Texture? faceAtlas, Vecxy.Assets.TextureSamplerState faceSampler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _binder.Bind(material, faceAtlas, faceSampler);
    }

    public void Clear()
    {
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
