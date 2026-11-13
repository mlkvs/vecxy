using Vecxy.Prototypes;
using Vecxy.Scene;

namespace Vecxy.Usd;

/// <summary>The prototype/spawn entry point for an authored USD scene.</summary>
public sealed class UsdScene : AComponent
{
    public UsdSceneInstance Instance { get; private set; } = null!;

    public sealed class Prototype(IUsdSceneSystem scenes) : APrototype<UsdScene, Prototype.Options>
    {
        public sealed class Options
        {
            public string Source { get; set; } = "";
            public string Name { get; set; } = "USD Scene";
        }

        protected override UsdScene Instantiate(IPrototypeContext context)
        {
            var ctx = RequireContext<ScenePrototypeContext>(context);
            var root = ctx.Object ?? ctx.CreateObject();
            if (ctx.Object is null)
            {
                root.Transform.Position = ctx.Position;
                root.Transform.Rotation = ctx.Rotation;
                root.Transform.Scale = ctx.Scale;
            }
            return root.AddComponent<UsdScene>();
        }

        protected override void Configure(UsdScene target, Options options, IPrototypeContext context)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.Source);
            target.SceneObject!.Name = options.Name;
            target.Instance = scenes.Instantiate(target.SceneInstance, options.Source, options.Name);
            target.Instance.Root.SetParent(target.SceneObject, worldPositionStays: false);
            target.Instance.ApplyComponents(RequireContext<ScenePrototypeContext>(context).Prototypes);
        }
    }
}
