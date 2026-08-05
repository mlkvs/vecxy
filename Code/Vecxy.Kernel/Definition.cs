using Autofac;

namespace Vecxy.Kernel;

public interface IDefinition
{
    IReadOnlyList<IDefinition> Children { get; }

    void RegisterGlobal(ContainerBuilder builder);
    void RegisterLocal(ContainerBuilder builder);
}

public abstract class ADefinition : IDefinition
{
    public virtual IReadOnlyList<IDefinition> Children => [];

    public virtual void RegisterGlobal(ContainerBuilder builder)
    {
    }

    public virtual void RegisterLocal(ContainerBuilder builder)
    {
    }
}

public abstract class AModuleDefinition<TModule> : ADefinition
    where TModule : class, IModule
{
    protected virtual IReadOnlyList<Type> Exports => [];

    protected virtual void RegisterModule(ContainerBuilder builder) { }

    public sealed override void RegisterLocal(ContainerBuilder builder)
    {
        builder
            .Register(context => new ModuleScope<TModule>(
                context.Resolve<ILifetimeScope>(),
                RegisterModule))
            .AsSelf()
            .SingleInstance();

        var registration = builder
            .Register(context => context.Resolve<ModuleScope<TModule>>().Module)
            .As<IModule>();

        foreach (var export in Exports)
        {
            if (!export.IsAssignableFrom(typeof(TModule)))
            {
                throw new InvalidOperationException(
                    $"Module '{typeof(TModule).FullName}' does not implement export '{export.FullName}'.");
            }

            registration.As(export);
        }

        registration.ExternallyOwned();
    }
}

internal sealed class ModuleScope<TModule> : IDisposable
    where TModule : class, IModule
{
    private readonly ILifetimeScope _scope;

    public TModule Module { get; }

    public ModuleScope(
        ILifetimeScope owner,
        Action<ContainerBuilder> registerModule)
    {
        _scope = owner.BeginLifetimeScope(registerModule);

        try
        {
            Module = _scope.Resolve<TModule>();
        }
        catch
        {
            _scope.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _scope.Dispose();
    }
}
