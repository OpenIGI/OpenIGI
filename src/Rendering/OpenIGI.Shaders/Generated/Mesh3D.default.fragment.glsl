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

layout(binding = 0) uniform sampler2D _1403;
layout(binding = 1) uniform sampler2D _1405;
layout(binding = 2) uniform samplerCube _1407;

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
        uint _763 = input_pushIndex * 128u;
        MeshPush _900 = MeshPush(mat4(vec4(pushBlocks._member0[_763 / 4u], pushBlocks._member0[(_763 + 16u) / 4u], pushBlocks._member0[(_763 + 32u) / 4u], pushBlocks._member0[(_763 + 48u) / 4u]), vec4(pushBlocks._member0[(_763 + 4u) / 4u], pushBlocks._member0[(_763 + 20u) / 4u], pushBlocks._member0[(_763 + 36u) / 4u], pushBlocks._member0[(_763 + 52u) / 4u]), vec4(pushBlocks._member0[(_763 + 8u) / 4u], pushBlocks._member0[(_763 + 24u) / 4u], pushBlocks._member0[(_763 + 40u) / 4u], pushBlocks._member0[(_763 + 56u) / 4u]), vec4(pushBlocks._member0[(_763 + 12u) / 4u], pushBlocks._member0[(_763 + 28u) / 4u], pushBlocks._member0[(_763 + 44u) / 4u], pushBlocks._member0[(_763 + 60u) / 4u])), vec4(pushBlocks._member0[(_763 + 64u) / 4u], pushBlocks._member0[(_763 + 68u) / 4u], pushBlocks._member0[(_763 + 72u) / 4u], pushBlocks._member0[(_763 + 76u) / 4u]), vec4(pushBlocks._member0[(_763 + 80u) / 4u], pushBlocks._member0[(_763 + 84u) / 4u], pushBlocks._member0[(_763 + 88u) / 4u], pushBlocks._member0[(_763 + 92u) / 4u]), vec4(pushBlocks._member0[(_763 + 96u) / 4u], pushBlocks._member0[(_763 + 100u) / 4u], pushBlocks._member0[(_763 + 104u) / 4u], pushBlocks._member0[(_763 + 108u) / 4u]), vec4(pushBlocks._member0[(_763 + 112u) / 4u], pushBlocks._member0[(_763 + 116u) / 4u], pushBlocks._member0[(_763 + 120u) / 4u], pushBlocks._member0[(_763 + 124u) / 4u]));
        push = _900;
        vec4 sampled = texture(_1403, input_diffuseUv);
        bool translucent = push.markerExposure.x < 0.0;
        bool _719;
        if (!translucent)
        {
            _719 = sampled.w < cAlphaReference;
        }
        else
        {
            _719 = false;
        }
        if (_719)
        {
            discard;
        }
        vec4 sampled_1 = texture(_1405, input_lightmapUv);
        vec3 _941 = sampled_1.xyz;
        vec3 _1285;
        if (_952)
        {
            _1285 = mix((pow(max(_941, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _941 * 12.9200000762939453125, step(_941, vec3(0.003130800090730190277099609375)));
        }
        else
        {
            _1285 = _941;
        }
        if (abs(push.normalKey0.w) > 0.5)
        {
            _719 = true;
        }
        else
        {
            _719 = push.markerExposure.x < 0.5;
        }
        vec3 modelTint;
        if (_719)
        {
            modelTint = vec3(1.0);
        }
        else
        {
            modelTint = push.normalKey1.xyz;
        }
        bool _1004 = abs(push.markerExposure.x) > 0.5;
        if (_1004)
        {
            _719 = _1007;
        }
        else
        {
            _719 = false;
        }
        float lightmapGain;
        if (_719)
        {
            lightmapGain = push.markerExposure.y;
        }
        else
        {
            lightmapGain = 1.0;
        }
        vec3 _1020 = sampled.xyz;
        vec3 _1303;
        if (_952)
        {
            _1303 = mix((pow(max(_1020, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _1020 * 12.9200000762939453125, step(_1020, vec3(0.003130800090730190277099609375)));
        }
        else
        {
            _1303 = _1020;
        }
        vec3 base_1 = ((((_1303 * _1285) * lightmapGain) * input_color.xyz) * input_fade) * modelTint;
        vec3 base;
        if (_295)
        {
            vec4 sampled_2 = texture(_1407, normalize(input_reflection).xzy);
            vec3 _1050 = sampled_2.xyz;
            vec3 _1321;
            if (_952)
            {
                _1321 = mix((pow(max(_1050, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _1050 * 12.9200000762939453125, step(_1050, vec3(0.003130800090730190277099609375)));
            }
            else
            {
                _1321 = _1050;
            }
            base = base_1 + _1321;
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
        if (_1004)
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
            vec3 _1094 = mix(push.normalKey2.xyz * fogShare, base, vec3(input_fog));
            if (_1095)
            {
                vec3 _1097 = max(_1094, vec3(0.0));
                entryPointParam_fragmentMain = vec4(mix(pow(max((_1097 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _1097 * vec3(0.077399380505084991455078125), step(_1097, vec3(0.040449999272823333740234375))) * push.markerExposure.y, alpha);
            }
            else
            {
                entryPointParam_fragmentMain = vec4(_1094, alpha);
            }
        }
        else
        {
            if (_1095)
            {
                vec3 _1133 = max(base, vec3(0.0));
                base = mix(pow(max((_1133 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _1133 * vec3(0.077399380505084991455078125), step(_1133, vec3(0.040449999272823333740234375)));
            }
            vec3 _1138 = normalize(vec3(-0.3499999940395355224609375, 0.449999988079071044921875, -0.819999992847442626953125));
            vec3 _1150 = normalize(input_normal);
            vec3 keyColour = vec3(push.normalKey0.w, push.normalKey1.w, push.normalKey2.w);
            float _1177 = max(dot(_1150, _1138), 0.0);
            vec3 _1206 = mix(vec3(0.039999999105930328369140625), base, vec3(0.3499999940395355224609375)) * keyColour;
            entryPointParam_fragmentMain = vec4(((((base * 0.64999997615814208984375) * ((vec3(push.markerExposure.w, push.markerExposure.w, push.markerExposure.w) + (keyColour * _1177)) + ((keyColour * 0.25) * max(dot(_1150, normalize(vec3(0.550000011920928955078125, -0.20000000298023223876953125, -0.550000011920928955078125))), 0.0)))) + ((_1206 * (pow(max(dot(_1150, normalize(_1138 + vec3(0.0, 0.0, -1.0))), 0.0), 132.5139312744140625) * 5.620557308197021484375)) * _1177)) + (((_1206 * pow(1.0 - max(dot(_1150, vec3(0.0, 0.0, -1.0)), 0.0), 4.0)) * 0.3499999940395355224609375) * 0.60000002384185791015625)) * push.markerExposure.y, alpha);
        }
    }
}

