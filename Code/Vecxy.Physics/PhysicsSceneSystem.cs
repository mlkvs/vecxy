using System.Numerics;
using Jitter2;
using Jitter2.Collision;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using Vecxy.Scene;
using Logger = Vecxy.Diagnostics.Logger;

namespace Vecxy.Physics;

public sealed class PhysicsSceneSystem : ASceneSystem
{
    private readonly Dictionary<Scene.SceneInstance, SceneRuntime> _runtimes = [];
    private PhysicsSettings _settings = PhysicsSettings.Default;
    private PhysicsSettings? _pendingSettings;

    public PhysicsSettings Settings => _settings;

    public void SetInitialSettings(PhysicsSettings settings)
    {
        if (_runtimes.Count != 0)
            throw new InvalidOperationException(
                "Initial physics settings must be applied before scenes are attached.");

        _settings = settings;
    }

    public void QueueSettings(PhysicsSettings settings)
    {
        if (_runtimes.Count == 0)
        {
            _settings = settings;
            _pendingSettings = null;
            return;
        }

        _pendingSettings = settings;
    }

    public override void OnSceneAttached(Scene.SceneInstance sceneInstance)
    {
        if (_runtimes.ContainsKey(sceneInstance))
            return;

        _runtimes.Add(sceneInstance, new SceneRuntime(sceneInstance, _settings));
    }

    public override void OnSceneDetached(Scene.SceneInstance sceneInstance)
    {
        if (!_runtimes.Remove(sceneInstance, out var runtime))
            return;

        runtime.Dispose();
    }

    public override void OnObjectAdded(SceneObject sceneObject)
    {
        var runtime = GetRuntime(sceneObject.SceneInstance);
        EnqueueOrApply(
            runtime,
            new StructuralChange(
                EStructuralChange.AddObject,
                sceneObject,
                null));
    }

    public override void OnObjectRemoved(SceneObject sceneObject)
    {
        if (!TryGetRuntime(sceneObject.SceneInstance, out var runtime))
            return;

        EnqueueOrApply(
            runtime,
            new StructuralChange(
                EStructuralChange.RemoveObject,
                sceneObject,
                null));
    }

    public override void OnComponentAdded(
        SceneObject sceneObject,
        AComponent component)
    {
        if (component is not Collider and not RigidBody)
            return;

        var runtime = GetRuntime(sceneObject.SceneInstance);
        EnqueueOrApply(
            runtime,
            new StructuralChange(
                EStructuralChange.AddComponent,
                sceneObject,
                component));
    }

    public override void OnComponentRemoved(
        SceneObject sceneObject,
        AComponent component)
    {
        if (component is not Collider and not RigidBody)
            return;

        if (!TryGetRuntime(sceneObject.SceneInstance, out var runtime))
            return;

        EnqueueOrApply(
            runtime,
            new StructuralChange(
                EStructuralChange.RemoveComponent,
                sceneObject,
                component));
    }

    public override void Update(Scene.SceneInstance sceneInstance, float deltaTime)
    {
        if (!TryGetRuntime(sceneInstance, out var runtime))
            return;

        ApplyPendingSettings();

        runtime.IsUpdating = true;
        try
        {
            FlushStructuralChanges(runtime);
            SynchronizeBodies(runtime);

            var acceptedDelta = Math.Min(
                Math.Max(deltaTime, 0.0f),
                _settings.MaxFrameDelta);

            runtime.Accumulator += acceptedDelta;
            var steps = 0;

            while (runtime.Accumulator >= _settings.FixedDeltaTime &&
                   steps < _settings.MaxSubSteps)
            {
                sceneInstance.ProcessFixedUpdate(_settings.FixedDeltaTime);
                FlushStructuralChanges(runtime);
                SynchronizeBodies(runtime);
                PushTransformsToPhysics(runtime);
                ApplyCommands(runtime);
                BeginContactDetection(runtime);
                CapturePreviousDynamicPoses(runtime);

                runtime.World.Step(
                    _settings.FixedDeltaTime,
                    multiThread: false);

                CaptureCurrentDynamicPoses(runtime);
                CompleteContactDetection(runtime);

                runtime.Accumulator -= _settings.FixedDeltaTime;
                steps++;
            }

            if (steps == _settings.MaxSubSteps &&
                runtime.Accumulator >= _settings.FixedDeltaTime)
            {
                runtime.Accumulator %= _settings.FixedDeltaTime;
            }

            ApplyDynamicTransforms(runtime);
            DispatchPendingContacts(runtime);
        }
        finally
        {
            runtime.IsUpdating = false;
            FlushStructuralChanges(runtime);
        }
    }

    public void EnqueueForce(RigidBody rigidBody, Vector3 force)
    {
        EnqueueCommand(
            rigidBody,
            new PhysicsCommand(
                EPhysicsCommand.AddForce,
                rigidBody,
                force,
                Quaternion.Identity));
    }

    public void EnqueueImpulse(RigidBody rigidBody, Vector3 impulse)
    {
        EnqueueCommand(
            rigidBody,
            new PhysicsCommand(
                EPhysicsCommand.AddImpulse,
                rigidBody,
                impulse,
                Quaternion.Identity));
    }

    public void EnqueueTeleport(
        RigidBody rigidBody,
        Vector3 position,
        Quaternion rotation)
    {
        EnqueueCommand(
            rigidBody,
            new PhysicsCommand(
                EPhysicsCommand.Teleport,
                rigidBody,
                position,
                rotation));
    }

