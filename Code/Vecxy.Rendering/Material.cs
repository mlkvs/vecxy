using Vecxy.Assets;
using Vecxy.Diagnostics;

namespace Vecxy.Rendering;

public sealed class Material
{
    private readonly ShaderLibrary _shaders;
    private readonly TextureLibrary _textures;

    internal Material(
        ShaderLibrary shaders,
        TextureLibrary textures)
    {
        _shaders = shaders;
        _textures = textures;
    }

    public Shader Bind(AssetRef<MaterialAsset> asset)
    {
        try
        {
            if (asset.HasError)
            {
                return BindFallback();
            }

            var material = asset.Value;
            if (material.Shader.HasError ||
                material.Parameters.Values
                    .OfType<TextureMaterialParameter>()
                    .Any(parameter => parameter.Texture.HasError))
            {
                return BindFallback();
            }

            var shader = _shaders.Get(material.Shader);
            shader.Bind();

            uint textureSlot = 0;
            foreach (var (name, parameter) in material.Parameters)
            {
                switch (parameter)
                {
                    case TextureMaterialParameter texture:
                        _textures.Get(texture.Texture).Bind(textureSlot);
                        shader.Set(name, (int)textureSlot);
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

            return shader;
        }
        catch (Exception exception)
        {
            Logger.Error(
                exception,
                $"Material bind failed, using fallback: {asset.Metadata.Path}");
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
