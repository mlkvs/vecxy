using System.Numerics;
using System.Reflection;

namespace Vecxy.Scene;

public interface IComponentInstantiator
{
    public TComponent Instantiate<TComponent>(AComponent.IPrototype.Context ctx) where TComponent : AComponent;

    public TComponent Instantiate<TComponent>(AComponent.IPrototype.Context ctx, AComponent.IPrototype.IOptions options)
        where TComponent : AComponent;
}

public class ComponentInstantiator : IComponentInstantiator
{
    private Dictionary<Type, AComponent.IPrototype> _prototypes = new();

    public ComponentInstantiator()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies.Distinct())
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (!IsConcretePrototype(type))
                {
                    continue;
                }

                var prototype = CreatePrototype(type);

                if (_prototypes.TryAdd(prototype.ComponentType, prototype))
                {
                    continue;
                }

                var registeredPrototype = _prototypes[prototype.ComponentType];

                throw new InvalidOperationException($"Several prototypes are registered for component " +
                                                    $"'{prototype.ComponentType.FullName}': " +
                                                    $"'{registeredPrototype.GetType().FullName}' and " +
                                                    $"'{type.FullName}'.");
            }
        }

        return;

        static AComponent.IPrototype CreatePrototype(Type prototypeType)
        {
            if (prototypeType.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new InvalidOperationException(
                    $"Prototype '{prototypeType.FullName}' must have " +
                    $"a public parameterless constructor.");
            }

            var instance = Activator.CreateInstance(prototypeType);

            if (instance is not AComponent.IPrototype prototype)
            {
                throw new InvalidOperationException(
                    $"Failed to instantiate prototype " +
                    $"'{prototypeType.FullName}'.");
            }

            return prototype;
        }

        static bool IsConcretePrototype(Type type)
        {
            return type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                   && typeof(AComponent.IPrototype).IsAssignableFrom(type);
        }

        static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.OfType<Type>();
            }
            catch
            {
                return [];
            }
        }
    }

    public TComponent Instantiate<TComponent>(AComponent.IPrototype.Context ctx) where TComponent : AComponent
    {
        var component = GetPrototype<TComponent>().Instantiate(ctx);

        return (TComponent)component;
    }

    public TComponent Instantiate<TComponent>(AComponent.IPrototype.Context ctx, AComponent.IPrototype.IOptions options)
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