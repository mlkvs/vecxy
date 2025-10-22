namespace Vecxy.Editor;

public struct ProjectCreateParameters
{
    public string? Name;
    public string? Author;
    public string? Description;
    
    public string Path;
}

public class ProjectManager
{
    public Project Create(ProjectCreateParameters parameters)
    {
        var project = new Project();

        return project;
    }
}