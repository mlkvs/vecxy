using System.Numerics;
using Vecxy.Prototypes;

namespace Vecxy.Scene;

public sealed class ScenePrototypeContext : APrototypeContext
{
    private readonly List<(SceneObject Object, bool Enabled)> _created = [];
    private bool _completed;

    public required SceneInstance Scene { get; init; }
    public SceneObject? Parent { get; init; }
    public SceneObject? Object { get; init; }
    public Vector3 Position { get; init; }
    public Quaternion Rotation { get; init; } = Quaternion.Identity;
    public Vector3 Scale { get; init; } = Vector3.One;

    public SceneObject CreateObject(string name = "SceneObject", bool isStatic = false, bool enabled = true,
        SceneObject? parent = null)
    {
        if (_completed) throw new InvalidOperationException("Prototype context is already completed.");
        var sceneObject = Scene.CreateObject(name, isStatic, enabled: false);
        _created.Add((sceneObject, enabled));
        sceneObject.SetParent(parent ?? Parent, worldPositionStays: false);
        return sceneObject;
    }

    public void SetEnabled(SceneObject sceneObject, bool enabled)
    {
        var index = _created.FindIndex(x => ReferenceEquals(x.Object, sceneObject));
        if (index < 0) throw new InvalidOperationException("Scene object was not created by this prototype context.");
        _created[index] = (sceneObject, enabled);
    }

    internal void Commit()
    {
        if (_completed) throw new InvalidOperationException("Prototype context is already completed.");
        foreach (var item in _created) item.Object.Enabled = item.Enabled;
        _completed = true;
    }

    internal void Rollback()
    {
        if (_completed) return;
        _completed = true;
        foreach (var item in _created.AsEnumerable().Reverse())
            if (!item.Object.IsDestroyed) Scene.DestroyObject(item.Object);
    }
}

public sealed class ScenePrototypeSystem : APrototypeSystem<AComponent, ScenePrototypeContext>
{
    public override IReadOnlyList<Type> TargetBaseTypes { get; } = [typeof(AComponent), typeof(SceneObject)];

    protected override object Instantiate(IPrototype prototype, object options, ScenePrototypeContext context)
    {
        object? result = null;
        try
        {
            result = base.Instantiate(prototype, options, context);
            context.Commit();
            return result;
        }
        catch (Exception exception)
        {
            if (result is AComponent component && context.Object is not null &&
                ReferenceEquals(component.SceneObject, context.Object))
                context.Object.RemoveComponent(component);
            context.Rollback();
            throw new PrototypeInstantiationException($"Could not instantiate scene prototype '{prototype.TargetType.FullName}'.", exception);
        }
    }

    public override IEnumerable<PrototypeDiagnostic> Validate(IPrototype prototype, object options)
    {
        if (options is not SceneObject.Prototype.Options sceneOptions) return [];
        return ValidateReferences(sceneOptions.Components.Concat(sceneOptions.Children));
    }

    private static IEnumerable<PrototypeDiagnostic> ValidateReferences(IEnumerable<PrototypeReference> references)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Id) || !ids.Add(reference.Id))
                yield return new("VXY3101", PrototypeDiagnosticSeverity.Error, $"data.references[{index}].id",
                    "Prototype reference IDs must be non-empty and unique.");
            if (string.IsNullOrWhiteSpace(reference.Path))
                yield return new("VXY3102", PrototypeDiagnosticSeverity.Error, $"data.references[{index}].path",
                    "Prototype reference path is required.");
            index++;
        }
    }
}
