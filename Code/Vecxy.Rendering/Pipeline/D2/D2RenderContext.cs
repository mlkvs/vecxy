using OpenTK.Graphics.OpenGL;
using Vector4 = System.Numerics.Vector4;
using System.Numerics;

namespace Vecxy.Rendering;

public interface ID2RenderContext : IRenderContext
{
    public void BeginBatch();
    public void EndBatch();

    public void DrawSprite(Sprite sprite);
}

public class D2RenderContext : ID2RenderContext
{
    public IRenderWindow Window { get; private set; }
    
    private ShaderProgram? _currentShader;
    private int _vao;
    private int _vbo;

    private readonly List<Sprite> _batchSprites = [];
    private float[]? _vertexBuffer;
    private int _vertexBufferIndex;
    private bool _isBatching;

    private const int MAX_SPRITES = 1000;
    private const int VERTEX_SIZE = 4;
    private const int VERTICES_PER_SPRITE = 6;
    private const int FLOATS_PER_SPRITE = VERTICES_PER_SPRITE * VERTEX_SIZE;

    public D2RenderContext(IRenderWindow window)
    {
        Window = window;
        
        _vertexBuffer = new float[MAX_SPRITES * FLOATS_PER_SPRITE];
        _vbo = GL.GenBuffer();

        InitializeQuad();

        _currentShader = ShaderProgram
            .Create("Shaders.base.vert", "Shaders.base.frag")
            .Build();

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.DepthTest);
    }

    private void InitializeQuad()
    {
        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertexBuffer!.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, VERTEX_SIZE * sizeof(float), 0);

        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, VERTEX_SIZE * sizeof(float), 2 * sizeof(float));

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
    }

    public void BeginBatch()
    {
        if (_isBatching)
        {
            return;
        }

        _batchSprites.Clear();
        _vertexBufferIndex = 0;
        _isBatching = true;
    }
    
    public void EndBatch()
    {
        _isBatching = false;

        if (_batchSprites.Count == 0)
        {
            return;
        }

        _currentShader.Use();

        _currentShader.SetUniform("u_Projection", CreateOrtho(Window.Width, Window.Height));
        _currentShader.SetUniform("u_Color", new Vector4(1f, 1f, 1f, 1f));

        if (_batchSprites[0].Texture != null)
        {
            _batchSprites[0].Texture.Bind();
            _currentShader.SetUniform("u_Texture", 0);
        }

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _vertexBufferIndex * sizeof(float), _vertexBuffer);

        int vertexCount = _batchSprites.Count * VERTICES_PER_SPRITE;
        GL.DrawArrays(PrimitiveType.Triangles, 0, vertexCount);

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);

        _batchSprites.Clear();
        _vertexBufferIndex = 0;
    }
    
    public void DrawSprite(Sprite sprite)
    {
        if (!_isBatching)
        {
            return;
        }

        _batchSprites.Add(sprite);

        var x0 = sprite.Position.X;
        var y0 = sprite.Position.Y;
        var x1 = x0 + sprite.Size.X;
        var y1 = y0 + sprite.Size.Y;

        var u0 = 0f;
        var v0 = 1f;
        var u1 = 1f;
        var v1 = 0f;

        float[] quadVertices = {
            x0, y0, u0, v0,
            x1, y0, u1, v0,
            x1, y1, u1, v1,

            x0, y0, u0, v0,
            x1, y1, u1, v1,
            x0, y1, u0, v1
        };

        Array.Copy(quadVertices, 0, _vertexBuffer, _vertexBufferIndex, quadVertices.Length);
        _vertexBufferIndex += quadVertices.Length;
    }

    // TODO: Разобраться как работает ортографическая проекция камеры
    private Matrix4x4 CreateOrtho(int width, int height)
    {
        float right = width * 0.5f;
        float left = -right;
        float top = height * 0.5f;
        float bottom = -top;

        return new Matrix4x4(
            2f / (right - left), 0f, 0f, -(right + left) / (right - left),
            0f, 2f / (top - bottom), 0f, -(top + bottom) / (top - bottom),
            0f, 0f, -1f, 0f,
            0f, 0f, 0f, 1f
        );
    }
}