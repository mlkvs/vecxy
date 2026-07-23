using System.Numerics;

namespace Vecxy.Rendering._Legacy;

public enum CullMode { None, Back, Front }

public sealed class Material(ShaderProgram shader)
{
    private readonly Dictionary<string, int> _ints = [];
    private readonly Dictionary<string, float> _floats = [];
    private readonly Dictionary<string, Vector4> _vectors = [];
    private readonly Dictionary<string, Texture2D> _textures = [];

    public ShaderProgram Shader { get; } = shader ?? throw new ArgumentNullException(nameof(shader));
    public bool DepthTest { get; set; }
    public bool Blending { get; set; } = true;
    public CullMode CullMode { get; set; }

    public Material Set(string name, int value) { _ints[name] = value; return this; }
    public Material Set(string name, float value) { _floats[name] = value; return this; }
    public Material Set(string name, Vector4 value) { _vectors[name] = value; return this; }
    public Material SetTexture(string name, Texture2D texture) { _textures[name] = texture; return this; }

    internal void Bind()
    {
        Shader.Bind();
        foreach (var (name, value) in _ints) Shader.Set(name, value);
        foreach (var (name, value) in _floats) Shader.Set(name, value);
        foreach (var (name, value) in _vectors) Shader.Set(name, value);
        uint unit = 0;
        foreach (var (name, texture) in _textures)
        {
            texture.Bind(unit);
            Shader.Set(name, (int)unit++);
        }
    }
}
