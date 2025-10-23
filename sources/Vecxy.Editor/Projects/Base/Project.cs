using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Vecxy.IO;

namespace Vecxy.Editor;

[JsonConverter(typeof(StringEnumConverter))]
public enum PROJECT_TYPE : byte
{
    GAME = 0,
    LIBRARY = 1,
    PACKAGE = 2
}
    
[DataContract]
public struct ProjectInfo
{
    [DataMember(Name = "type")] public PROJECT_TYPE Type { get; set; }
    [DataMember(Name = "name")] public string Name;
    [DataMember(Name = "description")] public string Description;
    [DataMember(Name = "author")] public string Author;
}
    
[DataContract]
public struct ProjectVersion
{
    [DataMember(Name = "game")] public string Game;
    [DataMember(Name = "engine")] public string Engine;
    [DataMember(Name = "editor")] public string Editor;
}

public class NotSupportedProjectTypeException(PROJECT_TYPE type)
    : Exception($"This type: {type} of project is not supported")
{
}

public abstract class Project(string path, ProjectInfo info)
{
    public string Path => path;
    public ProjectInfo Info => info;
    public ProjectVersion Version { get; private set; }

    #region static

    public static ProjectInfo Define(string projectPath)
    {
        var projectFile = System.IO.Path.Combine(projectPath, ".vecxy/project.json");

        if (!File.Exists(projectFile))
        {
            throw new FileNotFoundException("project file not found", projectFile);
        }

        var json = FileReader.ReadToEnd(projectFile);

        var info = JsonConvert.DeserializeObject<ProjectInfo>(json);

        return info;
    }

    #endregion

    public virtual void Open()
    {
        var vecxy = System.IO.Path.Combine(Path, ".vecxy");

        var versionPath = System.IO.Path.Combine(vecxy, "version.json");
        var versionJson = FileReader.ReadToEnd(versionPath);
        
        Version = JsonConvert.DeserializeObject<ProjectVersion>(versionJson);
    }

    public virtual void Save()
    {
        
    }
}