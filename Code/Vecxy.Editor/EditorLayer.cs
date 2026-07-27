using Vecxy.Engine;

using JetBrains.Annotations;
using Vecxy.Diagnostics.Console;
using Vecxy.Kernel;

namespace Vecxy.Editor;

[UsedImplicitly]
public sealed class EditorLayer(IEnumerable<IModule> modules) : AAppLayer
{
    public sealed class Definition : ADefinition<EditorLayer>
    {
        public override IReadOnlyList<Vecxy.Kernel.IDefinition> Children =>
            [
                new DebugConsoleModule.Definition(),
                new EditorModule.Definition()
            ];
    }

    private readonly IModule[] _modules = modules
        .Where(module =>
            module is EditorModule or
            Vecxy.Diagnostics.Console.DebugConsoleModule)
        .ToArray();

    public override void OnInitialize()
    {
        foreach (var module in _modules)
            module.OnInitialize();
    }

    public override void OnUpdate(float deltaTime)
    {
        foreach (var module in _modules)
        {
            if (module is IModule.IUpdatable updatable)
                updatable.OnUpdate(deltaTime);
        }
    }

    public override void OnUnload()
    {
        for (var index = _modules.Length - 1; index >= 0; index--)
        {
            try
            {
                _modules[index].OnShutdown();
            }
            catch
            {
                // Ignore
            }
        }
    }
}
