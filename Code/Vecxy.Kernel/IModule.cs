namespace Vecxy.Kernel;

public interface IModule : IDisposable
{
    public void OnLoad(Autofac.ILifetimeScope scope);
    public void OnInitialize();
    public void OnTick(float deltaTime);
    public void OnFrame();
    public void OnUnload();
}