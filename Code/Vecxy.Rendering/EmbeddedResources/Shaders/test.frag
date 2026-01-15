#version 330 core

in vec3 a_TargetColor;
in float a_AlphaColor;

out vec4 FragColor;

void main() 
{
    FragColor = vec4(a_TargetColor, a_AlphaColor);
}