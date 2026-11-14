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
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

struct _Array_std140_vector_float_4_4
{
    vec4 data[4];
};

struct _Array_std140_float4
{
    float data[4];
};

uniform vec2 _1081_comp[4] = vec2[](vec2(0.0), vec2(0.430000007152557373046875, 0.709999978542327880859375), vec2(0.790000021457672119140625, 0.23999999463558197021484375), vec2(0.17000000178813934326171875, 0.4600000083446502685546875));

layout(binding = 0, std140) uniform Params_std140
{
    layout(row_major) mat4 viewToWorld;
    vec4 unproject;
    vec4 resolution;
    vec4 ssr;
    vec4 misc;
    vec4 wet0;
    vec4 wet1;
    vec4 wet2;
    vec4 shelter0;
    vec4 shelter1;
    vec4 shelter2;
    vec4 trace;
    vec4 cacheOrigin;
    vec4 cacheGrid;
    vec4 cacheAtlas;
    vec4 trace2;
    vec4 field;
    _Array_std140_vector_float_4_4 sdfOrigin;
    _Array_std140_float4 sdfRange;
} p;

layout(binding = 1, std430) restrict readonly buffer StructuredBuffer
{
    ivec2 _member0[];
} uCells;

layout(binding = 2, std430) restrict readonly buffer uCellPages
{
    int _member0[];
} uCellPages_1;

