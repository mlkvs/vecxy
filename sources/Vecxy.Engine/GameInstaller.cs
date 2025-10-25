using Zenject;

namespace Vecxy.Engine;

public class GameInstaller : Installer<GameInstaller>
{
    public override void InstallBindings()
    {
        Container
            .Bind<IGameSettings>()
            .To<GameSettings>()
            .AsSingle();
    }
}