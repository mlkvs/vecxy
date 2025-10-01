using System.Drawing;
using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public class D2RenderPhase : IRenderPhase<ID2RenderContext>
{
    public RenderPhase Type => RenderPhase.D2;
    public ID2RenderContext Context { get; }

    private ShaderProgram _spriteShader;
    
    private int vao;
    private int vbo;

    public D2RenderPhase()
    {
        Context = new D2RenderContext();

        
    }


    public void Initialize()
    {
        GL.ClearColor(Color.DarkSlateGray);
        
        _spriteShader = ShaderProgram.Create
        (
            "Shaders.base.vert",
            "Shaders.base.frag"
        );
        _spriteShader.Initialize();
        _spriteShader.Compile();
        _spriteShader.Link();
            
        CreateTriangle();
            
        GL.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
    }

    public void OnBegin()
    {
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        
        _spriteShader.Use();
    }

    public void OnRender()
    {
        GL.BindVertexArray(vao);
    }

    public void OnEnd()
    {
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    public void Dispose()
    {
    }
    
    private void CreateTriangle()
    {
        float[] vertices = {
            -0.5f, -0.5f, 0.0f, // Bottom-left
            0.5f, -0.5f, 0.0f, // Bottom-right  
            0.0f,  0.5f, 0.0f  // Top
        };

        // Генерируем VAO
        vao = GL.GenVertexArray();
        GL.BindVertexArray(vao);

        // Генерируем и настраиваем VBO
        vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

        // Настраиваем атрибуты вершин
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        // Отвязываем
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
    }
}