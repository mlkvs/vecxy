using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Vecxy.Kernel;
using Vecxy.Rendering;

namespace Vecxy.Editor;

public sealed class ImGuiRenderer(
    IWindow window) : IDisposable
{
    private ImGuiController? _controller;
    private GL? _gl;
    private bool _disposed;
    private bool _resetLayoutRequested;
    private int _lastWidth;
    private int _lastHeight;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_controller is not null)
            return;

        if (window is not Window nativeWindow)
        {
            throw new NotSupportedException(
                $"ImGui requires the Silk.NET window, but received '{window.GetType().FullName}'.");
        }

        var native =
            ReadProperty<Silk.NET.Windowing.IWindow>(nativeWindow, "NativeWindow") ??
            ReadProperty<Silk.NET.Windowing.IWindow>(nativeWindow, "Native") ??
            throw new InvalidOperationException(
                "Rendering window does not expose a native Silk.NET window.");

        var input =
            ReadProperty<IInputContext>(nativeWindow, "InputContext") ??
            ReadProperty<IInputContext>(nativeWindow, "Input") ??
            throw new InvalidOperationException(
                "Rendering window does not expose a Silk.NET input context.");

        window.MakeCurrent();
        _gl = GL.GetApi(window.GetProcAddress);
        _controller = new ImGuiController(
            _gl,
            native,
            input);

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        _lastWidth = Math.Max(1, window.Width);
        _lastHeight = Math.Max(1, window.Height);
    }

    public void BeginFrame(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var width = Math.Max(1, window.Width);
        var height = Math.Max(1, window.Height);

        if (_controller is not null &&
            (width != _lastWidth || height != _lastHeight))
        {
            NotifyResize(_controller, width, height);
            _lastWidth = width;
            _lastHeight = height;
        }

        if (_resetLayoutRequested)
        {
            ImGui.LoadIniSettingsFromMemory(string.Empty);
            _resetLayoutRequested = false;
        }

        _controller?.Update(Math.Max(deltaTime, 0.000001f));

        var io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(width, height);
        io.DisplayFramebufferScale = System.Numerics.Vector2.One;
    }

    public void Render()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller?.Render();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _controller?.Dispose();
        _controller = null;
        _gl?.Dispose();
        _gl = null;
    }

    public void ResetLayout()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _resetLayoutRequested = true;
    }

    private static TValue? ReadProperty<TValue>(
        object instance,
        string propertyName)
        where TValue : class
    {
        var property = instance
            .GetType()
            .GetProperty(propertyName);

        return property?.GetValue(instance) as TValue;
    }

    private static void NotifyResize(
        ImGuiController controller,
        int width,
        int height)
    {
        var method = controller
            .GetType()
            .GetMethod(
                "WindowResized",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic,
                null,
                [typeof(System.Numerics.Vector2)],
                null);

        if (method is not null)
        {
            method.Invoke(
                controller,
                [new System.Numerics.Vector2(width, height)]);
            return;
        }

        method = controller
            .GetType()
            .GetMethod(
                "WindowResized",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic,
                null,
                [typeof(int), typeof(int)],
                null);

        if (method is not null)
        {
            method.Invoke(controller, [width, height]);
        }
    }
}
