using Vecxy.Engine;

namespace Vecxy.Platforms;

public interface IVEntry
{
    void OnConfigureEngine(PlatformContext context, Engine.Engine.Options options);

    void OnConfigureLayers(PlatformContext context, List<AAppLayer.IDefinition> layers);
}