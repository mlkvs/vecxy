namespace Vecxy.Diagnostics.Console;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ConsoleObjectAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public string? Description { get; init; }
}
