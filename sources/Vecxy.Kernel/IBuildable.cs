namespace Vecxy.Kernel;

public interface IBuildable<out TContext>
{
    public TContext Build();
}