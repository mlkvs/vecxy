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

public sealed class PhysicsSceneSystem : ISceneSystem
{
    private readonly Dictionary<Scene.Scene, SceneState> _states = [];
    private Vector3 _gravity = new(0.0f, -9.81f, 0.0f);

    public Vector3 Gravity
    {
        get => _gravity;
        set
        {
            _gravity = value;

            foreach (var state in _states.Values)
                state.World.Gravity = PhysicsShapeFactory.ToJVector(value);
        }
    }

    public void OnObjectAdded(SceneObject sceneObject)
    {
        if (sceneObject.GetComponent<Collider>() is null)
            return;

        GetState(sceneObject.Scene).Candidates.Add(sceneObject);
    }

    public void OnObjectRemoved(SceneObject sceneObject)
    {
        if (!TryGetState(sceneObject.Scene, out var state))
            return;

        state.Candidates.Remove(sceneObject);
        UnregisterActor(state, sceneObject);
        CleanupSceneState(state);
    }

    public void OnComponentAdded(SceneObject sceneObject, AComponent component)
    {
        if (component is Collider)
        {
            var stateTemp = GetState(sceneObject.Scene);
            stateTemp.Candidates.Add(sceneObject);
            TryRegisterActor(stateTemp, sceneObject);
            return;
        }

        if (component is RigidBody &&
            TryGetState(sceneObject.Scene, out var state) &&
            state.ActorsBySceneObject.TryGetValue(sceneObject, out var actor))
        {
            AttachRigidBody(actor, sceneObject.GetComponent<RigidBody>());
            ApplyActorProperties(actor);
        }
    }

    public void OnComponentRemoved(SceneObject sceneObject, AComponent component)
    {
        if (!TryGetState(sceneObject.Scene, out var state))
            return;

        if (component is Collider)
        {
            state.Candidates.Remove(sceneObject);
            UnregisterActor(state, sceneObject);
            CleanupSceneState(state);
            return;
        }

        if (component is RigidBody &&
            state.ActorsBySceneObject.TryGetValue(sceneObject, out var actor))
        {
            AttachRigidBody(actor, sceneObject.GetComponent<RigidBody>());
            ApplyActorProperties(actor);
        }
    }

    public void OnComponentChanged(SceneObject sceneObject, AComponent component)
    {
        if (!TryGetState(sceneObject.Scene, out var state))
            return;

        switch (component)
        {
            case Collider:
                RebuildActor(state, sceneObject);
                break;
            case RigidBody:
                if (state.ActorsBySceneObject.TryGetValue(sceneObject, out var actor))
                {
                    AttachRigidBody(actor, sceneObject.GetComponent<RigidBody>());
                    ApplyActorProperties(actor);
                }
                break;
        }
    }

    public void Update(float deltaTime)
    {
        foreach (var state in _states.Values)
        {
            if (!state.Scene.IsActive)
                continue;

            SyncRegisteredActors(state);
            SyncActorsToPhysics(state);

            const float step = 1.0f / 60.0f;
            state.Accumulator = Math.Min(state.Accumulator + deltaTime, 0.25f);

            while (state.Accumulator >= step)
            {
                state.World.Step(step, multiThread: false);
                state.Accumulator -= step;
            }

            SyncPhysicsToActors(state);
            DispatchCollisionStay(state);
            DispatchTriggers(state);
        }
    }

