namespace Vecxy.Assets;

[AttributeUsage(AttributeTargets.All)]
public class AssetPathAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class AssetReferenceAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}
