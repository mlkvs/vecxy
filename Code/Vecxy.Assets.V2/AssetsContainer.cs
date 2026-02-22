namespace Vecxy.Assets.V2;

public abstract class AssetsContainer : IDisposable
{
    public string Name { get; init; }
    public abstract void Dispose();
}