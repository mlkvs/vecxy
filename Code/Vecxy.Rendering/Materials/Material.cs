using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Rendering;

public sealed class Material : IDisposable
{
    private readonly Dictionary<string, MaterialParameter> _overrides =
        new(StringComparer.Ordinal);
    private readonly AssetRef<MaterialAsset>? _source;
    private readonly AssetRef<ShaderAsset>? _embeddedShader;
    private readonly IReadOnlyDictionary<string, MaterialParameter>? _embeddedParameters;
    private readonly string _sourcePath;
    private EMaterialSurface? _surfaceOverride;
    private float? _alphaCutoffOverride;
    private bool _disposed;

    public Material(AssetRef<MaterialAsset> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source.Acquire();
        _sourcePath = _source.Metadata.Path;
    }

    internal Material(
        AssetRef<ShaderAsset> shader,
        IDictionary<string, MaterialParameter> parameters,
        string sourcePath,
        EMaterialSurface surface = EMaterialSurface.Opaque,
        float alphaCutoff = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(shader);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        _embeddedShader = shader.Acquire();
        _embeddedParameters =
            new Dictionary<string, MaterialParameter>(
                parameters,
                StringComparer.Ordinal);
        _sourcePath = sourcePath;
        _surfaceOverride = surface;
        _alphaCutoffOverride = alphaCutoff;
    }

    internal AssetRef<MaterialAsset>? Source => _source;
    internal AssetRef<ShaderAsset> ShaderSource =>
        _source?.Value.Shader
        ?? _embeddedShader
        ?? throw new InvalidOperationException("Material has no shader source.");

    public string SourcePath => _sourcePath;

    public EMaterialSurface Surface
    {
        get
        {
            ThrowIfDisposed();
            return _surfaceOverride ?? _source?.Value.Surface ?? EMaterialSurface.Opaque;
        }
        set
        {
            ThrowIfDisposed();
            _surfaceOverride = value;
        }
    }

    public float AlphaCutoff
    {
        get
        {
            ThrowIfDisposed();
            return _alphaCutoffOverride ?? _source?.Value.AlphaCutoff ?? 0.5f;
        }
        set
        {
            ThrowIfDisposed();
            if (value is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _alphaCutoffOverride = value;
        }
    }

    public IEnumerable<KeyValuePair<string, MaterialParameter>> Parameters
    {
        get
        {
            ThrowIfDisposed();

            var yielded = new HashSet<string>(StringComparer.Ordinal);

            foreach (var pair in BaseParameters)
            {
                yielded.Add(pair.Key);
                yield return KeyValuePair.Create(pair.Key, GetParameter(pair.Key));
            }

            foreach (var pair in _overrides)
            {
                if (yielded.Add(pair.Key))
                    yield return pair;
            }
        }
    }

    public bool IsOverridden(string name)
    {
        ThrowIfDisposed();
        return _overrides.ContainsKey(name);
    }

    public MaterialParameter GetParameter(string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_overrides.TryGetValue(name, out var value))
            return value;

        if (BaseParameters.TryGetValue(name, out value))
            return value;

        throw new KeyNotFoundException(
            $"Material '{_sourcePath}' has no parameter '{name}'.");
    }

    public void SetVector(string name, Vector4 value)
    {
        SetOverride(name, new VectorMaterialParameter(value));
    }

    public void SetFloat(string name, float value)
    {
        SetOverride(name, new FloatMaterialParameter(value));
    }

    public void SetTexture(string name, AssetRef<TextureAsset> texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var current = GetTextureParameter(name);
        SetOverride(
            name,
            current switch
            {
                TextureMaterialParameter existing =>
                    new TextureMaterialParameter(
                        texture.Acquire(),
                        existing.Tiling,
                        existing.Offset,
                        existing.Sampler),

                EmbeddedTextureMaterialParameter existing =>
                    new TextureMaterialParameter(
                        texture.Acquire(),
                        existing.Tiling,
                        existing.Offset,
                        existing.Sampler),

                _ => throw new InvalidOperationException(
                    $"Material parameter '{name}' is not a texture.")
            });
    }

