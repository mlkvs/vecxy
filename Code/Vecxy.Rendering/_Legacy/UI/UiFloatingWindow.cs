using Silk.NET.OpenGL;

namespace Vecxy.Rendering._Legacy.UI;

public sealed class UiFloatingWindow : IDisposable
{
    private readonly Window _window;
    private readonly GraphicsDevice _device;
    private readonly UiSystem _ui;
    private bool _disposed;
    private bool _failed;

    public bool IsOpen => !_disposed && !_failed && _window.IsRunning;
    public UiDocument? Document => _ui.Document;

    public UiFloatingWindow(string title, int width, int height, Window owner,
        string uxml, string css)
    {
        // A floating panel owns its small UI renderer and therefore must own an independent GL context.
        // Sharing the main context makes GLFW teardown order platform-dependent and is unnecessary here.
        _window = new Window(new WindowConfig(title, width, height));
        _window.Initialize();
        _device = new GraphicsDevice(_window);
        _device.Initialize();
        _device.Resize(width, height);
        _window.Resized += size => _device.Resize(size.X, size.Y);
        _ui = new UiSystem(_device, _window);
        _ui.Initialize();
        _ui.Load(uxml, css);
    }

    public void Pump(float deltaTime)
    {
        if (!IsOpen) return;
        try
        {
            _window.ProcessEvents();
            if (!IsOpen) return;
            _window.MakeCurrent();
            var width = Math.Max(1, _window.Size.X); var height = Math.Max(1, _window.Size.Y);
            _device.GL.Viewport(0, 0, (uint)width, (uint)height);
            _device.GL.ClearColor(.055f, .065f, .085f, 1f);
            _device.GL.Clear(ClearBufferMask.ColorBufferBit);
            _ui.Update(deltaTime, width, height);
            _ui.Render(width, height, default);
            _window.SwapBuffers();
        }
        catch (Exception exception)
        {
            _failed = true;
            Vecxy.Diagnostics.Logger.Error(exception, "Floating UI window failed; panel will be docked back.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _window.MakeCurrent(); } catch { }
        try { _ui.Dispose(); } catch (Exception exception) { Vecxy.Diagnostics.Logger.Error(exception, "Floating UI cleanup failed."); }
        try { _device.Dispose(); } catch { }
        try { _window.Dispose(); } catch { }
    }
}
