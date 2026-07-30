#type vertex

#version 330 core

layout(location = 0) in vec3 aPosition;

uniform mat4 uViewProjection;

out vec3 vDirection;

void main()
{
    vDirection = aPosition;
    vec4 clipPosition = uViewProjection * vec4(aPosition, 1.0);
    gl_Position = clipPosition.xyww;
}

#type fragment

#version 330 core

in vec3 vDirection;

uniform samplerCube uSkybox;
uniform vec3 uSkyboxTint;
uniform float uSkyboxExposure;
uniform mat4 uSkyboxRotation;

out vec4 oColor;

void main()
{
    vec3 direction =
        normalize((uSkyboxRotation * vec4(vDirection, 0.0)).xyz);
    vec3 color =
        texture(uSkybox, direction).rgb *
        uSkyboxTint *
        uSkyboxExposure;
    oColor = vec4(color, 1.0);
}
