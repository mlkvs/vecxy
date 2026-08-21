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
uniform sampler2D uNormalTexture;
uniform sampler2D uMetallicRoughnessTexture;
uniform float uHasNormalTexture;
uniform float uHasMetallicRoughnessTexture;
uniform float uMetallicFactor;
uniform float uRoughnessFactor;
uniform vec4 uEmissiveColor;
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

vec3 applyNormalMap(vec3 geometricNormal, vec2 uv)
{
    if (uHasNormalTexture < 0.5)
        return normalize(geometricNormal);

    vec3 tangentNormal = texture(uNormalTexture, uv).xyz * 2.0 - 1.0;
    vec3 dp1 = dFdx(vWorldPosition);
    vec3 dp2 = dFdy(vWorldPosition);
    vec2 duv1 = dFdx(uv);
    vec2 duv2 = dFdy(uv);
    vec3 tangent = normalize(dp1 * duv2.y - dp2 * duv1.y);
    tangent = normalize(tangent - geometricNormal * dot(geometricNormal, tangent));
    vec3 bitangent = normalize(cross(geometricNormal, tangent));
    return normalize(mat3(tangent, bitangent, geometricNormal) * tangentNormal);
}

float distributionGgx(vec3 normal, vec3 halfVector, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float nDotH = max(dot(normal, halfVector), 0.0);
    float nDotH2 = nDotH * nDotH;
    float denominator = nDotH2 * (a2 - 1.0) + 1.0;
    return a2 / max(3.14159265 * denominator * denominator, 0.0001);
}

float geometrySchlickGgx(float nDotDirection, float roughness)
{
    float k = (roughness + 1.0);
    k = k * k / 8.0;
    return nDotDirection / max(nDotDirection * (1.0 - k) + k, 0.0001);
}

float geometrySmith(
    vec3 normal,
    vec3 viewDirection,
    vec3 lightDirection,
    float roughness)
{
    float nDotV = max(dot(normal, viewDirection), 0.0);
    float nDotL = max(dot(normal, lightDirection), 0.0);
    return
        geometrySchlickGgx(nDotV, roughness) *
        geometrySchlickGgx(nDotL, roughness);
}

vec3 fresnelSchlick(float cosine, vec3 f0)
{
    return f0 + (1.0 - f0) * pow(1.0 - cosine, 5.0);
}

vec3 evaluateDirectLight(
    vec3 normal,
    vec3 viewDirection,
    vec3 lightDirection,
    vec3 radiance,
    vec3 baseColor,
    float metallic,
    float roughness)
{
    float nDotL = max(dot(normal, lightDirection), 0.0);
    float nDotV = max(dot(normal, viewDirection), 0.0);
    if (nDotL <= 0.0 || nDotV <= 0.0)
        return vec3(0.0);

    vec3 halfVector = normalize(viewDirection + lightDirection);
    vec3 f0 = mix(vec3(0.04), baseColor, metallic);
    vec3 fresnel = fresnelSchlick(
        max(dot(halfVector, viewDirection), 0.0),
        f0);
    float distribution = distributionGgx(normal, halfVector, roughness);
    float geometry = geometrySmith(
        normal,
        viewDirection,
        lightDirection,
        roughness);
    vec3 specular =
        distribution * geometry * fresnel /
        max(4.0 * nDotV * nDotL, 0.0001);

    // Fresnel-reflected energy is unavailable to the diffuse lobe. Metals do
    // not have a diffuse component, so this also keeps the BRDF conservative.
    vec3 diffuseWeight = (vec3(1.0) - fresnel) * (1.0 - metallic);
    vec3 diffuse = diffuseWeight * baseColor / 3.14159265;
    return (diffuse + specular) * radiance * nDotL;
}

void main()
{
    vec2 uv = vTexCoord * uTextureTiling + uTextureOffset;
    vec4 textureColor = texture(uTexture, uv);

    if (textureColor.a * uColor.a * uTint.a < uAlphaCutoff)
        discard;

    vec3 baseColor = pow(textureColor.rgb, vec3(2.2)) * uColor.rgb * uTint.rgb;
    float metallic = clamp(uMetallicFactor, 0.0, 1.0);
    float roughness = clamp(uRoughnessFactor, 0.04, 1.0);
    if (uHasMetallicRoughnessTexture >= 0.5)
    {
        vec4 mr = texture(uMetallicRoughnessTexture, uv);
        metallic *= mr.b;
        roughness *= mr.g;
        roughness = clamp(roughness, 0.04, 1.0);
    }
    vec3 normal = applyNormalMap(normalize(vNormal), uv);
    if (!gl_FrontFacing)
        normal = -normal;

    vec3 viewVector = uCameraPosition - vWorldPosition;
    vec3 viewDirection = length(viewVector) > 0.0
        ? normalize(viewVector)
        : vec3(0.0, 0.0, 1.0);
    float upFactor = clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
    vec3 ambientLighting = mix(
        uAmbientGroundColor,
        uAmbientSkyColor,
        upFactor);
    vec3 directLighting = vec3(0.0);

    for (int i = 0; i < uDirectionalLightCount; ++i)
    {
        vec3 lightDirection = normalize(-uDirectionalLights[i].direction);
        vec3 radiance =
            uDirectionalLights[i].color *
            uDirectionalLights[i].intensity;
        directLighting += evaluateDirectLight(
            normal,
            viewDirection,
            lightDirection,
            radiance,
            baseColor,
            metallic,
            roughness);
    }

    for (int i = 0; i < uPointLightCount; ++i)
    {
        vec3 toLight = uPointLights[i].position - vWorldPosition;
        float distanceToLight = length(toLight);
        vec3 lightDirection = distanceToLight > 0.0
            ? toLight / distanceToLight
            : vec3(0.0, 1.0, 0.0);

        float attenuation = computeRangeFalloff(distanceToLight, uPointLights[i].range);
        vec3 radiance =
            uPointLights[i].color *
            uPointLights[i].intensity *
            attenuation;
        directLighting += evaluateDirectLight(
            normal,
            viewDirection,
            lightDirection,
            radiance,
            baseColor,
            metallic,
            roughness);
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
            computeRangeFalloff(distanceToLight, uSpotLights[i].range) * cone;
        vec3 radiance =
            uSpotLights[i].color *
            uSpotLights[i].intensity *
            attenuation;
        directLighting += evaluateDirectLight(
            normal,
            viewDirection,
            lightDirection,
            radiance,
            baseColor,
            metallic,
            roughness);
    }

    // Approximate image-based lighting from the sky/ground environment. This
    // gives metal surfaces a stable reflection even when no reflection probe
    // is present in the scene.
    vec3 reflectedEnvironment = mix(uAmbientGroundColor, uAmbientSkyColor,
        clamp(reflect(-viewDirection, normal).y * 0.5 + 0.5, 0.0, 1.0));
    vec3 environmentSpecular = reflectedEnvironment * fresnelSchlick(
        max(dot(normal, viewDirection), 0.0),
        mix(vec3(0.04), baseColor, metallic)) * (1.0 - roughness) * uSpecularStrength * 4.0;
    vec3 indirectDiffuse = baseColor * ambientLighting * (1.0 - metallic);
    vec3 litColor =
        (indirectDiffuse + directLighting + environmentSpecular + uEmissiveColor.rgb) *
        uExposure;
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
