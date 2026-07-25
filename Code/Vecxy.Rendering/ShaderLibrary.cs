using Vecxy.Assets;
using Vecxy.Diagnostics;

namespace Vecxy.Rendering;

public sealed class ShaderLibrary : IDisposable
{
    private readonly IAssetsManager _assets;
    private readonly ShaderCompiler _compiler;
    private readonly Dictionary<AssetId, Entry> _shaders = [];
    private Shader? _fallback;
    private bool _disposed;

    public ShaderLibrary(IAssetsManager assets, ShaderCompiler compiler)
    {
        _assets = assets;
        _compiler = compiler;
        _assets.Unloaded += OnAssetUnloaded;
    }

    public Shader Get(AssetRef<ShaderAsset> asset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(asset);

        if (asset.HasError)
        {
            return GetFallback();
        }

        if (_shaders.TryGetValue(asset.Id, out var entry))
        {
            if (entry.AssetVersion == asset.Version)
            {
                return entry.Shader ?? GetFallback();
            }

            try
            {
                var replacement = _compiler.Compile(asset.Value, asset.Metadata.Path);
                var previous = entry.Shader;
                entry.Shader = replacement;
                entry.AssetVersion = asset.Version;
                previous?.Dispose();
                return replacement;
            }
            catch (Exception exception)
            {
                entry.Shader?.Dispose();
                entry.Shader = null;
                entry.AssetVersion = asset.Version;
                Logger.Error(
                    exception,
                    $"Shader compilation failed, using fallback: {asset.Metadata.Path}");
                return GetFallback();
            }
        }

        try
        {
            var shader = _compiler.Compile(asset.Value, asset.Metadata.Path);
            _shaders.Add(asset.Id, new Entry(asset.Version, shader));
            return shader;
        }
        catch (Exception exception)
        {
            _shaders.Add(asset.Id, new Entry(asset.Version, null));
            Logger.Error(
                exception,
                $"Shader compilation failed, using fallback: {asset.Metadata.Path}");
            return GetFallback();
        }
    }

    public Shader GetFallback()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _fallback ??= _compiler.CompileFallback();
    }

    private void OnAssetUnloaded(AssetId id, Type assetType)
    {
        if (assetType == typeof(ShaderAsset) &&
            _shaders.Remove(id, out var entry))
        {
            entry.Shader?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _assets.Unloaded -= OnAssetUnloaded;
        foreach (var entry in _shaders.Values)
        {
            entry.Shader?.Dispose();
        }

        _shaders.Clear();
        _fallback?.Dispose();
        _fallback = null;
    }

    private sealed class Entry(int assetVersion, Shader? shader)
    {
        public int AssetVersion { get; set; } = assetVersion;
        public Shader? Shader { get; set; } = shader;
    }
}
