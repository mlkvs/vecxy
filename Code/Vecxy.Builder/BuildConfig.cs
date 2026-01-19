using System.Runtime.Serialization;

namespace Vecxy.Builder;

public enum BUILD_MODE : byte
{
    RELEASE,
    DEBUG
}

[DataContract]
public class BuildConfig
{
    [DataMember] public required string ProjectDir { get; set; }
    [DataMember] public required string OutputDir { get; set; }
    [DataMember] public required BUILD_MODE Mode { get; set; }
}