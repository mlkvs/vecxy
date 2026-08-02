using JetBrains.Annotations;
using Vecxy.Assets;
using Vecxy.Input;
using Vecxy.Interaction;
using Vecxy.Kernel;
using Vecxy.Physics;
using Vecxy.Rendering;
using Vecxy.Scene;
using Vecxy.UI;

namespace Vecxy.Engine;

[UsedImplicitly]
public sealed class EngineLayer(
    IEnumerable<IModule> modules,
    IAssetsManager assets,
    IInputManager input,
    IWindow window) : AAppLayer
{
    public sealed class Definition : ADefinition<EngineLayer>
    {
        public override IReadOnlyList<Vecxy.Kernel.IDefinition> Children { get; }

        public Definition(
            AssetsModule.Options? assets = null,
            PhysicsModule.Options? physics = null)
        {
            Children =
            [
                new AssetsModule.Definition(assets),
                new RenderingModule.Definition(),
                new InputModule.Definition(),
                new ScenesModule.Definition(),
                new PhysicsModule.Definition(physics),
                new UiModule.Definition(),
                new PointerInteractionModule.Definition(),
                //new AudioModule.Definition()
            ];
        }
    }

    private AssetRef<InputAsset>? _engineInputAsset;
    private InputMap? _engineInput;

    public override void OnInitialize()
    {
        foreach (var module in modules)
        {
            module.OnInitialize();
        }

        _engineInputAsset = assets.Load<InputAsset>("Controls.input");
        _engineInput = input.Create(_engineInputAsset, "Engine");
        _engineInput.GetAction("ToggleFullscreen").Started +=
            _ => window.ToggleFullscreen();
        _engineInput.Enable();
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
        _engineInput?.Dispose();
        _engineInput = null;
        _engineInputAsset?.Dispose();
        _engineInputAsset = null;

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