    public void SetTextureTransform(
        string name,
        Vector2 tiling,
        Vector2 offset)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var current = GetTextureParameter(name);
        SetOverride(
            name,
            current switch
            {
                TextureMaterialParameter texture =>
                    new TextureMaterialParameter(
                        texture.Texture.Acquire(),
                        tiling,
                        offset,
                        texture.Sampler),

                EmbeddedTextureMaterialParameter texture =>
                    new EmbeddedTextureMaterialParameter(
                        texture.Texture,
                        tiling,
                        offset,
                        texture.Sampler),

                _ => throw new InvalidOperationException(
                    $"Material parameter '{name}' is not a texture.")
            });
    }

    public Material Clone()
    {
        ThrowIfDisposed();

        var copy = _source is not null
            ? new Material(_source)
            : new Material(
                _embeddedShader!,
                new Dictionary<string, MaterialParameter>(BaseParameters, StringComparer.Ordinal),
                _sourcePath,
                Surface,
                AlphaCutoff);

        foreach (var (name, parameter) in _overrides)
        {
            copy._overrides[name] = CloneParameter(parameter);
        }

        copy._surfaceOverride = _surfaceOverride;
        copy._alphaCutoffOverride = _alphaCutoffOverride;

        return copy;
    }

    public void ClearOverride(string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_overrides.Remove(name, out var parameter))
            DisposeParameter(parameter);
    }

    public void ClearOverrides()
    {
        ThrowIfDisposed();

        foreach (var parameter in _overrides.Values)
            DisposeParameter(parameter);

        _overrides.Clear();
        _surfaceOverride = null;
        _alphaCutoffOverride = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        ClearOverrides();
        _disposed = true;
        _source?.Dispose();
        _embeddedShader?.Dispose();
        if (_embeddedParameters is not null)
        {
            foreach (var parameter in _embeddedParameters.Values)
                DisposeParameter(parameter);
        }
    }

    private void SetOverride(string name, MaterialParameter parameter)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(parameter);

        if (_overrides.TryGetValue(name, out var previous))
            DisposeParameter(previous);

        _overrides[name] = parameter;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private IReadOnlyDictionary<string, MaterialParameter> BaseParameters =>
        _source?.Value.Parameters
        ?? _embeddedParameters
        ?? throw new InvalidOperationException("Material has no parameters.");

    private MaterialParameter GetTextureParameter(string name)
    {
        var parameter = GetParameter(name);
        return parameter switch
        {
            TextureMaterialParameter => parameter,
            EmbeddedTextureMaterialParameter => parameter,
            _ => throw new InvalidOperationException(
                $"Material parameter '{name}' is not a texture.")
        };
    }

    private static void DisposeParameter(MaterialParameter parameter)
    {
        switch (parameter)
        {
            case TextureMaterialParameter texture:
                texture.Texture.Dispose();
                break;
            case EmbeddedTextureMaterialParameter:
                break;
        }
    }

    private static MaterialParameter CloneParameter(MaterialParameter parameter)
    {
        return parameter switch
        {
            TextureMaterialParameter texture =>
                new TextureMaterialParameter(
                    texture.Texture.Acquire(),
                    texture.Tiling,
                    texture.Offset,
                    texture.Sampler),
            EmbeddedTextureMaterialParameter texture =>
                new EmbeddedTextureMaterialParameter(
                    texture.Texture,
                    texture.Tiling,
                    texture.Offset,
                    texture.Sampler),
            VectorMaterialParameter vector =>
                new VectorMaterialParameter(vector.Value),
            FloatMaterialParameter scalar =>
                new FloatMaterialParameter(scalar.Value),
            _ => throw new NotSupportedException(
                $"Unsupported material parameter type '{parameter.GetType().Name}'.")
        };
    }
}
