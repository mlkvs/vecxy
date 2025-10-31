using Newtonsoft.Json;
using Vecxy.IO;
using Vecxy.Projects;
using Zenject;

namespace Vecxy.Editor;

public class Editor
{
    public string Version { get; } = "a.001";
    
    private Engine.Engine? _engine;
    private Project? _project;

    private DiContainer _diContainer;
    
    public Editor()
    {
        _diContainer = new DiContainer();
        EditorInstaller.Install(_diContainer);
    }
    
    public void Run()
    {
        _engine = new Engine.Engine();

        if (_project?.Info.Type != PROJECT_TYPE.GAME)
        {
            //TODO: Support some projects types
            return;
        }

        var gameProject = (GameProject)_project;
        
        gameProject.Build();

        var game = gameProject.CreateGame();
            
        _engine.Run(game);
    }

    public void OpenProject(Project project)
    {
        _project = project;
        
        _project.Open();
    }
    
    public void OpenProject(string projectPath)
    {
        var projectInfo = Project.Define(projectPath);

        switch (projectInfo.Type)
        {
            case PROJECT_TYPE.GAME:
                _project = new GameProject(projectPath, projectInfo);
                break;
            
            case PROJECT_TYPE.LIBRARY:
            case PROJECT_TYPE.PACKAGE:
            default:
                throw new NotSupportedProjectTypeException(projectInfo.Type);
        }
        
        _project.Open();
    }

    public Project GenerateProject(string name, string path, PROJECT_TYPE type)
    {
        var info = new ProjectInfo
        {
            Type = type,
            Author = "No Author",
            Description = "No Description",
            Name = name
        };

        var version = new ProjectVersion
        {
            Editor = Version,
            Game = "0.0.1",
            Engine = "a.001"
        };
        
        Project project;

        if (Directory.Exists(path))
        {
            throw new Exception("Project folder already exists!");
        }
        
        var projectDir = DirectoryUtils.GetOrCreateDirectory(path);

        CreateVecxyDir();
        
        switch (type)
        {
            case PROJECT_TYPE.GAME:
                project = new GameProject(path, info);
                break;
            
            case PROJECT_TYPE.LIBRARY:
            case PROJECT_TYPE.PACKAGE:
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        return project;
        
        void CreateVecxyDir()
        {
            var vecxyPath = Path.Combine(projectDir.FullName, ".vecxy");
            
            var vecxyDir = DirectoryUtils.GetOrCreateDirectory(vecxyPath);
            
            var projectFilePath = Path.Combine(vecxyPath, "project.json");

            //TODO: Create serializer wrapper
            var projectJson = JsonConvert.SerializeObject(info, Formatting.Indented);
            
            File.WriteAllText(projectFilePath, projectJson);
        }
    }
}