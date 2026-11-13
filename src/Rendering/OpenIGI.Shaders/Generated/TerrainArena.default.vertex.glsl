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
const bool _412 = (cReflective != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _667 = (cClipYDown != 0);
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
const bool _1041 = (cSamplerDecodes != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_12
#define SPIRV_CROSS_CONSTANT_ID_12 0
#endif
const int cLinearScene = SPIRV_CROSS_CONSTANT_ID_12;
const bool _1096 = (cLinearScene == 0);
const bool _1184 = (cLinearScene != 0);

layout(binding = 2, std430) restrict readonly buffer StructuredBuffer
{
    float _member0[];
} pushBlocks;

layout(binding = 0, std430) restrict readonly buffer uTerrainChunkRecord
{
    uint _member0[];
} uTerrainChunkRecord_1;

layout(binding = 1, std430) restrict readonly buffer uTerrainChunks
{
    vec4 _member0[];
} uTerrainChunks_1;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseInstance gl_BaseInstanceARB
#else
uniform int SPIRV_Cross_BaseInstance;
#endif
layout(location = 7) in uint input_chunk;
layout(location = 0) out vec2 entryPointParam_vertexMain_diffuseUv;
layout(location = 1) out vec2 entryPointParam_vertexMain_lightmapUv;
layout(location = 2) out vec4 entryPointParam_vertexMain_color;
layout(location = 3) out vec3 entryPointParam_vertexMain_normal;
layout(location = 4) out float entryPointParam_vertexMain_fade;
layout(location = 5) out float entryPointParam_vertexMain_fog;
layout(location = 6) out vec3 entryPointParam_vertexMain_reflection;
layout(location = 7) flat out uint entryPointParam_vertexMain_pushIndex;
layout(location = 0) in vec3 input_position;
layout(location = 5) in vec3 input_previousPosition;
layout(location = 1) in vec3 input_normal;
layout(location = 6) in vec3 input_previousNormal;
layout(location = 2) in vec2 input_diffuseUv;
layout(location = 3) in vec2 input_lightmapUv;
layout(location = 4) in vec4 input_colorBgra;
MeshPush push;

void main()
{
    {
        uint _82 = uint((gl_InstanceID + SPIRV_Cross_BaseInstance)) * 128u;
        MeshPush _259 = MeshPush(mat4(vec4(pushBlocks._member0[_82 / 4u], pushBlocks._member0[(_82 + 16u) / 4u], pushBlocks._member0[(_82 + 32u) / 4u], pushBlocks._member0[(_82 + 48u) / 4u]), vec4(pushBlocks._member0[(_82 + 4u) / 4u], pushBlocks._member0[(_82 + 20u) / 4u], pushBlocks._member0[(_82 + 36u) / 4u], pushBlocks._member0[(_82 + 52u) / 4u]), vec4(pushBlocks._member0[(_82 + 8u) / 4u], pushBlocks._member0[(_82 + 24u) / 4u], pushBlocks._member0[(_82 + 40u) / 4u], pushBlocks._member0[(_82 + 56u) / 4u]), vec4(pushBlocks._member0[(_82 + 12u) / 4u], pushBlocks._member0[(_82 + 28u) / 4u], pushBlocks._member0[(_82 + 44u) / 4u], pushBlocks._member0[(_82 + 60u) / 4u])), vec4(pushBlocks._member0[(_82 + 64u) / 4u], pushBlocks._member0[(_82 + 68u) / 4u], pushBlocks._member0[(_82 + 72u) / 4u], pushBlocks._member0[(_82 + 76u) / 4u]), vec4(pushBlocks._member0[(_82 + 80u) / 4u], pushBlocks._member0[(_82 + 84u) / 4u], pushBlocks._member0[(_82 + 88u) / 4u], pushBlocks._member0[(_82 + 92u) / 4u]), vec4(pushBlocks._member0[(_82 + 96u) / 4u], pushBlocks._member0[(_82 + 100u) / 4u], pushBlocks._member0[(_82 + 104u) / 4u], pushBlocks._member0[(_82 + 108u) / 4u]), vec4(pushBlocks._member0[(_82 + 112u) / 4u], pushBlocks._member0[(_82 + 116u) / 4u], pushBlocks._member0[(_82 + 120u) / 4u], pushBlocks._member0[(_82 + 124u) / 4u]));
        push = _259;
        uint _1462 = uint((gl_InstanceID + SPIRV_Cross_BaseInstance));
        vec4 _1454;
        vec2 _1455;
        vec2 _1456;
        vec4 _1457;
        float _1459;
        float _1460;
        vec3 _1461;
        if (uTerrainChunkRecord_1._member0[input_chunk] == 4294967295u)
        {
            _1455 = vec2(0.0);
            _1456 = vec2(0.0);
            _1457 = vec4(0.0);
            _1459 = 0.0;
            _1460 = 0.0;
            _1461 = vec3(0.0);
            _1454 = vec4(2.0, 2.0, 2.0, 1.0);
            uint _295 = uint((gl_InstanceID + SPIRV_Cross_BaseInstance));
            _1462 = _295;
            gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
            entryPointParam_vertexMain_diffuseUv = vec2(0.0);
            entryPointParam_vertexMain_lightmapUv = vec2(0.0);
            entryPointParam_vertexMain_color = vec4(0.0);
            entryPointParam_vertexMain_normal = vec3(0.0);
            entryPointParam_vertexMain_fade = 0.0;
            entryPointParam_vertexMain_fog = 0.0;
            entryPointParam_vertexMain_reflection = vec3(0.0);
            entryPointParam_vertexMain_pushIndex = _295;
        }
        else
        {
            uint at = uTerrainChunkRecord_1._member0[input_chunk] * 6u;
            push.modelViewProjection = transpose(mat4(uTerrainChunks_1._member0[at], uTerrainChunks_1._member0[at + 1u], uTerrainChunks_1._member0[at + 2u], uTerrainChunks_1._member0[at + 3u]));
            uint _360 = at + 4u;
            push.normalKey0.x = uTerrainChunks_1._member0[_360].x;
            push.normalKey0.y = uTerrainChunks_1._member0[_360].y;
            push.normalKey0.z = uTerrainChunks_1._member0[_360].z;
            uint _375 = at + 5u;
            push.normalKey1.x = uTerrainChunks_1._member0[_375].x;
            push.normalKey1.y = uTerrainChunks_1._member0[_375].y;
            push.normalKey1.z = uTerrainChunks_1._member0[_375].z;
            vec3 localPosition_1 = input_position + ((input_previousPosition - input_position) * push.markerExposure.z);
            vec3 localNormal = input_normal + ((input_previousNormal - input_normal) * push.markerExposure.z);
            _1461 = vec3(0.0);
            if (_412)
            {
                vec3 _415 = normalize(-localPosition_1);
                vec3 _417 = normalize(localNormal);
                _1461 = (_417 * (2.0 * dot(_415, _417))) - _415;
            }
            _1459 = 1.0;
            float terrain;
            if (abs(push.markerExposure.x) > 0.5)
            {
                terrain = push.normalKey0.w;
            }
            else
            {
                terrain = 0.0;
            }
            bool _443 = terrain > 0.5;
            float fogDensity;
            vec3 localPosition;
            vec2 diffuseUv;
            if (_443)
            {
                vec3 target = (input_position + push.normalKey1.xyz) + input_normal;
                float _467 = clamp(((max(abs(target.x), max(abs(target.y), abs(target.z))) * push.normalKey0.x) - push.normalKey0.y) * push.normalKey0.z, 0.0, 1.0);
                float _1323;
                {
                    bool _1324;
                    if (input_colorBgra.x > 0.999000012874603271484375)
                    {
                        _1324 = input_colorBgra.y > 0.999000012874603271484375;
                    }
                    else
                    {
                        _1324 = false;
                    }
                    if (_1324)
                    {
                        _1324 = input_colorBgra.z > 0.999000012874603271484375;
                    }
                    else
                    {
                        _1324 = false;
                    }
                    if (_1324)
                    {
                        float _1345 = input_colorBgra.w * 3.0;
                        float _1325;
                        if (_1345 < 0.5)
                        {
                            _1325 = 0.0;
                        }
                        else
                        {
                            if (_1345 < 1.5)
                            {
                                _1325 = _467;
                            }
                            else
                            {
                                if (_1345 < 2.5)
                                {
                                    _1325 = 1.0 - _467;
                                }
                                else
                                {
                                    _1325 = 1.0;
                                }
                            }
                        }
                        _1323 = _1325;
                    }
                    else
                    {
                        _1323 = input_colorBgra.z + ((input_colorBgra.y - input_colorBgra.x) * _467);
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
                        fogDensity = 1.0 - _467;
                    }
                    else
                    {
                        fogDensity = _467;
                    }
                    fogDensity = _1323 * fogDensity;
                }
                _1459 = fogDensity;
                localPosition = input_position + (input_normal * _467);
                diffuseUv = input_diffuseUv + (input_lightmapUv * _467);
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
            bool _610 = fogDensity > 0.0;
            float facing;
            if (_610)
            {
                facing = clamp(exp((-fogDensity) * max(clip.w, 0.0)), 0.0, 1.0);
            }
            else
            {
                facing = 1.0;
            }
            _1460 = facing;
            if (_610)
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
                _1460 = clamp(push.normalKey1.w - (fogDensity * clip.w), 0.0, 1.0);
            }
            float _1368;
            if (_667)
            {
                _1368 = -clip.y;
            }
            else
            {
                _1368 = clip.y;
            }
            vec4 _1502 = clip;
            _1502.y = _1368;
            _1502.z = (_1502.w - _1502.z) * 0.5;
            clip = _1502;
            _1454 = _1502;
            _1455 = diffuseUv;
            _1456 = input_lightmapUv;
            vec4 _26;
            if (_443)
            {
                _26 = vec4(1.0);
            }
            else
            {
                _26 = input_colorBgra.zyxw;
            }
            _1457 = _26;
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
                float _738 = length(localNormal);
                if (_738 > 0.0)
                {
                    facing = dot(localNormal / vec3(_738), push.normalKey0.xyz);
                }
                else
                {
                    facing = 0.0;
                }
                vec4 _760 = _1457;
                vec3 _765 = _760.xyz * clamp(vec3(cSunAmbientRed, cSunAmbientGreen, cSunAmbientBlue) + (vec3(cSunRed, cSunGreen, cSunBlue) * max(facing, 0.0)), vec3(0.0), vec3(1.0));
                vec4 _1507 = _760;
                _1507.x = _765.x;
                _1507.y = _765.y;
                _1507.z = _765.z;
                _1457 = _1507;
            }
            gl_Position = _1454;
            entryPointParam_vertexMain_diffuseUv = _1455;
            entryPointParam_vertexMain_lightmapUv = _1456;
            entryPointParam_vertexMain_color = _1457;
            entryPointParam_vertexMain_normal = vec3(dot(push.normalKey0.xyz, localNormal), dot(push.normalKey1.xyz, localNormal), dot(push.normalKey2.xyz, localNormal));
            entryPointParam_vertexMain_fade = _1459;
            entryPointParam_vertexMain_fog = _1460;
            entryPointParam_vertexMain_reflection = _1461;
            entryPointParam_vertexMain_pushIndex = _1462;
        }
    }
}

