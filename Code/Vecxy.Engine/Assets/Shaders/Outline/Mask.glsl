#type vertex
#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in vec4 aJoints;
layout(location = 4) in vec4 aWeights;

uniform mat4 uTransform;
uniform int uSkinningEnabled;
uniform mat4 uBones[64];

out vec2 vTexCoord;

void main()
{
    mat4 skin = mat4(1.0);
    if (uSkinningEnabled != 0)
    {
        skin =
            uBones[int(aJoints.x)] * aWeights.x +
            uBones[int(aJoints.y)] * aWeights.y +
            uBones[int(aJoints.z)] * aWeights.z +
            uBones[int(aJoints.w)] * aWeights.w;
    }
    gl_Position = uTransform * (skin * vec4(aPosition, 1.0));
    vTexCoord = aTexCoord;
}

#type fragment
#version 330 core

in vec2 vTexCoord;
uniform sampler2D uTexture;
uniform vec2 uTextureTiling;
uniform vec2 uTextureOffset;
uniform float uAlpha;
uniform float uAlphaCutoff;
out vec4 oColor;

void main()
{
    float alpha = texture(uTexture, vTexCoord * uTextureTiling + uTextureOffset).a * uAlpha;
    if (alpha < uAlphaCutoff)
        discard;
    oColor = vec4(1.0);
}
