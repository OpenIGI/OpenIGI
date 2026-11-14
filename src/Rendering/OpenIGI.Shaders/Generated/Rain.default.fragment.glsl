#version 430
#if defined(GL_EXT_control_flow_attributes)
#extension GL_EXT_control_flow_attributes : require
#define SPIRV_CROSS_FLATTEN [[flatten]]
#define SPIRV_CROSS_BRANCH [[dont_flatten]]
#define SPIRV_CROSS_UNROLL [[unroll]]
#define SPIRV_CROSS_LOOP [[dont_unroll]]
#else
#define SPIRV_CROSS_FLATTEN
#define SPIRV_CROSS_BRANCH
#define SPIRV_CROSS_UNROLL
#define SPIRV_CROSS_LOOP
#endif

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _58 = (cClipYDown != 0);

layout(binding = 0, std140) uniform RainParams_std140
{
    vec4 fog;
    vec4 screen;
    vec4 lens;
} p;

layout(binding = 0) uniform sampler2D _385;
layout(binding = 1) uniform sampler2D _387;

layout(location = 0) in vec2 input_diffuseUv;
layout(location = 1) in vec4 input_color;
layout(location = 2) in float input_viewDepth;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    vec4 sampled = texture(_385, input_diffuseUv);
    float shape = sampled.w;
    float coverage = shape * input_color.w;
    if (coverage < 0.00200000009499490261077880859375)
    {
        discard;
    }
    float _136 = max(p.lens.z, 1.0);
    float _142 = fract(input_diffuseUv.x * _136) * 2.0;
    float across = _142 - 1.0;
    float _327 = dFdx(input_diffuseUv.x);
    float _330 = dFdy(input_diffuseUv.x);
    vec2 slope = vec2(_327, _330);
    float _163 = max(length(slope), 9.9999999747524270787835121154785e-07);
    float rim = across * across;
    vec2 _199 = gl_FragCoord.xy * p.screen.zw;
    float _204 = p.screen.x * p.screen.w;
    int _331 = 0;
    vec3 _332 = vec3(0.0);
    SPIRV_CROSS_UNROLL
    for (;;)
    {
        if (!(_331 < 8))
        {
            break;
        }
        int _343 = _331;
        float _346 = (float(_343) + 0.5) * 0.785398185253143310546875;
        _331++;
        _332 += textureLod(_387, clamp((_199 + (vec2(cos(_346) / _204, sin(_346)) * 0.119999997317790985107421875)) - vec2(0.0, 0.07999999821186065673828125), vec2(0.0), vec2(1.0)), 0.0).xyz;
        continue;
    }
    float _311 = clamp(coverage * 1.39999997615814208984375, 0.0, 1.0);
    vec4 _313 = vec4((((mix(textureLod(_387, clamp((gl_FragCoord.xy + ((slope / vec2(_163)) * ((1.0 - _142) * max(((1.0 / _136) / _163) * p.lens.x, 1.0)))) * p.screen.zw, vec2(0.0), vec2(1.0)), 0.0).xyz, _332 * 0.125, vec3(clamp(p.lens.y * (0.699999988079071044921875 + (0.60000002384185791015625 * rim)), 0.0, 1.0))) * ((1.0 + (p.lens.w * rim)) - (0.0599999986588954925537109375 * (1.0 - rim)))) + (input_color.xyz * shape)) + ((input_color.xyz * shape) * (exp((-p.fog.w) * max(input_viewDepth, 0.0)) - 1.0))) * _311, _311);
    entryPointParam_fragmentMain = _313;
}

