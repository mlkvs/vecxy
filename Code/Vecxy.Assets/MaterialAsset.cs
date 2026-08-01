using System.Collections.ObjectModel;
using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Vecxy.Assets;

public enum EMaterialSurface : byte
{
    Opaque,
    Cutout,
    Transparent
}

public enum ETextureFilter : byte
{
    Nearest,
    Linear
}

public enum ETextureWrap : byte
{
    Repeat,
    Clamp
}

public readonly record struct TextureSamplerState(
    ETextureFilter MinFilter,
    ETextureFilter MagFilter,
    ETextureWrap WrapU,
    ETextureWrap WrapV)
{
    public static TextureSamplerState Default { get; } =
        new(
            ETextureFilter.Nearest,
            ETextureFilter.Nearest,
            ETextureWrap.Repeat,
            ETextureWrap.Repeat);

    public static TextureSamplerState PointClamp { get; } =
        new(
            ETextureFilter.Nearest,
            ETextureFilter.Nearest,
            ETextureWrap.Clamp,
            ETextureWrap.Clamp);
}

public abstract record MaterialParameter;

public sealed record TextureMaterialParameter(
    AssetRef<TextureAsset> Texture,
    Vector2 Tiling,
    Vector2 Offset,
    TextureSamplerState Sampler) : MaterialParameter
{
    public TextureMaterialParameter(AssetRef<TextureAsset> texture)
        : this(texture, Vector2.One, Vector2.Zero, TextureSamplerState.Default)
    {
    }

    public TextureMaterialParameter(
        AssetRef<TextureAsset> texture,
        Vector2 tiling,
        Vector2 offset)
        : this(texture, tiling, offset, TextureSamplerState.Default)
    {
    }
}

public sealed record EmbeddedTextureMaterialParameter(
    TextureAsset Texture,
    Vector2 Tiling,
    Vector2 Offset,
    TextureSamplerState Sampler) : MaterialParameter
{
    public EmbeddedTextureMaterialParameter(TextureAsset texture)
        : this(texture, Vector2.One, Vector2.Zero, TextureSamplerState.Default)
    {
    }

    public EmbeddedTextureMaterialParameter(
        TextureAsset texture,
        Vector2 tiling,
        Vector2 offset)
        : this(texture, tiling, offset, TextureSamplerState.Default)
    {
    }
}

public sealed record VectorMaterialParameter(
    Vector4 Value) : MaterialParameter;

public sealed record FloatMaterialParameter(
    float Value) : MaterialParameter;

public sealed class MaterialAsset : IDisposable
{
    private bool _disposed;

    public AssetRef<ShaderAsset> Shader { get; }
    public EMaterialSurface Surface { get; }
    public float AlphaCutoff { get; }
    public IReadOnlyDictionary<string, MaterialParameter> Parameters { get; }

    internal MaterialAsset(
        AssetRef<ShaderAsset> shader,
        IDictionary<string, MaterialParameter> parameters,
        EMaterialSurface surface,
        float alphaCutoff)
    {
        Shader = shader;
        Surface = surface;
        AlphaCutoff = alphaCutoff;
        Parameters = new ReadOnlyDictionary<string, MaterialParameter>(parameters);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Shader.Dispose();

        foreach (var texture in Parameters.Values.OfType<TextureMaterialParameter>())
        {
            texture.Texture.Dispose();
        }
    }
}

public sealed class MaterialAssetImporter : IAssetImporter<MaterialAsset>
{
    private static readonly IDeserializer Deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    public IReadOnlyCollection<string> Extensions { get; } = [".material"];

