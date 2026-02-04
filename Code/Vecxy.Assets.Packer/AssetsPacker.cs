using System.Runtime.Serialization;

namespace Vecxy.Assets.Packer;

[DataContract]
public class PackConfig
{
    [DataMember] public string PackDir { get; set; }
    [DataMember] public string OutputDir { get; set; }
}

public static class AssetsPacker
{
    public static AssetPack Pack(PackConfig cfg)
    {
        throw new NotImplementedException();
    }
}