using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Vecxy.Rendering;

public interface IRenderWindow
{
    public int Width { get; }
    public int Height { get; }

    public void Clear();
    public void SwapBuffers();
}

public class RenderWindow : GameWindow, IRenderWindow
{
    public int Width => Size.X;
    public int Height => Size.Y;

    public static RenderWindow Create(RenderWindowOptions options)
    {
        var native = new NativeWindowSettings();

        if (options.IsDebug)
        {
            native.Flags = ContextFlags.Debug;
        }

        var window = new RenderWindow(options, GameWindowSettings.Default, native);

        return window;
    }

    private RenderWindow(RenderWindowOptions options, GameWindowSettings settings, NativeWindowSettings native)
        : base(settings, native)
    {
        Size = new Vector2i(options.Width, options.Height);
        Title = options.Title;

        if (options.IsDebug)
        {
            EnableDebugMode();
        }
    }

    public void Clear()
    {
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GL.ClearColor(Color.DarkSlateGray);
    }

    protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
    {
        base.OnFramebufferResize(e);

        GL.Viewport(0, 0, e.Width, e.Height);
    }

    private static void EnableDebugMode()
    {
        GL.DebugMessageCallback(OnDebugMessage, IntPtr.Zero);
        GL.Enable(EnableCap.DebugOutput);
        GL.Enable(EnableCap.DebugOutputSynchronous);
    }

    private static void OnDebugMessage(
        DebugSource source, // Source of the debugging message.
        DebugType type, // Type of the debugging message.
        int id, // ID associated with the message.
        DebugSeverity severity, // Severity of the message.
        int length, // Length of the string in pMessage.
        IntPtr pMessage, // Pointer to message string.
        IntPtr pUserParam) // The pointer you gave to OpenGL, explained later.
    {
        // In order to access the string pointed to by pMessage, you can use Marshal
        // class to copy its contents to a C# string without unsafe code. You can
        // also use the new function Marshal.PtrToStringUTF8 since .NET Core 1.1.
        string message = Marshal.PtrToStringAnsi(pMessage, length);

        // The rest of the function is up to you to implement, however a debug output
        // is always useful.
        Console.WriteLine("[{0} source={1} type={2} id={3}] {4}", severity, source, type, id, message);

        // Potentially, you may want to throw from the function for certain severity
        // messages.
        if (type == DebugType.DebugTypeError)
        {
            throw new Exception(message);
        }
    }
}