using System.Diagnostics;

namespace JetBrains.Annotations
{
    [Flags]
    public enum ImplicitUseTargetFlags
    {
        Default = Itself,

        Itself = 1,
        Members = 2,
        WithInheritors = 4,
        WithMembers = Itself | Members
    }
    
    [Flags]
    public enum ImplicitUseKindFlags
    {
        Default = Access | Assign | InstantiatedWithFixedConstructorSignature,
        Access = 1,
        Assign = 2,
        InstantiatedWithFixedConstructorSignature = 4,
        InstantiatedNoFixedConstructorSignature = 8,
    }
    
    [AttributeUsage(AttributeTargets.All)]
    [Conditional("JETBRAINS_ANNOTATIONS")]
    public sealed class UsedImplicitlyAttribute : Attribute
    {
        public UsedImplicitlyAttribute()
            : this(ImplicitUseKindFlags.Default, ImplicitUseTargetFlags.Default) { }

        public UsedImplicitlyAttribute(ImplicitUseKindFlags useKindFlags)
            : this(useKindFlags, ImplicitUseTargetFlags.Default) { }

        public UsedImplicitlyAttribute(ImplicitUseTargetFlags targetFlags)
            : this(ImplicitUseKindFlags.Default, targetFlags) { }

        public UsedImplicitlyAttribute(ImplicitUseKindFlags useKindFlags, ImplicitUseTargetFlags targetFlags)
        {
            UseKindFlags = useKindFlags;
            TargetFlags = targetFlags;
        }

        public ImplicitUseKindFlags UseKindFlags { get; }

        public ImplicitUseTargetFlags TargetFlags { get; }

        public string Reason { get; set; }
    }
}

namespace Vecxy.Kernel
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class VecxyApplicationAttribute(string configPath = "Configs/Application.yaml") : Attribute
    {
        public string ConfigPath { get; } = configPath;
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AppLayerDefinitionAttribute(string id) : Attribute
    {
        public string Id { get; } = id;
    }
}
