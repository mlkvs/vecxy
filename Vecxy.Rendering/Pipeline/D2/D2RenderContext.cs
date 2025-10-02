using System.Numerics;
using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public class D2RenderContext : RenderContextBase, ID2RenderContext
{
    private ShaderProgram _currentShader;
    private int _vao;
    private int _vbo;

    private readonly List<Sprite> _batchSprites = new();

    public D2RenderContext(IRenderWindow window) : base(window)
    {
        InitializeQuad();
        
        _currentShader = ShaderProgram.Create("Shaders.base.vert", "Shaders.base.frag");
        
        _currentShader.Initialize();
        _currentShader.Compile();
        _currentShader.Link();
    }

    private void InitializeQuad()
    {
        float[] vertices = {
            // X, Y, U, V
            0f, 0f, 0f, 0f,
            1f, 0f, 1f, 0f,
            1f, 1f, 1f, 1f,

            0f, 0f, 0f, 0f,
            1f, 1f, 1f, 1f,
            0f, 1f, 0f, 1f
        };

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

        // Позиция
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        // UV
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
    }

    public void BeginBatch()
    {
        _batchSprites.Clear();
    }

    public void EndBatch()
    {
        foreach (var sprite in _batchSprites)
        {
            DrawSprite(sprite.Texture, sprite.Position, sprite.Size, sprite.Color);
        }
    }

    public void Flush()
    {
        // Можно добавить дополнительные очистки или финальный рендер батча, если потребуется
    }

    public void DrawSprite(Texture texture, Vector2 position, Vector2 size, Vector4 color)
    {
        _currentShader.Use();
        texture.Bind();
        _currentShader.SetUniform("u_Position", position);
        _currentShader.SetUniform("u_Size", size);
        _currentShader.SetUniform("u_Color", color);

        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);
    }

    public void QueueSprite(Sprite sprite)
    {
        _batchSprites.Add(sprite);
    }
}
