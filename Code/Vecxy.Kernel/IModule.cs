namespace Vecxy.Kernel;

public interface IModule : IDisposable
{
    void OnInitialize();
    void OnShutdown();
    
    public interface IUpdatable
    {
        void OnUpdate(float deltaTime);
    }
    
    public interface IRenderable
    {
        void OnRender();
    }
}



