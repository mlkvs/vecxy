using System.Numerics;

namespace Vecxy.Scene;

public sealed class SceneSkyboxSettings
{
    private Vector3 _tint = Vector3.One;
    private float _exposure = 1.0f;
    private Vector3 _rotation;

    public bool Enabled { get; set; }

    public string PositiveX { get; set; } = string.Empty;

    public string NegativeX { get; set; } = string.Empty;

    public string PositiveY { get; set; } = string.Empty;

    public string NegativeY { get; set; } = string.Empty;

    public string PositiveZ { get; set; } = string.Empty;

    public string NegativeZ { get; set; } = string.Empty;

    public Vector3 Tint
    {
        get => _tint;
        set => _tint = new Vector3(
            Math.Max(0.0f, value.X),
            Math.Max(0.0f, value.Y),
            Math.Max(0.0f, value.Z));
    }

    public float Exposure
    {
        get => _exposure;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _exposure = value;
        }
    }

    public Vector3 Rotation
    {
        get => _rotation;
        set => _rotation = value;
    }

    public bool HasAllFaces =>
        !string.IsNullOrWhiteSpace(PositiveX) &&
        !string.IsNullOrWhiteSpace(NegativeX) &&
        !string.IsNullOrWhiteSpace(PositiveY) &&
        !string.IsNullOrWhiteSpace(NegativeY) &&
        !string.IsNullOrWhiteSpace(PositiveZ) &&
        !string.IsNullOrWhiteSpace(NegativeZ);
    
    public void ApplyConfig(SkyboxConfig config)
    {
        Enabled = config.Enabled;
        PositiveX = config.PositiveX;
        NegativeX = config.NegativeX;
        PositiveY = config.PositiveY;
        NegativeY = config.NegativeY;
        PositiveZ = config.PositiveZ;
        NegativeZ = config.NegativeZ;
        Tint = config.GetTint();
        Rotation = config.GetRotation();
        Exposure = config.Exposure;
    }
}