using Newtonsoft.Json;
using Vecxy.Engine;

namespace Vecxy.Editor;

internal static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
    }

    public static void OpenProject(string projectPath)
    {
        var files = Directory.GetFiles(projectPath, ".project");

        if (files.Length == 0)
        {
            throw new Exception("No project files found!");
        }

        var projectFile = files[0];


        using var stream = new FileStream(projectFile, FileMode.Open);
        using var reader = new StreamReader(stream);

        var projectInfo = reader.ReadToEnd();
        
        reader.Close();
        stream.Close();

        var config = JsonConvert.DeserializeObject<ProjectConfig>(projectInfo);
    }
}