namespace Vecxy.Prototypes;

public interface IPrototypeContext;

public abstract class APrototypeContext : IPrototypeContext
{
    private IPrototypes? _prototypes;
    public IPrototypes Prototypes => _prototypes ?? throw new InvalidOperationException(
        "The prototype context is not currently bound to IPrototypes.");
    internal void Bind(IPrototypes prototypes) => _prototypes = prototypes;
}

public sealed class PrototypeReference
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, object?> Overrides { get; set; } = [];
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class PrototypeAliasAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

public interface IPrototype
{
    Type TargetType { get; }
    Type OptionsType { get; }
    object CreateOptions();
    object Instantiate(IPrototypeContext context);
    void Configure(object target, object options, IPrototypeContext context);
}

public abstract class APrototype<TTarget, TOptions> : IPrototype
    where TTarget : class
    where TOptions : class, new()
{
    public Type TargetType => typeof(TTarget);
    public Type OptionsType => typeof(TOptions);
    object IPrototype.CreateOptions() => new TOptions();
    object IPrototype.Instantiate(IPrototypeContext context) =>
        Instantiate(context) ?? throw new InvalidOperationException($"Prototype '{GetType().FullName}' returned null.");

    void IPrototype.Configure(object target, object options, IPrototypeContext context)
    {
        if (target is not TTarget typedTarget)
            throw new ArgumentException($"Expected target {typeof(TTarget).FullName}.", nameof(target));
        if (options is not TOptions typedOptions)
            throw new ArgumentException($"Expected options {typeof(TOptions).FullName}.", nameof(options));
        Configure(typedTarget, typedOptions, context);
    }

    protected abstract TTarget Instantiate(IPrototypeContext context);
    protected virtual void Configure(TTarget target, TOptions options) { }
    protected virtual void Configure(TTarget target, TOptions options, IPrototypeContext context) =>
        Configure(target, options);

    protected static TContext RequireContext<TContext>(IPrototypeContext context)
        where TContext : class, IPrototypeContext =>
        context as TContext ?? throw new PrototypeInstantiationException(
            $"Prototype '{typeof(TTarget).FullName}' requires context '{typeof(TContext).FullName}', " +
            $"but received '{context.GetType().FullName}'.");
}

public interface IPrototypeSystem
{
    IReadOnlyList<Type> TargetBaseTypes { get; }
    Type ContextType { get; }
    object Instantiate(IPrototype prototype, object options, IPrototypeContext context);
    IEnumerable<PrototypeDiagnostic> Validate(IPrototype prototype, object options);
}

public abstract class APrototypeSystem<TTargetBase, TContext> : IPrototypeSystem
    where TContext : class, IPrototypeContext
{
    public virtual IReadOnlyList<Type> TargetBaseTypes { get; } = [typeof(TTargetBase)];
    public Type ContextType => typeof(TContext);

    public object Instantiate(IPrototype prototype, object options, IPrototypeContext context)
    {
        if (context is not TContext typedContext)
            throw new PrototypeInstantiationException(
                $"Prototype system '{GetType().FullName}' requires context '{typeof(TContext).FullName}'.");
        return Instantiate(prototype, options, typedContext);
    }

    public virtual IEnumerable<PrototypeDiagnostic> Validate(IPrototype prototype, object options) => [];

    protected virtual object Instantiate(IPrototype prototype, object options, TContext context)
    {
        var target = prototype.Instantiate(context);
        prototype.Configure(target, options, context);
        return target;
    }
}

public sealed record PrototypeInfo(Type TargetType, Type OptionsType, Type SystemType, string Name,
    string Category, IReadOnlyList<string> Aliases);

public sealed record PrototypeSystemInfo(Type SystemType, Type ContextType, IReadOnlyList<Type> TargetBaseTypes);

public enum PrototypeDiagnosticSeverity : byte { Info, Warning, Error }

public sealed record PrototypeDiagnostic(string Code, PrototypeDiagnosticSeverity Severity, string Path, string Message);

public sealed class PrototypeValidationResult(IReadOnlyList<PrototypeDiagnostic> diagnostics)
{
    public IReadOnlyList<PrototypeDiagnostic> Diagnostics { get; } = diagnostics;
    public bool IsValid => Diagnostics.All(x => x.Severity != PrototypeDiagnosticSeverity.Error);
    public static PrototypeValidationResult Success { get; } = new([]);
}

public sealed class PrototypeValidationException(PrototypeValidationResult result)
    : Exception(string.Join(Environment.NewLine, result.Diagnostics.Select(x => $"{x.Code} {x.Path}: {x.Message}")))
{
    public PrototypeValidationResult Result { get; } = result;
}

public sealed class PrototypeInstantiationException : Exception
{
    public PrototypeInstantiationException(string message) : base(message) { }
    public PrototypeInstantiationException(string message, Exception innerException) : base(message, innerException) { }
}
