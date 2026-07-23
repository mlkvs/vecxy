using Autofac;
using Silk.NET.Maths;
using Vecxy.Kernel;
using Vecxy.UI;

namespace Vecxy.Rendering;

public sealed class RenderingModule : IModule
{
    private readonly Window _window;
    private readonly GraphicsDevice _device;

    public Renderer Renderer { get; }
    public UiSystem UI { get; }
    public UiSystem EditorUI { get; }
    public GameScreen GameScreen { get; }
    private bool _disposed;

    public RenderingModule(Window window)
    {
        _window = window;
        _device = new GraphicsDevice(window);
        GameScreen = new GameScreen();
        GameScreen.FillWindow(window.Size.X, window.Size.Y);
        Renderer = new Renderer(_device, window, GameScreen);
        UI = new UiSystem(_device, window);
        EditorUI = new UiSystem(_device, window);
    }

    public void OnLoad(ILifetimeScope scope) { }

    public void OnInitialize()
    {
        _device.Initialize();
        Renderer.Initialize();
        UI.Initialize();
        EditorUI.Initialize();
        _device.Resize(_window.Size.X, _window.Size.Y);
        _window.Resized += OnWindowResized;
    }

    public void OnUpdate(float deltaTime)
    {
        EditorUI.Update(deltaTime, _window.Size.X, _window.Size.Y);
        UI.Update(deltaTime, Renderer.Width, Renderer.Height, GameScreen.Bounds.X, GameScreen.Bounds.Y);
    }

    public void Render(IReadOnlyList<AppLayerRenderAdapter> layers, Color clearColor)
    {
        _window.MakeCurrent();
        Renderer.BeginFrame(clearColor);
        foreach (var layer in layers) layer.Render(Renderer);
        Renderer.EndFrame();
        UI.Render(Renderer.Width, Renderer.Height, Renderer.Stats, GameScreen.Bounds.X, GameScreen.Bounds.Y,
            _window.Size.X, _window.Size.Y);
        EditorUI.Render(_window.Size.X, _window.Size.Y, Renderer.Stats, 0, 0, _window.Size.X, _window.Size.Y);
        _window.SwapBuffers();
    }

    public void OnUnload() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Resized -= OnWindowResized;
        Renderer.Dispose();
        UI.Dispose();
        EditorUI.Dispose();
        _device.Dispose();
    }

    private void OnWindowResized(Vector2D<int> size)
    {
        _device.Resize(size.X, size.Y);
        if (EditorUI.Document is null) GameScreen.FillWindow(size.X, size.Y);
    }
}

public readonly record struct AppLayerRenderAdapter(Action<IRenderer> RenderAction)
{
    public void Render(IRenderer renderer) => RenderAction(renderer);
}
