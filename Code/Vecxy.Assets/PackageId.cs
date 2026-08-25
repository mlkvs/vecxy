using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vecxy.Assets;

[JsonConverter(typeof(PackageIdJsonConverter))]
public readonly record struct PackageId(Guid Value)
{
    public static PackageId Game { get; } = FromName("game");
    public bool IsEmpty => Value == Guid.Empty;

    public static PackageId FromName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"vecxy:vpack:{name.Trim().ToLowerInvariant()}"));
        return new PackageId(new Guid(hash.AsSpan(0, 16)));
    }

    public override string ToString() => Value.ToString("D");
}

public sealed class PackageIdJsonConverter : JsonConverter<PackageId>
{
    public override PackageId Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new PackageId(Guid.Parse(reader.GetString() ?? throw new JsonException("Package ID is missing.")));
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.TryGetProperty("value", out var value) && value.TryGetGuid(out var id)) return new PackageId(id);
        }
        throw new JsonException("Package ID must be a GUID string.");
    }

    public override void Write(Utf8JsonWriter writer, PackageId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public enum PackageLoadMode : byte
{
    Startup = 0,
    OnDemand = 1
}

public enum VPackPlatform : byte
{
    Windows = 1,
    Linux = 2,
    Android = 3
}

public enum VPackCompressionAlgorithm : byte
{
    None = 0,
    Lz4 = 1,
    Zstd = 2
}

public readonly record struct VPackCompressionSettings(
    VPackCompressionAlgorithm Algorithm,
    int Level,
    int BlockSize);
