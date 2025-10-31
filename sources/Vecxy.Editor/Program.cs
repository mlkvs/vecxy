using Vecxy.Projects;

namespace Vecxy.Editor;

internal static class Program
{
    private static Editor? _editor;
    
    public static void Main(string[] args)
    {
        _editor = new Editor();

        var projectPath = @"C:\Users\melkov\Desktop\Test";

        GenerateProject("Test", projectPath, PROJECT_TYPE.GAME);
    }

    private static void OpenProject(string path)
    {
        if (_editor == null)
        {
            throw new Exception("Editor not initialized");
        }
        
        _editor.OpenProject(path);
        
        _editor.Run();
    }
    
    private static void GenerateProject(string name, string path, PROJECT_TYPE type)
    {
        if (_editor == null)
        {
            throw new Exception("Editor not initialized");
        }
        
        _editor.GenerateProject(name, path, type);
    }
}