    public bool Raycast(
        Scene.SceneInstance sceneInstance,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        SceneObject? ignoreSceneObject,
        out PhysicsRaycastHit hit)
    {
        hit = default;

        if (!TryGetRuntime(sceneInstance, out var runtime) ||
            maxDistance <= 0.0f ||
            direction.LengthSquared() <= float.Epsilon)
        {
            return false;
        }

        var normalizedDirection = Vector3.Normalize(direction);
        var jOrigin = PhysicsShapeFactory.ToJVector(origin);
        var jDirection = PhysicsShapeFactory.ToJVector(normalizedDirection);
        var found = false;
        var nearestDistance = maxDistance;

        foreach (var bodyBinding in runtime.BodiesByObject.Values)
        {
            if (ReferenceEquals(
                    bodyBinding.SceneObject,
                    ignoreSceneObject))
            {
                continue;
            }

            foreach (var colliderBinding in bodyBinding.Colliders.Values)
            {
                var shape = colliderBinding.NativeShape;
                if (shape is null ||
                    !shape.RayCast(
                        jOrigin,
                        jDirection,
                        out var normal,
                        out var distance) ||
                    distance < 0.0f ||
                    distance > nearestDistance)
                {
                    continue;
                }

                nearestDistance = distance;
                hit = new PhysicsRaycastHit(
                    bodyBinding.SceneObject,
                    colliderBinding.Collider,
                    bodyBinding.RigidBody,
                    origin + normalizedDirection * distance,
                    PhysicsShapeFactory.ToVector3(normal),
                    distance);
                found = true;
            }
        }

        return found;
    }

    public void Shutdown()
    {
        foreach (var runtime in _runtimes.Values)
            runtime.Dispose();

        _runtimes.Clear();
    }

    private void EnqueueCommand(
        RigidBody rigidBody,
        PhysicsCommand command)
    {
        ArgumentNullException.ThrowIfNull(rigidBody);

        if (rigidBody.SceneObject is not { } sceneObject ||
            !TryGetRuntime(sceneObject.SceneInstance, out var runtime))
        {
            return;
        }

        runtime.Commands.Enqueue(command);
    }

    private void ApplyPendingSettings()
    {
        if (_pendingSettings is not { } next)
            return;

        _pendingSettings = null;

        if (_runtimes.Count != 0 &&
            (next.SolverIterations != _settings.SolverIterations ||
             next.SolverRelaxationIterations !=
             _settings.SolverRelaxationIterations))
        {
            Logger.Warning(
                "Physics solver settings changed, but these settings are " +
                "restart-required. Keeping the active solver configuration.");

            next = next with
            {
                SolverIterations = _settings.SolverIterations,
                SolverRelaxationIterations =
                _settings.SolverRelaxationIterations
            };
        }

        var fixedStepChanged =
            next.FixedDeltaTime != _settings.FixedDeltaTime;

        _settings = next;

        foreach (var runtime in _runtimes.Values)
        {
            runtime.World.Gravity =
                PhysicsShapeFactory.ToJVector(next.Gravity);
            runtime.World.AllowDeactivation = next.AllowSleeping;
            runtime.CollisionLayers = next.CollisionLayers;

            if (fixedStepChanged)
                runtime.Accumulator = 0.0f;
        }
    }

    private static void EnqueueOrApply(
        SceneRuntime runtime,
        StructuralChange change)
    {
        if (runtime.IsUpdating)
        {
            runtime.StructuralChanges.Enqueue(change);
            return;
        }

        ApplyStructuralChange(runtime, change);
    }

    private static void FlushStructuralChanges(SceneRuntime runtime)
    {
        while (runtime.StructuralChanges.TryDequeue(out var change))
            ApplyStructuralChange(runtime, change);
    }

