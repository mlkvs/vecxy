#version 100

attribute vec2 a_Position;
attribute vec2 a_TexCoord;

uniform vec2 u_Position;
uniform vec2 u_Size;

varying vec2 v_TexCoord;

void main()
{
    vec2 pos = a_Position * u_Size + u_Position;
    
    gl_Position = vec4(pos, 0.0, 1.0);
    
    v_TexCoord = a_TexCoord;
}