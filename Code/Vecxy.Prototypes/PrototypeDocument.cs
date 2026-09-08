using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Vecxy.Prototypes;

public sealed class PrototypeDocument
{
    public const int CurrentFormat = 1;
    public int Format { get; set; } = CurrentFormat;
    public required string Type { get; set; }
    public string? Prototype { get; set; }
    public YamlMappingNode Data { get; set; } = [];
}

public sealed class PrototypeAsset
{
    public required PrototypeDocument Document { get; init; }
}

public sealed class PrototypeOverrides
{
    public YamlMappingNode Data { get; }
    public PrototypeOverrides(YamlMappingNode data) => Data = data ?? throw new ArgumentNullException(nameof(data));
}

public static class PrototypeSerializer
{
    private static readonly ISerializer ValueSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
    private static readonly IDeserializer ValueDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).Build();

    public static PrototypeDocument Deserialize(string source, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var yaml = new YamlStream();
        try { using var reader = new StringReader(source); yaml.Load(reader); }
        catch (Exception exception)
        { throw new InvalidDataException($"Malformed prototype '{path ?? "<memory>"}': {exception.Message}", exception); }
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidDataException($"Prototype '{path ?? "<memory>"}' must contain one YAML mapping.");
        var format = int.TryParse(ReadScalar(root, "format"), out var value) ? value : PrototypeDocument.CurrentFormat;
        if (format != PrototypeDocument.CurrentFormat)
            throw new InvalidDataException($"Unsupported prototype format {format} in '{path ?? "<memory>"}'.");
        var type = ReadScalar(root, "type");
        if (string.IsNullOrWhiteSpace(type))
            throw new InvalidDataException($"Prototype '{path ?? "<memory>"}' has no type.");
        var data = Get(root, "data") switch
        {
            null => new YamlMappingNode(),
            YamlMappingNode mapping => CloneMapping(mapping),
            _ => throw new InvalidDataException($"Prototype data in '{path ?? "<memory>"}' must be a mapping.")
        };
        return new PrototypeDocument { Format = format, Type = type, Prototype = ReadScalar(root, "prototype"), Data = data };
    }

    public static string Serialize(PrototypeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Type);
        var root = new YamlMappingNode { { "format", document.Format.ToString() }, { "type", document.Type } };
        if (!string.IsNullOrWhiteSpace(document.Prototype)) root.Add("prototype", document.Prototype);
        root.Add("data", CloneMapping(document.Data));
        var stream = new YamlStream(new YamlDocument(root));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    public static YamlMappingNode SerializeOptions(object options)
    {
        var yaml = new YamlStream();
        using var reader = new StringReader(ValueSerializer.Serialize(options));
        yaml.Load(reader);
        return yaml.Documents[0].RootNode as YamlMappingNode ?? new YamlMappingNode();
    }

    public static object DeserializeOptions(YamlMappingNode data, Type optionsType)
    {
        var yaml = new YamlStream(new YamlDocument(CloneMapping(data)));
        using var writer = new StringWriter();
        yaml.Save(writer, assignAnchors: false);
        using var reader = new StringReader(writer.ToString());
        return ValueDeserializer.Deserialize(reader, optionsType)
               ?? throw new InvalidDataException($"Could not deserialize prototype options '{optionsType.FullName}'.");
    }

    public static YamlMappingNode Merge(YamlMappingNode defaults, YamlMappingNode overrides)
    {
        var result = CloneMapping(defaults);
        foreach (var pair in overrides.Children)
        {
            if (result.Children.TryGetValue(pair.Key, out var current) && current is YamlMappingNode currentMap &&
                pair.Value is YamlMappingNode overrideMap)
                result.Children[pair.Key] = Merge(currentMap, overrideMap);
            else result.Children[pair.Key] = Clone(pair.Value);
        }
        return result;
    }

    private static YamlNode? Get(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;
    private static string? ReadScalar(YamlMappingNode map, string key) =>
        Get(map, key) is YamlScalarNode scalar ? scalar.Value : null;
    public static YamlMappingNode CloneMapping(YamlMappingNode node) => (YamlMappingNode)Clone(node);
    private static YamlNode Clone(YamlNode node) => node switch
    {
        YamlScalarNode scalar => new YamlScalarNode(scalar.Value) { Style = scalar.Style },
        YamlSequenceNode sequence => new YamlSequenceNode(sequence.Children.Select(Clone)) { Style = sequence.Style },
        YamlMappingNode mapping => new YamlMappingNode(mapping.Children.Select(x =>
            new KeyValuePair<YamlNode, YamlNode>(Clone(x.Key), Clone(x.Value)))) { Style = mapping.Style },
        _ => throw new NotSupportedException($"Unsupported YAML node {node.GetType().Name}.")
    };
}
