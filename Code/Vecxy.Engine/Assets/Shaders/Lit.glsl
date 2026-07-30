#type vertex

#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

uniform mat4 uModel;
uniform mat4 uTransform;

out vec3 vNormal;
out vec3 vWorldPosition;
out vec2 vTexCoord;

void main()
{
    mat3 normalMatrix = transpose(inverse(mat3(uModel)));

    vNormal = normalMatrix * aNormal;
    vWorldPosition = (uModel * vec4(aPosition, 1.0)).xyz;
    vTexCoord = aTexCoord;

    gl_Position = uTransform * vec4(aPosition, 1.0);
}

#type fragment

#version 330 core

in vec3 vNormal;
in vec3 vWorldPosition;
in vec2 vTexCoord;

uniform sampler2D uTexture;
uniform vec2 uTextureTiling;
uniform vec2 uTextureOffset;
uniform vec4 uColor;
uniform vec4 uTint;
uniform float uAlphaCutoff;
uniform vec3 uAmbientSkyColor;
uniform vec3 uAmbientGroundColor;
uniform float uSpecularStrength;
uniform vec3 uCameraPosition;
uniform float uExposure;
uniform int uFogEnabled;
uniform int uFogMode;
uniform vec3 uFogColor;
uniform float uFogStart;
uniform float uFogEnd;
uniform float uFogDensity;
uniform int uHeightFogEnabled;
uniform float uFogHeight;
uniform float uFogHeightFalloff;
uniform float uFogVolumetricStrength;

struct PointLight
{
    vec3 position;
    vec3 color;
    float intensity;
    float range;
};

struct SpotLight
{
    vec3 position;
    vec3 direction;
    vec3 color;
    float intensity;
    float range;
    float innerConeCos;
    float outerConeCos;
};

struct DirectionalLight
{
    vec3 direction;
    vec3 color;
    float intensity;
};

uniform int uPointLightCount;
uniform int uSpotLightCount;
uniform int uDirectionalLightCount;
uniform PointLight uPointLights[8];
uniform SpotLight uSpotLights[8];
uniform DirectionalLight uDirectionalLights[4];

out vec4 oColor;

float computeRangeFalloff(float distanceToLight, float range)
{
    if (range <= 0.0)
        return 1.0 / max(distanceToLight * distanceToLight, 0.0001);

    float normalizedDistance = clamp(distanceToLight / range, 0.0, 1.0);
    float falloff = 1.0 - normalizedDistance * normalizedDistance;

    return (falloff * falloff) / max(distanceToLight * distanceToLight, 0.0001);
}

float computeBaseFogFactor(float distanceToCamera)
{
    if (uFogMode == 0)
    {
        return clamp(
            (distanceToCamera - uFogStart) /
            max(uFogEnd - uFogStart, 0.0001),
            0.0,
            1.0);
    }

    return clamp(
        1.0 - exp(-distanceToCamera * uFogDensity),
        0.0,
        1.0);
}

float computeHeightFogFactor(float worldY)
{
    if (uHeightFogEnabled == 0)
        return 1.0;

    float heightDelta = max(worldY - uFogHeight, 0.0);
    return exp(-heightDelta * uFogHeightFalloff);
}

