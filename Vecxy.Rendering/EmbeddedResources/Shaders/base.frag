#version 330
precision mediump float;

uniform sampler2D u_Texture;
uniform vec4 u_Color;

in vec2 v_TexCoord;
out vec4 FragColor;

void main()
{
    vec4 texColor = texture(u_Texture, v_TexCoord);
    FragColor = texColor * u_Color;
}