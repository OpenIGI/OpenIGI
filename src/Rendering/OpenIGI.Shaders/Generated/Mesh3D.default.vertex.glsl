#version 430
#ifdef GL_ARB_shader_draw_parameters
#extension GL_ARB_shader_draw_parameters : enable
#endif

struct MeshPush
{
    mat4 modelViewProjection;
    vec4 markerExposure;
    vec4 normalKey0;
    vec4 normalKey1;
    vec4 normalKey2;
};

#ifndef SPIRV_CROSS_CONSTANT_ID_1
#define SPIRV_CROSS_CONSTANT_ID_1 0
#endif
const int cReflective = SPIRV_CROSS_CONSTANT_ID_1;
const bool _295 = (cReflective != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _561 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_6
#define SPIRV_CROSS_CONSTANT_ID_6 0.0
#endif
const float cSunAmbientRed = SPIRV_CROSS_CONSTANT_ID_6;
#ifndef SPIRV_CROSS_CONSTANT_ID_7
#define SPIRV_CROSS_CONSTANT_ID_7 0.0
#endif
const float cSunAmbientGreen = SPIRV_CROSS_CONSTANT_ID_7;
#ifndef SPIRV_CROSS_CONSTANT_ID_8
#define SPIRV_CROSS_CONSTANT_ID_8 0.0
#endif
const float cSunAmbientBlue = SPIRV_CROSS_CONSTANT_ID_8;
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
#ifndef SPIRV_CROSS_CONSTANT_ID_0
#define SPIRV_CROSS_CONSTANT_ID_0 0.501960813999176025390625
#endif
const float cAlphaReference = SPIRV_CROSS_CONSTANT_ID_0;
#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _952 = (cSamplerDecodes != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_12
#define SPIRV_CROSS_CONSTANT_ID_12 0
#endif
const int cLinearScene = SPIRV_CROSS_CONSTANT_ID_12;
const bool _1007 = (cLinearScene == 0);
const bool _1095 = (cLinearScene != 0);

layout(binding = 0, std430) restrict readonly buffer StructuredBuffer
{
    float _member0[];
} pushBlocks;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseInstance gl_BaseInstanceARB
#else
uniform int SPIRV_Cross_BaseInstance;
#endif
layout(location = 0) in vec3 input_position;
layout(location = 5) in vec3 input_previousPosition;
layout(location = 1) in vec3 input_normal;
layout(location = 6) in vec3 input_previousNormal;
layout(location = 2) in vec2 input_diffuseUv;
layout(location = 3) in vec2 input_lightmapUv;
layout(location = 4) in vec4 input_colorBgra;
layout(location = 0) out vec2 entryPointParam_vertexMain_diffuseUv;
layout(location = 1) out vec2 entryPointParam_vertexMain_lightmapUv;
layout(location = 2) out vec4 entryPointParam_vertexMain_color;
layout(location = 3) out vec3 entryPointParam_vertexMain_normal;
layout(location = 4) out float entryPointParam_vertexMain_fade;
layout(location = 5) out float entryPointParam_vertexMain_fog;
layout(location = 6) out vec3 entryPointParam_vertexMain_reflection;
layout(location = 7) flat out uint entryPointParam_vertexMain_pushIndex;
MeshPush push;

void main()
{
    uint _80 = uint((gl_InstanceID + SPIRV_Cross_BaseInstance)) * 128u;
    MeshPush _257 = MeshPush(mat4(vec4(pushBlocks._member0[_80 / 4u], pushBlocks._member0[(_80 + 16u) / 4u], pushBlocks._member0[(_80 + 32u) / 4u], pushBlocks._member0[(_80 + 48u) / 4u]), vec4(pushBlocks._member0[(_80 + 4u) / 4u], pushBlocks._member0[(_80 + 20u) / 4u], pushBlocks._member0[(_80 + 36u) / 4u], pushBlocks._member0[(_80 + 52u) / 4u]), vec4(pushBlocks._member0[(_80 + 8u) / 4u], pushBlocks._member0[(_80 + 24u) / 4u], pushBlocks._member0[(_80 + 40u) / 4u], pushBlocks._member0[(_80 + 56u) / 4u]), vec4(pushBlocks._member0[(_80 + 12u) / 4u], pushBlocks._member0[(_80 + 28u) / 4u], pushBlocks._member0[(_80 + 44u) / 4u], pushBlocks._member0[(_80 + 60u) / 4u])), vec4(pushBlocks._member0[(_80 + 64u) / 4u], pushBlocks._member0[(_80 + 68u) / 4u], pushBlocks._member0[(_80 + 72u) / 4u], pushBlocks._member0[(_80 + 76u) / 4u]), vec4(pushBlocks._member0[(_80 + 80u) / 4u], pushBlocks._member0[(_80 + 84u) / 4u], pushBlocks._member0[(_80 + 88u) / 4u], pushBlocks._member0[(_80 + 92u) / 4u]), vec4(pushBlocks._member0[(_80 + 96u) / 4u], pushBlocks._member0[(_80 + 100u) / 4u], pushBlocks._member0[(_80 + 104u) / 4u], pushBlocks._member0[(_80 + 108u) / 4u]), vec4(pushBlocks._member0[(_80 + 112u) / 4u], pushBlocks._member0[(_80 + 116u) / 4u], pushBlocks._member0[(_80 + 120u) / 4u], pushBlocks._member0[(_80 + 124u) / 4u]));
    push = _257;
    uint _1371 = uint((gl_InstanceID + SPIRV_Cross_BaseInstance));
    vec3 localPosition_1 = input_position + ((input_previousPosition - input_position) * push.markerExposure.z);
    vec3 localNormal = input_normal + ((input_previousNormal - input_normal) * push.markerExposure.z);
    vec3 _1370 = vec3(0.0);
    if (_295)
    {
        vec3 _298 = normalize(-localPosition_1);
        vec3 _300 = normalize(localNormal);
        _1370 = (_300 * (2.0 * dot(_298, _300))) - _298;
    }
    float _1368 = 1.0;
    float terrain;
    if (abs(push.markerExposure.x) > 0.5)
    {
        terrain = push.normalKey0.w;
    }
    else
    {
        terrain = 0.0;
    }
    bool _330 = terrain > 0.5;
    float fogDensity;
    vec3 localPosition;
    vec2 diffuseUv;
    if (_330)
    {
        vec3 target = (input_position + push.normalKey1.xyz) + input_normal;
        float _357 = clamp(((max(abs(target.x), max(abs(target.y), abs(target.z))) * push.normalKey0.x) - push.normalKey0.y) * push.normalKey0.z, 0.0, 1.0);
        float _1232;
        {
            bool _1233;
            if (input_colorBgra.x > 0.999000012874603271484375)
            {
                _1233 = input_colorBgra.y > 0.999000012874603271484375;
            }
            else
            {
                _1233 = false;
            }
            if (_1233)
            {
                _1233 = input_colorBgra.z > 0.999000012874603271484375;
            }
            else
            {
                _1233 = false;
            }
            if (_1233)
            {
                float _1254 = input_colorBgra.w * 3.0;
                float _1234;
                if (_1254 < 0.5)
                {
                    _1234 = 0.0;
                }
                else
                {
                    if (_1254 < 1.5)
                    {
                        _1234 = _357;
                    }
                    else
                    {
                        if (_1254 < 2.5)
                        {
                            _1234 = 1.0 - _357;
                        }
                        else
                        {
                            _1234 = 1.0;
                        }
                    }
                }
                _1232 = _1234;
            }
            else
            {
                _1232 = input_colorBgra.z + ((input_colorBgra.y - input_colorBgra.x) * _357);
            }
        }
        if (terrain > 2.5)
        {
            fogDensity = 1.0;
        }
        else
        {
            if (terrain > 1.5)
            {
                fogDensity = 1.0 - _357;
            }
            else
            {
                fogDensity = _357;
            }
            fogDensity = _1232 * fogDensity;
        }
        _1368 = fogDensity;
        localPosition = input_position + (input_normal * _357);
        diffuseUv = input_diffuseUv + (input_lightmapUv * _357);
    }
    else
    {
        localPosition = localPosition_1;
        diffuseUv = input_diffuseUv;
    }
    vec4 clip = vec4(localPosition, 1.0) * push.modelViewProjection;
    bool _24;
    if (abs(push.markerExposure.x) > 0.5)
    {
        _24 = push.markerExposure.x < 1.5;
    }
    else
    {
        _24 = false;
    }
    if (_24)
    {
        fogDensity = push.normalKey2.w;
    }
    else
    {
        fogDensity = 0.0;
    }
    bool _502 = fogDensity > 0.0;
    float facing;
    if (_502)
    {
        facing = clamp(exp((-fogDensity) * max(clip.w, 0.0)), 0.0, 1.0);
    }
    else
    {
        facing = 1.0;
    }
    float _1369 = facing;
    if (_502)
    {
        _24 = terrain < 0.5;
    }
    else
    {
        _24 = false;
    }
    if (_24)
    {
        _24 = push.normalKey1.w > 0.0;
    }
    else
    {
        _24 = false;
    }
    if (_24)
    {
        _1369 = clamp(push.normalKey1.w - (fogDensity * clip.w), 0.0, 1.0);
    }
    float _1277;
    if (_561)
    {
        _1277 = -clip.y;
    }
    else
    {
        _1277 = clip.y;
    }
    vec4 _1392 = clip;
    _1392.y = _1277;
    _1392.z = (_1392.w - _1392.z) * 0.5;
    clip = _1392;
    vec4 _1363 = _1392;
    vec2 _1364 = diffuseUv;
    vec2 _1365 = input_lightmapUv;
    vec4 _26;
    if (_330)
    {
        _26 = vec4(1.0);
    }
    else
    {
        _26 = input_colorBgra.zyxw;
    }
    vec4 _1366 = _26;
    if (abs(push.markerExposure.x) > 0.5)
    {
        _24 = terrain < 0.5;
    }
    else
    {
        _24 = false;
    }
    if (_24)
    {
        _24 = push.normalKey0.w >= 0.0;
    }
    else
    {
        _24 = false;
    }
    if (_24)
    {
        _24 = dot(push.normalKey0.xyz, push.normalKey0.xyz) > 9.9999999392252902907785028219223e-09;
    }
    else
    {
        _24 = false;
    }
    if (_24)
    {
        float _633 = length(localNormal);
        if (_633 > 0.0)
        {
            facing = dot(localNormal / vec3(_633), push.normalKey0.xyz);
        }
        else
        {
            facing = 0.0;
        }
        vec4 _655 = _1366;
        vec3 _660 = _655.xyz * clamp(vec3(cSunAmbientRed, cSunAmbientGreen, cSunAmbientBlue) + (vec3(cSunRed, cSunGreen, cSunBlue) * max(facing, 0.0)), vec3(0.0), vec3(1.0));
        vec4 _1397 = _655;
        _1397.x = _660.x;
        _1397.y = _660.y;
        _1397.z = _660.z;
        _1366 = _1397;
    }
    gl_Position = _1363;
    entryPointParam_vertexMain_diffuseUv = _1364;
    entryPointParam_vertexMain_lightmapUv = _1365;
    entryPointParam_vertexMain_color = _1366;
    entryPointParam_vertexMain_normal = vec3(dot(push.normalKey0.xyz, localNormal), dot(push.normalKey1.xyz, localNormal), dot(push.normalKey2.xyz, localNormal));
    entryPointParam_vertexMain_fade = _1368;
    entryPointParam_vertexMain_fog = _1369;
    entryPointParam_vertexMain_reflection = _1370;
    entryPointParam_vertexMain_pushIndex = _1371;
}

