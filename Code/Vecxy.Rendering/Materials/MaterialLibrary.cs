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

    public Shader Bind(Material material)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _binder.Bind(material);
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
