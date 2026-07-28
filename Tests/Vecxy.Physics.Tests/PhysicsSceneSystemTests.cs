using System.Numerics;
using Vecxy.Scene;
using Xunit;

namespace Vecxy.Physics.Tests;

public sealed class PhysicsSceneSystemTests
{
    [Fact]
    public void PhysicsConfigProducesConfiguredFixedStep()
    {
        var config = new PhysicsConfig
        {
            FixedUpdateRate = 120.0f,
            MaxSubSteps = 4,
            Gravity = [0.0f, -3.0f, 0.0f]
        };

        config.Validate("Physics.yaml");
        var settings = config.ToSettings();

        Assert.Equal(1.0f / 120.0f, settings.FixedDeltaTime);
        Assert.Equal(4, settings.MaxSubSteps);
        Assert.Equal(new Vector3(0.0f, -3.0f, 0.0f), settings.Gravity);
        Assert.Equal(
            new PhysicsLayer(1u, 1u),
            settings.CollisionLayers.Resolve("default"));
    }

    [Fact]
    public void MultipleCollidersBecomeIndependentShapes()
    {
        using var fixture = new PhysicsFixture();
        var sceneObject = fixture.Scene.CreateObject(
            "Compound",
            isStatic: true);
        var left = sceneObject.AddComponent<BoxCollider>();
        left.Center = new Vector3(-2.0f, 0.0f, 0.0f);
        var right = sceneObject.AddComponent<BoxCollider>();
        right.Center = new Vector3(2.0f, 0.0f, 0.0f);

        fixture.Update(0.0f);

        Assert.True(
            fixture.System.Raycast(
                fixture.Scene,
                new Vector3(-2.0f, 0.0f, 5.0f),
                -Vector3.UnitZ,
                10.0f,
                null,
                out var leftHit));
        Assert.Same(left, leftHit.Collider);

        Assert.True(
            fixture.System.Raycast(
                fixture.Scene,
                new Vector3(2.0f, 0.0f, 5.0f),
                -Vector3.UnitZ,
                10.0f,
                null,
                out var rightHit));
        Assert.Same(right, rightHit.Collider);
    }

    [Fact]
    public void SingleInstanceAttributePreventsDuplicateRigidBodies()
    {
        using var fixture = new PhysicsFixture();
        var sceneObject = fixture.Scene.CreateObject("Body");
        sceneObject.AddComponent<RigidBody>();

        Assert.Throws<InvalidOperationException>(
            () => sceneObject.AddComponent<RigidBody>());
    }

    [Fact]
    public void FixedUpdateRunsForEveryPhysicsSubstep()
    {
        using var fixture = new PhysicsFixture();
        var sceneObject = fixture.Scene.CreateObject("Counter");
        var counter = sceneObject.AddComponent<FixedUpdateCounter>();

        fixture.Update(0.1f);

        Assert.Equal(6, counter.CallCount);
        Assert.All(
            counter.DeltaTimes,
            delta => Assert.Equal(
                fixture.System.Settings.FixedDeltaTime,
                delta));
    }

    [Fact]
    public void IsStaticOverridesRigidBodyMotionType()
    {
        using var fixture = new PhysicsFixture();
        var sceneObject = fixture.Scene.CreateObject(
            "Static Dynamic Body",
            isStatic: true);
        sceneObject.Transform.WorldPosition =
            new Vector3(0.0f, 5.0f, 0.0f);
        sceneObject.AddComponent<SphereCollider>();
        sceneObject.AddComponent<RigidBody>();

        for (var index = 0; index < 10; index++)
            fixture.Update(1.0f / 60.0f);

        Assert.Equal(5.0f, sceneObject.Transform.WorldPosition.Y, 4);

        sceneObject.IsStatic = false;

        for (var index = 0; index < 10; index++)
            fixture.Update(1.0f / 60.0f);

        Assert.True(sceneObject.Transform.WorldPosition.Y < 5.0f);
    }

    [Fact]
    public void RestartRequiredSettingsAreRejectedForLiveWorlds()
    {
        using var fixture = new PhysicsFixture();
        var current = fixture.System.Settings;
        var next = current with
        {
            Gravity = new Vector3(0.0f, -3.0f, 0.0f),
            SolverIterations = current.SolverIterations + 1
        };

        fixture.System.QueueSettings(next);
        fixture.Update(0.0f);

        Assert.Equal(next.Gravity, fixture.System.Settings.Gravity);
        Assert.Equal(
            current.SolverIterations,
            fixture.System.Settings.SolverIterations);
    }

    [Fact]
    public void CollisionReportsExactColliderPair()
    {
        using var fixture = new PhysicsFixture();

        var floorObject = fixture.Scene.CreateObject(
            "Floor",
            isStatic: true);
        floorObject.Transform.WorldPosition =
            new Vector3(0.0f, -0.5f, 0.0f);
        var floorCollider = floorObject.AddComponent<BoxCollider>();
        floorCollider.Size = new Vector3(10.0f, 1.0f, 10.0f);

        var ballObject = fixture.Scene.CreateObject("Ball");
        ballObject.Transform.WorldPosition =
            new Vector3(0.0f, 2.0f, 0.0f);
        var ballCollider = ballObject.AddComponent<SphereCollider>();
        ballObject.AddComponent<RigidBody>();
        var listener = ballObject.AddComponent<ContactListener>();

        for (var index = 0; index < 180; index++)
            fixture.Update(1.0f / 60.0f);

        var contact = Assert.Single(listener.CollisionEnters);
        Assert.Same(ballCollider, contact.SelfCollider);
        Assert.Same(floorCollider, contact.OtherCollider);
    }

