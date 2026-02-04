namespace Vecxy.Rendering;

public enum SHADER_TYPE
{
    VERTEX,
    FRAGMENT
}

public class Shader(string Src)
{

}

public class ShaderProgram(Shader VertShader, Shader FragShader)
{
    public void Compile()
    {

    }
}