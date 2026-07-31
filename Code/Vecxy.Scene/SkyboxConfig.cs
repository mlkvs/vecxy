using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Scene;

public sealed class SkyboxConfig : IYamlConfig
{
    public bool Enabled { get; set; } = true;
    public string PositiveX { get; set; } = "SkyBox/cubemap/px.png";
    public string NegativeX { get; set; } = "SkyBox/cubemap/nx.png";
    public string PositiveY { get; set; } = "SkyBox/cubemap/py.png";
    public string NegativeY { get; set; } = "SkyBox/cubemap/ny.png";
    public string PositiveZ { get; set; } = "SkyBox/cubemap/pz.png";
    public string NegativeZ { get; set; } = "SkyBox/cubemap/nz.png";
    public float[]? Tint { get; set; } = [1.0f, 1.0f, 1.0f];
    public float[]? Rotation { get; set; } = [0.0f, 0.0f, 0.0f];
    public float Exposure { get; set; } = 1.0f;

    public Vector3 GetTint() => Tint is null ? 
        Vector3.One : 
        new Vector3(Tint[0], Tint[1], Tint[2]);

    public Vector3 GetRotation() => Rotation is null ? 
        Vector3.Zero : 
        new Vector3(Rotation[0], Rotation[1], Rotation[2]);

    public void Validate()
    {
        _ = GetTint();
        _ = GetRotation();
        ValidateFace(PositiveX, nameof(PositiveX));
        ValidateFace(NegativeX, nameof(NegativeX));
        ValidateFace(PositiveY, nameof(PositiveY));
        ValidateFace(NegativeY, nameof(NegativeY));
        ValidateFace(PositiveZ, nameof(PositiveZ));
        ValidateFace(NegativeZ, nameof(NegativeZ));

        if (Exposure < 0.0f)
        {
            throw new InvalidDataException(
                $"SkyboxConfig cannot have negative exposure.");
        }
    }

    private static void ValidateFace(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                $"SkyboxConfig has empty face path '{name}'.");
        }
    }
}
