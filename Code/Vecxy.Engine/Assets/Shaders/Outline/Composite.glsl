#type vertex
#version 330 core

layout(location = 0) in vec2 aPosition;

void main()
{
    gl_Position = vec4(aPosition * 2.0, 0.0, 1.0);
}

#type fragment
#version 330 core

uniform sampler2D uMask;
#ifdef GL_ES
#define DEPTH_PRECISION highp
#else
#define DEPTH_PRECISION
#endif
uniform DEPTH_PRECISION sampler2D uMaskDepth;
uniform DEPTH_PRECISION sampler2D uSceneDepth;
uniform vec4 uColor;
uniform float uThickness;
uniform int uThroughWalls;
out vec4 oColor;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    if (texelFetch(uMask, pixel, 0).r > 0.5)
        discard;

    ivec2 size = textureSize(uMask, 0);
    float centerDepth = uThroughWalls != 0 ? 1.0 : texelFetch(uSceneDepth, pixel, 0).r;
    float radius = uThickness + 1.0;
    int extent = int(ceil(radius));
    float coverage = 0.0;
    const float depthTolerance = 0.000001;
    for (int y = -extent; y <= extent; ++y)
    {
        for (int x = -extent; x <= extent; ++x)
        {
            vec2 delta = vec2(x, y);
            float distanceSquared = dot(delta, delta);
            if (distanceSquared >= radius * radius)
                continue;

            ivec2 samplePixel = pixel + ivec2(x, y);
            if (any(lessThan(samplePixel, ivec2(0))) || any(greaterThanEqual(samplePixel, size)))
                continue;
            if (texelFetch(uMask, samplePixel, 0).r <= 0.5)
                continue;
            if (uThroughWalls == 0)
            {
                float depth = texelFetch(uMaskDepth, samplePixel, 0).r;
                float sampleSceneDepth = texelFetch(uSceneDepth, samplePixel, 0).r;
                if (depth > min(centerDepth, sampleSceneDepth) + depthTolerance)
                    continue;
            }

            coverage = max(coverage, clamp(radius - sqrt(distanceSquared), 0.0, 1.0));
            if (coverage == 1.0)
            {
                oColor = uColor;
                return;
            }
        }
    }

    if (coverage <= 0.0)
        discard;
    oColor = vec4(uColor.rgb, uColor.a * coverage);
}
