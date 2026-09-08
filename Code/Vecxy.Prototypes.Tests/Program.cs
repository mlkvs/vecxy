using Autofac;
using Vecxy.Assets;
using Vecxy.Kernel;
using Vecxy.Prototypes;
using Vecxy.Rendering;
using Vecxy.Scene;

var temporary = Path.Combine(Path.GetTempPath(), "vecxy-prototypes-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    TestSerializer();
    TestAutomaticDiscovery(temporary);
    TestScenePrototype(temporary);
    Console.WriteLine("All Vecxy.Prototypes checks passed.");
}
finally
{
    Directory.Delete(temporary, recursive: true);
}

static void TestSerializer()
{
    var source = """
        format: 1
        type: Demo.Widget
        data:
          name: test
          count: 3
        """;
    var document = PrototypeSerializer.Deserialize(source);
    Check(document.Type == "Demo.Widget", "document type");
    var roundTrip = PrototypeSerializer.Deserialize(PrototypeSerializer.Serialize(document));
    var options = (Widget.Prototype.Options)PrototypeSerializer.DeserializeOptions(
        roundTrip.Data, typeof(Widget.Prototype.Options));
    Check(options.Name == "test" && options.Count == 3, "YAML round trip");
}

static void TestAutomaticDiscovery(string root)
{
    var assets = new AssetsModule(new AssetsModule.Options { AssetsDirectory = root, HotReloadEnabled = false });
    assets.OnInitialize();
    var builder = new ContainerBuilder();
    builder.RegisterInstance(assets).As<IAssetsManager>().SingleInstance();
    new PrototypesModule.Definition([typeof(Widget).Assembly]).RegisterLocal(builder);
    using var container = builder.Build();
    var prototypes = container.Resolve<IPrototypes>();
    ((IModule)prototypes).OnInitialize();
    try
    {
        Check(prototypes.Types.Any(x => x.TargetType == typeof(Widget)), "automatic prototype discovery");
        Check(prototypes.Systems.Any(x => x.SystemType == typeof(WidgetPrototypeSystem)), "automatic system discovery");
        var direct = prototypes.Instantiate<Widget>(new WidgetContext(), new Widget.Prototype.Options { Name = "direct", Count = 2 });
        Check(direct.Name == "direct" && direct.Count == 2, "direct instantiation");

        prototypes.Save("base.prototype", PrototypeSerializer.Deserialize("""
            format: 1
            type: Widget
            data:
              name: base
              count: 4
            """));
        prototypes.Save("derived.prototype", PrototypeSerializer.Deserialize("""
            format: 1
            type: Widget
            prototype: base.prototype
            data:
              count: 9
            """));
        var derived = prototypes.Instantiate<Widget>("derived.prototype", new WidgetContext());
        Check(derived.Name == "base" && derived.Count == 9, "file inheritance and overrides");
        Check(prototypes.Validate(prototypes.Load("derived.prototype")).IsValid, "document validation");
        Check(prototypes.Validate("derived.prototype").IsValid, "resolved file validation");
    }
    finally
    {
        ((IModule)prototypes).Dispose();
        assets.Dispose();
    }
}

static void TestScenePrototype(string root)
{
    var assets = new AssetsModule(new AssetsModule.Options { AssetsDirectory = root, HotReloadEnabled = false });
    assets.OnInitialize();
    var prototypes = new PrototypesModule(assets,
        [new Camera.Prototype(), new SceneObject.Prototype()], [new ScenePrototypeSystem()]);
    prototypes.OnInitialize();
    var scene = new SceneInstance(new EmptyScene());
    scene.Load();
    scene.Activate();
    try
    {
        var camera = prototypes.Instantiate<Camera>(new ScenePrototypeContext
        {
            Scene = scene,
            Position = new System.Numerics.Vector3(1, 2, 3)
        }, new Camera.Prototype.Options { FieldOfView = 77 });
        Check(camera.FieldOfView == 77, "camera options");
        Check(camera.Transform.Position == new System.Numerics.Vector3(1, 2, 3), "scene context transform");
        Check(camera.SceneObject?.IsActive == true, "transactional scene activation");

        prototypes.Save("camera.prototype", PrototypeSerializer.Deserialize("""
            format: 1
            type: Vecxy.Rendering.Camera
            data:
              fieldOfView: 81
            """));
        prototypes.Save("camera-object.prototype", PrototypeSerializer.Deserialize("""
            format: 1
            type: Vecxy.Scene.SceneObject
            data:
              name: Camera prefab
              components:
                - id: camera
                  path: camera.prototype
            """));
        var rootObject = prototypes.Instantiate<SceneObject>("camera-object.prototype",
            new ScenePrototypeContext { Scene = scene });
        Check(rootObject.Name == "Camera prefab", "scene object prototype options");
        Check(rootObject.GetComponent<Camera>()?.FieldOfView == 81, "component composition");

        prototypes.Save("cycle.prototype", PrototypeSerializer.Deserialize("""
            format: 1
            type: Vecxy.Scene.SceneObject
            data:
              children:
                - id: recursive
                  path: cycle.prototype
            """));
        var count = scene.Objects.Count;
        AssertThrows<PrototypeInstantiationException>(() => prototypes.Instantiate<SceneObject>(
            "cycle.prototype", new ScenePrototypeContext { Scene = scene }), "composition cycle");
        Check(scene.Objects.Count == count, "scene rollback after nested failure");
    }
    finally
    {
        scene.Unload();
        prototypes.Dispose();
        assets.Dispose();
    }
}

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"Test failed: {name}");
}

static void AssertThrows<TException>(Action action, string name) where TException : Exception
{
    try { action(); }
    catch (TException) { return; }
    throw new InvalidOperationException($"Test failed: {name} did not throw {typeof(TException).Name}");
}

public sealed class Widget
{
    public string Name { get; private set; } = string.Empty;
    public int Count { get; private set; }

    public sealed class Prototype : APrototype<Widget, Prototype.Options>
    {
        public sealed class Options
        {
            public string Name { get; set; } = string.Empty;
            public int Count { get; set; }
        }

        protected override Widget Instantiate(IPrototypeContext context) => new();
        protected override void Configure(Widget target, Options options)
        { target.Name = options.Name; target.Count = options.Count; }
    }
}

public sealed class WidgetContext : IPrototypeContext;
public sealed class WidgetPrototypeSystem : APrototypeSystem<Widget, WidgetContext>;
public sealed class EmptyScene : IScene;
