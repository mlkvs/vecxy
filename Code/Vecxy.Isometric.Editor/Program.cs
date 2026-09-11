using Autofac;
using JetBrains.Annotations;
using Vecxy.Engine;
using Vecxy.Kernel;
using Vecxy.Platforms;
using Vecxy.UI;

namespace Vecxy.Isometric.Editor;

[App]
public sealed class Application : AApp;

[UsedImplicitly]
[Layer("isometric-editor")]
public sealed class IsometricEditorLayer(IUiManager ui, IsometricEditorController controller) : AAppLayer
{
    public sealed class Definition : ADefinition<IsometricEditorLayer>
    {
        public override void RegisterLocal(ContainerBuilder builder)
        {
            builder.RegisterType<AssetWorkspaceService>().SingleInstance();
            builder.RegisterType<EditorFileDialog>().SingleInstance();
            builder.RegisterType<EditorUiFactory>().SingleInstance();
            builder.RegisterType<IsometricEditorController>().SingleInstance();
        }
    }

    private UiDocument? _document;

    public override void OnInitialize()
    {
        controller.Start();
        _document = ui.Load("UI/Workspace.xml");
        _document.Reloaded += Bind;
        Bind(_document);
    }

    public override void OnUpdate(float deltaTime) => controller.Update();

    private void Bind(UiDocument document) => controller.Bind(document);

    public override void OnUnload()
    {
        if (_document is null)
            return;
        _document.Reloaded -= Bind;
        controller.Unbind();
        ui.Unload(_document);
        _document = null;
    }
}
