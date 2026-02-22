using Vecxy.Diagnostics;

namespace Vecxy.Assets.V2;

public interface IAssetsManager
{
    public void LoadPack(string name);
}

public class AssetsManager : IAssetsManager
{
    public void LoadPack(string name)
    {
        Logger.Info($"Loading pack {name}");
    }
}