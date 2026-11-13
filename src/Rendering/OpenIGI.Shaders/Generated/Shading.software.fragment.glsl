#version 430
#extension GL_EXT_texture_shadow_lod : enable
#ifdef GL_EXT_texture_shadow_lod
#define spvShadowLod0(s, c) textureLod(s, c, 0.0)
#else
#define spvShadowLod0(s, c) textureGrad(s, c, vec2(0.0), vec2(0.0))
#endif
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
const bool _57 = (cClipYDown != 0);

struct _Array_std140_matrix_float_4_4_4
{
    mat4 data[4];
};

struct FlashlightParams_std140
{
    vec4 position;
    vec4 axis;
    vec4 up;
    vec4 colour;
    vec4 cone;
    vec4 control;
};

uniform vec2 _2372_frag[12] = vec2[](vec2(-0.3260000050067901611328125, -0.4059999883174896240234375), vec2(-0.839999973773956298828125, -0.07400000095367431640625), vec2(-0.69599997997283935546875, 0.4569999873638153076171875), vec2(-0.20299999415874481201171875, 0.620999991893768310546875), vec2(0.96200001239776611328125, -0.194999992847442626953125), vec2(0.472999989986419677734375, -0.4799999892711639404296875), vec2(0.518999993801116943359375, 0.767000019550323486328125), vec2(0.185000002384185791015625, -0.89300000667572021484375), vec2(0.507000029087066650390625, 0.064000003039836883544921875), vec2(0.89600002765655517578125, 0.412000000476837158203125), vec2(-0.3219999969005584716796875, -0.933000028133392333984375), vec2(-0.791999995708465576171875, -0.597999989986419677734375));
uniform vec2 _3646_frag[4] = vec2[](vec2(0.0), vec2(0.430000007152557373046875, 0.709999978542327880859375), vec2(0.790000021457672119140625, 0.23999999463558197021484375), vec2(0.17000000178813934326171875, 0.4600000083446502685546875));

struct _Array_std140_vector_float_4_160
{
    vec4 data[160];
};

uvec3 _13735;
float _13736;

layout(binding = 0, std140) uniform Gi_std140
{
    vec4 unproject;
    vec4 probeOrigin;
    vec4 probeInvCount;
    vec4 probeMisc;
    vec4 probeSky;
    vec4 camera;
    vec4 giParams;
    layout(row_major) mat4 viewToWorld;
    vec4 cascadeSplits;
    vec4 shadowConfig;
    layout(row_major) _Array_std140_matrix_float_4_4_4 cascadeMatrix;
    vec4 wetness0;
    vec4 wetness1;
    vec4 shelterMap0;
    vec4 shelterMap1;
    vec4 shelterMap2;
    vec4 fog;
    FlashlightParams_std140 flashlight;
} gi;

layout(binding = 2, std140) uniform Light_std140
{
    vec4 sunDirectionModel;
    vec4 sunColourScale;
    vec4 ambient;
    vec4 sky;
    vec4 moon;
    vec4 stars;
} light;

layout(binding = 1, std140) uniform Lamps_std140
{
    vec4 control;
    vec4 source;
    _Array_std140_vector_float_4_160 positionRadius;
    _Array_std140_vector_float_4_160 colourIntensity;
    _Array_std140_vector_float_4_160 direction;
    _Array_std140_vector_float_4_160 rect;
} lamps;

layout(binding = 0, std430) restrict readonly buffer StructuredBuffer
{
    uint _member0[];
} uLampGrid;

