#version 330 core

in vec4 vColor;
in vec2 vUV;

out vec4 outColor;

void main() {
    // Radial soft circle — bright center fading to transparent edge
    float d    = length(vUV - 0.5) * 2.0;  // 0 at center, 1 at edge
    float alpha = max(0.0, 1.0 - d * d);   // quadratic falloff

    outColor = vec4(vColor.rgb, vColor.a * alpha);
}
