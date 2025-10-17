namespace Vecxy.Engine;

public enum PROJECT_TYPE : byte
{
    GAME = 0,
    
    LIBRARY = 1,
    
    PACKAGE = 2
}

// project.vecxy
public interface IProjectFile
{
    public PROJECT_TYPE Type { get; }
    public ProjectInfo Info { get; }
}

public struct ProjectVersion
{
    public string Game;
    public string Engine;
}

public struct ProjectInfo
{
    public string Name;
    public string Description;
    public string Author;

    public ProjectVersion Version;
}

public class Project
{
    public void SetName(string name)
    {
       
    }
}