namespace Vecxy.Kernel;

public interface IVecxySystem : IDisposable
{
    public void OnLoad();
    public void OnInitialize();
    public void OnTick();
    public void OnUnload();
}