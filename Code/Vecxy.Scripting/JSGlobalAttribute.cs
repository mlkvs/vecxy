namespace Vecxy.Scripting;

[AttributeUsage(AttributeTargets.Class)]
public class JSGlobalAttribute : Attribute
{
    public string Name { get; set; }
}