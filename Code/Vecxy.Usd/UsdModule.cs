using System.Numerics;
using Autofac;
using Vecxy.Assets;
using Vecxy.Kernel;
using Vecxy.Physics;
using Vecxy.Prototypes;
using Vecxy.Rendering;
using Vecxy.Scene;

namespace Vecxy.Usd;

public interface IUsdSceneSystem
{
    UsdSceneInstance Instantiate(SceneInstance scene, string assetPath, string? name = null);
}

/// <summary>Opt-in scene loader; add its Definition to EngineLayer additionalModules.</summary>
public sealed class UsdModule(IAssetsManager assets, ISceneInstantiator instantiator) : IModule, IUsdSceneSystem
{
    public sealed class Definition : AModuleDefinition<UsdModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(IUsdSceneSystem)];
        protected override void RegisterModule(ContainerBuilder builder) =>
            builder.RegisterType<UsdModule>().AsSelf().SingleInstance();
    }

    public void OnInitialize() => assets.RegisterImporter(new UsdSceneAssetImporter());
    public void OnShutdown() { }
    public void Dispose() { }

    public UsdSceneInstance Instantiate(SceneInstance scene, string assetPath, string? name = null)
    {
        var source = assets.Load<UsdSceneAsset>(assetPath);
        Model? model = null;
        SceneObject? root = null;
        try
        {
            model = new Model(source.Value.Model);
            root = instantiator.InstantiateModel(scene, model, name ?? Path.GetFileNameWithoutExtension(assetPath));
            var instance = root.GetComponent<ModelInstance>()!;
            var prims = new Dictionary<string, SceneObject>(StringComparer.Ordinal);
            var roles = new Dictionary<string, List<SceneObject>>(StringComparer.Ordinal);
            foreach (var record in source.Value.Document.Prims)
            {
                var node = instance.GetNode(record.Node);
                prims.Add(record.Path, node);
                node.AddComponent(new UsdPrim(record.Path, record.Prototype, record.Role, source.Value.Document.UpAxis));
                if (record.Role.Length > 0)
                {
                    if (!roles.TryGetValue(record.Role, out var objects))
                        roles.Add(record.Role, objects = []);
                    objects.Add(node);
                }
                Collider? collider = record.Collider switch
                {
                    "box" => node.AddComponent<BoxCollider>(),
                    "sphere" => node.AddComponent<SphereCollider>(),
                    "capsule" => node.AddComponent<CapsuleCollider>(),
                    "" when record.ColliderSize is not null => node.AddComponent<BoxCollider>(),
                    _ => null
                };
                if (collider is not null)
                {
                    if (collider is BoxCollider box && record.ColliderSize is { } size)
                        box.Size = new Vector3(size[0], size[1], size[2]);
                    if (collider is SphereCollider sphere) sphere.Radius = record.ColliderRadius;
                    if (collider is CapsuleCollider capsule)
                    {
                        capsule.Radius = record.ColliderRadius;
                        capsule.Height = record.ColliderHeight;
                        capsule.Rotation = record.ColliderAxis switch
                        {
                            "Z" => Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI * .5f),
                            "X" => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI * .5f),
                            _ => Quaternion.Identity
                        };
                    }
                    collider.Center = new Vector3(record.ColliderCenter[0], record.ColliderCenter[1], record.ColliderCenter[2]);
                    collider.Material.Friction = record.Friction;
                    collider.Material.Restitution = record.Restitution;
                    collider.IsTrigger = record.Trigger;
                }
                if (record.Tint is { } tint)
                    foreach (var child in node.EnumerateHierarchy())
                        child.GetComponent<MeshRenderer>()?.Material.SetVector("uTint", new Vector4(tint[0], tint[1], tint[2], tint[3]));
                ALight? light = record.Light switch
                {
                    "point" => node.AddComponent<PointLight>(),
                    "directional" => node.AddComponent<DirectionalLight>(),
                    "spot" => node.AddComponent<SpotLight>(),
                    "" => null,
                    _ => throw new InvalidDataException($"Unsupported USD light '{record.Light}' at {record.Path}.")
                };
                if (light is not null)
                {
                    light.Color = new Vector3(record.Color[0], record.Color[1], record.Color[2]);
                    light.Intensity = record.Intensity;
                    light.Range = record.Range;
                }
            }
            return root.AddComponent(new UsdSceneInstance(source, model, prims, roles));
        }
        catch
        {
            root?.Destroy();
            model?.Dispose();
            source.Dispose();
            throw;
        }
    }
}

public sealed class UsdPrim(string path, string prototype, string role, string upAxis = "Y") : AComponent
{
    public string Path { get; } = path;
    public string Prototype { get; } = prototype;
    public string Role { get; } = role;
    public string UpAxis { get; } = upAxis;
}

public sealed class UsdSceneInstance : AComponent
{
    private readonly AssetRef<UsdSceneAsset> _source;
    private readonly Model _model;
    private readonly IReadOnlyDictionary<string, SceneObject> _prims;
    private readonly IReadOnlyDictionary<string, List<SceneObject>> _roles;

    internal UsdSceneInstance(AssetRef<UsdSceneAsset> source, Model model,
        IReadOnlyDictionary<string, SceneObject> prims, IReadOnlyDictionary<string, List<SceneObject>> roles)
    {
        _source = source;
        _model = model;
        _prims = prims;
        _roles = roles;
    }

    public SceneObject Root => SceneObject!;
    public SceneObject GetPrim(string path) => _prims.TryGetValue(path, out var prim)
        ? prim : throw new KeyNotFoundException($"USD prim '{path}' was not imported.");
    public IReadOnlyList<SceneObject> FindRole(string role) => _roles.TryGetValue(role, out var values) ? values : [];
    public SceneObject RequireRole(string role) => FindRole(role) is { Count: 1 } matches ? matches[0]
        : throw new InvalidDataException($"USD scene requires exactly one '{role}' marker.");

    public void ApplyComponents(IPrototypes prototypes)
    {
        foreach (var record in _source.Value.Document.Prims)
        {
            foreach (var component in record.Components.Where(c => c.Enabled))
            {
                var definition = prototypes.Types.SingleOrDefault(info => info.TargetType.FullName == component.Type)
                    ?? throw new InvalidDataException($"No Vecxy prototype for USD component '{component.Type}' at '{record.Path}'.");
                if (!typeof(AComponent).IsAssignableFrom(definition.TargetType))
                    throw new InvalidDataException($"USD components must create AComponent: '{component.Type}'.");
                var document = PrototypeSerializer.Deserialize("format: 1\ntype: " + component.Type + "\ndata: " + component.Data.GetRawText());
                var options = PrototypeSerializer.DeserializeOptions(document.Data, definition.OptionsType);
                prototypes.Instantiate(definition.TargetType,
                    new ScenePrototypeContext { Scene = SceneInstance, Object = _prims[record.Path] }, options);
            }
        }
    }

    public override void OnDestroy()
    {
        _model.Dispose();
        _source.Dispose();
    }
}
