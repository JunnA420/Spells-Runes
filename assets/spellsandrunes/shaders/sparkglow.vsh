
#version 330 core
layout(location = 0) in vec3 position;
layout(location = 1) in vec2 uv;
layout(location = 2) in vec4 color;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
out vec4 vColor;
out vec2 vUV;
void main() {
    vColor = color / 255.0;
    vUV = uv;
    gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}