using OpenTK.Graphics.OpenGL;
using OpenTK.Platform;
using OpenTK.Windowing.Desktop;

namespace Vecxy.Rendering;

public class RenderPipeline
{
    private WindowHandle? _window;
    private OpenGLContextHandle? _context;
    private int _vao, _vbo;
    private Shader _shader;

    public void Start()
    {
        InitializeWindow();
        InitializeGraphics();
        MainLoop();
    }

    private void InitializeWindow()
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
        Toolkit.Window.SetPosition(_window, screenSize.X / 2 - 500 / 2, screenSize.Y / 2 - 500 / 2);
        Toolkit.Window.SetMode(_window, WindowMode.Normal);

        EventQueue.EventRaised += HandleEvents;
    }

    private void InitializeGraphics()
    {
        GL.ClearColor(0.2f, 0.3f, 0.4f, 1.0f);

        // Создаем и компилируем шейдеры
        _shader = new Shader();
        _shader.Setup();

        // Вершины треугольника
        float[] vertices = {
            -0.5f, -0.5f, 0.0f,  // левый нижний
             0.5f, -0.5f, 0.0f,  // правый нижний
             0.0f,  0.5f, 0.0f   // верхний центр
        };

        // Создаем VAO и VBO
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), 
                     vertices, BufferUsageHint.StaticDraw);

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
    }

    private void MainLoop()
    {
        while (true)
        {
            GL.Clear(ClearBufferMask.ColorBufferBit);
            
            // Используем шейдер и рисуем треугольник
            GL.UseProgram(_shader.ProgramHandle);
            GL.BindVertexArray(_vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            
            Toolkit.OpenGL.SwapBuffers(_context);
            Toolkit.Window.ProcessEvents(false);

            if (Toolkit.Window.IsWindowDestroyed(_window))
            {
                break;
            }
        }
        
        Cleanup();
    }

    private void Cleanup()
    {
        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_vbo);
        GL.DeleteProgram(_shader.ProgramHandle);
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