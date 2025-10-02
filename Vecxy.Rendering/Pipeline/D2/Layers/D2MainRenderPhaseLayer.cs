using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public class D2MainRenderPhaseLayer : RenderPhaseLayerBase
{
    public override RENDER_PHASE_LAYER_TYPE Type => RENDER_PHASE_LAYER_TYPE.MAIN_PROCESS;
    
    private ShaderProgram _spriteShader;
    
    private int vao;
    private int vbo;

    public override void Initialize(IRenderContext ctx)
    {
        base.Initialize(ctx);
        
        _spriteShader = ShaderProgram.Create
        (
            "Shaders.base.vert",
            "Shaders.base.frag"
        );
        _spriteShader.Initialize();
        _spriteShader.Compile();
        _spriteShader.Link();
            
        CreateTriangle();
    }

    public override void OnBegin(IRenderContext ctx)
    {
        base.OnBegin(ctx);
        
        _spriteShader.Use();
    }

    public override void OnRender(IRenderContext ctx)
    {
        base.OnRender(ctx);
        
        GL.BindVertexArray(vao);
    }

    public override void OnEnd(IRenderContext ctx)
    {
        base.OnEnd(ctx);
        
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
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