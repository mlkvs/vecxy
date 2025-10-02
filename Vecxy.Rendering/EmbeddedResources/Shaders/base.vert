#version 330
precision mediump float;

in vec2 a_Position;
in vec2 a_TexCoord;

uniform vec2 u_Position;
uniform vec2 u_Size;
uniform mat4 u_Projection;

out vec2 v_TexCoord;

void main()
{
    vec2 worldPos = a_Position * u_Size + u_Position;
    gl_Position = u_Projection * vec4(worldPos, 0.0, 1.0);
    v_TexCoord = a_TexCoord;
}