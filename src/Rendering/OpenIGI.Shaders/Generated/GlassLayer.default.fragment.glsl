#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _89 = (cClipYDown != 0);

layout(binding = 0) uniform sampler2D _408;
layout(binding = 1) uniform sampler2D _411;

layout(location = 0) in vec2 input_diffuseUv;
layout(location = 2) in vec4 input_color;
layout(location = 3) in vec3 input_worldNormal;
layout(location = 4) in float input_viewDepth;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    vec4 sampled = texture(_408, input_diffuseUv);
    float alpha = sampled.w * input_color.w;
    bool _194;
    if (alpha >= 0.5)
    {
        _194 = true;
    }
    else
    {
        _194 = alpha < 0.0199999995529651641845703125;
    }
    if (_194)
    {
        discard;
    }
    if (gl_FragCoord.z < texelFetch(_411, ivec3(ivec2(gl_FragCoord.xy), 0).xy, 0).x)
    {
        discard;
    }
    entryPointParam_fragmentMain = vec4(normalize(input_worldNormal), max(input_viewDepth, 9.9999997473787516355514526367188e-05));
}

