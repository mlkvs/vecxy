using System.Reflection;
using Vecxy.Engine;
using Vecxy.Kernel;

namespace Vecxy.Platforms;

public static class ApplicationResolver
{
    public static IVEntry Create()
    {
        var candidates = GetLoadableTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsPublic: true } &&
                           type.IsDefined(typeof(Kernel.AppAttribute), false) &&
                           typeof(IVEntry).IsAssignableFrom(type) &&
                           type.GetConstructor(Type.EmptyTypes) is not null)
            .ToArray();

        return candidates.Length switch
        {
            1 => (IVEntry)Activator.CreateInstance(candidates[0])!,
            0 => throw new InvalidOperationException(
                $"No public {nameof(IVEntry)} marked with [{nameof(Kernel.AppAttribute)}] was found."),
            _ => throw new InvalidOperationException(
                $"Multiple classes marked with [{nameof(Kernel.AppAttribute)}] were found: " +
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
        var loadableTypes = ApplicationResolver.GetLoadableTypes().ToArray();
        var layers = loadableTypes
            .Where(type => typeof(AAppLayer).IsAssignableFrom(type) &&
                           type is { IsAbstract: false, IsInterface: false } &&
                           type.GetCustomAttribute<LayerAttribute>()?.Id.Equals(
                               id, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        var layer = layers.Length switch
        {
            1 => layers[0],
            0 => throw new InvalidDataException(
                $"Unknown application layer '{id}'. Add [{nameof(LayerAttribute)}(\"{id}\")] to its layer class."),
            _ => throw new InvalidDataException(
                $"Multiple application layers use id '{id}': " +
                string.Join(", ", layers.Select(type => type.FullName)))
        };

        var definitions = loadableTypes
            .Where(type => typeof(AAppLayer.IDefinition).IsAssignableFrom(type) &&
                           type is { IsAbstract: false, IsInterface: false } &&
                           type.GetConstructor(Type.EmptyTypes) is not null)
            .Select(type => (Type: type, Value: (AAppLayer.IDefinition)Activator.CreateInstance(type)!))
            .Where(candidate => candidate.Value.LayerType == layer)
            .ToArray();

        return definitions.Length switch
        {
            1 => definitions[0].Value,
            0 => throw new InvalidOperationException(
                $"Application layer '{layer.FullName}' must have one definition with a parameterless constructor."),
            _ => throw new InvalidOperationException(
                $"Application layer '{layer.FullName}' has multiple definitions: " +
                string.Join(", ", definitions.Select(candidate => candidate.Type.FullName)))
        };
    }
}