void main()
{
    vec2 uv = vTexCoord * uTextureTiling + uTextureOffset;
    vec4 textureColor = texture(uTexture, uv);

    if (textureColor.a * uColor.a * uTint.a < uAlphaCutoff)
        discard;

    vec3 baseColor = (textureColor * uColor * uTint).rgb;
    vec3 normal = normalize(vNormal);
    if (!gl_FrontFacing)
        normal = -normal;

    vec3 viewVector = uCameraPosition - vWorldPosition;
    vec3 viewDirection = length(viewVector) > 0.0
        ? normalize(viewVector)
        : vec3(0.0, 0.0, 1.0);
    float upFactor = clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
    vec3 lighting = mix(
        uAmbientGroundColor,
        uAmbientSkyColor,
        upFactor);

    for (int i = 0; i < uDirectionalLightCount; ++i)
    {
        vec3 lightDirection = normalize(-uDirectionalLights[i].direction);
        float diffuse = max(dot(normal, lightDirection), 0.0);
        vec3 halfVector = normalize(lightDirection + viewDirection);
        float specular = diffuse > 0.0
            ? pow(max(dot(normal, halfVector), 0.0), 32.0) * uSpecularStrength
            : 0.0;

        lighting +=
            uDirectionalLights[i].color *
            uDirectionalLights[i].intensity *
            (diffuse + specular);
    }

    for (int i = 0; i < uPointLightCount; ++i)
    {
        vec3 toLight = uPointLights[i].position - vWorldPosition;
        float distanceToLight = length(toLight);
        vec3 lightDirection = distanceToLight > 0.0
            ? toLight / distanceToLight
            : vec3(0.0, 1.0, 0.0);

        float diffuse = max(dot(normal, lightDirection), 0.0);
        vec3 halfVector = normalize(lightDirection + viewDirection);
        float specular = diffuse > 0.0
            ? pow(max(dot(normal, halfVector), 0.0), 32.0) * uSpecularStrength
            : 0.0;
        float attenuation = computeRangeFalloff(distanceToLight, uPointLights[i].range);

        lighting +=
            uPointLights[i].color *
            uPointLights[i].intensity *
            attenuation *
            (diffuse + specular);
    }

    for (int i = 0; i < uSpotLightCount; ++i)
    {
        vec3 toLight = uSpotLights[i].position - vWorldPosition;
        float distanceToLight = length(toLight);
        vec3 lightDirection = distanceToLight > 0.0
            ? toLight / distanceToLight
            : vec3(0.0, 1.0, 0.0);

        float spotCos = dot(-lightDirection, normalize(uSpotLights[i].direction));
        float cone = smoothstep(
            uSpotLights[i].outerConeCos,
            uSpotLights[i].innerConeCos,
            spotCos);

        if (cone <= 0.0)
            continue;

        float diffuse = max(dot(normal, lightDirection), 0.0);
        vec3 halfVector = normalize(lightDirection + viewDirection);
        float specular = diffuse > 0.0
            ? pow(max(dot(normal, halfVector), 0.0), 32.0) * uSpecularStrength
            : 0.0;
        float attenuation =
            computeRangeFalloff(distanceToLight, uSpotLights[i].range) * cone;

        lighting +=
            uSpotLights[i].color *
            uSpotLights[i].intensity *
            attenuation *
            (diffuse + specular);
    }

    vec3 litColor = baseColor * lighting * uExposure;
    vec3 mapped = vec3(1.0) - exp(-litColor);
    vec3 gammaCorrected = pow(mapped, vec3(1.0 / 2.2));

    if (uFogEnabled != 0)
    {
        float distanceToCamera = length(uCameraPosition - vWorldPosition);
        float fogFactor =
            computeBaseFogFactor(distanceToCamera) *
            computeHeightFogFactor(vWorldPosition.y);

        vec3 fogColor = uFogColor;

        if (uFogVolumetricStrength > 0.0)
        {
            vec3 inScattering = vec3(0.0);

            for (int i = 0; i < uDirectionalLightCount; ++i)
            {
                inScattering +=
                    uDirectionalLights[i].color *
                    uDirectionalLights[i].intensity;
            }

            for (int i = 0; i < uPointLightCount; ++i)
            {
                vec3 toLight = uPointLights[i].position - vWorldPosition;
                float distanceToLight = length(toLight);
                float attenuation =
                    computeRangeFalloff(distanceToLight, uPointLights[i].range);

                inScattering +=
                    uPointLights[i].color *
                    uPointLights[i].intensity *
                    attenuation;
            }

            for (int i = 0; i < uSpotLightCount; ++i)
            {
                vec3 toLight = uSpotLights[i].position - vWorldPosition;
                float distanceToLight = length(toLight);
                vec3 lightDirection = distanceToLight > 0.0
                    ? toLight / distanceToLight
                    : vec3(0.0, 1.0, 0.0);

                float spotCos = dot(-lightDirection, normalize(uSpotLights[i].direction));
                float cone = smoothstep(
                    uSpotLights[i].outerConeCos,
                    uSpotLights[i].innerConeCos,
                    spotCos);

                if (cone <= 0.0)
                    continue;

                float attenuation =
                    computeRangeFalloff(distanceToLight, uSpotLights[i].range) *
                    cone;

                inScattering +=
                    uSpotLights[i].color *
                    uSpotLights[i].intensity *
                    attenuation;
            }

            fogColor += inScattering * uExposure * uFogVolumetricStrength;
        }

        gammaCorrected = mix(
            gammaCorrected,
            fogColor,
            fogFactor);
    }

    oColor = vec4(
        gammaCorrected,
        textureColor.a * uColor.a * uTint.a);
}
