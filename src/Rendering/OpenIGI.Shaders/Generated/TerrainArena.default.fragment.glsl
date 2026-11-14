#version 430

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

layout(binding = 0) uniform sampler2D _1513;
layout(binding = 1) uniform sampler2D _1515;
layout(binding = 2) uniform samplerCube _1517;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseInstance gl_BaseInstanceARB
#else
uniform int SPIRV_Cross_BaseInstance;
#endif
layout(location = 7) flat in uint input_pushIndex;
layout(location = 0) in vec2 input_diffuseUv;
layout(location = 1) in vec2 input_lightmapUv;
layout(location = 2) in vec4 input_color;
layout(location = 4) in float input_fade;
layout(location = 6) in vec3 input_reflection;
layout(location = 5) in float input_fog;
layout(location = 0) out vec4 entryPointParam_fragmentMain;
layout(location = 3) in vec3 input_normal;
MeshPush push;

void main()
{
    {
        uint _852 = input_pushIndex * 128u;
        MeshPush _989 = MeshPush(mat4(vec4(pushBlocks._member0[_852 / 4u], pushBlocks._member0[(_852 + 16u) / 4u], pushBlocks._member0[(_852 + 32u) / 4u], pushBlocks._member0[(_852 + 48u) / 4u]), vec4(pushBlocks._member0[(_852 + 4u) / 4u], pushBlocks._member0[(_852 + 20u) / 4u], pushBlocks._member0[(_852 + 36u) / 4u], pushBlocks._member0[(_852 + 52u) / 4u]), vec4(pushBlocks._member0[(_852 + 8u) / 4u], pushBlocks._member0[(_852 + 24u) / 4u], pushBlocks._member0[(_852 + 40u) / 4u], pushBlocks._member0[(_852 + 56u) / 4u]), vec4(pushBlocks._member0[(_852 + 12u) / 4u], pushBlocks._member0[(_852 + 28u) / 4u], pushBlocks._member0[(_852 + 44u) / 4u], pushBlocks._member0[(_852 + 60u) / 4u])), vec4(pushBlocks._member0[(_852 + 64u) / 4u], pushBlocks._member0[(_852 + 68u) / 4u], pushBlocks._member0[(_852 + 72u) / 4u], pushBlocks._member0[(_852 + 76u) / 4u]), vec4(pushBlocks._member0[(_852 + 80u) / 4u], pushBlocks._member0[(_852 + 84u) / 4u], pushBlocks._member0[(_852 + 88u) / 4u], pushBlocks._member0[(_852 + 92u) / 4u]), vec4(pushBlocks._member0[(_852 + 96u) / 4u], pushBlocks._member0[(_852 + 100u) / 4u], pushBlocks._member0[(_852 + 104u) / 4u], pushBlocks._member0[(_852 + 108u) / 4u]), vec4(pushBlocks._member0[(_852 + 112u) / 4u], pushBlocks._member0[(_852 + 116u) / 4u], pushBlocks._member0[(_852 + 120u) / 4u], pushBlocks._member0[(_852 + 124u) / 4u]));
        push = _989;
        vec4 sampled = texture(_1513, input_diffuseUv);
        bool translucent = push.markerExposure.x < 0.0;
        bool _809;
        if (!translucent)
        {
            _809 = sampled.w < cAlphaReference;
        }
        else
        {
            _809 = false;
        }
        if (_809)
        {
            discard;
        }
        vec4 sampled_1 = texture(_1515, input_lightmapUv);
        vec3 _1030 = sampled_1.xyz;
        vec3 _1376;
        if (_1041)
        {
            _1376 = mix((pow(max(_1030, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _1030 * 12.9200000762939453125, step(_1030, vec3(0.003130800090730190277099609375)));
        }
        else
        {
            _1376 = _1030;
        }
        if (abs(push.normalKey0.w) > 0.5)
        {
            _809 = true;
        }
        else
        {
            _809 = push.markerExposure.x < 0.5;
        }
        vec3 modelTint;
        if (_809)
        {
            modelTint = vec3(1.0);
        }
        else
        {
            modelTint = push.normalKey1.xyz;
        }
        bool _1093 = abs(push.markerExposure.x) > 0.5;
        if (_1093)
        {
            _809 = _1096;
        }
        else
        {
            _809 = false;
        }
        float lightmapGain;
        if (_809)
        {
            lightmapGain = push.markerExposure.y;
        }
        else
        {
            lightmapGain = 1.0;
        }
        vec3 _1109 = sampled.xyz;
        vec3 _1394;
        if (_1041)
        {
            _1394 = mix((pow(max(_1109, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _1109 * 12.9200000762939453125, step(_1109, vec3(0.003130800090730190277099609375)));
        }
        else
        {
            _1394 = _1109;
        }
        vec3 base_1 = ((((_1394 * _1376) * lightmapGain) * input_color.xyz) * input_fade) * modelTint;
        vec3 base;
        if (_412)
        {
            vec4 sampled_2 = texture(_1517, normalize(input_reflection).xzy);
            vec3 _1139 = sampled_2.xyz;
            vec3 _1412;
            if (_1041)
            {
                _1412 = mix((pow(max(_1139, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _1139 * 12.9200000762939453125, step(_1139, vec3(0.003130800090730190277099609375)));
            }
            else
            {
                _1412 = _1139;
            }
            base = base_1 + _1412;
        }
        else
        {
            base = base_1;
        }
        float alpha;
        if (translucent)
        {
            alpha = sampled.w * input_color.w;
        }
        else
        {
            if (push.markerExposure.x > 1.5)
            {
                lightmapGain = 0.0;
            }
            else
            {
                lightmapGain = push.markerExposure.x;
            }
            alpha = lightmapGain;
        }
        if (_1093)
        {
            float fogShare;
            if (push.normalKey0.w < (-0.5))
            {
                fogShare = input_color.x;
            }
            else
            {
                fogShare = 1.0;
            }
            vec3 _1183 = mix(push.normalKey2.xyz * fogShare, base, vec3(input_fog));
            if (_1184)
            {
                vec3 _1186 = max(_1183, vec3(0.0));
                entryPointParam_fragmentMain = vec4(mix(pow(max((_1186 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _1186 * vec3(0.077399380505084991455078125), step(_1186, vec3(0.040449999272823333740234375))) * push.markerExposure.y, alpha);
            }
            else
            {
                entryPointParam_fragmentMain = vec4(_1183, alpha);
            }
        }
        else
        {
            if (_1184)
            {
                vec3 _1222 = max(base, vec3(0.0));
                base = mix(pow(max((_1222 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _1222 * vec3(0.077399380505084991455078125), step(_1222, vec3(0.040449999272823333740234375)));
            }
            vec3 _1227 = normalize(vec3(-0.3499999940395355224609375, 0.449999988079071044921875, -0.819999992847442626953125));
            vec3 _1239 = normalize(input_normal);
            vec3 keyColour = vec3(push.normalKey0.w, push.normalKey1.w, push.normalKey2.w);
            float _1266 = max(dot(_1239, _1227), 0.0);
            vec3 _1295 = mix(vec3(0.039999999105930328369140625), base, vec3(0.3499999940395355224609375)) * keyColour;
            entryPointParam_fragmentMain = vec4(((((base * 0.64999997615814208984375) * ((vec3(push.markerExposure.w, push.markerExposure.w, push.markerExposure.w) + (keyColour * _1266)) + ((keyColour * 0.25) * max(dot(_1239, normalize(vec3(0.550000011920928955078125, -0.20000000298023223876953125, -0.550000011920928955078125))), 0.0)))) + ((_1295 * (pow(max(dot(_1239, normalize(_1227 + vec3(0.0, 0.0, -1.0))), 0.0), 132.5139312744140625) * 5.620557308197021484375)) * _1266)) + (((_1295 * pow(1.0 - max(dot(_1239, vec3(0.0, 0.0, -1.0)), 0.0), 4.0)) * 0.3499999940395355224609375) * 0.60000002384185791015625)) * push.markerExposure.y, alpha);
        }
    }
}

