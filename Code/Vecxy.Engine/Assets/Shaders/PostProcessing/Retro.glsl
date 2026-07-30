#type vertex

#version 330 core

layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;

uniform mat4 uTransform;

out vec2 vTexCoord;

void main()
{
    vTexCoord = aTexCoord;
    gl_Position = uTransform * vec4(aPosition, 0.0, 1.0);
}

#type fragment

#version 330 core

in vec2 vTexCoord;

uniform sampler2D uSceneTexture;
uniform vec2 uResolution;
uniform float uTime;
uniform int uPixelWidth;
uniform float uJitterStrength;
uniform float uWarpStrength;
uniform float uDriftStrength;

out vec4 oColor;

vec2 applyPixelation(vec2 uv)
{
    float pixelWidth = max(float(uPixelWidth), 1.0);
    float aspect = uResolution.y / max(uResolution.x, 1.0);
    vec2 grid = vec2(pixelWidth, pixelWidth * aspect);

    return (floor(uv * grid) + 0.5) / grid;
}

void main()
{
    vec2 uv = vTexCoord;
    vec2 centered = uv * 2.0 - 1.0;

    float waveX =
        sin(centered.y * 18.0 + uTime * 2.7) *
        cos(centered.y * 11.0 - uTime * 1.9);
    float waveY =
        sin(centered.x * 14.0 - uTime * 2.3) *
        cos(centered.x * 9.0 + uTime * 1.4);

    uv.x += waveX * uWarpStrength;
    uv.y += waveY * uWarpStrength * 0.75;

    vec2 snapOffset = vec2(
        sin(uTime * 30.0 + centered.y * 40.0),
        cos(uTime * 26.0 + centered.x * 36.0)) *
        uJitterStrength;

    uv += snapOffset;
    uv = applyPixelation(uv);

    vec2 drift = vec2(
        sin(uTime * 0.9) * uDriftStrength,
        cos(uTime * 0.7) * uDriftStrength * 0.5);

    vec3 color;
    color.r = texture(uSceneTexture, clamp(uv + drift, 0.0, 1.0)).r;
    color.g = texture(uSceneTexture, clamp(uv, 0.0, 1.0)).g;
    color.b = texture(uSceneTexture, clamp(uv - drift, 0.0, 1.0)).b;

    oColor = vec4(color, 1.0);
}
