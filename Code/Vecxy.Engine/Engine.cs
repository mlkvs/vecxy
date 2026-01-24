namespace Vecxy.Engine;

public class Engine : IDisposable
{
    private readonly ApplicationLayer _applicationLayer;

    public Engine(ApplicationLayer applicationLayer)
    {
        _applicationLayer = applicationLayer;
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }
}