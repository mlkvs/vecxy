namespace Vecxy.Editor;

[AttributeUsage(AttributeTargets.Property)]
public sealed class EditorPropertyAttribute : Attribute
{
    public string? Label { get; init; }
    public int Order { get; init; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class EditorIgnoreAttribute : Attribute
{
}
