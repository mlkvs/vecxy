using System.Globalization;
using System.Reflection;

namespace Vecxy.Diagnostics.Console;

internal static class ConsoleTypeDisplay
{
    public static string Format(Type type)
    {
        var underlyingNullable = Nullable.GetUnderlyingType(type);
        if (underlyingNullable is not null)
            return $"{Format(underlyingNullable)}?";

        if (type.IsEnum)
            return type.Name;

        if (type == typeof(void))
            return "void";

        if (type == typeof(bool))
            return "bool";

        if (type == typeof(byte))
            return "byte";

        if (type == typeof(sbyte))
            return "sbyte";

        if (type == typeof(short))
            return "short";

        if (type == typeof(ushort))
            return "ushort";

        if (type == typeof(int))
            return "int";

        if (type == typeof(uint))
            return "uint";

        if (type == typeof(long))
            return "long";

        if (type == typeof(ulong))
            return "ulong";

        if (type == typeof(float))
            return "float";

        if (type == typeof(double))
            return "double";

        if (type == typeof(decimal))
            return "decimal";

        if (type == typeof(string))
            return "string";

        return type.Name;
    }

    public static string FormatValue(object? value)
    {
        if (value is null)
            return "null";

        return value switch
        {
            string text => $"\"{text.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
            bool booleanValue => booleanValue ? "true" : "false",
            Enum enumValue => enumValue.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}

internal sealed class ConsoleObjectDescriptor : IConsoleObjectDescriptor
{
    private readonly Dictionary<string, IConsoleMemberDescriptor> _members;
    private readonly IConsoleObjectResolver? _resolver;
    private object? _instance;

    public ConsoleObjectDescriptor(
        string name,
        string? description,
        Type objectType,
        bool isStatic,
        IReadOnlyDictionary<string, IConsoleMemberDescriptor> members,
        IConsoleObjectResolver? resolver)
    {
        Name = name;
        Description = description;
        ObjectType = objectType;
        IsStatic = isStatic;
        _resolver = resolver;
        _members = new Dictionary<string, IConsoleMemberDescriptor>(
            members,
            StringComparer.OrdinalIgnoreCase);
    }

    public string Name { get; }

    public string? Description { get; }

    public Type ObjectType { get; }

    public bool IsStatic { get; }

    public object? RegisteredInstance => _instance;

    public IReadOnlyDictionary<string, IConsoleMemberDescriptor> Members => _members;

    public void BindInstance(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (!ObjectType.IsInstanceOfType(instance))
        {
            throw new InvalidOperationException(
                $"Cannot register instance of '{instance.GetType().FullName}' for console object '{Name}' that expects '{ObjectType.FullName}'.");
        }

        _instance = instance;
    }

    public bool UnbindInstance(object instance)
    {
        if (!ReferenceEquals(_instance, instance))
            return false;

        _instance = null;
        return true;
    }

    public bool TryGetMember(string name, out IConsoleMemberDescriptor descriptor) =>
        _members.TryGetValue(name, out descriptor!);

    public bool TryResolveTarget(out object? target, out string? error)
    {
        if (IsStatic)
        {
            target = null;
            error = null;
            return true;
        }

        if (_instance is not null)
        {
            target = _instance;
            error = null;
            return true;
        }

        if (_resolver is not null &&
            _resolver.TryResolve(ObjectType, out target) &&
            target is not null)
        {
            error = null;
            return true;
        }

        target = null;
        error =
            $"Console object \"{Name}\" requires an instance of '{ObjectType.Name}', but no instance is registered.";
        return false;
    }
}

internal abstract class ConsoleMemberDescriptorBase(
    string objectName,
    string name,
    string? description,
    ConsoleMemberKind kind,
    ConsoleAccess access,
    Type valueType,
    bool isStatic,
    bool isReadable,
    bool isWritable,
    bool isExecutable,
    MemberInfo memberInfo,
    IReadOnlyList<ConsoleParameterDescriptor> parameters,
    string displaySignature) : IConsoleMemberDescriptor
{
    public string Name { get; } = name;

    public string QualifiedName { get; } = $"{objectName}.{name}";

    public string? Description { get; } = description;

    public ConsoleMemberKind Kind { get; } = kind;

    public ConsoleAccess Access { get; } = access;

    public Type ValueType { get; } = valueType;

    public bool IsStatic { get; } = isStatic;

    public bool IsReadable { get; } = isReadable;

    public bool IsWritable { get; } = isWritable;

    public bool IsExecutable { get; } = isExecutable;

    public IReadOnlyList<ConsoleParameterDescriptor> Parameters { get; } = parameters;

    public MemberInfo MemberInfo { get; } = memberInfo;

    public string DisplaySignature { get; } = displaySignature;

    public virtual object? Read(object? target) =>
        throw new InvalidOperationException($"Member '{QualifiedName}' is not readable.");

    public virtual void Write(object? target, object? value) =>
        throw new InvalidOperationException($"Member '{QualifiedName}' is not writable.");

    public virtual object? Invoke(object? target, object?[] arguments) =>
        throw new InvalidOperationException($"Member '{QualifiedName}' is not executable.");
}

internal sealed class ConsoleFieldDescriptor : ConsoleMemberDescriptorBase
{
    private readonly FieldInfo _field;

    public ConsoleFieldDescriptor(
        string objectName,
        string name,
        string? description,
        ConsoleAccess access,
        FieldInfo field)
        : base(
            objectName,
            name,
            description,
            ConsoleMemberKind.Field,
            access,
            field.FieldType,
            field.IsStatic,
            access is ConsoleAccess.ReadOnly or ConsoleAccess.ReadWrite,
            !field.IsLiteral &&
            !field.IsInitOnly &&
            access is ConsoleAccess.WriteOnly or ConsoleAccess.ReadWrite,
            false,
            field,
            [],
            $"{name} : {ConsoleTypeDisplay.Format(field.FieldType)}")
    {
        _field = field;
    }

    public override object? Read(object? target) => _field.GetValue(target);

    public override void Write(object? target, object? value) => _field.SetValue(target, value);
}

internal sealed class ConsolePropertyDescriptor : ConsoleMemberDescriptorBase
{
    private readonly PropertyInfo _property;

    public ConsolePropertyDescriptor(
        string objectName,
        string name,
        string? description,
        ConsoleAccess access,
        PropertyInfo property)
        : base(
            objectName,
            name,
            description,
            ConsoleMemberKind.Property,
            access,
            property.PropertyType,
            property.GetMethod?.IsStatic ?? property.SetMethod?.IsStatic ?? false,
            property.GetMethod is not null &&
            access is ConsoleAccess.ReadOnly or ConsoleAccess.ReadWrite,
            property.SetMethod is not null &&
            access is ConsoleAccess.WriteOnly or ConsoleAccess.ReadWrite,
            false,
            property,
            [],
            $"{name} : {ConsoleTypeDisplay.Format(property.PropertyType)}" +
            (property.SetMethod is null ? " { get; }" : " { get; set; }"))
    {
        _property = property;
    }

    public override object? Read(object? target) => _property.GetValue(target);

    public override void Write(object? target, object? value) => _property.SetValue(target, value);
}

internal sealed class ConsoleMethodDescriptor : ConsoleMemberDescriptorBase
{
    private readonly MethodInfo _method;

    public ConsoleMethodDescriptor(
        string objectName,
        string name,
        string? description,
        MethodInfo method,
        IReadOnlyList<ConsoleParameterDescriptor> parameters)
        : base(
            objectName,
            name,
            description,
            ConsoleMemberKind.Method,
            ConsoleAccess.Execute,
            method.ReturnType,
            method.IsStatic,
            false,
            false,
            true,
            method,
            parameters,
            BuildSignature(name, method.ReturnType, parameters))
    {
        _method = method;
    }

    public override object? Invoke(object? target, object?[] arguments) =>
        _method.Invoke(target, arguments);

    private static string BuildSignature(
        string name,
        Type returnType,
        IReadOnlyList<ConsoleParameterDescriptor> parameters)
    {
        var signature = string.Join(
            ", ",
            parameters.Select(parameter =>
            {
                var text = $"{ConsoleTypeDisplay.Format(parameter.ParameterType)} {parameter.Name}";
                if (!parameter.IsOptional)
                    return text;

                var defaultValue = parameter.DefaultValue is DBNull
                    ? "null"
                    : ConsoleTypeDisplay.FormatValue(parameter.DefaultValue);
                return $"{text} = {defaultValue}";
            }));
        return $"{name}({signature}) : {ConsoleTypeDisplay.Format(returnType)}";
    }
}
