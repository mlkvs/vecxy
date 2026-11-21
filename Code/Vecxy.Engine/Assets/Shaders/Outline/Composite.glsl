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
uniform vec4 uOccludedColor;
uniform float uThickness;
uniform int uMode;
out vec4 oColor;

// Keep in sync with Vecxy.Rendering.OutlineMode.
const int VisibleOnly = 0;
const int Always = 1;
const int OccludedOnly = 2;
const int VisibleAndOccluded = 3;

void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    if (texelFetch(uMask, pixel, 0).r > 0.5)
        discard;

    ivec2 size = textureSize(uMask, 0);
    float centerDepth = uMode == Always ? 1.0 : texelFetch(uSceneDepth, pixel, 0).r;
    float radius = uThickness + 1.0;
    int extent = int(ceil(radius));
    float coverage = 0.0;
    float occludedCoverage = 0.0;
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
            bool occluded = false;
            if (uMode != Always)
            {
                float depth = texelFetch(uMaskDepth, samplePixel, 0).r;
                float sampleSceneDepth = texelFetch(uSceneDepth, samplePixel, 0).r;
                occluded = depth > min(centerDepth, sampleSceneDepth) + depthTolerance;
            }

            if ((uMode == VisibleOnly && occluded) || (uMode == OccludedOnly && !occluded))
                continue;

            float sampleCoverage = clamp(radius - sqrt(distanceSquared), 0.0, 1.0);
            if (uMode == VisibleAndOccluded && occluded)
            {
                occludedCoverage = max(occludedCoverage, sampleCoverage);
                continue;
            }

            coverage = max(coverage, sampleCoverage);
            if (coverage == 1.0)
            {
                oColor = uColor;
                return;
            }
        }
    }

    float visibleAlpha = uColor.a * coverage;
    // Visible samples take precedence where dilated edges overlap.
    float occludedAlpha = uOccludedColor.a * max(occludedCoverage - coverage, 0.0);
    float alpha = visibleAlpha + occludedAlpha;
    if (alpha <= 0.0)
        discard;
    oColor = vec4((uColor.rgb * visibleAlpha + uOccludedColor.rgb * occludedAlpha) / alpha, alpha);
}
