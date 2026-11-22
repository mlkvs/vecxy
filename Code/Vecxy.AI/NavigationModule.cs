using System.Numerics;
using Autofac;
using Vecxy.Assets;
using Vecxy.Kernel;
using Vecxy.Pathfinding;
using Vecxy.Rendering;
using Vecxy.Scene;

namespace Vecxy.AI;

public interface INavigation
{
    bool DebugDraw { get; set; }
    NavigationBakeInput Capture(SceneInstance scene, NavigationBakeSettings settings);
    NavigationMap Bake(SceneInstance scene, NavigationBakeSettings settings);
    NavigationMap SetMap(SceneInstance scene, NavigationData data);
    NavigationMap Load(SceneInstance scene, string assetPath);
    NavigationMap? GetMap(SceneInstance scene);
}

public sealed class NavigationModule(
    IAssetsManager assets, IRenderWorldStage rendering, IGraphicsDeviceProvider graphics,
    NavigationSceneSystem system, NavigationModule.Options options) : IModule, INavigation
{
    public sealed record Options
    {
        public bool DebugDraw { get; init; }
    }

    public sealed class Definition(Options? options = null) : AModuleDefinition<NavigationModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(INavigation)];

        public override void RegisterGlobal(ContainerBuilder builder) =>
            builder.RegisterType<NavigationSceneSystem>().AsSelf().As<ISceneSystem>().SingleInstance();

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder.RegisterInstance(options ?? new Options());
            builder.RegisterType<NavigationModule>().AsSelf().SingleInstance();
        }
    }

    private NavigationDebugRenderer? _debug;
    public bool DebugDraw { get; set; } = options.DebugDraw;

    public NavigationBakeInput Capture(SceneInstance scene, NavigationBakeSettings settings) =>
        NavigationGeometry.Capture(scene, settings);

    public NavigationMap Bake(SceneInstance scene, NavigationBakeSettings settings)
    {
        var input = Capture(scene, settings);
        // Keep code-authored blockers live, so enabling or moving them updates the current map.
        return SetMap(scene, NavigationBaker.Bake(input with { Blockers = null, Obstacles = null }));
    }

    public NavigationMap SetMap(SceneInstance scene, NavigationData data) => system.SetMap(scene, data);

    public NavigationMap Load(SceneInstance scene, string assetPath)
    {
        var id = assets.Find(assetPath);
        using var stream = new MemoryStream(assets.ReadAllBytes(new AssetHandle(id)), writable: false);
        return SetMap(scene, NavigationData.Read(stream));
    }

    public NavigationMap? GetMap(SceneInstance scene) => system.Get(scene)?.Map;

    public void OnInitialize() => rendering.RegisterWorld(Draw);

    private void Draw(RenderWorldContext context)
    {
        if (!DebugDraw || system.Get(context.Scene) is not { Map: not null } state)
            return;
        (_debug ??= new NavigationDebugRenderer(graphics.GraphicsDevice.GL)).Draw(context, state);
    }

    public void OnShutdown()
    {
        rendering.UnregisterWorld(Draw);
        _debug?.Dispose();
        _debug = null;
    }

    public void Dispose() => OnShutdown();
}

public sealed class NavigationSceneSystem : ASceneSystem
{
    private readonly Dictionary<SceneInstance, NavigationSceneState> _scenes = [];

    public override void OnSceneAttached(SceneInstance scene) => _scenes.Add(scene, new());
    public override void OnSceneDetached(SceneInstance scene) => _scenes.Remove(scene);

    internal NavigationSceneState? Get(SceneInstance scene) => _scenes.GetValueOrDefault(scene);

    internal NavigationMap SetMap(SceneInstance scene, NavigationData data)
    {
        var state = _scenes[scene];
        var map = new NavigationMap(data);
        state.Map = map;
        state.Coverage.Clear();
        state.UpdateObstacles();
        return map;
    }

    public override void OnComponentAdded(SceneObject obj, AComponent component)
    {
        var state = _scenes[obj.SceneInstance];
        if (component is NavigationAgent agent)
            state.Agents.Add(agent);
        if (component is NavigationObstacle or NavigationBlocker)
            state.Obstacles.Add(component);
    }

    public override void OnComponentRemoved(SceneObject obj, AComponent component)
    {
        var state = _scenes[obj.SceneInstance];
        if (component is NavigationAgent agent)
            state.Agents.Remove(agent);
        if (state.Obstacles.Remove(component))
            state.RemoveCoverage(component);
    }

    public override void Update(SceneInstance scene, float deltaTime)
    {
        var state = _scenes[scene];
        state.UpdateObstacles();
        foreach (var agent in state.Agents)
            if (agent.IsActive)
                agent.Advance(state.Map, deltaTime);
    }
}

internal sealed class NavigationSceneState
{
    internal sealed record CoverageState(NavigationBox[] Boxes, HashSet<int> Cells);
    private static readonly HashSet<int> Empty = [];

    public NavigationMap? Map { get; set; }
    public HashSet<NavigationAgent> Agents { get; } = [];
    public HashSet<AComponent> Obstacles { get; } = [];
    public Dictionary<AComponent, CoverageState> Coverage { get; } = [];

    public void UpdateObstacles()
    {
        if (Map is not { } map)
            return;
        foreach (var obstacle in Obstacles)
        {
            if (!obstacle.Enabled || obstacle.SceneObject is not { } owner || !NavigationGeometry.Enabled(owner))
            {
                RemoveCoverage(obstacle);
                continue;
            }

            var boxes = obstacle switch
            {
                NavigationBlocker blocker => [NavigationGeometry.BlockerBox(blocker)],
                NavigationObstacle component => NavigationGeometry.ObstacleBoxes(component),
                _ => Array.Empty<NavigationBox>()
            };
            var previous = Coverage.GetValueOrDefault(obstacle);
            if (previous is not null && boxes.AsSpan().SequenceEqual(previous.Boxes))
                continue;
            var cells = boxes.SelectMany(map.Data.BlockedCells).ToHashSet();
            map.UpdateObstacle(previous?.Cells ?? Empty, cells);
            Coverage[obstacle] = new(boxes, cells);
        }
    }

    public void RemoveCoverage(AComponent obstacle)
    {
        if (Coverage.Remove(obstacle, out var previous))
            Map?.UpdateObstacle(previous.Cells, Empty);
    }
}