layout(binding = 0) uniform sampler2D _13742;
layout(binding = 2) uniform sampler2D _13745;
layout(binding = 3) uniform sampler2D _13748;
layout(binding = 4) uniform sampler2D _13751;
layout(binding = 19) uniform sampler3D _13753;
layout(binding = 13) uniform sampler2D _13756;
layout(binding = 14) uniform sampler2D _13758;
layout(binding = 15) uniform sampler2D _13760;
layout(binding = 1) uniform sampler2D _13763;
layout(binding = 12) uniform sampler2D _13765;
layout(binding = 9) uniform sampler2D _13767;
layout(binding = 21) uniform sampler2D _13770;
layout(binding = 10) uniform sampler2DArray _13773;
layout(binding = 11) uniform sampler2DArrayShadow _13775;
layout(binding = 17) uniform sampler2D _13777;
layout(binding = 20) uniform sampler2D _13780;
layout(binding = 18) uniform sampler2D _13783;
layout(binding = 5) uniform sampler3D _13785;
layout(binding = 6) uniform sampler3D _13787;
layout(binding = 7) uniform sampler3D _13789;
layout(binding = 8) uniform sampler3D _13791;
layout(binding = 16) uniform sampler2D _13794;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) in vec2 input_uv;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    bool _9905 = false;
    bool _9208 = false;
    {
        float _5604 = fwidth(input_uv.x);
        vec4 _5612 = texelFetch(_13742, ivec3(ivec2(gl_FragCoord.xy), 0).xy, 0);
        vec3 albedo = _5612.xyz;
        vec4 _5621 = texelFetch(_13745, ivec3(ivec2(gl_FragCoord.xy), 0).xy, 0);
        vec3 baked = texelFetch(_13748, ivec3(ivec2(gl_FragCoord.xy), 0).xy, 0).xyz;
        vec4 _5641 = vec4(texelFetch(_13751, ivec3(ivec2(gl_FragCoord.xy), 0).xy, 0).x, 0.0, 0.0, 0.0);
        float depth = _5641.x;
        int debugView = int(light.ambient.w + 0.5);
        bool lampGridded;
        float visibility;
        vec3 sunLight;
        vec3 lampLight;
        if (depth <= 0.0)
        {
            if (debugView != 0)
            {
                lampGridded = debugView != 11;
            }
            else
            {
                lampGridded = false;
            }
            if (lampGridded)
            {
                entryPointParam_fragmentMain = vec4(0.0, 0.0, 0.0, 1.0);
            }
            else
            {
                float _425 = clamp(light.sky.x, 0.0, 1.0);
                if (light.sky.w > 0.0)
                {
                    visibility = light.sky.w;
                }
                else
                {
                    float _5647 = (_5621.y * 255.0) - 128.0;
                    float _5642;
                    if (_5647 > 0.5)
                    {
                        _5642 = _5647 * 0.0625;
                    }
                    else
                    {
                        _5642 = 1.0;
                    }
                    visibility = _5642;
                }
                vec3 paintedSky = albedo * visibility;
                if (light.stars.w < 0.5)
                {
                    vec3 _5655;
                    {
                        if (gi.fog.z <= 0.0)
                        {
                            _5655 = paintedSky;
                        }
                        else
                        {
                            vec4 _5685 = textureLod(_13753, vec3(input_uv, clamp(((log2(max((gi.unproject.w * gi.fog.x) + gi.fog.y, 9.9999999747524270787835121154785e-07)) * gi.fog.z) - 1.0) * gi.fog.w, 0.0, 1.0)), 0.0);
                            _5655 = (paintedSky * _5685.w) + _5685.xyz;
                        }
                    }
                    sunLight = _5655;
                }
                else
                {
                    sunLight = paintedSky;
                }
                if (_425 >= 0.999000012874603271484375)
                {
                    entryPointParam_fragmentMain = vec4(sunLight, 1.0);
                }
                else
                {
                    vec2 ndc = (input_uv * 2.0) - vec2(1.0);
                    vec3 _557 = normalize((vec4(normalize(vec3(ndc.x / gi.unproject.x, (-ndc.y) / gi.unproject.y, 1.0)), 0.0) * spvWorkaroundRowMajor(gi.viewToWorld)).xyz);
                    uint _5694 = uvec2(textureSize(_13756, 0)).y;
                    float _5716 = acos(clamp(sqrt(max(0.0, (gi.giParams.w * gi.giParams.w) - 40449600.0)) / gi.giParams.w, -1.0, 1.0));
                    float _5717 = 3.1415927410125732421875 - _5716;
                    float _5720 = acos(clamp(_557.z, -1.0, 1.0));
                    float _5706;
                    if (_5720 < _5717)
                    {
                        _5706 = (1.0 - sqrt(max(0.0, 1.0 - (_5720 / _5717)))) * 0.5;
                    }
                    else
                    {
                        _5706 = (sqrt(max(0.0, (_5720 - _5717) / max(_5716, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                    }
                    float _5740 = atan(_557.y, _557.x);
                    float _5707;
                    if (_5740 < 0.0)
                    {
                        _5707 = _5740 + 6.283185482025146484375;
                    }
                    else
                    {
                        _5707 = _5740;
                    }
                    vec2 _5749 = vec2(_5707 * 0.15915493667125701904296875, _5706);
                    float _5703 = float(_5694);
                    _5749.y = min(_5749.y, (floor(_5703 * 0.5) - 1.0) / max(_5703 - 1.0, 1.0));
                    uvec2 _5768 = uvec2(textureSize(_13756, 0));
                    vec2 _5775 = vec2(float(_5768.x), float(_5768.y));
                    float _701 = 2.0 * _5604;
                    float sunPixelAngle = _701 / max(gi.unproject.x, 9.9999997473787516355514526367188e-05);
                    vec3 _714 = normalize((vec4(light.sunDirectionModel.xyz, 0.0) * spvWorkaroundRowMajor(gi.viewToWorld)).xyz);
                    vec3 _5792;
                    {
                        float _5797 = dot(_557, _714);
                        bool _5793;
                        if (_5797 <= 0.999000012874603271484375)
                        {
                            _5793 = true;
                        }
                        else
                        {
                            _5793 = _714.z <= (-0.0199999995529651641845703125);
                        }
                        if (_5793)
                        {
                            _5792 = vec3(0.0);
                        }
                        else
                        {
                            float _5808 = acos(clamp(_5797, 0.0, 1.0));
                            float _5809 = max(0.0046799997799098491668701171875, sunPixelAngle);
                            float _5810 = sunPixelAngle * 0.5;
                            float _5814 = 1.0 - smoothstep(_5809 - _5810, _5809 + _5810, _5808);
                            if (_5814 <= 0.0)
                            {
                                _5792 = vec3(0.0);
                            }
                            else
                            {
                                float _5819 = clamp(_5808 / _5809, 0.0, 1.0);
                                _5792 = min((light.sunColourScale.xyz * light.sunColourScale.w) / vec3(max(6.283185482025146484375 * (1.0 - cos(_5809)), 9.9999999392252902907785028219223e-09)), vec3(4000.0)) * ((0.4000000059604644775390625 + (0.60000002384185791015625 * sqrt(max(1.0 - (_5819 * _5819), 0.0)))) * _5814);
                            }
                        }
                    }
                    vec3 background = textureLod(_13758, (_5749 * ((_5775 - vec2(1.0)) / _5775)) + (vec2(0.5) / _5775), 0.0).xyz + _5792;
                    if (light.stars.x > 0.0)
                    {
                        lampGridded = true;
                    }
                    else
                    {
                        lampGridded = light.moon.w > 0.0;
                    }
                    if (lampGridded)
                    {
                        float pixelAngle = _701 / max(gi.unproject.x, 9.9999997473787516355514526367188e-05);
                        vec3 _810 = normalize(light.moon.xyz);
                        vec3 _5838;
                        {
                            vec3 _5843 = vec3(0.0, 0.0, gi.giParams.w);
                            vec2 _5867;
                            {
                                float _5871 = dot(_557, _557);
                                float _5872 = dot(_5843, _557);
                                float _5880 = (4.0 * (_5872 * _5872)) - ((4.0 * _5871) * (dot(_5843, _5843) - 40449600.0));
                                if (_5880 < 0.0)
                                {
                                    _5867 = vec2(-1.0);
                                }
                                else
                                {
                                    float _5884 = sqrt(_5880);
                                    float _5885 = _5872 * (-2.0);
                                    float _5887 = 2.0 * _5871;
                                    _5867 = vec2((_5885 - _5884) / _5887, (_5885 + _5884) / _5887);
                                }
                            }
                            bool _5839;
                            if (_5867.x > 0.0)
                            {
                                _5839 = true;
                            }
                            else
                            {
                                _5839 = _5867.y > 0.0;
                            }
                            if (_5839)
                            {
                                _5838 = vec3(0.0);
                            }
                            else
                            {
                                float _5855 = _557.z;
                                float _5897 = gi.giParams.w * gi.giParams.w;
                                float _5900 = sqrt(max(0.0, _5897 - 40449600.0));
                                vec3 _5862 = textureLod(_13760, vec2((max(0.0, ((-gi.giParams.w) * _5855) + sqrt(max(0.0, (_5897 * ((_5855 * _5855) - 1.0)) + 41216400.0))) - (6420.0 - gi.giParams.w)) / ((-5544.3291015625) + (_5900 + gi.giParams.w)), _5900 * 0.001141981221735477447509765625), 0.0).xyz;
                                _5838 = _5862 * _5862;
                            }
                        }
                        float _5926 = clamp(1.0 - light.stars.y, 0.0, 1.0);
                        bool _5921;
                        if (light.stars.x > 0.0)
                        {
                            _5921 = _5926 > 0.0;
                        }
                        else
                        {
                            _5921 = false;
                        }
                        vec3 _5922;
                        if (_5921)
                        {
                            vec3 _6042 = _810 - (vec3(0.0, 0.62000000476837158203125, 0.785000026226043701171875) * dot(_810, vec3(0.0, 0.62000000476837158203125, 0.785000026226043701171875)));
                            float _6043 = length(_6042);
                            vec3 _6037;
                            if (_6043 > 9.9999997473787516355514526367188e-05)
                            {
                                _6037 = _6042 / vec3(_6043);
                            }
                            else
                            {
                                _6037 = vec3(1.0, 0.0, 0.0);
                            }
                            vec3 _6056 = vec3(dot(_557, _6037), dot(_557, cross(vec3(0.0, 0.62000000476837158203125, 0.785000026226043701171875), _6037)), dot(_557, vec3(0.0, 0.62000000476837158203125, 0.785000026226043701171875)));
                            float _5941 = 0.3499999940395355224609375 + (0.64999997615814208984375 * clamp(1.0 - (_557.z * 2.5), 0.0, 1.0));
                            vec3 _6058;
                            {
                                vec2 _6059;
                                uint _6127;
                                {
                                    vec3 _6133 = abs(_6056);
                                    float _6134 = _6133.x;
                                    float _6135 = _6133.y;
                                    bool _6128;
                                    if (_6134 >= _6135)
                                    {
                                        _6128 = _6134 >= _6133.z;
                                    }
                                    else
                                    {
                                        _6128 = false;
                                    }
                                    uint _6129;
                                    if (_6128)
                                    {
                                        _6059 = _6056.yz / vec2(_6134);
                                        if (_6056.x > 0.0)
                                        {
                                            _6129 = 0u;
                                        }
                                        else
                                        {
                                            _6129 = 1u;
                                        }
                                        _6127 = _6129;
                                    }
                                    else
                                    {
                                        float _6154 = _6133.z;
                                        if (_6135 >= _6154)
                                        {
                                            _6059 = _6056.xz / vec2(_6135);
                                            if (_6056.y > 0.0)
                                            {
                                                _6129 = 2u;
                                            }
                                            else
                                            {
                                                _6129 = 3u;
                                            }
                                            _6127 = _6129;
                                        }
                                        else
                                        {
                                            _6059 = _6056.xy / vec2(_6154);
                                            if (_6056.z > 0.0)
                                            {
                                                _6129 = 4u;
                                            }
                                            else
                                            {
                                                _6129 = 5u;
                                            }
                                            _6127 = _6129;
                                        }
                                    }
                                }
                                vec2 _6069 = floor(((_6059 * 0.5) + vec2(0.5)) * 55.0);
                                uvec3 _6076 = uvec3(uint(_6069.x), uint(_6069.y), (_6127 * 7919u) + 17u);
                                uvec3 _6189 = (_6076 * uvec3(1664525u)) + uvec3(1013904223u);
                                _6189.x = _6189.x + (_6189.y * _6189.z);
                                _6189.y = _6189.y + (_6189.z * _6189.x);
                                _6189.z = _6189.z + (_6189.x * _6189.y);
                                uvec3 _6216 = _6189 ^ (_6189 >> uvec3(16u));
                                _6216.x = _6216.x + (_6216.y * _6216.z);
                                _6216.y = _6216.y + (_6216.z * _6216.x);
                                _6216.z = _6216.z + (_6216.x * _6216.y);
                                vec3 _6182 = vec3(_6216) * 2.3283064365386962890625e-10;
                                if (_6182.z > 0.5)
                                {
                                    _6058 = vec3(0.0);
                                }
                                else
                                {
                                    uvec3 _6248 = ((_6076 + uvec3(101u, 211u, 307u)) * uvec3(1664525u)) + uvec3(1013904223u);
                                    _6248.x = _6248.x + (_6248.y * _6248.z);
                                    _6248.y = _6248.y + (_6248.z * _6248.x);
                                    _6248.z = _6248.z + (_6248.x * _6248.y);
                                    uvec3 _6275 = _6248 ^ (_6248 >> uvec3(16u));
                                    _6275.x = _6275.x + (_6275.y * _6275.z);
                                    _6275.y = _6275.y + (_6275.z * _6275.x);
                                    _6275.z = _6275.z + (_6275.x * _6275.y);
                                    vec3 _6241 = vec3(_6275) * 2.3283064365386962890625e-10;
                                    vec2 _6093 = ((((_6069 + vec2(0.25)) + (_6182.xy * 0.5)) * vec2(0.01818181760609149932861328125)) - vec2(0.5)) * 2.0;
                                    vec3 _6296;
                                    if (_6127 == 0u)
                                    {
                                        _6296 = vec3(1.0, _6093);
                                    }
                                    else
                                    {
                                        if (_6127 == 1u)
                                        {
                                            _6296 = vec3(-1.0, _6093);
                                        }
                                        else
                                        {
                                            if (_6127 == 2u)
                                            {
                                                _6296 = vec3(_6093.x, 1.0, _6093.y);
                                            }
                                            else
                                            {
                                                if (_6127 == 3u)
                                                {
                                                    _6296 = vec3(_6093.x, -1.0, _6093.y);
                                                }
                                                else
                                                {
                                                    if (_6127 == 4u)
                                                    {
                                                        _6296 = vec3(_6093, 1.0);
                                                    }
                                                    else
                                                    {
                                                        _6296 = vec3(_6093, -1.0);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    float _6096 = length(_6056 - normalize(_6296));
                                    float _6098 = max(pixelAngle * 0.60000002384185791015625, 9.9999997473787516355514526367188e-06);
                                    float _6114 = _6241.z;
                                    vec3 _6340;
                                    {
                                        if (_6114 < 0.180000007152557373046875)
                                        {
                                            _6340 = vec3(0.7200000286102294921875, 0.819999992847442626953125, 1.0);
                                        }
                                        else
                                        {
                                            if (_6114 < 0.62000000476837158203125)
                                            {
                                                _6340 = vec3(0.949999988079071044921875, 0.959999978542327880859375, 1.0);
                                            }
                                            else
                                            {
                                                if (_6114 < 0.86000001430511474609375)
                                                {
                                                    _6340 = vec3(1.0, 0.920000016689300537109375, 0.7599999904632568359375);
                                                }
                                                else
                                                {
                                                    _6340 = vec3(1.0, 0.7599999904632568359375, 0.519999980926513671875);
                                                }
                                            }
                                        }
                                    }
                                    _6058 = _6340 * (((0.014999999664723873138427734375 + (0.98500001430511474609375 * pow(_6241.x, 12.0))) * (1.0 + ((_5941 * 0.449999988079071044921875) * sin((light.stars.z * (2.0 + (6.0 * _6241.y))) + (_6114 * 60.0))))) * exp((-(_6096 * _6096)) / ((2.0 * _6098) * _6098)));
                                }
                            }
                            vec3 _6356;
                            {
                                vec2 _6357;
                                uint _6425;
                                {
                                    vec3 _6431 = abs(_6056);
                                    float _6432 = _6431.x;
                                    float _6433 = _6431.y;
                                    bool _6426;
                                    if (_6432 >= _6433)
                                    {
                                        _6426 = _6432 >= _6431.z;
                                    }
                                    else
                                    {
                                        _6426 = false;
                                    }
                                    uint _6427;
                                    if (_6426)
                                    {
                                        _6357 = _6056.yz / vec2(_6432);
                                        if (_6056.x > 0.0)
                                        {
                                            _6427 = 0u;
                                        }
                                        else
                                        {
                                            _6427 = 1u;
                                        }
                                        _6425 = _6427;
                                    }
                                    else
                                    {
                                        float _6452 = _6431.z;
                                        if (_6433 >= _6452)
                                        {
                                            _6357 = _6056.xz / vec2(_6433);
                                            if (_6056.y > 0.0)
                                            {
                                                _6427 = 2u;
                                            }
                                            else
                                            {
                                                _6427 = 3u;
                                            }
                                            _6425 = _6427;
                                        }
                                        else
                                        {
                                            _6357 = _6056.xy / vec2(_6452);
                                            if (_6056.z > 0.0)
                                            {
                                                _6427 = 4u;
                                            }
                                            else
                                            {
                                                _6427 = 5u;
                                            }
                                            _6425 = _6427;
                                        }
                                    }
                                }
                                vec2 _6367 = floor(((_6357 * 0.5) + vec2(0.5)) * 150.0);
                                uvec3 _6374 = uvec3(uint(_6367.x), uint(_6367.y), (_6425 * 7919u) + 131u);
                                uvec3 _6487 = (_6374 * uvec3(1664525u)) + uvec3(1013904223u);
                                _6487.x = _6487.x + (_6487.y * _6487.z);
                                _6487.y = _6487.y + (_6487.z * _6487.x);
                                _6487.z = _6487.z + (_6487.x * _6487.y);
                                uvec3 _6514 = _6487 ^ (_6487 >> uvec3(16u));
                                _6514.x = _6514.x + (_6514.y * _6514.z);
                                _6514.y = _6514.y + (_6514.z * _6514.x);
                                _6514.z = _6514.z + (_6514.x * _6514.y);
                                vec3 _6480 = vec3(_6514) * 2.3283064365386962890625e-10;
                                if (_6480.z > 0.300000011920928955078125)
                                {
                                    _6356 = vec3(0.0);
                                }
                                else
                                {
                                    uvec3 _6546 = ((_6374 + uvec3(101u, 211u, 307u)) * uvec3(1664525u)) + uvec3(1013904223u);
                                    _6546.x = _6546.x + (_6546.y * _6546.z);
                                    _6546.y = _6546.y + (_6546.z * _6546.x);
                                    _6546.z = _6546.z + (_6546.x * _6546.y);
                                    uvec3 _6573 = _6546 ^ (_6546 >> uvec3(16u));
                                    _6573.x = _6573.x + (_6573.y * _6573.z);
                                    _6573.y = _6573.y + (_6573.z * _6573.x);
                                    _6573.z = _6573.z + (_6573.x * _6573.y);
                                    vec3 _6539 = vec3(_6573) * 2.3283064365386962890625e-10;
                                    vec2 _6391 = ((((_6367 + vec2(0.25)) + (_6480.xy * 0.5)) * vec2(0.006666666828095912933349609375)) - vec2(0.5)) * 2.0;
                                    vec3 _6594;
                                    if (_6425 == 0u)
                                    {
                                        _6594 = vec3(1.0, _6391);
                                    }
                                    else
                                    {
                                        if (_6425 == 1u)
                                        {
                                            _6594 = vec3(-1.0, _6391);
                                        }
                                        else
                                        {
                                            if (_6425 == 2u)
                                            {
                                                _6594 = vec3(_6391.x, 1.0, _6391.y);
                                            }
                                            else
                                            {
                                                if (_6425 == 3u)
                                                {
                                                    _6594 = vec3(_6391.x, -1.0, _6391.y);
                                                }
                                                else
                                                {
                                                    if (_6425 == 4u)
                                                    {
                                                        _6594 = vec3(_6391, 1.0);
                                                    }
                                                    else
                                                    {
                                                        _6594 = vec3(_6391, -1.0);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    float _6394 = length(_6056 - normalize(_6594));
                                    float _6396 = max(pixelAngle * 0.60000002384185791015625, 9.9999997473787516355514526367188e-06);
                                    float _6412 = _6539.z;
                                    vec3 _6638;
                                    {
                                        if (_6412 < 0.180000007152557373046875)
                                        {
                                            _6638 = vec3(0.7200000286102294921875, 0.819999992847442626953125, 1.0);
                                        }
                                        else
                                        {
                                            if (_6412 < 0.62000000476837158203125)
                                            {
                                                _6638 = vec3(0.949999988079071044921875, 0.959999978542327880859375, 1.0);
                                            }
                                            else
                                            {
                                                if (_6412 < 0.86000001430511474609375)
                                                {
                                                    _6638 = vec3(1.0, 0.920000016689300537109375, 0.7599999904632568359375);
                                                }
                                                else
                                                {
                                                    _6638 = vec3(1.0, 0.7599999904632568359375, 0.519999980926513671875);
                                                }
                                            }
                                        }
                                    }
                                    _6356 = _6638 * (((0.014999999664723873138427734375 + (0.98500001430511474609375 * pow(_6539.x, 12.0))) * (1.0 + ((_5941 * 0.449999988079071044921875) * sin((light.stars.z * (2.0 + (6.0 * _6539.y))) + (_6412 * 60.0))))) * exp((-(_6394 * _6394)) / ((2.0 * _6396) * _6396)));
                                }
                            }
                            float _5947 = dot(_6056, normalize(vec3(0.4600000083446502685546875, -0.519999980926513671875, 0.7200000286102294921875)));
                            vec3 _5952 = _6056 * 9.0;
                            vec3 _6655 = floor(_5952);
                            vec3 _6656 = _5952 - _6655;
                            vec3 _6661 = (_6656 * _6656) * (vec3(3.0) - (_6656 * 2.0));
                            uvec3 _6664 = uvec3(ivec3(_6655) + ivec3(4096));
                            uvec3 _6709 = (_6664 * uvec3(1664525u)) + uvec3(1013904223u);
                            _6709.x = _6709.x + (_6709.y * _6709.z);
                            _6709.y = _6709.y + (_6709.z * _6709.x);
                            _6709.z = _6709.z + (_6709.x * _6709.y);
                            uvec3 _6736 = _6709 ^ (_6709 >> uvec3(16u));
                            uvec3 _13170;
                            _13170.x = _6736.x + (_6736.y * _6736.z);
                            uvec3 _13173 = _13170;
                            _13173.y = _13170.y + (_13170.z * _13170.x);
                            uvec3 _13176 = _13170;
                            _13176.z = _13170.z + (_13170.x * _13170.y);
                            uvec3 _6768 = ((_6664 + uvec3(1u, 0u, 0u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _6768.x = _6768.x + (_6768.y * _6768.z);
                            _6768.y = _6768.y + (_6768.z * _6768.x);
                            _6768.z = _6768.z + (_6768.x * _6768.y);
                            uvec3 _6795 = _6768 ^ (_6768 >> uvec3(16u));
                            uvec3 _13188;
                            _13188.x = _6795.x + (_6795.y * _6795.z);
                            uvec3 _13191 = _13188;
                            _13191.y = _13188.y + (_13188.z * _13188.x);
                            uvec3 _13194 = _13188;
                            _13194.z = _13188.z + (_13188.x * _13188.y);
                            uvec3 _6827 = ((_6664 + uvec3(0u, 1u, 0u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _6827.x = _6827.x + (_6827.y * _6827.z);
                            _6827.y = _6827.y + (_6827.z * _6827.x);
                            _6827.z = _6827.z + (_6827.x * _6827.y);
                            uvec3 _6854 = _6827 ^ (_6827 >> uvec3(16u));
                            uvec3 _13206;
                            _13206.x = _6854.x + (_6854.y * _6854.z);
                            uvec3 _13209 = _13206;
                            _13209.y = _13206.y + (_13206.z * _13206.x);
                            uvec3 _13212 = _13206;
                            _13212.z = _13206.z + (_13206.x * _13206.y);
                            uvec3 _6886 = ((_6664 + uvec3(1u, 1u, 0u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _6886.x = _6886.x + (_6886.y * _6886.z);
                            _6886.y = _6886.y + (_6886.z * _6886.x);
                            _6886.z = _6886.z + (_6886.x * _6886.y);
                            uvec3 _6913 = _6886 ^ (_6886 >> uvec3(16u));
                            uvec3 _13224;
                            _13224.x = _6913.x + (_6913.y * _6913.z);
                            uvec3 _13227 = _13224;
                            _13227.y = _13224.y + (_13224.z * _13224.x);
                            uvec3 _13230 = _13224;
                            _13230.z = _13224.z + (_13224.x * _13224.y);
                            uvec3 _6945 = ((_6664 + uvec3(0u, 0u, 1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _6945.x = _6945.x + (_6945.y * _6945.z);
                            _6945.y = _6945.y + (_6945.z * _6945.x);
                            _6945.z = _6945.z + (_6945.x * _6945.y);
                            uvec3 _6972 = _6945 ^ (_6945 >> uvec3(16u));
                            uvec3 _13242;
                            _13242.x = _6972.x + (_6972.y * _6972.z);
                            uvec3 _13245 = _13242;
                            _13245.y = _13242.y + (_13242.z * _13242.x);
                            uvec3 _13248 = _13242;
                            _13248.z = _13242.z + (_13242.x * _13242.y);
                            uvec3 _7004 = ((_6664 + uvec3(1u, 0u, 1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7004.x = _7004.x + (_7004.y * _7004.z);
                            _7004.y = _7004.y + (_7004.z * _7004.x);
                            _7004.z = _7004.z + (_7004.x * _7004.y);
                            uvec3 _7031 = _7004 ^ (_7004 >> uvec3(16u));
                            uvec3 _13260;
                            _13260.x = _7031.x + (_7031.y * _7031.z);
                            uvec3 _13263 = _13260;
                            _13263.y = _13260.y + (_13260.z * _13260.x);
                            uvec3 _13266 = _13260;
                            _13266.z = _13260.z + (_13260.x * _13260.y);
                            uvec3 _7063 = ((_6664 + uvec3(0u, 1u, 1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7063.x = _7063.x + (_7063.y * _7063.z);
                            _7063.y = _7063.y + (_7063.z * _7063.x);
                            _7063.z = _7063.z + (_7063.x * _7063.y);
                            uvec3 _7090 = _7063 ^ (_7063 >> uvec3(16u));
                            uvec3 _13278;
                            _13278.x = _7090.x + (_7090.y * _7090.z);
                            uvec3 _13281 = _13278;
                            _13281.y = _13278.y + (_13278.z * _13278.x);
                            uvec3 _13284 = _13278;
                            _13284.z = _13278.z + (_13278.x * _13278.y);
                            uvec3 _7122 = ((_6664 + uvec3(1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7122.x = _7122.x + (_7122.y * _7122.z);
                            _7122.y = _7122.y + (_7122.z * _7122.x);
                            _7122.z = _7122.z + (_7122.x * _7122.y);
                            uvec3 _7149 = _7122 ^ (_7122 >> uvec3(16u));
                            uvec3 _13296;
                            _13296.x = _7149.x + (_7149.y * _7149.z);
                            uvec3 _13299 = _13296;
                            _13299.y = _13296.y + (_13296.z * _13296.x);
                            uvec3 _13302 = _13296;
                            _13302.z = _13296.z + (_13296.x * _13296.y);
                            float _6688 = _6661.x;
                            float _6693 = _6661.y;
                            vec3 _5955 = _6056 * 23.0;
                            vec3 _7172 = floor(_5955);
                            vec3 _7173 = _5955 - _7172;
                            vec3 _7178 = (_7173 * _7173) * (vec3(3.0) - (_7173 * 2.0));
                            uvec3 _7181 = uvec3(ivec3(_7172) + ivec3(4096));
                            uvec3 _7226 = (_7181 * uvec3(1664525u)) + uvec3(1013904223u);
                            _7226.x = _7226.x + (_7226.y * _7226.z);
                            _7226.y = _7226.y + (_7226.z * _7226.x);
                            _7226.z = _7226.z + (_7226.x * _7226.y);
                            uvec3 _7253 = _7226 ^ (_7226 >> uvec3(16u));
                            uvec3 _13314;
                            _13314.x = _7253.x + (_7253.y * _7253.z);
                            uvec3 _13317 = _13314;
                            _13317.y = _13314.y + (_13314.z * _13314.x);
                            uvec3 _13320 = _13314;
                            _13320.z = _13314.z + (_13314.x * _13314.y);
                            uvec3 _7285 = ((_7181 + uvec3(1u, 0u, 0u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7285.x = _7285.x + (_7285.y * _7285.z);
                            _7285.y = _7285.y + (_7285.z * _7285.x);
                            _7285.z = _7285.z + (_7285.x * _7285.y);
                            uvec3 _7312 = _7285 ^ (_7285 >> uvec3(16u));
                            uvec3 _13332;
                            _13332.x = _7312.x + (_7312.y * _7312.z);
                            uvec3 _13335 = _13332;
                            _13335.y = _13332.y + (_13332.z * _13332.x);
                            uvec3 _13338 = _13332;
                            _13338.z = _13332.z + (_13332.x * _13332.y);
                            uvec3 _7344 = ((_7181 + uvec3(0u, 1u, 0u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7344.x = _7344.x + (_7344.y * _7344.z);
                            _7344.y = _7344.y + (_7344.z * _7344.x);
                            _7344.z = _7344.z + (_7344.x * _7344.y);
                            uvec3 _7371 = _7344 ^ (_7344 >> uvec3(16u));
                            uvec3 _13350;
                            _13350.x = _7371.x + (_7371.y * _7371.z);
                            uvec3 _13353 = _13350;
                            _13353.y = _13350.y + (_13350.z * _13350.x);
                            uvec3 _13356 = _13350;
                            _13356.z = _13350.z + (_13350.x * _13350.y);
                            uvec3 _7403 = ((_7181 + uvec3(1u, 1u, 0u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7403.x = _7403.x + (_7403.y * _7403.z);
                            _7403.y = _7403.y + (_7403.z * _7403.x);
                            _7403.z = _7403.z + (_7403.x * _7403.y);
                            uvec3 _7430 = _7403 ^ (_7403 >> uvec3(16u));
                            uvec3 _13368;
                            _13368.x = _7430.x + (_7430.y * _7430.z);
                            uvec3 _13371 = _13368;
                            _13371.y = _13368.y + (_13368.z * _13368.x);
                            uvec3 _13374 = _13368;
                            _13374.z = _13368.z + (_13368.x * _13368.y);
                            uvec3 _7462 = ((_7181 + uvec3(0u, 0u, 1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7462.x = _7462.x + (_7462.y * _7462.z);
                            _7462.y = _7462.y + (_7462.z * _7462.x);
                            _7462.z = _7462.z + (_7462.x * _7462.y);
                            uvec3 _7489 = _7462 ^ (_7462 >> uvec3(16u));
                            uvec3 _13386;
                            _13386.x = _7489.x + (_7489.y * _7489.z);
                            uvec3 _13389 = _13386;
                            _13389.y = _13386.y + (_13386.z * _13386.x);
                            uvec3 _13392 = _13386;
                            _13392.z = _13386.z + (_13386.x * _13386.y);
                            uvec3 _7521 = ((_7181 + uvec3(1u, 0u, 1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7521.x = _7521.x + (_7521.y * _7521.z);
                            _7521.y = _7521.y + (_7521.z * _7521.x);
                            _7521.z = _7521.z + (_7521.x * _7521.y);
                            uvec3 _7548 = _7521 ^ (_7521 >> uvec3(16u));
                            uvec3 _13404;
                            _13404.x = _7548.x + (_7548.y * _7548.z);
                            uvec3 _13407 = _13404;
                            _13407.y = _13404.y + (_13404.z * _13404.x);
                            uvec3 _13410 = _13404;
                            _13410.z = _13404.z + (_13404.x * _13404.y);
                            uvec3 _7580 = ((_7181 + uvec3(0u, 1u, 1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7580.x = _7580.x + (_7580.y * _7580.z);
                            _7580.y = _7580.y + (_7580.z * _7580.x);
                            _7580.z = _7580.z + (_7580.x * _7580.y);
                            uvec3 _7607 = _7580 ^ (_7580 >> uvec3(16u));
                            uvec3 _13422;
                            _13422.x = _7607.x + (_7607.y * _7607.z);
                            uvec3 _13425 = _13422;
                            _13425.y = _13422.y + (_13422.z * _13422.x);
                            uvec3 _13428 = _13422;
                            _13428.z = _13422.z + (_13422.x * _13422.y);
                            uvec3 _7639 = ((_7181 + uvec3(1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7639.x = _7639.x + (_7639.y * _7639.z);
                            _7639.y = _7639.y + (_7639.z * _7639.x);
                            _7639.z = _7639.z + (_7639.x * _7639.y);
                            uvec3 _7666 = _7639 ^ (_7639 >> uvec3(16u));
                            uvec3 _13440;
                            _13440.x = _7666.x + (_7666.y * _7666.z);
                            uvec3 _13443 = _13440;
                            _13443.y = _13440.y + (_13440.z * _13440.x);
                            uvec3 _13446 = _13440;
                            _13446.z = _13440.z + (_13440.x * _13440.y);
                            float _7205 = _7178.x;
                            float _7210 = _7178.y;
                            vec3 _5960 = (_6056 * 14.0) + vec3(3.099999904632568359375, 7.69999980926513671875, 1.2999999523162841796875);
                            vec3 _7689 = floor(_5960);
                            vec3 _7690 = _5960 - _7689;
                            vec3 _7695 = (_7690 * _7690) * (vec3(3.0) - (_7690 * 2.0));
                            uvec3 _7698 = uvec3(ivec3(_7689) + ivec3(4096));
                            uvec3 _7743 = (_7698 * uvec3(1664525u)) + uvec3(1013904223u);
                            _7743.x = _7743.x + (_7743.y * _7743.z);
                            _7743.y = _7743.y + (_7743.z * _7743.x);
                            _7743.z = _7743.z + (_7743.x * _7743.y);
                            uvec3 _7770 = _7743 ^ (_7743 >> uvec3(16u));
                            uvec3 _13458;
                            _13458.x = _7770.x + (_7770.y * _7770.z);
                            uvec3 _13461 = _13458;
                            _13461.y = _13458.y + (_13458.z * _13458.x);
                            uvec3 _13464 = _13458;
                            _13464.z = _13458.z + (_13458.x * _13458.y);
                            uvec3 _7802 = ((_7698 + uvec3(1u, 0u, 0u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7802.x = _7802.x + (_7802.y * _7802.z);
                            _7802.y = _7802.y + (_7802.z * _7802.x);
                            _7802.z = _7802.z + (_7802.x * _7802.y);
                            uvec3 _7829 = _7802 ^ (_7802 >> uvec3(16u));
                            uvec3 _13476;
                            _13476.x = _7829.x + (_7829.y * _7829.z);
                            uvec3 _13479 = _13476;
                            _13479.y = _13476.y + (_13476.z * _13476.x);
                            uvec3 _13482 = _13476;
                            _13482.z = _13476.z + (_13476.x * _13476.y);
                            uvec3 _7861 = ((_7698 + uvec3(0u, 1u, 0u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7861.x = _7861.x + (_7861.y * _7861.z);
                            _7861.y = _7861.y + (_7861.z * _7861.x);
                            _7861.z = _7861.z + (_7861.x * _7861.y);
                            uvec3 _7888 = _7861 ^ (_7861 >> uvec3(16u));
                            uvec3 _13494;
                            _13494.x = _7888.x + (_7888.y * _7888.z);
                            uvec3 _13497 = _13494;
                            _13497.y = _13494.y + (_13494.z * _13494.x);
                            uvec3 _13500 = _13494;
                            _13500.z = _13494.z + (_13494.x * _13494.y);
                            uvec3 _7920 = ((_7698 + uvec3(1u, 1u, 0u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7920.x = _7920.x + (_7920.y * _7920.z);
                            _7920.y = _7920.y + (_7920.z * _7920.x);
                            _7920.z = _7920.z + (_7920.x * _7920.y);
                            uvec3 _7947 = _7920 ^ (_7920 >> uvec3(16u));
                            uvec3 _13512;
                            _13512.x = _7947.x + (_7947.y * _7947.z);
                            uvec3 _13515 = _13512;
                            _13515.y = _13512.y + (_13512.z * _13512.x);
                            uvec3 _13518 = _13512;
                            _13518.z = _13512.z + (_13512.x * _13512.y);
                            uvec3 _7979 = ((_7698 + uvec3(0u, 0u, 1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _7979.x = _7979.x + (_7979.y * _7979.z);
                            _7979.y = _7979.y + (_7979.z * _7979.x);
                            _7979.z = _7979.z + (_7979.x * _7979.y);
                            uvec3 _8006 = _7979 ^ (_7979 >> uvec3(16u));
                            uvec3 _13530;
                            _13530.x = _8006.x + (_8006.y * _8006.z);
                            uvec3 _13533 = _13530;
                            _13533.y = _13530.y + (_13530.z * _13530.x);
                            uvec3 _13536 = _13530;
                            _13536.z = _13530.z + (_13530.x * _13530.y);
                            uvec3 _8038 = ((_7698 + uvec3(1u, 0u, 1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _8038.x = _8038.x + (_8038.y * _8038.z);
                            _8038.y = _8038.y + (_8038.z * _8038.x);
                            _8038.z = _8038.z + (_8038.x * _8038.y);
                            uvec3 _8065 = _8038 ^ (_8038 >> uvec3(16u));
                            uvec3 _13548;
                            _13548.x = _8065.x + (_8065.y * _8065.z);
                            uvec3 _13551 = _13548;
                            _13551.y = _13548.y + (_13548.z * _13548.x);
                            uvec3 _13554 = _13548;
                            _13554.z = _13548.z + (_13548.x * _13548.y);
                            uvec3 _8097 = ((_7698 + uvec3(0u, 1u, 1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _8097.x = _8097.x + (_8097.y * _8097.z);
                            _8097.y = _8097.y + (_8097.z * _8097.x);
                            _8097.z = _8097.z + (_8097.x * _8097.y);
                            uvec3 _8124 = _8097 ^ (_8097 >> uvec3(16u));
                            uvec3 _13566;
                            _13566.x = _8124.x + (_8124.y * _8124.z);
                            uvec3 _13569 = _13566;
                            _13569.y = _13566.y + (_13566.z * _13566.x);
                            uvec3 _13572 = _13566;
                            _13572.z = _13566.z + (_13566.x * _13566.y);
                            uvec3 _8156 = ((_7698 + uvec3(1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                            _8156.x = _8156.x + (_8156.y * _8156.z);
                            _8156.y = _8156.y + (_8156.z * _8156.x);
                            _8156.z = _8156.z + (_8156.x * _8156.y);
                            uvec3 _8183 = _8156 ^ (_8156 >> uvec3(16u));
                            uvec3 _13584;
                            _13584.x = _8183.x + (_8183.y * _8183.z);
                            uvec3 _13587 = _13584;
                            _13587.y = _13584.y + (_13584.z * _13584.x);
                            uvec3 _13590 = _13584;
                            _13590.z = _13584.z + (_13584.x * _13584.y);
                            float _7722 = _7695.x;
                            float _7727 = _7695.y;
                            float _5963 = ((0.550000011920928955078125 * mix(mix(mix((vec3(_13170) * 2.3283064365386962890625e-10).x, (vec3(_13188) * 2.3283064365386962890625e-10).x, _6688), mix((vec3(_13206) * 2.3283064365386962890625e-10).x, (vec3(_13224) * 2.3283064365386962890625e-10).x, _6688), _6693), mix(mix((vec3(_13242) * 2.3283064365386962890625e-10).x, (vec3(_13260) * 2.3283064365386962890625e-10).x, _6688), mix((vec3(_13278) * 2.3283064365386962890625e-10).x, (vec3(_13296) * 2.3283064365386962890625e-10).x, _6688), _6693), _6661.z)) + (0.449999988079071044921875 * mix(mix(mix((vec3(_13314) * 2.3283064365386962890625e-10).x, (vec3(_13332) * 2.3283064365386962890625e-10).x, _7205), mix((vec3(_13350) * 2.3283064365386962890625e-10).x, (vec3(_13368) * 2.3283064365386962890625e-10).x, _7205), _7210), mix(mix((vec3(_13386) * 2.3283064365386962890625e-10).x, (vec3(_13404) * 2.3283064365386962890625e-10).x, _7205), mix((vec3(_13422) * 2.3283064365386962890625e-10).x, (vec3(_13440) * 2.3283064365386962890625e-10).x, _7205), _7210), _7178.z))) * 1.2999999523162841796875;
                            vec3 _5975 = (((_6058 + (_6356 * 0.119999997317790985107421875)) + (vec3(0.85000002384185791015625, 0.89999997615814208984375, 1.0) * (((exp((_5947 * _5947) * (-41.322315216064453125)) * clamp(_5963 - 0.25, 0.0, 1.0)) * (1.0 - (0.60000002384185791015625 * smoothstep(0.3499999940395355224609375, 0.75, mix(mix(mix((vec3(_13458) * 2.3283064365386962890625e-10).x, (vec3(_13476) * 2.3283064365386962890625e-10).x, _7722), mix((vec3(_13494) * 2.3283064365386962890625e-10).x, (vec3(_13512) * 2.3283064365386962890625e-10).x, _7722), _7727), mix(mix((vec3(_13530) * 2.3283064365386962890625e-10).x, (vec3(_13548) * 2.3283064365386962890625e-10).x, _7722), mix((vec3(_13566) * 2.3283064365386962890625e-10).x, (vec3(_13584) * 2.3283064365386962890625e-10).x, _7722), _7727), _7695.z))))) * 0.01200000010430812835693359375))) * light.stars.x) * (_5926 * _5926);
                            _5922 = _5975;
                        }
                        else
                        {
                            _5922 = vec3(0.0);
                        }
                        if (light.moon.w > 0.0)
                        {
                            float _5979 = dot(_557, _810);
                            float _5983 = sqrt(max(2.0 - (2.0 * _5979), 0.0));
                            float _5984 = max(pixelAngle, 0.0005249999812804162502288818359375);
                            float _5988 = 1.0 - smoothstep(0.010499999858438968658447265625 - _5984, 0.010499999858438968658447265625 + _5984, _5983);
                            if (_5988 > 0.0)
                            {
                                float _5992 = clamp(_5983 * 95.23809814453125, 0.0, 1.0);
                                vec3 _6003 = (((_557 - _810) * vec3(95.23809814453125)) * 1.60000002384185791015625) + vec3(11.0, 5.0, 2.0);
                                vec3 _8206 = floor(_6003);
                                vec3 _8207 = _6003 - _8206;
                                vec3 _8212 = (_8207 * _8207) * (vec3(3.0) - (_8207 * 2.0));
                                uvec3 _8215 = uvec3(ivec3(_8206) + ivec3(4096));
                                uvec3 _8260 = (_8215 * uvec3(1664525u)) + uvec3(1013904223u);
                                _8260.x = _8260.x + (_8260.y * _8260.z);
                                _8260.y = _8260.y + (_8260.z * _8260.x);
                                _8260.z = _8260.z + (_8260.x * _8260.y);
                                uvec3 _8287 = _8260 ^ (_8260 >> uvec3(16u));
                                uvec3 _13602;
                                _13602.x = _8287.x + (_8287.y * _8287.z);
                                uvec3 _13605 = _13602;
                                _13605.y = _13602.y + (_13602.z * _13602.x);
                                uvec3 _13608 = _13602;
                                _13608.z = _13602.z + (_13602.x * _13602.y);
                                uvec3 _8319 = ((_8215 + uvec3(1u, 0u, 0u)) * uvec3(1664525u)) + uvec3(1013904223u);
                                _8319.x = _8319.x + (_8319.y * _8319.z);
                                _8319.y = _8319.y + (_8319.z * _8319.x);
                                _8319.z = _8319.z + (_8319.x * _8319.y);
                                uvec3 _8346 = _8319 ^ (_8319 >> uvec3(16u));
                                uvec3 _13620;
                                _13620.x = _8346.x + (_8346.y * _8346.z);
                                uvec3 _13623 = _13620;
                                _13623.y = _13620.y + (_13620.z * _13620.x);
                                uvec3 _13626 = _13620;
                                _13626.z = _13620.z + (_13620.x * _13620.y);
                                uvec3 _8378 = ((_8215 + uvec3(0u, 1u, 0u)) * uvec3(1664525u)) + uvec3(1013904223u);
                                _8378.x = _8378.x + (_8378.y * _8378.z);
                                _8378.y = _8378.y + (_8378.z * _8378.x);
                                _8378.z = _8378.z + (_8378.x * _8378.y);
                                uvec3 _8405 = _8378 ^ (_8378 >> uvec3(16u));
                                uvec3 _13638;
                                _13638.x = _8405.x + (_8405.y * _8405.z);
                                uvec3 _13641 = _13638;
                                _13641.y = _13638.y + (_13638.z * _13638.x);
                                uvec3 _13644 = _13638;
                                _13644.z = _13638.z + (_13638.x * _13638.y);
                                uvec3 _8437 = ((_8215 + uvec3(1u, 1u, 0u)) * uvec3(1664525u)) + uvec3(1013904223u);
                                _8437.x = _8437.x + (_8437.y * _8437.z);
                                _8437.y = _8437.y + (_8437.z * _8437.x);
                                _8437.z = _8437.z + (_8437.x * _8437.y);
                                uvec3 _8464 = _8437 ^ (_8437 >> uvec3(16u));
                                uvec3 _13656;
                                _13656.x = _8464.x + (_8464.y * _8464.z);
                                uvec3 _13659 = _13656;
                                _13659.y = _13656.y + (_13656.z * _13656.x);
                                uvec3 _13662 = _13656;
                                _13662.z = _13656.z + (_13656.x * _13656.y);
                                uvec3 _8496 = ((_8215 + uvec3(0u, 0u, 1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                                _8496.x = _8496.x + (_8496.y * _8496.z);
                                _8496.y = _8496.y + (_8496.z * _8496.x);
                                _8496.z = _8496.z + (_8496.x * _8496.y);
                                uvec3 _8523 = _8496 ^ (_8496 >> uvec3(16u));
                                uvec3 _13674;
                                _13674.x = _8523.x + (_8523.y * _8523.z);
                                uvec3 _13677 = _13674;
                                _13677.y = _13674.y + (_13674.z * _13674.x);
                                uvec3 _13680 = _13674;
                                _13680.z = _13674.z + (_13674.x * _13674.y);
                                uvec3 _8555 = ((_8215 + uvec3(1u, 0u, 1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                                _8555.x = _8555.x + (_8555.y * _8555.z);
                                _8555.y = _8555.y + (_8555.z * _8555.x);
                                _8555.z = _8555.z + (_8555.x * _8555.y);
                                uvec3 _8582 = _8555 ^ (_8555 >> uvec3(16u));
                                uvec3 _13692;
                                _13692.x = _8582.x + (_8582.y * _8582.z);
                                uvec3 _13695 = _13692;
                                _13695.y = _13692.y + (_13692.z * _13692.x);
                                uvec3 _13698 = _13692;
                                _13698.z = _13692.z + (_13692.x * _13692.y);
                                uvec3 _8614 = ((_8215 + uvec3(0u, 1u, 1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                                _8614.x = _8614.x + (_8614.y * _8614.z);
                                _8614.y = _8614.y + (_8614.z * _8614.x);
                                _8614.z = _8614.z + (_8614.x * _8614.y);
                                uvec3 _8641 = _8614 ^ (_8614 >> uvec3(16u));
                                uvec3 _13710;
                                _13710.x = _8641.x + (_8641.y * _8641.z);
                                uvec3 _13713 = _13710;
                                _13713.y = _13710.y + (_13710.z * _13710.x);
                                uvec3 _13716 = _13710;
                                _13716.z = _13710.z + (_13710.x * _13710.y);
                                uvec3 _8673 = ((_8215 + uvec3(1u)) * uvec3(1664525u)) + uvec3(1013904223u);
                                _8673.x = _8673.x + (_8673.y * _8673.z);
                                _8673.y = _8673.y + (_8673.z * _8673.x);
                                _8673.z = _8673.z + (_8673.x * _8673.y);
                                uvec3 _8700 = _8673 ^ (_8673 >> uvec3(16u));
                                uvec3 _13728;
                                _13728.x = _8700.x + (_8700.y * _8700.z);
                                uvec3 _13731 = _13728;
                                _13731.y = _13728.y + (_13728.z * _13728.x);
                                uvec3 _13734 = _13728;
                                _13734.z = _13728.z + (_13728.x * _13728.y);
                                float _8239 = _8212.x;
                                float _8244 = _8212.y;
                                vec3 _6015 = vec3(1.0, 0.980000019073486328125, 0.939999997615814208984375) * ((((light.moon.w * _5988) * (0.62000000476837158203125 + (0.37999999523162841796875 * sqrt(max(1.0 - (_5992 * _5992), 0.0))))) * (1.0 - (0.2800000011920928955078125 * smoothstep(0.449999988079071044921875, 0.800000011920928955078125, mix(mix(mix((vec3(_13602) * 2.3283064365386962890625e-10).x, (vec3(_13620) * 2.3283064365386962890625e-10).x, _8239), mix((vec3(_13638) * 2.3283064365386962890625e-10).x, (vec3(_13656) * 2.3283064365386962890625e-10).x, _8239), _8244), mix(mix((vec3(_13674) * 2.3283064365386962890625e-10).x, (vec3(_13692) * 2.3283064365386962890625e-10).x, _8239), mix((vec3(_13710) * 2.3283064365386962890625e-10).x, (vec3(_13728) * 2.3283064365386962890625e-10).x, _8239), _8244), _8212.z))))) * clamp(1.0 - (light.stars.y * 1.5), 0.0, 1.0));
                                _5922 += _6015;
                            }
                            float _6019 = clamp(_5979, 0.0, 1.0);
                            _5922 += ((vec3(0.819999992847442626953125, 0.87999999523162841796875, 1.0) * light.moon.w) * ((((pow(_6019, 6000.0) * 0.006000000052154064178466796875) * _5926) + (pow(_6019, 180.0) * 0.0003000000142492353916168212890625)) + ((pow(_6019, 18.0) * 0.000600000028498470783233642578125) * light.stars.y)));
                        }
                        lampLight = background + (_5922 * _5838);
                    }
                    else
                    {
                        lampLight = background;
                    }
                    vec3 _8722;
                    {
                        if (gi.fog.z <= 0.0)
                        {
                            _8722 = lampLight;
                        }
                        else
                        {
                            vec4 _8752 = textureLod(_13753, vec3(input_uv, clamp(((log2(max((gi.unproject.w * gi.fog.x) + gi.fog.y, 9.9999999747524270787835121154785e-07)) * gi.fog.z) - 1.0) * gi.fog.w, 0.0, 1.0)), 0.0);
                            _8722 = (lampLight * _8752.w) + _8752.xyz;
                        }
                    }
                    entryPointParam_fragmentMain = vec4(mix(_8722, sunLight, vec3(_425)), 1.0);
                }
            }
        }
        else
        {
            vec4 _90;
            if ((int((_5621.w * 255.0) + 0.5) & 15) == 0)
            {
                if (debugView != 0)
                {
                    lampGridded = debugView != 11;
                }
                else
                {
                    lampGridded = false;
                }
                if (lampGridded)
                {
                    if (debugView == 1)
                    {
                        _90 = vec4(albedo, 1.0);
                    }
                    else
                    {
                        _90 = vec4(0.0, 0.0, 0.0, 1.0);
                    }
                    entryPointParam_fragmentMain = _90;
                }
                else
                {
                    float _8772 = (_5621.y * 255.0) - 128.0;
                    float _8767;
                    if (_8772 > 0.5)
                    {
                        _8767 = _8772 * 0.0625;
                    }
                    else
                    {
                        _8767 = 1.0;
                    }
                    vec3 _1614 = albedo * _8767;
                    float _8794 = ((2.0 * gi.unproject.z) * gi.unproject.w) / ((gi.unproject.w + gi.unproject.z) - ((1.0 - (2.0 * depth)) * (gi.unproject.w - gi.unproject.z)));
                    vec2 _8797 = (input_uv * 2.0) - vec2(1.0);
                    vec3 _8811;
                    {
                        if (gi.fog.z <= 0.0)
                        {
                            _8811 = _1614;
                        }
                        else
                        {
                            vec4 _8841 = textureLod(_13753, vec3(input_uv, clamp(((log2(max((vec3(_13736, _13736, _8794).z * gi.fog.x) + gi.fog.y, 9.9999999747524270787835121154785e-07)) * gi.fog.z) - 1.0) * gi.fog.w, 0.0, 1.0)), 0.0);
                            _8811 = (_1614 * _8841.w) + _8841.xyz;
                        }
                    }
                    entryPointParam_fragmentMain = vec4(_8811, 1.0);
                }
            }
            else
            {
                vec3 normal = normalize((texelFetch(_13763, ivec3(ivec2(gl_FragCoord.xy), 0).xy, 0).xyz * 2.0) - vec3(1.0));
                int model = int(light.sunDirectionModel.w + 0.5);
                if (debugView == 1)
                {
                    entryPointParam_fragmentMain = vec4(albedo, 1.0);
                }
                else
                {
                    if (debugView == 2)
                    {
                        entryPointParam_fragmentMain = vec4((normal * 0.5) + vec3(0.5), 1.0);
                    }
                    else
                    {
                        if (debugView == 3)
                        {
                            float _8880 = ((2.0 * gi.unproject.z) * gi.unproject.w) / ((gi.unproject.w + gi.unproject.z) - ((1.0 - (2.0 * depth)) * (gi.unproject.w - gi.unproject.z)));
                            vec2 _8883 = (input_uv * 2.0) - vec2(1.0);
                            float _1712 = pow(clamp(vec3(_13736, _13736, _8880).z * 0.0024999999441206455230712890625, 0.0, 1.0), 0.4544999897480010986328125);
                            entryPointParam_fragmentMain = vec4(_1712, _1712, _1712, 1.0);
                        }
                        else
                        {
                            if (debugView == 4)
                            {
                                entryPointParam_fragmentMain = vec4(_5621.zxw, 1.0);
                            }
                            else
                            {
                                if (debugView == 6)
                                {
                                    entryPointParam_fragmentMain = vec4(baked, 1.0);
                                }
                                else
                                {
                                    if (debugView == 7)
                                    {
                                        entryPointParam_fragmentMain = vec4(textureLod(_13765, input_uv, 0.0).xxx, 1.0);
                                    }
                                    else
                                    {
                                        if (debugView == 8)
                                        {
                                            entryPointParam_fragmentMain = vec4(textureLod(_13767, input_uv, 0.0).xyz * gi.giParams.x, 1.0);
                                        }
                                        else
                                        {
                                            if (debugView == 18)
                                            {
                                                entryPointParam_fragmentMain = vec4(_5621.zzz, 1.0);
                                            }
                                            else
                                            {
                                                if (debugView == 19)
                                                {
                                                    entryPointParam_fragmentMain = vec4(_5621.xxx, 1.0);
                                                }
                                                else
                                                {
                                                    if (debugView == 20)
                                                    {
                                                        entryPointParam_fragmentMain = vec4(_5612.www, 1.0);
                                                    }
                                                    else
                                                    {
                                                        if (debugView == 11)
                                                        {
                                                            albedo = vec3(0.180000007152557373046875);
                                                        }
                                                        if (debugView == 28)
                                                        {
                                                            float _8897;
                                                            {
                                                                if (gi.flashlight.control.x <= 0.5)
                                                                {
                                                                    _8897 = 1.0;
                                                                }
                                                                else
                                                                {
                                                                    _8897 = clamp(texelFetch(_13770, ivec3(ivec2(gl_FragCoord.xy), 0).xy, 0).x, 0.0, 1.0);
                                                                }
                                                            }
                                                            entryPointParam_fragmentMain = vec4(_8897, _8897, _8897, 1.0);
                                                        }
                                                        else
                                                        {
                                                            float _8934 = ((2.0 * gi.unproject.z) * gi.unproject.w) / ((gi.unproject.w + gi.unproject.z) - ((1.0 - (2.0 * depth)) * (gi.unproject.w - gi.unproject.z)));
                                                            vec2 _8937 = (input_uv * 2.0) - vec2(1.0);
                                                            vec3 _8949 = vec3((_8937.x * _8934) / gi.unproject.x, ((-_8937.y) * _8934) / gi.unproject.y, _8934);
                                                            vec3 flashDir;
                                                            vec3 _8951;
                                                            {
                                                                flashDir = vec3(0.0, 0.0, 1.0);
                                                                if (!(gi.flashlight.position.w > 0.0))
                                                                {
                                                                    _8951 = vec3(0.0);
                                                                }
                                                                else
                                                                {
                                                                    vec3 _8952;
                                                                    vec3 _8989;
                                                                    {
                                                                        vec3 _8999 = _8949 - gi.flashlight.position.xyz;
                                                                        float _9000 = dot(_8999, _8999);
                                                                        float _9002 = sqrt(max(_9000, 9.9999999392252902907785028219223e-09));
                                                                        float _8953 = _9002;
                                                                        _8952 = (-_8999) / vec3(_9002);
                                                                        bool _8990;
                                                                        if (gi.flashlight.position.w <= 0.0)
                                                                        {
                                                                            _8990 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _8990 = _8953 >= gi.flashlight.position.w;
                                                                        }
                                                                        if (_8990)
                                                                        {
                                                                            _8989 = vec3(0.0);
                                                                        }
                                                                        else
                                                                        {
                                                                            vec3 _9017 = -_8952;
                                                                            float _9056;
                                                                            {
                                                                                float _9066 = dot(gi.flashlight.axis.xyz, _9017);
                                                                                bool _9057;
                                                                                if (_9066 <= gi.flashlight.axis.w)
                                                                                {
                                                                                    _9057 = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _9057 = _9066 <= 0.0;
                                                                                }
                                                                                if (_9057)
                                                                                {
                                                                                    _9056 = 0.0;
                                                                                }
                                                                                else
                                                                                {
                                                                                    float _9085 = (sqrt(max(1.0 - (_9066 * _9066), 0.0)) / _9066) / max(gi.flashlight.cone.x, 9.9999997473787516355514526367188e-05);
                                                                                    float _9097 = _9085 - 0.86000001430511474609375;
                                                                                    vec3 _9109 = cross(gi.flashlight.up.xyz, gi.flashlight.axis.xyz);
                                                                                    float _9110 = length(_9109);
                                                                                    vec3 _9058;
                                                                                    if (_9110 > 9.9999997473787516355514526367188e-05)
                                                                                    {
                                                                                        _9058 = _9109 / vec3(_9110);
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _9058 = vec3(1.0, 0.0, 0.0);
                                                                                    }
                                                                                    float _9123 = atan(dot(_9017, cross(gi.flashlight.axis.xyz, _9058)), dot(_9017, _9058) + 9.9999999747524270787835121154785e-07);
                                                                                    _9056 = max(((((0.550000011920928955078125 * exp(-((_9085 * _9085) / max((0.300000011920928955078125 * gi.flashlight.cone.y) * gi.flashlight.cone.y, 9.9999997473787516355514526367188e-05)))) + (0.449999988079071044921875 * (1.0 - smoothstep(gi.flashlight.cone.y, 1.0, _9085)))) + (0.070000000298023223876953125 * exp((_9097 * _9097) * (-249.9999847412109375)))) * ((1.0 + ((0.04500000178813934326171875 * _9085) * sin((5.0 * _9123) + 1.2999999523162841796875))) + (0.02999999932944774627685546875 * sin((33.0 * _9085) + (2.0 * _9123))))) + ((1.0 - smoothstep(1.0, gi.flashlight.cone.z, _9085)) * gi.flashlight.cone.w), 0.0);
                                                                                }
                                                                            }
                                                                            if (_9056 <= 0.0)
                                                                            {
                                                                                _8989 = vec3(0.0);
                                                                            }
                                                                            else
                                                                            {
                                                                                float _9023 = _9000 / (gi.flashlight.position.w * gi.flashlight.position.w);
                                                                                float _9026 = clamp(1.0 - (_9023 * _9023), 0.0, 1.0);
                                                                                if (true)
                                                                                {
                                                                                    _8990 = _8953 > 5.0;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _8990 = false;
                                                                                }
                                                                                float _8991;
                                                                                if (_8990)
                                                                                {
                                                                                    _8991 = pow(_8953 * 0.20000000298023223876953125, gi.flashlight.control.z);
                                                                                }
                                                                                else
                                                                                {
                                                                                    _8991 = 1.0;
                                                                                }
                                                                                _8989 = gi.flashlight.colour.xyz * (((_9056 * (_9026 * _9026)) * _8991) / max(_9000, gi.flashlight.colour.w));
                                                                            }
                                                                        }
                                                                    }
                                                                    if (all(lessThanEqual(_8989, vec3(0.0))))
                                                                    {
                                                                        _8951 = vec3(0.0);
                                                                    }
                                                                    else
                                                                    {
                                                                        vec3 _8972 = normalize((vec4(_8952, 0.0) * spvWorkaroundRowMajor(gi.viewToWorld)).xyz);
                                                                        flashDir = _8972;
                                                                        if (dot(normal, _8972) <= 0.0)
                                                                        {
                                                                            _8951 = vec3(0.0);
                                                                        }
                                                                        else
                                                                        {
                                                                            float _9147;
                                                                            {
                                                                                if (gi.flashlight.control.x <= 0.5)
                                                                                {
                                                                                    _9147 = 1.0;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _9147 = clamp(texelFetch(_13770, ivec3(ivec2(gl_FragCoord.xy), 0).xy, 0).x, 0.0, 1.0);
                                                                                }
                                                                            }
                                                                            _8951 = _8989 * _9147;
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                            vec3 flashDiffuse = (albedo * _8951) * max(dot(normal, flashDir), 0.0);
                                                            if (model == 0)
                                                            {
                                                                vec3 _2084 = (albedo * baked) + flashDiffuse;
                                                                vec3 _9170;
                                                                {
                                                                    if (gi.fog.z <= 0.0)
                                                                    {
                                                                        _9170 = _2084;
                                                                    }
                                                                    else
                                                                    {
                                                                        vec4 _9200 = textureLod(_13753, vec3(input_uv, clamp(((log2(max((_8949.z * gi.fog.x) + gi.fog.y, 9.9999999747524270787835121154785e-07)) * gi.fog.z) - 1.0) * gi.fog.w, 0.0, 1.0)), 0.0);
                                                                        _9170 = (_2084 * _9200.w) + _9200.xyz;
                                                                    }
                                                                }
                                                                entryPointParam_fragmentMain = vec4(_9170, 1.0);
                                                            }
                                                            else
                                                            {
                                                                vec3 _2097 = normalize((vec4(light.sunDirectionModel.xyz, 0.0) * spvWorkaroundRowMajor(gi.viewToWorld)).xyz);
                                                                float _2100 = max(dot(normal, _2097), 0.0);
                                                                if (_2100 > 0.0)
                                                                {
                                                                    lampGridded = true;
                                                                }
                                                                else
                                                                {
                                                                    lampGridded = debugView == 12;
                                                                }
                                                                if (lampGridded)
                                                                {
                                                                    if (gi.probeMisc.w > 0.5)
                                                                    {
                                                                        visibility = textureLod(_13777, input_uv, 0.0).x;
                                                                    }
                                                                    else
                                                                    {
                                                                        _9208 = false;
                                                                        float _9209;
                                                                        {
                                                                            int _9222 = int(gi.shadowConfig.x + 0.5);
                                                                            if (_9222 <= 0)
                                                                            {
                                                                                _9208 = true;
                                                                                _9209 = 1.0;
                                                                            }
                                                                            else
                                                                            {
                                                                                float _9229 = max(gi.probeSky.w, 1.0);
                                                                                vec3 _9234 = (vec4(_8949, 1.0) * spvWorkaroundRowMajor(gi.viewToWorld)).xyz;
                                                                                int _9210 = 0;
                                                                                bool _9211;
                                                                                float _9212;
                                                                                float _9213;
                                                                                bool _9214;
                                                                                float _9310;
                                                                                float _9318;
                                                                                float _9493;
                                                                                int _9494;
                                                                                float _9495;
                                                                                float _9608;
                                                                                float _9616;
                                                                                float _9791;
                                                                                int _9792;
                                                                                float _9793;
                                                                                for (;;)
                                                                                {
                                                                                    bool _9237_ladder_break = false;
                                                                                    {
                                                                                        if (!(_9210 < 4))
                                                                                        {
                                                                                            _9237_ladder_break = true;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            if (_9210 >= _9222)
                                                                                            {
                                                                                                _9237_ladder_break = true;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                float _9247 = _8949.z;
                                                                                                if (!(_9247 > gi.cascadeSplits[_9210]))
                                                                                                {
                                                                                                    {
                                                                                                        float _9344 = (2.0 * gi.shadowConfig.w) / max(length(vec3(spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9210])[0].x, spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9210])[1].x, spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9210])[2].x)), 9.9999999747524270787835121154785e-07);
                                                                                                        float _9346 = 2.0 / max(length(vec3(spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9210])[0].z, spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9210])[1].z, spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9210])[2].z)), 9.9999999747524270787835121154785e-07);
                                                                                                        vec4 _9353 = vec4(_9234 + (normal * (gi.shadowConfig.z * _9344)), 1.0) * spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9210]);
                                                                                                        vec3 _9357 = _9353.xyz / vec3(_9353.w);
                                                                                                        bool _9361 = all(lessThanEqual(abs(_9357.xy), vec2(1.0)));
                                                                                                        _9211 = _9361;
                                                                                                        if (!_9361)
                                                                                                        {
                                                                                                            _9310 = 1.0;
                                                                                                        }
                                                                                                        else
                                                                                                        {
                                                                                                            float _9372 = ((_9357.z * 0.5) + 0.5) - ((gi.shadowConfig.y * _9344) / _9346);
                                                                                                            float _9391 = max((_9229 * ((2.0 * gi.shadowConfig.w) / max(length(vec3(spvWorkaroundRowMajor(gi.cascadeMatrix.data[0])[0].x, spvWorkaroundRowMajor(gi.cascadeMatrix.data[0])[1].x, spvWorkaroundRowMajor(gi.cascadeMatrix.data[0])[2].x)), 9.9999999747524270787835121154785e-07))) / max(_9344, 9.9999999747524270787835121154785e-07), 1.0);
                                                                                                            vec2 _9398 = vec2((_9357.x * 0.5) + 0.5, 0.5 - (_9357.y * 0.5));
                                                                                                            uvec3 _9400 = uvec3(textureSize(_13773, 0));
                                                                                                            ivec2 _9408 = ivec2(int(_9400.x), int(_9400.y));
                                                                                                            float _9411 = clamp(0.140399992465972900390625 / max(_9344, 9.9999999747524270787835121154785e-07), 2.0, 16.0);
                                                                                                            float _9414 = fract(52.98291778564453125 * fract(dot(gl_FragCoord.xy + vec2(7.0, 3.0), vec2(0.067110560834407806396484375, 0.005837149918079376220703125)))) * 6.283185482025146484375;
                                                                                                            float _9415 = sin(_9414);
                                                                                                            float _9416 = cos(_9414);
                                                                                                            int _9314 = 0;
                                                                                                            float _9315 = 0.0;
                                                                                                            float _9316 = 0.0;
                                                                                                            for (;;)
                                                                                                            {
                                                                                                                if (!(_9314 < 12))
                                                                                                                {
                                                                                                                    break;
                                                                                                                }
                                                                                                                vec4 _9451 = texelFetch(_13773, ivec4(clamp(ivec2((_9398 + (vec2((_2372_frag[_9314].x * _9416) - (_2372_frag[_9314].y * _9415), (_2372_frag[_9314].x * _9415) + (_2372_frag[_9314].y * _9416)) * (_9411 * gi.shadowConfig.w))) * vec2(_9408)), ivec2(0), _9408 - ivec2(1)), _9210, 0).xyz, 0);
                                                                                                                float _9452 = _9451.x;
                                                                                                                if (_9452 < _9372)
                                                                                                                {
                                                                                                                    _9315 += (_9372 - _9452);
                                                                                                                    _9316 += 1.0;
                                                                                                                }
                                                                                                                _9314++;
                                                                                                                continue;
                                                                                                            }
                                                                                                            if (_9316 > 0.0)
                                                                                                            {
                                                                                                                _9318 = clamp(max(_9391, (((_9315 / _9316) * _9346) * 0.0046799997799098491668701171875) / max(_9344, 9.9999999747524270787835121154785e-07)), 1.0, 24.0);
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                _9318 = _9391;
                                                                                                            }
                                                                                                            vec3 _9481 = vec3(_9398, _9372);
                                                                                                            {
                                                                                                                if (_9318 <= 1.5)
                                                                                                                {
                                                                                                                    _9494 = -1;
                                                                                                                    _9495 = 0.0;
                                                                                                                    for (;;)
                                                                                                                    {
                                                                                                                        if (!(_9494 <= 1))
                                                                                                                        {
                                                                                                                            break;
                                                                                                                        }
                                                                                                                        int _9496 = -1;
                                                                                                                        for (;;)
                                                                                                                        {
                                                                                                                            if (!(_9496 <= 1))
                                                                                                                            {
                                                                                                                                break;
                                                                                                                            }
                                                                                                                            int _9523 = _9496;
                                                                                                                            _9496++;
                                                                                                                            _9495 += spvShadowLod0(_13775, vec4(vec3(_9481.xy + (vec2(float(_9523), float(_9494)) * gi.shadowConfig.w), float(_9210)), _9372));
                                                                                                                            continue;
                                                                                                                        }
                                                                                                                        _9494++;
                                                                                                                        continue;
                                                                                                                    }
                                                                                                                    _9493 = _9495 * 0.111111111938953399658203125;
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    float _9555 = fract(52.98291778564453125 * fract(dot(gl_FragCoord.xy, vec2(0.067110560834407806396484375, 0.005837149918079376220703125)))) * 6.283185482025146484375;
                                                                                                                    float _9556 = sin(_9555);
                                                                                                                    float _9557 = cos(_9555);
                                                                                                                    float _9558 = _9318 * gi.shadowConfig.w;
                                                                                                                    _9494 = 0;
                                                                                                                    _9495 = 0.0;
                                                                                                                    for (;;)
                                                                                                                    {
                                                                                                                        if (!(_9494 < 12))
                                                                                                                        {
                                                                                                                            break;
                                                                                                                        }
                                                                                                                        int _9567 = _9494;
                                                                                                                        _9494++;
                                                                                                                        _9495 += spvShadowLod0(_13775, vec4(vec3(_9481.xy + (vec2((_2372_frag[_9567].x * _9557) - (_2372_frag[_9567].y * _9556), (_2372_frag[_9567].x * _9556) + (_2372_frag[_9567].y * _9557)) * _9558), float(_9210)), _9372));
                                                                                                                        continue;
                                                                                                                    }
                                                                                                                    _9493 = _9495 * 0.083333335816860198974609375;
                                                                                                                }
                                                                                                            }
                                                                                                            _9310 = _9493;
                                                                                                        }
                                                                                                    }
                                                                                                    if (!(!_9211))
                                                                                                    {
                                                                                                        if (_9210 == 0)
                                                                                                        {
                                                                                                            _9212 = 0.0;
                                                                                                        }
                                                                                                        else
                                                                                                        {
                                                                                                            _9212 = gi.cascadeSplits[_9210 - 1];
                                                                                                        }
                                                                                                        float _9271 = _9212;
                                                                                                        float _9274 = max((gi.cascadeSplits[_9210] - _9271) * 0.100000001490116119384765625, 0.001000000047497451305389404296875);
                                                                                                        float _9279 = clamp((_9247 - (gi.cascadeSplits[_9210] - _9274)) / _9274, 0.0, 1.0);
                                                                                                        if (_9279 > 0.0)
                                                                                                        {
                                                                                                            int _9284 = _9210 + 1;
                                                                                                            if (_9284 < _9222)
                                                                                                            {
                                                                                                                {
                                                                                                                    float _9642 = (2.0 * gi.shadowConfig.w) / max(length(vec3(spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9284])[0].x, spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9284])[1].x, spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9284])[2].x)), 9.9999999747524270787835121154785e-07);
                                                                                                                    float _9644 = 2.0 / max(length(vec3(spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9284])[0].z, spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9284])[1].z, spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9284])[2].z)), 9.9999999747524270787835121154785e-07);
                                                                                                                    vec4 _9651 = vec4(_9234 + (normal * (gi.shadowConfig.z * _9642)), 1.0) * spvWorkaroundRowMajor(gi.cascadeMatrix.data[_9284]);
                                                                                                                    vec3 _9655 = _9651.xyz / vec3(_9651.w);
                                                                                                                    bool _9659 = all(lessThanEqual(abs(_9655.xy), vec2(1.0)));
                                                                                                                    _9214 = _9659;
                                                                                                                    if (!_9659)
                                                                                                                    {
                                                                                                                        _9608 = 1.0;
                                                                                                                    }
                                                                                                                    else
                                                                                                                    {
                                                                                                                        float _9670 = ((_9655.z * 0.5) + 0.5) - ((gi.shadowConfig.y * _9642) / _9644);
                                                                                                                        float _9689 = max((_9229 * ((2.0 * gi.shadowConfig.w) / max(length(vec3(spvWorkaroundRowMajor(gi.cascadeMatrix.data[0])[0].x, spvWorkaroundRowMajor(gi.cascadeMatrix.data[0])[1].x, spvWorkaroundRowMajor(gi.cascadeMatrix.data[0])[2].x)), 9.9999999747524270787835121154785e-07))) / max(_9642, 9.9999999747524270787835121154785e-07), 1.0);
                                                                                                                        vec2 _9696 = vec2((_9655.x * 0.5) + 0.5, 0.5 - (_9655.y * 0.5));
                                                                                                                        uvec3 _9698 = uvec3(textureSize(_13773, 0));
                                                                                                                        ivec2 _9706 = ivec2(int(_9698.x), int(_9698.y));
                                                                                                                        float _9709 = clamp(0.140399992465972900390625 / max(_9642, 9.9999999747524270787835121154785e-07), 2.0, 16.0);
                                                                                                                        float _9712 = fract(52.98291778564453125 * fract(dot(gl_FragCoord.xy + vec2(7.0, 3.0), vec2(0.067110560834407806396484375, 0.005837149918079376220703125)))) * 6.283185482025146484375;
                                                                                                                        float _9713 = sin(_9712);
                                                                                                                        float _9714 = cos(_9712);
                                                                                                                        int _9612 = 0;
                                                                                                                        float _9613 = 0.0;
                                                                                                                        float _9614 = 0.0;
                                                                                                                        for (;;)
                                                                                                                        {
                                                                                                                            if (!(_9612 < 12))
                                                                                                                            {
                                                                                                                                break;
                                                                                                                            }
                                                                                                                            vec4 _9749 = texelFetch(_13773, ivec4(clamp(ivec2((_9696 + (vec2((_2372_frag[_9612].x * _9714) - (_2372_frag[_9612].y * _9713), (_2372_frag[_9612].x * _9713) + (_2372_frag[_9612].y * _9714)) * (_9709 * gi.shadowConfig.w))) * vec2(_9706)), ivec2(0), _9706 - ivec2(1)), _9284, 0).xyz, 0);
                                                                                                                            float _9750 = _9749.x;
                                                                                                                            if (_9750 < _9670)
                                                                                                                            {
                                                                                                                                _9613 += (_9670 - _9750);
                                                                                                                                _9614 += 1.0;
                                                                                                                            }
                                                                                                                            _9612++;
                                                                                                                            continue;
                                                                                                                        }
                                                                                                                        if (_9614 > 0.0)
                                                                                                                        {
                                                                                                                            _9616 = clamp(max(_9689, (((_9613 / _9614) * _9644) * 0.0046799997799098491668701171875) / max(_9642, 9.9999999747524270787835121154785e-07)), 1.0, 24.0);
                                                                                                                        }
                                                                                                                        else
                                                                                                                        {
                                                                                                                            _9616 = _9689;
                                                                                                                        }
                                                                                                                        vec3 _9779 = vec3(_9696, _9670);
                                                                                                                        {
                                                                                                                            if (_9616 <= 1.5)
                                                                                                                            {
                                                                                                                                _9792 = -1;
                                                                                                                                _9793 = 0.0;
                                                                                                                                for (;;)
                                                                                                                                {
                                                                                                                                    if (!(_9792 <= 1))
                                                                                                                                    {
                                                                                                                                        break;
                                                                                                                                    }
                                                                                                                                    int _9794 = -1;
                                                                                                                                    for (;;)
                                                                                                                                    {
                                                                                                                                        if (!(_9794 <= 1))
                                                                                                                                        {
                                                                                                                                            break;
                                                                                                                                        }
                                                                                                                                        int _9821 = _9794;
                                                                                                                                        _9794++;
                                                                                                                                        _9793 += spvShadowLod0(_13775, vec4(vec3(_9779.xy + (vec2(float(_9821), float(_9792)) * gi.shadowConfig.w), float(_9284)), _9670));
                                                                                                                                        continue;
                                                                                                                                    }
                                                                                                                                    _9792++;
                                                                                                                                    continue;
                                                                                                                                }
                                                                                                                                _9791 = _9793 * 0.111111111938953399658203125;
                                                                                                                            }
                                                                                                                            else
                                                                                                                            {
                                                                                                                                float _9853 = fract(52.98291778564453125 * fract(dot(gl_FragCoord.xy, vec2(0.067110560834407806396484375, 0.005837149918079376220703125)))) * 6.283185482025146484375;
                                                                                                                                float _9854 = sin(_9853);
                                                                                                                                float _9855 = cos(_9853);
                                                                                                                                float _9856 = _9616 * gi.shadowConfig.w;
                                                                                                                                _9792 = 0;
                                                                                                                                _9793 = 0.0;
                                                                                                                                for (;;)
                                                                                                                                {
                                                                                                                                    if (!(_9792 < 12))
                                                                                                                                    {
                                                                                                                                        break;
                                                                                                                                    }
                                                                                                                                    int _9865 = _9792;
                                                                                                                                    _9792++;
                                                                                                                                    _9793 += spvShadowLod0(_13775, vec4(vec3(_9779.xy + (vec2((_2372_frag[_9865].x * _9855) - (_2372_frag[_9865].y * _9854), (_2372_frag[_9865].x * _9854) + (_2372_frag[_9865].y * _9855)) * _9856), float(_9284)), _9670));
                                                                                                                                    continue;
                                                                                                                                }
                                                                                                                                _9791 = _9793 * 0.083333335816860198974609375;
                                                                                                                            }
                                                                                                                        }
                                                                                                                        _9608 = _9791;
                                                                                                                    }
                                                                                                                }
                                                                                                                if (_9214)
                                                                                                                {
                                                                                                                    _9212 = _9608;
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    _9212 = 1.0;
                                                                                                                }
                                                                                                                _9213 = _9212;
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                _9213 = 1.0;
                                                                                                            }
                                                                                                            _9213 = mix(_9310, _9213, _9279);
                                                                                                        }
                                                                                                        else
                                                                                                        {
                                                                                                            _9213 = _9310;
                                                                                                        }
                                                                                                        _9208 = true;
                                                                                                        _9209 = _9213;
                                                                                                        _9237_ladder_break = true;
                                                                                                    }
                                                                                                }
                                                                                            }
                                                                                        }
                                                                                    }
                                                                                    if (_9237_ladder_break)
                                                                                    {
                                                                                        break;
                                                                                    }
                                                                                    _9210++;
                                                                                    continue;
                                                                                }
                                                                                if (!(_9208))
                                                                                {
                                                                                    _9208 = true;
                                                                                    _9209 = 1.0;
                                                                                }
                                                                            }
                                                                        }
                                                                        if (_9209 > 0.0)
                                                                        {
                                                                            lampGridded = gi.shadowConfig.x > 0.5;
                                                                        }
                                                                        else
                                                                        {
                                                                            lampGridded = false;
                                                                        }
                                                                        if (lampGridded)
                                                                        {
                                                                            vec4 _2692 = spvWorkaroundRowMajor(gi.viewToWorld) * vec4(_2097, 0.0);
                                                                            _9905 = false;
                                                                            float _9906;
                                                                            {
                                                                                bool _9907;
                                                                                if (gi.camera.w <= 0.0)
                                                                                {
                                                                                    _9907 = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _9907 = _2692.z <= (-0.999000012874603271484375);
                                                                                }
                                                                                if (_9907)
                                                                                {
                                                                                    _9905 = true;
                                                                                    _9906 = 1.0;
                                                                                }
                                                                                else
                                                                                {
                                                                                    uvec2 _9926 = uvec2(textureSize(_13751, 0));
                                                                                    vec2 _9933 = vec2(float(_9926.x), float(_9926.y));
                                                                                    vec3 _9935 = _2692.xyz * (gi.camera.w * 0.083333335816860198974609375);
                                                                                    float _9940 = _8949.z;
                                                                                    float _9942 = max(0.300000011920928955078125, 0.0199999995529651641845703125 * _9940);
                                                                                    float _9944 = 0.00999999977648258209228515625 + (0.00200000009499490261077880859375 * _9940);
                                                                                    int _9910 = 0;
                                                                                    vec3 _9911 = _8949 + (_9935 * fract(52.98291778564453125 * fract(dot(gl_FragCoord.xy + vec2(17.0, 31.0), vec2(0.067110560834407806396484375, 0.005837149918079376220703125)))));
                                                                                    bool _9912;
                                                                                    for (;;)
                                                                                    {
                                                                                        vec3 _9954;
                                                                                        bool _9947_ladder_break = false;
                                                                                        {
                                                                                            if (!(_9910 < 12))
                                                                                            {
                                                                                                _9947_ladder_break = true;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                _9954 = _9911 + _9935;
                                                                                                if (_9954.z <= gi.unproject.z)
                                                                                                {
                                                                                                    _9947_ladder_break = true;
                                                                                                }
                                                                                                else
                                                                                                {
                                                                                                    vec2 _9976 = vec2(0.5 + (((0.5 * _9954.x) * gi.unproject.x) / _9954.z), 0.5 - (((0.5 * _9954.y) * gi.unproject.y) / _9954.z));
                                                                                                    if (any(lessThan(_9976, vec2(0.0))))
                                                                                                    {
                                                                                                        _9907 = true;
                                                                                                    }
                                                                                                    else
                                                                                                    {
                                                                                                        _9907 = any(greaterThan(_9976, vec2(1.0)));
                                                                                                    }
                                                                                                    if (_9907)
                                                                                                    {
                                                                                                        _9947_ladder_break = true;
                                                                                                    }
                                                                                                    else
                                                                                                    {
                                                                                                        vec4 _9992 = texelFetch(_13751, ivec3(ivec2(_9976 * _9933), 0).xy, 0);
                                                                                                        float _9993 = _9992.x;
                                                                                                        if (!(_9993 <= 0.0))
                                                                                                        {
                                                                                                            float _9998 = _9954.z - (((2.0 * gi.unproject.z) * gi.unproject.w) / ((gi.unproject.w + gi.unproject.z) - ((1.0 - (2.0 * _9993)) * (gi.unproject.w - gi.unproject.z))));
                                                                                                            if (_9998 > _9944)
                                                                                                            {
                                                                                                                _9912 = _9998 < _9942;
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                _9912 = false;
                                                                                                            }
                                                                                                            if (_9912)
                                                                                                            {
                                                                                                                _9905 = true;
                                                                                                                _9906 = float(_9910) * 0.083333335816860198974609375;
                                                                                                                _9947_ladder_break = true;
                                                                                                            }
                                                                                                        }
                                                                                                    }
                                                                                                }
                                                                                            }
                                                                                        }
                                                                                        if (_9947_ladder_break)
                                                                                        {
                                                                                            break;
                                                                                        }
                                                                                        _9910++;
                                                                                        _9911 = _9954;
                                                                                        continue;
                                                                                    }
                                                                                    if (!(_9905))
                                                                                    {
                                                                                        _9905 = true;
                                                                                        _9906 = 1.0;
                                                                                    }
                                                                                }
                                                                            }
                                                                            visibility = _9209 * _9906;
                                                                        }
                                                                        else
                                                                        {
                                                                            visibility = _9209;
                                                                        }
                                                                    }
                                                                }
                                                                else
                                                                {
                                                                    visibility = 1.0;
                                                                }
                                                                if (debugView == 12)
                                                                {
                                                                    entryPointParam_fragmentMain = vec4(visibility, visibility, visibility, 1.0);
                                                                }
                                                                else
                                                                {
                                                                    if (model == 1)
                                                                    {
                                                                        if (light.sky.z > 0.5)
                                                                        {
                                                                            sunLight = vec3(0.0);
                                                                        }
                                                                        else
                                                                        {
                                                                            sunLight = (light.sunColourScale.xyz * _2100) * visibility;
                                                                        }
                                                                        vec3 _2915 = ((albedo * (baked + sunLight)) * light.sunColourScale.w) + flashDiffuse;
                                                                        vec3 _10043;
                                                                        {
                                                                            if (gi.fog.z <= 0.0)
                                                                            {
                                                                                _10043 = _2915;
                                                                            }
                                                                            else
                                                                            {
                                                                                vec4 _10073 = textureLod(_13753, vec3(input_uv, clamp(((log2(max((_8949.z * gi.fog.x) + gi.fog.y, 9.9999999747524270787835121154785e-07)) * gi.fog.z) - 1.0) * gi.fog.w, 0.0, 1.0)), 0.0);
                                                                                _10043 = (_2915 * _10073.w) + _10073.xyz;
                                                                            }
                                                                        }
                                                                        entryPointParam_fragmentMain = vec4(_10043, 1.0);
                                                                    }
                                                                    else
                                                                    {
                                                                        float metallic = _5621.x;
                                                                        float specularLevel_1 = _5621.y;
                                                                        float roughness = _5621.z;
                                                                        vec3 _2930 = normalize(-(vec4(_8949, 0.0) * spvWorkaroundRowMajor(gi.viewToWorld)).xyz);
                                                                        vec3 worldRayToPixel = -_2930;
                                                                        float waterMask = 0.0;
                                                                        float waterReflectance = 0.0;
                                                                        if (gi.wetness0.x > 0.0)
                                                                        {
                                                                            lampGridded = !((int((_5621.w * 255.0) + 0.5) & 16) != 0);
                                                                        }
                                                                        else
                                                                        {
                                                                            lampGridded = false;
                                                                        }
                                                                        float specularLevel;
                                                                        if (lampGridded)
                                                                        {
                                                                            vec3 _2963 = normal;
                                                                            vec4 _2984 = vec4(_8949, 1.0) * spvWorkaroundRowMajor(gi.viewToWorld);
                                                                            {
                                                                                waterMask = 0.0;
                                                                                waterReflectance = 0.0;
                                                                                float _10160 = max(gi.wetness0.x, 0.0);
                                                                                bool _10098;
                                                                                if (_10160 <= 0.0)
                                                                                {
                                                                                    _10098 = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    bool _10182;
                                                                                    {
                                                                                        ivec2 _10256 = ivec2(floor(((_2984.xy + gi.camera.xy) - gi.shelterMap0.xy) * gi.shelterMap0.z));
                                                                                        vec2 _12943 = gi.shelterMap2.xy;
                                                                                        bool _10257;
                                                                                        if (gi.wetness1.w > 0.5)
                                                                                        {
                                                                                            _10257 = _10256.x >= 0;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _10257 = false;
                                                                                        }
                                                                                        if (_10257)
                                                                                        {
                                                                                            _10257 = _10256.y >= 0;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _10257 = false;
                                                                                        }
                                                                                        if (_10257)
                                                                                        {
                                                                                            _10257 = _10256.x < int(_12943.x);
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _10257 = false;
                                                                                        }
                                                                                        if (_10257)
                                                                                        {
                                                                                            _10257 = _10256.y < int(_12943.y);
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _10257 = false;
                                                                                        }
                                                                                        if (!_10257)
                                                                                        {
                                                                                            _10182 = false;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            vec4 _10204 = texelFetch(_13780, ivec3(_10256, 0).xy, 0);
                                                                                            float _12947 = gi.shelterMap0.w;
                                                                                            float _12948 = gi.shelterMap1.x;
                                                                                            float _10307 = floor((_10204.z * 255.0) + 0.5) + (floor((_10204.y * 255.0) + 0.5) * 256.0);
                                                                                            float _10295;
                                                                                            if (_10307 < 0.5)
                                                                                            {
                                                                                                _10295 = -1000000015047466219876688855040.0;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                _10295 = _12947 + ((_10307 - 1.0) * _12948);
                                                                                            }
                                                                                            float _10210 = _2984.z + gi.camera.z;
                                                                                            float _12952 = gi.shelterMap1.z;
                                                                                            bool _10320;
                                                                                            if (_10295 > (_10210 + gi.shelterMap1.y))
                                                                                            {
                                                                                                _10320 = _10295 < (_10210 + _12952);
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                _10320 = false;
                                                                                            }
                                                                                            _10182 = _10320;
                                                                                        }
                                                                                    }
                                                                                    _10098 = _10182;
                                                                                }
                                                                                if (!(_10098))
                                                                                {
                                                                                    vec3 _10115 = normal;
                                                                                    float _10340 = clamp((_10115.z * 0.5) + 0.5, 0.0, 1.0);
                                                                                    float _10363 = max(gi.wetness1.x, 0.0500000007450580596923828125);
                                                                                    float _10371 = _10363 * 0.4600000083446502685546875;
                                                                                    vec2 _10396 = vec2((0.800000011920928955078125 * gi.camera.xy.x) - (0.60000002384185791015625 * gi.camera.xy.y), (0.60000002384185791015625 * gi.camera.xy.x) + (0.800000011920928955078125 * gi.camera.xy.y)) / vec2(_10371);
                                                                                    vec2 _10397 = floor(_10396);
                                                                                    float _10423 = _2984.x;
                                                                                    float _10426 = _2984.y;
                                                                                    vec2 _10375 = ((vec2((0.800000011920928955078125 * _10423) - (0.60000002384185791015625 * _10426), (0.60000002384185791015625 * _10423) + (0.800000011920928955078125 * _10426)) / vec2(_10371)) + (_10396 - _10397)) + vec2(11.30000019073486328125);
                                                                                    vec2 _10435 = floor(_10375);
                                                                                    vec2 _10436 = _10375 - _10435;
                                                                                    vec2 _10441 = (_10436 * _10436) * (vec2(3.0) - (_10436 * 2.0));
                                                                                    vec2 _10442 = _10435 + _10397;
                                                                                    uvec2 _10460 = uvec2(ivec2(_10442) + ivec2(1048576));
                                                                                    uint _10465 = (_10460.x * 73856093u) ^ (_10460.y * 19349663u);
                                                                                    uint _10468 = (_10465 ^ (_10465 >> uint(13))) * 1540483477u;
                                                                                    uvec2 _10479 = uvec2(ivec2(_10442 + vec2(1.0, 0.0)) + ivec2(1048576));
                                                                                    uint _10484 = (_10479.x * 73856093u) ^ (_10479.y * 19349663u);
                                                                                    uint _10487 = (_10484 ^ (_10484 >> uint(13))) * 1540483477u;
                                                                                    uvec2 _10498 = uvec2(ivec2(_10442 + vec2(0.0, 1.0)) + ivec2(1048576));
                                                                                    uint _10503 = (_10498.x * 73856093u) ^ (_10498.y * 19349663u);
                                                                                    uint _10506 = (_10503 ^ (_10503 >> uint(13))) * 1540483477u;
                                                                                    uvec2 _10517 = uvec2(ivec2(_10442 + vec2(1.0)) + ivec2(1048576));
                                                                                    uint _10522 = (_10517.x * 73856093u) ^ (_10517.y * 19349663u);
                                                                                    uint _10525 = (_10522 ^ (_10522 >> uint(13))) * 1540483477u;
                                                                                    float _10450 = _10441.x;
                                                                                    float _10454 = mix(mix(float((_10468 ^ (_10468 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_10487 ^ (_10487 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _10450), mix(float((_10506 ^ (_10506 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_10525 ^ (_10525 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _10450), _10441.y);
                                                                                    vec2 _10541 = gi.camera.xy / vec2(_10363);
                                                                                    vec2 _10542 = floor(_10541);
                                                                                    vec2 _10383 = ((vec2(_2984.xy) / vec2(_10363)) + (_10541 - _10542)) + vec2((_10454 - 0.5) * 0.4000000059604644775390625);
                                                                                    vec2 _10580 = floor(_10383);
                                                                                    vec2 _10581 = _10383 - _10580;
                                                                                    vec2 _10586 = (_10581 * _10581) * (vec2(3.0) - (_10581 * 2.0));
                                                                                    vec2 _10587 = _10580 + _10542;
                                                                                    uvec2 _10605 = uvec2(ivec2(_10587) + ivec2(1048576));
                                                                                    uint _10610 = (_10605.x * 73856093u) ^ (_10605.y * 19349663u);
                                                                                    uint _10613 = (_10610 ^ (_10610 >> uint(13))) * 1540483477u;
                                                                                    uvec2 _10624 = uvec2(ivec2(_10587 + vec2(1.0, 0.0)) + ivec2(1048576));
                                                                                    uint _10629 = (_10624.x * 73856093u) ^ (_10624.y * 19349663u);
                                                                                    uint _10632 = (_10629 ^ (_10629 >> uint(13))) * 1540483477u;
                                                                                    uvec2 _10643 = uvec2(ivec2(_10587 + vec2(0.0, 1.0)) + ivec2(1048576));
                                                                                    uint _10648 = (_10643.x * 73856093u) ^ (_10643.y * 19349663u);
                                                                                    uint _10651 = (_10648 ^ (_10648 >> uint(13))) * 1540483477u;
                                                                                    uvec2 _10662 = uvec2(ivec2(_10587 + vec2(1.0)) + ivec2(1048576));
                                                                                    uint _10667 = (_10662.x * 73856093u) ^ (_10662.y * 19349663u);
                                                                                    uint _10670 = (_10667 ^ (_10667 >> uint(13))) * 1540483477u;
                                                                                    float _10595 = _10586.x;
                                                                                    float _10350 = clamp((_10160 * mix(0.25, 1.0, _10340 * _10340)) + (((clamp(((clamp(gi.wetness0.y, 0.0, 1.0) - ((mix(mix(float((_10613 ^ (_10613 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_10632 ^ (_10632 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _10595), mix(float((_10651 ^ (_10651 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_10670 ^ (_10670 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _10595), _10586.y) * 0.64999997615814208984375) + (_10454 * 0.3499999940395355224609375))) * 6.0) + 0.89999997615814208984375, 0.0, 1.0) * clamp((_10115.z - 0.977999985218048095703125) * 60.0, 0.0, 1.0)) * _10160) * 0.449999988079071044921875), 0.0, 1.0);
                                                                                    float _10696 = max(gi.wetness1.x, 0.0500000007450580596923828125);
                                                                                    float _10704 = _10696 * 0.4600000083446502685546875;
                                                                                    vec2 _10729 = vec2((0.800000011920928955078125 * gi.camera.xy.x) - (0.60000002384185791015625 * gi.camera.xy.y), (0.60000002384185791015625 * gi.camera.xy.x) + (0.800000011920928955078125 * gi.camera.xy.y)) / vec2(_10704);
                                                                                    vec2 _10730 = floor(_10729);
                                                                                    float _10756 = _2984.x;
                                                                                    float _10759 = _2984.y;
                                                                                    vec2 _10708 = ((vec2((0.800000011920928955078125 * _10756) - (0.60000002384185791015625 * _10759), (0.60000002384185791015625 * _10756) + (0.800000011920928955078125 * _10759)) / vec2(_10704)) + (_10729 - _10730)) + vec2(11.30000019073486328125);
                                                                                    vec2 _10768 = floor(_10708);
                                                                                    vec2 _10769 = _10708 - _10768;
                                                                                    vec2 _10774 = (_10769 * _10769) * (vec2(3.0) - (_10769 * 2.0));
                                                                                    vec2 _10775 = _10768 + _10730;
                                                                                    uvec2 _10793 = uvec2(ivec2(_10775) + ivec2(1048576));
                                                                                    uint _10798 = (_10793.x * 73856093u) ^ (_10793.y * 19349663u);
                                                                                    uint _10801 = (_10798 ^ (_10798 >> uint(13))) * 1540483477u;
                                                                                    uvec2 _10812 = uvec2(ivec2(_10775 + vec2(1.0, 0.0)) + ivec2(1048576));
                                                                                    uint _10817 = (_10812.x * 73856093u) ^ (_10812.y * 19349663u);
                                                                                    uint _10820 = (_10817 ^ (_10817 >> uint(13))) * 1540483477u;
                                                                                    uvec2 _10831 = uvec2(ivec2(_10775 + vec2(0.0, 1.0)) + ivec2(1048576));
                                                                                    uint _10836 = (_10831.x * 73856093u) ^ (_10831.y * 19349663u);
                                                                                    uint _10839 = (_10836 ^ (_10836 >> uint(13))) * 1540483477u;
                                                                                    uvec2 _10850 = uvec2(ivec2(_10775 + vec2(1.0)) + ivec2(1048576));
                                                                                    uint _10855 = (_10850.x * 73856093u) ^ (_10850.y * 19349663u);
                                                                                    uint _10858 = (_10855 ^ (_10855 >> uint(13))) * 1540483477u;
                                                                                    float _10783 = _10774.x;
                                                                                    float _10787 = mix(mix(float((_10801 ^ (_10801 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_10820 ^ (_10820 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _10783), mix(float((_10839 ^ (_10839 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_10858 ^ (_10858 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _10783), _10774.y);
                                                                                    vec2 _10874 = gi.camera.xy / vec2(_10696);
                                                                                    vec2 _10875 = floor(_10874);
                                                                                    vec2 _10716 = ((vec2(_2984.xy) / vec2(_10696)) + (_10874 - _10875)) + vec2((_10787 - 0.5) * 0.4000000059604644775390625);
                                                                                    vec2 _10913 = floor(_10716);
                                                                                    vec2 _10914 = _10716 - _10913;
                                                                                    vec2 _10919 = (_10914 * _10914) * (vec2(3.0) - (_10914 * 2.0));
                                                                                    vec2 _10920 = _10913 + _10875;
                                                                                    uvec2 _10938 = uvec2(ivec2(_10920) + ivec2(1048576));
                                                                                    uint _10943 = (_10938.x * 73856093u) ^ (_10938.y * 19349663u);
                                                                                    uint _10946 = (_10943 ^ (_10943 >> uint(13))) * 1540483477u;
                                                                                    uvec2 _10957 = uvec2(ivec2(_10920 + vec2(1.0, 0.0)) + ivec2(1048576));
                                                                                    uint _10962 = (_10957.x * 73856093u) ^ (_10957.y * 19349663u);
                                                                                    uint _10965 = (_10962 ^ (_10962 >> uint(13))) * 1540483477u;
                                                                                    uvec2 _10976 = uvec2(ivec2(_10920 + vec2(0.0, 1.0)) + ivec2(1048576));
                                                                                    uint _10981 = (_10976.x * 73856093u) ^ (_10976.y * 19349663u);
                                                                                    uint _10984 = (_10981 ^ (_10981 >> uint(13))) * 1540483477u;
                                                                                    uvec2 _10995 = uvec2(ivec2(_10920 + vec2(1.0)) + ivec2(1048576));
                                                                                    uint _11000 = (_10995.x * 73856093u) ^ (_10995.y * 19349663u);
                                                                                    uint _11003 = (_11000 ^ (_11000 >> uint(13))) * 1540483477u;
                                                                                    float _10928 = _10919.x;
                                                                                    float _10691 = (clamp((clamp(gi.wetness0.y, 0.0, 1.0) - ((mix(mix(float((_10946 ^ (_10946 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_10965 ^ (_10965 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _10928), mix(float((_10984 ^ (_10984 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_11003 ^ (_11003 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _10928), _10919.y) * 0.64999997615814208984375) + (_10787 * 0.3499999940395355224609375))) * 20.0, 0.0, 1.0) * clamp((_10115.z - 0.977999985218048095703125) * 60.0, 0.0, 1.0)) * _10160;
                                                                                    waterMask = _10691;
                                                                                    albedo *= mix(1.0, 0.60000002384185791015625, _10350);
                                                                                    roughness = mix(roughness, max(0.550000011920928955078125, roughness * 0.75), _10350);
                                                                                    if (!(_10691 <= 0.0))
                                                                                    {
                                                                                        vec3 _10130 = vec3(_10691);
                                                                                        albedo = mix(albedo, albedo * 0.300000011920928955078125, _10130);
                                                                                        vec2 _13013 = gi.camera.xy;
                                                                                        float _13016 = gi.wetness1.z;
                                                                                        float _11025 = max(gi.wetness1.y, 0.0199999995529651641845703125);
                                                                                        float _11028 = gi.wetness0.w * 0.60000002384185791015625;
                                                                                        int _11065 = 0;
                                                                                        vec2 _11066 = vec2(0.0);
                                                                                        vec2 _11169;
                                                                                        vec2 _11174;
                                                                                        SPIRV_CROSS_UNROLL
                                                                                        for (;;)
                                                                                        {
                                                                                            bool _11076_ladder_break = false;
                                                                                            {
                                                                                                if (!(_11065 < 4))
                                                                                                {
                                                                                                    _11076_ladder_break = true;
                                                                                                }
                                                                                                else
                                                                                                {
                                                                                                    vec4 _11067 = vec4(0.0, 0.2199999988079071044921875, 0.439999997615814208984375, 0.660000026226043701171875);
                                                                                                    float _11087 = clamp((gi.wetness0.z - _11067[_11065]) * 4.0, 0.0, 1.0);
                                                                                                    if (!(_11087 <= 0.0))
                                                                                                    {
                                                                                                        vec4 _11068 = vec4(0.519999980926513671875, 0.7400000095367431640625, 0.9700000286102294921875, 1.2799999713897705078125);
                                                                                                        float _11094 = _11025 * _11068[_11065];
                                                                                                        vec2 _11131 = _13013 / vec2(_11094);
                                                                                                        vec2 _11132 = floor(_11131);
                                                                                                        vec4 _11069 = vec4(0.959999978542327880859375, 0.810000002384185791015625, 0.88999998569488525390625, 0.7200000286102294921875);
                                                                                                        vec4 _11070 = vec4(0.0, 0.310000002384185791015625, 0.579999983310699462890625, 0.829999983310699462890625);
                                                                                                        float _11104 = fract((_11028 * _11069[_11065]) + _11070[_11065]);
                                                                                                        vec2 _11109 = ((vec2(_2984.xy) / vec2(_11094)) + (_11131 - _11132)) + _3646_frag[_11065];
                                                                                                        {
                                                                                                            if (_11087 <= 0.0)
                                                                                                            {
                                                                                                                _11169 = vec2(0.0);
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                float _11182 = mix(0.20000000298023223876953125, 1.0, clamp(_11087, 0.0, 1.0));
                                                                                                                vec2 _11185 = floor(_11109 - vec2(0.5));
                                                                                                                int _11170 = 0;
                                                                                                                vec2 _11171 = vec2(0.0);
                                                                                                                for (;;)
                                                                                                                {
                                                                                                                    if (!(_11170 < 2))
                                                                                                                    {
                                                                                                                        break;
                                                                                                                    }
                                                                                                                    int _11172 = 0;
                                                                                                                    vec2 _11173 = _11171;
                                                                                                                    for (;;)
                                                                                                                    {
                                                                                                                        bool _11197_ladder_break = false;
                                                                                                                        {
                                                                                                                            if (!(_11172 < 2))
                                                                                                                            {
                                                                                                                                _11197_ladder_break = true;
                                                                                                                            }
                                                                                                                            else
                                                                                                                            {
                                                                                                                                vec2 _11208 = _11185 + vec2(float(_11172), float(_11170));
                                                                                                                                vec2 _11211 = _11109 - (_11208 + vec2(0.5));
                                                                                                                                if (!(dot(_11211, _11211) > 0.608399927616119384765625))
                                                                                                                                {
                                                                                                                                    uvec2 _11271 = uvec2(ivec2(_11208 + _11132) + ivec2(1048576));
                                                                                                                                    uint _11276 = (_11271.x * 73856093u) ^ (_11271.y * 19349663u);
                                                                                                                                    uint _11279 = (_11276 ^ (_11276 >> uint(13))) * 1540483477u;
                                                                                                                                    uint _11281 = _11279 ^ (_11279 >> uint(15));
                                                                                                                                    uint _11282 = _11281 * 668265261u;
                                                                                                                                    uint _11284 = _11282 ^ (_11282 >> uint(15));
                                                                                                                                    uint _11285 = _11284 * 2246822507u;
                                                                                                                                    vec3 _11295 = vec3(float(_11281 & 16777215u), float(_11284 & 16777215u), float((_11285 ^ (_11285 >> uint(13))) & 16777215u)) * 5.9604651880817982601001858711243e-08;
                                                                                                                                    float _11220 = fract(_11104 + _11295.x);
                                                                                                                                    vec2 _11225 = _11211 - ((_11295.yz - vec2(0.5)) * 0.5);
                                                                                                                                    float _11226 = length(_11225);
                                                                                                                                    if (_11226 > 9.9999997473787516355514526367188e-06)
                                                                                                                                    {
                                                                                                                                        _11174 = _11225 / vec2(_11226);
                                                                                                                                    }
                                                                                                                                    else
                                                                                                                                    {
                                                                                                                                        _11174 = vec2(0.0);
                                                                                                                                    }
                                                                                                                                    _11173 -= (_11174 * ((sin(3.1415927410125732421875 * clamp((_11220 - clamp(_11226 * 2.17391300201416015625, 0.0, 1.0)) * 24.0, 0.0, 4.0)) * clamp(_11182 - _11220, 0.0, 1.0)) * 0.449999988079071044921875));
                                                                                                                                }
                                                                                                                            }
                                                                                                                        }
                                                                                                                        if (_11197_ladder_break)
                                                                                                                        {
                                                                                                                            break;
                                                                                                                        }
                                                                                                                        _11172++;
                                                                                                                        continue;
                                                                                                                    }
                                                                                                                    _11170++;
                                                                                                                    _11171 = _11173;
                                                                                                                    continue;
                                                                                                                }
                                                                                                                _11169 = _11171;
                                                                                                            }
                                                                                                        }
                                                                                                        _11066 += _11169;
                                                                                                    }
                                                                                                }
                                                                                            }
                                                                                            if (_11076_ladder_break)
                                                                                            {
                                                                                                break;
                                                                                            }
                                                                                            _11065++;
                                                                                            continue;
                                                                                        }
                                                                                        vec2 _11323 = _13013 * vec2(0.5555555820465087890625);
                                                                                        vec2 _11324 = floor(_11323);
                                                                                        vec2 _11303 = ((vec2(_2984.xy) * vec2(0.5555555820465087890625)) + (_11323 - _11324)) + vec2(gi.wetness0.w * 0.07999999821186065673828125, gi.wetness0.w * 0.0500000007450580596923828125);
                                                                                        vec2 _11362 = floor(_11303);
                                                                                        vec2 _11363 = _11303 - _11362;
                                                                                        vec2 _11368 = (_11363 * _11363) * (vec2(3.0) - (_11363 * 2.0));
                                                                                        vec2 _11369 = _11362 + _11324;
                                                                                        uvec2 _11387 = uvec2(ivec2(_11369) + ivec2(1048576));
                                                                                        uint _11392 = (_11387.x * 73856093u) ^ (_11387.y * 19349663u);
                                                                                        uint _11395 = (_11392 ^ (_11392 >> uint(13))) * 1540483477u;
                                                                                        uvec2 _11406 = uvec2(ivec2(_11369 + vec2(1.0, 0.0)) + ivec2(1048576));
                                                                                        uint _11411 = (_11406.x * 73856093u) ^ (_11406.y * 19349663u);
                                                                                        uint _11414 = (_11411 ^ (_11411 >> uint(13))) * 1540483477u;
                                                                                        uvec2 _11425 = uvec2(ivec2(_11369 + vec2(0.0, 1.0)) + ivec2(1048576));
                                                                                        uint _11430 = (_11425.x * 73856093u) ^ (_11425.y * 19349663u);
                                                                                        uint _11433 = (_11430 ^ (_11430 >> uint(13))) * 1540483477u;
                                                                                        uvec2 _11444 = uvec2(ivec2(_11369 + vec2(1.0)) + ivec2(1048576));
                                                                                        uint _11449 = (_11444.x * 73856093u) ^ (_11444.y * 19349663u);
                                                                                        uint _11452 = (_11449 ^ (_11449 >> uint(13))) * 1540483477u;
                                                                                        float _11377 = _11368.x;
                                                                                        float _11381 = mix(mix(float((_11395 ^ (_11395 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_11414 ^ (_11414 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _11377), mix(float((_11433 ^ (_11433 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_11452 ^ (_11452 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _11377), _11368.y);
                                                                                        vec2 _11306 = _11303 + vec2(0.1500000059604644775390625, 0.0);
                                                                                        vec2 _11460 = floor(_11306);
                                                                                        vec2 _11461 = _11306 - _11460;
                                                                                        vec2 _11466 = (_11461 * _11461) * (vec2(3.0) - (_11461 * 2.0));
                                                                                        vec2 _11467 = _11460 + _11324;
                                                                                        uvec2 _11485 = uvec2(ivec2(_11467) + ivec2(1048576));
                                                                                        uint _11490 = (_11485.x * 73856093u) ^ (_11485.y * 19349663u);
                                                                                        uint _11493 = (_11490 ^ (_11490 >> uint(13))) * 1540483477u;
                                                                                        uvec2 _11504 = uvec2(ivec2(_11467 + vec2(1.0, 0.0)) + ivec2(1048576));
                                                                                        uint _11509 = (_11504.x * 73856093u) ^ (_11504.y * 19349663u);
                                                                                        uint _11512 = (_11509 ^ (_11509 >> uint(13))) * 1540483477u;
                                                                                        uvec2 _11523 = uvec2(ivec2(_11467 + vec2(0.0, 1.0)) + ivec2(1048576));
                                                                                        uint _11528 = (_11523.x * 73856093u) ^ (_11523.y * 19349663u);
                                                                                        uint _11531 = (_11528 ^ (_11528 >> uint(13))) * 1540483477u;
                                                                                        uvec2 _11542 = uvec2(ivec2(_11467 + vec2(1.0)) + ivec2(1048576));
                                                                                        uint _11547 = (_11542.x * 73856093u) ^ (_11542.y * 19349663u);
                                                                                        uint _11550 = (_11547 ^ (_11547 >> uint(13))) * 1540483477u;
                                                                                        vec2 _11309 = _11303 + vec2(0.0, 0.1500000059604644775390625);
                                                                                        vec2 _11558 = floor(_11309);
                                                                                        vec2 _11559 = _11309 - _11558;
                                                                                        vec2 _11564 = (_11559 * _11559) * (vec2(3.0) - (_11559 * 2.0));
                                                                                        vec2 _11565 = _11558 + _11324;
                                                                                        uvec2 _11583 = uvec2(ivec2(_11565) + ivec2(1048576));
                                                                                        uint _11588 = (_11583.x * 73856093u) ^ (_11583.y * 19349663u);
                                                                                        uint _11591 = (_11588 ^ (_11588 >> uint(13))) * 1540483477u;
                                                                                        uvec2 _11602 = uvec2(ivec2(_11565 + vec2(1.0, 0.0)) + ivec2(1048576));
                                                                                        uint _11607 = (_11602.x * 73856093u) ^ (_11602.y * 19349663u);
                                                                                        uint _11610 = (_11607 ^ (_11607 >> uint(13))) * 1540483477u;
                                                                                        uvec2 _11621 = uvec2(ivec2(_11565 + vec2(0.0, 1.0)) + ivec2(1048576));
                                                                                        uint _11626 = (_11621.x * 73856093u) ^ (_11621.y * 19349663u);
                                                                                        uint _11629 = (_11626 ^ (_11626 >> uint(13))) * 1540483477u;
                                                                                        uvec2 _11640 = uvec2(ivec2(_11565 + vec2(1.0)) + ivec2(1048576));
                                                                                        uint _11645 = (_11640.x * 73856093u) ^ (_11640.y * 19349663u);
                                                                                        uint _11648 = (_11645 ^ (_11645 >> uint(13))) * 1540483477u;
                                                                                        vec2 _11036 = (vec2(mix(mix(float((_11493 ^ (_11493 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_11512 ^ (_11512 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _11466.x), mix(float((_11531 ^ (_11531 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_11550 ^ (_11550 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _11466.x), _11466.y) - _11381, mix(mix(float((_11591 ^ (_11591 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_11610 ^ (_11610 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _11564.x), mix(float((_11629 ^ (_11629 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_11648 ^ (_11648 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _11564.x), _11564.y) - _11381) * _13016) * 1.60000002384185791015625;
                                                                                        vec2 _11037 = _11066 + _11036;
                                                                                        float _11040 = clamp(((abs((_5604 * 2.0) * _8949.z) / max(gi.unproject.x, 9.9999997473787516355514526367188e-05)) / max(max(dot(_2963, _2930), 0.001000000047497451305389404296875), 0.07999999821186065673828125)) / (_11025 * 0.5), 0.0, 1.0);
                                                                                        float _11042 = 1.0 - _11040;
                                                                                        vec3 _10133 = normal;
                                                                                        vec3 _10136 = normalize(mix(_10133, normalize(vec3(_11037 * _11042, 1.0)), _10130));
                                                                                        normal = _10136;
                                                                                        roughness = mix(roughness, clamp(((0.0500000007450580596923828125 + (0.2199999988079071044921875 * clamp(gi.wetness0.z, 0.0, 1.0))) + ((length(_11037) * _11042) * 0.100000001490116119384765625)) + (((0.20000000298023223876953125 * gi.wetness0.z) + (0.100000001490116119384765625 * _13016)) * _11040), 0.0500000007450580596923828125, 0.449999988079071044921875), _10691);
                                                                                        float _11658 = 1.0 - clamp(dot(_10136, _2930), 0.0, 1.0);
                                                                                        float _11659 = _11658 * _11658;
                                                                                        float _10144 = ((0.0199999995529651641845703125 + (((0.980000019073486328125 * _11659) * _11659) * _11658)) * _10691) * (1.0 - (0.75 * clamp(_10160 * 5.0, 0.0, 1.0)));
                                                                                        waterReflectance = _10144;
                                                                                        albedo *= (1.0 - _10144);
                                                                                    }
                                                                                }
                                                                            }
                                                                            specularLevel = max(specularLevel_1, 0.5 * waterMask);
                                                                        }
                                                                        else
                                                                        {
                                                                            specularLevel = specularLevel_1;
                                                                        }
                                                                        if (debugView == 13)
                                                                        {
                                                                            entryPointParam_fragmentMain = vec4(waterMask, (normal.xy * 0.5) + vec2(0.5), 1.0);
                                                                        }
                                                                        else
                                                                        {
                                                                            vec3 sunIlluminance = light.sunColourScale.xyz * light.sunColourScale.w;
                                                                            vec3 _11681 = mix(vec3(0.07999999821186065673828125 * specularLevel), albedo, vec3(metallic));
                                                                            float _11682 = max(roughness, 0.0199999995529651641845703125);
                                                                            float _11683 = _11682 * _11682;
                                                                            float _11684 = _11683 * _11683;
                                                                            vec3 _11685 = _2930 + _2097;
                                                                            vec3 _11689 = _11685 * inversesqrt(max(dot(_11685, _11685), 9.9999999392252902907785028219223e-09));
                                                                            float _11691 = clamp(dot(normal, _11689), 0.0, 1.0);
                                                                            float _11695 = clamp(dot(normal, _2097), 0.0, 1.0);
                                                                            float _11699 = clamp(abs(dot(normal, _2930)) + 9.9999997473787516355514526367188e-06, 0.0, 1.0);
                                                                            float _11703 = ((_11691 * _11691) * (_11684 - 1.0)) + 1.0;
                                                                            vec3 surfaceDiffuse = (albedo * (1.0 - metallic)) * (_11695 * 0.3183098733425140380859375);
                                                                            vec3 surfaceSpecular = ((_11681 + ((vec3(clamp(50.0 * _11681.y, 0.0, 1.0)) - _11681) * pow(1.0 - clamp(dot(_2930, _11689), 0.0, 1.0), 5.0))) * ((_11684 / ((3.1415927410125732421875 * _11703) * _11703)) * (0.5 / max(mix((2.0 * _11695) * _11699, _11695 + _11699, _11683), 9.9999999747524270787835121154785e-07)))) * _11695;
                                                                            if (waterMask > 0.0)
                                                                            {
                                                                                surfaceSpecular *= (1.0 - (waterMask * clamp(max(gi.wetness0.x, 0.0) * 5.0, 0.0, 1.0)));
                                                                            }
                                                                            vec3 sunLight_1 = ((surfaceDiffuse + surfaceSpecular) * sunIlluminance) * visibility;
                                                                            if (waterReflectance > 0.0)
                                                                            {
                                                                                vec3 _4077 = normalize(reflect(worldRayToPixel, normal));
                                                                                float _11784 = acos(clamp(sqrt(max(0.0, (gi.giParams.w * gi.giParams.w) - 40449600.0)) / gi.giParams.w, -1.0, 1.0));
                                                                                float _11785 = 3.1415927410125732421875 - _11784;
                                                                                float _11788 = acos(clamp(_4077.z, -1.0, 1.0));
                                                                                float _11774;
                                                                                if (_11788 < _11785)
                                                                                {
                                                                                    _11774 = (1.0 - sqrt(max(0.0, 1.0 - (_11788 / _11785)))) * 0.5;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _11774 = (sqrt(max(0.0, (_11788 - _11785) / max(_11784, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                                                                }
                                                                                float _11808 = atan(_4077.y, _4077.x);
                                                                                float _11775;
                                                                                if (_11808 < 0.0)
                                                                                {
                                                                                    _11775 = _11808 + 6.283185482025146484375;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _11775 = _11808;
                                                                                }
                                                                                uvec2 _11823 = uvec2(textureSize(_13756, 0));
                                                                                vec2 _11830 = vec2(float(_11823.x), float(_11823.y));
                                                                                sunLight = sunLight_1 + ((textureLod(_13758, (vec2(_11775 * 0.15915493667125701904296875, _11774) * ((_11830 - vec2(1.0)) / _11830)) + (vec2(0.5) / _11830), 0.0).xyz * waterReflectance) * clamp(_4077.z * 20.0, 0.0, 1.0));
                                                                            }
                                                                            else
                                                                            {
                                                                                sunLight = sunLight_1;
                                                                            }
                                                                            float _4115 = _8949.z;
                                                                            uvec2 _11866 = uvec2(textureSize(_13763, 0));
                                                                            uint _11867 = _11866.x;
                                                                            uint _11851 = _11867;
                                                                            uint _11868 = _11866.y;
                                                                            uint _11852 = _11868;
                                                                            ivec2 _11875 = ivec2(int(_11867) - 1, int(_11868) - 1);
                                                                            uvec2 _11877 = uvec2(textureSize(_13783, 0));
                                                                            vec2 _11884 = vec2(float(_11877.x), float(_11877.y));
                                                                            vec2 _11887 = (input_uv * _11884) - vec2(0.5);
                                                                            vec2 _11888 = fract(_11887);
                                                                            ivec2 _11890 = ivec2(floor(_11887));
                                                                            ivec2 _11893 = ivec2(_11884) - ivec2(1);
                                                                            float _11894 = _11888.x;
                                                                            float _11895 = 1.0 - _11894;
                                                                            float _11896 = _11888.y;
                                                                            float _11897 = 1.0 - _11896;
                                                                            vec4 _11855 = vec4(_11895 * _11897, _11894 * _11897, _11895 * _11896, _11894 * _11896);
                                                                            float _11856 = 1000000015047466219876688855040.0;
                                                                            vec2 _11858 = vec2(1.0, 0.0);
                                                                            int _11860 = 0;
                                                                            vec2 _11859 = vec2(0.0);
                                                                            float _11857 = 0.0;
                                                                            vec2 _11861;
                                                                            float _11862;
                                                                            for (;;)
                                                                            {
                                                                                bool _11905_ladder_break = false;
                                                                                {
                                                                                    if (!(_11860 < 4))
                                                                                    {
                                                                                        _11905_ladder_break = true;
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        ivec2 _11917 = clamp(_11890 + ivec2(_11860 & 1, _11860 >> 1), ivec2(0), _11893);
                                                                                        vec4 _11921 = texelFetch(_13783, ivec3(_11917, 0).xy, 0);
                                                                                        float _11923 = _11921.z;
                                                                                        if (!(_11923 <= 0.0))
                                                                                        {
                                                                                            float _11928 = abs(_11923 - _4115);
                                                                                            if (_11928 < _11856)
                                                                                            {
                                                                                                _11862 = _11928;
                                                                                                _11861 = _11921.xy;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                _11862 = _11856;
                                                                                                _11861 = _11858;
                                                                                            }
                                                                                            float _11956 = clamp(dot(normalize((texelFetch(_13763, ivec3(clamp(ivec2((vec2(_11917) + vec2(0.5)) * (vec2(float(_11851), float(_11852)) / _11884)), ivec2(0), _11875), 0).xy, 0).xyz * 2.0) - vec3(1.0)), normal), 0.0, 1.0);
                                                                                            float _11969 = ((((_11855[_11860] * exp2((-_11928) / max(0.02999999932944774627685546875 * _4115, 0.001000000047497451305389404296875))) * _11956) * _11956) * _11956) * _11956;
                                                                                            _11856 = _11862;
                                                                                            _11858 = _11861;
                                                                                            _11859 += (_11921.xy * _11969);
                                                                                            _11857 += _11969;
                                                                                        }
                                                                                    }
                                                                                }
                                                                                if (_11905_ladder_break)
                                                                                {
                                                                                    break;
                                                                                }
                                                                                _11860++;
                                                                                continue;
                                                                            }
                                                                            if (_11857 > 9.9999997473787516355514526367188e-05)
                                                                            {
                                                                                _11861 = _11859 / vec2(_11857);
                                                                            }
                                                                            else
                                                                            {
                                                                                _11861 = _11858;
                                                                            }
                                                                            vec3 _11998 = vec3(_11861, _13736);
                                                                            bool lampShadows = lamps.control.y > 0.5;
                                                                            float lampOcclusion;
                                                                            if (lampShadows)
                                                                            {
                                                                                lampOcclusion = clamp(_11998.x, 0.0, 1.0);
                                                                            }
                                                                            else
                                                                            {
                                                                                lampOcclusion = 1.0;
                                                                            }
                                                                            if (debugView == 29)
                                                                            {
                                                                                float _4323 = _11998.y;
                                                                                if (_4323 > 0.0040000001899898052215576171875)
                                                                                {
                                                                                    visibility = clamp(_4323, 0.0, 1.0);
                                                                                }
                                                                                else
                                                                                {
                                                                                    visibility = 0.0;
                                                                                }
                                                                                entryPointParam_fragmentMain = vec4(clamp(_11998.x, 0.0, 1.0), visibility, 0.0, 1.0);
                                                                            }
                                                                            else
                                                                            {
                                                                                float trust;
                                                                                if (debugView == 30)
                                                                                {
                                                                                    float _4339 = clamp(_11998.x, 0.0, 1.0);
                                                                                    float _4340 = _11998.y;
                                                                                    if (_4340 > 0.0)
                                                                                    {
                                                                                        trust = clamp(_4340 - 0.00200000009499490261077880859375, 0.0, 1.0);
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        trust = 0.0;
                                                                                    }
                                                                                    entryPointParam_fragmentMain = vec4(clamp((1.0 - _4339) - trust, 0.0, 1.0), _4339, trust, 1.0);
                                                                                }
                                                                                else
                                                                                {
                                                                                    if (debugView == 26)
                                                                                    {
                                                                                        if (lampShadows)
                                                                                        {
                                                                                            lampGridded = _11998.y <= 0.0;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            lampGridded = false;
                                                                                        }
                                                                                        if (lampGridded)
                                                                                        {
                                                                                            _90 = vec4(0.60000002384185791015625, 0.0, 0.0, 1.0);
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _90 = vec4(lampOcclusion, lampOcclusion, lampOcclusion, 1.0);
                                                                                        }
                                                                                        entryPointParam_fragmentMain = _90;
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        uint lampFirst = 0u;
                                                                                        uint lampListed = 0u;
                                                                                        uint lampCell;
                                                                                        bool _12007;
                                                                                        {
                                                                                            lampCell = 0u;
                                                                                            bool _12008;
                                                                                            if (uLampGrid._member0[0] == 0u)
                                                                                            {
                                                                                                _12008 = true;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                _12008 = uLampGrid._member0[1] == 0u;
                                                                                            }
                                                                                            if (_12008)
                                                                                            {
                                                                                                _12007 = false;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                float _12029 = uintBitsToFloat(uLampGrid._member0[3]);
                                                                                                int _12009;
                                                                                                if (_4115 <= _12029)
                                                                                                {
                                                                                                    _12009 = 0;
                                                                                                }
                                                                                                else
                                                                                                {
                                                                                                    _12009 = int(floor(log2(_4115 / _12029) * uintBitsToFloat(uLampGrid._member0[4])));
                                                                                                }
                                                                                                lampCell = (((uint(clamp(_12009, 0, int(uLampGrid._member0[2]) - 1)) * uLampGrid._member0[1]) + uint(clamp(int(floor(input_uv.y * uintBitsToFloat(uLampGrid._member0[6]))), 0, int(uLampGrid._member0[1]) - 1))) * uLampGrid._member0[0]) + uint(clamp(int(floor(input_uv.x * uintBitsToFloat(uLampGrid._member0[5]))), 0, int(uLampGrid._member0[0]) - 1));
                                                                                                _12007 = true;
                                                                                            }
                                                                                        }
                                                                                        if (_12007)
                                                                                        {
                                                                                            uint _12077 = lampCell * 32u;
                                                                                            uint _12078 = 8u + _12077;
                                                                                            lampListed = uLampGrid._member0[_12078];
                                                                                            lampFirst = _12077 + 9u;
                                                                                            lampGridded = uLampGrid._member0[_12078] != 4294967295u;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            lampGridded = false;
                                                                                        }
                                                                                        int _109;
                                                                                        if (lampGridded)
                                                                                        {
                                                                                            _109 = int(lampListed);
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _109 = int(lamps.control.x + 0.5);
                                                                                        }
                                                                                        int n = 0;
                                                                                        lampLight = vec3(0.0);
                                                                                        int i;
                                                                                        vec3 _12091;
                                                                                        float _12092;
                                                                                        float _12093;
                                                                                        float _12094;
                                                                                        vec3 _12095;
                                                                                        bool _12096;
                                                                                        float _12244;
                                                                                        float _12279;
                                                                                        float _12280;
                                                                                        float _12281;
                                                                                        for (;;)
                                                                                        {
                                                                                            bool _240_ladder_break = false;
                                                                                            {
                                                                                                if (!(n < _109))
                                                                                                {
                                                                                                    _240_ladder_break = true;
                                                                                                }
                                                                                                else
                                                                                                {
                                                                                                    if (lampGridded)
                                                                                                    {
                                                                                                        i = int(uLampGrid._member0[lampFirst + uint(n)]);
                                                                                                    }
                                                                                                    else
                                                                                                    {
                                                                                                        i = n;
                                                                                                    }
                                                                                                    {
                                                                                                        vec3 _12107 = lamps.positionRadius.data[i].xyz - _8949;
                                                                                                        float _12108 = dot(_12107, _12107);
                                                                                                        {
                                                                                                            float _12248 = lamps.positionRadius.data[i].w * lamps.positionRadius.data[i].w;
                                                                                                            if (_12108 >= _12248)
                                                                                                            {
                                                                                                                _12244 = 0.0;
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                float _12253 = _12108 / max(_12248, 9.9999997473787516355514526367188e-05);
                                                                                                                float _12256 = clamp(1.0 - (_12253 * _12253), 0.0, 1.0);
                                                                                                                _12244 = _12256 * _12256;
                                                                                                            }
                                                                                                        }
                                                                                                        if (_12244 <= 0.0)
                                                                                                        {
                                                                                                            _12091 = vec3(0.0);
                                                                                                        }
                                                                                                        else
                                                                                                        {
                                                                                                            vec3 _12118 = (vec4(_12107, 0.0) * spvWorkaroundRowMajor(gi.viewToWorld)).xyz;
                                                                                                            vec3 _12121 = _12118 * inversesqrt(max(_12108, 9.9999999392252902907785028219223e-09));
                                                                                                            if (dot(lamps.direction.data[i].xyz, lamps.direction.data[i].xyz) > 0.25)
                                                                                                            {
                                                                                                                _12092 = _12244 * max(dot(normalize((vec4(lamps.direction.data[i].xyz, 0.0) * spvWorkaroundRowMajor(gi.viewToWorld)).xyz), -_12121), 0.0);
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                _12092 = _12244;
                                                                                                            }
                                                                                                            if (_12092 <= 0.0)
                                                                                                            {
                                                                                                                _12091 = vec3(0.0);
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                float _12152 = clamp(lamps.direction.data[i].w, 0.0500000007450580596923828125, 1.0);
                                                                                                                vec3 _12161 = (vec4(lamps.rect.data[i].xyz, 0.0) * spvWorkaroundRowMajor(gi.viewToWorld)).xyz;
                                                                                                                if (dot(_12161, _12161) > 9.9999997473787516355514526367188e-05)
                                                                                                                {
                                                                                                                    vec3 _12167 = _12118 - _12161;
                                                                                                                    vec3 _12168 = _12161 * 2.0;
                                                                                                                    {
                                                                                                                        float _12285 = dot(normal, _12167);
                                                                                                                        float _12286 = dot(normal, _12168);
                                                                                                                        if (abs(_12286) > 9.9999999747524270787835121154785e-07)
                                                                                                                        {
                                                                                                                            float _12295 = (-_12285) / _12286;
                                                                                                                            if (_12286 > 0.0)
                                                                                                                            {
                                                                                                                                _12280 = 1.0;
                                                                                                                                _12281 = max(0.0, _12295);
                                                                                                                            }
                                                                                                                            else
                                                                                                                            {
                                                                                                                                _12280 = min(1.0, _12295);
                                                                                                                                _12281 = 0.0;
                                                                                                                            }
                                                                                                                            if (_12280 <= _12281)
                                                                                                                            {
                                                                                                                                _12279 = 0.0;
                                                                                                                            }
                                                                                                                            else
                                                                                                                            {
                                                                                                                                float _12308 = dot(_12168, _12168);
                                                                                                                                float _12309 = dot(_12167, _12168);
                                                                                                                                float _12310 = dot(_12167, _12167);
                                                                                                                                float _12313 = (_12308 * _12310) - (_12309 * _12309);
                                                                                                                                if (_12313 <= ((9.9999997473787516355514526367188e-06 * _12308) * _12310))
                                                                                                                                {
                                                                                                                                    vec3 _12323 = _12167 + (_12168 * (0.5 * (_12281 + _12280)));
                                                                                                                                    float _12325 = max(dot(_12323, _12323), 9.9999997473787516355514526367188e-05);
                                                                                                                                    _12279 = (clamp(dot(normal, _12323) * inversesqrt(_12325), 0.0, 1.0) / _12325) * (_12280 - _12281);
                                                                                                                                }
                                                                                                                                else
                                                                                                                                {
                                                                                                                                    float _12337 = _12308 * _12281;
                                                                                                                                    float _12340 = 2.0 * _12309;
                                                                                                                                    float _12348 = _12308 * _12280;
                                                                                                                                    _12279 = max((((_12285 * (_12348 + _12309)) - (_12286 * ((_12309 * _12280) + _12310))) / (_12313 * sqrt(max(((_12348 * _12280) + (_12340 * _12280)) + _12310, 9.9999999392252902907785028219223e-09)))) - (((_12285 * (_12337 + _12309)) - (_12286 * ((_12309 * _12281) + _12310))) / (_12313 * sqrt(max(((_12337 * _12281) + (_12340 * _12281)) + _12310, 9.9999999392252902907785028219223e-09)))), 0.0);
                                                                                                                                }
                                                                                                                            }
                                                                                                                        }
                                                                                                                        else
                                                                                                                        {
                                                                                                                            if (_12285 <= 0.0)
                                                                                                                            {
                                                                                                                                _12279 = 0.0;
                                                                                                                            }
                                                                                                                            else
                                                                                                                            {
                                                                                                                                _12280 = 1.0;
                                                                                                                                _12281 = 0.0;
                                                                                                                                if (_12280 <= _12281)
                                                                                                                                {
                                                                                                                                    _12279 = 0.0;
                                                                                                                                }
                                                                                                                                else
                                                                                                                                {
                                                                                                                                    float _12308 = dot(_12168, _12168);
                                                                                                                                    float _12309 = dot(_12167, _12168);
                                                                                                                                    float _12310 = dot(_12167, _12167);
                                                                                                                                    float _12313 = (_12308 * _12310) - (_12309 * _12309);
                                                                                                                                    if (_12313 <= ((9.9999997473787516355514526367188e-06 * _12308) * _12310))
                                                                                                                                    {
                                                                                                                                        vec3 _12323 = _12167 + (_12168 * (0.5 * (_12281 + _12280)));
                                                                                                                                        float _12325 = max(dot(_12323, _12323), 9.9999997473787516355514526367188e-05);
                                                                                                                                        _12279 = (clamp(dot(normal, _12323) * inversesqrt(_12325), 0.0, 1.0) / _12325) * (_12280 - _12281);
                                                                                                                                    }
                                                                                                                                    else
                                                                                                                                    {
                                                                                                                                        float _12337 = _12308 * _12281;
                                                                                                                                        float _12340 = 2.0 * _12309;
                                                                                                                                        float _12348 = _12308 * _12280;
                                                                                                                                        _12279 = max((((_12285 * (_12348 + _12309)) - (_12286 * ((_12309 * _12280) + _12310))) / (_12313 * sqrt(max(((_12348 * _12280) + (_12340 * _12280)) + _12310, 9.9999999392252902907785028219223e-09)))) - (((_12285 * (_12337 + _12309)) - (_12286 * ((_12309 * _12281) + _12310))) / (_12313 * sqrt(max(((_12337 * _12281) + (_12340 * _12281)) + _12310, 9.9999999392252902907785028219223e-09)))), 0.0);
                                                                                                                                    }
                                                                                                                                }
                                                                                                                            }
                                                                                                                        }
                                                                                                                    }
                                                                                                                    vec3 _12171 = reflect(-_2930, normal);
                                                                                                                    float _12172 = dot(_12171, _12168);
                                                                                                                    float _12175 = dot(_12168, _12168) - (_12172 * _12172);
                                                                                                                    if (_12175 > 9.9999999747524270787835121154785e-07)
                                                                                                                    {
                                                                                                                        _12093 = ((dot(_12171, _12167) * _12172) - dot(_12167, _12168)) / _12175;
                                                                                                                    }
                                                                                                                    else
                                                                                                                    {
                                                                                                                        _12093 = 0.5;
                                                                                                                    }
                                                                                                                    _12095 = _12167 + (_12168 * clamp(_12093, 0.0, 1.0));
                                                                                                                    _12093 = _12279;
                                                                                                                    _12094 = clamp(lamps.rect.data[i].w, 0.0199999995529651641845703125, _12152);
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    _12095 = _12118;
                                                                                                                    _12093 = clamp(dot(normal, _12121), 0.0, 1.0) / max(_12108, _12152 * _12152);
                                                                                                                    _12094 = _12152;
                                                                                                                }
                                                                                                                float _12195 = max(dot(_12095, _12095), 9.9999999747524270787835121154785e-07);
                                                                                                                vec3 _12198 = _12095 * inversesqrt(_12195);
                                                                                                                if (_12093 <= 0.0)
                                                                                                                {
                                                                                                                    _12096 = dot(normal, _12198) <= 0.0;
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    _12096 = false;
                                                                                                                }
                                                                                                                if (_12096)
                                                                                                                {
                                                                                                                    _12091 = vec3(0.0);
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    float _12214 = max(roughness, 0.0199999995529651641845703125) * max(roughness, 0.0199999995529651641845703125);
                                                                                                                    float _12220 = clamp(_12214 + (_12094 / (2.0 * sqrt(_12195))), 0.0, 1.0);
                                                                                                                    vec3 _12385 = mix(vec3(0.07999999821186065673828125 * specularLevel), albedo, vec3(metallic));
                                                                                                                    float _12386 = max(sqrt(_12220), 0.0199999995529651641845703125);
                                                                                                                    float _12387 = _12386 * _12386;
                                                                                                                    float _12388 = _12387 * _12387;
                                                                                                                    vec3 _12389 = _2930 + _12198;
                                                                                                                    vec3 _12393 = _12389 * inversesqrt(max(dot(_12389, _12389), 9.9999999392252902907785028219223e-09));
                                                                                                                    float _12395 = clamp(dot(normal, _12393), 0.0, 1.0);
                                                                                                                    float _12399 = clamp(dot(normal, _12198), 0.0, 1.0);
                                                                                                                    float _12403 = clamp(abs(dot(normal, _2930)) + 9.9999997473787516355514526367188e-06, 0.0, 1.0);
                                                                                                                    float _12407 = ((_12395 * _12395) * (_12388 - 1.0)) + 1.0;
                                                                                                                    _12091 = ((lamps.colourIntensity.data[i].xyz * lamps.colourIntensity.data[i].w) * _12092) * (((albedo * (1.0 - metallic)) * _12093) + ((((((_12385 + ((vec3(clamp(50.0 * _12385.y, 0.0, 1.0)) - _12385) * pow(1.0 - clamp(dot(_2930, _12393), 0.0, 1.0), 5.0))) * ((_12388 / ((3.1415927410125732421875 * _12407) * _12407)) * (0.5 / max(mix((2.0 * _12399) * _12403, _12399 + _12403, _12387), 9.9999999747524270787835121154785e-07)))) * _12399) * ((_12214 / _12220) * (_12214 / _12220))) * (1.0 / max(_12195, _12094 * _12094))) * 3.1415927410125732421875));
                                                                                                                }
                                                                                                            }
                                                                                                        }
                                                                                                    }
                                                                                                    vec3 lampTerm = _12091 * lampOcclusion;
                                                                                                    if (!(all(lessThanEqual(lampTerm, vec3(0.0)))))
                                                                                                    {
                                                                                                        sunLight += lampTerm;
                                                                                                        lampLight += lampTerm;
                                                                                                    }
                                                                                                }
                                                                                            }
                                                                                            if (_240_ladder_break)
                                                                                            {
                                                                                                break;
                                                                                            }
                                                                                            n++;
                                                                                            continue;
                                                                                        }
                                                                                        if (debugView == 14)
                                                                                        {
                                                                                            entryPointParam_fragmentMain = vec4(lampLight, 1.0);
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            vec3 flashLight;
                                                                                            if (any(greaterThan(_8951, vec3(0.0))))
                                                                                            {
                                                                                                vec3 _12437 = mix(vec3(0.07999999821186065673828125 * specularLevel), albedo, vec3(metallic));
                                                                                                float _12438 = max(roughness, 0.0199999995529651641845703125);
                                                                                                float _12439 = _12438 * _12438;
                                                                                                float _12440 = _12439 * _12439;
                                                                                                vec3 _12441 = _2930 + flashDir;
                                                                                                vec3 _12445 = _12441 * inversesqrt(max(dot(_12441, _12441), 9.9999999392252902907785028219223e-09));
                                                                                                float _12447 = clamp(dot(normal, _12445), 0.0, 1.0);
                                                                                                float _12451 = clamp(dot(normal, flashDir), 0.0, 1.0);
                                                                                                float _12455 = clamp(abs(dot(normal, _2930)) + 9.9999997473787516355514526367188e-06, 0.0, 1.0);
                                                                                                float _12459 = ((_12447 * _12447) * (_12440 - 1.0)) + 1.0;
                                                                                                vec3 flashLight_1 = ((((albedo * (1.0 - metallic)) * (_12451 * 0.3183098733425140380859375)) + (((_12437 + ((vec3(clamp(50.0 * _12437.y, 0.0, 1.0)) - _12437) * pow(1.0 - clamp(dot(_2930, _12445), 0.0, 1.0), 5.0))) * ((_12440 / ((3.1415927410125732421875 * _12459) * _12459)) * (0.5 / max(mix((2.0 * _12451) * _12455, _12451 + _12455, _12439), 9.9999999747524270787835121154785e-07)))) * _12451)) * _8951) * 3.1415927410125732421875;
                                                                                                flashLight = flashLight_1;
                                                                                                sunLight += flashLight_1;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                flashLight = vec3(0.0);
                                                                                            }
                                                                                            if (debugView == 27)
                                                                                            {
                                                                                                entryPointParam_fragmentMain = vec4(flashLight, 1.0);
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                vec3 diffuseColour = albedo - (albedo * metallic);
                                                                                                bool gathered = gi.giParams.x > 0.0;
                                                                                                bool _4967 = !gathered;
                                                                                                if (_4967)
                                                                                                {
                                                                                                    lampGridded = true;
                                                                                                }
                                                                                                else
                                                                                                {
                                                                                                    lampGridded = debugView == 9;
                                                                                                }
                                                                                                if (lampGridded)
                                                                                                {
                                                                                                    lampGridded = gi.probeOrigin.w > 0.5;
                                                                                                }
                                                                                                else
                                                                                                {
                                                                                                    lampGridded = false;
                                                                                                }
                                                                                                vec3 indirect;
                                                                                                if (lampGridded)
                                                                                                {
                                                                                                    vec3 worldPos = (vec4(_8949, 1.0) * spvWorkaroundRowMajor(gi.viewToWorld)).xyz + gi.camera.xyz;
                                                                                                    vec3 grid = ((worldPos + (normal * gi.probeMisc.x)) - gi.probeOrigin.xyz) / vec3(gi.probeInvCount.w);
                                                                                                    vec3 uvw = (grid + vec3(0.5)) * gi.probeInvCount.xyz;
                                                                                                    if (all(greaterThanEqual(uvw, vec3(0.0))))
                                                                                                    {
                                                                                                        lampGridded = all(lessThanEqual(uvw, vec3(1.0)));
                                                                                                    }
                                                                                                    else
                                                                                                    {
                                                                                                        lampGridded = false;
                                                                                                    }
                                                                                                    if (lampGridded)
                                                                                                    {
                                                                                                        vec4 sh = vec4(1.0, normal);
                                                                                                        vec3 probeLight = vec3(max(dot(textureLod(_13785, uvw, 0.0), sh), 0.0), max(dot(textureLod(_13787, uvw, 0.0), sh), 0.0), max(dot(textureLod(_13789, uvw, 0.0), sh), 0.0));
                                                                                                        if (debugView == 9)
                                                                                                        {
                                                                                                            entryPointParam_fragmentMain = vec4(probeLight * gi.probeSky.xyz, 1.0);
                                                                                                        }
                                                                                                        else
                                                                                                        {
                                                                                                            vec4 sampled_3 = textureLod(_13791, uvw, 0.0);
                                                                                                            float _5076 = sampled_3.x;
                                                                                                            float _5079 = max(sampled_3.y - (_5076 * _5076), 9.9999997473787516355514526367188e-05);
                                                                                                            float _5088 = length(worldPos - (gi.probeOrigin.xyz + (round(grid) * gi.probeInvCount.w)));
                                                                                                            if (_5088 > _5076)
                                                                                                            {
                                                                                                                float difference = _5088 - _5076;
                                                                                                                float trust_1 = _5079 / (_5079 + (difference * difference));
                                                                                                                trust = (trust_1 * trust_1) * trust_1;
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                trust = 1.0;
                                                                                                            }
                                                                                                            indirect = mix(light.ambient.xyz, probeLight * gi.probeSky.xyz, vec3(trust * gi.probeMisc.y));
                                                                                                            if (debugView == 9)
                                                                                                            {
                                                                                                                entryPointParam_fragmentMain = vec4(0.0, 0.0, 0.0, 1.0);
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                if (gathered)
                                                                                                                {
                                                                                                                    indirect = textureLod(_13767, input_uv, 0.0).xyz * gi.giParams.x;
                                                                                                                }
                                                                                                                float ambientVisibility = _5612.w;
                                                                                                                if (gi.giParams.z > 0.5)
                                                                                                                {
                                                                                                                    trust = ambientVisibility * textureLod(_13765, input_uv, 0.0).x;
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    trust = ambientVisibility;
                                                                                                                }
                                                                                                                vec3 indirect_1;
                                                                                                                if (trust < 1.0)
                                                                                                                {
                                                                                                                    vec3 _12497 = max(vec3(trust), ((((((albedo * 2.040400028228759765625) - vec3(0.3323999941349029541015625)) * trust) + (vec3(0.6417000293731689453125) - (albedo * 4.79510021209716796875))) * trust) + ((albedo * 2.755199909210205078125) + vec3(0.69029998779296875))) * trust);
                                                                                                                    if (gi.probeOrigin.w > 0.5)
                                                                                                                    {
                                                                                                                        lampGridded = _4967;
                                                                                                                    }
                                                                                                                    else
                                                                                                                    {
                                                                                                                        lampGridded = false;
                                                                                                                    }
                                                                                                                    if (lampGridded)
                                                                                                                    {
                                                                                                                        lampLight = mix(vec3(1.0), _12497, vec3(0.5));
                                                                                                                    }
                                                                                                                    else
                                                                                                                    {
                                                                                                                        lampLight = _12497;
                                                                                                                    }
                                                                                                                    indirect_1 = indirect * lampLight;
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    indirect_1 = indirect;
                                                                                                                }
                                                                                                                float _5214 = clamp(dot(normal, _2930), 9.9999997473787516355514526367188e-05, 1.0);
                                                                                                                vec3 _5220 = mix(vec3(0.07999999821186065673828125 * specularLevel), albedo, vec3(metallic));
                                                                                                                float _12500 = 1.0 - roughness;
                                                                                                                float _12511 = 1.03999996185302734375 * (((min(_12500 * _12500, exp2((-9.27999973297119140625) * _5214)) * _12500) + 0.0425000004470348358154296875) - (0.0274999998509883880615234375 * roughness));
                                                                                                                vec2 _12516 = vec2((1.03999996185302734375 - (0.572000026702880859375 * roughness)) - _12511, ((0.02199999988079071044921875 * roughness) - 0.039999999105930328369140625) + _12511);
                                                                                                                vec3 ambientSpecular_1 = ((indirect * 0.3183098733425140380859375) * ((_5220 * _12516.x) + vec3(clamp(50.0 * _5220.y, 0.0, 1.0) * _12516.y))) * clamp((pow(_5214 + trust, roughness * roughness) - 1.0) + trust, 0.0, 1.0);
                                                                                                                vec3 ambientSpecular;
                                                                                                                if (gi.shelterMap2.z > 0.0)
                                                                                                                {
                                                                                                                    ambientSpecular = ambientSpecular_1 * (1.0 - clamp((gi.shelterMap2.z - roughness) / max(gi.shelterMap2.z * 0.25, 0.001000000047497451305389404296875), 0.0, 1.0));
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    ambientSpecular = ambientSpecular_1;
                                                                                                                }
                                                                                                                vec3 ambientSpecular_2 = ambientSpecular * (1.0 - waterMask);
                                                                                                                vec3 bakedShare = (albedo * baked) * (light.sky.y * light.sunColourScale.w);
                                                                                                                if (gathered)
                                                                                                                {
                                                                                                                    vec3 _12529 = exp(vec3(-0.01024200022220611572265625, -0.01799800060689449310302734375, -0.03754000365734100341796875) * (_4115 * 0.001000000047497451305389404296875));
                                                                                                                    float _12530 = dot(_2097, worldRayToPixel);
                                                                                                                    float _12557 = 1.6400001049041748046875 + (_12530 * (-1.60000002384185791015625));
                                                                                                                    vec3 _12545 = ((((((diffuseColour * 0.3183098733425140380859375) * indirect_1) + ambientSpecular_2) + sunLight) + bakedShare) * _12529) + ((((vec3(0.00580199994146823883056640625, 0.0135580003261566162109375, 0.033100001513957977294921875) * (0.0596831031143665313720703125 * (1.0 + (_12530 * _12530)))) + vec3(0.0039960001595318317413330078125 * (0.359999954700469970703125 / ((12.56637096405029296875 * _12557) * sqrt(max(_12557, 9.9999999392252902907785028219223e-09)))))) * sunIlluminance) * ((vec3(1.0) - _12529) * vec3(97.637176513671875, 55.561725616455078125, 26.6382503509521484375)));
                                                                                                                    vec3 _12565;
                                                                                                                    {
                                                                                                                        if (gi.fog.z <= 0.0)
                                                                                                                        {
                                                                                                                            _12565 = _12545;
                                                                                                                        }
                                                                                                                        else
                                                                                                                        {
                                                                                                                            vec4 _12595 = textureLod(_13753, vec3(input_uv, clamp(((log2(max((_4115 * gi.fog.x) + gi.fog.y, 9.9999999747524270787835121154785e-07)) * gi.fog.z) - 1.0) * gi.fog.w, 0.0, 1.0)), 0.0);
                                                                                                                            _12565 = (_12545 * _12595.w) + _12595.xyz;
                                                                                                                        }
                                                                                                                    }
                                                                                                                    entryPointParam_fragmentMain = vec4(_12565, 1.0);
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    vec4 _12605 = vec4(normal, 1.0);
                                                                                                                    vec4 _12621 = _12605.xyzz * _12605.yzzx;
                                                                                                                    vec3 _12653 = exp(vec3(-0.01024200022220611572265625, -0.01799800060689449310302734375, -0.03754000365734100341796875) * (_4115 * 0.001000000047497451305389404296875));
                                                                                                                    float _12654 = dot(_2097, worldRayToPixel);
                                                                                                                    float _12681 = 1.6400001049041748046875 + (_12654 * (-1.60000002384185791015625));
                                                                                                                    vec3 _12669 = (((((((albedo * indirect_1) * light.sunColourScale.w) + (max(vec3(0.0), (vec3(dot(texelFetch(_13794, ivec2(0), 0), _12605), dot(texelFetch(_13794, ivec2(1, 0), 0), _12605), dot(texelFetch(_13794, ivec2(2, 0), 0), _12605)) + vec3(dot(texelFetch(_13794, ivec2(3, 0), 0), _12621), dot(texelFetch(_13794, ivec2(4, 0), 0), _12621), dot(texelFetch(_13794, ivec2(5, 0), 0), _12621))) + (texelFetch(_13794, ivec2(6, 0), 0).xyz * ((normal.x * normal.x) - (normal.y * normal.y)))) * diffuseColour)) + ambientSpecular_2) + sunLight) + bakedShare) * _12653) + ((((vec3(0.00580199994146823883056640625, 0.0135580003261566162109375, 0.033100001513957977294921875) * (0.0596831031143665313720703125 * (1.0 + (_12654 * _12654)))) + vec3(0.0039960001595318317413330078125 * (0.359999954700469970703125 / ((12.56637096405029296875 * _12681) * sqrt(max(_12681, 9.9999999392252902907785028219223e-09)))))) * sunIlluminance) * ((vec3(1.0) - _12653) * vec3(97.637176513671875, 55.561725616455078125, 26.6382503509521484375)));
                                                                                                                    vec3 _12689;
                                                                                                                    {
                                                                                                                        if (gi.fog.z <= 0.0)
                                                                                                                        {
                                                                                                                            _12689 = _12669;
                                                                                                                        }
                                                                                                                        else
                                                                                                                        {
                                                                                                                            vec4 _12719 = textureLod(_13753, vec3(input_uv, clamp(((log2(max((_4115 * gi.fog.x) + gi.fog.y, 9.9999999747524270787835121154785e-07)) * gi.fog.z) - 1.0) * gi.fog.w, 0.0, 1.0)), 0.0);
                                                                                                                            _12689 = (_12669 * _12719.w) + _12719.xyz;
                                                                                                                        }
                                                                                                                    }
                                                                                                                    entryPointParam_fragmentMain = vec4(_12689, 1.0);
                                                                                                                }
                                                                                                            }
                                                                                                        }
                                                                                                    }
                                                                                                    else
                                                                                                    {
                                                                                                        indirect = light.ambient.xyz;
                                                                                                        if (debugView == 9)
                                                                                                        {
                                                                                                            entryPointParam_fragmentMain = vec4(0.0, 0.0, 0.0, 1.0);
                                                                                                        }
                                                                                                        else
                                                                                                        {
                                                                                                            if (gathered)
                                                                                                            {
                                                                                                                indirect = textureLod(_13767, input_uv, 0.0).xyz * gi.giParams.x;
                                                                                                            }
                                                                                                            float ambientVisibility = _5612.w;
                                                                                                            if (gi.giParams.z > 0.5)
                                                                                                            {
                                                                                                                trust = ambientVisibility * textureLod(_13765, input_uv, 0.0).x;
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                trust = ambientVisibility;
                                                                                                            }
                                                                                                            vec3 indirect_1;
                                                                                                            if (trust < 1.0)
                                                                                                            {
                                                                                                                vec3 _12497 = max(vec3(trust), ((((((albedo * 2.040400028228759765625) - vec3(0.3323999941349029541015625)) * trust) + (vec3(0.6417000293731689453125) - (albedo * 4.79510021209716796875))) * trust) + ((albedo * 2.755199909210205078125) + vec3(0.69029998779296875))) * trust);
                                                                                                                if (gi.probeOrigin.w > 0.5)
                                                                                                                {
                                                                                                                    lampGridded = _4967;
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    lampGridded = false;
                                                                                                                }
                                                                                                                if (lampGridded)
                                                                                                                {
                                                                                                                    lampLight = mix(vec3(1.0), _12497, vec3(0.5));
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    lampLight = _12497;
                                                                                                                }
                                                                                                                indirect_1 = indirect * lampLight;
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                indirect_1 = indirect;
                                                                                                            }
                                                                                                            float _5214 = clamp(dot(normal, _2930), 9.9999997473787516355514526367188e-05, 1.0);
                                                                                                            vec3 _5220 = mix(vec3(0.07999999821186065673828125 * specularLevel), albedo, vec3(metallic));
                                                                                                            float _12500 = 1.0 - roughness;
                                                                                                            float _12511 = 1.03999996185302734375 * (((min(_12500 * _12500, exp2((-9.27999973297119140625) * _5214)) * _12500) + 0.0425000004470348358154296875) - (0.0274999998509883880615234375 * roughness));
                                                                                                            vec2 _12516 = vec2((1.03999996185302734375 - (0.572000026702880859375 * roughness)) - _12511, ((0.02199999988079071044921875 * roughness) - 0.039999999105930328369140625) + _12511);
                                                                                                            vec3 ambientSpecular_1 = ((indirect * 0.3183098733425140380859375) * ((_5220 * _12516.x) + vec3(clamp(50.0 * _5220.y, 0.0, 1.0) * _12516.y))) * clamp((pow(_5214 + trust, roughness * roughness) - 1.0) + trust, 0.0, 1.0);
                                                                                                            vec3 ambientSpecular;
                                                                                                            if (gi.shelterMap2.z > 0.0)
                                                                                                            {
                                                                                                                ambientSpecular = ambientSpecular_1 * (1.0 - clamp((gi.shelterMap2.z - roughness) / max(gi.shelterMap2.z * 0.25, 0.001000000047497451305389404296875), 0.0, 1.0));
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                ambientSpecular = ambientSpecular_1;
                                                                                                            }
                                                                                                            vec3 ambientSpecular_2 = ambientSpecular * (1.0 - waterMask);
                                                                                                            vec3 bakedShare = (albedo * baked) * (light.sky.y * light.sunColourScale.w);
                                                                                                            if (gathered)
                                                                                                            {
                                                                                                                vec3 _12529 = exp(vec3(-0.01024200022220611572265625, -0.01799800060689449310302734375, -0.03754000365734100341796875) * (_4115 * 0.001000000047497451305389404296875));
                                                                                                                float _12530 = dot(_2097, worldRayToPixel);
                                                                                                                float _12557 = 1.6400001049041748046875 + (_12530 * (-1.60000002384185791015625));
                                                                                                                vec3 _12545 = ((((((diffuseColour * 0.3183098733425140380859375) * indirect_1) + ambientSpecular_2) + sunLight) + bakedShare) * _12529) + ((((vec3(0.00580199994146823883056640625, 0.0135580003261566162109375, 0.033100001513957977294921875) * (0.0596831031143665313720703125 * (1.0 + (_12530 * _12530)))) + vec3(0.0039960001595318317413330078125 * (0.359999954700469970703125 / ((12.56637096405029296875 * _12557) * sqrt(max(_12557, 9.9999999392252902907785028219223e-09)))))) * sunIlluminance) * ((vec3(1.0) - _12529) * vec3(97.637176513671875, 55.561725616455078125, 26.6382503509521484375)));
                                                                                                                vec3 _12565;
                                                                                                                {
                                                                                                                    if (gi.fog.z <= 0.0)
                                                                                                                    {
                                                                                                                        _12565 = _12545;
                                                                                                                    }
                                                                                                                    else
                                                                                                                    {
                                                                                                                        vec4 _12595 = textureLod(_13753, vec3(input_uv, clamp(((log2(max((_4115 * gi.fog.x) + gi.fog.y, 9.9999999747524270787835121154785e-07)) * gi.fog.z) - 1.0) * gi.fog.w, 0.0, 1.0)), 0.0);
                                                                                                                        _12565 = (_12545 * _12595.w) + _12595.xyz;
                                                                                                                    }
                                                                                                                }
                                                                                                                entryPointParam_fragmentMain = vec4(_12565, 1.0);
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                vec4 _12605 = vec4(normal, 1.0);
                                                                                                                vec4 _12621 = _12605.xyzz * _12605.yzzx;
                                                                                                                vec3 _12653 = exp(vec3(-0.01024200022220611572265625, -0.01799800060689449310302734375, -0.03754000365734100341796875) * (_4115 * 0.001000000047497451305389404296875));
                                                                                                                float _12654 = dot(_2097, worldRayToPixel);
                                                                                                                float _12681 = 1.6400001049041748046875 + (_12654 * (-1.60000002384185791015625));
                                                                                                                vec3 _12669 = (((((((albedo * indirect_1) * light.sunColourScale.w) + (max(vec3(0.0), (vec3(dot(texelFetch(_13794, ivec2(0), 0), _12605), dot(texelFetch(_13794, ivec2(1, 0), 0), _12605), dot(texelFetch(_13794, ivec2(2, 0), 0), _12605)) + vec3(dot(texelFetch(_13794, ivec2(3, 0), 0), _12621), dot(texelFetch(_13794, ivec2(4, 0), 0), _12621), dot(texelFetch(_13794, ivec2(5, 0), 0), _12621))) + (texelFetch(_13794, ivec2(6, 0), 0).xyz * ((normal.x * normal.x) - (normal.y * normal.y)))) * diffuseColour)) + ambientSpecular_2) + sunLight) + bakedShare) * _12653) + ((((vec3(0.00580199994146823883056640625, 0.0135580003261566162109375, 0.033100001513957977294921875) * (0.0596831031143665313720703125 * (1.0 + (_12654 * _12654)))) + vec3(0.0039960001595318317413330078125 * (0.359999954700469970703125 / ((12.56637096405029296875 * _12681) * sqrt(max(_12681, 9.9999999392252902907785028219223e-09)))))) * sunIlluminance) * ((vec3(1.0) - _12653) * vec3(97.637176513671875, 55.561725616455078125, 26.6382503509521484375)));
                                                                                                                vec3 _12689;
                                                                                                                {
                                                                                                                    if (gi.fog.z <= 0.0)
                                                                                                                    {
                                                                                                                        _12689 = _12669;
                                                                                                                    }
                                                                                                                    else
                                                                                                                    {
                                                                                                                        vec4 _12719 = textureLod(_13753, vec3(input_uv, clamp(((log2(max((_4115 * gi.fog.x) + gi.fog.y, 9.9999999747524270787835121154785e-07)) * gi.fog.z) - 1.0) * gi.fog.w, 0.0, 1.0)), 0.0);
                                                                                                                        _12689 = (_12669 * _12719.w) + _12719.xyz;
                                                                                                                    }
                                                                                                                }
                                                                                                                entryPointParam_fragmentMain = vec4(_12689, 1.0);
                                                                                                            }
                                                                                                        }
                                                                                                    }
                                                                                                }
                                                                                                else
                                                                                                {
                                                                                                    indirect = light.ambient.xyz;
                                                                                                    if (debugView == 9)
                                                                                                    {
                                                                                                        entryPointParam_fragmentMain = vec4(0.0, 0.0, 0.0, 1.0);
                                                                                                    }
                                                                                                    else
                                                                                                    {
                                                                                                        if (gathered)
                                                                                                        {
                                                                                                            indirect = textureLod(_13767, input_uv, 0.0).xyz * gi.giParams.x;
                                                                                                        }
                                                                                                        float ambientVisibility = _5612.w;
                                                                                                        if (gi.giParams.z > 0.5)
                                                                                                        {
                                                                                                            trust = ambientVisibility * textureLod(_13765, input_uv, 0.0).x;
                                                                                                        }
                                                                                                        else
                                                                                                        {
                                                                                                            trust = ambientVisibility;
                                                                                                        }
                                                                                                        vec3 indirect_1;
                                                                                                        if (trust < 1.0)
                                                                                                        {
                                                                                                            vec3 _12497 = max(vec3(trust), ((((((albedo * 2.040400028228759765625) - vec3(0.3323999941349029541015625)) * trust) + (vec3(0.6417000293731689453125) - (albedo * 4.79510021209716796875))) * trust) + ((albedo * 2.755199909210205078125) + vec3(0.69029998779296875))) * trust);
                                                                                                            if (gi.probeOrigin.w > 0.5)
                                                                                                            {
                                                                                                                lampGridded = _4967;
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                lampGridded = false;
                                                                                                            }
                                                                                                            if (lampGridded)
                                                                                                            {
                                                                                                                lampLight = mix(vec3(1.0), _12497, vec3(0.5));
                                                                                                            }
                                                                                                            else
                                                                                                            {
                                                                                                                lampLight = _12497;
                                                                                                            }
                                                                                                            indirect_1 = indirect * lampLight;
                                                                                                        }
                                                                                                        else
                                                                                                        {
                                                                                                            indirect_1 = indirect;
                                                                                                        }
                                                                                                        float _5214 = clamp(dot(normal, _2930), 9.9999997473787516355514526367188e-05, 1.0);
                                                                                                        vec3 _5220 = mix(vec3(0.07999999821186065673828125 * specularLevel), albedo, vec3(metallic));
                                                                                                        float _12500 = 1.0 - roughness;
                                                                                                        float _12511 = 1.03999996185302734375 * (((min(_12500 * _12500, exp2((-9.27999973297119140625) * _5214)) * _12500) + 0.0425000004470348358154296875) - (0.0274999998509883880615234375 * roughness));
                                                                                                        vec2 _12516 = vec2((1.03999996185302734375 - (0.572000026702880859375 * roughness)) - _12511, ((0.02199999988079071044921875 * roughness) - 0.039999999105930328369140625) + _12511);
                                                                                                        vec3 ambientSpecular_1 = ((indirect * 0.3183098733425140380859375) * ((_5220 * _12516.x) + vec3(clamp(50.0 * _5220.y, 0.0, 1.0) * _12516.y))) * clamp((pow(_5214 + trust, roughness * roughness) - 1.0) + trust, 0.0, 1.0);
                                                                                                        vec3 ambientSpecular;
                                                                                                        if (gi.shelterMap2.z > 0.0)
                                                                                                        {
                                                                                                            ambientSpecular = ambientSpecular_1 * (1.0 - clamp((gi.shelterMap2.z - roughness) / max(gi.shelterMap2.z * 0.25, 0.001000000047497451305389404296875), 0.0, 1.0));
                                                                                                        }
                                                                                                        else
                                                                                                        {
                                                                                                            ambientSpecular = ambientSpecular_1;
                                                                                                        }
                                                                                                        vec3 ambientSpecular_2 = ambientSpecular * (1.0 - waterMask);
                                                                                                        vec3 bakedShare = (albedo * baked) * (light.sky.y * light.sunColourScale.w);
                                                                                                        if (gathered)
                                                                                                        {
                                                                                                            vec3 _12529 = exp(vec3(-0.01024200022220611572265625, -0.01799800060689449310302734375, -0.03754000365734100341796875) * (_4115 * 0.001000000047497451305389404296875));
                                                                                                            float _12530 = dot(_2097, worldRayToPixel);
                                                                                                            float _12557 = 1.6400001049041748046875 + (_12530 * (-1.60000002384185791015625));
                                                                                                            vec3 _12545 = ((((((diffuseColour * 0.3183098733425140380859375) * indirect_1) + ambientSpecular_2) + sunLight) + bakedShare) * _12529) + ((((vec3(0.00580199994146823883056640625, 0.0135580003261566162109375, 0.033100001513957977294921875) * (0.0596831031143665313720703125 * (1.0 + (_12530 * _12530)))) + vec3(0.0039960001595318317413330078125 * (0.359999954700469970703125 / ((12.56637096405029296875 * _12557) * sqrt(max(_12557, 9.9999999392252902907785028219223e-09)))))) * sunIlluminance) * ((vec3(1.0) - _12529) * vec3(97.637176513671875, 55.561725616455078125, 26.6382503509521484375)));
                                                                                                            vec3 _12565;
                                                                                                            {
                                                                                                                if (gi.fog.z <= 0.0)
                                                                                                                {
                                                                                                                    _12565 = _12545;
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    vec4 _12595 = textureLod(_13753, vec3(input_uv, clamp(((log2(max((_4115 * gi.fog.x) + gi.fog.y, 9.9999999747524270787835121154785e-07)) * gi.fog.z) - 1.0) * gi.fog.w, 0.0, 1.0)), 0.0);
                                                                                                                    _12565 = (_12545 * _12595.w) + _12595.xyz;
                                                                                                                }
                                                                                                            }
                                                                                                            entryPointParam_fragmentMain = vec4(_12565, 1.0);
                                                                                                        }
                                                                                                        else
                                                                                                        {
                                                                                                            vec4 _12605 = vec4(normal, 1.0);
                                                                                                            vec4 _12621 = _12605.xyzz * _12605.yzzx;
                                                                                                            vec3 _12653 = exp(vec3(-0.01024200022220611572265625, -0.01799800060689449310302734375, -0.03754000365734100341796875) * (_4115 * 0.001000000047497451305389404296875));
                                                                                                            float _12654 = dot(_2097, worldRayToPixel);
                                                                                                            float _12681 = 1.6400001049041748046875 + (_12654 * (-1.60000002384185791015625));
                                                                                                            vec3 _12669 = (((((((albedo * indirect_1) * light.sunColourScale.w) + (max(vec3(0.0), (vec3(dot(texelFetch(_13794, ivec2(0), 0), _12605), dot(texelFetch(_13794, ivec2(1, 0), 0), _12605), dot(texelFetch(_13794, ivec2(2, 0), 0), _12605)) + vec3(dot(texelFetch(_13794, ivec2(3, 0), 0), _12621), dot(texelFetch(_13794, ivec2(4, 0), 0), _12621), dot(texelFetch(_13794, ivec2(5, 0), 0), _12621))) + (texelFetch(_13794, ivec2(6, 0), 0).xyz * ((normal.x * normal.x) - (normal.y * normal.y)))) * diffuseColour)) + ambientSpecular_2) + sunLight) + bakedShare) * _12653) + ((((vec3(0.00580199994146823883056640625, 0.0135580003261566162109375, 0.033100001513957977294921875) * (0.0596831031143665313720703125 * (1.0 + (_12654 * _12654)))) + vec3(0.0039960001595318317413330078125 * (0.359999954700469970703125 / ((12.56637096405029296875 * _12681) * sqrt(max(_12681, 9.9999999392252902907785028219223e-09)))))) * sunIlluminance) * ((vec3(1.0) - _12653) * vec3(97.637176513671875, 55.561725616455078125, 26.6382503509521484375)));
                                                                                                            vec3 _12689;
                                                                                                            {
                                                                                                                if (gi.fog.z <= 0.0)
                                                                                                                {
                                                                                                                    _12689 = _12669;
                                                                                                                }
                                                                                                                else
                                                                                                                {
                                                                                                                    vec4 _12719 = textureLod(_13753, vec3(input_uv, clamp(((log2(max((_4115 * gi.fog.x) + gi.fog.y, 9.9999999747524270787835121154785e-07)) * gi.fog.z) - 1.0) * gi.fog.w, 0.0, 1.0)), 0.0);
                                                                                                                    _12689 = (_12669 * _12719.w) + _12719.xyz;
                                                                                                                }
                                                                                                            }
                                                                                                            entryPointParam_fragmentMain = vec4(_12689, 1.0);
                                                                                                        }
                                                                                                    }
                                                                                                }
                                                                                            }
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

