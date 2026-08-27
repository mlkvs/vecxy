namespace Vecxy.Scripting;

public sealed class ScriptContext
{
    private readonly Dictionary<string, object> _capabilities =
        new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, object> Capabilities => _capabilities;

    public ScriptContext Add(string name, object capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(capability);
        if (!_capabilities.TryAdd(name, capability))
            throw new InvalidOperationException($"Script capability '{name}' is already registered.");
        return this;
    }
}

public sealed class ScriptRuntimeOptions
{
    public long MemoryLimitBytes { get; init; } = 16 * 1024 * 1024;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(100);
}

public sealed class ScriptExecutionException : Exception
{
    public string ScriptPath { get; }
    public string? Function { get; }

    public ScriptExecutionException(
        string scriptPath,
        string? function,
        Exception innerException)
        : base(
            function is null
                ? $"Could not execute script '{scriptPath}'."
                : $"Could not execute '{function}' in script '{scriptPath}'.",
            innerException)
    {
        ScriptPath = scriptPath;
        Function = function;
    }
}
