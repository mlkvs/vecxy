using System.Numerics;
using System.Runtime.CompilerServices;
using Autofac;
using Vecxy.Assets;
using Vecxy.Diagnostics;
using Vecxy.Kernel;
using Vecxy.Scene;

namespace Vecxy.Physics;

public sealed class PhysicsModule(
    ISceneManager scenes,
    IConfigProvider configs,
    PhysicsModule.Options options,
    PhysicsSceneSystem physicsSceneSystem) :
    IModule,
    IPhysicsSystem
{
    public sealed class Options
    {
        public string ConfigPath { get; init; } = "Configs/Physics.yaml";
    }

    public sealed class Definition : AModuleDefinition<PhysicsModule>
    {
        private readonly Options _options;

        protected override IReadOnlyList<Type> Exports => [typeof(IPhysicsSystem)];

        public Definition(Options? options = null)
        {
            _options = options ?? new Options();
        }

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
                .RegisterInstance(_options)
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<PhysicsModule>()
                .AsSelf()
                .SingleInstance();
        }
    }

    private ConfigRef<PhysicsConfig>? _config;
    private bool _disposed;

    public PhysicsSettings Settings => physicsSceneSystem.Settings;

    public void OnInitialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _config = configs.LoadConfig<PhysicsConfig>(
            options.ConfigPath);

        if (!_config.TryGetValue(out var config) ||
            config is null)
        {
            throw new InvalidOperationException(
                $"Physics config '{options.ConfigPath}' is invalid.",
                _config.LastError);
        }

        physicsSceneSystem.SetInitialSettings(
            config.ToSettings());
        _config.Changed += OnConfigChanged;
    }

    public void AddForce(RigidBody body, Vector3 force)
    {
        ThrowIfNotFinite(force, nameof(force));
        physicsSceneSystem.EnqueueForce(body, force);
    }

    public void AddImpulse(RigidBody body, Vector3 impulse)
    {
        ThrowIfNotFinite(impulse, nameof(impulse));
        physicsSceneSystem.EnqueueImpulse(body, impulse);
    }

    public void Teleport(RigidBody body, Vector3 position, Quaternion rotation)
    {
        ThrowIfNotFinite(position, nameof(position));

        if (!IsFinite(rotation) ||
            rotation.LengthSquared() <= float.Epsilon)
        {
            throw new ArgumentException(
                "Teleport rotation must be finite and non-zero.",
                nameof(rotation));
        }

        physicsSceneSystem.EnqueueTeleport(
            body,
            position,
            Quaternion.Normalize(rotation));
    }

    public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, SceneObject? ignoreSceneObject, out PhysicsRaycastHit hit)
    {
        hit = default;

        var scene = ignoreSceneObject?.SceneInstance ?? scenes.ActiveScene;
        
        return scene is not null &&
               physicsSceneSystem.Raycast(
                   scene,
                   origin,
                   direction,
                   maxDistance,
                   ignoreSceneObject,
                   out hit);
    }

    public void OnShutdown()
    {
        if (_config is not null)
        {
            _config.Changed -= OnConfigChanged;
            _config.Dispose();
            _config = null;
        }

        physicsSceneSystem.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        OnShutdown();
        _disposed = true;
    }

    private void OnConfigChanged(PhysicsConfig config)
    {
        physicsSceneSystem.QueueSettings(config.ToSettings());
    }

    private static void ThrowIfNotFinite(Vector3 value, string parameterName)
    {
        if (float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z))
        {
            return;
        }

        throw new ArgumentException(
            "Physics vector must be finite.",
            parameterName);
    }

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}
