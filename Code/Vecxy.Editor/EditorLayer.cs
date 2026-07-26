using Vecxy.Engine;

using JetBrains.Annotations;
using Vecxy.Kernel;

namespace Vecxy.Editor;

[UsedImplicitly]
public sealed class EditorLayer(IEnumerable<IModule> modules) : AAppLayer
{
    public sealed class Definition : ADefinition<EditorLayer>
    {
        public override IReadOnlyList<Vecxy.Kernel.IDefinition> Children =>
            [new EditorModule.Definition()];
    }

    private readonly EditorModule _module =
        modules.OfType<EditorModule>().Single();

    public override void OnInitialize()
    {
        _module.OnInitialize();
    }

    public override void OnUpdate(float deltaTime)
    {
        _module.OnUpdate(deltaTime);
    }

    public override void OnUnload()
    {
        try
        {
            _module.OnShutdown();
        }
        catch
        {
            // Ignore
        }
    }
}
