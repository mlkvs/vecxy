namespace Vecxy.Rendering;

public class Material(ShaderProgram shader)
{
    public ShaderProgram Shader { get; } = shader;
    public Texture Texture { get; private set; }

    public void SetTexture(Texture texture)
    {
        Texture = texture;
    }
}