    [Fact]
    public void TriggerCanCoexistWithSolidCollider()
    {
        using var fixture = new PhysicsFixture();

        var sensorObject = fixture.Scene.CreateObject(
            "Sensor",
            isStatic: true);
        var solid = sensorObject.AddComponent<BoxCollider>();
        solid.Size = new Vector3(4.0f, 0.25f, 4.0f);
        solid.Center = new Vector3(0.0f, -2.0f, 0.0f);
        var trigger = sensorObject.AddComponent<SphereCollider>();
        trigger.Radius = 2.0f;
        trigger.IsTrigger = true;

        var visitorObject = fixture.Scene.CreateObject("Visitor");
        var visitor = visitorObject.AddComponent<SphereCollider>();
        var rigidBody = visitorObject.AddComponent<RigidBody>();
        rigidBody.AffectedByGravity = false;
        var listener = visitorObject.AddComponent<ContactListener>();

        fixture.Update(1.0f / 60.0f);

        var contact = Assert.Single(listener.TriggerEnters);
        Assert.Same(visitor, contact.SelfCollider);
        Assert.Same(trigger, contact.OtherCollider);
    }

    [Fact]
    public void CollisionMatrixControlsLayersAndUpdatesAtRuntime()
    {
        var disabledConfig = CreateLayerConfig(
            sensorTargets: [],
            visitorTargets: []);
        disabledConfig.Validate("Physics.yaml");

        using var fixture = new PhysicsFixture(
            disabledConfig.ToSettings());

        var sensorObject = fixture.Scene.CreateObject(
            "Sensor",
            isStatic: true);
        var sensor = sensorObject.AddComponent<SphereCollider>();
        sensor.IsTrigger = true;
        sensor.CollisionLayer = "sensor";

        var visitorObject = fixture.Scene.CreateObject("Visitor");
        var visitor = visitorObject.AddComponent<SphereCollider>();
        visitor.CollisionLayer = "visitor";
        var body = visitorObject.AddComponent<RigidBody>();
        body.AffectedByGravity = false;
        var listener = visitorObject.AddComponent<ContactListener>();

        fixture.Update(1.0f / 60.0f);
        Assert.Empty(listener.TriggerEnters);

        var enabledConfig = CreateLayerConfig(
            sensorTargets: ["visitor"],
            visitorTargets: ["sensor"]);
        enabledConfig.Validate("Physics.yaml");
        fixture.System.QueueSettings(enabledConfig.ToSettings());

        fixture.Update(1.0f / 60.0f);

        var contact = Assert.Single(listener.TriggerEnters);
        Assert.Same(visitor, contact.SelfCollider);
        Assert.Same(sensor, contact.OtherCollider);

        fixture.System.QueueSettings(disabledConfig.ToSettings());
        fixture.Update(1.0f / 60.0f);

        var exit = Assert.Single(listener.TriggerExits);
        Assert.Same(visitor, exit.SelfCollider);
        Assert.Same(sensor, exit.OtherCollider);
    }

    [Fact]
    public void CollisionMatrixMustBeSymmetric()
    {
        var config = CreateLayerConfig(
            sensorTargets: ["visitor"],
            visitorTargets: []);

        Assert.Throws<InvalidDataException>(
            () => config.Validate("Physics.yaml"));
    }

    private static PhysicsConfig CreateLayerConfig(
        string[] sensorTargets,
        string[] visitorTargets) =>
        new()
        {
            Gravity = [0.0f, 0.0f, 0.0f],
            CollisionLayers = new Dictionary<
                string,
                PhysicsCollisionLayerConfig>
            {
                ["default"] = new()
                {
                    Index = 0,
                    CollidesWith = ["default"]
                },
                ["sensor"] = new()
                {
                    Index = 1,
                    CollidesWith = sensorTargets
                },
                ["visitor"] = new()
                {
                    Index = 2,
                    CollidesWith = visitorTargets
                }
            }
        };

    private sealed class FixedUpdateCounter : AComponent
    {
        public int CallCount { get; private set; }
        public List<float> DeltaTimes { get; } = [];

        public override void FixedUpdate(float deltaTime)
        {
            CallCount++;
            DeltaTimes.Add(deltaTime);
        }
    }

    private sealed class ContactListener :
        AComponent,
        ICollisionHandler,
        ITriggerHandler
    {
        public List<PhysicsContact> CollisionEnters { get; } = [];
        public List<PhysicsContact> TriggerEnters { get; } = [];
        public List<PhysicsContact> TriggerExits { get; } = [];

        public void OnCollisionEnter(in PhysicsContact contact)
        {
            CollisionEnters.Add(contact);
        }

        public void OnCollisionStay(in PhysicsContact contact) { }
        public void OnCollisionExit(in PhysicsContact contact) { }

        public void OnTriggerEnter(in PhysicsContact contact)
        {
            TriggerEnters.Add(contact);
        }

        public void OnTriggerStay(in PhysicsContact contact) { }

        public void OnTriggerExit(in PhysicsContact contact)
        {
            TriggerExits.Add(contact);
        }
    }

    private sealed class PhysicsFixture : IDisposable
    {
        private readonly ScenesModule _scenes;

        public PhysicsSceneSystem System { get; } = new();
        public Scene.Scene Scene { get; }

        public PhysicsFixture(PhysicsSettings? settings = null)
        {
            System.SetInitialSettings(
                (settings ?? PhysicsSettings.Default) with
                {
                    InterpolationEnabled = false
                });

            _scenes = new ScenesModule([System]);
            _scenes.OnInitialize();
            Scene = _scenes.Create();
            _scenes.SetActiveScene(Scene);
        }

        public void Update(float deltaTime)
        {
            _scenes.OnUpdate(deltaTime);
        }

        public void Dispose()
        {
            _scenes.UnloadActiveScene();
            _scenes.Dispose();
            System.Shutdown();
        }
    }
}
