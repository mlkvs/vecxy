using Silk.NET.OpenGL;
using Autofac;
using Vecxy.Kernel;

namespace Vecxy.Rendering;

public sealed class RenderingModule(Window window) : IModule
{
    private GL? _gl;
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

    public void OnLoad(ILifetimeScope scope)
    {
     
    }

    public void OnInitialize()
    {
        _gl = GL.GetApi(window);

        Resize(window.Size.X, window.Size.Y);
        window.Resized += size => Resize(size.X, size.Y);

        // 1. Создание шейдерной программы
        uint vertexShader = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        uint fragmentShader = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);
        _program = _gl.CreateProgram();
        _gl.AttachShader(_program, vertexShader);
        _gl.AttachShader(_program, fragmentShader);
        _gl.LinkProgram(_program);

        _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out var linked);
        if (linked == 0)
        {
            throw new InvalidOperationException($"OpenGL program linking failed: {_gl.GetProgramInfoLog(_program)}");
        }

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
    }

    public void OnFrame()
    {
        if (_gl is null) return;

        _gl.ClearColor(0.2f, 0.3f, 0.2f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        // Используем шейдер и VAO
        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);

        // Рисуем 3 вершины
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

        window.SwapBuffers();
    }

    public void OnUnload()
    {
        Dispose();
    }

    private uint CompileShader(ShaderType type, string source)
    {
        var gl = _gl ?? throw new InvalidOperationException("OpenGL is not initialized.");
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);

        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        if (compiled == 0)
            throw new InvalidOperationException($"Error compiling {type}: {gl.GetShaderInfoLog(shader)}");

        return shader;
    }

    public void Dispose()
    {
        if (_gl is null) return;
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_program != 0) _gl.DeleteProgram(_program);
        _gl.Dispose();
        _gl = null;
    }

    private void Resize(int width, int height)
    {
        _gl?.Viewport(0, 0, (uint)Math.Max(1, width), (uint)Math.Max(1, height));
    }
}
