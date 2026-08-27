using Autofac;
using Vecxy.Assets;
using Vecxy.Kernel;

namespace Vecxy.Scripting;

public sealed class ScriptingModule(IAssetsManager assets) : IModule, IModule.IUpdatable, IScriptRuntime
{
    public sealed class Definition : AModuleDefinition<ScriptingModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(IScriptRuntime)];

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder.RegisterType<ScriptingModule>().AsSelf().SingleInstance();
        }
    }

    private readonly LuauRuntime _runtime = new(assets);

    public void OnInitialize() =>
        assets.RegisterImporter<ScriptAsset>(new LuauAssetImporter());

    public void OnUpdate(float deltaTime) => _runtime.Update();

    public void OnShutdown()
    {
        _runtime.DisposeAll();
        assets.UnregisterImporter<ScriptAsset>();
    }

    public IScriptInstance Create(
        IAssetHandle script,
        ScriptContext? context = null,
        ScriptRuntimeOptions? options = null) =>
        _runtime.Create(script, context, options);

    public IScriptInstance Create(
        string path,
        ScriptContext? context = null,
        ScriptRuntimeOptions? options = null) =>
        _runtime.Create(path, context, options);

    public void Dispose()
    {
    }
}
