using System.Collections.ObjectModel;
using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Vecxy.Assets;

public abstract record MaterialParameter;

public sealed record TextureMaterialParameter(
    AssetRef<TextureAsset> Texture) : MaterialParameter;

public sealed record VectorMaterialParameter(
    Vector4 Value) : MaterialParameter;

public sealed record FloatMaterialParameter(
    float Value) : MaterialParameter;

public sealed class MaterialAsset : IDisposable
{
    private bool _disposed;

    public AssetRef<ShaderAsset> Shader { get; }
    public IReadOnlyDictionary<string, MaterialParameter> Parameters { get; }

    internal MaterialAsset(
        AssetRef<ShaderAsset> shader,
        IDictionary<string, MaterialParameter> parameters)
    {
        Shader = shader;
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

        var shader = context.Load<ShaderAsset>(descriptor.Shader);
        var parameters = new Dictionary<string, MaterialParameter>(StringComparer.Ordinal);

        try
        {
            foreach (var (name, parameter) in descriptor.Parameters)
            {
                parameters.Add(name, ImportParameter(name, parameter, metadata.Path, context));
            }

            return new MaterialAsset(shader, parameters);
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
                context.Load<TextureAsset>(descriptor.Texture));
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

    private sealed class MaterialDescriptor
    {
        public string Shader { get; init; } = string.Empty;
        public Dictionary<string, MaterialParameterDescriptor> Parameters { get; init; } = [];
    }

    private sealed class MaterialParameterDescriptor
    {
        public string Texture { get; init; } = string.Empty;
        public List<float>? Color { get; init; }
        public List<float>? Vector { get; init; }
        public float? Float { get; init; }
    }
}