    public MaterialAsset Import(
        AssetMetadata metadata,
        AssetImportContext context)
    {
        var descriptor = Deserializer.Deserialize<MaterialDescriptor>(
            context.ReadAllText(metadata.Path))
            ?? throw new InvalidDataException($"Material is empty: {metadata.Path}");

        if (string.IsNullOrWhiteSpace(descriptor.Shader))
        {
            throw new InvalidDataException($"Material has no shader: {metadata.Path}");
        }

        if (descriptor.AlphaCutoff is < 0.0f or > 1.0f)
        {
            throw new InvalidDataException(
                $"Material alphaCutoff in '{metadata.Path}' must be between 0 and 1.");
        }

        var shader = context.Load<ShaderAsset>(descriptor.Shader);
        var parameters = new Dictionary<string, MaterialParameter>(StringComparer.Ordinal);

        try
        {
            foreach (var (name, parameter) in descriptor.Parameters)
            {
                parameters.Add(name, ImportParameter(name, parameter, metadata.Path, context));
            }

            return new MaterialAsset(
                shader,
                parameters,
                descriptor.Surface,
                descriptor.AlphaCutoff);
        }
        catch
        {
            shader.Dispose();
            foreach (var texture in parameters.Values.OfType<TextureMaterialParameter>())
            {
                texture.Texture.Dispose();
            }

            throw;
        }
    }

    private static MaterialParameter ImportParameter(
        string name,
        MaterialParameterDescriptor descriptor,
        string materialPath,
        AssetImportContext context)
    {
        var definedValues =
            (string.IsNullOrWhiteSpace(descriptor.Texture) ? 0 : 1) +
            (descriptor.Color is null ? 0 : 1) +
            (descriptor.Vector is null ? 0 : 1) +
            (descriptor.Float.HasValue ? 1 : 0);

        if (definedValues != 1)
        {
            throw new InvalidDataException(
                $"Material parameter '{name}' in '{materialPath}' must define exactly one value.");
        }

        if (!string.IsNullOrWhiteSpace(descriptor.Texture))
        {
            return new TextureMaterialParameter(
                context.Load<TextureAsset>(descriptor.Texture),
                ToVector2(
                    descriptor.Tiling,
                    Vector2.One,
                    "tiling",
                    name,
                    materialPath),
                ToVector2(
                    descriptor.Offset,
                    Vector2.Zero,
                    "offset",
                    name,
                    materialPath),
                new TextureSamplerState(
                    descriptor.MinFilter ?? descriptor.Filter,
                    descriptor.MagFilter ?? descriptor.Filter,
                    descriptor.WrapU ?? descriptor.Wrap,
                    descriptor.WrapV ?? descriptor.Wrap));
        }

        if (descriptor.Color is not null)
        {
            return new VectorMaterialParameter(
                ToVector(descriptor.Color, name, materialPath));
        }

        if (descriptor.Vector is not null)
        {
            return new VectorMaterialParameter(
                ToVector(descriptor.Vector, name, materialPath));
        }

        return new FloatMaterialParameter(descriptor.Float!.Value);
    }

    private static Vector4 ToVector(
        IReadOnlyList<float> values,
        string name,
        string materialPath)
    {
        if (values.Count != 4)
        {
            throw new InvalidDataException(
                $"Material parameter '{name}' in '{materialPath}' must contain four components.");
        }

        return new Vector4(values[0], values[1], values[2], values[3]);
    }

    private static Vector2 ToVector2(
        IReadOnlyList<float>? values,
        Vector2 fallback,
        string field,
        string name,
        string materialPath)
    {
        if (values is null)
            return fallback;

        if (values.Count != 2)
        {
            throw new InvalidDataException(
                $"Material parameter '{name}' in '{materialPath}' field '{field}' must contain two components.");
        }

        return new Vector2(values[0], values[1]);
    }

    private sealed class MaterialDescriptor
    {
        public string Shader { get; init; } = string.Empty;
        public EMaterialSurface Surface { get; init; } = EMaterialSurface.Opaque;
        public float AlphaCutoff { get; init; } = 0.5f;
        public Dictionary<string, MaterialParameterDescriptor> Parameters { get; init; } = [];
    }

    private sealed class MaterialParameterDescriptor
    {
        public string Texture { get; init; } = string.Empty;
        public List<float>? Color { get; init; }
        public List<float>? Vector { get; init; }
        public float? Float { get; init; }
        public List<float>? Tiling { get; init; }
        public List<float>? Offset { get; init; }
        public ETextureFilter Filter { get; init; } = ETextureFilter.Nearest;
        public ETextureFilter? MinFilter { get; init; }
        public ETextureFilter? MagFilter { get; init; }
        public ETextureWrap Wrap { get; init; } = ETextureWrap.Repeat;
        public ETextureWrap? WrapU { get; init; }
        public ETextureWrap? WrapV { get; init; }
    }
}
