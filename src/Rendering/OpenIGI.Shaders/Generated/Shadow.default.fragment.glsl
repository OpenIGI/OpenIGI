#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _116 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_0
#define SPIRV_CROSS_CONSTANT_ID_0 0.501960813999176025390625
#endif
const float cAlphaReference = SPIRV_CROSS_CONSTANT_ID_0;

layout(binding = 0) uniform sampler2D _217;

layout(location = 0) in vec2 input_diffuseUv;
layout(location = 1) in float input_alpha;

void main()
{
    if ((texture(_217, input_diffuseUv).w * input_alpha) < cAlphaReference)
    {
        discard;
    }
}