    private static void ApplyStructuralChange(
        SceneRuntime runtime,
        StructuralChange change)
    {
        switch (change.Type)
        {
            case EStructuralChange.AddObject:
                RegisterExistingComponents(runtime, change.SceneObject);
                break;

            case EStructuralChange.RemoveObject:
                RemoveBodyBinding(runtime, change.SceneObject);
                break;

            case EStructuralChange.AddComponent:
                switch (change.Component)
                {
                    case Collider collider:
                        AddColliderBinding(
                            runtime,
                            change.SceneObject,
                            collider);
                        break;
                    case RigidBody rigidBody:
                        AttachRigidBody(
                            runtime,
                            change.SceneObject,
                            rigidBody);
                        break;
                }
                break;

            case EStructuralChange.RemoveComponent:
                switch (change.Component)
                {
                    case Collider collider:
                        RemoveColliderBinding(runtime, collider);
                        break;
                    case RigidBody rigidBody:
                        DetachRigidBody(
                            runtime,
                            change.SceneObject,
                            rigidBody);
                        break;
                }
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void RegisterExistingComponents(
        SceneRuntime runtime,
        SceneObject sceneObject)
    {
        foreach (var collider in sceneObject.GetComponents<Collider>())
            AddColliderBinding(runtime, sceneObject, collider);

        RigidBody? rigidBody = null;
        foreach (var candidate in sceneObject.GetComponents<RigidBody>())
        {
            if (rigidBody is not null)
            {
                throw new InvalidOperationException(
                    $"Scene object '{sceneObject.Name}' contains multiple rigid bodies.");
            }

            rigidBody = candidate;
        }

        if (rigidBody is not null)
            AttachRigidBody(runtime, sceneObject, rigidBody);
    }

    private static BodyBinding GetOrCreateBodyBinding(
        SceneRuntime runtime,
        SceneObject sceneObject)
    {
        if (runtime.BodiesByObject.TryGetValue(
                sceneObject,
                out var binding))
        {
            return binding;
        }

        binding = new BodyBinding(sceneObject);
        runtime.BodiesByObject.Add(sceneObject, binding);
        return binding;
    }

    private static void AddColliderBinding(
        SceneRuntime runtime,
        SceneObject sceneObject,
        Collider collider)
    {
        if (runtime.ShapesByCollider.ContainsKey(collider))
            return;

        var bodyBinding = GetOrCreateBodyBinding(runtime, sceneObject);
        var colliderBinding = new ColliderBinding(
            runtime.NextColliderId++,
            bodyBinding,
            collider);

        bodyBinding.Colliders.Add(collider, colliderBinding);
        runtime.ShapesByCollider.Add(collider, colliderBinding);
    }

    private static void AttachRigidBody(
        SceneRuntime runtime,
        SceneObject sceneObject,
        RigidBody rigidBody)
    {
        var bodyBinding = GetOrCreateBodyBinding(runtime, sceneObject);

        if (bodyBinding.RigidBody is not null &&
            !ReferenceEquals(bodyBinding.RigidBody, rigidBody))
        {
            throw new InvalidOperationException(
                $"Scene object '{sceneObject.Name}' cannot contain multiple rigid bodies.");
        }

        bodyBinding.RigidBody = rigidBody;
    }

    private static void DetachRigidBody(
        SceneRuntime runtime,
        SceneObject sceneObject,
        RigidBody rigidBody)
    {
        if (!runtime.BodiesByObject.TryGetValue(
                sceneObject,
                out var bodyBinding) ||
            !ReferenceEquals(bodyBinding.RigidBody, rigidBody))
        {
            return;
        }

        bodyBinding.RigidBody = null;

        if (bodyBinding.Colliders.Count == 0)
            RemoveBodyBinding(runtime, sceneObject);
    }

    private static void RemoveColliderBinding(
        SceneRuntime runtime,
        Collider collider)
    {
        if (!runtime.ShapesByCollider.Remove(
                collider,
                out var colliderBinding))
        {
            return;
        }

        var bodyBinding = colliderBinding.Body;
        RemovePairsFor(runtime, colliderBinding);
        RemoveNativeShape(runtime, bodyBinding, colliderBinding);
        bodyBinding.Colliders.Remove(collider);

        if (bodyBinding.Colliders.Count == 0)
            RemoveBodyBinding(runtime, bodyBinding.SceneObject);
    }

    private static void RemoveBodyBinding(
        SceneRuntime runtime,
        SceneObject sceneObject)
    {
        if (!runtime.BodiesByObject.Remove(
                sceneObject,
                out var bodyBinding))
        {
            return;
        }

        foreach (var colliderBinding in bodyBinding.Colliders.Values)
        {
            runtime.ShapesByCollider.Remove(colliderBinding.Collider);
            RemovePairsFor(runtime, colliderBinding);
            RemoveNativeShape(runtime, bodyBinding, colliderBinding);
        }

        bodyBinding.Colliders.Clear();
        DestroyNativeBody(runtime, bodyBinding);
    }

    private static void SynchronizeBodies(SceneRuntime runtime)
    {
        foreach (var bodyBinding in runtime.BodiesByObject.Values)
            SynchronizeBody(runtime, bodyBinding);
    }

    private static void SynchronizeBody(
        SceneRuntime runtime,
        BodyBinding binding)
    {
        var sceneObject = binding.SceneObject;

        if (sceneObject.IsDestroyed ||
            !sceneObject.IsActive)
        {
            DestroyNativeBody(runtime, binding);
            return;
        }

        var activeColliderCount = 0;
        foreach (var colliderBinding in binding.Colliders.Values)
        {
            if (colliderBinding.Collider.IsActive)
                activeColliderCount++;
            else
                RemoveNativeShape(runtime, binding, colliderBinding);
        }

        if (activeColliderCount == 0)
        {
            DestroyNativeBody(runtime, binding);
            return;
        }

        EnsureNativeBody(runtime, binding);

        var shapesChanged = false;
        foreach (var colliderBinding in binding.Colliders.Values)
        {
            if (!colliderBinding.Collider.IsActive)
                continue;

            try
            {
                shapesChanged |= SynchronizeShape(
                    runtime,
                    binding,
                    colliderBinding);
                colliderBinding.HasSyncError = false;
            }
            catch (Exception exception)
            {
                if (!colliderBinding.HasSyncError)
                {
                    Logger.Error(
                        exception,
                        $"Collider synchronization failed on " +
                        $"'{sceneObject.Name}'.");
                    colliderBinding.HasSyncError = true;
                }

                RemoveNativeShape(runtime, binding, colliderBinding);
            }
        }

        var hasNativeShape = false;
        foreach (var colliderBinding in binding.Colliders.Values)
        {
            if (colliderBinding.NativeShape is null)
                continue;

            hasNativeShape = true;
            break;
        }

        if (!hasNativeShape)
        {
            DestroyNativeBody(runtime, binding);
            return;
        }

        var desired = PhysicsDescriptionFactory.DescribeBody(
            sceneObject,
            binding.RigidBody);

        ApplyBodyDefinition(
            binding,
            desired,
            shapesChanged || binding.InertiaDirty);
        binding.AppliedDefinition = desired;
        binding.InertiaDirty = false;
        ApplyVelocityOverrides(binding);
    }

    private static bool SynchronizeShape(
        SceneRuntime runtime,
        BodyBinding bodyBinding,
        ColliderBinding colliderBinding)
    {
        if (!runtime.CollisionLayers.TryResolve(
                colliderBinding.Collider.CollisionLayer,
                out _))
        {
            throw new InvalidOperationException(
                $"Collider references unknown collision layer " +
                $"'{colliderBinding.Collider.CollisionLayer}'.");
        }

        var desired = PhysicsDescriptionFactory.DescribeShape(
            colliderBinding.Collider,
            bodyBinding.SceneObject.Transform.WorldScale);

        if (colliderBinding.NativeShape is not null &&
            colliderBinding.AppliedDefinition == desired)
        {
            var triggerChanged =
                colliderBinding.AppliedIsTrigger !=
                colliderBinding.Collider.IsTrigger;

            colliderBinding.AppliedIsTrigger =
                colliderBinding.Collider.IsTrigger;

            if (triggerChanged)
                bodyBinding.InertiaDirty = true;

            return triggerChanged;
        }

        RemoveNativeShape(runtime, bodyBinding, colliderBinding);

        var shape = PhysicsShapeFactory.Create(desired);
        bodyBinding.NativeBody!.AddShape(
            shape,
            MassInertiaUpdateMode.Preserve);

        colliderBinding.NativeShape = shape;
        colliderBinding.AppliedDefinition = desired;
        colliderBinding.AppliedIsTrigger =
            colliderBinding.Collider.IsTrigger;
        bodyBinding.InertiaDirty = true;
        runtime.CollidersByShapeId.Add(shape.ShapeId, colliderBinding);
        return true;
    }

    private static void EnsureNativeBody(
        SceneRuntime runtime,
        BodyBinding binding)
    {
        if (binding.NativeBody is not null)
            return;

        var pose = PhysicsPose.From(binding.SceneObject.Transform);
        var nativeBody = runtime.World.CreateRigidBody();
        nativeBody.Tag = binding;
        nativeBody.Position = PhysicsShapeFactory.ToJVector(pose.Position);
        nativeBody.Orientation =
            PhysicsShapeFactory.ToJQuaternion(pose.Rotation);
        nativeBody.BeginCollide += runtime.OnBeginCollide;

        binding.NativeBody = nativeBody;
        binding.PreviousPose = pose;
        binding.CurrentPose = pose;
        binding.AppliedDefinition = null;
        runtime.BodiesByNativeBody.Add(nativeBody, binding);
    }

    private static void DestroyNativeBody(
        SceneRuntime runtime,
        BodyBinding binding)
    {
        var nativeBody = binding.NativeBody;
        if (nativeBody is null)
            return;

        foreach (var colliderBinding in binding.Colliders.Values)
            RemoveNativeShape(runtime, binding, colliderBinding);

        nativeBody.BeginCollide -= runtime.OnBeginCollide;
        runtime.BodiesByNativeBody.Remove(nativeBody);
        runtime.World.Remove(nativeBody);

        binding.NativeBody = null;
        binding.AppliedDefinition = null;
    }

    private static void RemoveNativeShape(
        SceneRuntime runtime,
        BodyBinding bodyBinding,
        ColliderBinding colliderBinding)
    {
        var shape = colliderBinding.NativeShape;
        if (shape is null)
            return;

        runtime.CollidersByShapeId.Remove(shape.ShapeId);

        if (bodyBinding.NativeBody is not null)
        {
            bodyBinding.NativeBody.RemoveShape(
                shape,
                MassInertiaUpdateMode.Preserve);
        }

        colliderBinding.NativeShape = null;
        colliderBinding.AppliedDefinition = null;
        colliderBinding.AppliedIsTrigger = null;
        bodyBinding.InertiaDirty = true;
    }

    private static void ApplyBodyDefinition(
        BodyBinding binding,
        in PhysicsBodyDefinition desired,
        bool shapesChanged)
    {
        var nativeBody = binding.NativeBody!;
        var previous = binding.AppliedDefinition;

        nativeBody.MotionType = desired.MotionType;
        nativeBody.AffectedByGravity = desired.AffectedByGravity;
        nativeBody.EnableSpeculativeContacts =
            desired.EnableSpeculativeContacts;
        nativeBody.Damping = (
            desired.LinearDamping,
            desired.AngularDamping);

        if (desired.MotionType == MotionType.Dynamic &&
            (shapesChanged ||
             previous is null ||
             previous.Value.Mass != desired.Mass ||
             previous.Value.MotionType != desired.MotionType))
        {
            RecalculateMassAndInertia(binding, desired.Mass);
        }

        nativeBody.SetActivationState(true);
    }

    private static void RecalculateMassAndInertia(
        BodyBinding binding,
        float mass)
    {
        var nativeBody = binding.NativeBody!;
        var triggerShapes = binding.Colliders.Values
            .Where(collider =>
                collider.Collider.IsTrigger &&
                collider.NativeShape is not null)
            .Select(collider => collider.NativeShape!)
            .ToArray();

        var solidShapeCount =
            nativeBody.Shapes.Count - triggerShapes.Length;

        if (solidShapeCount > 0)
        {
            foreach (var shape in triggerShapes)
            {
                nativeBody.RemoveShape(
                    shape,
                    MassInertiaUpdateMode.Preserve);
            }

            nativeBody.SetMassInertia(mass);

            foreach (var shape in triggerShapes)
            {
                nativeBody.AddShape(
                    shape,
                    MassInertiaUpdateMode.Preserve);
            }
        }
        else
        {
            // A trigger-only dynamic body still needs a valid inertia tensor.
            // Its shapes are used as the best available approximation.
            nativeBody.SetMassInertia(mass);
        }
    }

    private static void ApplyVelocityOverrides(BodyBinding binding)
    {
        var rigidBody = binding.RigidBody;
        var nativeBody = binding.NativeBody;

        if (rigidBody is null ||
            nativeBody is null ||
            nativeBody.MotionType == MotionType.Static)
        {
            return;
        }

        if (binding.AppliedVelocityVersion !=
            rigidBody.VelocityVersion)
        {
            nativeBody.Velocity =
                PhysicsShapeFactory.ToJVector(rigidBody.Velocity);
            binding.AppliedVelocityVersion =
                rigidBody.VelocityVersion;
        }

        if (binding.AppliedAngularVelocityVersion !=
            rigidBody.AngularVelocityVersion)
        {
            nativeBody.AngularVelocity =
                PhysicsShapeFactory.ToJVector(
                    rigidBody.AngularVelocity);
            binding.AppliedAngularVelocityVersion =
                rigidBody.AngularVelocityVersion;
        }
    }

    private static void PushTransformsToPhysics(SceneRuntime runtime)
    {
        foreach (var binding in runtime.BodiesByObject.Values)
        {
            var nativeBody = binding.NativeBody;
            if (nativeBody is null ||
                nativeBody.MotionType == MotionType.Dynamic)
            {
                continue;
            }

            var pose = PhysicsPose.From(binding.SceneObject.Transform);
            nativeBody.Position =
                PhysicsShapeFactory.ToJVector(pose.Position);
            nativeBody.Orientation =
                PhysicsShapeFactory.ToJQuaternion(pose.Rotation);
            binding.PreviousPose = pose;
            binding.CurrentPose = pose;
        }
    }

    private static void CapturePreviousDynamicPoses(
        SceneRuntime runtime)
    {
        foreach (var binding in runtime.BodiesByObject.Values)
        {
            if (binding.NativeBody?.MotionType == MotionType.Dynamic)
                binding.PreviousPose = binding.CurrentPose;
        }
    }

    private static void CaptureCurrentDynamicPoses(
        SceneRuntime runtime)
    {
        foreach (var binding in runtime.BodiesByObject.Values)
        {
            var nativeBody = binding.NativeBody;
            if (nativeBody?.MotionType != MotionType.Dynamic)
                continue;

            binding.CurrentPose = new PhysicsPose(
                PhysicsShapeFactory.ToVector3(nativeBody.Position),
                PhysicsShapeFactory.ToQuaternion(
                    nativeBody.Orientation));

            binding.RigidBody?.SetSimulationVelocity(
                PhysicsShapeFactory.ToVector3(nativeBody.Velocity),
                PhysicsShapeFactory.ToVector3(
                    nativeBody.AngularVelocity));
        }
    }

    private void ApplyDynamicTransforms(SceneRuntime runtime)
    {
        var alpha = _settings.FixedDeltaTime <= float.Epsilon
            ? 1.0f
            : Math.Clamp(
                runtime.Accumulator / _settings.FixedDeltaTime,
                0.0f,
                1.0f);

        foreach (var binding in runtime.BodiesByObject.Values)
        {
            if (binding.NativeBody?.MotionType != MotionType.Dynamic)
                continue;

            var pose = _settings.InterpolationEnabled
                ? new PhysicsPose(
                    Vector3.Lerp(
                        binding.PreviousPose.Position,
                        binding.CurrentPose.Position,
                        alpha),
                    Quaternion.Slerp(
                        binding.PreviousPose.Rotation,
                        binding.CurrentPose.Rotation,
                        alpha))
                : binding.CurrentPose;

            binding.SceneObject.Transform.WorldPosition =
                pose.Position;
            binding.SceneObject.Transform.WorldRotation =
                pose.Rotation;
        }
    }

    private static void ApplyCommands(SceneRuntime runtime)
    {
        while (runtime.Commands.TryDequeue(out var command))
        {
            if (command.Target.SceneObject is not { } sceneObject ||
                !runtime.BodiesByObject.TryGetValue(
                    sceneObject,
                    out var binding) ||
                binding.NativeBody is not { } nativeBody)
            {
                continue;
            }

            switch (command.Type)
            {
                case EPhysicsCommand.AddForce:
                {
                    var force =
                        PhysicsShapeFactory.ToJVector(command.Vector);
                    nativeBody.AddForce(force, true);
                    break;
                }

                case EPhysicsCommand.AddImpulse:
                {
                    var impulse =
                        PhysicsShapeFactory.ToJVector(command.Vector);
                    nativeBody.ApplyImpulse(impulse, true);
                    break;
                }

                case EPhysicsCommand.Teleport:
                {
                    nativeBody.Position =
                        PhysicsShapeFactory.ToJVector(command.Vector);
                    nativeBody.Orientation =
                        PhysicsShapeFactory.ToJQuaternion(
                            command.Rotation);
                    nativeBody.SetActivationState(true);

                    var pose = new PhysicsPose(
                        command.Vector,
                        command.Rotation);
                    binding.PreviousPose = pose;
                    binding.CurrentPose = pose;
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private static void BeginContactDetection(SceneRuntime runtime)
    {
        runtime.CollisionPairsNext.Clear();
        runtime.TriggerPairsNext.Clear();
        runtime.CollisionGeometryNext.Clear();
        runtime.TriggerGeometryNext.Clear();
    }

    private static void CompleteContactDetection(SceneRuntime runtime)
    {
        CompletePairSet(
            runtime,
            runtime.CollisionPairs,
            runtime.CollisionPairsNext,
            runtime.CollisionGeometry,
            runtime.CollisionGeometryNext,
            trigger: false);

        CompletePairSet(
            runtime,
            runtime.TriggerPairs,
            runtime.TriggerPairsNext,
            runtime.TriggerGeometry,
            runtime.TriggerGeometryNext,
            trigger: true);
    }

    private static void CompletePairSet(
        SceneRuntime runtime,
        HashSet<ColliderPair> active,
        HashSet<ColliderPair> next,
        Dictionary<ColliderPair, ContactGeometry> activeGeometry,
        Dictionary<ColliderPair, ContactGeometry> nextGeometry,
        bool trigger)
    {
        foreach (var pair in next)
        {
            runtime.PendingContacts.Add(
                new PendingContact(
                    active.Contains(pair)
                        ? EContactEvent.Stay
                        : EContactEvent.Enter,
                    pair,
                    trigger,
                    nextGeometry.GetValueOrDefault(pair)));
        }

        foreach (var pair in active)
        {
            if (!next.Contains(pair))
            {
                runtime.PendingContacts.Add(
                    new PendingContact(
                        EContactEvent.Exit,
                        pair,
                        trigger,
                        activeGeometry.GetValueOrDefault(pair)));
            }
        }

        active.Clear();
        activeGeometry.Clear();
        foreach (var pair in next)
        {
            active.Add(pair);
            activeGeometry[pair] =
                nextGeometry.GetValueOrDefault(pair);
        }
    }

    private static void RemovePairsFor(
        SceneRuntime runtime,
        ColliderBinding collider)
    {
        RemovePairsFor(
            runtime,
            runtime.CollisionPairs,
            runtime.CollisionGeometry,
            collider,
            trigger: false);
        RemovePairsFor(
            runtime,
            runtime.TriggerPairs,
            runtime.TriggerGeometry,
            collider,
            trigger: true);

        runtime.CollisionPairsNext.RemoveWhere(
            pair => pair.Contains(collider));
        runtime.TriggerPairsNext.RemoveWhere(
            pair => pair.Contains(collider));
    }

    private static void RemovePairsFor(
        SceneRuntime runtime,
        HashSet<ColliderPair> pairs,
        Dictionary<ColliderPair, ContactGeometry> geometry,
        ColliderBinding collider,
        bool trigger)
    {
        foreach (var pair in pairs
                     .Where(pair => pair.Contains(collider))
                     .ToArray())
        {
            pairs.Remove(pair);
            runtime.PendingContacts.Add(
                new PendingContact(
                    EContactEvent.Exit,
                    pair,
                    trigger,
                    geometry.GetValueOrDefault(pair)));
            geometry.Remove(pair);
        }
    }

    private static void DispatchPendingContacts(SceneRuntime runtime)
    {
        foreach (var pending in runtime.PendingContacts)
        {
            DispatchContact(
                pending.Pair.A,
                pending.Pair.B,
                pending.Type,
                pending.IsTrigger,
                pending.Geometry);
            DispatchContact(
                pending.Pair.B,
                pending.Pair.A,
                pending.Type,
                pending.IsTrigger,
                pending.Geometry);
        }

        runtime.PendingContacts.Clear();
    }

    private static void DispatchContact(
        ColliderBinding self,
        ColliderBinding other,
        EContactEvent type,
        bool trigger,
        ContactGeometry geometry)
    {
        var selfObject = self.Body.SceneObject;
        var otherObject = other.Body.SceneObject;

        if (selfObject.IsDestroyed ||
            otherObject.IsDestroyed)
        {
            return;
        }

        var contact = new PhysicsContact(
            selfObject,
            self.Collider,
            otherObject,
            other.Collider,
            geometry.Point,
            ReferenceEquals(self, geometry.First)
                ? geometry.Normal
                : -geometry.Normal,
            geometry.Penetration);

        foreach (var component in selfObject.Components.ToArray())
        {
            try
            {
                if (trigger && component is ITriggerHandler triggerHandler)
                {
                    switch (type)
                    {
                        case EContactEvent.Enter:
                            triggerHandler.OnTriggerEnter(contact);
                            break;
                        case EContactEvent.Stay:
                            triggerHandler.OnTriggerStay(contact);
                            break;
                        case EContactEvent.Exit:
                            triggerHandler.OnTriggerExit(contact);
                            break;
                    }
                }
                else if (!trigger &&
                         component is ICollisionHandler collisionHandler)
                {
                    switch (type)
                    {
                        case EContactEvent.Enter:
                            collisionHandler.OnCollisionEnter(contact);
                            break;
                        case EContactEvent.Stay:
                            collisionHandler.OnCollisionStay(contact);
                            break;
                        case EContactEvent.Exit:
                            collisionHandler.OnCollisionExit(contact);
                            break;
                    }
                }
            }
            catch (Exception exception)
            {
                Logger.Error(
                    exception,
                    $"Physics callback failed on " +
                    $"'{component.GetType().Name}'.");
            }
        }
    }

    private SceneRuntime GetRuntime(Scene.SceneInstance sceneInstance)
    {
        if (_runtimes.TryGetValue(sceneInstance, out var runtime))
            return runtime;

        runtime = new SceneRuntime(sceneInstance, _settings);
        _runtimes.Add(sceneInstance, runtime);
        return runtime;
    }

    private bool TryGetRuntime(
        Scene.SceneInstance sceneInstance,
        out SceneRuntime runtime) =>
        _runtimes.TryGetValue(sceneInstance, out runtime!);

    private enum EStructuralChange : byte
    {
        AddObject,
        RemoveObject,
        AddComponent,
        RemoveComponent
    }

    private enum EPhysicsCommand : byte
    {
        AddForce,
        AddImpulse,
        Teleport
    }

    private enum EContactEvent : byte
    {
        Enter,
        Stay,
        Exit
    }

    private readonly record struct StructuralChange(
        EStructuralChange Type,
        SceneObject SceneObject,
        AComponent? Component);

    private readonly record struct PhysicsCommand(
        EPhysicsCommand Type,
        RigidBody Target,
        Vector3 Vector,
        Quaternion Rotation);

    private readonly record struct PendingContact(
        EContactEvent Type,
        ColliderPair Pair,
        bool IsTrigger,
        ContactGeometry Geometry);

    private readonly record struct ContactGeometry(
        ColliderBinding? First,
        Vector3 Point,
        Vector3 Normal,
        float Penetration);

    private sealed class BodyBinding(SceneObject sceneObject)
    {
        public SceneObject SceneObject { get; } = sceneObject;
        public RigidBody? RigidBody { get; set; }
        public Jitter2.Dynamics.RigidBody? NativeBody { get; set; }
        public PhysicsBodyDefinition? AppliedDefinition { get; set; }
        public Dictionary<Collider, ColliderBinding> Colliders { get; } = [];
        public PhysicsPose PreviousPose { get; set; }
        public PhysicsPose CurrentPose { get; set; }
        public int AppliedVelocityVersion { get; set; } = -1;
        public int AppliedAngularVelocityVersion { get; set; } = -1;
        public bool InertiaDirty { get; set; }
    }

    private sealed class ColliderBinding(
        long id,
        BodyBinding body,
        Collider collider)
    {
        public long Id { get; } = id;
        public BodyBinding Body { get; } = body;
        public Collider Collider { get; } = collider;
        public RigidBodyShape? NativeShape { get; set; }
        public PhysicsShapeDefinition? AppliedDefinition { get; set; }
        public bool? AppliedIsTrigger { get; set; }
        public bool HasSyncError { get; set; }
    }

    private readonly record struct ColliderPair
    {
        public ColliderBinding A { get; }
        public ColliderBinding B { get; }

        public ColliderPair(
            ColliderBinding first,
            ColliderBinding second)
        {
            if (first.Id <= second.Id)
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

        public bool Contains(ColliderBinding collider) =>
            ReferenceEquals(A, collider) ||
            ReferenceEquals(B, collider);
    }

    private sealed class BroadPhaseFilter(
        SceneRuntime runtime) : IBroadPhaseFilter
    {
        public bool Filter(
            IDynamicTreeProxy proxyA,
            IDynamicTreeProxy proxyB)
        {
            if (proxyA is not RigidBodyShape shapeA ||
                proxyB is not RigidBodyShape shapeB)
            {
                return false;
            }

            if (ReferenceEquals(shapeA.RigidBody, shapeB.RigidBody))
                return false;

            if (!runtime.CollidersByShapeId.TryGetValue(
                    shapeA.ShapeId,
                    out var colliderA) ||
                !runtime.CollidersByShapeId.TryGetValue(
                    shapeB.ShapeId,
                    out var colliderB))
            {
                return false;
            }

            return PassesCollisionMask(
                runtime,
                colliderA.Collider,
                colliderB.Collider);
        }
    }

    private sealed class NarrowPhaseFilter(
        SceneRuntime runtime) : INarrowPhaseFilter
    {
        public bool Filter(
            RigidBodyShape shapeA,
            RigidBodyShape shapeB,
            ref JVector pointA,
            ref JVector pointB,
            ref JVector normal,
            ref float penetration)
        {
            if (!runtime.CollidersByShapeId.TryGetValue(
                    shapeA.ShapeId,
                    out var colliderA) ||
                !runtime.CollidersByShapeId.TryGetValue(
                    shapeB.ShapeId,
                    out var colliderB))
            {
                return false;
            }

            if (!PassesCollisionMask(
                    runtime,
                    colliderA.Collider,
                    colliderB.Collider))
            {
                return false;
            }

            var pair = new ColliderPair(colliderA, colliderB);
            var isTrigger =
                colliderA.Collider.IsTrigger ||
                colliderB.Collider.IsTrigger;
            var geometry = new ContactGeometry(
                colliderA,
                (PhysicsShapeFactory.ToVector3(pointA) +
                 PhysicsShapeFactory.ToVector3(pointB)) * 0.5f,
                PhysicsShapeFactory.ToVector3(normal),
                penetration);

            if (isTrigger)
            {
                runtime.TriggerPairsNext.Add(pair);
                StoreDeepestContact(
                    runtime.TriggerGeometryNext,
                    pair,
                    geometry);
            }
            else
            {
                runtime.CollisionPairsNext.Add(pair);
                StoreDeepestContact(
                    runtime.CollisionGeometryNext,
                    pair,
                    geometry);
                runtime.ApplyContactMaterial(
                    shapeA.ShapeId,
                    shapeB.ShapeId);
            }

            return !isTrigger;
        }

        private static void StoreDeepestContact(
            Dictionary<ColliderPair, ContactGeometry> contacts,
            ColliderPair pair,
            ContactGeometry geometry)
        {
            if (!contacts.TryGetValue(pair, out var current) ||
                geometry.Penetration > current.Penetration)
            {
                contacts[pair] = geometry;
            }
        }
    }

    private static bool PassesCollisionMask(
        SceneRuntime runtime,
        Collider first,
        Collider second)
    {
        if (!runtime.CollisionLayers.TryResolve(
                first.CollisionLayer,
                out var firstLayer) ||
            !runtime.CollisionLayers.TryResolve(
                second.CollisionLayer,
                out var secondLayer))
        {
            return false;
        }

        return (firstLayer.Bit & secondLayer.Mask) != 0 &&
               (secondLayer.Bit & firstLayer.Mask) != 0;
    }

    private sealed class SceneRuntime : IDisposable
    {
        public SceneRuntime(
            Scene.SceneInstance sceneInstance,
            PhysicsSettings settings)
        {
            SceneInstance = sceneInstance;
            CollisionLayers = settings.CollisionLayers;
            World = new World
            {
                Gravity =
                    PhysicsShapeFactory.ToJVector(settings.Gravity),
                AllowDeactivation = settings.AllowSleeping
            };
            World.SolverIterations = (
                settings.SolverIterations,
                settings.SolverRelaxationIterations);
            World.BroadPhaseFilter = new BroadPhaseFilter(this);
            World.NarrowPhaseFilter = new NarrowPhaseFilter(this);
        }

        public Scene.SceneInstance SceneInstance { get; }
        public World World { get; }
        public PhysicsCollisionLayers CollisionLayers { get; set; }
        public Dictionary<SceneObject, BodyBinding> BodiesByObject { get; } = [];
        public Dictionary<Jitter2.Dynamics.RigidBody, BodyBinding> BodiesByNativeBody { get; } = [];
        public Dictionary<Collider, ColliderBinding> ShapesByCollider { get; } = [];
        public Dictionary<ulong, ColliderBinding> CollidersByShapeId { get; } = [];
        public Queue<StructuralChange> StructuralChanges { get; } = [];
        public Queue<PhysicsCommand> Commands { get; } = [];
        public HashSet<ColliderPair> CollisionPairs { get; } = [];
        public HashSet<ColliderPair> CollisionPairsNext { get; } = [];
        public HashSet<ColliderPair> TriggerPairs { get; } = [];
        public HashSet<ColliderPair> TriggerPairsNext { get; } = [];
        public Dictionary<ColliderPair, ContactGeometry> CollisionGeometry { get; } = [];
        public Dictionary<ColliderPair, ContactGeometry> CollisionGeometryNext { get; } = [];
        public Dictionary<ColliderPair, ContactGeometry> TriggerGeometry { get; } = [];
        public Dictionary<ColliderPair, ContactGeometry> TriggerGeometryNext { get; } = [];
        public List<PendingContact> PendingContacts { get; } = [];
        public float Accumulator { get; set; }
        public long NextColliderId { get; set; }
        public bool IsUpdating { get; set; }

        public void OnBeginCollide(Arbiter arbiter)
        {
            ref var contact = ref arbiter.Handle.Data;
            var key = contact.Key;

            if (!CollidersByShapeId.TryGetValue(
                    key.Key1,
                    out var colliderA) ||
                !CollidersByShapeId.TryGetValue(
                    key.Key2,
                    out var colliderB))
            {
                return;
            }

            ApplyContactMaterial(
                ref contact,
                colliderA,
                colliderB);
        }

        public void ApplyContactMaterial(
            ulong firstShapeId,
            ulong secondShapeId)
        {
            var firstId = Math.Min(firstShapeId, secondShapeId);
            var secondId = Math.Max(firstShapeId, secondShapeId);

            if (!World.GetArbiter(firstId, secondId, out var arbiter) ||
                !CollidersByShapeId.TryGetValue(
                    firstId,
                    out var colliderA) ||
                !CollidersByShapeId.TryGetValue(
                    secondId,
                    out var colliderB))
            {
                return;
            }

            ref var contact = ref arbiter.Handle.Data;
            ApplyContactMaterial(
                ref contact,
                colliderA,
                colliderB);
        }

        private static void ApplyContactMaterial(
            ref ContactData contact,
            ColliderBinding colliderA,
            ColliderBinding colliderB)
        {
            var materialA = colliderA.Collider.Material;
            var materialB = colliderB.Collider.Material;
            contact.Friction = MathF.Sqrt(
                materialA.Friction * materialB.Friction);
            contact.Restitution = Math.Max(
                materialA.Restitution,
                materialB.Restitution);
        }

        public void Dispose()
        {
            foreach (var binding in BodiesByObject.Values.ToArray())
                DestroyNativeBody(this, binding);

            BodiesByObject.Clear();
            BodiesByNativeBody.Clear();
            ShapesByCollider.Clear();
            CollidersByShapeId.Clear();
            CollisionPairs.Clear();
            CollisionPairsNext.Clear();
            TriggerPairs.Clear();
            TriggerPairsNext.Clear();
            CollisionGeometry.Clear();
            CollisionGeometryNext.Clear();
            TriggerGeometry.Clear();
            TriggerGeometryNext.Clear();
            PendingContacts.Clear();
            StructuralChanges.Clear();
            Commands.Clear();
            World.Dispose();
        }
    }
}