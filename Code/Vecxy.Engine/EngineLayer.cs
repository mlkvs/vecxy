using JetBrains.Annotations;
using Vecxy.Assets;
using Vecxy.Kernel;
using Vecxy.Rendering;
using Vecxy.Scene;

namespace Vecxy.Engine;

[UsedImplicitly]
public sealed class EngineLayer(IEnumerable<IModule> modules) : AAppLayer
{
    public sealed class Definition : ADefinition<EngineLayer>
    {
        public override IReadOnlyList<Vecxy.Kernel.IDefinition> Children { get; }

        public Definition(AssetsModule.Options? assets = null)
        {
            Children =
            [
                new AssetsModule.Definition(assets),
                new RenderingModule.Definition(),
                new ScenesModule.Definition()
            ];
        }
    }

    public override void OnInitialize()
    {
        foreach (var module in modules)
        {
            module.OnInitialize();
        }
    }

    public override void OnUpdate(float deltaTime)
    {
        foreach (var module in modules)
        {
            if (module is IModule.IUpdatable updatable)
            {
                updatable.OnUpdate(deltaTime);
            }
        }
    }

    public override void OnRender()
    {
        foreach (var module in modules)
        {
            if (module is IModule.IRenderable renderable)
            {
                renderable.OnRender();
            }
        }
    }

    public override void OnUnload()
    {
        foreach (var module in modules)
        {
            try
            {
                module.OnShutdown();
            }
            catch
            {
                // Ignore
            }
        }
    }
}
