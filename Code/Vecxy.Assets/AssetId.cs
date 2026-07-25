using System.Security.Cryptography;
using System.Text;

namespace Vecxy.Assets;

public readonly record struct AssetId(Guid Value)
{
    public static readonly AssetId Empty = default;

    public bool IsEmpty => Value == Guid.Empty;

    public static AssetId New() => new(Guid.NewGuid());

    public static AssetId FromPath(string path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return new AssetId(new Guid(bytes.AsSpan(0, 16)));
    }

    public override string ToString() => Value.ToString("N");
}