    public bool Raycast(
        Scene.Scene scene,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        SceneObject? ignoreSceneObject,
        out PhysicsRaycastHit hit)
    {
        hit = default;

        if (!TryGetState(scene, out var state) || maxDistance <= 0.0f)
            return false;

        var directionLengthSquared = direction.LengthSquared();
        if (directionLengthSquared <= float.Epsilon)
            return false;

        var dir = Vector3.Normalize(direction);
        var jOrigin = PhysicsShapeFactory.ToJVector(origin);
        var jDir = PhysicsShapeFactory.ToJVector(dir);

        var found = false;
        var nearestDistance = maxDistance;

        foreach (var actor in state.ActorsBySceneObject.Values)
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

    public void Shutdown()
    {
        foreach (var state in _states.Values)
        {
            foreach (var actor in state.ActorsBySceneObject.Values.ToArray())
                UnregisterActor(state, actor.SceneObject);

            state.Candidates.Clear();
            state.ActorsBySceneObject.Clear();
            state.ActorsByBody.Clear();
            state.CollisionPairs.Clear();
            state.TriggerPairs.Clear();
            state.TriggerPairsNext.Clear();
            state.World.Dispose();
        }

        _states.Clear();
    }

    private SceneState GetState(Scene.Scene scene)
    {
        if (_states.TryGetValue(scene, out var state))
            return state;

        state = new SceneState(scene, this, Gravity);
        _states.Add(scene, state);
        return state;
    }

    private bool TryGetState(Scene.Scene scene, out SceneState state)
    {
        return _states.TryGetValue(scene, out state!);
    }

    private void CleanupSceneState(SceneState state)
    {
        if (state.Scene.IsActive ||
            state.Candidates.Count > 0 ||
            state.ActorsBySceneObject.Count > 0)
        {
            return;
        }

        _states.Remove(state.Scene);
        state.World.Dispose();
    }

    private void SyncRegisteredActors(SceneState state)
    {
        foreach (var sceneObject in state.ActorsBySceneObject.Keys.ToArray())
        {
            if (!ShouldHaveActor(sceneObject))
                UnregisterActor(state, sceneObject);
        }

        foreach (var sceneObject in state.Candidates.ToArray())
        {
            if (sceneObject.IsDestroyed)
            {
                state.Candidates.Remove(sceneObject);
                continue;
            }

            if (!state.ActorsBySceneObject.ContainsKey(sceneObject))
                TryRegisterActor(state, sceneObject);
        }
    }

    private void TryRegisterActor(SceneState state, SceneObject sceneObject)
    {
        if (!ShouldHaveActor(sceneObject) ||
            state.ActorsBySceneObject.ContainsKey(sceneObject))
        {
            return;
        }

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
        var body = state.World.CreateRigidBody();
        var actor = new PhysicsActor(sceneObject, collider, rigidBody, body);

        body.Tag = actor;
        body.Position = PhysicsShapeFactory.ToJVector(sceneObject.Transform.WorldPosition);
        body.Orientation = PhysicsShapeFactory.ToJQuaternion(sceneObject.Transform.WorldRotation);
        body.BeginCollide += state.OnBeginCollide;
        body.EndCollide += state.OnEndCollide;
        body.AddShape(shape, MassInertiaUpdateMode.Update);

        ApplyActorProperties(actor);
        body.SetActivationState(true);

        if (rigidBody is not null)
            rigidBody.NativeBody = body;

        state.ActorsBySceneObject.Add(sceneObject, actor);
        state.ActorsByBody.Add(body, actor);
    }

    private void RebuildActor(SceneState state, SceneObject sceneObject)
    {
        var hadActor = state.ActorsBySceneObject.ContainsKey(sceneObject);
        if (hadActor)
            UnregisterActor(state, sceneObject);

        if (sceneObject.GetComponent<Collider>() is null)
        {
            state.Candidates.Remove(sceneObject);
            CleanupSceneState(state);
            return;
        }

        state.Candidates.Add(sceneObject);
        TryRegisterActor(state, sceneObject);
    }

    private void UnregisterActor(SceneState state, SceneObject sceneObject)
    {
        if (!state.ActorsBySceneObject.Remove(sceneObject, out var actor))
            return;

        actor.Body.BeginCollide -= state.OnBeginCollide;
        actor.Body.EndCollide -= state.OnEndCollide;

        if (actor.RigidBody is not null)
            actor.RigidBody.NativeBody = null;

        state.ActorsByBody.Remove(actor.Body);
        state.World.Remove(actor.Body);

        RemovePairsFor(state, actor);
    }

    private static bool ShouldHaveActor(SceneObject sceneObject)
    {
        return !sceneObject.IsDestroyed &&
               sceneObject.IsActive &&
               sceneObject.GetComponent<Collider>() is not null;
    }

    private static void AttachRigidBody(PhysicsActor actor, RigidBody? rigidBody)
    {
        if (ReferenceEquals(actor.RigidBody, rigidBody))
            return;

        if (actor.RigidBody is not null)
            actor.RigidBody.NativeBody = null;

        actor.RigidBody = rigidBody;

        if (rigidBody is not null)
            rigidBody.NativeBody = actor.Body;
    }

    private static void SyncActorsToPhysics(SceneState state)
    {
        foreach (var actor in state.ActorsBySceneObject.Values)
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

    private static void SyncPhysicsToActors(SceneState state)
    {
        foreach (var actor in state.ActorsBySceneObject.Values)
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

    private static void ApplyActorProperties(PhysicsActor actor)
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

    private static MotionType GetMotionType(PhysicsActor actor)
    {
        return actor.RigidBody?.MotionType switch
        {
            EPhysicsMotionType.Dynamic => MotionType.Dynamic,
            EPhysicsMotionType.Kinematic => MotionType.Kinematic,
            _ => MotionType.Static
        };
    }

    private bool FilterBroadPhasePair(
        SceneState state,
        IDynamicTreeProxy proxyA,
        IDynamicTreeProxy proxyB)
    {
        if (proxyA is not RigidBodyShape shapeA ||
            proxyB is not RigidBodyShape shapeB)
        {
            return false;
        }

        if (!state.ActorsByBody.TryGetValue(shapeA.RigidBody, out var actorA) ||
            !state.ActorsByBody.TryGetValue(shapeB.RigidBody, out var actorB))
        {
            return true;
        }

        if (actorA.Collider.IsTrigger || actorB.Collider.IsTrigger)
            return false;

        return true;
    }

    private static void OnBeginCollide(SceneState state, Arbiter arbiter)
    {
        if (!TryGetActors(state, arbiter, out var actorA, out var actorB))
            return;

        var pair = new PhysicsPair(actorA, actorB);
        if (!state.CollisionPairs.Add(pair))
            return;

        DispatchCollisionEnter(actorA, actorB);
        DispatchCollisionEnter(actorB, actorA);
    }

    private static void OnEndCollide(SceneState state, Arbiter arbiter)
    {
        if (!TryGetActors(state, arbiter, out var actorA, out var actorB))
            return;

        var pair = new PhysicsPair(actorA, actorB);
        if (!state.CollisionPairs.Remove(pair))
            return;

        DispatchCollisionExit(actorA, actorB);
        DispatchCollisionExit(actorB, actorA);
    }

    private static bool TryGetActors(
        SceneState state,
        Arbiter arbiter,
        out PhysicsActor actorA,
        out PhysicsActor actorB)
    {
        actorA = null!;
        actorB = null!;

        if (!state.ActorsByBody.TryGetValue(arbiter.Body1, out var resolvedActorA) ||
            !state.ActorsByBody.TryGetValue(arbiter.Body2, out var resolvedActorB))
        {
            return false;
        }

        actorA = resolvedActorA;
        actorB = resolvedActorB;
        return true;
    }

    private static void DispatchCollisionStay(SceneState state)
    {
        foreach (var pair in state.CollisionPairs)
        {
            if (pair.A.SceneObject.IsDestroyed || pair.B.SceneObject.IsDestroyed)
                continue;

            DispatchCollisionStay(pair.A, pair.B);
            DispatchCollisionStay(pair.B, pair.A);
        }
    }

    private static void DispatchTriggers(SceneState state)
    {
        state.TriggerPairsNext.Clear();

        var actors = state.ActorsBySceneObject.Values.ToArray();
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
                state.TriggerPairsNext.Add(pair);

                if (state.TriggerPairs.Contains(pair))
                    continue;

                DispatchTriggerEnter(actorA, actorB);
                DispatchTriggerEnter(actorB, actorA);
            }
        }

        foreach (var pair in state.TriggerPairs)
        {
            if (state.TriggerPairsNext.Contains(pair))
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

        state.TriggerPairs.Clear();
        foreach (var pair in state.TriggerPairsNext)
            state.TriggerPairs.Add(pair);
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

    private static void RemovePairsFor(SceneState state, PhysicsActor actor)
    {
        foreach (var pair in state.CollisionPairs.Where(pair => pair.Contains(actor)).ToArray())
        {
            state.CollisionPairs.Remove(pair);

            var other = pair.Other(actor);
            DispatchCollisionExit(actor, other);
            DispatchCollisionExit(other, actor);
        }

        foreach (var pair in state.TriggerPairs.Where(pair => pair.Contains(actor)).ToArray())
        {
            state.TriggerPairs.Remove(pair);

            var other = pair.Other(actor);
            DispatchTriggerExit(actor, other);
            DispatchTriggerExit(other, actor);
        }
    }

    private static void DispatchCollisionEnter(
        PhysicsActor self,
        PhysicsActor other)
    {
        DispatchPhysicsComponents(
            self.SceneObject,
            self.Collider,
            other.Collider,
            static (component, selfCollider, otherCollider) =>
                component.OnCollisionEnter(selfCollider, otherCollider));
    }

    private static void DispatchCollisionStay(
        PhysicsActor self,
        PhysicsActor other)
    {
        DispatchPhysicsComponents(
            self.SceneObject,
            self.Collider,
            other.Collider,
            static (component, selfCollider, otherCollider) =>
                component.OnCollisionStay(selfCollider, otherCollider));
    }

    private static void DispatchCollisionExit(
        PhysicsActor self,
        PhysicsActor other)
    {
        DispatchPhysicsComponents(
            self.SceneObject,
            self.Collider,
            other.Collider,
            static (component, selfCollider, otherCollider) =>
                component.OnCollisionExit(selfCollider, otherCollider));
    }

    private static void DispatchTriggerEnter(
        PhysicsActor self,
        PhysicsActor other)
    {
        DispatchPhysicsComponents(
            self.SceneObject,
            self.Collider,
            other.Collider,
            static (component, selfCollider, otherCollider) =>
                component.OnTriggerEnter(selfCollider, otherCollider));
    }

    private static void DispatchTriggerStay(
        PhysicsActor self,
        PhysicsActor other)
    {
        DispatchPhysicsComponents(
            self.SceneObject,
            self.Collider,
            other.Collider,
            static (component, selfCollider, otherCollider) =>
                component.OnTriggerStay(selfCollider, otherCollider));
    }

    private static void DispatchTriggerExit(
        PhysicsActor self,
        PhysicsActor other)
    {
        DispatchPhysicsComponents(
            self.SceneObject,
            self.Collider,
            other.Collider,
            static (component, selfCollider, otherCollider) =>
                component.OnTriggerExit(selfCollider, otherCollider));
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
                continue;

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
        public RigidBody? RigidBody { get; set; } = rigidBody;
        public JitterRigidBody Body { get; } = body;
    }

    private sealed class BroadPhaseFilter(
        SceneState state,
        PhysicsSceneSystem system) : IBroadPhaseFilter
    {
        public bool Filter(
            IDynamicTreeProxy proxyA,
            IDynamicTreeProxy proxyB)
        {
            return system.FilterBroadPhasePair(state, proxyA, proxyB);
        }
    }

    private sealed class SceneState
    {
        public SceneState(
            Scene.Scene scene,
            PhysicsSceneSystem system,
            Vector3 gravity)
        {
            Scene = scene;
            World = new World
            {
                Gravity = PhysicsShapeFactory.ToJVector(gravity),
                AllowDeactivation = true
            };
            World.SolverIterations = (solver: 6, relaxation: 2);
            World.BroadPhaseFilter = new BroadPhaseFilter(this, system);
        }

        public Scene.Scene Scene { get; }
        public HashSet<SceneObject> Candidates { get; } = [];
        public Dictionary<SceneObject, PhysicsActor> ActorsBySceneObject { get; } = [];
        public Dictionary<JitterRigidBody, PhysicsActor> ActorsByBody { get; } = [];
        public HashSet<PhysicsPair> CollisionPairs { get; } = [];
        public HashSet<PhysicsPair> TriggerPairs { get; } = [];
        public HashSet<PhysicsPair> TriggerPairsNext { get; } = [];
        public World World { get; }
        public float Accumulator { get; set; }

        public void OnBeginCollide(Arbiter arbiter) =>
            PhysicsSceneSystem.OnBeginCollide(this, arbiter);

        public void OnEndCollide(Arbiter arbiter) =>
            PhysicsSceneSystem.OnEndCollide(this, arbiter);
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

public sealed class PhysicsModule(
    ISceneManager scenes,
    PhysicsSceneSystem physicsSceneSystem) :
    IModule,
    IPhysicsSystem
{
    public sealed class Definition : AModuleDefinition<PhysicsModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(IPhysicsSystem)];

        public override void RegisterGlobal(ContainerBuilder builder)
        {
            builder
                .RegisterType<PhysicsSceneSystem>()
                .AsSelf()
                .As<ISceneSystem>()
                .SingleInstance();
        }

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder
                .RegisterType<PhysicsModule>()
                .AsSelf()
                .SingleInstance();
        }
    }

    private bool _disposed;

    public Vector3 Gravity
    {
        get => physicsSceneSystem.Gravity;
        set => physicsSceneSystem.Gravity = value;
    }

    public void OnInitialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public bool Raycast(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        SceneObject? ignoreSceneObject,
        out PhysicsRaycastHit hit)
    {
        hit = default;

        var scene = ignoreSceneObject?.Scene ?? scenes.ActiveScene;
        if (scene is null ||
            !scene.TryGetSystem<PhysicsSceneSystem>(out var system))
        {
            return false;
        }

        return system.Raycast(
            scene,
            origin,
            direction,
            maxDistance,
            ignoreSceneObject,
            out hit);
    }

    public void OnShutdown()
    {
        physicsSceneSystem.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        OnShutdown();
        _disposed = true;
    }
}
