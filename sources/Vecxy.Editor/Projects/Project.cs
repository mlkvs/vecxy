using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Vecxy.Editor;

[JsonConverter(typeof(StringEnumConverter))]
public enum PROJECT_TYPE : byte
{
    GAME = 0,
    LIBRARY = 1,
    PACKAGE = 2
}

[DataContract]
public struct ProjectVersion
{
    [DataMember(Name = "game")] public string Game;
    [DataMember(Name = "engine")] public string Engine;
}

[DataContract]
public struct ProjectInfo
{
    [DataMember(Name = "name")] public string Name;
    [DataMember(Name = "description")] public string Description;
    [DataMember(Name = "author")] public string Author;

    [DataMember(Name = "version")] public ProjectVersion Version;
}

[DataContract]
public struct ProjectFile
{
    [DataMember(Name = "type")] public PROJECT_TYPE Type { get; set; }
    [DataMember(Name = "info")] public ProjectInfo Info { get; set; }
}


public class Project(string path, ProjectFile file)
{
    public string Path => path;
    public ProjectFile ProjectFile => file;
}