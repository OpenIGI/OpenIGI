#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _238 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_0
#define SPIRV_CROSS_CONSTANT_ID_0 0.501960813999176025390625
#endif
const float cAlphaReference = SPIRV_CROSS_CONSTANT_ID_0;
#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _541 = (cSamplerDecodes != 0);

layout(binding = 1, std140) uniform GBufferPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;
    vec4 material;
} push;

layout(location = 6) in vec3 input_previousPosition;
layout(location = 0) in vec3 input_position;
layout(location = 1) in vec3 input_normal;
layout(location = 4) in vec4 input_colorBgra;
layout(location = 7) in vec3 input_previousNormal;
layout(location = 5) in vec4 input_surfaceNormal;
layout(location = 2) in vec2 input_diffuseUv;
layout(location = 3) in vec2 input_lightmapUv;
layout(location = 0) out vec2 entryPointParam_vertexMain_diffuseUv;
layout(location = 1) out vec2 entryPointParam_vertexMain_lightmapUv;
layout(location = 2) out vec4 entryPointParam_vertexMain_color;
layout(location = 3) out vec3 entryPointParam_vertexMain_worldNormal;
layout(location = 4) out float entryPointParam_vertexMain_fade;
layout(location = 5) out vec3 entryPointParam_vertexMain_surfacePosition;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    bool ramped;
    if (push.normal0.w > 0.5)
    {
        ramped = push.normal0.w < 2.5;
    }
    else
    {
        ramped = false;
    }
    vec3 localPosition_1 = input_position + ((input_previousPosition - input_position) * push.normal1.w);
    float _1210 = 1.0;
    float morph;
    vec3 localPosition;
    if (ramped)
    {
        vec3 target = (localPosition_1 + push.normal1.xyz) + input_normal;
        float _113 = clamp(((max(abs(target.x), max(abs(target.y), abs(target.z))) * push.normal0.x) - push.normal0.y) * push.normal0.z, 0.0, 1.0);
        float _1004;
        {
            bool _1005;
            if (input_colorBgra.x > 0.999000012874603271484375)
            {
                _1005 = input_colorBgra.y > 0.999000012874603271484375;
            }
            else
            {
                _1005 = false;
            }
            if (_1005)
            {
                _1005 = input_colorBgra.z > 0.999000012874603271484375;
            }
            else
            {
                _1005 = false;
            }
            if (_1005)
            {
                float _1026 = input_colorBgra.w * 3.0;
                float _1006;
                if (_1026 < 0.5)
                {
                    _1006 = 0.0;
                }
                else
                {
                    if (_1026 < 1.5)
                    {
                        _1006 = _113;
                    }
                    else
                    {
                        if (_1026 < 2.5)
                        {
                            _1006 = 1.0 - _113;
                        }
                        else
                        {
                            _1006 = 1.0;
                        }
                    }
                }
                _1004 = _1006;
            }
            else
            {
                _1004 = input_colorBgra.z + ((input_colorBgra.y - input_colorBgra.x) * _113);
            }
        }
        if (push.normal0.w > 2.5)
        {
            morph = 1.0;
        }
        else
        {
            if (push.normal0.w > 1.5)
            {
                morph = 1.0 - _113;
            }
            else
            {
                morph = _113;
            }
            morph = _1004 * morph;
        }
        _1210 = morph;
        morph = _113;
        localPosition = localPosition_1 + (input_normal * _113);
    }
    else
    {
        morph = 0.0;
        localPosition = localPosition_1;
    }
    vec4 _224 = vec4(localPosition, 1.0) * spvWorkaroundRowMajor(push.modelViewProjection);
    vec4 clip = _224;
    float _227 = _224.y;
    float _1049;
    if (_238)
    {
        _1049 = -_227;
    }
    else
    {
        _1049 = _227;
    }
    vec4 _1245 = clip;
    _1245.y = _1049;
    _1245.z = (_1245.w - _1245.z) * 0.5;
    clip = _1245;
    vec4 _1205 = _1245;
    vec3 localNormal;
    if (input_surfaceNormal.w > 0.5)
    {
        localNormal = (input_surfaceNormal.xyz * 2.0) - vec3(1.0);
    }
    else
    {
        localNormal = input_normal + ((input_previousNormal - input_normal) * push.normal1.w);
    }
    if (!ramped)
    {
        localNormal = ((push.normal0.xyz * localNormal.x) + (push.normal1.xyz * localNormal.y)) + (push.normal2.xyz * localNormal.z);
    }
    vec3 _1209 = localNormal;
    if (ramped)
    {
        localPosition += push.normal1.xyz;
    }
    else
    {
        localPosition = ((push.normal0.xyz * localPosition.x) + (push.normal1.xyz * localPosition.y)) + (push.normal2.xyz * localPosition.z);
    }
    vec3 _1211 = localPosition;
    vec2 _23;
    if (ramped)
    {
        _23 = input_diffuseUv + (input_lightmapUv * morph);
    }
    else
    {
        _23 = input_diffuseUv;
    }
    vec2 _1206 = _23;
    vec2 _1207 = input_lightmapUv;
    vec4 _24;
    if (ramped)
    {
        _24 = vec4(1.0);
    }
    else
    {
        _24 = input_colorBgra.zyxw;
    }
    gl_Position = _1205;
    entryPointParam_vertexMain_diffuseUv = _1206;
    entryPointParam_vertexMain_lightmapUv = _1207;
    entryPointParam_vertexMain_color = _24;
    entryPointParam_vertexMain_worldNormal = _1209;
    entryPointParam_vertexMain_fade = _1210;
    entryPointParam_vertexMain_surfacePosition = _1211;
}

