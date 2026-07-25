using System.Numerics;
using Autofac;
using Silk.NET.OpenGL;
using Vecxy.Assets;
using Vecxy.Kernel;

namespace Vecxy.Rendering;

public interface IRenderer
{
    RenderingStatistics Statistics { get; }

    GameView CreateGameView(IRenderTarget? target = null);
    void DestroyGameView(GameView view);
    Mesh CreateQuad();
}

public sealed class RenderingModule(
    GraphicsDevice device,
    BackbufferRenderTarget backbuffer,
    MaterialLibrary materials,
    RenderingStatistics statistics,
    ImGuiOverlay overlay)
    :
        IModule,
        IModule.IUpdatable,
        IModule.IRenderable,
        IRenderer
{
    public sealed class Definition : AModuleDefinition<RenderingModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(IRenderer)];

        protected override void RegisterModule(ContainerBuilder builder)
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
        overlay.Initialize();
    }

    public void OnUpdate(float deltaTime)
    {
        statistics.BeginFrame(
            deltaTime,
            _views.Count(view => view.Enabled));
        overlay.BeginFrame(deltaTime);
    }

    public void OnRender()
    {
        var presentedTargets = new HashSet<IRenderTarget>();

        foreach (var view in _views.Where(view => view.Enabled))
        {
            view.Target.Bind(device);
            device.GL.ClearColor(
                view.ClearColor.X,
                view.ClearColor.Y,
                view.ClearColor.Z,
                view.ClearColor.W);
            device.GL.Clear(ClearBufferMask.ColorBufferBit);

            var aspectCorrection =
                (float)view.Target.Height /
                Math.Max(1, view.Target.Width);
            var viewTransform = Matrix4x4.CreateScale(
                aspectCorrection,
                1.0f,
                1.0f);

            foreach (var item in view.Items
                         .Where(item => item.Enabled)
                         .OrderBy(item => item.Phase))
            {
                var material = materials.Get(item.Material);
                var shader = material.Bind(item.Material);
                shader.Set("uTransform", item.Transform * viewTransform);
                item.Mesh.Draw();
                statistics.RecordDraw();
            }

            presentedTargets.Add(view.Target);
        }

        backbuffer.Bind(device);
        overlay.Render(statistics);
        presentedTargets.Add(backbuffer);

        foreach (var target in presentedTargets)
        {
            target.Present();
        }
    }

    public GameView CreateGameView(IRenderTarget? target = null)
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

        ReadOnlySpan<uint> indices = [0, 1, 2, 2, 3, 0];
        return new Mesh(device, vertices, indices);
    }

    public void OnShutdown()
    {
        foreach (var view in _views)
        {
            view.Clear();
        }

        _views.Clear();
        materials.Clear();
    }

    public void Dispose()
    {
    }
}
