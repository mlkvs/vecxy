namespace Vecxy.Assets;

[AttributeUsage(AttributeTargets.All)]
public class AssetPathAttribute(string path) : Attribute
{
    public string Path { get; } = path;
}