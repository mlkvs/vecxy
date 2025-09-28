using System.Reflection;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using Vecxy.Diagnostics;
using Vecxy.Reflection;
using Vecxy.Rendering;

public class RenderPipeline : IDisposable
{
    private ShaderProgram shader;
    private int vao;
    private int vbo;
    private bool disposed;

    private RenderingWindow _window;

    public void Start()
    {
        var options = new RenderingWindowOptions
        {
            Width = 800,
            Height = 600,
            Title = "Vecxy.Rendering",
        };

        using var window = new RenderingWindow(options);

        _window = window;
        window.Load += OnLoad;
        window.RenderFrame += OnRenderFrame;
        window.Resize += OnResize;
        
        window.Run();
    }

    private void OnLoad()
    {
        try
        {
            Logger.Info("Initializing render pipeline...");
            
            var assembly = Assembly.GetExecutingAssembly();
            
            // Проверяем доступные ресурсы
            var resourceNames = assembly.GetManifestResourceNames();
            Logger.Info("Available embedded resources:");
            foreach (var name in resourceNames)
            {
                Logger.Info($"  - {name}");
            }
            
            var vertexSource = assembly.GetEmbeddedResource("Shaders.base.vert")?.Text();
            var fragmentSource = assembly.GetEmbeddedResource("Shaders.base.frag")?.Text();

            if (string.IsNullOrEmpty(vertexSource))
                throw new Exception("Vertex shader not found or empty");
            if (string.IsNullOrEmpty(fragmentSource))
                throw new Exception("Fragment shader not found or empty");

            Logger.Info($"Vertex shader source loaded ({vertexSource.Length} chars)");
            Logger.Info($"Fragment shader source loaded ({fragmentSource.Length} chars)");

            // Создаем и компилируем шейдеры
            shader = new ShaderProgram(vertexSource, fragmentSource);
            shader.Initialize();
            shader.Compile();
            shader.Link();

            // Создаем геометрию
            CreateTriangle();
            
            // Устанавливаем цвет очистки (темный, чтобы оранжевый треугольник был виден)
            GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
            
            CheckGLError("OnLoad complete");
            
            Logger.Info("Render pipeline initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize render pipeline");
            throw;
        }
    }

    private void CreateTriangle()
    {
        Logger.Info("Creating triangle geometry...");
        
        float[] vertices = {
            -0.5f, -0.5f, 0.0f, // Bottom-left
             0.5f, -0.5f, 0.0f, // Bottom-right  
             0.0f,  0.5f, 0.0f  // Top
        };

        // Генерируем VAO
        vao = GL.GenVertexArray();
        GL.BindVertexArray(vao);
        Logger.Info($"Generated VAO: {vao}");

        // Генерируем и настраиваем VBO
        vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        Logger.Info($"Generated VBO: {vbo}");

        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        CheckGLError("After buffer data");

        // Настраиваем атрибуты вершин
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        CheckGLError("After vertex attributes");

        // Отвязываем
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
        
        Logger.Info("Triangle geometry created successfully");
    }

    private void OnRenderFrame(FrameEventArgs e)
    {
        // Очищаем буфер цвета и глубины
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Активируем шейдер
        shader.Use();
        
        // Привязываем VAO и рисуем
        GL.BindVertexArray(vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        
        // Проверяем ошибки после рендеринга
        CheckGLError("After draw arrays");
        
        _window.SwapBuffers();
    }

    private void OnResize(ResizeEventArgs e)
    {
        GL.Viewport(0, 0, e.Width, e.Height);
        Logger.Info($"Viewport resized to: {e.Width}x{e.Height}");
    }

    private void CheckGLError(string context)
    {
        var error = GL.GetError();
        if (error != ErrorCode.NoError)
        {
            Logger.Error($"OpenGL error in {context}: {error}");
        }
    }

    public void Dispose()
    {
        if (!disposed)
        {
            Logger.Info("Disposing render pipeline...");
            
            GL.DeleteBuffer(vbo);
            GL.DeleteVertexArray(vao);
            shader?.Dispose();
            
            disposed = true;
            Logger.Info("Render pipeline disposed");
        }
    }
}