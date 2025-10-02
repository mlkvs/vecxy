using System.Numerics;
using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public class D2RenderContext : RenderContextBase, ID2RenderContext
{
    private ShaderProgram _currentShader;
    private int _vao;
    private int _vbo;

    private readonly List<Sprite> _batchSprites = new();
    private float[] _vertexBuffer;
    private int _vertexBufferIndex;
    private readonly int _maxSprites = 1000;
    private bool _isBatching = false;

    private readonly int _windowWidth;
    private readonly int _windowHeight;

    public D2RenderContext(IRenderWindow window) : base(window)
    {
        _windowWidth = window.Width;
        _windowHeight = window.Height;

        _vertexBuffer = new float[_maxSprites * 6 * 4];
        _vbo = GL.GenBuffer();

        InitializeQuad();

        _currentShader = ShaderProgram.Create("Shaders.base.vert", "Shaders.base.frag");
        _currentShader.Initialize();
        _currentShader.Compile();
        _currentShader.Link();
    }

    private void InitializeQuad()
    {
        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertexBuffer.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
    }

    public void BeginBatch()
    {
        
        _batchSprites.Clear();
        _vertexBufferIndex = 0;
        _isBatching = true;
    }

    public void AddSpriteToBatch(Sprite sprite)
    {
        if (!_isBatching) return;
        if (_batchSprites.Count >= _maxSprites) return;

        _batchSprites.Add(sprite);

        float ndcX = (sprite.Position.X / _windowWidth) * 2f - 1f;
        float ndcY = 1f - (sprite.Position.Y / _windowHeight) * 2f;
        float ndcW = (sprite.Size.X / _windowWidth) * 2f;
        float ndcH = (sprite.Size.Y / _windowHeight) * 2f;

        float u0 = 0f;
        float v0 = 0f;
        float u1 = 1f;
        float v1 = 1f;

        float[] quadVertices = new float[]
        {
            ndcX, ndcY, u0, v0,
            ndcX + ndcW, ndcY, u1, v0,
            ndcX + ndcW, ndcY + ndcH, u1, v1,

            ndcX, ndcY, u0, v0,
            ndcX + ndcW, ndcY + ndcH, u1, v1,
            ndcX, ndcY + ndcH, u0, v1
        };

        Array.Copy(quadVertices, 0, _vertexBuffer, _vertexBufferIndex, 24);
        _vertexBufferIndex += 24;
    }

    public void EndBatch()
    {
        _isBatching = false;
    }

    public void Flush()
    {
        if (_batchSprites.Count == 0) return;

        _batchSprites[0].Texture.Bind();
        _currentShader.Use();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _vertexBufferIndex * sizeof(float), _vertexBuffer);

        GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexBufferIndex / 4);

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);

        _batchSprites.Clear();
        _vertexBufferIndex = 0;
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
}
