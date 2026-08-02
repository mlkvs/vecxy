using Vecxy.Engine;
using VecxyEngine = Vecxy.Engine.Engine;

namespace Vecxy.Platforms;

public interface IVecxyApplication
{
    VecxyEngine.Options CreateEngineOptions(PlatformContext context);

    IReadOnlyList<AAppLayer.IDefinition> CreateLayers(PlatformContext context);
}
