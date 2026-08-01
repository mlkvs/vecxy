using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using System.Runtime.InteropServices;
using Vecxy.Kernel;
using Vecxy.Rendering;

namespace Vecxy.Editor;

public sealed class ImGuiRenderer(
    IWindow window) : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate byte* GetClipboardTextDelegate(nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void SetClipboardTextDelegate(
        nint userData,
        byte* text);

    private ImGuiController? _controller;
    private GL? _gl;
    private IKeyboard? _keyboard;
    private GetClipboardTextDelegate? _getClipboardText;
    private SetClipboardTextDelegate? _setClipboardText;
    private nint _clipboardText;
    private bool _disposed;
    private bool _resetLayoutRequested;
    private int _lastWidth;
    private int _lastHeight;

    public unsafe void Initialize()
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

        _keyboard = input.Keyboards.FirstOrDefault() ??
            throw new InvalidOperationException(
                "ImGui requires at least one keyboard.");
        _getClipboardText = GetClipboardText;
        _setClipboardText = SetClipboardText;

        window.MakeCurrent();
        _gl = GL.GetApi(window.GetProcAddress);
        _controller = new ImGuiController(
            _gl,
            native,
            input,
            onConfigureIO: ConfigureImGui);

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
        _keyboard = null;
        _getClipboardText = null;
        _setClipboardText = null;
        if (_clipboardText != 0)
        {
            Marshal.FreeCoTaskMem(_clipboardText);
            _clipboardText = 0;
        }
        _gl?.Dispose();
        _gl = null;
    }

    public void ResetLayout()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _resetLayoutRequested = true;
    }

    public unsafe void DisableIniPersistence()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ImGui.GetIO().NativePtr->IniFilename = null;
    }

    private void ConfigureImGui()
    {
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.GetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(
            _getClipboardText!);
        io.SetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(
            _setClipboardText!);
        io.ClipboardUserData = nint.Zero;
    }

    private unsafe byte* GetClipboardText(nint _)
    {
        if (_clipboardText != 0)
            Marshal.FreeCoTaskMem(_clipboardText);

        _clipboardText = Marshal.StringToCoTaskMemUTF8(
            _keyboard?.ClipboardText ?? string.Empty);
        return (byte*)_clipboardText;
    }

    private unsafe void SetClipboardText(nint _, byte* text)
    {
        if (_keyboard is null)
            return;

        _keyboard.ClipboardText =
            Marshal.PtrToStringUTF8((nint)text) ?? string.Empty;
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
