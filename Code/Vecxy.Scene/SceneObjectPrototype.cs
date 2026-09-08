using System.Numerics;
using Vecxy.Prototypes;

namespace Vecxy.Scene;

public sealed partial class SceneObject
{
    public sealed class Prototype : APrototype<SceneObject, Prototype.Options>
    {
        public sealed class Options
        {
            public string Name { get; set; } = "SceneObject";
            public bool Enabled { get; set; } = true;
            public bool IsStatic { get; set; }
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; } = Quaternion.Identity;
            public Vector3 Scale { get; set; } = Vector3.One;
            public List<PrototypeReference> Components { get; set; } = [];
            public List<PrototypeReference> Children { get; set; } = [];
        }

        protected override SceneObject Instantiate(IPrototypeContext context)
        {
            var ctx = RequireContext<ScenePrototypeContext>(context);
            return ctx.CreateObject(parent: ctx.Parent);
        }

        protected override void Configure(SceneObject target, Options options, IPrototypeContext context)
        {
            target.Name = options.Name;
            target.IsStatic = options.IsStatic;
            target.Transform.Position = options.Position;
            target.Transform.Rotation = options.Rotation;
            target.Transform.Scale = options.Scale;
            RequireContext<ScenePrototypeContext>(context).SetEnabled(target, options.Enabled);
            var ctx = RequireContext<ScenePrototypeContext>(context);
            foreach (var component in options.Components)
            {
                var created = ctx.Prototypes.Instantiate(component.Path, new ScenePrototypeContext
                {
                    Scene = ctx.Scene,
                    Object = target
                }, ToOverrides(component));
                if (created is not AComponent)
                    throw new InvalidDataException($"Prototype '{component.Path}' does not create a scene component.");
            }
            foreach (var child in options.Children)
            {
                _ = ctx.Prototypes.Instantiate(child.Path, new ScenePrototypeContext
                {
                    Scene = ctx.Scene,
                    Parent = target
                }, ToOverrides(child));
            }
        }

        private static PrototypeOverrides? ToOverrides(PrototypeReference reference) => reference.Overrides.Count == 0
            ? null
            : new PrototypeOverrides(PrototypeSerializer.SerializeOptions(reference.Overrides));
    }
}
