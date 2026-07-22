using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Rendering;

public interface IRenderer
{
    int Width { get; }
    int Height { get; }
    IRenderCamera Camera { get; set; }
    Material FallbackMaterial { get; }
    Texture2D FallbackTexture { get; }
    RenderStats Stats { get; }
    ScreenRect ScreenBounds { get; }

    ShaderProgram CreateShader(string vertexSource, string fragmentSource, string name = "Shader");
    ShaderProgram CreateShader(TextAsset vertexShader, TextAsset fragmentShader);
    Mesh CreateMesh(ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices);
    Texture2D CreateTexture(ImageAsset image, TextureOptions options = default);
    Material CreateMaterial(ShaderProgram shader);

    void BeginFrame(Color clearColor);
    void Submit(Mesh mesh, Material material, Matrix4x4 transform);
    void MarkStaticBatch(int sourceObjects);
    void EndFrame();
}
