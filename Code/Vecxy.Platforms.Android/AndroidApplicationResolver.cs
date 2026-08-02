using System.Reflection;

namespace Vecxy.Platforms.Android;

internal static class AndroidApplicationResolver
{
    public static IVecxyApplication Create()
    {
        var entryAssembly = Assembly.GetEntryAssembly() ??
            throw new InvalidOperationException("The Android entry assembly is unavailable.");
        var candidates = entryAssembly.DefinedTypes
            .Where(type =>
                !type.IsAbstract &&
                !type.IsInterface &&
                typeof(IVecxyApplication).IsAssignableFrom(type.AsType()) &&
                type.GetConstructor(Type.EmptyTypes) is not null)
            .ToArray();

        return candidates.Length switch
        {
            1 => (IVecxyApplication)Activator.CreateInstance(candidates[0].AsType())!,
            0 => throw new InvalidOperationException(
                $"Assembly '{entryAssembly.GetName().Name}' must contain one public " +
                $"{nameof(IVecxyApplication)} implementation with a parameterless constructor."),
            _ => throw new InvalidOperationException(
                $"Assembly '{entryAssembly.GetName().Name}' contains multiple " +
                $"{nameof(IVecxyApplication)} implementations: " +
                string.Join(", ", candidates.Select(type => type.FullName)))
        };
    }
}
