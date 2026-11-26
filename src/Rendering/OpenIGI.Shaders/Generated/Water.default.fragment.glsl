#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_1
#define SPIRV_CROSS_CONSTANT_ID_1 0.0
#endif
const float cSunX = SPIRV_CROSS_CONSTANT_ID_1;
#ifndef SPIRV_CROSS_CONSTANT_ID_2
#define SPIRV_CROSS_CONSTANT_ID_2 0.0
#endif
const float cSunY = SPIRV_CROSS_CONSTANT_ID_2;
#ifndef SPIRV_CROSS_CONSTANT_ID_3
#define SPIRV_CROSS_CONSTANT_ID_3 0.0
#endif
const float cSunZ = SPIRV_CROSS_CONSTANT_ID_3;
#ifndef SPIRV_CROSS_CONSTANT_ID_9
#define SPIRV_CROSS_CONSTANT_ID_9 0.0
#endif
const float cSunRed = SPIRV_CROSS_CONSTANT_ID_9;
#ifndef SPIRV_CROSS_CONSTANT_ID_10
#define SPIRV_CROSS_CONSTANT_ID_10 0.0
#endif
const float cSunGreen = SPIRV_CROSS_CONSTANT_ID_10;
#ifndef SPIRV_CROSS_CONSTANT_ID_11
#define SPIRV_CROSS_CONSTANT_ID_11 0.0
#endif
const float cSunBlue = SPIRV_CROSS_CONSTANT_ID_11;
#ifndef SPIRV_CROSS_CONSTANT_ID_6
#define SPIRV_CROSS_CONSTANT_ID_6 0.0
#endif
const float cAmbientRed = SPIRV_CROSS_CONSTANT_ID_6;
#ifndef SPIRV_CROSS_CONSTANT_ID_7
#define SPIRV_CROSS_CONSTANT_ID_7 0.0
#endif
const float cAmbientGreen = SPIRV_CROSS_CONSTANT_ID_7;
#ifndef SPIRV_CROSS_CONSTANT_ID_8
#define SPIRV_CROSS_CONSTANT_ID_8 0.0
#endif
const float cAmbientBlue = SPIRV_CROSS_CONSTANT_ID_8;
#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _187 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _259 = (cSamplerDecodes != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_12
#define SPIRV_CROSS_CONSTANT_ID_12 0
#endif
const int cLinearScene = SPIRV_CROSS_CONSTANT_ID_12;
const bool _306 = (cLinearScene != 0);

layout(binding = 0, std140) uniform WaterPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 eyeFacing;
    vec4 diffuseScale;
    vec4 specularFog;
    vec4 fogNormalScale;
} push;

layout(binding = 0) uniform samplerCube _427;

layout(location = 2) in vec3 input_reflection;
layout(location = 0) in vec4 input_colour0;
layout(location = 1) in vec3 input_colour1;
layout(location = 3) in float input_fog;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    vec4 sampled = texture(_427, input_reflection);
    vec3 _249 = sampled.xyz;
    vec3 _359;
    if (_259)
    {
        _359 = mix((pow(max(_249, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _249 * 12.9200000762939453125, step(_249, vec3(0.003130800090730190277099609375)));
    }
    else
    {
        _359 = _249;
    }
    vec3 _304 = mix(push.fogNormalScale.xyz, clamp((input_colour0.xyz * _359) + input_colour1, vec3(0.0), vec3(1.0)), vec3(input_fog));
    vec3 shown;
    if (_306)
    {
        vec3 _308 = max(_304, vec3(0.0));
        shown = mix(pow(max((_308 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _308 * vec3(0.077399380505084991455078125), step(_308, vec3(0.040449999272823333740234375))) * push.diffuseScale.w;
    }
    else
    {
        shown = _304;
    }
    entryPointParam_fragmentMain = vec4(shown, input_colour0.w);
}

