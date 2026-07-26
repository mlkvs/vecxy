using System.Numerics;
using System.Runtime.CompilerServices;
using Autofac;
using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using Vecxy.Diagnostics;
using Vecxy.Kernel;
using Vecxy.Scene;
using Logger = Vecxy.Diagnostics.Logger;
using JitterRigidBody = Jitter2.Dynamics.RigidBody;

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

    private readonly Dictionary<SceneObject, PhysicsActor> _actorsBySceneObject = [];
    private readonly Dictionary<JitterRigidBody, PhysicsActor> _actorsByBody = [];
    private readonly HashSet<PhysicsPair> _collisionPairs = [];
    private readonly HashSet<PhysicsPair> _triggerPairs = [];
    private readonly HashSet<PhysicsPair> _triggerPairsNext = [];
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
        _world.BroadPhaseFilter = new BroadPhaseFilter(this);
        _initialized = true;
    }

    public void OnUpdate(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized || _world is null)
            return;

        _world.Gravity = PhysicsShapeFactory.ToJVector(Gravity);

        SyncRegisteredActors();
        SyncActorsToPhysics();

        const float step = 1.0f / 60.0f;
        _accumulator = Math.Min(_accumulator + deltaTime, 0.25f);

        while (_accumulator >= step)
        {
            _world.Step(step, multiThread: false);
            _accumulator -= step;
        }

        SyncPhysicsToActors();
        DispatchCollisionStay();
        DispatchTriggers();
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

        foreach (var actor in _actorsBySceneObject.Values)
        {
            if (ReferenceEquals(actor.SceneObject, ignoreSceneObject))
                continue;

            foreach (var shape in actor.Body.Shapes)
            {
                if (!shape.RayCast(jOrigin, jDir, out var normal, out var lambda))
                    continue;

                if (lambda < 0.0f || lambda > nearestDistance)
                    continue;

                var point = origin + dir * lambda;
                nearestDistance = lambda;
                hit = new PhysicsRaycastHit(
                    actor.SceneObject,
                    actor.Collider,
                    actor.RigidBody,
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

        foreach (var actor in _actorsBySceneObject.Values.ToArray())
            UnregisterActor(actor.SceneObject);

        _actorsBySceneObject.Clear();
        _actorsByBody.Clear();
        _collisionPairs.Clear();
        _triggerPairs.Clear();
        _triggerPairsNext.Clear();
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

    private void SyncRegisteredActors()
    {
        var activeScene = scenes.ActiveScene;
        var activeObjects = activeScene is null
            ? []
            : activeScene.Objects
                .Where(sceneObject =>
                    !sceneObject.IsDestroyed &&
                    sceneObject.IsActive &&
                    sceneObject.GetComponent<Collider>() is not null)
                .ToArray();

        var activeSet = activeObjects.ToHashSet();

        foreach (var sceneObject in _actorsBySceneObject.Keys.ToArray())
        {
            if (!activeSet.Contains(sceneObject))
                UnregisterActor(sceneObject);
        }

        foreach (var sceneObject in activeObjects)
        {
            if (!_actorsBySceneObject.ContainsKey(sceneObject))
                RegisterActor(sceneObject);
        }
    }

    private void RegisterActor(SceneObject sceneObject)
    {
        if (_world is null)
            return;

        var collider = sceneObject.GetComponent<Collider>();
        if (collider is null)
            return;

        var shape = PhysicsShapeFactory.Create(collider);
        if (shape is null)
        {
            Logger.Error(
                $"Unsupported collider '{collider.GetType().Name}' on '{sceneObject.Name}'.");
            return;
        }

        var rigidBody = sceneObject.GetComponent<RigidBody>();
        var body = _world.CreateRigidBody();
        var actor = new PhysicsActor(sceneObject, collider, rigidBody, body);

        body.Tag = actor;
        body.Position = PhysicsShapeFactory.ToJVector(sceneObject.Transform.WorldPosition);
        body.Orientation = PhysicsShapeFactory.ToJQuaternion(sceneObject.Transform.WorldRotation);
        body.BeginCollide += OnBeginCollide;
        body.EndCollide += OnEndCollide;
        body.AddShape(shape, MassInertiaUpdateMode.Update);

        ApplyActorProperties(actor);
        body.SetActivationState(true);

        if (rigidBody is not null)
            rigidBody.NativeBody = body;

        _actorsBySceneObject.Add(sceneObject, actor);
        _actorsByBody.Add(body, actor);
    }

    private void UnregisterActor(SceneObject sceneObject)
    {
        if (_world is null ||
            !_actorsBySceneObject.Remove(sceneObject, out var actor))
        {
            return;
        }

        actor.Body.BeginCollide -= OnBeginCollide;
        actor.Body.EndCollide -= OnEndCollide;

        if (actor.RigidBody is not null)
            actor.RigidBody.NativeBody = null;

        _actorsByBody.Remove(actor.Body);
        _world.Remove(actor.Body);

        RemovePairsFor(actor);
    }

    private void SyncActorsToPhysics()
    {
        foreach (var actor in _actorsBySceneObject.Values)
        {
            ApplyActorProperties(actor);

            if (GetMotionType(actor) is MotionType.Static or MotionType.Kinematic)
            {
                actor.Body.Position =
                    PhysicsShapeFactory.ToJVector(actor.SceneObject.Transform.WorldPosition);
                actor.Body.Orientation =
                    PhysicsShapeFactory.ToJQuaternion(actor.SceneObject.Transform.WorldRotation);
            }

            if (actor.RigidBody is not null &&
                actor.Body.MotionType != MotionType.Static)
            {
                actor.Body.Velocity =
                    PhysicsShapeFactory.ToJVector(actor.RigidBody.Velocity);
                actor.Body.AngularVelocity =
                    PhysicsShapeFactory.ToJVector(actor.RigidBody.AngularVelocity);
            }
        }
    }

    private void SyncPhysicsToActors()
    {
        foreach (var actor in _actorsBySceneObject.Values)
        {
            if (actor.RigidBody is null ||
                actor.Body.MotionType != MotionType.Dynamic)
            {
                continue;
            }

            actor.SceneObject.Transform.WorldPosition =
                PhysicsShapeFactory.ToVector3(actor.Body.Position);
            actor.SceneObject.Transform.WorldRotation =
                PhysicsShapeFactory.ToQuaternion(actor.Body.Orientation);
        }
    }

    private void ApplyActorProperties(PhysicsActor actor)
    {
        var body = actor.Body;
        body.MotionType = GetMotionType(actor);
        body.AffectedByGravity = actor.RigidBody?.AffectedByGravity ?? false;
        body.Friction = actor.RigidBody?.Friction ?? 0.5f;
        body.Restitution = actor.RigidBody?.Restitution ?? 0.0f;
        body.EnableSpeculativeContacts = actor.RigidBody?.EnableSpeculativeContacts ?? false;

        if (body.MotionType == MotionType.Dynamic)
        {
            body.SetMassInertia(actor.RigidBody?.Mass ?? 1.0f);
        }
        else
        {
            body.SetMassInertia(JMatrix.Identity, 0.0f, setAsInverse: true);
        }
    }

    private MotionType GetMotionType(PhysicsActor actor)
    {
        return actor.RigidBody?.MotionType switch
        {
            EPhysicsMotionType.Dynamic => MotionType.Dynamic,
            EPhysicsMotionType.Kinematic => MotionType.Kinematic,
            _ => MotionType.Static
        };
    }

    private bool FilterBroadPhasePair(
        IDynamicTreeProxy proxyA,
        IDynamicTreeProxy proxyB)
    {
        if (proxyA is not RigidBodyShape shapeA ||
            proxyB is not RigidBodyShape shapeB)
        {
            return false;
        }

        if (!_actorsByBody.TryGetValue(shapeA.RigidBody, out var actorA) ||
            !_actorsByBody.TryGetValue(shapeB.RigidBody, out var actorB))
        {
            return true;
        }

        if (actorA.Collider.IsTrigger || actorB.Collider.IsTrigger)
            return false;

        return true;
    }

    private void OnBeginCollide(Arbiter arbiter)
    {
        if (!TryGetActors(arbiter, out var actorA, out var actorB))
            return;

        var pair = new PhysicsPair(actorA, actorB);
        if (!_collisionPairs.Add(pair))
            return;

        DispatchCollisionEnter(actorA, actorB);
        DispatchCollisionEnter(actorB, actorA);
    }

    private void OnEndCollide(Arbiter arbiter)
    {
        if (!TryGetActors(arbiter, out var actorA, out var actorB))
            return;

        var pair = new PhysicsPair(actorA, actorB);
        if (!_collisionPairs.Remove(pair))
            return;

        DispatchCollisionExit(actorA, actorB);
        DispatchCollisionExit(actorB, actorA);
    }

    private bool TryGetActors(
        Arbiter arbiter,
        out PhysicsActor actorA,
        out PhysicsActor actorB)
    {
        actorA = null!;
        actorB = null!;

        if (!_actorsByBody.TryGetValue(arbiter.Body1, out var resolvedActorA) ||
            !_actorsByBody.TryGetValue(arbiter.Body2, out var resolvedActorB))
        {
            return false;
        }

        actorA = resolvedActorA;
        actorB = resolvedActorB;
        return true;
    }

    private void DispatchCollisionStay()
    {
        foreach (var pair in _collisionPairs)
        {
            if (pair.A.SceneObject.IsDestroyed || pair.B.SceneObject.IsDestroyed)
                continue;

            DispatchCollisionStay(pair.A, pair.B);
            DispatchCollisionStay(pair.B, pair.A);
        }
    }

    private void DispatchTriggers()
    {
        _triggerPairsNext.Clear();

        var actors = _actorsBySceneObject.Values.ToArray();
        for (var indexA = 0; indexA < actors.Length; indexA++)
        {
            for (var indexB = indexA + 1; indexB < actors.Length; indexB++)
            {
                var actorA = actors[indexA];
                var actorB = actors[indexB];

                if (!actorA.Collider.IsTrigger &&
                    !actorB.Collider.IsTrigger)
                {
                    continue;
                }

                if (!ShapesOverlap(actorA, actorB))
                    continue;

                var pair = new PhysicsPair(actorA, actorB);
                _triggerPairsNext.Add(pair);

                if (_triggerPairs.Contains(pair))
                    continue;

                DispatchTriggerEnter(actorA, actorB);
                DispatchTriggerEnter(actorB, actorA);
            }
        }

        foreach (var pair in _triggerPairs)
        {
            if (_triggerPairsNext.Contains(pair))
            {
                if (!pair.A.SceneObject.IsDestroyed &&
                    !pair.B.SceneObject.IsDestroyed)
                {
                    DispatchTriggerStay(pair.A, pair.B);
                    DispatchTriggerStay(pair.B, pair.A);
                }

                continue;
            }

            DispatchTriggerExit(pair.A, pair.B);
            DispatchTriggerExit(pair.B, pair.A);
        }

        _triggerPairs.Clear();
        foreach (var pair in _triggerPairsNext)
            _triggerPairs.Add(pair);
    }

    private static bool ShapesOverlap(
        PhysicsActor actorA,
        PhysicsActor actorB)
    {
        foreach (var shapeA in actorA.Body.Shapes)
        {
            foreach (var shapeB in actorB.Body.Shapes)
            {
                if (Intersects(shapeA.WorldBoundingBox, shapeB.WorldBoundingBox))
                    return true;
            }
        }

        return false;
    }

    private static bool Intersects(
        JBoundingBox a,
        JBoundingBox b)
    {
        return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
               a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
               a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
    }

    private void RemovePairsFor(PhysicsActor actor)
    {
        foreach (var pair in _collisionPairs.Where(pair => pair.Contains(actor)).ToArray())
        {
            _collisionPairs.Remove(pair);

            var other = pair.Other(actor);
            DispatchCollisionExit(actor, other);
            DispatchCollisionExit(other, actor);
        }

        foreach (var pair in _triggerPairs.Where(pair => pair.Contains(actor)).ToArray())
        {
            _triggerPairs.Remove(pair);

            var other = pair.Other(actor);
            DispatchTriggerExit(actor, other);
            DispatchTriggerExit(other, actor);
        }
    }

    private void DispatchCollisionEnter(
        PhysicsActor self,
        PhysicsActor other)
    {
        DispatchPhysicsComponents(
            self.SceneObject,
            self.Collider,
            other.Collider,
            static (component, selfCollider, otherCollider) =>
                component.DispatchCollisionEnter(selfCollider, otherCollider));
    }

    private void DispatchCollisionStay(
        PhysicsActor self,
        PhysicsActor other)
    {
        DispatchPhysicsComponents(
            self.SceneObject,
            self.Collider,
            other.Collider,
            static (component, selfCollider, otherCollider) =>
                component.DispatchCollisionStay(selfCollider, otherCollider));
    }

    private void DispatchCollisionExit(
        PhysicsActor self,
        PhysicsActor other)
    {
        DispatchPhysicsComponents(
            self.SceneObject,
            self.Collider,
            other.Collider,
            static (component, selfCollider, otherCollider) =>
                component.DispatchCollisionExit(selfCollider, otherCollider));
    }

    private void DispatchTriggerEnter(
        PhysicsActor self,
        PhysicsActor other)
    {
        DispatchPhysicsComponents(
            self.SceneObject,
            self.Collider,
            other.Collider,
            static (component, selfCollider, otherCollider) =>
                component.DispatchTriggerEnter(selfCollider, otherCollider));
    }

    private void DispatchTriggerStay(
        PhysicsActor self,
        PhysicsActor other)
    {
        DispatchPhysicsComponents(
            self.SceneObject,
            self.Collider,
            other.Collider,
            static (component, selfCollider, otherCollider) =>
                component.DispatchTriggerStay(selfCollider, otherCollider));
    }

    private void DispatchTriggerExit(
        PhysicsActor self,
        PhysicsActor other)
    {
        DispatchPhysicsComponents(
            self.SceneObject,
            self.Collider,
            other.Collider,
            static (component, selfCollider, otherCollider) =>
                component.DispatchTriggerExit(selfCollider, otherCollider));
    }

    private static void DispatchPhysicsComponents(
        SceneObject sceneObject,
        Collider self,
        Collider other,
        Action<AComponent, Collider, Collider> dispatch)
    {
        foreach (var component in sceneObject.Components)
        {
            if (!component.IsActive || component.IsDestroyed)
            {
                continue;
            }

            try
            {
                dispatch(component, self, other);
            }
            catch (Exception exception)
            {
                Logger.Error(
                    exception,
                    $"Physics callback failed on '{component.GetType().Name}'.");
            }
        }
    }

    private sealed class PhysicsActor(
        SceneObject sceneObject,
        Collider collider,
        RigidBody? rigidBody,
        JitterRigidBody body)
    {
        public SceneObject SceneObject { get; } = sceneObject;
        public Collider Collider { get; } = collider;
        public RigidBody? RigidBody { get; } = rigidBody;
        public JitterRigidBody Body { get; } = body;
    }

    private sealed class BroadPhaseFilter(
        PhysicsModule module) : IBroadPhaseFilter
    {
        public bool Filter(
            IDynamicTreeProxy proxyA,
            IDynamicTreeProxy proxyB)
        {
            return module.FilterBroadPhasePair(proxyA, proxyB);
        }
    }

    private readonly record struct PhysicsPair
    {
        public PhysicsActor A { get; }
        public PhysicsActor B { get; }

        public PhysicsPair(
            PhysicsActor first,
            PhysicsActor second)
        {
            if (RuntimeHelpers.GetHashCode(first) <= RuntimeHelpers.GetHashCode(second))
            {
                A = first;
                B = second;
            }
            else
            {
                A = second;
                B = first;
            }
        }

        public bool Contains(PhysicsActor actor) =>
            ReferenceEquals(A, actor) || ReferenceEquals(B, actor);

        public PhysicsActor Other(PhysicsActor actor) =>
            ReferenceEquals(A, actor) ? B : A;
    }
}
