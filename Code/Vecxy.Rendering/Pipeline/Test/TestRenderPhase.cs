using System.Reflection;
using OpenTK.Graphics.OpenGL;
using Vecxy.Reflection;

namespace Vecxy.Rendering;


public class TestRenderPhase() : IRenderPhase
{
    public RENDER_PHASE_TYPE Type => RENDER_PHASE_TYPE.NONE;
    
    private readonly List<IRenderable> _renderables = [];

    private ShaderProgram _shader;
    private int _vertexBufferObject;
    private int _vertexArrayObject;
    private int _elementBufferObject;

    private float[] _vertices;
    private uint[] _indices;
    
    public void OnInitialize()
    {
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        
        _shader = ShaderProgram
            .Create("Shaders.test.vert", "Shaders.test.frag")
            .Build();

        var cubeSource = EmbeddedResource
            .Get(Assembly.GetExecutingAssembly(), "Models.test_cube_default.obj")!
            .Text();
        
        var parser = new ObjParser();
        var obj = parser.Parse(cubeSource);
        
        /*var vertices = new[]
        {
            -0.5f, -0.5f, 0.0f, // Left-Bottom
            0.5f, -0.5f, 0.0f, // Right-Bottom,
            0.0f, 0.5f, 0.0f, // Top
        };*/

        _vertices = 
        [
            // Right-Top 
            /*Position*/ 0.5f, 0.5f, 0.0f, 
            /*Color*/ 1.0f, 0.0f, 0.0f,
            /*Alpha*/ 1.0f,
            
            // Bottom-Right
            /*Position*/ 0.5f, -0.5f, 0.0f, 
            /*Color*/ 0.0f, 0.0f, 1.0f,
            /*Alpha*/ 0.0f,
            
            // Bottom-Left 
            /*Position*/ -0.5f, -0.5f, 0.0f, 
            /*Color*/ 0.0f, 1.0f, 0.0f,
            /*Alpha*/ 0.0f,
            
            // Left-Top
            /*Position*/ -0.5f, 0.5f, 0.0f, 
            /*Color*/ 0.0f, 0.0f, 0.5f,
            /*Alpha*/ 0.5f,
        ];

        _indices =
        [
            0, 1, 3,
            1, 2, 3
        ];

        // VBO
        _vertexBufferObject = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
        GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Length * sizeof(float), _vertices, BufferUsageHint.StaticDraw);
        
        // VAO
        _vertexArrayObject = GL.GenVertexArray();
        GL.BindVertexArray(_vertexArrayObject);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        
        GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 7 * sizeof(float), 6 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        
        // EBO
        _elementBufferObject = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _elementBufferObject);
        GL.BufferData(BufferTarget.ElementArrayBuffer, _indices.Length * sizeof(uint), _indices, BufferUsageHint.StaticDraw);
    }

    public void OnRender()
    {
        _shader.Use();
        
        GL.BindVertexArray(_vertexArrayObject);
        GL.DrawElements(PrimitiveType.Triangles, _indices.Length, DrawElementsType.UnsignedInt, 0);
    }

    public void RegisterRenderable(IRenderable renderable)
    {
        
    }

    public void Dispose()
    {
        _renderables.Clear();
        
        GC.SuppressFinalize(this);
    }
}