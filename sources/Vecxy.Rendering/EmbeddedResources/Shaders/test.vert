#version 330 core

layout (location = 0) in vec3 a_Position;
layout (location = 1) in vec3 a_Color;
layout (location = 2) in float a_Alpha;

out vec3 a_TargetColor;
out float a_AlphaColor;

void main() 
{
    gl_Position = vec4(a_Position, 1.0f);

    a_TargetColor = a_Color;
    a_AlphaColor = a_Alpha;
}