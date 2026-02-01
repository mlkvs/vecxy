using System.Reflection;
using Vecxy.Diagnostics;

namespace Vecxy.Builder;

public enum BUILD_TARGET : byte
{
    NONE = 0,
    WINDOWS = 1,
    WEB = 2,
}

public enum BUILD_CONFIGURATION : byte
{
    DEBUG = 0,
    RELEASE = 1
}

public record BuildArtifact<TArtifact>(string Name, TArtifact Value);

public class BuildContext
{
    public BUILD_TARGET Target { get; init; }
    public BUILD_CONFIGURATION Configuration { get; init; }

    public string ProjectDir { get; init; }
    public string OutputDir { get; init; }

    private readonly Dictionary<string, object> _artifacts = new ();

    public void SetArtifact<TArtifact>(string name, TArtifact artifact) => _artifacts[name] = artifact!;

    public BuildArtifact<TArtifact> GetArtifact<TArtifact>(string name)
    {
        if (!_artifacts.TryGetValue(name, out var value))
        {
            throw new Exception($"Artifact '{name}' not found");
        }

        return (BuildArtifact<TArtifact>)value;
    }
}

public abstract class BuildStage
{
    public abstract string Name { get; }
    public virtual int Priority => 0;

    public virtual bool Can(BuildContext ctx) => true;

    public virtual void OnBefore(BuildContext ctx) { }
    public virtual void Execute(BuildContext ctx) { }
    public virtual void OnAfter(BuildContext ctx) { }
}

public static class BuildPipeline
{
    public static void Build(BuildConfig cfg)
    {
        var ctx = new BuildContext
        {
            ProjectDir = cfg.ProjectDir,
            OutputDir = cfg.OutputDir,
            Configuration = cfg.Configuration,
            Target = BUILD_TARGET.WINDOWS
        };

        var asm = Assembly.GetExecutingAssembly();

        var stages = asm
            .GetTypes()
            .Where(t => t.IsSubclassOf(typeof(BuildStage)) && !t.IsAbstract)
            .Select(t => (BuildStage)Activator.CreateInstance(t)!)
            .Where(s => s.Can(ctx))
            .OrderByDescending(s => s.Priority)
            .ToList();

        foreach (var stage in stages) ExecuteStage("Before", stage.Name, () => stage.OnBefore(ctx));
        foreach (var stage in stages) ExecuteStage("Execute", stage.Name, () => stage.Execute(ctx));
        foreach (var stage in stages) ExecuteStage("After", stage.Name, () => stage.OnAfter(ctx));
    }

    private static void ExecuteStage(string phase, string stageName, Action action)
    {
        Logger.Info($"[{phase}] {stageName}...");

        try
        {
            action();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed at {stageName}: {ex.Message}");

            throw;
        }
    }
}