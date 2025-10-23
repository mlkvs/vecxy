namespace Vecxy.Editor;

public class Editor()
{
    private Project? _project;
    
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