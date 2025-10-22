namespace Vecxy.Editor;

public enum PROJECT_TYPE : byte
{
    GAME = 0,
    
    LIBRARY = 1,
    
    PACKAGE = 2
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

public struct ProjectConfig
{
    public PROJECT_TYPE Type { get; }
    public ProjectInfo Info { get; }
}

public class Project
{
    public ProjectConfig Config { get; }
}