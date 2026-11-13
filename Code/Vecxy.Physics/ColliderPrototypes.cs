using System.Numerics;
using Vecxy.Prototypes;
using Vecxy.Scene;

namespace Vecxy.Physics;

public class ColliderOptions
{
    [Authoring("Center", Order = 1, Description = "Offset in the object's local coordinates.")]
    public Vector3 Center { get; set; }
    [Authoring("Friction", Min = 0, Order = 20)]
    public float Friction { get; set; } = 0.5f;
    [Authoring("Restitution", Min = 0, Max = 1, Order = 21)]
    public float Restitution { get; set; }
    [Authoring("Trigger", Order = 22)]
    public bool IsTrigger { get; set; }

    internal void Apply(Collider collider)
    {
        collider.Center = Center;
        collider.Material.Friction = Friction;
        collider.Material.Restitution = Restitution;
        collider.IsTrigger = IsTrigger;
    }
}

public sealed partial class BoxCollider
{
    public sealed class Prototype : APrototype<BoxCollider, Prototype.Options>
    {
        public sealed class Options : ColliderOptions
        {
            [Authoring("Size", Min = 0.001, Order = 0, Description = "Full dimensions in local coordinates.")]
            public Vector3 Size { get; set; } = Vector3.One;
        }
        protected override BoxCollider Instantiate(IPrototypeContext context)
        {
            var ctx = RequireContext<ScenePrototypeContext>(context);
            return (ctx.Object ?? ctx.CreateObject("Box collider")).AddComponent<BoxCollider>();
        }
        protected override void Configure(BoxCollider collider, Options options)
        {
            collider.Size = options.Size;
            options.Apply(collider);
        }
    }
}

public sealed partial class SphereCollider
{
    public sealed class Prototype : APrototype<SphereCollider, Prototype.Options>
    {
        public sealed class Options : ColliderOptions
        {
            [Authoring("Radius", Min = 0.001, Order = 0)]
            public float Radius { get; set; } = 0.5f;
        }
        protected override SphereCollider Instantiate(IPrototypeContext context)
        {
            var ctx = RequireContext<ScenePrototypeContext>(context);
            return (ctx.Object ?? ctx.CreateObject("Sphere collider")).AddComponent<SphereCollider>();
        }
        protected override void Configure(SphereCollider collider, Options options)
        {
            collider.Radius = options.Radius;
            options.Apply(collider);
        }
    }
}

public enum EColliderAxis { X, Y, Z }

public sealed partial class CapsuleCollider
{
    public sealed class Prototype : APrototype<CapsuleCollider, Prototype.Options>
    {
        public sealed class Options : ColliderOptions
        {
            [Authoring("Radius", Min = 0.001, Order = 0)]
            public float Radius { get; set; } = 0.35f;
            [Authoring("Cylinder height", Min = 0, Order = 2, Description = "Height between hemisphere centers.")]
            public float Height { get; set; } = 1.1f;
            [Authoring("Axis", Default = "Z", Order = 3)]
            public EColliderAxis Axis { get; set; } = EColliderAxis.Y;
        }
        protected override CapsuleCollider Instantiate(IPrototypeContext context)
        {
            var ctx = RequireContext<ScenePrototypeContext>(context);
            return (ctx.Object ?? ctx.CreateObject("Capsule collider")).AddComponent<CapsuleCollider>();
        }
        protected override void Configure(CapsuleCollider collider, Options options)
        {
            collider.Radius = options.Radius;
            collider.Height = options.Height;
            collider.Rotation = options.Axis switch
            {
                EColliderAxis.X => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2),
                EColliderAxis.Z => Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                _ => Quaternion.Identity
            };
            options.Apply(collider);
        }
    }
}

public sealed partial class RigidBody
{
    public sealed class Prototype : APrototype<RigidBody, Prototype.Options>
    {
        public sealed class Options
        {
            [Authoring("Motion", Order = 0)]
            public EPhysicsMotionType MotionType { get; set; } = EPhysicsMotionType.Dynamic;
            [Authoring("Mass", Min = 0.001, Unit = "kg", Order = 1)]
            public float Mass { get; set; } = 1;
            [Authoring("Gravity", Order = 2)]
            public bool AffectedByGravity { get; set; } = true;
            [Authoring("Linear damping", Min = 0, Max = 1, Order = 3)]
            public float LinearDamping { get; set; }
        }
        protected override RigidBody Instantiate(IPrototypeContext context)
        {
            var ctx = RequireContext<ScenePrototypeContext>(context);
            return (ctx.Object ?? ctx.CreateObject("Rigid body")).AddComponent<RigidBody>();
        }
        protected override void Configure(RigidBody body, Options options)
        {
            body.MotionType = options.MotionType;
            body.Mass = options.Mass;
            body.AffectedByGravity = options.AffectedByGravity;
            body.LinearDamping = options.LinearDamping;
        }
    }
}
