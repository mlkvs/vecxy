using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;
using System.Drawing;
using Autofac;
using Vecxy.Kernel;
using Vecxy.Native;

namespace Vecxy.Rendering;

public class RenderingModule(Window window) : IModule, INativeContext
{
    private GL _gl;
    private uint _vao;
    private uint _vbo;
    private uint _program;

    // Исходный код шейдеров
    private const string VertexShaderSource = @"
        #version 330 core
        layout (location = 0) in vec3 aPos;
        void main() {
            gl_Position = vec4(aPos.x, aPos.y, aPos.z, 1.0);
        }";

    private const string FragmentShaderSource = @"
        #version 330 core
        out vec4 FragColor;
        void main() {
            FragColor = vec4(1.0f, 0.5f, 1f, 1.0f);
        }";

    public void OnBindings(ContainerBuilder builder)
    {
        throw new NotImplementedException();
    }

    public void OnLoad(ILifetimeScope scope)
    {
     
    }

    public void OnInitialize()
    {
        _gl = GL.GetApi(this);

        _gl.Viewport(0, 0, 800, 600);

        // 1. Создание шейдерной программы
        uint vertexShader = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        uint fragmentShader = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);
        _program = _gl.CreateProgram();
        _gl.AttachShader(_program, vertexShader);
        _gl.AttachShader(_program, fragmentShader);
        _gl.LinkProgram(_program);

        // Удаляем временные шейдеры после линковки
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        // 2. Подготовка данных (координаты X, Y, Z)
        float[] vertices = {
            -0.5f, -0.5f, 0.0f, // Лево низ
             0.5f, -0.5f, 0.0f, // Право низ
             0.0f,  0.5f, 0.0f  // Верх центр
        };

        // 3. Создаем VAO и VBO
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (void* v = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            }
        }

        // Указываем, как читать данные (location = 0)
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        _gl.EnableVertexAttribArray(0);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindVertexArray(0);
    }

    public void OnTick(float deltaTime)
    {
        _gl.ClearColor(0.2f, 0.3f, 0.2f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        // Используем шейдер и VAO
        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);

        // Рисуем 3 вершины
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

        window.SwapBuffers();
    }

    public void OnFrame()
    {
        throw new NotImplementedException();
    }

    public void OnUnload()
    {
        throw new NotImplementedException();
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        string infoLog = _gl.GetShaderInfoLog(shader);
        if (!string.IsNullOrWhiteSpace(infoLog))
            throw new Exception($"Error compiling {type}: {infoLog}");

        return shader;
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
    }

    IntPtr INativeContext.GetProcAddress(string proc, int? slot = null)
        => window.GetProcAddress(proc, slot);

    bool INativeContext.TryGetProcAddress(string proc, out IntPtr addr, int? slot = null)
    {
        addr = window.GetProcAddress(proc, slot);
        return addr != IntPtr.Zero;
    }
}