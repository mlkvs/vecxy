using System.Reflection;
using Vecxy.Engine;
using Vecxy.Kernel;

namespace Vecxy.Platforms;

public static class ApplicationResolver
{
    public static IEntryPoint Create()
    {
        var candidates = GetLoadableTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsPublic: true } &&
                           type.IsDefined(typeof(VecxyApplicationAttribute), false) &&
                           typeof(IEntryPoint).IsAssignableFrom(type) &&
                           type.GetConstructor(Type.EmptyTypes) is not null)
            .ToArray();

        return candidates.Length switch
        {
            1 => (IEntryPoint)Activator.CreateInstance(candidates[0])!,
            0 => throw new InvalidOperationException(
                $"No public {nameof(IEntryPoint)} marked with [{nameof(VecxyApplicationAttribute)}] was found."),
            _ => throw new InvalidOperationException(
                $"Multiple classes marked with [{nameof(VecxyApplicationAttribute)}] were found: " +
                string.Join(", ", candidates.Select(type => type.FullName)))
        };
    }

    internal static IEnumerable<Type> GetLoadableTypes() =>
        AppDomain.CurrentDomain.GetAssemblies().SelectMany(GetLoadableTypes);

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.DefinedTypes.Select(type => type.AsType());
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
        catch
        {
            return [];
        }
    }
}

internal static class ApplicationLayerResolver
{
    public static AAppLayer.IDefinition Create(string id)
    {
        var candidates = ApplicationResolver.GetLoadableTypes()
            .Where(type => typeof(AAppLayer.IDefinition).IsAssignableFrom(type) &&
                           type is { IsAbstract: false, IsInterface: false } &&
                           type.GetCustomAttribute<AppLayerDefinitionAttribute>()?.Id.Equals(
                               id, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        return candidates.Length switch
        {
            1 when candidates[0].GetConstructor(Type.EmptyTypes) is not null =>
                (AAppLayer.IDefinition)Activator.CreateInstance(candidates[0])!,
            1 => throw new InvalidOperationException(
                $"Layer definition '{candidates[0].FullName}' must have a parameterless constructor."),
            0 => throw new InvalidDataException(
                $"Unknown application layer '{id}'. Add [{nameof(AppLayerDefinitionAttribute)}(\"{id}\")] to its definition."),
            _ => throw new InvalidDataException(
                $"Multiple application layers use id '{id}': " +
                string.Join(", ", candidates.Select(type => type.FullName)))
        };
    }
}
