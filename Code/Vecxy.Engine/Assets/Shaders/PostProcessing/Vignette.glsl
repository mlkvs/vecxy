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
uniform int uVignetteEnabled;
uniform int uVignetteRoundEnabled;
uniform int uVignetteSidesEnabled;
uniform vec4 uVignetteColor;
uniform float uVignetteIntensity;
uniform float uVignetteRoundRadius;
uniform float uVignetteRoundSoftness;
uniform vec4 uVignetteSides;
uniform float uVignetteEdgeSoftness;

out vec4 oColor;

float computeRoundMask(vec2 uv)
{
    vec2 centered = uv * 2.0 - 1.0;
    float distanceFromCenter = length(centered);

    return smoothstep(
        uVignetteRoundRadius,
        uVignetteRoundRadius + uVignetteRoundSoftness,
        distanceFromCenter);
}

float computeSideMask(vec2 uv)
{
    float topMask = 0.0;
    float rightMask = 0.0;
    float bottomMask = 0.0;
    float leftMask = 0.0;

    if (uVignetteSides.x > 0.0)
    {
        topMask = 1.0 - smoothstep(
            uVignetteSides.x - uVignetteEdgeSoftness,
            uVignetteSides.x,
            uv.y);
    }

    if (uVignetteSides.y > 0.0)
    {
        rightMask = smoothstep(
            1.0 - uVignetteSides.y,
            1.0 - uVignetteSides.y + uVignetteEdgeSoftness,
            uv.x);
    }

    if (uVignetteSides.z > 0.0)
    {
        bottomMask = 1.0 - smoothstep(
            uVignetteSides.z - uVignetteEdgeSoftness,
            uVignetteSides.z,
            1.0 - uv.y);
    }

    if (uVignetteSides.w > 0.0)
    {
        leftMask = 1.0 - smoothstep(
            uVignetteSides.w - uVignetteEdgeSoftness,
            uVignetteSides.w,
            uv.x);
    }

    return clamp(max(max(topMask, rightMask), max(bottomMask, leftMask)), 0.0, 1.0);
}

void main()
{
    vec3 color = texture(uSceneTexture, clamp(vTexCoord, 0.0, 1.0)).rgb;

    if (uVignetteEnabled != 0)
    {
        float mask = 0.0;

        if (uVignetteRoundEnabled != 0)
            mask = max(mask, computeRoundMask(vTexCoord));

        if (uVignetteSidesEnabled != 0)
            mask = max(mask, computeSideMask(vTexCoord));

        float strength = clamp(mask * uVignetteIntensity * uVignetteColor.a, 0.0, 1.0);
        color = mix(color, uVignetteColor.rgb, strength);
    }

    oColor = vec4(color, 1.0);
}
