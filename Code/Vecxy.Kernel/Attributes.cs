namespace JetBrains.Annotations
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method)]
    public sealed class UsedImplicitlyAttribute : Attribute { }
}

namespace Vecxy.Kernel
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class VecxyApplicationAttribute : Attribute;
}
