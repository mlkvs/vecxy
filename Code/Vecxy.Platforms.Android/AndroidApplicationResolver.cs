using System.Reflection;
using Vecxy.Kernel;

namespace Vecxy.Platforms.Android;

internal static class AndroidApplicationResolver
{
    public static IVecxyApplication Create()
    {
        var candidates = AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(GetDefinedTypes)
            .Where(type =>
                type is
                {
                    IsAbstract: false,
                    IsInterface: false,
                    IsPublic: true
                } &&
                type.IsDefined(
                    typeof(VecxyApplicationAttribute),
                    inherit: false) &&
                typeof(IVecxyApplication).IsAssignableFrom(type) &&
                type.GetConstructor(Type.EmptyTypes) is not null)
            .ToArray();

        return candidates.Length switch
        {
            1 => (IVecxyApplication)Activator.CreateInstance(candidates[0])!,

            0 => throw new InvalidOperationException(
                $"No class marked with [{nameof(VecxyApplicationAttribute)}] " +
                $"and implementing {nameof(IVecxyApplication)} was found."),

            _ => throw new InvalidOperationException(
                $"Multiple classes marked with " +
                $"[{nameof(VecxyApplicationAttribute)}] were found: " +
                string.Join(", ", candidates.Select(type => type.FullName)))
        };
    }

    private static IEnumerable<Type> GetDefinedTypes(Assembly assembly)
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