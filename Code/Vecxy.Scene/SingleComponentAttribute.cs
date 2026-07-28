namespace Vecxy.Scene;

[AttributeUsage(
    AttributeTargets.Class,
    Inherited = true,
    AllowMultiple = false)]
public sealed class SingleComponentAttribute : Attribute;
