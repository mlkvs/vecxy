namespace Vecxy.Editor;

public class GameProject : Project
{
    public string AssemblyPath { get; set; }
    
    public List<LibraryProject> Libraries { get; }
    public List<PackageProject> Packages { get; }
}