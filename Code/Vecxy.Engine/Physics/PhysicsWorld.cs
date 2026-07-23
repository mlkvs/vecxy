using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Vecxy.Engine.Physics;

public sealed class PhysicsWorld : IDisposable
{
    private readonly BufferPool _pool = new();
    private float _accumulator;
    internal Simulation Simulation { get; }
    public Vector3 Gravity { get; } = new(0, -9.81f, 0);
    public float FixedTimeStep { get; set; } = 1f / 60f;

    public PhysicsWorld() => Simulation = Simulation.Create(_pool, new NarrowPhaseCallbacks(),
        new PoseIntegratorCallbacks(Gravity), new SolveDescription(8, 1));

    internal PhysicsHandle AddBox(Vector3 position, Quaternion rotation, Vector3 size, PhysicsBodyType type, float mass)
    {
        size = Vector3.Max(Vector3.Abs(size), new Vector3(.001f));
        var shape = new Box(size.X, size.Y, size.Z);
        var shapeIndex = Simulation.Shapes.Add(shape);
        if (type == PhysicsBodyType.Static)
        {
            var staticDescription = new StaticDescription(position, rotation, shapeIndex);
            return new PhysicsHandle(Simulation.Statics.Add(staticDescription), shapeIndex);
        }
        var collidable = new CollidableDescription(shapeIndex, .1f);
        var activity = new BodyActivityDescription(.01f);
        var pose = new RigidPose(position, rotation);
        var description = type == PhysicsBodyType.Kinematic
            ? BodyDescription.CreateKinematic(pose, collidable, activity)
            : BodyDescription.CreateDynamic(pose, shape.ComputeInertia(MathF.Max(.001f, mass)), collidable, activity);
        return new PhysicsHandle(Simulation.Bodies.Add(description), shapeIndex);
    }

    internal PhysicsHandle AddCapsule(Vector3 position, float radius, float length, float mass)
    {
        var shape = new Capsule(radius, length);
        var shapeIndex = Simulation.Shapes.Add(shape);
        var inertia = shape.ComputeInertia(MathF.Max(.001f, mass));
        inertia.InverseInertiaTensor = default;
        var description = BodyDescription.CreateDynamic(new RigidPose(position), inertia,
            new CollidableDescription(shapeIndex, .1f), new BodyActivityDescription(.01f));
        return new PhysicsHandle(Simulation.Bodies.Add(description), shapeIndex);
    }

    internal void Remove(PhysicsHandle handle)
    {
        if (handle.IsBody) Simulation.Bodies.Remove(handle.Body);
        else Simulation.Statics.Remove(handle.Static);
        Simulation.Shapes.RemoveAndDispose(handle.Shape, _pool);
    }

    internal BodyReference Body(PhysicsHandle handle) => Simulation.Bodies[handle.Body];

    public void Step(float deltaTime)
    {
        _accumulator += Math.Min(deltaTime, .1f);
        while (_accumulator >= FixedTimeStep)
        {
            Simulation.Timestep(FixedTimeStep);
            _accumulator -= FixedTimeStep;
        }
    }

    public void Dispose() { Simulation.Dispose(); _pool.Clear(); }

    private struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation) { }
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b,
            ref float speculativeMargin) => a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;
        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;
        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold,
            out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial = new PairMaterialProperties(1f, 2f, new SpringSettings(30f, 1f));
            return true;
        }
        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB,
            ref ConvexContactManifold manifold) => true;
        public void Dispose() { }
    }

    private struct PoseIntegratorCallbacks(Vector3 gravity) : IPoseIntegratorCallbacks
    {
        public Vector3 Gravity = gravity;
        private Vector3Wide _gravityWideDt;
        public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public bool AllowSubstepsForUnconstrainedBodies => false;
        public bool IntegrateVelocityForKinematics => false;
        public void Initialize(Simulation simulation) { }
        public void PrepareForIntegration(float dt) { Vector3Wide.Broadcast(Gravity * dt, out _gravityWideDt); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
            BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt,
            ref BodyVelocityWide velocity) => velocity.Linear += _gravityWideDt;
    }
}

public enum PhysicsBodyType { Static, Kinematic, Dynamic }

internal readonly struct PhysicsHandle
{
    public readonly BodyHandle Body;
    public readonly StaticHandle Static;
    public readonly TypedIndex Shape;
    public readonly bool IsBody;
    public PhysicsHandle(BodyHandle body, TypedIndex shape) { Body = body; Static = default; Shape = shape; IsBody = true; }
    public PhysicsHandle(StaticHandle value, TypedIndex shape) { Static = value; Body = default; Shape = shape; IsBody = false; }
}
