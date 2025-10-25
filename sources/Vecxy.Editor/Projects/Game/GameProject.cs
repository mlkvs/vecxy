using System.Reflection;
using System.Runtime.Serialization;
using SlnParser;
using SlnParser.Contracts;
using Vecxy.Diagnostics;
using Vecxy.Engine;

namespace Vecxy.Editor;

public class DLL
{
    public string Path;

    public Assembly Assembly { get; private set; }

    public virtual Assembly Load()
    {
        Assembly = Assembly.LoadFrom(Path);

        return Assembly;
    }
}

public class GameDLL : DLL
{
    private Type _entryType;
    
    public override Assembly Load()
    {
        var assembly = base.Load();
        
        var gameType = assembly
            .GetTypes()
            .FirstOrDefault(t =>
            {
                if (t.IsAbstract || t.IsInterface)
                {
                    return false;
                }

                return typeof(IGame).IsAssignableFrom(t);
            });

        _entryType = gameType ?? throw new Exception("Game assembly not found");

        return Assembly;
    }

    public IGame CreateGame()
    {
        var game = (IGame)Activator.CreateInstance(_entryType)!;

        return game;
    }
}

public class GameProjectSettings 
{
    
}

[DataContract]
public class GameProject(string path, ProjectInfo info) : Project(path, info)
{
    private ISolution _solution;
    private IProject _csProjectEntry;

    private GameDLL _dllEntry;
    
    public override void Open()
    {
        base.Open();

        //TODO: Name Name => Name_Name
        var solutionPath = System.IO.Path.Combine(path, $"{info.Name}.sln");
        
        var parser = new SolutionParser();
        _solution = parser.Parse(solutionPath);

        var csProject = _solution.AllProjects.FirstOrDefault(p => p.Name.Equals($"{info.Name}.Game"));

        _csProjectEntry = csProject ?? throw new Exception("Game project not found");
    }

    public void Build()
    {
        var project = (SolutionProject)_csProjectEntry;
        
        var dllName = $"{_csProjectEntry.Name}.dll";

        _dllEntry = new GameDLL
        {
            Path = System.IO.Path.Combine(project.File.Directory!.FullName, "bin", dllName)
        };
    }

    public IGameHost CreateGame()
    {
        _dllEntry.Load();
        
        var game = _dllEntry.CreateGame();


        return new GameHost(game, new GameSettings
        {
            Verison = Version.Game
        });
    }

    public override void Save()
    {
        base.Save();
    }
}