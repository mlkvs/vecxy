using System.Reflection;

namespace Vecxy.Diagnostics.Console;

public sealed class ConsoleRegistry(
    IConsoleObjectResolver? resolver = null) : IConsoleRegistry
{
    private static readonly BindingFlags MemberFlags =
        BindingFlags.Instance |
        BindingFlags.Static |
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.FlattenHierarchy;

    private readonly object _sync = new();
    private readonly Dictionary<string, ConsoleObjectDescriptor> _objects =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IConsoleObjectDescriptor> Objects
    {
        get
        {
            lock (_sync)
            {
                return _objects.Values.Cast<IConsoleObjectDescriptor>().ToArray();
            }
        }
    }

    public void Register(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        RegisterCore(instance.GetType(), instance, allowInstance: true);
    }

    public void Register(Type staticType)
    {
        ArgumentNullException.ThrowIfNull(staticType);
        RegisterCore(staticType, null, allowInstance: false);
    }

    public void RegisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<ConsoleObjectAttribute>() is null)
                continue;

            RegisterCore(type, null, allowInstance: false);
        }
    }

    public bool Unregister(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        lock (_sync)
        {
            foreach (var descriptor in _objects.Values)
            {
                if (descriptor.UnbindInstance(instance))
                    return true;
            }
        }

        return false;
    }

    private void RegisterCore(
        Type objectType,
        object? instance,
        bool allowInstance)
    {
        var objectAttribute = objectType.GetCustomAttribute<ConsoleObjectAttribute>()
            ?? throw new InvalidOperationException(
                $"Type '{objectType.FullName}' must be decorated with [ConsoleObject].");

        var descriptor = CreateDescriptor(objectType, objectAttribute);

        lock (_sync)
        {
            if (_objects.TryGetValue(objectAttribute.Name, out var existing))
            {
                if (existing.ObjectType != descriptor.ObjectType)
                {
                    throw new InvalidOperationException(
                        $"Console object name \"{objectAttribute.Name}\" is already used by '{existing.ObjectType.FullName}'.");
                }

                if (allowInstance && instance is not null)
                    existing.BindInstance(instance);

                return;
            }

            if (allowInstance && instance is not null)
                descriptor.BindInstance(instance);

            _objects.Add(descriptor.Name, descriptor);
        }
    }

    private ConsoleObjectDescriptor CreateDescriptor(
        Type objectType,
        ConsoleObjectAttribute attribute)
    {
        var isStatic = objectType.IsAbstract && objectType.IsSealed;
        var members = new Dictionary<string, IConsoleMemberDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in objectType.GetFields(MemberFlags))
        {
            var memberAttribute = field.GetCustomAttribute<ConsoleMemberAttribute>();
            if (memberAttribute is null)
                continue;

            var memberName = ResolveMemberName(memberAttribute, field.Name);
            AddMember(
                members,
                memberName,
                new ConsoleFieldDescriptor(
                    attribute.Name,
                    memberName,
                    memberAttribute.Description,
                    ResolveFieldAccess(memberAttribute.Access, field),
                    field));
        }

        foreach (var property in objectType.GetProperties(MemberFlags))
        {
            var memberAttribute = property.GetCustomAttribute<ConsoleMemberAttribute>();
            if (memberAttribute is null)
                continue;

            var memberName = ResolveMemberName(memberAttribute, property.Name);
            AddMember(
                members,
                memberName,
                new ConsolePropertyDescriptor(
                    attribute.Name,
                    memberName,
                    memberAttribute.Description,
                    ResolvePropertyAccess(memberAttribute.Access, property),
                    property));
        }

        foreach (var method in objectType.GetMethods(MemberFlags))
        {
            var memberAttribute = method.GetCustomAttribute<ConsoleMemberAttribute>();
            if (memberAttribute is null)
                continue;

            var memberName = ResolveMemberName(memberAttribute, method.Name);
            var parameters = method
                .GetParameters()
                .Select(parameter => new ConsoleParameterDescriptor(
                    parameter.Name ?? $"arg{parameter.Position}",
                    parameter.ParameterType,
                    parameter.IsOptional,
                    parameter.DefaultValue))
                .ToArray();

            AddMember(
                members,
                memberName,
                new ConsoleMethodDescriptor(
                    attribute.Name,
                    memberName,
                    memberAttribute.Description,
                    method,
                    parameters));
        }

        return new ConsoleObjectDescriptor(
            attribute.Name,
            attribute.Description,
            objectType,
            isStatic,
            members,
            resolver);
    }

    private static void AddMember(
        IDictionary<string, IConsoleMemberDescriptor> members,
        string memberName,
        IConsoleMemberDescriptor descriptor)
    {
        if (members.TryGetValue(memberName, out var existing))
        {
            throw new InvalidOperationException(
                $"Console member name conflict for \"{descriptor.QualifiedName}\". Existing member: '{existing.MemberInfo.DeclaringType?.FullName}.{existing.MemberInfo.Name}', conflicting member: '{descriptor.MemberInfo.DeclaringType?.FullName}.{descriptor.MemberInfo.Name}'.");
        }

        members.Add(memberName, descriptor);
    }

    private static string ResolveMemberName(
        ConsoleMemberAttribute attribute,
        string fallbackName) =>
        string.IsNullOrWhiteSpace(attribute.Name)
            ? fallbackName
            : attribute.Name;

    private static ConsoleAccess ResolveFieldAccess(
        ConsoleAccess requestedAccess,
        FieldInfo field)
    {
        if (field.IsLiteral || field.IsInitOnly)
            return requestedAccess == ConsoleAccess.WriteOnly
                ? ConsoleAccess.WriteOnly
                : ConsoleAccess.ReadOnly;

        return requestedAccess;
    }

    private static ConsoleAccess ResolvePropertyAccess(
        ConsoleAccess requestedAccess,
        PropertyInfo property)
    {
        var canRead = property.GetMethod is not null;
        var canWrite = property.SetMethod is not null;

        return (canRead, canWrite, requestedAccess) switch
        {
            (true, false, _) => ConsoleAccess.ReadOnly,
            (false, true, _) => ConsoleAccess.WriteOnly,
            (true, true, _) => requestedAccess,
            _ => ConsoleAccess.ReadOnly
        };
    }
}
