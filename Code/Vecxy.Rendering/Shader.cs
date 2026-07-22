namespace Vecxy.Rendering;

public enum SHADER_TYPE
{
    VERTEX,
    FRAGMENT
}

public sealed class Shader(string source, SHADER_TYPE type)
{
    public string Source { get; } = source;
    public SHADER_TYPE Type { get; } = type;
}

public sealed class ShaderProgram(Shader vertexShader, Shader fragmentShader)
{
    public Shader VertexShader { get; } = vertexShader;
    public Shader FragmentShader { get; } = fragmentShader;

    public void Compile()
    {
        if (VertexShader.Type != SHADER_TYPE.VERTEX)
            throw new ArgumentException("Vertex shader has an invalid type.", nameof(VertexShader));
        if (FragmentShader.Type != SHADER_TYPE.FRAGMENT)
            throw new ArgumentException("Fragment shader has an invalid type.", nameof(FragmentShader));
    }
}
