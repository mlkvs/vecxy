namespace Vecxy.Engine;

public struct ProjectCreateParameters
{
    public string? Name;
    public string? Author;
    public string? Description;
    
    public string Path;
}

public class ProjectManager
{
    public IProject Create(ProjectCreateParameters parameters)
    {
        var project = new Project(parameters.Path);

        return project;
    }
}