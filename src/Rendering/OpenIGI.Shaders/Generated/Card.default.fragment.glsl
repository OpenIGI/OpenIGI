#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _45 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _139 = (cSamplerDecodes != 0);

layout(binding = 0, std140) uniform CardPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;
    vec4 material;
} push;

layout(binding = 0) uniform sampler2D _292;

layout(location = 0) in vec2 input_diffuseUv;
layout(location = 1) in vec4 input_color;
layout(location = 2) in vec3 input_modelNormal;
layout(location = 0) out vec4 entryPointParam_fragmentMain_albedo;
layout(location = 1) out vec4 entryPointParam_fragmentMain_normal;
layout(location = 2) out vec4 entryPointParam_fragmentMain_material;
layout(location = 3) out vec4 entryPointParam_fragmentMain_depth;

void main()
{
    vec4 sampled = texture(_292, input_diffuseUv);
    if (sampled.w < 0.5)
    {
        discard;
    }
    vec3 _128 = sampled.xyz;
    vec3 _229;
    if (_139)
    {
        _229 = _128;
    }
    else
    {
        _229 = mix(pow(max((_128 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _128 * vec3(0.077399380505084991455078125), step(_128, vec3(0.040449999272823333740234375)));
    }
    entryPointParam_fragmentMain_albedo = vec4(_229 * mix(pow(max((input_color.xyz + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), input_color.xyz * vec3(0.077399380505084991455078125), step(input_color.xyz, vec3(0.040449999272823333740234375))), 1.0);
    entryPointParam_fragmentMain_normal = vec4((normalize(input_modelNormal) * 0.5) + vec3(0.5), 1.0);
    entryPointParam_fragmentMain_material = vec4(push.material.x, push.material.y, push.material.z, 1.0);
    entryPointParam_fragmentMain_depth = vec4(gl_FragCoord.z, 0.0, 0.0, 1.0);
}

