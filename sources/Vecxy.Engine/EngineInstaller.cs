using Zenject;

namespace Vecxy.Engine;

internal class EngineInstaller : Installer
{
    public override void InstallBindings()
    {
        Container
            .Bind<ISceneManager>()
            .To<SceneManager>()
            .AsSingle()
            .NonLazy();

        GameInstaller.Install(Container);
    }
}