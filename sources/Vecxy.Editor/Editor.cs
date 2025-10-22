using Newtonsoft.Json;

namespace Vecxy.Editor;

public class Editor()
{
    private Project? _project;
    
    public void OpenProject(string projectPath)
    {
        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException(projectPath);
        }
        
        var files = Directory.GetFiles(projectPath);

        if (files.Length == 0)
        {
            throw new Exception("No project files found!");
        }

        var projectFilePath = files.First(f => Path.GetExtension(f).EndsWith("project"));

        using var stream = new FileStream(projectFilePath, FileMode.Open);
        using var reader = new StreamReader(stream);

        var projectInfo = reader.ReadToEnd();
        
        reader.Close();
        stream.Close();

        var projectFile = JsonConvert.DeserializeObject<ProjectFile>(projectInfo);

        switch (projectFile.Type)
        {
            case PROJECT_TYPE.GAME:
                var game = new GameProject(projectPath, projectFile);
                
                _project = game;
                break;
            
            case PROJECT_TYPE.LIBRARY:
            case PROJECT_TYPE.PACKAGE:
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}