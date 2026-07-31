using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Scene;

public sealed class SceneLightingSettings
{
    private Vector3 _ambientSkyColor = new(0.18f, 0.2f, 0.24f);
    private Vector3 _ambientGroundColor = new(0.05f, 0.045f, 0.04f);
    private float _ambientIntensity = 1.0f;
    private float _directLightIntensityScale = 1.0f;
    private float _specularStrength = 0.08f;
    private float _exposure = 0.0025f;

    public Vector3 AmbientSkyColor
    {
        get => _ambientSkyColor;
        set => _ambientSkyColor = new Vector3(
            Math.Max(0.0f, value.X),
            Math.Max(0.0f, value.Y),
            Math.Max(0.0f, value.Z));
    }

    public Vector3 AmbientGroundColor
    {
        get => _ambientGroundColor;
        set => _ambientGroundColor = new Vector3(
            Math.Max(0.0f, value.X),
            Math.Max(0.0f, value.Y),
            Math.Max(0.0f, value.Z));
    }

    public float AmbientIntensity
    {
        get => _ambientIntensity;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _ambientIntensity = value;
        }
    }

    public float DirectLightIntensityScale
    {
        get => _directLightIntensityScale;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _directLightIntensityScale = value;
        }
    }

    public float SpecularStrength
    {
        get => _specularStrength;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _specularStrength = value;
        }
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
    
    public SceneFogSettings Fog { get; } = new();
    
    public SceneSkyboxSettings Skybox { get; } = new();
}