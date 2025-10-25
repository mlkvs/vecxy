using Zenject;

namespace Vecxy.Editor;

public class Editor
{
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
}