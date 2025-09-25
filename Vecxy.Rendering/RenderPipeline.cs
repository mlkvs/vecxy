using OpenTK.Graphics.OpenGL;
using OpenTK.Platform;
using OpenTK.Windowing.Desktop;

namespace Vecxy.Rendering;

public class RenderPipeline
{
    private WindowHandle? _window;
    private OpenGLContextHandle? _context;
    public void Start()
    {
        var options = new ToolkitOptions()
        {
            ApplicationName = "OpenTK Application",
        };
        
        Toolkit.Init(options);
        
        var hints = new OpenGLGraphicsApiHints();

        _window = Toolkit.Window.Create(hints);

        _context = Toolkit.OpenGL.CreateFromWindow(_window);
        Toolkit.OpenGL.SetCurrentContext(_context);
        
        var binding = Toolkit.OpenGL.GetBindingsContext(_context);
        OpenTK.Graphics.GLLoader.LoadBindings(binding);

        var monitor = Monitors.GetPrimaryMonitor();

        var screenSize = monitor.ClientArea.Size;
        
        Toolkit.Window.SetSize(_window, 500, 500);
        Toolkit.Window.SetPosition(_window, screenSize.X / 2 - 500 / 2 ,screenSize.Y / 2 - 500 / 2);
        Toolkit.Window.SetMode(_window, WindowMode.Normal);

        EventQueue.EventRaised += HandleEvents;
        
        

        while (true)
        {
            GL.ClearColor(0.2f, 0.3f, 0.4f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            
            Toolkit.OpenGL.SwapBuffers(_context);
            
            Toolkit.Window.ProcessEvents(false);

            if (Toolkit.Window.IsWindowDestroyed(_window))
            {
                break;
            }
        }
    }

    private void HandleEvents(PalHandle? handle, PlatformEventType type, EventArgs args)
    {
        switch (args)
        {
            case CloseEventArgs closeEvent:
                Toolkit.Window.Destroy(_window);
                break;
        }
    }
}