layout(binding = 0, std430) restrict readonly buffer uPages
{
    vec4 _member0[];
} uPages_1;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uReflection;
layout(binding = 2) uniform sampler2D _7102;
layout(binding = 4) uniform sampler2D _7104;
layout(binding = 3) uniform sampler2D _7106;
layout(binding = 7) uniform sampler2D _7109;
layout(binding = 8) uniform sampler2D _7111;
layout(binding = 1) uniform sampler2D _7114;
layout(binding = 0) uniform sampler2D _7116;
layout(binding = 5) uniform sampler2D _7119;
layout(binding = 6) uniform sampler2D _7121;
layout(binding = 13) uniform sampler3D _7123;
layout(binding = 9) uniform sampler2D _7126;
layout(binding = 10) uniform sampler2D _7128;
layout(binding = 11) uniform sampler2D _7130;
layout(binding = 12) uniform sampler2D _7132;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    bool _5948 = false;
    bool _5790 = false;
    bool _5632 = false;
    bool _5521 = false;
    {
        ivec2 _233 = ivec2(gl_GlobalInvocationID.xy);
        bool marches;
        if (_233.x >= int(p.resolution.x))
        {
            marches = true;
        }
        else
        {
            marches = _233.y >= int(p.resolution.y);
        }
        if (!(marches))
        {
            vec2 _266 = vec2(_233);
            vec2 uv = (_266 + vec2(0.5)) * p.resolution.zw;
            bool raw = p.wet2.z > 0.5;
            vec4 _3644 = textureLod(_7102, uv, 0.0);
            float _3645 = _3644.x;
            vec4 sampled = textureLod(_7104, uv, 0.0);
            if (_3645 <= 0.0)
            {
                marches = true;
            }
            else
            {
                marches = (int((sampled.w * 255.0) + 0.5) & 15) == 0;
            }
            if (marches)
            {
                marches = true;
            }
            else
            {
                marches = _3645 >= 0.89999997615814208984375;
            }
            if (marches)
            {
                marches = true;
            }
            else
            {
                marches = (int((sampled.w * 255.0) + 0.5) & 16) != 0;
            }
            if (marches)
            {
                imageStore(uReflection, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
            }
            else
            {
                bool traces = p.trace.x > 0.5;
                bool _365 = !traces;
                if (_365)
                {
                    marches = p.field.x > 0.5;
                }
                else
                {
                    marches = false;
                }
                vec3 _389 = normalize((textureLod(_7106, uv, 0.0).xyz * 2.0) - vec3(1.0));
                float roughness = sampled.z;
                float _3682 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _3645)) * (p.unproject.w - p.unproject.z)));
                vec2 _3659 = (uv * 2.0) - vec2(1.0);
                vec3 _3669 = vec3((_3659.x * _3682) / p.unproject.x, ((-_3659.y) * _3682) / p.unproject.y, _3682);
                vec3 _447 = normalize(_3669 * mat3(spvWorkaroundRowMajor(p.viewToWorld)[0].xyz, spvWorkaroundRowMajor(p.viewToWorld)[1].xyz, spvWorkaroundRowMajor(p.viewToWorld)[2].xyz));
                vec3 _448 = -_447;
                float _450 = max(dot(_389, _448), 0.001000000047497451305389404296875);
                vec3 _460 = _3669 * mat3(spvWorkaroundRowMajor(p.viewToWorld)[0].xyz, spvWorkaroundRowMajor(p.viewToWorld)[1].xyz, spvWorkaroundRowMajor(p.viewToWorld)[2].xyz);
                float _3694 = max(p.misc.x, 0.0);
                bool screenAllowed;
                if (_3694 > 0.0)
                {
                    screenAllowed = _389.z > 0.977999985218048095703125;
                }
                else
                {
                    screenAllowed = false;
                }
                if (screenAllowed)
                {
                    bool _3716;
                    {
                        ivec2 _3790 = ivec2(floor(((_460.xy + p.wet1.zw) - p.shelter0.xy) * p.shelter0.z));
                        vec2 _6971 = p.shelter2.xy;
                        bool _3791;
                        if (p.wet2.x > 0.5)
                        {
                            _3791 = _3790.x >= 0;
                        }
                        else
                        {
                            _3791 = false;
                        }
                        if (_3791)
                        {
                            _3791 = _3790.y >= 0;
                        }
                        else
                        {
                            _3791 = false;
                        }
                        if (_3791)
                        {
                            _3791 = _3790.x < int(_6971.x);
                        }
                        else
                        {
                            _3791 = false;
                        }
                        if (_3791)
                        {
                            _3791 = _3790.y < int(_6971.y);
                        }
                        else
                        {
                            _3791 = false;
                        }
                        if (!_3791)
                        {
                            _3716 = false;
                        }
                        else
                        {
                            vec4 _3738 = texelFetch(_7109, ivec3(_3790, 0).xy, 0);
                            float _6975 = p.shelter0.w;
                            float _6976 = p.shelter1.x;
                            float _3841 = floor((_3738.z * 255.0) + 0.5) + (floor((_3738.y * 255.0) + 0.5) * 256.0);
                            float _3829;
                            if (_3841 < 0.5)
                            {
                                _3829 = -1000000015047466219876688855040.0;
                            }
                            else
                            {
                                _3829 = _6975 + ((_3841 - 1.0) * _6976);
                            }
                            float _3745 = _460.z + p.wet2.y;
                            float _6980 = p.shelter1.z;
                            bool _3854;
                            if (_3829 > (_3745 + p.shelter1.y))
                            {
                                _3854 = _3829 < (_3745 + _6980);
                            }
                            else
                            {
                                _3854 = false;
                            }
                            _3716 = _3854;
                        }
                    }
                    screenAllowed = !_3716;
                }
                else
                {
                    screenAllowed = false;
                }
                float puddle;
                if (screenAllowed)
                {
                    float _3883 = max(p.wet0.y, 0.0500000007450580596923828125);
                    float _3891 = _3883 * 0.4600000083446502685546875;
                    vec2 _3916 = vec2((0.800000011920928955078125 * p.wet1.zw.x) - (0.60000002384185791015625 * p.wet1.zw.y), (0.60000002384185791015625 * p.wet1.zw.x) + (0.800000011920928955078125 * p.wet1.zw.y)) / vec2(_3891);
                    vec2 _3917 = floor(_3916);
                    float _3943 = _460.x;
                    float _3946 = _460.y;
                    vec2 _3895 = ((vec2((0.800000011920928955078125 * _3943) - (0.60000002384185791015625 * _3946), (0.60000002384185791015625 * _3943) + (0.800000011920928955078125 * _3946)) / vec2(_3891)) + (_3916 - _3917)) + vec2(11.30000019073486328125);
                    vec2 _3955 = floor(_3895);
                    vec2 _3956 = _3895 - _3955;
                    vec2 _3961 = (_3956 * _3956) * (vec2(3.0) - (_3956 * 2.0));
                    vec2 _3962 = _3955 + _3917;
                    uvec2 _3980 = uvec2(ivec2(_3962) + ivec2(1048576));
                    uint _3985 = (_3980.x * 73856093u) ^ (_3980.y * 19349663u);
                    uint _3988 = (_3985 ^ (_3985 >> uint(13))) * 1540483477u;
                    uvec2 _3999 = uvec2(ivec2(_3962 + vec2(1.0, 0.0)) + ivec2(1048576));
                    uint _4004 = (_3999.x * 73856093u) ^ (_3999.y * 19349663u);
                    uint _4007 = (_4004 ^ (_4004 >> uint(13))) * 1540483477u;
                    uvec2 _4018 = uvec2(ivec2(_3962 + vec2(0.0, 1.0)) + ivec2(1048576));
                    uint _4023 = (_4018.x * 73856093u) ^ (_4018.y * 19349663u);
                    uint _4026 = (_4023 ^ (_4023 >> uint(13))) * 1540483477u;
                    uvec2 _4037 = uvec2(ivec2(_3962 + vec2(1.0)) + ivec2(1048576));
                    uint _4042 = (_4037.x * 73856093u) ^ (_4037.y * 19349663u);
                    uint _4045 = (_4042 ^ (_4042 >> uint(13))) * 1540483477u;
                    float _3970 = _3961.x;
                    float _3974 = mix(mix(float((_3988 ^ (_3988 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_4007 ^ (_4007 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _3970), mix(float((_4026 ^ (_4026 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_4045 ^ (_4045 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _3970), _3961.y);
                    vec2 _4061 = p.wet1.zw / vec2(_3883);
                    vec2 _4062 = floor(_4061);
                    vec2 _3903 = ((vec2(_460.xy) / vec2(_3883)) + (_4061 - _4062)) + vec2((_3974 - 0.5) * 0.4000000059604644775390625);
                    vec2 _4100 = floor(_3903);
                    vec2 _4101 = _3903 - _4100;
                    vec2 _4106 = (_4101 * _4101) * (vec2(3.0) - (_4101 * 2.0));
                    vec2 _4107 = _4100 + _4062;
                    uvec2 _4125 = uvec2(ivec2(_4107) + ivec2(1048576));
                    uint _4130 = (_4125.x * 73856093u) ^ (_4125.y * 19349663u);
                    uint _4133 = (_4130 ^ (_4130 >> uint(13))) * 1540483477u;
                    uvec2 _4144 = uvec2(ivec2(_4107 + vec2(1.0, 0.0)) + ivec2(1048576));
                    uint _4149 = (_4144.x * 73856093u) ^ (_4144.y * 19349663u);
                    uint _4152 = (_4149 ^ (_4149 >> uint(13))) * 1540483477u;
                    uvec2 _4163 = uvec2(ivec2(_4107 + vec2(0.0, 1.0)) + ivec2(1048576));
                    uint _4168 = (_4163.x * 73856093u) ^ (_4163.y * 19349663u);
                    uint _4171 = (_4168 ^ (_4168 >> uint(13))) * 1540483477u;
                    uvec2 _4182 = uvec2(ivec2(_4107 + vec2(1.0)) + ivec2(1048576));
                    uint _4187 = (_4182.x * 73856093u) ^ (_4182.y * 19349663u);
                    uint _4190 = (_4187 ^ (_4187 >> uint(13))) * 1540483477u;
                    float _4115 = _4106.x;
                    puddle = (clamp((clamp(p.wet0.x, 0.0, 1.0) - ((mix(mix(float((_4133 ^ (_4133 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_4152 ^ (_4152 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _4115), mix(float((_4171 ^ (_4171 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_4190 ^ (_4190 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _4115), _4106.y) * 0.64999997615814208984375) + (_3974 * 0.3499999940395355224609375))) * 20.0, 0.0, 1.0) * clamp((_389.z - 0.977999985218048095703125) * 60.0, 0.0, 1.0)) * _3694;
                }
                else
                {
                    puddle = 0.0;
                }
                float _942 = puddle;
                bool water = _942 > 0.00999999977648258209228515625;
                float ceiling;
                float fade;
                vec2 facetNoise;
                vec3 up;
                vec3 normalW;
                if (water)
                {
                    vec2 _7033 = p.wet1.zw;
                    float _7036 = p.wet1.y;
                    float _4329 = max(p.wet1.x, 0.0199999995529651641845703125);
                    float _4332 = p.wet0.w * 0.60000002384185791015625;
                    int _4369 = 0;
                    vec2 _4370 = vec2(0.0);
                    vec2 _4473;
                    vec2 _4478;
                    SPIRV_CROSS_UNROLL
                    for (;;)
                    {
                        bool _4380_ladder_break = false;
                        {
                            if (!(_4369 < 4))
                            {
                                _4380_ladder_break = true;
                            }
                            else
                            {
                                vec4 _4371 = vec4(0.0, 0.2199999988079071044921875, 0.439999997615814208984375, 0.660000026226043701171875);
                                float _4391 = clamp((p.wet0.z - _4371[_4369]) * 4.0, 0.0, 1.0);
                                if (!(_4391 <= 0.0))
                                {
                                    vec4 _4372 = vec4(0.519999980926513671875, 0.7400000095367431640625, 0.9700000286102294921875, 1.2799999713897705078125);
                                    float _4398 = _4329 * _4372[_4369];
                                    vec2 _4435 = _7033 / vec2(_4398);
                                    vec2 _4436 = floor(_4435);
                                    vec4 _4373 = vec4(0.959999978542327880859375, 0.810000002384185791015625, 0.88999998569488525390625, 0.7200000286102294921875);
                                    vec4 _4374 = vec4(0.0, 0.310000002384185791015625, 0.579999983310699462890625, 0.829999983310699462890625);
                                    float _4408 = fract((_4332 * _4373[_4369]) + _4374[_4369]);
                                    vec2 _4413 = ((vec2(_460.xy) / vec2(_4398)) + (_4435 - _4436)) + _1081_comp[_4369];
                                    {
                                        if (_4391 <= 0.0)
                                        {
                                            _4473 = vec2(0.0);
                                        }
                                        else
                                        {
                                            float _4486 = mix(0.20000000298023223876953125, 1.0, clamp(_4391, 0.0, 1.0));
                                            vec2 _4489 = floor(_4413 - vec2(0.5));
                                            int _4474 = 0;
                                            vec2 _4475 = vec2(0.0);
                                            for (;;)
                                            {
                                                if (!(_4474 < 2))
                                                {
                                                    break;
                                                }
                                                int _4476 = 0;
                                                vec2 _4477 = _4475;
                                                for (;;)
                                                {
                                                    bool _4501_ladder_break = false;
                                                    {
                                                        if (!(_4476 < 2))
                                                        {
                                                            _4501_ladder_break = true;
                                                        }
                                                        else
                                                        {
                                                            vec2 _4512 = _4489 + vec2(float(_4476), float(_4474));
                                                            vec2 _4515 = _4413 - (_4512 + vec2(0.5));
                                                            if (!(dot(_4515, _4515) > 0.608399927616119384765625))
                                                            {
                                                                uvec2 _4575 = uvec2(ivec2(_4512 + _4436) + ivec2(1048576));
                                                                uint _4580 = (_4575.x * 73856093u) ^ (_4575.y * 19349663u);
                                                                uint _4583 = (_4580 ^ (_4580 >> uint(13))) * 1540483477u;
                                                                uint _4585 = _4583 ^ (_4583 >> uint(15));
                                                                uint _4586 = _4585 * 668265261u;
                                                                uint _4588 = _4586 ^ (_4586 >> uint(15));
                                                                uint _4589 = _4588 * 2246822507u;
                                                                vec3 _4599 = vec3(float(_4585 & 16777215u), float(_4588 & 16777215u), float((_4589 ^ (_4589 >> uint(13))) & 16777215u)) * 5.9604651880817982601001858711243e-08;
                                                                float _4524 = fract(_4408 + _4599.x);
                                                                vec2 _4529 = _4515 - ((_4599.yz - vec2(0.5)) * 0.5);
                                                                float _4530 = length(_4529);
                                                                if (_4530 > 9.9999997473787516355514526367188e-06)
                                                                {
                                                                    _4478 = _4529 / vec2(_4530);
                                                                }
                                                                else
                                                                {
                                                                    _4478 = vec2(0.0);
                                                                }
                                                                _4477 -= (_4478 * ((sin(3.1415927410125732421875 * clamp((_4524 - clamp(_4530 * 2.17391300201416015625, 0.0, 1.0)) * 24.0, 0.0, 4.0)) * clamp(_4486 - _4524, 0.0, 1.0)) * 0.449999988079071044921875));
                                                            }
                                                        }
                                                    }
                                                    if (_4501_ladder_break)
                                                    {
                                                        break;
                                                    }
                                                    _4476++;
                                                    continue;
                                                }
                                                _4474++;
                                                _4475 = _4477;
                                                continue;
                                            }
                                            _4473 = _4475;
                                        }
                                    }
                                    _4370 += _4473;
                                }
                            }
                        }
                        if (_4380_ladder_break)
                        {
                            break;
                        }
                        _4369++;
                        continue;
                    }
                    vec2 _4627 = _7033 * vec2(0.5555555820465087890625);
                    vec2 _4628 = floor(_4627);
                    vec2 _4607 = ((vec2(_460.xy) * vec2(0.5555555820465087890625)) + (_4627 - _4628)) + vec2(p.wet0.w * 0.07999999821186065673828125, p.wet0.w * 0.0500000007450580596923828125);
                    vec2 _4666 = floor(_4607);
                    vec2 _4667 = _4607 - _4666;
                    vec2 _4672 = (_4667 * _4667) * (vec2(3.0) - (_4667 * 2.0));
                    vec2 _4673 = _4666 + _4628;
                    uvec2 _4691 = uvec2(ivec2(_4673) + ivec2(1048576));
                    uint _4696 = (_4691.x * 73856093u) ^ (_4691.y * 19349663u);
                    uint _4699 = (_4696 ^ (_4696 >> uint(13))) * 1540483477u;
                    uvec2 _4710 = uvec2(ivec2(_4673 + vec2(1.0, 0.0)) + ivec2(1048576));
                    uint _4715 = (_4710.x * 73856093u) ^ (_4710.y * 19349663u);
                    uint _4718 = (_4715 ^ (_4715 >> uint(13))) * 1540483477u;
                    uvec2 _4729 = uvec2(ivec2(_4673 + vec2(0.0, 1.0)) + ivec2(1048576));
                    uint _4734 = (_4729.x * 73856093u) ^ (_4729.y * 19349663u);
                    uint _4737 = (_4734 ^ (_4734 >> uint(13))) * 1540483477u;
                    uvec2 _4748 = uvec2(ivec2(_4673 + vec2(1.0)) + ivec2(1048576));
                    uint _4753 = (_4748.x * 73856093u) ^ (_4748.y * 19349663u);
                    uint _4756 = (_4753 ^ (_4753 >> uint(13))) * 1540483477u;
                    float _4681 = _4672.x;
                    float _4685 = mix(mix(float((_4699 ^ (_4699 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_4718 ^ (_4718 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _4681), mix(float((_4737 ^ (_4737 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_4756 ^ (_4756 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _4681), _4672.y);
                    vec2 _4610 = _4607 + vec2(0.1500000059604644775390625, 0.0);
                    vec2 _4764 = floor(_4610);
                    vec2 _4765 = _4610 - _4764;
                    vec2 _4770 = (_4765 * _4765) * (vec2(3.0) - (_4765 * 2.0));
                    vec2 _4771 = _4764 + _4628;
                    uvec2 _4789 = uvec2(ivec2(_4771) + ivec2(1048576));
                    uint _4794 = (_4789.x * 73856093u) ^ (_4789.y * 19349663u);
                    uint _4797 = (_4794 ^ (_4794 >> uint(13))) * 1540483477u;
                    uvec2 _4808 = uvec2(ivec2(_4771 + vec2(1.0, 0.0)) + ivec2(1048576));
                    uint _4813 = (_4808.x * 73856093u) ^ (_4808.y * 19349663u);
                    uint _4816 = (_4813 ^ (_4813 >> uint(13))) * 1540483477u;
                    uvec2 _4827 = uvec2(ivec2(_4771 + vec2(0.0, 1.0)) + ivec2(1048576));
                    uint _4832 = (_4827.x * 73856093u) ^ (_4827.y * 19349663u);
                    uint _4835 = (_4832 ^ (_4832 >> uint(13))) * 1540483477u;
                    uvec2 _4846 = uvec2(ivec2(_4771 + vec2(1.0)) + ivec2(1048576));
                    uint _4851 = (_4846.x * 73856093u) ^ (_4846.y * 19349663u);
                    uint _4854 = (_4851 ^ (_4851 >> uint(13))) * 1540483477u;
                    float _4779 = _4770.x;
                    vec2 _4613 = _4607 + vec2(0.0, 0.1500000059604644775390625);
                    vec2 _4862 = floor(_4613);
                    vec2 _4863 = _4613 - _4862;
                    vec2 _4868 = (_4863 * _4863) * (vec2(3.0) - (_4863 * 2.0));
                    vec2 _4869 = _4862 + _4628;
                    uvec2 _4887 = uvec2(ivec2(_4869) + ivec2(1048576));
                    uint _4892 = (_4887.x * 73856093u) ^ (_4887.y * 19349663u);
                    uint _4895 = (_4892 ^ (_4892 >> uint(13))) * 1540483477u;
                    uvec2 _4906 = uvec2(ivec2(_4869 + vec2(1.0, 0.0)) + ivec2(1048576));
                    uint _4911 = (_4906.x * 73856093u) ^ (_4906.y * 19349663u);
                    uint _4914 = (_4911 ^ (_4911 >> uint(13))) * 1540483477u;
                    uvec2 _4925 = uvec2(ivec2(_4869 + vec2(0.0, 1.0)) + ivec2(1048576));
                    uint _4930 = (_4925.x * 73856093u) ^ (_4925.y * 19349663u);
                    uint _4933 = (_4930 ^ (_4930 >> uint(13))) * 1540483477u;
                    uvec2 _4944 = uvec2(ivec2(_4869 + vec2(1.0)) + ivec2(1048576));
                    uint _4949 = (_4944.x * 73856093u) ^ (_4944.y * 19349663u);
                    uint _4952 = (_4949 ^ (_4949 >> uint(13))) * 1540483477u;
                    float _4877 = _4868.x;
                    vec2 _4340 = (vec2(mix(mix(float((_4797 ^ (_4797 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_4816 ^ (_4816 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _4779), mix(float((_4835 ^ (_4835 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_4854 ^ (_4854 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _4779), _4770.y) - _4685, mix(mix(float((_4895 ^ (_4895 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_4914 ^ (_4914 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _4877), mix(float((_4933 ^ (_4933 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, float((_4952 ^ (_4952 >> uint(15))) & 16777215u) * 5.9604651880817982601001858711243e-08, _4877), _4868.y) - _4685) * _7036) * 1.60000002384185791015625;
                    float _4344 = clamp(((abs((2.0 * p.resolution.z) * _3669.z) / max(p.unproject.x, 9.9999997473787516355514526367188e-05)) / max(_450, 0.07999999821186065673828125)) / (_4329 * 0.5), 0.0, 1.0);
                    vec3 _1348 = normalize(mix(_389, normalize(vec3((_4370 + _4340) * (1.0 - _4344), 1.0)), vec3(puddle)));
                    float _4962 = 1.0 - clamp(dot(_1348, _448), 0.0, 1.0);
                    float _4963 = _4962 * _4962;
                    up = vec3((((0.0199999995529651641845703125 + (((0.980000019073486328125 * _4963) * _4963) * _4962)) * puddle) * clamp(1.0 - (_4344 * 2.0), 0.0, 1.0)) * (1.0 - (0.75 * clamp(_3694 * 5.0, 0.0, 1.0))));
                    normalW = _1348;
                    screenAllowed = true;
                    if (max(up.x, max(up.y, up.z)) <= 0.0030000000260770320892333984375)
                    {
                        imageStore(uReflection, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
                    }
                    else
                    {
                        vec3 _1736 = reflect(normalize(_3669), normalize(normalW * transpose(mat3(spvWorkaroundRowMajor(p.viewToWorld)[0].xyz, spvWorkaroundRowMajor(p.viewToWorld)[1].xyz, spvWorkaroundRowMajor(p.viewToWorld)[2].xyz))));
                        bool have;
                        if (_365)
                        {
                            have = _1736.z > 0.0;
                        }
                        else
                        {
                            have = false;
                        }
                        if (!have)
                        {
                            screenAllowed = false;
                        }
                        int _1753 = max(int(p.ssr.x + 0.5), 1);
                        vec2 _4985 = _266 + (vec2(12.0780429840087890625, 9.1174449920654296875) * p.misc.y);
                        have = false;
                        ceiling = -1.0;
                        vec3 previous = _3669;
                        int i = 0;
                        fade = 0.20000000298023223876953125 * (0.5 + fract(52.98291778564453125 * fract((0.067110560834407806396484375 * _4985.x) + (0.005837149918079376220703125 * _4985.y))));
                        bool crossed;
                        float hitEdge;
                        vec3 a;
                        vec3 b;
                        bool met;
                        bool _36;
                        float _5160;
                        for (;;)
                        {
                            vec3 pv;
                            float t;
                            bool _116_ladder_break = false;
                            {
                                if (screenAllowed)
                                {
                                    crossed = i < _1753;
                                }
                                else
                                {
                                    crossed = false;
                                }
                                if (!crossed)
                                {
                                    hitEdge = 0.0;
                                    facetNoise = vec2(0.0);
                                    _116_ladder_break = true;
                                }
                                else
                                {
                                    pv = _3669 + (_1736 * fade);
                                    if (pv.z <= p.unproject.z)
                                    {
                                        met = true;
                                    }
                                    else
                                    {
                                        met = fade > p.misc.z;
                                    }
                                    if (met)
                                    {
                                        hitEdge = 0.0;
                                        facetNoise = vec2(0.0);
                                        _116_ladder_break = true;
                                    }
                                    else
                                    {
                                        t = fade + max(0.20000000298023223876953125, fade * 0.07999999821186065673828125);
                                        vec2 _5015 = (vec2((pv.x * p.unproject.x) / pv.z, ((-pv.y) * p.unproject.y) / pv.z) * 0.5) + vec2(0.5);
                                        if (any(lessThan(_5015, vec2(0.0))))
                                        {
                                            _36 = true;
                                        }
                                        else
                                        {
                                            _36 = any(greaterThan(_5015, vec2(1.0)));
                                        }
                                        if (_36)
                                        {
                                            hitEdge = 0.0;
                                            facetNoise = vec2(0.0);
                                            _116_ladder_break = true;
                                        }
                                        else
                                        {
                                            uvec2 _5021 = uvec2(textureSize(_7114, 0));
                                            uint _5022 = _5021.x;
                                            uint _5023 = _5021.y;
                                            vec4 _5042 = texelFetch(_7114, ivec3(clamp(ivec2(_5015 * vec2(float(_5022), float(_5023))), ivec2(0), ivec2(int(_5022) - 1, int(_5023) - 1)), 0).xy, 0);
                                            float _5043 = _5042.x;
                                            if (_5043 <= 0.0)
                                            {
                                                ceiling = -1.0;
                                            }
                                            else
                                            {
                                                float diff = pv.z - (((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _5043)) * (p.unproject.w - p.unproject.z))));
                                                if (diff > 0.0)
                                                {
                                                    float _1919 = max(p.ssr.w, pv.z * 0.0500000007450580596923828125);
                                                    if (have)
                                                    {
                                                        crossed = ceiling < 0.0;
                                                    }
                                                    else
                                                    {
                                                        crossed = false;
                                                    }
                                                    if (crossed)
                                                    {
                                                        screenAllowed = true;
                                                    }
                                                    else
                                                    {
                                                        screenAllowed = diff < _1919;
                                                    }
                                                    if (screenAllowed)
                                                    {
                                                        a = previous;
                                                        b = pv;
                                                        int k = 0;
                                                        for (;;)
                                                        {
                                                            if (!(k < 5))
                                                            {
                                                                break;
                                                            }
                                                            vec3 _1945 = b;
                                                            vec3 mid = (a + _1945) * 0.5;
                                                            float _5075 = mid.z;
                                                            uvec2 _5092 = uvec2(textureSize(_7114, 0));
                                                            uint _5093 = _5092.x;
                                                            uint _5094 = _5092.y;
                                                            vec4 _5113 = texelFetch(_7114, ivec3(clamp(ivec2(((vec2((mid.x * p.unproject.x) / _5075, ((-mid.y) * p.unproject.y) / _5075) * 0.5) + vec2(0.5)) * vec2(float(_5093), float(_5094))), ivec2(0), ivec2(int(_5093) - 1, int(_5094) - 1)), 0).xy, 0);
                                                            float _5114 = _5113.x;
                                                            if (_5114 > 0.0)
                                                            {
                                                                screenAllowed = (mid.z - (((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _5114)) * (p.unproject.w - p.unproject.z))))) > 0.0;
                                                            }
                                                            else
                                                            {
                                                                screenAllowed = false;
                                                            }
                                                            if (screenAllowed)
                                                            {
                                                                b = mid;
                                                            }
                                                            else
                                                            {
                                                                a = mid;
                                                            }
                                                            k++;
                                                            continue;
                                                        }
                                                        vec2 _5157 = (vec2((b.x * p.unproject.x) / b.z, ((-b.y) * p.unproject.y) / b.z) * 0.5) + vec2(0.5);
                                                        uvec2 _5164 = uvec2(textureSize(_7114, 0));
                                                        uint _5165 = _5164.x;
                                                        uint _5166 = _5164.y;
                                                        vec4 _5185 = texelFetch(_7114, ivec3(clamp(ivec2(_5157 * vec2(float(_5165), float(_5166))), ivec2(0), ivec2(int(_5165) - 1, int(_5166) - 1)), 0).xy, 0);
                                                        float _5186 = _5185.x;
                                                        if (_5186 > 0.0)
                                                        {
                                                            _5160 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _5186)) * (p.unproject.w - p.unproject.z)));
                                                        }
                                                        else
                                                        {
                                                            _5160 = 0.0;
                                                        }
                                                        if (_5160 > 0.0)
                                                        {
                                                            met = (b.z - _5160) < _1919;
                                                        }
                                                        else
                                                        {
                                                            met = false;
                                                        }
                                                        vec2 _2037 = clamp((abs((_5157 * 2.0) - vec2(1.0)) * 5.0) - vec2(4.0), vec2(0.0), vec2(1.0));
                                                        if (met)
                                                        {
                                                            puddle = clamp(1.0 - dot(_2037, _2037), 0.0, 1.0);
                                                        }
                                                        else
                                                        {
                                                            puddle = 0.0;
                                                        }
                                                        hitEdge = puddle;
                                                        facetNoise = _5157;
                                                    }
                                                    else
                                                    {
                                                        hitEdge = 0.0;
                                                        facetNoise = vec2(0.0);
                                                    }
                                                    _116_ladder_break = true;
                                                }
                                                else
                                                {
                                                    ceiling = diff;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            if (_116_ladder_break)
                            {
                                break;
                            }
                            have = true;
                            previous = pv;
                            i++;
                            fade = t;
                            continue;
                        }
                        float screenWeight;
                        if (hitEdge > 0.0)
                        {
                            vec4 sampled_1 = textureLod(_7116, facetNoise, 0.0);
                            if (water)
                            {
                                puddle = 1.0;
                            }
                            else
                            {
                                puddle = p.ssr.y;
                            }
                            if (water)
                            {
                                vec3 _2097 = normalize(reflect(_447, normalW));
                                float _5231 = acos(clamp(sqrt(max(0.0, (p.misc.w * p.misc.w) - 40449600.0)) / p.misc.w, -1.0, 1.0));
                                float _5232 = 3.1415927410125732421875 - _5231;
                                float _5235 = acos(clamp(_2097.z, -1.0, 1.0));
                                float _5221;
                                if (_5235 < _5232)
                                {
                                    _5221 = (1.0 - sqrt(max(0.0, 1.0 - (_5235 / _5232)))) * 0.5;
                                }
                                else
                                {
                                    _5221 = (sqrt(max(0.0, (_5235 - _5232) / max(_5231, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                }
                                float _5255 = atan(_2097.y, _2097.x);
                                float _5222;
                                if (_5255 < 0.0)
                                {
                                    _5222 = _5255 + 6.283185482025146484375;
                                }
                                else
                                {
                                    _5222 = _5255;
                                }
                                uvec2 _5270 = uvec2(textureSize(_7119, 0));
                                vec2 _5277 = vec2(float(_5270.x), float(_5270.y));
                                a = textureLod(_7121, (vec2(_5222 * 0.15915493667125701904296875, _5221) * ((_5277 - vec2(1.0)) / _5277)) + (vec2(0.5) / _5277), 0.0).xyz * clamp(_2097.z * 20.0, 0.0, 1.0);
                            }
                            else
                            {
                                a = vec3(0.0);
                            }
                            float screenWeight_1 = hitEdge * step(0.5, sampled_1.w);
                            screenWeight = screenWeight_1;
                            a = (((max(sampled_1.xyz, vec3(0.0)) * puddle) - a) * up) * screenWeight_1;
                        }
                        else
                        {
                            screenWeight = 0.0;
                            a = vec3(0.0);
                        }
                        if (marches)
                        {
                            screenAllowed = screenWeight < 0.999000012874603271484375;
                        }
                        else
                        {
                            screenAllowed = false;
                        }
                        if (screenAllowed)
                        {
                            vec3 _2244 = normalW;
                            vec3 _2246 = normalize(reflect(_447, _2244));
                            vec3 originRelative = _460 + (_2244 * (0.0199999995529651641845703125 + (0.0005000000237487256526947021484375 * _3669.z)));
                            vec2 _2259 = _460.xy + p.wet1.zw;
                            float _2263 = _460.z + p.wet2.y;
                            vec2 _7045 = p.shelter0.xy;
                            float _7046 = p.shelter0.z;
                            float _7047 = p.shelter0.w;
                            float _7048 = p.shelter1.x;
                            float _7049 = p.shelter1.y;
                            vec2 _7050 = p.shelter2.xy;
                            float _7051 = p.wet2.x;
                            bool _5331;
                            {
                                ivec2 _5384 = ivec2(floor((_2259 - _7045) * _7046));
                                bool _5385;
                                if (_7051 > 0.5)
                                {
                                    _5385 = _5384.x >= 0;
                                }
                                else
                                {
                                    _5385 = false;
                                }
                                if (_5385)
                                {
                                    _5385 = _5384.y >= 0;
                                }
                                else
                                {
                                    _5385 = false;
                                }
                                if (_5385)
                                {
                                    _5385 = _5384.x < int(_7050.x);
                                }
                                else
                                {
                                    _5385 = false;
                                }
                                if (_5385)
                                {
                                    _5385 = _5384.y < int(_7050.y);
                                }
                                else
                                {
                                    _5385 = false;
                                }
                                if (!_5385)
                                {
                                    _5331 = false;
                                }
                                else
                                {
                                    vec4 _5343 = texelFetch(_7109, ivec3(_5384, 0).xy, 0);
                                    float _5435 = floor((_5343.z * 255.0) + 0.5) + (floor((_5343.y * 255.0) + 0.5) * 256.0);
                                    float _5423;
                                    if (_5435 < 0.5)
                                    {
                                        _5423 = -1000000015047466219876688855040.0;
                                    }
                                    else
                                    {
                                        _5423 = _7047 + ((_5435 - 1.0) * _7048);
                                    }
                                    float _5348 = _2263 + _7049;
                                    if (_5423 <= _5348)
                                    {
                                        _5331 = false;
                                    }
                                    else
                                    {
                                        float _5352 = _2246.z;
                                        if (_5352 <= 0.0500000007450580596923828125)
                                        {
                                            _5331 = false;
                                        }
                                        else
                                        {
                                            ivec2 _5457 = ivec2(floor(((_2259 + (_2246.xy * ((_5423 - _2263) / _5352))) - _7045) * _7046));
                                            bool _5458;
                                            if (_7051 > 0.5)
                                            {
                                                _5458 = _5457.x >= 0;
                                            }
                                            else
                                            {
                                                _5458 = false;
                                            }
                                            if (_5458)
                                            {
                                                _5458 = _5457.y >= 0;
                                            }
                                            else
                                            {
                                                _5458 = false;
                                            }
                                            if (_5458)
                                            {
                                                _5458 = _5457.x < int(_7050.x);
                                            }
                                            else
                                            {
                                                _5458 = false;
                                            }
                                            if (_5458)
                                            {
                                                _5458 = _5457.y < int(_7050.y);
                                            }
                                            else
                                            {
                                                _5458 = false;
                                            }
                                            if (!_5458)
                                            {
                                                _5331 = false;
                                            }
                                            else
                                            {
                                                vec4 _5369 = texelFetch(_7109, ivec3(_5457, 0).xy, 0);
                                                float _5508 = floor((_5369.z * 255.0) + 0.5) + (floor((_5369.y * 255.0) + 0.5) * 256.0);
                                                float _5496;
                                                if (_5508 < 0.5)
                                                {
                                                    _5496 = -1000000015047466219876688855040.0;
                                                }
                                                else
                                                {
                                                    _5496 = _7047 + ((_5508 - 1.0) * _7048);
                                                }
                                                _5331 = _5496 > _5348;
                                            }
                                        }
                                    }
                                }
                            }
                            _5521 = false;
                            vec3 hitRelative;
                            float voxel;
                            bool _5522;
                            {
                                hitRelative = originRelative;
                                voxel = p.sdfOrigin.data[0].w;
                                if (int(p.field.x) <= 0)
                                {
                                    _5521 = true;
                                    _5522 = false;
                                }
                                else
                                {
                                    vec3 _5553 = originRelative + ((normalW * p.sdfOrigin.data[0].w) * 1.5);
                                    bool _5523 = false;
                                    float _5524 = 0.0;
                                    float _5525 = 0.0;
                                    int _5527 = 0;
                                    float _5526 = 0.0;
                                    bool _5528;
                                    bool _5529;
                                    float _5531;
                                    float _5532;
                                    bool _5533;
                                    float _5633;
                                    bool _5635;
                                    bool _5748;
                                    float _5750;
                                    float _5791;
                                    bool _5793;
                                    bool _5906;
                                    float _5908;
                                    float _5949;
                                    bool _5951;
                                    for (;;)
                                    {
                                        if (_5527 < 96)
                                        {
                                            _5528 = _5526 < p.trace.y;
                                        }
                                        else
                                        {
                                            _5528 = false;
                                        }
                                        if (!_5528)
                                        {
                                            break;
                                        }
                                        vec3 _5570 = _5553 + (_2246 * _5526);
                                        _5632 = false;
                                        {
                                            _5529 = false;
                                            _5531 = p.sdfOrigin.data[0].w;
                                            int _5634 = 0;
                                            for (;;)
                                            {
                                                bool _5646_ladder_break = false;
                                                {
                                                    if (!(_5634 < int(p.field.x)))
                                                    {
                                                        _5646_ladder_break = true;
                                                    }
                                                    else
                                                    {
                                                        vec3 _5663 = (_5570 - p.sdfOrigin.data[_5634].xyz) / vec3(p.sdfOrigin.data[_5634].w);
                                                        if (any(lessThan(_5663, vec3(0.5))))
                                                        {
                                                            _5635 = true;
                                                        }
                                                        else
                                                        {
                                                            _5635 = any(greaterThan(_5663, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                                        }
                                                        if (!(_5635))
                                                        {
                                                            _5529 = true;
                                                            _5531 = p.sdfOrigin.data[_5634].w;
                                                            _5632 = true;
                                                            _5633 = ((textureLod(_7123, vec3(_5663.x / float(int(p.field.y)), _5663.y / float(int(p.field.y)), ((float(_5634) * float(int(p.field.y))) + _5663.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_5634];
                                                            _5646_ladder_break = true;
                                                        }
                                                    }
                                                }
                                                if (_5646_ladder_break)
                                                {
                                                    break;
                                                }
                                                _5634++;
                                                continue;
                                            }
                                            if (!(_5632))
                                            {
                                                _5632 = true;
                                                _5633 = 1000000.0;
                                            }
                                        }
                                        if (!_5529)
                                        {
                                            _5521 = true;
                                            _5522 = false;
                                            break;
                                        }
                                        if (_5523)
                                        {
                                            if (_5633 <= 0.0)
                                            {
                                                float _5584 = _5525 - _5633;
                                                if (_5584 > 9.9999997473787516355514526367188e-06)
                                                {
                                                    _5532 = _5524 + ((_5526 - _5524) * (_5525 / _5584));
                                                }
                                                else
                                                {
                                                    _5532 = _5526;
                                                }
                                                voxel = p.sdfOrigin.data[0].w;
                                                int _5746 = 0;
                                                float _5747 = clamp(_5532, _5524, _5526);
                                                for (;;)
                                                {
                                                    if (!(_5746 < 2))
                                                    {
                                                        break;
                                                    }
                                                    vec3 _5768 = _5553 + (_2246 * _5747);
                                                    _5790 = false;
                                                    {
                                                        _5748 = false;
                                                        _5750 = p.sdfOrigin.data[0].w;
                                                        int _5792 = 0;
                                                        for (;;)
                                                        {
                                                            bool _5804_ladder_break = false;
                                                            {
                                                                if (!(_5792 < int(p.field.x)))
                                                                {
                                                                    _5804_ladder_break = true;
                                                                }
                                                                else
                                                                {
                                                                    vec3 _5821 = (_5768 - p.sdfOrigin.data[_5792].xyz) / vec3(p.sdfOrigin.data[_5792].w);
                                                                    if (any(lessThan(_5821, vec3(0.5))))
                                                                    {
                                                                        _5793 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _5793 = any(greaterThan(_5821, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                                                    }
                                                                    if (!(_5793))
                                                                    {
                                                                        _5748 = true;
                                                                        _5750 = p.sdfOrigin.data[_5792].w;
                                                                        _5790 = true;
                                                                        _5791 = ((textureLod(_7123, vec3(_5821.x / float(int(p.field.y)), _5821.y / float(int(p.field.y)), ((float(_5792) * float(int(p.field.y))) + _5821.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_5792];
                                                                        _5804_ladder_break = true;
                                                                    }
                                                                }
                                                            }
                                                            if (_5804_ladder_break)
                                                            {
                                                                break;
                                                            }
                                                            _5792++;
                                                            continue;
                                                        }
                                                        if (!(_5790))
                                                        {
                                                            _5790 = true;
                                                            _5791 = 1000000.0;
                                                        }
                                                    }
                                                    if (!_5748)
                                                    {
                                                        break;
                                                    }
                                                    voxel = _5750;
                                                    _5746++;
                                                    _5747 += max(_5791, 0.0);
                                                    continue;
                                                }
                                                hitRelative = _5553 + (_2246 * (_5747 + (voxel * 0.25)));
                                                _5521 = true;
                                                _5522 = true;
                                                break;
                                            }
                                            if (_5633 > _5525)
                                            {
                                                _5533 = _5525 < (_5531 * 0.0500000007450580596923828125);
                                            }
                                            else
                                            {
                                                _5533 = false;
                                            }
                                            if (_5533)
                                            {
                                                voxel = p.sdfOrigin.data[0].w;
                                                int _5904 = 0;
                                                float _5905 = _5524;
                                                for (;;)
                                                {
                                                    if (!(_5904 < 2))
                                                    {
                                                        break;
                                                    }
                                                    vec3 _5926 = _5553 + (_2246 * _5905);
                                                    _5948 = false;
                                                    {
                                                        _5906 = false;
                                                        _5908 = p.sdfOrigin.data[0].w;
                                                        int _5950 = 0;
                                                        for (;;)
                                                        {
                                                            bool _5962_ladder_break = false;
                                                            {
                                                                if (!(_5950 < int(p.field.x)))
                                                                {
                                                                    _5962_ladder_break = true;
                                                                }
                                                                else
                                                                {
                                                                    vec3 _5979 = (_5926 - p.sdfOrigin.data[_5950].xyz) / vec3(p.sdfOrigin.data[_5950].w);
                                                                    if (any(lessThan(_5979, vec3(0.5))))
                                                                    {
                                                                        _5951 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _5951 = any(greaterThan(_5979, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                                                    }
                                                                    if (!(_5951))
                                                                    {
                                                                        _5906 = true;
                                                                        _5908 = p.sdfOrigin.data[_5950].w;
                                                                        _5948 = true;
                                                                        _5949 = ((textureLod(_7123, vec3(_5979.x / float(int(p.field.y)), _5979.y / float(int(p.field.y)), ((float(_5950) * float(int(p.field.y))) + _5979.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_5950];
                                                                        _5962_ladder_break = true;
                                                                    }
                                                                }
                                                            }
                                                            if (_5962_ladder_break)
                                                            {
                                                                break;
                                                            }
                                                            _5950++;
                                                            continue;
                                                        }
                                                        if (!(_5948))
                                                        {
                                                            _5948 = true;
                                                            _5949 = 1000000.0;
                                                        }
                                                    }
                                                    if (!_5906)
                                                    {
                                                        break;
                                                    }
                                                    voxel = _5908;
                                                    _5904++;
                                                    _5905 += max(_5949, 0.0);
                                                    continue;
                                                }
                                                hitRelative = _5553 + (_2246 * (_5905 + (voxel * 0.25)));
                                                _5521 = true;
                                                _5522 = true;
                                                break;
                                            }
                                        }
                                        _5523 = true;
                                        _5524 = _5526;
                                        _5526 += max(_5633, _5531 * 0.25);
                                        _5525 = _5633;
                                        _5527++;
                                        continue;
                                    }
                                    if (!(_5521))
                                    {
                                        _5521 = true;
                                        _5522 = false;
                                    }
                                }
                            }
                            vec3 radiance = vec3(0.0);
                            if (!_5522)
                            {
                                float _6640 = acos(clamp(sqrt(max(0.0, (p.misc.w * p.misc.w) - 40449600.0)) / p.misc.w, -1.0, 1.0));
                                float _6641 = 3.1415927410125732421875 - _6640;
                                float _6644 = acos(clamp(_2246.z, -1.0, 1.0));
                                float _6630;
                                if (_6644 < _6641)
                                {
                                    _6630 = (1.0 - sqrt(max(0.0, 1.0 - (_6644 / _6641)))) * 0.5;
                                }
                                else
                                {
                                    _6630 = (sqrt(max(0.0, (_6644 - _6641) / max(_6640, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                }
                                float _6664 = atan(_2246.y, _2246.x);
                                float _6631;
                                if (_6664 < 0.0)
                                {
                                    _6631 = _6664 + 6.283185482025146484375;
                                }
                                else
                                {
                                    _6631 = _6664;
                                }
                                uvec2 _6679 = uvec2(textureSize(_7119, 0));
                                vec2 _6686 = vec2(float(_6679.x), float(_6679.y));
                                vec3 _6692 = textureLod(_7121, (vec2(_6631 * 0.15915493667125701904296875, _6630) * ((_6686 - vec2(1.0)) / _6686)) + (vec2(0.5) / _6686), 0.0).xyz;
                                if (_5331)
                                {
                                    normalW = vec3(dot(_6692, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * 0.300000011920928955078125);
                                }
                                else
                                {
                                    normalW = _6692;
                                }
                                float _6710 = clamp(textureLod(_7111, uv, 0.0).w, 0.0, 1.0);
                                radiance = normalW * clamp((pow(_450 + _6710, roughness * roughness) - 1.0) + _6710, 0.0, 1.0);
                            }
                            else
                            {
                                bool _6063;
                                {
                                    radiance = vec3(0.0);
                                    vec3 _6078 = hitRelative * transpose(mat3(spvWorkaroundRowMajor(p.viewToWorld)[0].xyz, spvWorkaroundRowMajor(p.viewToWorld)[1].xyz, spvWorkaroundRowMajor(p.viewToWorld)[2].xyz));
                                    float _6079 = _6078.z;
                                    if (_6079 <= p.unproject.z)
                                    {
                                        _6063 = false;
                                    }
                                    else
                                    {
                                        float _6138 = _6078.z;
                                        vec2 _6149 = (vec2((_6078.x * p.unproject.x) / _6138, ((-_6078.y) * p.unproject.y) / _6138) * 0.5) + vec2(0.5);
                                        bool _6064;
                                        if (any(lessThan(_6149, vec2(0.0))))
                                        {
                                            _6064 = true;
                                        }
                                        else
                                        {
                                            _6064 = any(greaterThan(_6149, vec2(1.0)));
                                        }
                                        if (_6064)
                                        {
                                            _6063 = false;
                                        }
                                        else
                                        {
                                            vec4 _6105 = texelFetch(_7114, ivec3(ivec2(_6149 * p.resolution.xy), 0).xy, 0);
                                            float _6106 = _6105.x;
                                            if (_6106 <= 0.0)
                                            {
                                                _6063 = false;
                                            }
                                            else
                                            {
                                                if (_6106 >= 0.89999997615814208984375)
                                                {
                                                    _6063 = false;
                                                }
                                                else
                                                {
                                                    float _6173 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _6106)) * (p.unproject.w - p.unproject.z)));
                                                    if (abs(_6079 - _6173) > ((0.0199999995529651641845703125 * max(_6173, 1.0)) + voxel))
                                                    {
                                                        _6063 = false;
                                                    }
                                                    else
                                                    {
                                                        radiance = max(textureLod(_7116, _6149, 0.0).xyz, vec3(0.0));
                                                        _6063 = true;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                if (!_6063)
                                {
                                    float _2940 = voxel * 2.0;
                                    float tolerance = 0.25 + _2940;
                                    vec3 _2947 = hitRelative + p.cacheOrigin.xyz;
                                    vec2 litFound;
                                    float agreement;
                                    float cardDepth;
                                    bool _6175;
                                    {
                                        cardDepth = 0.0;
                                        litFound = vec2(0.0);
                                        agreement = 1000000000.0;
                                        if (int(p.cacheGrid.w) <= 0)
                                        {
                                            _6175 = false;
                                        }
                                        else
                                        {
                                            ivec3 _6219 = ivec3(floor((_2947 - p.cacheGrid.xyz) / vec3(p.cacheOrigin.w)));
                                            int _6437 = int((((uint(_6219.x) * 2376512323u) ^ (uint(_6219.y) * 3625334849u)) ^ (uint(_6219.z) * 3407524639u)) & uint(int(p.cacheGrid.w) - 1));
                                            uvec2 _6224 = uvec2(textureSize(_7126, 0));
                                            vec2 _6231 = vec2(float(_6224.x), float(_6224.y));
                                            int _6234 = int(p.cacheGrid.w);
                                            bool _6178 = false;
                                            bool _6179 = false;
                                            int _6180 = 0;
                                            bool _6181;
                                            ivec2 _6182;
                                            bool _6186;
                                            bool _6195;
                                            bool _6196;
                                            bool _6197;
                                            bool _6198;
                                            bool _6199;
                                            ivec2 _6438;
                                            for (;;)
                                            {
                                                if (_6180 <= uCells._member0[_6234].y)
                                                {
                                                    _6181 = !_6178;
                                                }
                                                else
                                                {
                                                    _6181 = false;
                                                }
                                                if (!_6181)
                                                {
                                                    break;
                                                }
                                                bool _6253 = _6180 == 0;
                                                if (_6253)
                                                {
                                                    _6182 = uCells._member0[_6437];
                                                }
                                                else
                                                {
                                                    int _6442 = uCells._member0[_6234].x + ((_6180 - 1) * 6);
                                                    float _6458 = intBitsToFloat(uCellPages_1._member0[_6442 + 3]);
                                                    vec3 _6460 = _2947 - vec3(intBitsToFloat(uCellPages_1._member0[_6442]), intBitsToFloat(uCellPages_1._member0[_6442 + 1]), intBitsToFloat(uCellPages_1._member0[_6442 + 2]));
                                                    if (dot(_6460, _6460) > (_6458 * _6458))
                                                    {
                                                        _6438 = ivec2(0);
                                                    }
                                                    else
                                                    {
                                                        _6438 = ivec2(uCellPages_1._member0[_6442 + 4], uCellPages_1._member0[_6442 + 5]);
                                                    }
                                                    _6182 = _6438;
                                                }
                                                bool _6183 = _6178;
                                                bool _6184 = _6179;
                                                int _6185 = 0;
                                                for (;;)
                                                {
                                                    bool _6265_ladder_break = false;
                                                    {
                                                        if (_6185 < _6182.y)
                                                        {
                                                            _6186 = !_6183;
                                                        }
                                                        else
                                                        {
                                                            _6186 = false;
                                                        }
                                                        if (!_6186)
                                                        {
                                                            _6265_ladder_break = true;
                                                        }
                                                        else
                                                        {
                                                            int _6282 = _6182.x + _6185;
                                                            int _6285 = uCellPages_1._member0[_6282] * 10;
                                                            if (!((uPages_1._member0[_6285 + 5].w > 0.5) == _6253))
                                                            {
                                                                int _6294 = _6285 + 9;
                                                                vec3 _6298 = _2947 - uPages_1._member0[_6294].xyz;
                                                                if (!(dot(_6298, _6298) > (uPages_1._member0[_6294].w * uPages_1._member0[_6294].w)))
                                                                {
                                                                    int _6476 = uCellPages_1._member0[_6282] * 10;
                                                                    vec4 _6193 = uPages_1._member0[_6476];
                                                                    vec4 _6194 = uPages_1._member0[_6476 + 1];
                                                                    int _6482 = _6476 + 2;
                                                                    int _6485 = _6476 + 3;
                                                                    int _6488 = _6476 + 4;
                                                                    int _6494 = _6476 + 6;
                                                                    int _6497 = _6476 + 7;
                                                                    int _6500 = _6476 + 8;
                                                                    vec3 _6507 = _2947 - vec3(uPages_1._member0[_6494].w, uPages_1._member0[_6497].w, uPages_1._member0[_6500].w);
                                                                    vec3 _6192 = vec3(dot(uPages_1._member0[_6494].xyz, vec3(-0.0)), dot(uPages_1._member0[_6497].xyz, vec3(-0.0)), dot(uPages_1._member0[_6500].xyz, vec3(-0.0)));
                                                                    vec3 _6520 = vec3(dot(uPages_1._member0[_6494].xyz, _6507), dot(uPages_1._member0[_6497].xyz, _6507), dot(uPages_1._member0[_6500].xyz, _6507)) - uPages_1._member0[_6482].xyz;
                                                                    vec2 _6530 = vec2(dot(_6520, uPages_1._member0[_6485].xyz), dot(_6520, uPages_1._member0[_6488].xyz)) / max(vec2(uPages_1._member0[_6482].w, uPages_1._member0[_6485].w), vec2(9.9999999747524270787835121154785e-07));
                                                                    vec2 _6187 = _6530;
                                                                    float _6189 = -dot(_6520, uPages_1._member0[_6476 + 5].xyz);
                                                                    float _6190 = uPages_1._member0[_6488].w;
                                                                    vec3 _6191 = uPages_1._member0[_6476 + 5].xyz;
                                                                    if (abs(_6530.x) > 1.0)
                                                                    {
                                                                        _6195 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _6195 = abs(_6187.y) > 1.0;
                                                                    }
                                                                    if (!(_6195))
                                                                    {
                                                                        if (false)
                                                                        {
                                                                            _6196 = dot(_6191, _6192) < (-0.0500000007450580596923828125);
                                                                        }
                                                                        else
                                                                        {
                                                                            _6196 = false;
                                                                        }
                                                                        if (!(_6196))
                                                                        {
                                                                            if (_6189 < ((-0.25) - _2940))
                                                                            {
                                                                                _6197 = true;
                                                                            }
                                                                            else
                                                                            {
                                                                                _6197 = _6189 > (_6190 + tolerance);
                                                                            }
                                                                            if (!(_6197))
                                                                            {
                                                                                vec2 _6349 = (_6187 * 0.5) + vec2(0.5);
                                                                                vec2 _6356 = (_6194.xy + (_6349 * _6194.zw)) / _6231;
                                                                                if (!(textureLod(_7128, _6356, 0.0).w < 0.5))
                                                                                {
                                                                                    float _6376 = abs((textureLod(_7130, _6356, 0.0).x * _6190) - _6189);
                                                                                    if (_6376 < agreement)
                                                                                    {
                                                                                        agreement = _6376;
                                                                                        cardDepth = _6190;
                                                                                        litFound = (_6193.xy + (_6349 * _6193.zw)) / vec3(p.cacheAtlas.xy, 0.0).xy;
                                                                                        _6198 = _6376 < 0.0199999995529651641845703125;
                                                                                        _6199 = true;
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _6198 = _6183;
                                                                                        _6199 = _6184;
                                                                                    }
                                                                                    _6183 = _6198;
                                                                                    _6184 = _6199;
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                    if (_6265_ladder_break)
                                                    {
                                                        break;
                                                    }
                                                    _6185++;
                                                    continue;
                                                }
                                                _6178 = _6183;
                                                _6179 = _6184;
                                                _6180++;
                                                continue;
                                            }
                                            _6175 = _6179;
                                        }
                                    }
                                    if (_6175)
                                    {
                                        screenAllowed = agreement <= (tolerance + (cardDepth * 0.5));
                                    }
                                    else
                                    {
                                        screenAllowed = false;
                                    }
                                    if (screenAllowed)
                                    {
                                        radiance = max(textureLod(_7132, litFound, 0.0).xyz, vec3(0.0)) * p.trace.w;
                                        crossed = true;
                                    }
                                    else
                                    {
                                        crossed = _6063;
                                    }
                                }
                                else
                                {
                                    crossed = _6063;
                                }
                                if (!crossed)
                                {
                                    float _6549 = acos(clamp(sqrt(max(0.0, (p.misc.w * p.misc.w) - 40449600.0)) / p.misc.w, -1.0, 1.0));
                                    float _6550 = 3.1415927410125732421875 - _6549;
                                    float _6553 = acos(clamp(_2246.z, -1.0, 1.0));
                                    float _6539;
                                    if (_6553 < _6550)
                                    {
                                        _6539 = (1.0 - sqrt(max(0.0, 1.0 - (_6553 / _6550)))) * 0.5;
                                    }
                                    else
                                    {
                                        _6539 = (sqrt(max(0.0, (_6553 - _6550) / max(_6549, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                    }
                                    float _6573 = atan(_2246.y, _2246.x);
                                    float _6540;
                                    if (_6573 < 0.0)
                                    {
                                        _6540 = _6573 + 6.283185482025146484375;
                                    }
                                    else
                                    {
                                        _6540 = _6573;
                                    }
                                    uvec2 _6588 = uvec2(textureSize(_7119, 0));
                                    vec2 _6595 = vec2(float(_6588.x), float(_6588.y));
                                    vec3 _6601 = textureLod(_7121, (vec2(_6540 * 0.15915493667125701904296875, _6539) * ((_6595 - vec2(1.0)) / _6595)) + (vec2(0.5) / _6595), 0.0).xyz;
                                    if (_5331)
                                    {
                                        normalW = vec3(dot(_6601, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * 0.300000011920928955078125);
                                    }
                                    else
                                    {
                                        normalW = _6601;
                                    }
                                    float _6619 = clamp(textureLod(_7111, uv, 0.0).w, 0.0, 1.0);
                                    radiance = normalW * clamp((pow(_450 + _6619, roughness * roughness) - 1.0) + _6619, 0.0, 1.0);
                                }
                            }
                            float _3522 = max(radiance.x, max(radiance.y, radiance.z));
                            if (_3522 > p.trace.z)
                            {
                                radiance *= (p.trace.z / _3522);
                            }
                            if (water)
                            {
                                float _6731 = acos(clamp(sqrt(max(0.0, (p.misc.w * p.misc.w) - 40449600.0)) / p.misc.w, -1.0, 1.0));
                                float _6732 = 3.1415927410125732421875 - _6731;
                                float _6735 = acos(clamp(_2246.z, -1.0, 1.0));
                                float _6721;
                                if (_6735 < _6732)
                                {
                                    _6721 = (1.0 - sqrt(max(0.0, 1.0 - (_6735 / _6732)))) * 0.5;
                                }
                                else
                                {
                                    _6721 = (sqrt(max(0.0, (_6735 - _6732) / max(_6731, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                }
                                float _6755 = atan(_2246.y, _2246.x);
                                float _6722;
                                if (_6755 < 0.0)
                                {
                                    _6722 = _6755 + 6.283185482025146484375;
                                }
                                else
                                {
                                    _6722 = _6755;
                                }
                                uvec2 _6770 = uvec2(textureSize(_7119, 0));
                                vec2 _6777 = vec2(float(_6770.x), float(_6770.y));
                                b = textureLod(_7121, (vec2(_6722 * 0.15915493667125701904296875, _6721) * ((_6777 - vec2(1.0)) / _6777)) + (vec2(0.5) / _6777), 0.0).xyz * clamp(_2246.z * 20.0, 0.0, 1.0);
                            }
                            else
                            {
                                b = vec3(0.0);
                            }
                            float _3550 = screenWeight;
                            screenWeight = 1.0;
                            a += (((radiance - b) * up) * (1.0 - _3550));
                        }
                        if (!raw)
                        {
                            screenAllowed = !water;
                        }
                        else
                        {
                            screenAllowed = false;
                        }
                        if (screenAllowed)
                        {
                            screenAllowed = roughness > 0.0500000007450580596923828125;
                        }
                        else
                        {
                            screenAllowed = false;
                        }
                        float spread;
                        if (screenAllowed)
                        {
                            spread = 2.0 + roughness;
                        }
                        else
                        {
                            if (traces)
                            {
                                marches = true;
                            }
                            if (marches)
                            {
                                puddle = 1.0;
                            }
                            else
                            {
                                puddle = screenWeight;
                            }
                            spread = puddle;
                        }
                        imageStore(uReflection, ivec2(gl_GlobalInvocationID.xy), vec4(a, spread));
                    }
                }
                else
                {
                    if (traces)
                    {
                        screenAllowed = true;
                    }
                    else
                    {
                        screenAllowed = marches;
                    }
                    if (screenAllowed)
                    {
                        ceiling = p.trace2.x;
                    }
                    else
                    {
                        ceiling = p.ssr.z;
                    }
                    if (roughness > ceiling)
                    {
                        imageStore(uReflection, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
                    }
                    else
                    {
                        vec3 _1437 = mix(vec3(0.07999999821186065673828125 * sampled.y), textureLod(_7111, uv, 0.0).xyz, vec3(sampled.x));
                        float _4204 = 1.0 - roughness;
                        float _4215 = 1.03999996185302734375 * (((min(_4204 * _4204, exp2((-9.27999973297119140625) * _450)) * _4204) + 0.0425000004470348358154296875) - (0.0274999998509883880615234375 * roughness));
                        vec2 _4220 = vec2((1.03999996185302734375 - (0.572000026702880859375 * roughness)) - _4215, ((0.02199999988079071044921875 * roughness) - 0.039999999105930328369140625) + _4215);
                        if (traces)
                        {
                            screenAllowed = true;
                        }
                        else
                        {
                            screenAllowed = marches;
                        }
                        if (screenAllowed)
                        {
                            fade = clamp((ceiling - roughness) / max(ceiling * 0.25, 0.001000000047497451305389404296875), 0.0, 1.0);
                        }
                        else
                        {
                            fade = clamp((ceiling - roughness) / max(ceiling, 0.001000000047497451305389404296875), 0.0, 1.0);
                        }
                        if (roughness > 0.0500000007450580596923828125)
                        {
                            float alpha = roughness * roughness;
                            if (raw)
                            {
                                uvec3 _4268 = (uvec3(gl_GlobalInvocationID.xy, uint(p.misc.y) ^ 408859118u) * uvec3(1664525u)) + uvec3(1013904223u);
                                _4268.x = _4268.x + (_4268.y * _4268.z);
                                _4268.y = _4268.y + (_4268.z * _4268.x);
                                _4268.z = _4268.z + (_4268.x * _4268.y);
                                uvec3 _4295 = _4268 ^ (_4268 >> uvec3(16u));
                                _4295.x = _4295.x + (_4295.y * _4295.z);
                                _4295.y = _4295.y + (_4295.z * _4295.x);
                                uvec3 _7097 = _4295;
                                _7097.z = _4295.z + (_4295.x * _4295.y);
                                facetNoise = (vec3(_4295 >> uvec3(8u)) * 5.9604644775390625e-08).xy;
                            }
                            else
                            {
                                vec2 _4224 = _266 + (vec2(12.0780429840087890625, 9.1174449920654296875) * p.misc.y);
                                vec2 _4239 = (_266 + vec2(31.0, 17.0)) + (vec2(12.0780429840087890625, 9.1174449920654296875) * p.misc.y);
                                facetNoise = vec2(fract(52.98291778564453125 * fract((0.067110560834407806396484375 * _4224.x) + (0.005837149918079376220703125 * _4224.y))), fract(52.98291778564453125 * fract((0.067110560834407806396484375 * _4239.x) + (0.005837149918079376220703125 * _4239.y))));
                            }
                            float _1661 = sqrt((1.0 - facetNoise.x) / max(1.0 + (((alpha * alpha) - 1.0) * facetNoise.x), 9.9999999747524270787835121154785e-07));
                            float _1665 = sqrt(max(1.0 - (_1661 * _1661), 0.0));
                            float phi = 6.283185482025146484375 * facetNoise.y;
                            if (abs(_389.z) < 0.999000012874603271484375)
                            {
                                up = vec3(0.0, 0.0, 1.0);
                            }
                            else
                            {
                                up = vec3(1.0, 0.0, 0.0);
                            }
                            vec3 _1681 = normalize(cross(up, _389));
                            vec3 _1692 = normalize(((_1681 * (_1665 * cos(phi))) + (cross(_389, _1681) * (_1665 * sin(phi)))) + (_389 * _1661));
                            if (dot(reflect(_447, _1692), _389) > 0.0199999995529651641845703125)
                            {
                                normalW = _1692;
                            }
                            else
                            {
                                normalW = _389;
                            }
                        }
                        else
                        {
                            normalW = _389;
                        }
                        up = ((_1437 * _4220.x) + (vec3(clamp(50.0 * _1437.y, 0.0, 1.0)) * _4220.y)) * fade;
                        screenAllowed = roughness <= p.ssr.z;
                        if (max(up.x, max(up.y, up.z)) <= 0.0030000000260770320892333984375)
                        {
                            imageStore(uReflection, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
                        }
                        else
                        {
                            vec3 _1736 = reflect(normalize(_3669), normalize(normalW * transpose(mat3(spvWorkaroundRowMajor(p.viewToWorld)[0].xyz, spvWorkaroundRowMajor(p.viewToWorld)[1].xyz, spvWorkaroundRowMajor(p.viewToWorld)[2].xyz))));
                            bool have;
                            if (_365)
                            {
                                have = _1736.z > 0.0;
                            }
                            else
                            {
                                have = false;
                            }
                            if (!have)
                            {
                                screenAllowed = false;
                            }
                            int _1753 = max(int(p.ssr.x + 0.5), 1);
                            vec2 _4985 = _266 + (vec2(12.0780429840087890625, 9.1174449920654296875) * p.misc.y);
                            have = false;
                            ceiling = -1.0;
                            vec3 previous = _3669;
                            int i = 0;
                            fade = 0.20000000298023223876953125 * (0.5 + fract(52.98291778564453125 * fract((0.067110560834407806396484375 * _4985.x) + (0.005837149918079376220703125 * _4985.y))));
                            bool crossed;
                            float hitEdge;
                            vec3 a;
                            vec3 b;
                            bool met;
                            bool _36;
                            float _5160;
                            for (;;)
                            {
                                vec3 pv;
                                float t;
                                bool _116_ladder_break = false;
                                {
                                    if (screenAllowed)
                                    {
                                        crossed = i < _1753;
                                    }
                                    else
                                    {
                                        crossed = false;
                                    }
                                    if (!crossed)
                                    {
                                        hitEdge = 0.0;
                                        facetNoise = vec2(0.0);
                                        _116_ladder_break = true;
                                    }
                                    else
                                    {
                                        pv = _3669 + (_1736 * fade);
                                        if (pv.z <= p.unproject.z)
                                        {
                                            met = true;
                                        }
                                        else
                                        {
                                            met = fade > p.misc.z;
                                        }
                                        if (met)
                                        {
                                            hitEdge = 0.0;
                                            facetNoise = vec2(0.0);
                                            _116_ladder_break = true;
                                        }
                                        else
                                        {
                                            t = fade + max(0.20000000298023223876953125, fade * 0.07999999821186065673828125);
                                            vec2 _5015 = (vec2((pv.x * p.unproject.x) / pv.z, ((-pv.y) * p.unproject.y) / pv.z) * 0.5) + vec2(0.5);
                                            if (any(lessThan(_5015, vec2(0.0))))
                                            {
                                                _36 = true;
                                            }
                                            else
                                            {
                                                _36 = any(greaterThan(_5015, vec2(1.0)));
                                            }
                                            if (_36)
                                            {
                                                hitEdge = 0.0;
                                                facetNoise = vec2(0.0);
                                                _116_ladder_break = true;
                                            }
                                            else
                                            {
                                                uvec2 _5021 = uvec2(textureSize(_7114, 0));
                                                uint _5022 = _5021.x;
                                                uint _5023 = _5021.y;
                                                vec4 _5042 = texelFetch(_7114, ivec3(clamp(ivec2(_5015 * vec2(float(_5022), float(_5023))), ivec2(0), ivec2(int(_5022) - 1, int(_5023) - 1)), 0).xy, 0);
                                                float _5043 = _5042.x;
                                                if (_5043 <= 0.0)
                                                {
                                                    ceiling = -1.0;
                                                }
                                                else
                                                {
                                                    float diff = pv.z - (((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _5043)) * (p.unproject.w - p.unproject.z))));
                                                    if (diff > 0.0)
                                                    {
                                                        float _1919 = max(p.ssr.w, pv.z * 0.0500000007450580596923828125);
                                                        if (have)
                                                        {
                                                            crossed = ceiling < 0.0;
                                                        }
                                                        else
                                                        {
                                                            crossed = false;
                                                        }
                                                        if (crossed)
                                                        {
                                                            screenAllowed = true;
                                                        }
                                                        else
                                                        {
                                                            screenAllowed = diff < _1919;
                                                        }
                                                        if (screenAllowed)
                                                        {
                                                            a = previous;
                                                            b = pv;
                                                            int k = 0;
                                                            for (;;)
                                                            {
                                                                if (!(k < 5))
                                                                {
                                                                    break;
                                                                }
                                                                vec3 _1945 = b;
                                                                vec3 mid = (a + _1945) * 0.5;
                                                                float _5075 = mid.z;
                                                                uvec2 _5092 = uvec2(textureSize(_7114, 0));
                                                                uint _5093 = _5092.x;
                                                                uint _5094 = _5092.y;
                                                                vec4 _5113 = texelFetch(_7114, ivec3(clamp(ivec2(((vec2((mid.x * p.unproject.x) / _5075, ((-mid.y) * p.unproject.y) / _5075) * 0.5) + vec2(0.5)) * vec2(float(_5093), float(_5094))), ivec2(0), ivec2(int(_5093) - 1, int(_5094) - 1)), 0).xy, 0);
                                                                float _5114 = _5113.x;
                                                                if (_5114 > 0.0)
                                                                {
                                                                    screenAllowed = (mid.z - (((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _5114)) * (p.unproject.w - p.unproject.z))))) > 0.0;
                                                                }
                                                                else
                                                                {
                                                                    screenAllowed = false;
                                                                }
                                                                if (screenAllowed)
                                                                {
                                                                    b = mid;
                                                                }
                                                                else
                                                                {
                                                                    a = mid;
                                                                }
                                                                k++;
                                                                continue;
                                                            }
                                                            vec2 _5157 = (vec2((b.x * p.unproject.x) / b.z, ((-b.y) * p.unproject.y) / b.z) * 0.5) + vec2(0.5);
                                                            uvec2 _5164 = uvec2(textureSize(_7114, 0));
                                                            uint _5165 = _5164.x;
                                                            uint _5166 = _5164.y;
                                                            vec4 _5185 = texelFetch(_7114, ivec3(clamp(ivec2(_5157 * vec2(float(_5165), float(_5166))), ivec2(0), ivec2(int(_5165) - 1, int(_5166) - 1)), 0).xy, 0);
                                                            float _5186 = _5185.x;
                                                            if (_5186 > 0.0)
                                                            {
                                                                _5160 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _5186)) * (p.unproject.w - p.unproject.z)));
                                                            }
                                                            else
                                                            {
                                                                _5160 = 0.0;
                                                            }
                                                            if (_5160 > 0.0)
                                                            {
                                                                met = (b.z - _5160) < _1919;
                                                            }
                                                            else
                                                            {
                                                                met = false;
                                                            }
                                                            vec2 _2037 = clamp((abs((_5157 * 2.0) - vec2(1.0)) * 5.0) - vec2(4.0), vec2(0.0), vec2(1.0));
                                                            if (met)
                                                            {
                                                                puddle = clamp(1.0 - dot(_2037, _2037), 0.0, 1.0);
                                                            }
                                                            else
                                                            {
                                                                puddle = 0.0;
                                                            }
                                                            hitEdge = puddle;
                                                            facetNoise = _5157;
                                                        }
                                                        else
                                                        {
                                                            hitEdge = 0.0;
                                                            facetNoise = vec2(0.0);
                                                        }
                                                        _116_ladder_break = true;
                                                    }
                                                    else
                                                    {
                                                        ceiling = diff;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                if (_116_ladder_break)
                                {
                                    break;
                                }
                                have = true;
                                previous = pv;
                                i++;
                                fade = t;
                                continue;
                            }
                            float screenWeight;
                            if (hitEdge > 0.0)
                            {
                                vec4 sampled_1 = textureLod(_7116, facetNoise, 0.0);
                                if (water)
                                {
                                    puddle = 1.0;
                                }
                                else
                                {
                                    puddle = p.ssr.y;
                                }
                                if (water)
                                {
                                    vec3 _2097 = normalize(reflect(_447, normalW));
                                    float _5231 = acos(clamp(sqrt(max(0.0, (p.misc.w * p.misc.w) - 40449600.0)) / p.misc.w, -1.0, 1.0));
                                    float _5232 = 3.1415927410125732421875 - _5231;
                                    float _5235 = acos(clamp(_2097.z, -1.0, 1.0));
                                    float _5221;
                                    if (_5235 < _5232)
                                    {
                                        _5221 = (1.0 - sqrt(max(0.0, 1.0 - (_5235 / _5232)))) * 0.5;
                                    }
                                    else
                                    {
                                        _5221 = (sqrt(max(0.0, (_5235 - _5232) / max(_5231, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                    }
                                    float _5255 = atan(_2097.y, _2097.x);
                                    float _5222;
                                    if (_5255 < 0.0)
                                    {
                                        _5222 = _5255 + 6.283185482025146484375;
                                    }
                                    else
                                    {
                                        _5222 = _5255;
                                    }
                                    uvec2 _5270 = uvec2(textureSize(_7119, 0));
                                    vec2 _5277 = vec2(float(_5270.x), float(_5270.y));
                                    a = textureLod(_7121, (vec2(_5222 * 0.15915493667125701904296875, _5221) * ((_5277 - vec2(1.0)) / _5277)) + (vec2(0.5) / _5277), 0.0).xyz * clamp(_2097.z * 20.0, 0.0, 1.0);
                                }
                                else
                                {
                                    a = vec3(0.0);
                                }
                                float screenWeight_1 = hitEdge * step(0.5, sampled_1.w);
                                screenWeight = screenWeight_1;
                                a = (((max(sampled_1.xyz, vec3(0.0)) * puddle) - a) * up) * screenWeight_1;
                            }
                            else
                            {
                                screenWeight = 0.0;
                                a = vec3(0.0);
                            }
                            if (marches)
                            {
                                screenAllowed = screenWeight < 0.999000012874603271484375;
                            }
                            else
                            {
                                screenAllowed = false;
                            }
                            if (screenAllowed)
                            {
                                vec3 _2244 = normalW;
                                vec3 _2246 = normalize(reflect(_447, _2244));
                                vec3 originRelative = _460 + (_2244 * (0.0199999995529651641845703125 + (0.0005000000237487256526947021484375 * _3669.z)));
                                vec2 _2259 = _460.xy + p.wet1.zw;
                                float _2263 = _460.z + p.wet2.y;
                                vec2 _7045 = p.shelter0.xy;
                                float _7046 = p.shelter0.z;
                                float _7047 = p.shelter0.w;
                                float _7048 = p.shelter1.x;
                                float _7049 = p.shelter1.y;
                                vec2 _7050 = p.shelter2.xy;
                                float _7051 = p.wet2.x;
                                bool _5331;
                                {
                                    ivec2 _5384 = ivec2(floor((_2259 - _7045) * _7046));
                                    bool _5385;
                                    if (_7051 > 0.5)
                                    {
                                        _5385 = _5384.x >= 0;
                                    }
                                    else
                                    {
                                        _5385 = false;
                                    }
                                    if (_5385)
                                    {
                                        _5385 = _5384.y >= 0;
                                    }
                                    else
                                    {
                                        _5385 = false;
                                    }
                                    if (_5385)
                                    {
                                        _5385 = _5384.x < int(_7050.x);
                                    }
                                    else
                                    {
                                        _5385 = false;
                                    }
                                    if (_5385)
                                    {
                                        _5385 = _5384.y < int(_7050.y);
                                    }
                                    else
                                    {
                                        _5385 = false;
                                    }
                                    if (!_5385)
                                    {
                                        _5331 = false;
                                    }
                                    else
                                    {
                                        vec4 _5343 = texelFetch(_7109, ivec3(_5384, 0).xy, 0);
                                        float _5435 = floor((_5343.z * 255.0) + 0.5) + (floor((_5343.y * 255.0) + 0.5) * 256.0);
                                        float _5423;
                                        if (_5435 < 0.5)
                                        {
                                            _5423 = -1000000015047466219876688855040.0;
                                        }
                                        else
                                        {
                                            _5423 = _7047 + ((_5435 - 1.0) * _7048);
                                        }
                                        float _5348 = _2263 + _7049;
                                        if (_5423 <= _5348)
                                        {
                                            _5331 = false;
                                        }
                                        else
                                        {
                                            float _5352 = _2246.z;
                                            if (_5352 <= 0.0500000007450580596923828125)
                                            {
                                                _5331 = false;
                                            }
                                            else
                                            {
                                                ivec2 _5457 = ivec2(floor(((_2259 + (_2246.xy * ((_5423 - _2263) / _5352))) - _7045) * _7046));
                                                bool _5458;
                                                if (_7051 > 0.5)
                                                {
                                                    _5458 = _5457.x >= 0;
                                                }
                                                else
                                                {
                                                    _5458 = false;
                                                }
                                                if (_5458)
                                                {
                                                    _5458 = _5457.y >= 0;
                                                }
                                                else
                                                {
                                                    _5458 = false;
                                                }
                                                if (_5458)
                                                {
                                                    _5458 = _5457.x < int(_7050.x);
                                                }
                                                else
                                                {
                                                    _5458 = false;
                                                }
                                                if (_5458)
                                                {
                                                    _5458 = _5457.y < int(_7050.y);
                                                }
                                                else
                                                {
                                                    _5458 = false;
                                                }
                                                if (!_5458)
                                                {
                                                    _5331 = false;
                                                }
                                                else
                                                {
                                                    vec4 _5369 = texelFetch(_7109, ivec3(_5457, 0).xy, 0);
                                                    float _5508 = floor((_5369.z * 255.0) + 0.5) + (floor((_5369.y * 255.0) + 0.5) * 256.0);
                                                    float _5496;
                                                    if (_5508 < 0.5)
                                                    {
                                                        _5496 = -1000000015047466219876688855040.0;
                                                    }
                                                    else
                                                    {
                                                        _5496 = _7047 + ((_5508 - 1.0) * _7048);
                                                    }
                                                    _5331 = _5496 > _5348;
                                                }
                                            }
                                        }
                                    }
                                }
                                _5521 = false;
                                vec3 hitRelative;
                                float voxel;
                                bool _5522;
                                {
                                    hitRelative = originRelative;
                                    voxel = p.sdfOrigin.data[0].w;
                                    if (int(p.field.x) <= 0)
                                    {
                                        _5521 = true;
                                        _5522 = false;
                                    }
                                    else
                                    {
                                        vec3 _5553 = originRelative + ((normalW * p.sdfOrigin.data[0].w) * 1.5);
                                        bool _5523 = false;
                                        float _5524 = 0.0;
                                        float _5525 = 0.0;
                                        int _5527 = 0;
                                        float _5526 = 0.0;
                                        bool _5528;
                                        bool _5529;
                                        float _5531;
                                        float _5532;
                                        bool _5533;
                                        float _5633;
                                        bool _5635;
                                        bool _5748;
                                        float _5750;
                                        float _5791;
                                        bool _5793;
                                        bool _5906;
                                        float _5908;
                                        float _5949;
                                        bool _5951;
                                        for (;;)
                                        {
                                            if (_5527 < 96)
                                            {
                                                _5528 = _5526 < p.trace.y;
                                            }
                                            else
                                            {
                                                _5528 = false;
                                            }
                                            if (!_5528)
                                            {
                                                break;
                                            }
                                            vec3 _5570 = _5553 + (_2246 * _5526);
                                            _5632 = false;
                                            {
                                                _5529 = false;
                                                _5531 = p.sdfOrigin.data[0].w;
                                                int _5634 = 0;
                                                for (;;)
                                                {
                                                    bool _5646_ladder_break = false;
                                                    {
                                                        if (!(_5634 < int(p.field.x)))
                                                        {
                                                            _5646_ladder_break = true;
                                                        }
                                                        else
                                                        {
                                                            vec3 _5663 = (_5570 - p.sdfOrigin.data[_5634].xyz) / vec3(p.sdfOrigin.data[_5634].w);
                                                            if (any(lessThan(_5663, vec3(0.5))))
                                                            {
                                                                _5635 = true;
                                                            }
                                                            else
                                                            {
                                                                _5635 = any(greaterThan(_5663, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                                            }
                                                            if (!(_5635))
                                                            {
                                                                _5529 = true;
                                                                _5531 = p.sdfOrigin.data[_5634].w;
                                                                _5632 = true;
                                                                _5633 = ((textureLod(_7123, vec3(_5663.x / float(int(p.field.y)), _5663.y / float(int(p.field.y)), ((float(_5634) * float(int(p.field.y))) + _5663.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_5634];
                                                                _5646_ladder_break = true;
                                                            }
                                                        }
                                                    }
                                                    if (_5646_ladder_break)
                                                    {
                                                        break;
                                                    }
                                                    _5634++;
                                                    continue;
                                                }
                                                if (!(_5632))
                                                {
                                                    _5632 = true;
                                                    _5633 = 1000000.0;
                                                }
                                            }
                                            if (!_5529)
                                            {
                                                _5521 = true;
                                                _5522 = false;
                                                break;
                                            }
                                            if (_5523)
                                            {
                                                if (_5633 <= 0.0)
                                                {
                                                    float _5584 = _5525 - _5633;
                                                    if (_5584 > 9.9999997473787516355514526367188e-06)
                                                    {
                                                        _5532 = _5524 + ((_5526 - _5524) * (_5525 / _5584));
                                                    }
                                                    else
                                                    {
                                                        _5532 = _5526;
                                                    }
                                                    voxel = p.sdfOrigin.data[0].w;
                                                    int _5746 = 0;
                                                    float _5747 = clamp(_5532, _5524, _5526);
                                                    for (;;)
                                                    {
                                                        if (!(_5746 < 2))
                                                        {
                                                            break;
                                                        }
                                                        vec3 _5768 = _5553 + (_2246 * _5747);
                                                        _5790 = false;
                                                        {
                                                            _5748 = false;
                                                            _5750 = p.sdfOrigin.data[0].w;
                                                            int _5792 = 0;
                                                            for (;;)
                                                            {
                                                                bool _5804_ladder_break = false;
                                                                {
                                                                    if (!(_5792 < int(p.field.x)))
                                                                    {
                                                                        _5804_ladder_break = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        vec3 _5821 = (_5768 - p.sdfOrigin.data[_5792].xyz) / vec3(p.sdfOrigin.data[_5792].w);
                                                                        if (any(lessThan(_5821, vec3(0.5))))
                                                                        {
                                                                            _5793 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _5793 = any(greaterThan(_5821, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                                                        }
                                                                        if (!(_5793))
                                                                        {
                                                                            _5748 = true;
                                                                            _5750 = p.sdfOrigin.data[_5792].w;
                                                                            _5790 = true;
                                                                            _5791 = ((textureLod(_7123, vec3(_5821.x / float(int(p.field.y)), _5821.y / float(int(p.field.y)), ((float(_5792) * float(int(p.field.y))) + _5821.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_5792];
                                                                            _5804_ladder_break = true;
                                                                        }
                                                                    }
                                                                }
                                                                if (_5804_ladder_break)
                                                                {
                                                                    break;
                                                                }
                                                                _5792++;
                                                                continue;
                                                            }
                                                            if (!(_5790))
                                                            {
                                                                _5790 = true;
                                                                _5791 = 1000000.0;
                                                            }
                                                        }
                                                        if (!_5748)
                                                        {
                                                            break;
                                                        }
                                                        voxel = _5750;
                                                        _5746++;
                                                        _5747 += max(_5791, 0.0);
                                                        continue;
                                                    }
                                                    hitRelative = _5553 + (_2246 * (_5747 + (voxel * 0.25)));
                                                    _5521 = true;
                                                    _5522 = true;
                                                    break;
                                                }
                                                if (_5633 > _5525)
                                                {
                                                    _5533 = _5525 < (_5531 * 0.0500000007450580596923828125);
                                                }
                                                else
                                                {
                                                    _5533 = false;
                                                }
                                                if (_5533)
                                                {
                                                    voxel = p.sdfOrigin.data[0].w;
                                                    int _5904 = 0;
                                                    float _5905 = _5524;
                                                    for (;;)
                                                    {
                                                        if (!(_5904 < 2))
                                                        {
                                                            break;
                                                        }
                                                        vec3 _5926 = _5553 + (_2246 * _5905);
                                                        _5948 = false;
                                                        {
                                                            _5906 = false;
                                                            _5908 = p.sdfOrigin.data[0].w;
                                                            int _5950 = 0;
                                                            for (;;)
                                                            {
                                                                bool _5962_ladder_break = false;
                                                                {
                                                                    if (!(_5950 < int(p.field.x)))
                                                                    {
                                                                        _5962_ladder_break = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        vec3 _5979 = (_5926 - p.sdfOrigin.data[_5950].xyz) / vec3(p.sdfOrigin.data[_5950].w);
                                                                        if (any(lessThan(_5979, vec3(0.5))))
                                                                        {
                                                                            _5951 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _5951 = any(greaterThan(_5979, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                                                        }
                                                                        if (!(_5951))
                                                                        {
                                                                            _5906 = true;
                                                                            _5908 = p.sdfOrigin.data[_5950].w;
                                                                            _5948 = true;
                                                                            _5949 = ((textureLod(_7123, vec3(_5979.x / float(int(p.field.y)), _5979.y / float(int(p.field.y)), ((float(_5950) * float(int(p.field.y))) + _5979.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_5950];
                                                                            _5962_ladder_break = true;
                                                                        }
                                                                    }
                                                                }
                                                                if (_5962_ladder_break)
                                                                {
                                                                    break;
                                                                }
                                                                _5950++;
                                                                continue;
                                                            }
                                                            if (!(_5948))
                                                            {
                                                                _5948 = true;
                                                                _5949 = 1000000.0;
                                                            }
                                                        }
                                                        if (!_5906)
                                                        {
                                                            break;
                                                        }
                                                        voxel = _5908;
                                                        _5904++;
                                                        _5905 += max(_5949, 0.0);
                                                        continue;
                                                    }
                                                    hitRelative = _5553 + (_2246 * (_5905 + (voxel * 0.25)));
                                                    _5521 = true;
                                                    _5522 = true;
                                                    break;
                                                }
                                            }
                                            _5523 = true;
                                            _5524 = _5526;
                                            _5526 += max(_5633, _5531 * 0.25);
                                            _5525 = _5633;
                                            _5527++;
                                            continue;
                                        }
                                        if (!(_5521))
                                        {
                                            _5521 = true;
                                            _5522 = false;
                                        }
                                    }
                                }
                                vec3 radiance = vec3(0.0);
                                if (!_5522)
                                {
                                    float _6640 = acos(clamp(sqrt(max(0.0, (p.misc.w * p.misc.w) - 40449600.0)) / p.misc.w, -1.0, 1.0));
                                    float _6641 = 3.1415927410125732421875 - _6640;
                                    float _6644 = acos(clamp(_2246.z, -1.0, 1.0));
                                    float _6630;
                                    if (_6644 < _6641)
                                    {
                                        _6630 = (1.0 - sqrt(max(0.0, 1.0 - (_6644 / _6641)))) * 0.5;
                                    }
                                    else
                                    {
                                        _6630 = (sqrt(max(0.0, (_6644 - _6641) / max(_6640, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                    }
                                    float _6664 = atan(_2246.y, _2246.x);
                                    float _6631;
                                    if (_6664 < 0.0)
                                    {
                                        _6631 = _6664 + 6.283185482025146484375;
                                    }
                                    else
                                    {
                                        _6631 = _6664;
                                    }
                                    uvec2 _6679 = uvec2(textureSize(_7119, 0));
                                    vec2 _6686 = vec2(float(_6679.x), float(_6679.y));
                                    vec3 _6692 = textureLod(_7121, (vec2(_6631 * 0.15915493667125701904296875, _6630) * ((_6686 - vec2(1.0)) / _6686)) + (vec2(0.5) / _6686), 0.0).xyz;
                                    if (_5331)
                                    {
                                        normalW = vec3(dot(_6692, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * 0.300000011920928955078125);
                                    }
                                    else
                                    {
                                        normalW = _6692;
                                    }
                                    float _6710 = clamp(textureLod(_7111, uv, 0.0).w, 0.0, 1.0);
                                    radiance = normalW * clamp((pow(_450 + _6710, roughness * roughness) - 1.0) + _6710, 0.0, 1.0);
                                }
                                else
                                {
                                    bool _6063;
                                    {
                                        radiance = vec3(0.0);
                                        vec3 _6078 = hitRelative * transpose(mat3(spvWorkaroundRowMajor(p.viewToWorld)[0].xyz, spvWorkaroundRowMajor(p.viewToWorld)[1].xyz, spvWorkaroundRowMajor(p.viewToWorld)[2].xyz));
                                        float _6079 = _6078.z;
                                        if (_6079 <= p.unproject.z)
                                        {
                                            _6063 = false;
                                        }
                                        else
                                        {
                                            float _6138 = _6078.z;
                                            vec2 _6149 = (vec2((_6078.x * p.unproject.x) / _6138, ((-_6078.y) * p.unproject.y) / _6138) * 0.5) + vec2(0.5);
                                            bool _6064;
                                            if (any(lessThan(_6149, vec2(0.0))))
                                            {
                                                _6064 = true;
                                            }
                                            else
                                            {
                                                _6064 = any(greaterThan(_6149, vec2(1.0)));
                                            }
                                            if (_6064)
                                            {
                                                _6063 = false;
                                            }
                                            else
                                            {
                                                vec4 _6105 = texelFetch(_7114, ivec3(ivec2(_6149 * p.resolution.xy), 0).xy, 0);
                                                float _6106 = _6105.x;
                                                if (_6106 <= 0.0)
                                                {
                                                    _6063 = false;
                                                }
                                                else
                                                {
                                                    if (_6106 >= 0.89999997615814208984375)
                                                    {
                                                        _6063 = false;
                                                    }
                                                    else
                                                    {
                                                        float _6173 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _6106)) * (p.unproject.w - p.unproject.z)));
                                                        if (abs(_6079 - _6173) > ((0.0199999995529651641845703125 * max(_6173, 1.0)) + voxel))
                                                        {
                                                            _6063 = false;
                                                        }
                                                        else
                                                        {
                                                            radiance = max(textureLod(_7116, _6149, 0.0).xyz, vec3(0.0));
                                                            _6063 = true;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (!_6063)
                                    {
                                        float _2940 = voxel * 2.0;
                                        float tolerance = 0.25 + _2940;
                                        vec3 _2947 = hitRelative + p.cacheOrigin.xyz;
                                        vec2 litFound;
                                        float agreement;
                                        float cardDepth;
                                        bool _6175;
                                        {
                                            cardDepth = 0.0;
                                            litFound = vec2(0.0);
                                            agreement = 1000000000.0;
                                            if (int(p.cacheGrid.w) <= 0)
                                            {
                                                _6175 = false;
                                            }
                                            else
                                            {
                                                ivec3 _6219 = ivec3(floor((_2947 - p.cacheGrid.xyz) / vec3(p.cacheOrigin.w)));
                                                int _6437 = int((((uint(_6219.x) * 2376512323u) ^ (uint(_6219.y) * 3625334849u)) ^ (uint(_6219.z) * 3407524639u)) & uint(int(p.cacheGrid.w) - 1));
                                                uvec2 _6224 = uvec2(textureSize(_7126, 0));
                                                vec2 _6231 = vec2(float(_6224.x), float(_6224.y));
                                                int _6234 = int(p.cacheGrid.w);
                                                bool _6178 = false;
                                                bool _6179 = false;
                                                int _6180 = 0;
                                                bool _6181;
                                                ivec2 _6182;
                                                bool _6186;
                                                bool _6195;
                                                bool _6196;
                                                bool _6197;
                                                bool _6198;
                                                bool _6199;
                                                ivec2 _6438;
                                                for (;;)
                                                {
                                                    if (_6180 <= uCells._member0[_6234].y)
                                                    {
                                                        _6181 = !_6178;
                                                    }
                                                    else
                                                    {
                                                        _6181 = false;
                                                    }
                                                    if (!_6181)
                                                    {
                                                        break;
                                                    }
                                                    bool _6253 = _6180 == 0;
                                                    if (_6253)
                                                    {
                                                        _6182 = uCells._member0[_6437];
                                                    }
                                                    else
                                                    {
                                                        int _6442 = uCells._member0[_6234].x + ((_6180 - 1) * 6);
                                                        float _6458 = intBitsToFloat(uCellPages_1._member0[_6442 + 3]);
                                                        vec3 _6460 = _2947 - vec3(intBitsToFloat(uCellPages_1._member0[_6442]), intBitsToFloat(uCellPages_1._member0[_6442 + 1]), intBitsToFloat(uCellPages_1._member0[_6442 + 2]));
                                                        if (dot(_6460, _6460) > (_6458 * _6458))
                                                        {
                                                            _6438 = ivec2(0);
                                                        }
                                                        else
                                                        {
                                                            _6438 = ivec2(uCellPages_1._member0[_6442 + 4], uCellPages_1._member0[_6442 + 5]);
                                                        }
                                                        _6182 = _6438;
                                                    }
                                                    bool _6183 = _6178;
                                                    bool _6184 = _6179;
                                                    int _6185 = 0;
                                                    for (;;)
                                                    {
                                                        bool _6265_ladder_break = false;
                                                        {
                                                            if (_6185 < _6182.y)
                                                            {
                                                                _6186 = !_6183;
                                                            }
                                                            else
                                                            {
                                                                _6186 = false;
                                                            }
                                                            if (!_6186)
                                                            {
                                                                _6265_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                int _6282 = _6182.x + _6185;
                                                                int _6285 = uCellPages_1._member0[_6282] * 10;
                                                                if (!((uPages_1._member0[_6285 + 5].w > 0.5) == _6253))
                                                                {
                                                                    int _6294 = _6285 + 9;
                                                                    vec3 _6298 = _2947 - uPages_1._member0[_6294].xyz;
                                                                    if (!(dot(_6298, _6298) > (uPages_1._member0[_6294].w * uPages_1._member0[_6294].w)))
                                                                    {
                                                                        int _6476 = uCellPages_1._member0[_6282] * 10;
                                                                        vec4 _6193 = uPages_1._member0[_6476];
                                                                        vec4 _6194 = uPages_1._member0[_6476 + 1];
                                                                        int _6482 = _6476 + 2;
                                                                        int _6485 = _6476 + 3;
                                                                        int _6488 = _6476 + 4;
                                                                        int _6494 = _6476 + 6;
                                                                        int _6497 = _6476 + 7;
                                                                        int _6500 = _6476 + 8;
                                                                        vec3 _6507 = _2947 - vec3(uPages_1._member0[_6494].w, uPages_1._member0[_6497].w, uPages_1._member0[_6500].w);
                                                                        vec3 _6192 = vec3(dot(uPages_1._member0[_6494].xyz, vec3(-0.0)), dot(uPages_1._member0[_6497].xyz, vec3(-0.0)), dot(uPages_1._member0[_6500].xyz, vec3(-0.0)));
                                                                        vec3 _6520 = vec3(dot(uPages_1._member0[_6494].xyz, _6507), dot(uPages_1._member0[_6497].xyz, _6507), dot(uPages_1._member0[_6500].xyz, _6507)) - uPages_1._member0[_6482].xyz;
                                                                        vec2 _6530 = vec2(dot(_6520, uPages_1._member0[_6485].xyz), dot(_6520, uPages_1._member0[_6488].xyz)) / max(vec2(uPages_1._member0[_6482].w, uPages_1._member0[_6485].w), vec2(9.9999999747524270787835121154785e-07));
                                                                        vec2 _6187 = _6530;
                                                                        float _6189 = -dot(_6520, uPages_1._member0[_6476 + 5].xyz);
                                                                        float _6190 = uPages_1._member0[_6488].w;
                                                                        vec3 _6191 = uPages_1._member0[_6476 + 5].xyz;
                                                                        if (abs(_6530.x) > 1.0)
                                                                        {
                                                                            _6195 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _6195 = abs(_6187.y) > 1.0;
                                                                        }
                                                                        if (!(_6195))
                                                                        {
                                                                            if (false)
                                                                            {
                                                                                _6196 = dot(_6191, _6192) < (-0.0500000007450580596923828125);
                                                                            }
                                                                            else
                                                                            {
                                                                                _6196 = false;
                                                                            }
                                                                            if (!(_6196))
                                                                            {
                                                                                if (_6189 < ((-0.25) - _2940))
                                                                                {
                                                                                    _6197 = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _6197 = _6189 > (_6190 + tolerance);
                                                                                }
                                                                                if (!(_6197))
                                                                                {
                                                                                    vec2 _6349 = (_6187 * 0.5) + vec2(0.5);
                                                                                    vec2 _6356 = (_6194.xy + (_6349 * _6194.zw)) / _6231;
                                                                                    if (!(textureLod(_7128, _6356, 0.0).w < 0.5))
                                                                                    {
                                                                                        float _6376 = abs((textureLod(_7130, _6356, 0.0).x * _6190) - _6189);
                                                                                        if (_6376 < agreement)
                                                                                        {
                                                                                            agreement = _6376;
                                                                                            cardDepth = _6190;
                                                                                            litFound = (_6193.xy + (_6349 * _6193.zw)) / vec3(p.cacheAtlas.xy, 0.0).xy;
                                                                                            _6198 = _6376 < 0.0199999995529651641845703125;
                                                                                            _6199 = true;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _6198 = _6183;
                                                                                            _6199 = _6184;
                                                                                        }
                                                                                        _6183 = _6198;
                                                                                        _6184 = _6199;
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                        if (_6265_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _6185++;
                                                        continue;
                                                    }
                                                    _6178 = _6183;
                                                    _6179 = _6184;
                                                    _6180++;
                                                    continue;
                                                }
                                                _6175 = _6179;
                                            }
                                        }
                                        if (_6175)
                                        {
                                            screenAllowed = agreement <= (tolerance + (cardDepth * 0.5));
                                        }
                                        else
                                        {
                                            screenAllowed = false;
                                        }
                                        if (screenAllowed)
                                        {
                                            radiance = max(textureLod(_7132, litFound, 0.0).xyz, vec3(0.0)) * p.trace.w;
                                            crossed = true;
                                        }
                                        else
                                        {
                                            crossed = _6063;
                                        }
                                    }
                                    else
                                    {
                                        crossed = _6063;
                                    }
                                    if (!crossed)
                                    {
                                        float _6549 = acos(clamp(sqrt(max(0.0, (p.misc.w * p.misc.w) - 40449600.0)) / p.misc.w, -1.0, 1.0));
                                        float _6550 = 3.1415927410125732421875 - _6549;
                                        float _6553 = acos(clamp(_2246.z, -1.0, 1.0));
                                        float _6539;
                                        if (_6553 < _6550)
                                        {
                                            _6539 = (1.0 - sqrt(max(0.0, 1.0 - (_6553 / _6550)))) * 0.5;
                                        }
                                        else
                                        {
                                            _6539 = (sqrt(max(0.0, (_6553 - _6550) / max(_6549, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                        }
                                        float _6573 = atan(_2246.y, _2246.x);
                                        float _6540;
                                        if (_6573 < 0.0)
                                        {
                                            _6540 = _6573 + 6.283185482025146484375;
                                        }
                                        else
                                        {
                                            _6540 = _6573;
                                        }
                                        uvec2 _6588 = uvec2(textureSize(_7119, 0));
                                        vec2 _6595 = vec2(float(_6588.x), float(_6588.y));
                                        vec3 _6601 = textureLod(_7121, (vec2(_6540 * 0.15915493667125701904296875, _6539) * ((_6595 - vec2(1.0)) / _6595)) + (vec2(0.5) / _6595), 0.0).xyz;
                                        if (_5331)
                                        {
                                            normalW = vec3(dot(_6601, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * 0.300000011920928955078125);
                                        }
                                        else
                                        {
                                            normalW = _6601;
                                        }
                                        float _6619 = clamp(textureLod(_7111, uv, 0.0).w, 0.0, 1.0);
                                        radiance = normalW * clamp((pow(_450 + _6619, roughness * roughness) - 1.0) + _6619, 0.0, 1.0);
                                    }
                                }
                                float _3522 = max(radiance.x, max(radiance.y, radiance.z));
                                if (_3522 > p.trace.z)
                                {
                                    radiance *= (p.trace.z / _3522);
                                }
                                if (water)
                                {
                                    float _6731 = acos(clamp(sqrt(max(0.0, (p.misc.w * p.misc.w) - 40449600.0)) / p.misc.w, -1.0, 1.0));
                                    float _6732 = 3.1415927410125732421875 - _6731;
                                    float _6735 = acos(clamp(_2246.z, -1.0, 1.0));
                                    float _6721;
                                    if (_6735 < _6732)
                                    {
                                        _6721 = (1.0 - sqrt(max(0.0, 1.0 - (_6735 / _6732)))) * 0.5;
                                    }
                                    else
                                    {
                                        _6721 = (sqrt(max(0.0, (_6735 - _6732) / max(_6731, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                    }
                                    float _6755 = atan(_2246.y, _2246.x);
                                    float _6722;
                                    if (_6755 < 0.0)
                                    {
                                        _6722 = _6755 + 6.283185482025146484375;
                                    }
                                    else
                                    {
                                        _6722 = _6755;
                                    }
                                    uvec2 _6770 = uvec2(textureSize(_7119, 0));
                                    vec2 _6777 = vec2(float(_6770.x), float(_6770.y));
                                    b = textureLod(_7121, (vec2(_6722 * 0.15915493667125701904296875, _6721) * ((_6777 - vec2(1.0)) / _6777)) + (vec2(0.5) / _6777), 0.0).xyz * clamp(_2246.z * 20.0, 0.0, 1.0);
                                }
                                else
                                {
                                    b = vec3(0.0);
                                }
                                float _3550 = screenWeight;
                                screenWeight = 1.0;
                                a += (((radiance - b) * up) * (1.0 - _3550));
                            }
                            if (!raw)
                            {
                                screenAllowed = !water;
                            }
                            else
                            {
                                screenAllowed = false;
                            }
                            if (screenAllowed)
                            {
                                screenAllowed = roughness > 0.0500000007450580596923828125;
                            }
                            else
                            {
                                screenAllowed = false;
                            }
                            float spread;
                            if (screenAllowed)
                            {
                                spread = 2.0 + roughness;
                            }
                            else
                            {
                                if (traces)
                                {
                                    marches = true;
                                }
                                if (marches)
                                {
                                    puddle = 1.0;
                                }
                                else
                                {
                                    puddle = screenWeight;
                                }
                                spread = puddle;
                            }
                            imageStore(uReflection, ivec2(gl_GlobalInvocationID.xy), vec4(a, spread));
                        }
                    }
                }
            }
        }
    }
}

