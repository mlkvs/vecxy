using System.Numerics;
using Autofac;
using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using Vecxy.Diagnostics;
using Vecxy.Kernel;
using Vecxy.Scene;
using Logger = Vecxy.Diagnostics.Logger;

namespace Vecxy.Physics;

public sealed class PhysicsModule(
    ISceneManager scenes) :
    IModule,
    IModule.IUpdatable,
    IPhysicsSystem
{
    public sealed class Definition : AModuleDefinition<PhysicsModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(IPhysicsSystem)];

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder
                .RegisterType<PhysicsModule>()
                .AsSelf()
                .SingleInstance();
        }
    }

    private readonly Dictionary<RigidBody, Jitter2.Dynamics.RigidBody> _bodies = [];
    private World? _world;
    private float _accumulator;
    private bool _initialized;
    private bool _disposed;

    public Vector3 Gravity { get; set; } = new(0.0f, -9.81f, 0.0f);

    public void OnInitialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
            return;

        _world = new World
        {
            Gravity = PhysicsShapeFactory.ToJVector(Gravity),
            AllowDeactivation = true
        };
        _world.SolverIterations = (solver: 6, relaxation: 2);
        _initialized = true;
    }

    public void OnUpdate(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized || _world is null)
            return;

        _world.Gravity = PhysicsShapeFactory.ToJVector(Gravity);

        SyncRegisteredBodies();
        SyncBodiesToPhysics();

        const float step = 1.0f / 60.0f;
        _accumulator = Math.Min(_accumulator + deltaTime, 0.25f);

        while (_accumulator >= step)
        {
            _world.Step(step, multiThread: false);
            _accumulator -= step;
        }

        SyncPhysicsToBodies();
    }

    public bool Raycast(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        SceneObject? ignoreSceneObject,
        out PhysicsRaycastHit hit)
    {
        hit = default;

        if (_world is null || maxDistance <= 0.0f)
            return false;

        var directionLengthSquared = direction.LengthSquared();
        if (directionLengthSquared <= float.Epsilon)
            return false;

        var dir = Vector3.Normalize(direction);
        var jOrigin = PhysicsShapeFactory.ToJVector(origin);
        var jDir = PhysicsShapeFactory.ToJVector(dir);

        var found = false;
        var nearestDistance = maxDistance;

        foreach (var (component, body) in _bodies)
        {
            if (component.SceneObject is null ||
                ReferenceEquals(component.SceneObject, ignoreSceneObject))
            {
                continue;
            }

            var collider = component.SceneObject?.GetComponent<Collider>();
            if (collider is null)
                continue;

            foreach (var shape in body.Shapes)
            {
                if (!shape.RayCast(jOrigin, jDir, out var normal, out var lambda))
                    continue;

                if (lambda < 0.0f || lambda > nearestDistance)
                    continue;

                var point = origin + dir * lambda;
                nearestDistance = lambda;
                hit = new PhysicsRaycastHit(
                    component.SceneObject!,
                    collider,
                    component,
                    point,
                    PhysicsShapeFactory.ToVector3(normal),
                    lambda);
                found = true;
            }
        }

        return found;
    }

    public void OnShutdown()
    {
        if (!_initialized)
            return;

        foreach (var body in _bodies.Values.ToArray())
            _world?.Remove(body);

        _bodies.Clear();
        _world?.Dispose();
        _world = null;
        _accumulator = 0.0f;
        _initialized = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        OnShutdown();
        _disposed = true;
    }

    private void SyncRegisteredBodies()
    {
        var activeScene = scenes.ActiveScene;
        var activeBodies = activeScene is null
            ? []
            : activeScene.Objects
                .Where(sceneObject => !sceneObject.IsDestroyed)
                .Select(sceneObject => sceneObject.GetComponent<RigidBody>())
                .Where(body => body is not null)
                .Cast<RigidBody>()
                .ToArray();

        var activeSet = activeBodies.ToHashSet();

        foreach (var body in _bodies.Keys.ToArray())
        {
            if (!activeSet.Contains(body) ||
                body.IsDestroyed ||
                !body.IsActive ||
                body.SceneObject?.GetComponent<Collider>() is null)
            {
                UnregisterBody(body);
            }
        }

        foreach (var body in activeBodies)
        {
            if (!body.IsActive)
                continue;

            if (body.SceneObject?.GetComponent<Collider>() is null)
                continue;

            if (!_bodies.ContainsKey(body))
                RegisterBody(body);
        }
    }

    private void RegisterBody(RigidBody component)
    {
        if (_world is null || component.SceneObject is null)
            return;

        var collider = component.SceneObject.GetComponent<Collider>();
        if (collider is null)
            return;

        var shape = PhysicsShapeFactory.Create(collider);
        if (shape is null)
        {
            Logger.Error(
                $"Unsupported collider '{collider.GetType().Name}' on '{component.SceneObject.Name}'.");
            return;
        }

        var body = _world.CreateRigidBody();
        body.Tag = component;
        body.Position = PhysicsShapeFactory.ToJVector(component.Transform.WorldPosition);
        body.Orientation = PhysicsShapeFactory.ToJQuaternion(component.Transform.WorldRotation);
        body.MotionType = MapMotionType(component.MotionType);
        body.AffectedByGravity = component.AffectedByGravity;
        body.Friction = component.Friction;
        body.Restitution = component.Restitution;
        body.EnableSpeculativeContacts = component.EnableSpeculativeContacts;
        body.AddShape(shape, MassInertiaUpdateMode.Update);

        if (body.MotionType == MotionType.Dynamic)
        {
            body.SetMassInertia(component.Mass);
        }
        else
        {
            body.SetMassInertia(JMatrix.Identity, 0.0f, setAsInverse: true);
        }

        if (body.MotionType != MotionType.Static)
        {
            body.Velocity = PhysicsShapeFactory.ToJVector(component.Velocity);
            body.AngularVelocity = PhysicsShapeFactory.ToJVector(component.AngularVelocity);
        }

        body.SetActivationState(true);

        component.NativeBody = body;
        _bodies.Add(component, body);
    }

    private void UnregisterBody(RigidBody component)
    {
        if (_world is null || !_bodies.Remove(component, out var body))
            return;

        component.NativeBody = null;
        _world.Remove(body);
    }

    private void SyncBodiesToPhysics()
    {
        foreach (var (component, body) in _bodies)
        {
            if (component.SceneObject is null || component.IsDestroyed)
                continue;

            body.MotionType = MapMotionType(component.MotionType);
            body.AffectedByGravity = component.AffectedByGravity;
            body.Friction = component.Friction;
            body.Restitution = component.Restitution;
            body.EnableSpeculativeContacts = component.EnableSpeculativeContacts;

            if (body.MotionType == MotionType.Dynamic)
                body.SetMassInertia(component.Mass);

            if (body.MotionType is MotionType.Static or MotionType.Kinematic)
            {
                body.Position = PhysicsShapeFactory.ToJVector(component.Transform.WorldPosition);
                body.Orientation = PhysicsShapeFactory.ToJQuaternion(component.Transform.WorldRotation);
            }

            if (body.MotionType != MotionType.Static)
            {
                body.Velocity = PhysicsShapeFactory.ToJVector(component.Velocity);
                body.AngularVelocity = PhysicsShapeFactory.ToJVector(component.AngularVelocity);
            }
        }
    }

    private void SyncPhysicsToBodies()
    {
        foreach (var (component, body) in _bodies)
        {
            if (component.SceneObject is null || component.IsDestroyed)
                continue;

            if (body.MotionType == MotionType.Dynamic)
            {
                component.Transform.WorldPosition =
                    PhysicsShapeFactory.ToVector3(body.Position);
                component.Transform.WorldRotation =
                    PhysicsShapeFactory.ToQuaternion(body.Orientation);
            }
        }
    }

    private static MotionType MapMotionType(EPhysicsMotionType type)
    {
        return type switch
        {
            EPhysicsMotionType.Dynamic => MotionType.Dynamic,
            EPhysicsMotionType.Kinematic => MotionType.Kinematic,
            _ => MotionType.Static
        };
    }
}
