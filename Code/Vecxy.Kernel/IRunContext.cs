namespace Vecxy.Kernel;

public interface IRunContext : IDisposable
{
    bool IsRunning { get; }

    void Initialize();
    void PollEvents();
}