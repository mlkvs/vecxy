using Vecxy.Assets;
using Vecxy.Diagnostics;

namespace Vecxy.Rendering;

internal sealed class MaterialBinder
{
    private readonly ShaderLibrary _shaders;
    private readonly TextureLibrary _textures;

    public MaterialBinder(
        ShaderLibrary shaders,
        TextureLibrary textures)
    {
        _shaders = shaders;
        _textures = textures;
    }

    public Shader Bind(Material material)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(material);
            var materialAsset = material.Source;
            if (materialAsset is { HasError: true })
            {
                return BindFallback();
            }

            var shaderAsset = material.ShaderSource;
            if (shaderAsset.HasError)
                return BindFallback();

            var shader = _shaders.Get(shaderAsset);
            shader.Bind();
            shader.Set("uAlphaCutoff", material.AlphaCutoff);

            uint textureSlot = 0;
            var textureBound = false;
            foreach (var (name, parameter) in material.Parameters)
            {
                switch (parameter)
                {
                    case TextureMaterialParameter texture:
                        if (texture.Texture.HasError)
                            return BindFallback();

                        _textures.Get(texture.Texture).Bind(textureSlot);
                        shader.Set(name, (int)textureSlot);
                        shader.Set($"{name}Tiling", texture.Tiling);
                        shader.Set($"{name}Offset", texture.Offset);
                        textureBound = true;
                        textureSlot++;
                        break;

                    case EmbeddedTextureMaterialParameter texture:
                        _textures.Get(texture.Texture).Bind(textureSlot);
                        shader.Set(name, (int)textureSlot);
                        shader.Set($"{name}Tiling", texture.Tiling);
                        shader.Set($"{name}Offset", texture.Offset);
                        textureBound = true;
                        textureSlot++;
                        break;

                    case VectorMaterialParameter vector:
                        shader.Set(name, vector.Value);
                        break;

                    case FloatMaterialParameter scalar:
                        shader.Set(name, scalar.Value);
                        break;
                }
            }

            if (!textureBound)
            {
                _textures.GetWhite().Bind(textureSlot);
                shader.Set("uTexture", (int)textureSlot);
                shader.Set("uTextureTiling", System.Numerics.Vector2.One);
                shader.Set("uTextureOffset", System.Numerics.Vector2.Zero);
            }

            return shader;
        }
        catch (Exception exception)
        {
            Logger.Error(
                exception,
                $"Material bind failed, using fallback: {material.SourcePath}");
            return BindFallback();
        }
    }

    private Shader BindFallback()
    {
        var fallback = _shaders.GetFallback();
        fallback.Bind();
        return fallback;
    }
}
