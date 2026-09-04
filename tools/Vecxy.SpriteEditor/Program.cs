using Autofac;
using JetBrains.Annotations;
using Vecxy.Engine;
using Vecxy.Kernel;
using Vecxy.Platforms;
using Vecxy.UI;

namespace Vecxy.SpriteEditor;

[App]
public sealed class Application : AApp;

[UsedImplicitly]
[Layer("sprite-editor")]
public sealed class SpriteEditorLayer(IUiManager ui, SpriteEditorController controller) : AAppLayer
{
    public sealed class Definition : ADefinition<SpriteEditorLayer>
    {
        public override void RegisterLocal(ContainerBuilder builder)
        {
            builder.RegisterType<AtlasRepository>().SingleInstance();
            builder.RegisterType<ProjectFolderDialog>().SingleInstance();
            builder.RegisterType<SpriteEditorController>().SingleInstance();
        }
    }

    private UiDocument? _document;

    public override void OnInitialize()
    {
        _document = ui.Load("UI/Workspace.xml");
        _document.Reloaded += Bind;
        Bind(_document);
    }

    private void Bind(UiDocument document) => controller.Bind(document);

    public override void OnUpdate(float deltaTime) => controller.Update();

    public override void OnUnload()
    {
        if (_document is null) return;
        _document.Reloaded -= Bind;
        controller.Unbind();
        ui.Unload(_document);
        _document = null;
    }
}
