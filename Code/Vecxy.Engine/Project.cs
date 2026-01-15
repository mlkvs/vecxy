using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Vecxy.IO;

namespace Vecxy.Engine;

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
    [DataMember(Name = "type")] public PROJECT_TYPE Type;
    [DataMember(Name = "name")] public string Name;
    [DataMember(Name = "description")] public string Description;
    [DataMember(Name = "author")] public string Author;
}
    
[DataContract]
public struct ProjectVersion
{
    [DataMember(Name = "game")] public string Game;
    [DataMember(Name = "engine")] public string Engine;
}


[DataContract]
public class Project
{
    public string BuildDir => GetReserverPath(Path, RESERVED_PATH.BUILD_DIRECTORY);
    public string AssetsDir => GetReserverPath(Path, RESERVED_PATH.ASSETS_DIRECTORY);
    public string TempDir => GetReserverPath(Path, RESERVED_PATH.TEMP_DIRECTORY);
    
    public readonly string Path;
    public readonly ProjectInfo Info;
    public readonly ProjectVersion Version;

    public Project(string path)
    {
        Path = path;
        
        var projectFile =  GetReserverPath(path, RESERVED_PATH.PROJECT_JSON);;
        var versionPath =  GetReserverPath(path, RESERVED_PATH.VERSION_JSON);;

        if (!File.Exists(projectFile))
        {
            throw new FileNotFoundException("project file not found", projectFile);
        }

        /*if (!File.Exists(versionPath))
        {
            throw new FileNotFoundException("version file not found", versionPath);
        }
        
        var versionJson = FileReader.ReadToEnd(versionPath);
        Version = JsonConvert.DeserializeObject<ProjectVersion>(versionJson);*/
        
        var projectJson = FileReader.ReadToEnd(projectFile);
        Info = JsonConvert.DeserializeObject<ProjectInfo>(projectJson);
        
    }

    private Project(string path, ProjectInfo info, ProjectVersion version)
    {
        Path = path;
        Info = info;
        Version = version;
    }
    
    public static Project Create(ProjectInfo info, string projectAbsolutePath, PROJECT_TYPE type, ProjectVersion? version = null)
    {
        version ??= new ProjectVersion
        {
            Game = "0.0.1",
            Engine = Engine.VERSION
        };
        
        Project project;

        if (Directory.Exists(projectAbsolutePath))
        {
            throw new Exception("Project folder already exists!");
        }
        
        DirectoryUtils.GetOrCreateDirectory(projectAbsolutePath);

        CreateVecxyDir();
        
        switch (type)
        {
            case PROJECT_TYPE.GAME:
                project = new Project(projectAbsolutePath, info, version.Value);
                break;
            
            case PROJECT_TYPE.LIBRARY:
            case PROJECT_TYPE.PACKAGE:
            default:
                throw new NotSupportedProjectTypeException(type);
        }

        return project;
        
        void CreateVecxyDir()
        {
            // .vecxy
            var vecxyPath = GetReserverPath(projectAbsolutePath, RESERVED_PATH.VECXY_DIRECTORY);
            
            DirectoryUtils.GetOrCreateDirectory(vecxyPath);
            
            // project.json
            var projectFilePath = GetReserverPath(projectAbsolutePath, RESERVED_PATH.PROJECT_JSON);
            var projectJson = JsonConvert.SerializeObject(info, Formatting.Indented);
            
            File.WriteAllText(projectFilePath, projectJson);
            
            // version.json
            var versionFilePath = GetReserverPath(projectAbsolutePath, RESERVED_PATH.VERSION_JSON);
            var versionJson = JsonConvert.SerializeObject(version.Value, Formatting.Indented);
            
            File.WriteAllText(versionFilePath, versionJson);
        }
    }

    private static string GetReserverPath(string rootPath, RESERVED_PATH type)
    {
        var vecxy = System.IO.Path.Combine(rootPath, ".vecxy");

        return type switch
        {
            // Directories
            RESERVED_PATH.VECXY_DIRECTORY => vecxy,
            RESERVED_PATH.SETTINGS_DIRECTORY => System.IO.Path.Combine(vecxy, "Settings"),
            RESERVED_PATH.ASSETS_DIRECTORY => System.IO.Path.Combine(rootPath, "Assets"),
            RESERVED_PATH.BUILD_DIRECTORY => System.IO.Path.Combine(rootPath, "Build"),
            RESERVED_PATH.TEMP_DIRECTORY => System.IO.Path.Combine(rootPath, "Temp"),
            
            // Configs
            RESERVED_PATH.PROJECT_JSON => System.IO.Path.Combine(vecxy, "project.json"),
            RESERVED_PATH.VERSION_JSON => System.IO.Path.Combine(vecxy, "version.json"),
            
            // Settings
            RESERVED_PATH.GAME_SETTINGS_JSON => System.IO.Path.Combine(GetReserverPath(rootPath, RESERVED_PATH.SETTINGS_DIRECTORY), "engine.json"),

            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }
    
    private enum RESERVED_PATH : byte
    {
        VECXY_DIRECTORY,
        SETTINGS_DIRECTORY,
        ASSETS_DIRECTORY,
        BUILD_DIRECTORY,
        TEMP_DIRECTORY,
        
        PROJECT_JSON,
        VERSION_JSON,
        
        GAME_SETTINGS_JSON,
    }
}