using Vecxy.Assets;

namespace Vecxy.Scripting;

public interface IScriptRuntime
{
    IScriptInstance Create(
        IAssetHandle script,
        ScriptContext? context = null,
        ScriptRuntimeOptions? options = null);

    IScriptInstance Create(
        string path,
        ScriptContext? context = null,
        ScriptRuntimeOptions? options = null);
}

public interface IScriptInstance : IDisposable
{
    string Path { get; }
    bool HasFunction(string name);
    object? Invoke(string name, params object?[] arguments);
    object? InvokeOptional(string name, params object?[] arguments);
}
