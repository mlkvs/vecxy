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

uniform sampler2D uTexture;
uniform vec4 uColor;
uniform float uAlphaCutoff;
uniform int uFlipX;
uniform int uFlipY;
uniform vec4 uUvRect;

out vec4 oColor;

void main()
{
    // TextureAsset stores its first source row at v=0. The shared quad uses
    // the conventional OpenGL bottom-to-top UV direction, hence the Y flip.
    vec2 frameUv = vec2(vTexCoord.x, 1.0 - vTexCoord.y);
    if (uFlipX != 0)
        frameUv.x = 1.0 - frameUv.x;
    if (uFlipY != 0)
        frameUv.y = 1.0 - frameUv.y;
    vec2 uv = mix(uUvRect.xy, uUvRect.zw, frameUv);

    vec4 color = texture(uTexture, uv) * uColor;
    if (color.a <= uAlphaCutoff)
        discard;

    oColor = color;
}
