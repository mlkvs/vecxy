namespace Vecxy.Kernel;

public interface IModule : IDisposable
{
    public void OnLoad() { }
    public void OnInitialize() { }
    public void OnTick(float deltaTime) { }
    public void OnUnload() { }
}