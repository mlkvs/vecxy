namespace Vecxy.Prototypes;

/// <summary>Opt a component with a nested Prototype into project authoring catalogs.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorComponentAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public string Category { get; set; } = "Gameplay";
    public string Description { get; set; } = "";
    public string Key { get; set; } = "";
}

/// <summary>Expose an explicitly serializable Prototype.Options field to authoring tools.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class EditorFieldAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public string Description { get; set; } = "";
    public double Min { get; set; } = double.NaN;
    public double Max { get; set; } = double.NaN;
    public string Unit { get; set; } = "";
    public int Order { get; set; }
    /// <summary>An optional authoring default, such as Blender's Z capsule axis.</summary>
    public string Default { get; set; } = "";
}
