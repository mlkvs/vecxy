using System.Reflection;

namespace Vecxy.Reflection;

public class EmbeddedResource
{
    public readonly Assembly Assembly;
    public readonly string ResourcePath;
    public readonly string ResourceName;


    private EmbeddedResource(Assembly assembly, string resourceName)
    {
        Assembly = assembly;
        ResourceName = resourceName;
        ResourcePath = Path(assembly, resourceName);
    }
    
    private EmbeddedResource(Assembly assembly, string resourceName, string resourcePath)
    {
        Assembly = assembly;
        ResourceName = resourceName;
        ResourcePath = resourcePath;
    }

    public static bool Has(Assembly? assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var resources = assembly.GetManifestResourceNames();

        for (int index = 0, count = resources.Length; index < count; index++)
        {
            var resource = resources[index];

            if (resource.EndsWith(resourceName))
            {
                return true;
            }
        }

        return false;
    }

    public static string Path(Assembly? assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        
        var resources = assembly.GetManifestResourceNames();
        
        for (int index = 0, count = resources.Length; index < count; index++)
        {
            var resource = resources[index];

            if (resource.EndsWith(resourceName))
            {
                return resource;
            }
        }
        
        throw new FileNotFoundException();
    }

    public static EmbeddedResource[] From(Assembly? assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var resources = assembly.GetManifestResourceNames();
        
        var results = new EmbeddedResource[resources.Length];

        for (int index = 0, count = resources.Length; index < count; index++)
        {
            var resourcePath = resources[index];

            var pathSplit = resourcePath.Split(".");

            var name = pathSplit[^2];
            var extension = pathSplit[^1];
            
            var resourceName = string.Join(".", name, extension);
            
            results[index] = new EmbeddedResource(assembly, resourceName, resourcePath);
        }

        return results;
    }

    public static EmbeddedResource? Get(Assembly? assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (Has(assembly, resourceName) == false)
        {
            return null;
        }
        
        var resource = new EmbeddedResource(assembly, resourceName);

        return resource;
    }

    public static bool TryGet(Assembly? assembly, string resourceName, out EmbeddedResource? resource)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        resource = Get(assembly, resourceName);
        
        return resource != null;
    }
    
    public string Text()
    {
        using var stream = Stream();
        
        using var reader = new StreamReader(stream);
        
        return reader.ReadToEnd();
    }

    public Stream Stream()
    {
        return Assembly.GetManifestResourceStream(ResourcePath)!;
    }
}