using System.Runtime.Serialization;

namespace Vecxy.Builder;

[DataContract]
public class BuildConfig
{
    [DataMember] public string ProjectDir { get; set; }
    [DataMember] public string OutputDir { get; set; }
    [DataMember] public BUILD_CONFIGURATION Configuration { get; set; }
}