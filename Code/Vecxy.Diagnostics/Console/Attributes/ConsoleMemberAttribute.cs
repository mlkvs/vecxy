namespace Vecxy.Diagnostics.Console;

public enum ConsoleAccess
{
    ReadOnly,
    WriteOnly,
    ReadWrite,
    Execute
}

[AttributeUsage(
    AttributeTargets.Field |
    AttributeTargets.Property |
    AttributeTargets.Method,
    Inherited = true)]
public sealed class ConsoleMemberAttribute : Attribute
{
    public ConsoleMemberAttribute()
    {
    }

    public ConsoleMemberAttribute(string name)
    {
        Name = name;
    }

    public string? Name { get; }

    public string? Description { get; init; }

    public ConsoleAccess Access { get; init; } = ConsoleAccess.ReadWrite;
}
