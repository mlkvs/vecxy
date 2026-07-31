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
    bool Wireframe { get; set; }
    bool ScenePresentationEnabled { get; set; }
    nint SceneTextureId { get; }

    GameView CreateGameView(
        IRenderTarget? target = null);

    void DestroyGameView(GameView view);
    void SetSceneViewportSize(int width, int height);

    Mesh CreateQuad();
}

public interface IMeshResolver
{
    IReadOnlyList<Mesh> GetMeshes(Model model, int meshIndex);
}

public interface IRenderOverlayStage
{
    void RegisterOverlay(Action draw);
    void UnregisterOverlay(Action draw);
}

public sealed class RenderingModule(
    IAssetsManager assets,
    GraphicsDevice device,
    BackbufferRenderTarget backbuffer,
    ShaderLibrary shaders,
    MaterialLibrary materials,
    MeshLibrary meshes,
    RenderingStatistics statistics,
    ISceneManager scenes)
    :
        IModule,
        IModule.IUpdatable,
        IModule.IRenderable,
        IRenderer,
        IMeshResolver,
        IRenderOverlayStage
{
    private const int MaxPointLights = 8;
    private const int MaxSpotLights = 8;
    private const int MaxDirectionalLights = 4;

    public sealed class Definition :
        AModuleDefinition<RenderingModule>
    {
        protected override IReadOnlyList<Type> Exports =>
            [
                typeof(IRenderer),
                typeof(IMeshResolver),
                typeof(IRenderOverlayStage)
            ];

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
        }
    }

    private readonly List<Action> _overlayCallbacks = [];
    private readonly List<GameView> _views = [];
    private AssetRef<ShaderAsset>? _litShader;
    private AssetRef<ShaderAsset>? _skyboxShader;
    private AssetRef<ShaderAsset>? _copyPostShader;
    private SceneRenderTarget? _sceneTarget;
    private SceneRenderTarget? _postProcessTargetA;
    private SceneRenderTarget? _postProcessTargetB;
    private SceneRenderTarget? _presentedSceneTarget;
    private Mesh? _fullscreenQuad;
    private Mesh? _skyboxCube;
    private SkyboxRuntime? _skybox;
    private float _time;
    private int _sceneViewportWidth;
    private int _sceneViewportHeight;

    public RenderingStatistics Statistics => statistics;
    public bool Wireframe { get; set; }
    public bool ScenePresentationEnabled { get; set; } = true;
    public nint SceneTextureId =>
        _presentedSceneTarget is null
            ? 0
            : (nint)_presentedSceneTarget.ColorTextureHandle;

    public void OnInitialize()
    {
        _litShader = assets.Load<ShaderAsset>("Shaders/Lit.glsl");
        _skyboxShader = assets.Load<ShaderAsset>("Shaders/Skybox.glsl");
        _copyPostShader = assets.Load<ShaderAsset>("Shaders/PostProcessing/Copy.glsl");
        _sceneTarget = new SceneRenderTarget(device);
        _postProcessTargetA = new SceneRenderTarget(device);
        _postProcessTargetB = new SceneRenderTarget(device);
        _fullscreenQuad = CreateQuad();
        _skyboxCube = CreateSkyboxCube();
        _skybox = new SkyboxRuntime();
        device.GL.Enable(EnableCap.DepthTest);
        device.GL.DepthFunc(DepthFunction.Less);
    }

    public void OnUpdate(float deltaTime)
    {
        var activeViews =
            _views.Count(view => view.Enabled) +
            (FindActiveCamera() is null ? 0 : 1);

        statistics.BeginFrame(
            deltaTime,
            activeViews);
        _time += deltaTime;
    }

    public void OnRender()
    {
        device.GL.PolygonMode(
            TriangleFace.FrontAndBack,
            Wireframe ? PolygonMode.Line : PolygonMode.Fill);

        var presentedTargets = new HashSet<IRenderTarget>();
        var renderSceneToViewport =
            _sceneViewportWidth > 0 &&
            _sceneViewportHeight > 0;

        RenderSubmittedViews(presentedTargets);

        if (ScenePresentationEnabled && RenderActiveScene())
            presentedTargets.Add(backbuffer);

        backbuffer.Bind(device);
        device.GL.Disable(EnableCap.DepthTest);
        if (renderSceneToViewport || !presentedTargets.Contains(backbuffer))
        {
            device.GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            device.GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        foreach (var draw in _overlayCallbacks.ToArray())
            draw();

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

    public void SetSceneViewportSize(int width, int height)
    {
        _sceneViewportWidth = Math.Max(0, width);
        _sceneViewportHeight = Math.Max(0, height);
    }

    public void RegisterOverlay(Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);

        if (!_overlayCallbacks.Contains(draw))
            _overlayCallbacks.Add(draw);
    }

    public void UnregisterOverlay(Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        _overlayCallbacks.Remove(draw);
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
            new Vector3(-0.5f, -0.5f, 0.0f),
            new Vector3(0.5f, 0.5f, 0.0f),
            "Fullscreen Quad",
            new VertexAttribute(0, 2, 0),
            new VertexAttribute(1, 2, 2));
    }

    private Mesh CreateSkyboxCube()
    {
        ReadOnlySpan<float> vertices =
        [
            -1.0f,  1.0f, -1.0f,
            -1.0f, -1.0f, -1.0f,
             1.0f, -1.0f, -1.0f,
             1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f,  1.0f,
            -1.0f, -1.0f,  1.0f,
             1.0f, -1.0f,  1.0f,
             1.0f,  1.0f,  1.0f
        ];

        ReadOnlySpan<uint> indices =
        [
            0, 1, 2, 2, 3, 0,
            3, 2, 6, 6, 7, 3,
            7, 6, 5, 5, 4, 7,
            4, 5, 1, 1, 0, 4,
            4, 0, 3, 3, 7, 4,
            1, 5, 6, 6, 2, 1
        ];

        return new Mesh(
            device,
            vertices,
            indices,
            3,
            new Vector3(-1.0f),
            new Vector3(1.0f),
            "Skybox Cube",
            new VertexAttribute(0, 3, 0));
    }

    public SceneObject InstantiateModel(
        Vecxy.Scene.SceneInstance sceneInstance,
        Model model,
        string? name = null,
        Material? fallbackMaterial = null)
    {
        ArgumentNullException.ThrowIfNull(sceneInstance);
        ArgumentNullException.ThrowIfNull(model);
        var rootName = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(
                model.Source.Metadata.Path)
            : name;
        var root = sceneInstance.CreateObject(rootName);

        try
        {
            BuildModelHierarchy(
                root,
                model,
                fallbackMaterial);
            return root;
        }
        catch
        {
            root.Destroy();
            throw;
        }
    }

    public IReadOnlyList<Mesh> GetMeshes(Model model, int meshIndex)
    {
        ArgumentNullException.ThrowIfNull(model);
        return meshes.Get(model, meshIndex);
    }

    internal void BuildModelHierarchy(
        SceneObject root,
        Model model,
        Material? fallbackMaterial)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(model);

        foreach (var rootNode in model.RootNodes)
        {
            SpawnNode(
                root.SceneInstance,
                root,
                model,
                fallbackMaterial,
                rootNode);
        }
    }

    private void SpawnNode(
        Vecxy.Scene.SceneInstance sceneInstance,
        SceneObject parent,
        Model model,
        Material? fallbackMaterial,
        int nodeIndex)
    {
        var node = model.Nodes[nodeIndex];
        var sceneObject = sceneInstance.CreateObject(node.Name);
        sceneObject.SetParent(
            parent,
            worldPositionStays: false);
        sceneObject.Transform.LocalMatrix =
            node.LocalTransform;

        if (node.MeshIndex is { } meshIndex)
        {
            var primitiveMeshes =
                meshes.Get(model, meshIndex);

            if (primitiveMeshes.Count == 1)
            {
                var renderer =
                    sceneObject.AddComponent<MeshRenderer>();
                renderer.SetMesh(
                    primitiveMeshes[0],
                    ResolveMaterial(
                        model,
                        meshIndex,
                        0,
                        fallbackMaterial));
            }
            else
            {
                for (var primitiveIndex = 0;
                     primitiveIndex < primitiveMeshes.Count;
                     primitiveIndex++)
                {
                    var primitiveObject =
                        sceneInstance.CreateObject(
                            $"{node.Name} Primitive {primitiveIndex}");
                    primitiveObject.SetParent(
                        sceneObject,
                        worldPositionStays: false);

                    var renderer =
                        primitiveObject.AddComponent<MeshRenderer>();
                    renderer.SetMesh(
                        primitiveMeshes[primitiveIndex],
                        ResolveMaterial(
                            model,
                            meshIndex,
                            primitiveIndex,
                            fallbackMaterial));
                }
            }
        }

        if (node.LightIndex is { } lightIndex)
        {
            var light = model.Lights[lightIndex];

            switch (light.Kind)
            {
                case EModelLightKind.Directional:
                {
                    var directionalLight =
                        sceneObject.AddComponent<DirectionalLight>();
                    directionalLight.Color = light.Color;
                    directionalLight.Intensity = light.Intensity;
                    break;
                }

                case EModelLightKind.Point:
                {
                    var pointLight =
                        sceneObject.AddComponent<PointLight>();
                    pointLight.Color = light.Color;
                    pointLight.Intensity = light.Intensity;
                    pointLight.Range = light.Range;
                    break;
                }

                case EModelLightKind.Spot:
                {
                    var spotLight =
                        sceneObject.AddComponent<SpotLight>();
                    spotLight.Color = light.Color;
                    spotLight.Intensity = light.Intensity;
                    spotLight.Range = light.Range;
                    spotLight.InnerConeAngle = light.InnerConeAngle;
                    spotLight.OuterConeAngle = light.OuterConeAngle;
                    break;
                }
            }
        }

        foreach (var childIndex in node.Children)
        {
            SpawnNode(
                sceneInstance,
                sceneObject,
                model,
                fallbackMaterial,
                childIndex);
        }
    }

    private Material ResolveMaterial(
        Model model,
        int meshIndex,
        int primitiveIndex,
        Material? fallbackMaterial)
    {
        if (_litShader is null)
            return CloneFallback(fallbackMaterial) ?? CreateDefaultMaterial(model);

        if (meshIndex < 0 || meshIndex >= model.Meshes.Count)
            return CloneFallback(fallbackMaterial) ?? CreateDefaultMaterial(model);

        var primitives = model.Meshes[meshIndex].Primitives;
        if (primitiveIndex < 0 || primitiveIndex >= primitives.Count)
            return CloneFallback(fallbackMaterial) ?? CreateDefaultMaterial(model);

        var materialIndex = primitives[primitiveIndex].MaterialIndex;
        if (materialIndex is null ||
            materialIndex < 0 ||
            materialIndex >= model.Materials.Count)
        {
            return CloneFallback(fallbackMaterial) ?? CreateDefaultMaterial(model);
        }

        var source = model.Materials[materialIndex.Value];
        var parameters =
            new Dictionary<string, Vecxy.Assets.MaterialParameter>(
            StringComparer.Ordinal)
        {
            ["uColor"] = new VectorMaterialParameter(source.BaseColor),
            ["uTint"] = new VectorMaterialParameter(Vector4.One)
        };

        if (source.BaseColorTexture is not null)
        {
            parameters["uTexture"] =
                new EmbeddedTextureMaterialParameter(
                    source.BaseColorTexture);
        }

        return new Material(
            _litShader,
            parameters,
            $"{model.Source.Metadata.Path}::{source.Name}",
            source.BaseColor.W < 0.999f
                ? EMaterialSurface.Transparent
                : EMaterialSurface.Opaque);
    }

    private Material CreateDefaultMaterial(Model model)
    {
        if (_litShader is null)
            throw new InvalidOperationException(
                "Default material cannot be created because Lit shader is unavailable.");

        var parameters =
            new Dictionary<string, Vecxy.Assets.MaterialParameter>(
                StringComparer.Ordinal)
            {
                ["uColor"] = new VectorMaterialParameter(Vector4.One),
                ["uTint"] = new VectorMaterialParameter(Vector4.One)
            };

        return new Material(
            _litShader,
            parameters,
            $"{model.Source.Metadata.Path}::Default");
    }

    private static Material? CloneFallback(Material? material)
    {
        return material?.Clone();
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
                var shader =
                    materials.Bind(item.Material);
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

        var renderWidth =
            _sceneViewportWidth > 0
                ? _sceneViewportWidth
                : backbuffer.Width;
        var renderHeight =
            _sceneViewportHeight > 0
                ? _sceneViewportHeight
                : backbuffer.Height;

        _sceneTarget ??= new SceneRenderTarget(device);
        _sceneTarget.EnsureSize(renderWidth, renderHeight);
        _sceneTarget.Bind(device);
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

        DrawSkybox(camera, scene.Lighting.Skybox);

        var aspectRatio =
            Math.Max(1, renderWidth) /
            (float)Math.Max(1, renderHeight);
        var viewProjection =
            camera.ViewMatrix *
            camera.GetProjectionMatrix(aspectRatio);
        var lighting = CollectLighting(scene);

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
            .Select(renderer => renderer!)
            .ToArray();

        foreach (var renderer in renderers
                     .Where(renderer => GetEffectiveRenderPhase(renderer) != ERenderPhase.Transparent)
                     .OrderBy(GetEffectiveRenderPhase))
        {
            Draw(renderer, camera, viewProjection, lighting);
        }

        foreach (var renderer in renderers
                     .Where(renderer => GetEffectiveRenderPhase(renderer) == ERenderPhase.Transparent)
                     .OrderByDescending(renderer =>
                         Vector3.DistanceSquared(
                             renderer.Transform.WorldPosition,
                             camera.Transform.WorldPosition)))
        {
            Draw(renderer, camera, viewProjection, lighting);
        }

        PresentScene(
            camera,
            renderWidth,
            renderHeight,
            _sceneViewportWidth > 0 && _sceneViewportHeight > 0);

        return true;
    }

    private void DrawSkybox(
        Camera camera,
        SceneSkyboxSettings settings)
    {
        if (!settings.Enabled ||
            !settings.HasAllFaces ||
            _skyboxCube is null ||
            _skyboxShader is null ||
            _skyboxShader.HasError)
        {
            return;
        }

        var cubemap = _skybox?.Resolve(
            assets,
            device,
            settings);

        if (cubemap is null)
            return;

        var shader = shaders.Get(_skyboxShader);
        var view = camera.ViewMatrix;
        view.M41 = 0.0f;
        view.M42 = 0.0f;
        view.M43 = 0.0f;

        var aspectRatio =
            Math.Max(1, _sceneTarget?.Width ?? backbuffer.Width) /
            (float)Math.Max(1, _sceneTarget?.Height ?? backbuffer.Height);
        var viewProjection =
            view *
            camera.GetProjectionMatrix(aspectRatio);

        device.GL.DepthMask(false);
        device.GL.DepthFunc(DepthFunction.Lequal);

        shader.Bind();
        cubemap.Bind(0);
        shader.Set("uSkybox", 0);
        shader.Set("uSkyboxRotation", CreateSkyboxRotation(settings.Rotation));
        shader.Set("uViewProjection", viewProjection);
        shader.Set("uSkyboxTint", settings.Tint);
        shader.Set("uSkyboxExposure", settings.Exposure);
        _skyboxCube.Draw();
        statistics.RecordDraw();

        device.GL.DepthFunc(DepthFunction.Less);
        device.GL.DepthMask(true);
    }

    private static Matrix4x4 CreateSkyboxRotation(Vector3 rotationDegrees)
    {
        const float degToRad = MathF.PI / 180.0f;
        return Matrix4x4.CreateFromYawPitchRoll(
            rotationDegrees.Y * degToRad,
            rotationDegrees.X * degToRad,
            rotationDegrees.Z * degToRad);
    }

    private void PresentScene(
        Camera camera,
        int renderWidth,
        int renderHeight,
        bool renderToSceneViewport)
    {
        if (_sceneTarget is null ||
            _postProcessTargetA is null ||
            _postProcessTargetB is null ||
            _fullscreenQuad is null)
        {
            return;
        }

        var effects = GetActivePostProcessEffects(camera);
        _presentedSceneTarget = null;

        if (renderToSceneViewport)
        {
            if (effects.Length == 0)
            {
                _presentedSceneTarget = _sceneTarget;
                return;
            }

            _postProcessTargetA.EnsureSize(renderWidth, renderHeight);
            _postProcessTargetB.EnsureSize(renderWidth, renderHeight);

            SceneRenderTarget input = _sceneTarget;
            SceneRenderTarget output = _postProcessTargetA;

            for (var index = 0; index < effects.Length; index++)
            {
                var effect = effects[index];
                var shader = shaders.Get(effect.GetShaderAsset(assets));
                var isLast = index == effects.Length - 1;

                DrawPostProcessPass(
                    shader,
                    input,
                    output,
                    camera,
                    effect,
                    new Vector2(renderWidth, renderHeight));

                if (isLast)
                {
                    _presentedSceneTarget = output;
                    break;
                }

                if (ReferenceEquals(output, _postProcessTargetA))
                {
                    input = _postProcessTargetA;
                    output = _postProcessTargetB;
                }
                else
                {
                    input = _postProcessTargetB;
                    output = _postProcessTargetA;
                }
            }

            return;
        }

        if (effects.Length == 0)
        {
            DrawPostProcessPass(
                ResolveCopyPostProcessShader(),
                _sceneTarget,
                backbuffer,
                camera,
                null,
                new Vector2(renderWidth, renderHeight));
            return;
        }

        _postProcessTargetA.EnsureSize(renderWidth, renderHeight);
        _postProcessTargetB.EnsureSize(renderWidth, renderHeight);

        SceneRenderTarget backbufferInput = _sceneTarget;
        SceneRenderTarget backbufferOutput = _postProcessTargetA;

        for (var index = 0; index < effects.Length; index++)
        {
            var effect = effects[index];
            var shader = shaders.Get(effect.GetShaderAsset(assets));
            var isLast = index == effects.Length - 1;

            if (isLast)
            {
                DrawPostProcessPass(
                    shader,
                    backbufferInput,
                    backbuffer,
                    camera,
                    effect,
                    new Vector2(renderWidth, renderHeight));
                break;
            }

            DrawPostProcessPass(
                shader,
                backbufferInput,
                backbufferOutput,
                camera,
                effect,
                new Vector2(renderWidth, renderHeight));

            if (ReferenceEquals(backbufferOutput, _postProcessTargetA))
            {
                backbufferInput = _postProcessTargetA;
                backbufferOutput = _postProcessTargetB;
            }
            else
            {
                backbufferInput = _postProcessTargetB;
                backbufferOutput = _postProcessTargetA;
            }
        }
    }

    private Shader ResolveCopyPostProcessShader()
    {
        return _copyPostShader is { HasError: false }
            ? shaders.Get(_copyPostShader)
            : shaders.GetFallback();
    }

    private void DrawPostProcessPass(
        Shader shader,
        SceneRenderTarget input,
        IRenderTarget output,
        Camera camera,
        APostProcessEffect? effect,
        Vector2 resolution)
    {
        output.Bind(device);
        device.GL.Disable(EnableCap.DepthTest);
        device.GL.DepthMask(false);
        device.GL.ClearColor(
            camera.ClearColor.X,
            camera.ClearColor.Y,
            camera.ClearColor.Z,
            camera.ClearColor.W);
        device.GL.Clear(ClearBufferMask.ColorBufferBit);

        shader.Bind();
        input.BindColorTexture(0);
        shader.Set("uSceneTexture", 0);

        var context = new PostProcessContext(
            resolution,
            _time,
            camera);

        shader.Set("uResolution", context.Resolution);
        shader.Set("uTime", context.Time);

        effect?.Apply(shader, context);

        shader.Set(
            "uTransform",
            Matrix4x4.CreateScale(2.0f, 2.0f, 1.0f));
        _fullscreenQuad!.Draw();
        statistics.RecordDraw();
        device.GL.DepthMask(true);
    }

    private APostProcessEffect[] GetActivePostProcessEffects(
        Camera camera)
    {
        if (!camera.UsePostProcessing)
            return [];

        var postProcessing = FindActivePostProcessing();
        if (postProcessing is null)
            return [];

        return postProcessing.EnumerateEffects()
            .Where(effect => effect.Enabled)
            .OrderBy(effect => effect.Order)
            .ToArray();
    }

    private void Draw(
        MeshRenderer renderer,
        Camera camera,
        Matrix4x4 viewProjection,
        SceneLighting lighting)
    {
        try
        {
            var modelTransform =
                renderer.Transform.WorldMatrix;
            ApplyMaterialState(renderer.Material);
            var shader =
                materials.Bind(renderer.Material);

            ApplyLighting(shader, camera, lighting);
            shader.Set("uModel", modelTransform);
            shader.Set(
                "uTransform",
                modelTransform * viewProjection);

            renderer.Mesh.Draw();
            statistics.RecordDraw();
        }
        catch (Exception exception)
        {
            Logger.Error(
                exception,
                $"Could not render '{renderer.SceneObject?.Name ?? "destroyed object"}'.");
        }
    }

    private void ApplyMaterialState(Material material)
    {
        switch (material.Surface)
        {
            case EMaterialSurface.Transparent:
                device.GL.Enable(EnableCap.Blend);
                device.GL.BlendFunc(
                    BlendingFactor.SrcAlpha,
                    BlendingFactor.OneMinusSrcAlpha);
                device.GL.DepthMask(false);
                break;

            case EMaterialSurface.Cutout:
            case EMaterialSurface.Opaque:
            default:
                device.GL.Disable(EnableCap.Blend);
                device.GL.DepthMask(true);
                break;
        }
    }

    private static ERenderPhase GetEffectiveRenderPhase(MeshRenderer renderer)
    {
        return renderer.Material.Surface == EMaterialSurface.Transparent
            ? ERenderPhase.Transparent
            : renderer.Phase;
    }

    private static SceneLighting CollectLighting(
        Vecxy.Scene.SceneInstance sceneInstance)
    {
        var pointLights = new List<PointLightData>(MaxPointLights);
        var spotLights = new List<SpotLightData>(MaxSpotLights);
        var directionalLights =
            new List<DirectionalLightData>(MaxDirectionalLights);
        var global = new GlobalLightingData(
            sceneInstance.Lighting.AmbientSkyColor,
            sceneInstance.Lighting.AmbientGroundColor,
            sceneInstance.Lighting.AmbientIntensity,
            sceneInstance.Lighting.DirectLightIntensityScale,
            sceneInstance.Lighting.SpecularStrength,
            sceneInstance.Lighting.Exposure);
        FogData? fog = sceneInstance.Lighting.Fog.Enabled
            ? CreateFogData(sceneInstance.Lighting.Fog)
            : null;

        foreach (var sceneObject in sceneInstance.Objects)
        {
            if (!sceneObject.IsActive)
                continue;

            if (pointLights.Count < MaxPointLights &&
                sceneObject.GetComponent<PointLight>() is
                { IsActive: true } pointLight)
            {
                pointLights.Add(
                    new PointLightData(
                        pointLight.Transform.WorldPosition,
                        pointLight.Color,
                        pointLight.Intensity,
                        pointLight.Range));
            }

            if (spotLights.Count < MaxSpotLights &&
                sceneObject.GetComponent<SpotLight>() is
                { IsActive: true } spotLight)
            {
                spotLights.Add(
                    new SpotLightData(
                        spotLight.Transform.WorldPosition,
                        Vector3.Normalize(spotLight.Transform.Forward),
                        spotLight.Color,
                        spotLight.Intensity,
                        spotLight.Range,
                        MathF.Cos(spotLight.InnerConeAngle),
                        MathF.Cos(spotLight.OuterConeAngle)));
            }

            if (directionalLights.Count < MaxDirectionalLights &&
                sceneObject.GetComponent<DirectionalLight>() is
                { IsActive: true } directionalLight)
            {
                directionalLights.Add(
                    new DirectionalLightData(
                        Vector3.Normalize(
                            directionalLight.Transform.Forward),
                        directionalLight.Color,
                        directionalLight.Intensity));
            }
        }

        return new SceneLighting(
            global,
            directionalLights.ToArray(),
            pointLights.ToArray(),
            spotLights.ToArray(),
            fog);
    }

    private static FogData CreateFogData(SceneFogSettings fog)
    {
        return new FogData(
            fog.Mode,
            fog.Color,
            fog.StartDistance,
            fog.EndDistance,
            fog.Density,
            fog.HeightEnabled,
            fog.Height,
            fog.HeightFalloff,
            fog.VolumetricStrength);
    }

    private static void ApplyLighting(
        Shader shader,
        Camera camera,
        SceneLighting lighting)
    {
        var global = lighting.Global;
        var ambientSky = global.AmbientSkyColor * global.AmbientIntensity;
        var ambientGround = global.AmbientGroundColor * global.AmbientIntensity;

        shader.Set(
            "uCameraPosition",
            camera.Transform.WorldPosition);
        shader.Set("uAmbientSkyColor", ambientSky);
        shader.Set("uAmbientGroundColor", ambientGround);
        shader.Set("uExposure", global.Exposure);
        shader.Set("uSpecularStrength", global.SpecularStrength);

        if (lighting.Fog is { } fog)
        {
            shader.Set("uFogEnabled", 1);
            shader.Set("uFogMode", fog.Mode == EFogMode.Linear ? 0 : 1);
            shader.Set("uFogColor", fog.Color);
            shader.Set("uFogStart", fog.StartDistance);
            shader.Set("uFogEnd", fog.EndDistance);
            shader.Set("uFogDensity", fog.Density);
            shader.Set("uHeightFogEnabled", fog.HeightFogEnabled ? 1 : 0);
            shader.Set("uFogHeight", fog.Height);
            shader.Set("uFogHeightFalloff", fog.HeightFalloff);
            shader.Set("uFogVolumetricStrength", fog.VolumetricStrength);
        }
        else
        {
            shader.Set("uFogEnabled", 0);
            shader.Set("uFogMode", 0);
            shader.Set("uFogColor", Vector3.Zero);
            shader.Set("uFogStart", 0.0f);
            shader.Set("uFogEnd", 1.0f);
            shader.Set("uFogDensity", 0.0f);
            shader.Set("uHeightFogEnabled", 0);
            shader.Set("uFogHeight", 0.0f);
            shader.Set("uFogHeightFalloff", 0.0f);
            shader.Set("uFogVolumetricStrength", 0.0f);
        }

        shader.Set(
            "uDirectionalLightCount",
            lighting.DirectionalLights.Length);
        for (var index = 0; index < lighting.DirectionalLights.Length; index++)
        {
            var light = lighting.DirectionalLights[index];
            shader.Set(
                $"uDirectionalLights[{index}].direction",
                light.Direction);
            shader.Set(
                $"uDirectionalLights[{index}].color",
                light.Color);
            shader.Set(
                $"uDirectionalLights[{index}].intensity",
                light.Intensity * global.DirectLightIntensityScale);
        }

        shader.Set(
            "uPointLightCount",
            lighting.PointLights.Length);
        for (var index = 0; index < lighting.PointLights.Length; index++)
        {
            var light = lighting.PointLights[index];
            shader.Set(
                $"uPointLights[{index}].position",
                light.Position);
            shader.Set(
                $"uPointLights[{index}].color",
                light.Color);
            shader.Set(
                $"uPointLights[{index}].intensity",
                light.Intensity * global.DirectLightIntensityScale);
            shader.Set(
                $"uPointLights[{index}].range",
                light.Range);
        }

        shader.Set(
            "uSpotLightCount",
            lighting.SpotLights.Length);
        for (var index = 0; index < lighting.SpotLights.Length; index++)
        {
            var light = lighting.SpotLights[index];
            shader.Set(
                $"uSpotLights[{index}].position",
                light.Position);
            shader.Set(
                $"uSpotLights[{index}].direction",
                light.Direction);
            shader.Set(
                $"uSpotLights[{index}].color",
                light.Color);
            shader.Set(
                $"uSpotLights[{index}].intensity",
                light.Intensity * global.DirectLightIntensityScale);
            shader.Set(
                $"uSpotLights[{index}].range",
                light.Range);
            shader.Set(
                $"uSpotLights[{index}].innerConeCos",
                light.InnerConeCos);
            shader.Set(
                $"uSpotLights[{index}].outerConeCos",
                light.OuterConeCos);
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

    private PostProcessing? FindActivePostProcessing()
    {
        var scene = scenes.ActiveScene;
        if (scene is null)
            return null;

        return scene.Objects
            .Where(sceneObject => sceneObject.IsActive)
            .Select(sceneObject => sceneObject.GetComponent<PostProcessing>())
            .FirstOrDefault(component => component is { IsActive: true });
    }

    public void OnShutdown()
    {
        foreach (var view in _views)
            view.Clear();

        _views.Clear();
        _overlayCallbacks.Clear();
        assets.UnregisterImporter<Model>();
        assets.UnregisterImporter<Material>();
        _litShader?.Dispose();
        _litShader = null;
        _skyboxShader?.Dispose();
        _skyboxShader = null;
        _copyPostShader?.Dispose();
        _copyPostShader = null;
        _sceneTarget?.Dispose();
        _sceneTarget = null;
        _postProcessTargetA?.Dispose();
        _postProcessTargetA = null;
        _postProcessTargetB?.Dispose();
        _postProcessTargetB = null;
        _fullscreenQuad?.Dispose();
        _fullscreenQuad = null;
        _skyboxCube?.Dispose();
        _skyboxCube = null;
        _skybox?.Dispose();
        _skybox = null;
        meshes.Dispose();
        materials.Clear();
    }

    public void Dispose()
    {
    }

    private readonly record struct SceneLighting(
        GlobalLightingData Global,
        DirectionalLightData[] DirectionalLights,
        PointLightData[] PointLights,
        SpotLightData[] SpotLights,
        FogData? Fog);

    private readonly record struct GlobalLightingData(
        Vector3 AmbientSkyColor,
        Vector3 AmbientGroundColor,
        float AmbientIntensity,
        float DirectLightIntensityScale,
        float SpecularStrength,
        float Exposure);

    private readonly record struct DirectionalLightData(
        Vector3 Direction,
        Vector3 Color,
        float Intensity);

    private readonly record struct PointLightData(
        Vector3 Position,
        Vector3 Color,
        float Intensity,
        float Range);

    private readonly record struct SpotLightData(
        Vector3 Position,
        Vector3 Direction,
        Vector3 Color,
        float Intensity,
        float Range,
        float InnerConeCos,
        float OuterConeCos);

    private readonly record struct FogData(
        EFogMode Mode,
        Vector3 Color,
        float StartDistance,
        float EndDistance,
        float Density,
        bool HeightFogEnabled,
        float Height,
        float HeightFalloff,
        float VolumetricStrength);

    private sealed class SkyboxRuntime : IDisposable
    {
        private string? _positiveX;
        private string? _negativeX;
        private string? _positiveY;
        private string? _negativeY;
        private string? _positiveZ;
        private string? _negativeZ;
        private AssetRef<TextureAsset>? _px;
        private AssetRef<TextureAsset>? _nx;
        private AssetRef<TextureAsset>? _py;
        private AssetRef<TextureAsset>? _ny;
        private AssetRef<TextureAsset>? _pz;
        private AssetRef<TextureAsset>? _nz;
        private CubemapTexture? _cubemap;
        private int _pxVersion;
        private int _nxVersion;
        private int _pyVersion;
        private int _nyVersion;
        private int _pzVersion;
        private int _nzVersion;

        public CubemapTexture? Resolve(
            IAssetsManager assets,
            GraphicsDevice device,
            SceneSkyboxSettings settings)
        {
            ArgumentNullException.ThrowIfNull(assets);
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(settings);

            if (!Matches(settings))
                ReloadFromPaths(assets, device, settings);
            else if (NeedsReload())
                ReloadFromExisting(device);

            return _cubemap;
        }

        public void Dispose()
        {
            _cubemap?.Dispose();
            _cubemap = null;
            DisposeRefs();
        }

        private bool Matches(SceneSkyboxSettings settings)
        {
            return string.Equals(_positiveX, settings.PositiveX, StringComparison.Ordinal) &&
                   string.Equals(_negativeX, settings.NegativeX, StringComparison.Ordinal) &&
                   string.Equals(_positiveY, settings.PositiveY, StringComparison.Ordinal) &&
                   string.Equals(_negativeY, settings.NegativeY, StringComparison.Ordinal) &&
                   string.Equals(_positiveZ, settings.PositiveZ, StringComparison.Ordinal) &&
                   string.Equals(_negativeZ, settings.NegativeZ, StringComparison.Ordinal);
        }

        private bool NeedsReload()
        {
            return _px is not null &&
                   _nx is not null &&
                   _py is not null &&
                   _ny is not null &&
                   _pz is not null &&
                   _nz is not null &&
                   (_pxVersion != _px.Version ||
                    _nxVersion != _nx.Version ||
                    _pyVersion != _py.Version ||
                    _nyVersion != _ny.Version ||
                    _pzVersion != _pz.Version ||
                    _nzVersion != _nz.Version);
        }

        private void ReloadFromPaths(
            IAssetsManager assets,
            GraphicsDevice device,
            SceneSkyboxSettings settings)
        {
            using var px = assets.Load<TextureAsset>(settings.PositiveX);
            using var nx = assets.Load<TextureAsset>(settings.NegativeX);
            using var py = assets.Load<TextureAsset>(settings.PositiveY);
            using var ny = assets.Load<TextureAsset>(settings.NegativeY);
            using var pz = assets.Load<TextureAsset>(settings.PositiveZ);
            using var nz = assets.Load<TextureAsset>(settings.NegativeZ);

            try
            {
                var cubemap = CreateCubemap(device, px, nx, py, ny, pz, nz);
                var oldCubemap = _cubemap;
                _cubemap = cubemap;
                oldCubemap?.Dispose();

                DisposeRefs();
                _px = px.Acquire();
                _nx = nx.Acquire();
                _py = py.Acquire();
                _ny = ny.Acquire();
                _pz = pz.Acquire();
                _nz = nz.Acquire();
                CaptureVersions();

                _positiveX = settings.PositiveX;
                _negativeX = settings.NegativeX;
                _positiveY = settings.PositiveY;
                _negativeY = settings.NegativeY;
                _positiveZ = settings.PositiveZ;
                _negativeZ = settings.NegativeZ;
            }
            catch (Exception exception)
            {
                DisposeRefs();
                _px = px.Acquire();
                _nx = nx.Acquire();
                _py = py.Acquire();
                _ny = ny.Acquire();
                _pz = pz.Acquire();
                _nz = nz.Acquire();
                CaptureVersions();

                _positiveX = settings.PositiveX;
                _negativeX = settings.NegativeX;
                _positiveY = settings.PositiveY;
                _negativeY = settings.NegativeY;
                _positiveZ = settings.PositiveZ;
                _negativeZ = settings.NegativeZ;
                Logger.Error(exception, "Skybox reload failed, keeping previous cubemap.");
            }
        }

        private void ReloadFromExisting(GraphicsDevice device)
        {
            if (_px is null ||
                _nx is null ||
                _py is null ||
                _ny is null ||
                _pz is null ||
                _nz is null)
            {
                return;
            }

            try
            {
                var cubemap = CreateCubemap(device, _px, _nx, _py, _ny, _pz, _nz);
                var oldCubemap = _cubemap;
                _cubemap = cubemap;
                oldCubemap?.Dispose();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Skybox texture update failed, keeping previous cubemap.");
            }
            finally
            {
                CaptureVersions();
            }
        }

        private CubemapTexture CreateCubemap(
            GraphicsDevice device,
            AssetRef<TextureAsset> px,
            AssetRef<TextureAsset> nx,
            AssetRef<TextureAsset> py,
            AssetRef<TextureAsset> ny,
            AssetRef<TextureAsset> pz,
            AssetRef<TextureAsset> nz)
        {
            ThrowIfInvalid(px);
            ThrowIfInvalid(nx);
            ThrowIfInvalid(py);
            ThrowIfInvalid(ny);
            ThrowIfInvalid(pz);
            ThrowIfInvalid(nz);

            return new CubemapTexture(
                device,
                px.Value,
                nx.Value,
                py.Value,
                ny.Value,
                pz.Value,
                nz.Value);
        }

        private void CaptureVersions()
        {
            _pxVersion = _px?.Version ?? 0;
            _nxVersion = _nx?.Version ?? 0;
            _pyVersion = _py?.Version ?? 0;
            _nyVersion = _ny?.Version ?? 0;
            _pzVersion = _pz?.Version ?? 0;
            _nzVersion = _nz?.Version ?? 0;
        }

        private void DisposeRefs()
        {
            _px?.Dispose();
            _nx?.Dispose();
            _py?.Dispose();
            _ny?.Dispose();
            _pz?.Dispose();
            _nz?.Dispose();
            _px = null;
            _nx = null;
            _py = null;
            _ny = null;
            _pz = null;
            _nz = null;
            _pxVersion = 0;
            _nxVersion = 0;
            _pyVersion = 0;
            _nyVersion = 0;
            _pzVersion = 0;
            _nzVersion = 0;
        }

        private static void ThrowIfInvalid(AssetRef<TextureAsset> asset)
        {
            if (!asset.HasError)
                return;

            throw asset.Error ?? asset.LastError ?? new InvalidOperationException(
                $"Skybox texture '{asset.Metadata.Path}' is invalid.");
        }
    }
}
