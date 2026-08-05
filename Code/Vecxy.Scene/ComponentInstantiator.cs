using System.Numerics;

namespace Vecxy.Scene;

public class InstantiateContext
{
    public SceneInstance? Scene { get; init; }
    public SceneObject? Parent { get; init; }

    public Vector3 Position = Vector3.Zero;
    public Quaternion Rotation { get; init; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;
}

public interface IComponentInstantiator
{
    public TComponent Instantiate<TComponent>(SceneInstance? scene = null) where TComponent : AComponent;
    public TComponent Instantiate<TComponent>(InstantiateContext ctx) where TComponent : AComponent;

    public TComponent Instantiate<TComponent>(InstantiateContext ctx, AComponent.IPrototype.IOptions options)
        where TComponent : AComponent;
}

public class ComponentInstantiator : IComponentInstantiator
{
    private readonly Dictionary<Type, AComponent.IPrototype> _prototypes = new();

    public ComponentInstantiator(IEnumerable<AComponent.IPrototype> prototypes)
    {
        foreach (var prototype in prototypes)
        {
            _prototypes.Add(prototype.ComponentType, prototype);
        }
    }

    public TComponent Instantiate<TComponent>(SceneInstance? scene = null) where TComponent : AComponent =>
        Instantiate<TComponent>
        (
            new InstantiateContext
            {
                Scene = scene
            }
        );

    public TComponent Instantiate<TComponent>(InstantiateContext ctx) where TComponent : AComponent
    {
        var component = GetPrototype<TComponent>().Instantiate(ctx);

        return (TComponent)component;
    }

    public TComponent Instantiate<TComponent>(InstantiateContext ctx, AComponent.IPrototype.IOptions options)
        where TComponent : AComponent
    {
        var prototype = GetPrototype<TComponent>();

        var component = prototype.Instantiate(ctx);

        prototype.Configure(component, options);

        return (TComponent)component;
    }

    private AComponent.IPrototype GetPrototype<TComponent>()
    {
        var type = typeof(TComponent);

        return !_prototypes.TryGetValue(type, out var prototype)
            ? throw new Exception($"Not found prototype '{type.Name}.APrototype<{type.Name}>'")
            : prototype;
    }
}