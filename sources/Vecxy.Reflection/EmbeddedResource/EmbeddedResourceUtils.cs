using System.Reflection;

namespace Vecxy.Reflection;

public static class EmbeddedResourceUtils
{
    public static EmbeddedResource? GetEmbeddedResource(this Assembly? assembly, string resourceName)
    {
        return EmbeddedResource.Get(assembly, resourceName);
    }
    
    public static bool HasEmbeddedResource(this Assembly? assembly, string resourceName)
    {
        return EmbeddedResource.Has(assembly, resourceName);
    }
    
    
}