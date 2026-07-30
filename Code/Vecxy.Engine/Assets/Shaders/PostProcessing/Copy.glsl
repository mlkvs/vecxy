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

out vec4 oColor;

void main()
{
    oColor = texture(uSceneTexture, clamp(vTexCoord, 0.0, 1.0));
}
