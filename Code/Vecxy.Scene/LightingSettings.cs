using System.Numerics;

namespace Vecxy.Scene;

public enum EFogMode : byte
{
    Linear,
    Exponential
}

public sealed class SceneLightingSettings
{
    public SceneFogSettings Fog { get; } = new();
    public SceneSkyboxSettings Skybox { get; } = new();
}

public sealed class SceneFogSettings
{
    private Vector3 _color = new(0.03f, 0.04f, 0.06f);
    private float _startDistance = 2.0f;
    private float _endDistance = 20.0f;
    private float _density = 0.08f;
    private float _height = 1.0f;
    private float _heightFalloff = 0.35f;
    private float _volumetricStrength = 0.35f;

    public bool Enabled { get; set; } = true;

    public bool HeightEnabled { get; set; } = true;

    public EFogMode Mode { get; set; } = EFogMode.Linear;

    public Vector3 Color
    {
        get => _color;
        set => _color = new Vector3(
            Math.Max(0.0f, value.X),
            Math.Max(0.0f, value.Y),
            Math.Max(0.0f, value.Z));
    }

    public float StartDistance
    {
        get => _startDistance;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _startDistance = value;
        }
    }

    public float EndDistance
    {
        get => _endDistance;
        set
        {
            if (value <= _startDistance)
                throw new ArgumentOutOfRangeException(nameof(value));

            _endDistance = value;
        }
    }

    public float Density
    {
        get => _density;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _density = value;
        }
    }

    public float Height
    {
        get => _height;
        set => _height = value;
    }

    public float HeightFalloff
    {
        get => _heightFalloff;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _heightFalloff = value;
        }
    }

    public float VolumetricStrength
    {
        get => _volumetricStrength;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _volumetricStrength = value;
        }
    }
}

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
}
