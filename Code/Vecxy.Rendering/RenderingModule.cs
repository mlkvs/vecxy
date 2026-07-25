using System.Numerics;
using Autofac;
using Silk.NET.OpenGL;
using Vecxy.Assets;
using Vecxy.Diagnostics;
using Vecxy.Kernel;
using Vecxy.Scene;

namespace Vecxy.Rendering;

public interface IRenderer
{
    RenderingStatistics Statistics { get; }

    GameView CreateGameView(
        IRenderTarget? target = null);

    void DestroyGameView(GameView view);

    Mesh CreateQuad();

    SceneObject SpawnModel(
        Vecxy.Scene.Scene scene,
        AssetRef<ModelAsset> model,
        AssetRef<MaterialAsset> material,
        string? name = null);
}

public sealed class RenderingModule(
    GraphicsDevice device,
    BackbufferRenderTarget backbuffer,
    MaterialLibrary materials,
    MeshLibrary meshes,
    RenderingStatistics statistics,
    ImGuiOverlay overlay,
    ISceneManager scenes)
    :
        IModule,
        IModule.IUpdatable,
        IModule.IRenderable,
        IRenderer
{
    public sealed class Definition :
        AModuleDefinition<RenderingModule>
    {
        protected override IReadOnlyList<Type> Exports =>
            [typeof(IRenderer)];

        protected override void RegisterModule(
            ContainerBuilder builder)
        {
            
            builder
                .RegisterType<RenderingModule>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<GraphicsDevice>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<BackbufferRenderTarget>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<ShaderCompiler>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<ShaderLibrary>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<TextureLibrary>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<MaterialLibrary>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<MeshLibrary>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<RenderingStatistics>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<ImGuiOverlay>()
                .AsSelf()
                .SingleInstance();
        }
    }

    private readonly List<GameView> _views = [];

    public RenderingStatistics Statistics => statistics;

    public void OnInitialize()
    {
        device.GL.Enable(EnableCap.DepthTest);
        device.GL.DepthFunc(DepthFunction.Less);
        overlay.Initialize();
    }

    public void OnUpdate(float deltaTime)
    {
        var activeViews =
            _views.Count(view => view.Enabled) +
            (FindActiveCamera() is null ? 0 : 1);

        statistics.BeginFrame(
            deltaTime,
            activeViews);
        overlay.BeginFrame(deltaTime);
    }

    public void OnRender()
    {
        var presentedTargets = new HashSet<IRenderTarget>();

        RenderSubmittedViews(presentedTargets);

        if (RenderActiveScene())
            presentedTargets.Add(backbuffer);

        backbuffer.Bind(device);
        device.GL.Disable(EnableCap.DepthTest);
        overlay.Render(statistics);
        presentedTargets.Add(backbuffer);

        foreach (var target in presentedTargets)
            target.Present();
    }

    public GameView CreateGameView(
        IRenderTarget? target = null)
    {
        var view = new GameView(target ?? backbuffer);
        _views.Add(view);
        return view;
    }

    public void DestroyGameView(GameView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _views.Remove(view);
        view.Clear();
    }

    public Mesh CreateQuad()
    {
        ReadOnlySpan<float> vertices =
        [
            -0.5f,  0.5f, 0.0f, 1.0f,
            -0.5f, -0.5f, 0.0f, 0.0f,
             0.5f, -0.5f, 1.0f, 0.0f,
             0.5f,  0.5f, 1.0f, 1.0f
        ];

        ReadOnlySpan<uint> indices =
            [0, 1, 2, 2, 3, 0];

        return new Mesh(
            device,
            vertices,
            indices,
            4,
            new VertexAttribute(0, 2, 0),
            new VertexAttribute(1, 2, 2));
    }

    public SceneObject SpawnModel(
        Vecxy.Scene.Scene scene,
        AssetRef<ModelAsset> model,
        AssetRef<MaterialAsset> material,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(material);

        if (model.HasError)
        {
            throw new InvalidOperationException(
                $"Cannot spawn failed model '{model.Metadata.Path}'.",
                model.Error);
        }

        var asset = model.Value;
        var rootName = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(
                model.Metadata.Path)
            : name;
        var root = scene.CreateObject(rootName);

        try
        {
            foreach (var rootNode in asset.RootNodes)
            {
                SpawnNode(
                    scene,
                    root,
                    model,
                    material,
                    asset,
                    rootNode);
            }

            return root;
        }
        catch
        {
            root.Destroy();
            throw;
        }
    }

    private static void SpawnNode(
        Vecxy.Scene.Scene scene,
        SceneObject parent,
        AssetRef<ModelAsset> model,
        AssetRef<MaterialAsset> material,
        ModelAsset asset,
        int nodeIndex)
    {
        var node = asset.Nodes[nodeIndex];
        var sceneObject = scene.CreateObject(node.Name);
        sceneObject.SetParent(
            parent,
            worldPositionStays: false);
        sceneObject.Transform.LocalMatrix =
            node.LocalTransform;

        if (node.MeshIndex is { } meshIndex)
        {
            var renderer =
                sceneObject.AddComponent<MeshRenderer>();
            renderer.SetMesh(
                model,
                meshIndex,
                material);
        }

        foreach (var childIndex in node.Children)
        {
            SpawnNode(
                scene,
                sceneObject,
                model,
                material,
                asset,
                childIndex);
        }
    }

    private void RenderSubmittedViews(
        ISet<IRenderTarget> presentedTargets)
    {
        device.GL.Disable(EnableCap.DepthTest);

        foreach (var view in _views.Where(view => view.Enabled))
        {
            view.Target.Bind(device);
            device.GL.ClearColor(
                view.ClearColor.X,
                view.ClearColor.Y,
                view.ClearColor.Z,
                view.ClearColor.W);
            device.GL.Clear(
                ClearBufferMask.ColorBufferBit);

            var aspectCorrection =
                (float)view.Target.Height /
                Math.Max(1, view.Target.Width);
            var viewTransform =
                Matrix4x4.CreateScale(
                    aspectCorrection,
                    1.0f,
                    1.0f);

            foreach (var item in view.Items
                         .Where(item => item.Enabled)
                         .OrderBy(item => item.Phase))
            {
                var material =
                    materials.Get(item.Material);
                var shader =
                    material.Bind(item.Material);
                shader.Set(
                    "uTransform",
                    item.Transform * viewTransform);
                item.Mesh.Draw();
                statistics.RecordDraw();
            }

            presentedTargets.Add(view.Target);
        }
    }

    private bool RenderActiveScene()
    {
        var scene = scenes.ActiveScene;
        var camera = FindActiveCamera();

        if (scene is null || camera is null)
            return false;

        backbuffer.Bind(device);
        device.GL.Enable(EnableCap.DepthTest);
        device.GL.DepthMask(true);
        device.GL.ClearColor(
            camera.ClearColor.X,
            camera.ClearColor.Y,
            camera.ClearColor.Z,
            camera.ClearColor.W);
        device.GL.Clear(
            ClearBufferMask.ColorBufferBit |
            ClearBufferMask.DepthBufferBit);

        var aspectRatio =
            Math.Max(1, backbuffer.Width) /
            (float)Math.Max(1, backbuffer.Height);
        var viewProjection =
            camera.ViewMatrix *
            camera.GetProjectionMatrix(aspectRatio);

        var renderers = scene.Objects
            .Where(sceneObject => sceneObject.IsActive)
            .Select(sceneObject =>
                sceneObject.GetComponent<MeshRenderer>())
            .Where(renderer =>
                renderer is
                {
                    IsActive: true,
                    IsConfigured: true
                })
            .OrderBy(renderer => renderer!.Phase);

        foreach (var renderer in renderers)
        {
            Draw(renderer!, viewProjection);
        }

        return true;
    }

    private void Draw(
        MeshRenderer renderer,
        Matrix4x4 viewProjection)
    {
        if (!renderer.Model.IsLoaded)
            return;

        try
        {
            var modelTransform =
                renderer.Transform.WorldMatrix;
            var material =
                materials.Get(renderer.Material);
            var shader =
                material.Bind(renderer.Material);

            shader.Set("uModel", modelTransform);
            shader.Set(
                "uTransform",
                modelTransform * viewProjection);

            foreach (var mesh in meshes.Get(
                         renderer.Model,
                         renderer.MeshIndex))
            {
                mesh.Draw();
                statistics.RecordDraw();
            }
        }
        catch (Exception exception)
        {
            Logger.Error(
                exception,
                $"Could not render '{renderer.SceneObject?.Name ?? "destroyed object"}'.");
        }
    }

    private Camera? FindActiveCamera()
    {
        var scene = scenes.ActiveScene;
        if (scene is null)
            return null;

        return scene.Objects
            .Where(sceneObject => sceneObject.IsActive)
            .Select(sceneObject =>
                sceneObject.GetComponent<Camera>())
            .Where(camera =>
                camera is { IsActive: true })
            .OrderByDescending(camera =>
                camera!.Priority)
            .FirstOrDefault();
    }

    public void OnShutdown()
    {
        foreach (var view in _views)
            view.Clear();

        _views.Clear();
        meshes.Dispose();
        materials.Clear();
    }

    public void Dispose()
    {
    }
}
