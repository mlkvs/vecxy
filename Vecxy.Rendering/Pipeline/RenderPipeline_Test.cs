using System.Reflection;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using Vecxy.Diagnostics;
using Vecxy.Reflection;

namespace Vecxy.Rendering;

public class RenderPipeline_Test : IDisposable
{
    private ShaderProgram shader;
    private int vao;
    private int vbo;
    private bool disposed;

    private RenderWindow _window;

    public void Start()
    {
        var options = new RenderWindowOptions
        {
            Width = 800,
            Height = 600,
            Title = "Vecxy.Rendering",
        };

        using var window = new RenderWindow(options);

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
            
            
            
            CheckGLError("OnLoad complete");
            
            Logger.Info("Render pipeline initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize render pipeline");
            throw;
        }
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