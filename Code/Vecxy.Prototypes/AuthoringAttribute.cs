namespace Vecxy.Prototypes;

/// <summary>Describe a component or prototype option for scene/model authoring tools.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = true)]
public sealed class AuthoringAttribute(string name = "") : Attribute
{
    public string Name { get; } = name;
    public string Category { get; set; } = "Gameplay";
    public string Description { get; set; } = "";
    public string Key { get; set; } = "";
    public double Min { get; set; } = double.NaN;
    public double Max { get; set; } = double.NaN;
    public string Unit { get; set; } = "";
    public int Order { get; set; }
    /// <summary>Optional authoring default overriding the factory default.</summary>
    public string Default { get; set; } = "";
}
