#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

struct _Array_std140_vector_float_4_4
{
    vec4 data[4];
};

struct _Array_std140_float4
{
    float data[4];
};

layout(binding = 0, std140) uniform Params_std140
{
    layout(row_major) mat4 viewToWorld;
    vec4 unproject;
    vec4 resolution;
    vec4 trace;
    vec4 cacheOrigin;
    vec4 cacheGrid;
    vec4 cacheAtlas;
    vec4 debug;
    vec4 cameraWorld;
    uvec4 records;
    vec4 sun;
    vec4 sunColour;
    vec4 ambient;
    vec4 field;
    _Array_std140_vector_float_4_4 sdfOrigin;
    _Array_std140_float4 sdfRange;
    vec4 shelter0;
    vec4 shelter1;
    vec4 shelter2;
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

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uOut;
layout(binding = 0) uniform sampler2D _4810;
layout(binding = 1) uniform sampler2D _4813;
layout(binding = 2) uniform sampler2D _4815;
layout(binding = 8) uniform sampler2D _4818;
layout(binding = 7) uniform sampler3D _4820;
layout(binding = 3) uniform sampler2D _4823;
layout(binding = 4) uniform sampler2D _4825;
layout(binding = 5) uniform sampler2D _4827;
layout(binding = 6) uniform sampler2D _4829;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    bool _3874 = false;
    bool _3760 = false;
    bool _3646 = false;
    bool _3532 = false;
    bool _3418 = false;
    bool _3304 = false;
    bool _2924 = false;
    bool _2766 = false;
    bool _2608 = false;
    bool _2497 = false;
    {
        int gRoute = 0;
        ivec2 _46 = ivec2(gl_GlobalInvocationID.xy);
        bool _7;
        if (_46.x >= int(p.resolution.x))
        {
            _7 = true;
        }
        else
        {
            _7 = _46.y >= int(p.resolution.y);
        }
        if (!(_7))
        {
            vec4 sampled = texelFetch(_4810, ivec3(_46, 0).xy, 0);
            float _89 = sampled.w;
            if (_89 <= 0.0)
            {
                imageStore(uOut, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
            }
            else
            {
                vec3 _102 = normalize(sampled.xyz);
                vec2 ndc = (((vec2(_46) + vec2(0.5)) * p.resolution.zw) * 2.0) - vec2(1.0);
                vec3 _142 = vec3((ndc.x * _89) / p.unproject.x, ((-ndc.y) * _89) / p.unproject.y, _89) * mat3(spvWorkaroundRowMajor(p.viewToWorld)[0].xyz, spvWorkaroundRowMajor(p.viewToWorld)[1].xyz, spvWorkaroundRowMajor(p.viewToWorld)[2].xyz);
                vec3 _143 = normalize(_142);
                vec3 normalW;
                if (dot(_102, _143) > 0.0)
                {
                    normalW = -_102;
                }
                else
                {
                    normalW = _102;
                }
                vec3 _154 = normalize(reflect(_143, normalW));
                float _162 = 0.959999978542327880859375 * pow(1.0 - clamp(dot(normalW, -_143), 0.0, 1.0), 5.0);
                vec3 _2069;
                {
                    gRoute = 0;
                    if (int(p.field.x) <= 0)
                    {
                        uint _2178 = uvec2(textureSize(_4813, 0)).y;
                        float _2200 = acos(clamp(sqrt(max(0.0, (p.trace.w * p.trace.w) - 40449600.0)) / p.trace.w, -1.0, 1.0));
                        float _2201 = 3.1415927410125732421875 - _2200;
                        float _2204 = acos(clamp(_154.z, -1.0, 1.0));
                        float _2190;
                        if (_2204 < _2201)
                        {
                            _2190 = (1.0 - sqrt(max(0.0, 1.0 - (_2204 / _2201)))) * 0.5;
                        }
                        else
                        {
                            _2190 = (sqrt(max(0.0, (_2204 - _2201) / max(_2200, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                        }
                        float _2224 = atan(_154.y, _154.x);
                        float _2191;
                        if (_2224 < 0.0)
                        {
                            _2191 = _2224 + 6.283185482025146484375;
                        }
                        else
                        {
                            _2191 = _2224;
                        }
                        vec2 _2233 = vec2(_2191 * 0.15915493667125701904296875, _2190);
                        float _2187 = float(_2178);
                        _2233.y = min(_2233.y, (floor(_2187 * 0.5) - 1.0) / max(_2187 - 1.0, 1.0));
                        uvec2 _2252 = uvec2(textureSize(_4813, 0));
                        vec2 _2259 = vec2(float(_2252.x), float(_2252.y));
                        _2069 = textureLod(_4815, (_2233 * ((_2259 - vec2(1.0)) / _2259)) + (vec2(0.5) / _2259), 0.0).xyz * smoothstep(-0.100000001490116119384765625, 0.25, _154.z);
                    }
                    else
                    {
                        vec2 _2092 = _142.xy + p.cameraWorld.xy;
                        float _2096 = _142.z + p.cameraWorld.z;
                        vec2 _4776 = p.shelter0.xy;
                        float _4777 = p.shelter0.z;
                        float _4778 = p.shelter0.w;
                        float _4779 = p.shelter1.x;
                        float _4780 = p.shelter1.y;
                        vec2 _4782 = p.shelter2.xy;
                        float _4783 = p.shelter1.w;
                        bool _2307;
                        {
                            ivec2 _2360 = ivec2(floor((_2092 - _4776) * _4777));
                            bool _2361;
                            if (_4783 > 0.5)
                            {
                                _2361 = _2360.x >= 0;
                            }
                            else
                            {
                                _2361 = false;
                            }
                            if (_2361)
                            {
                                _2361 = _2360.y >= 0;
                            }
                            else
                            {
                                _2361 = false;
                            }
                            if (_2361)
                            {
                                _2361 = _2360.x < int(_4782.x);
                            }
                            else
                            {
                                _2361 = false;
                            }
                            if (_2361)
                            {
                                _2361 = _2360.y < int(_4782.y);
                            }
                            else
                            {
                                _2361 = false;
                            }
                            if (!_2361)
                            {
                                _2307 = false;
                            }
                            else
                            {
                                vec4 _2319 = texelFetch(_4818, ivec3(_2360, 0).xy, 0);
                                float _2411 = floor((_2319.z * 255.0) + 0.5) + (floor((_2319.y * 255.0) + 0.5) * 256.0);
                                float _2399;
                                if (_2411 < 0.5)
                                {
                                    _2399 = -1000000015047466219876688855040.0;
                                }
                                else
                                {
                                    _2399 = _4778 + ((_2411 - 1.0) * _4779);
                                }
                                float _2324 = _2096 + _4780;
                                if (_2399 <= _2324)
                                {
                                    _2307 = false;
                                }
                                else
                                {
                                    float _2328 = _154.z;
                                    if (_2328 <= 0.0500000007450580596923828125)
                                    {
                                        _2307 = false;
                                    }
                                    else
                                    {
                                        ivec2 _2433 = ivec2(floor(((_2092 + (_154.xy * ((_2399 - _2096) / _2328))) - _4776) * _4777));
                                        bool _2434;
                                        if (_4783 > 0.5)
                                        {
                                            _2434 = _2433.x >= 0;
                                        }
                                        else
                                        {
                                            _2434 = false;
                                        }
                                        if (_2434)
                                        {
                                            _2434 = _2433.y >= 0;
                                        }
                                        else
                                        {
                                            _2434 = false;
                                        }
                                        if (_2434)
                                        {
                                            _2434 = _2433.x < int(_4782.x);
                                        }
                                        else
                                        {
                                            _2434 = false;
                                        }
                                        if (_2434)
                                        {
                                            _2434 = _2433.y < int(_4782.y);
                                        }
                                        else
                                        {
                                            _2434 = false;
                                        }
                                        if (!_2434)
                                        {
                                            _2307 = false;
                                        }
                                        else
                                        {
                                            vec4 _2345 = texelFetch(_4818, ivec3(_2433, 0).xy, 0);
                                            float _2484 = floor((_2345.z * 255.0) + 0.5) + (floor((_2345.y * 255.0) + 0.5) * 256.0);
                                            float _2472;
                                            if (_2484 < 0.5)
                                            {
                                                _2472 = -1000000015047466219876688855040.0;
                                            }
                                            else
                                            {
                                                _2472 = _4778 + ((_2484 - 1.0) * _4779);
                                            }
                                            _2307 = _2472 > _2324;
                                        }
                                    }
                                }
                            }
                        }
                        _2497 = false;
                        vec3 _2071;
                        float _2072;
                        bool _2498;
                        {
                            _2071 = _142;
                            _2072 = p.sdfOrigin.data[0].w;
                            if (int(p.field.x) <= 0)
                            {
                                _2497 = true;
                                _2498 = false;
                            }
                            else
                            {
                                vec3 _2529 = _142 + ((normalW * p.sdfOrigin.data[0].w) * 1.5);
                                bool _2499 = false;
                                float _2500 = 0.0;
                                float _2501 = 0.0;
                                int _2503 = 0;
                                float _2502 = 0.0;
                                bool _2504;
                                bool _2505;
                                float _2507;
                                float _2508;
                                bool _2509;
                                float _2609;
                                bool _2611;
                                bool _2724;
                                float _2726;
                                float _2767;
                                bool _2769;
                                bool _2882;
                                float _2884;
                                float _2925;
                                bool _2927;
                                for (;;)
                                {
                                    if (_2503 < 96)
                                    {
                                        _2504 = _2502 < p.trace.y;
                                    }
                                    else
                                    {
                                        _2504 = false;
                                    }
                                    if (!_2504)
                                    {
                                        break;
                                    }
                                    vec3 _2546 = _2529 + (_154 * _2502);
                                    _2608 = false;
                                    {
                                        _2505 = false;
                                        _2507 = p.sdfOrigin.data[0].w;
                                        int _2610 = 0;
                                        for (;;)
                                        {
                                            bool _2622_ladder_break = false;
                                            {
                                                if (!(_2610 < int(p.field.x)))
                                                {
                                                    _2622_ladder_break = true;
                                                }
                                                else
                                                {
                                                    vec3 _2639 = (_2546 - p.sdfOrigin.data[_2610].xyz) / vec3(p.sdfOrigin.data[_2610].w);
                                                    if (any(lessThan(_2639, vec3(0.5))))
                                                    {
                                                        _2611 = true;
                                                    }
                                                    else
                                                    {
                                                        _2611 = any(greaterThan(_2639, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                                    }
                                                    if (!(_2611))
                                                    {
                                                        _2505 = true;
                                                        _2507 = p.sdfOrigin.data[_2610].w;
                                                        _2608 = true;
                                                        _2609 = ((textureLod(_4820, vec3(_2639.x / float(int(p.field.y)), _2639.y / float(int(p.field.y)), ((float(_2610) * float(int(p.field.y))) + _2639.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_2610];
                                                        _2622_ladder_break = true;
                                                    }
                                                }
                                            }
                                            if (_2622_ladder_break)
                                            {
                                                break;
                                            }
                                            _2610++;
                                            continue;
                                        }
                                        if (!(_2608))
                                        {
                                            _2608 = true;
                                            _2609 = 1000000.0;
                                        }
                                    }
                                    if (!_2505)
                                    {
                                        _2497 = true;
                                        _2498 = false;
                                        break;
                                    }
                                    if (_2499)
                                    {
                                        if (_2609 <= 0.0)
                                        {
                                            float _2560 = _2501 - _2609;
                                            if (_2560 > 9.9999997473787516355514526367188e-06)
                                            {
                                                _2508 = _2500 + ((_2502 - _2500) * (_2501 / _2560));
                                            }
                                            else
                                            {
                                                _2508 = _2502;
                                            }
                                            _2072 = p.sdfOrigin.data[0].w;
                                            int _2722 = 0;
                                            float _2723 = clamp(_2508, _2500, _2502);
                                            for (;;)
                                            {
                                                if (!(_2722 < 2))
                                                {
                                                    break;
                                                }
                                                vec3 _2744 = _2529 + (_154 * _2723);
                                                _2766 = false;
                                                {
                                                    _2724 = false;
                                                    _2726 = p.sdfOrigin.data[0].w;
                                                    int _2768 = 0;
                                                    for (;;)
                                                    {
                                                        bool _2780_ladder_break = false;
                                                        {
                                                            if (!(_2768 < int(p.field.x)))
                                                            {
                                                                _2780_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                vec3 _2797 = (_2744 - p.sdfOrigin.data[_2768].xyz) / vec3(p.sdfOrigin.data[_2768].w);
                                                                if (any(lessThan(_2797, vec3(0.5))))
                                                                {
                                                                    _2769 = true;
                                                                }
                                                                else
                                                                {
                                                                    _2769 = any(greaterThan(_2797, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                                                }
                                                                if (!(_2769))
                                                                {
                                                                    _2724 = true;
                                                                    _2726 = p.sdfOrigin.data[_2768].w;
                                                                    _2766 = true;
                                                                    _2767 = ((textureLod(_4820, vec3(_2797.x / float(int(p.field.y)), _2797.y / float(int(p.field.y)), ((float(_2768) * float(int(p.field.y))) + _2797.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_2768];
                                                                    _2780_ladder_break = true;
                                                                }
                                                            }
                                                        }
                                                        if (_2780_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _2768++;
                                                        continue;
                                                    }
                                                    if (!(_2766))
                                                    {
                                                        _2766 = true;
                                                        _2767 = 1000000.0;
                                                    }
                                                }
                                                if (!_2724)
                                                {
                                                    break;
                                                }
                                                _2072 = _2726;
                                                _2722++;
                                                _2723 += max(_2767, 0.0);
                                                continue;
                                            }
                                            _2071 = _2529 + (_154 * (_2723 + (_2072 * 0.25)));
                                            _2497 = true;
                                            _2498 = true;
                                            break;
                                        }
                                        if (_2609 > _2501)
                                        {
                                            _2509 = _2501 < (_2507 * 0.0500000007450580596923828125);
                                        }
                                        else
                                        {
                                            _2509 = false;
                                        }
                                        if (_2509)
                                        {
                                            _2072 = p.sdfOrigin.data[0].w;
                                            int _2880 = 0;
                                            float _2881 = _2500;
                                            for (;;)
                                            {
                                                if (!(_2880 < 2))
                                                {
                                                    break;
                                                }
                                                vec3 _2902 = _2529 + (_154 * _2881);
                                                _2924 = false;
                                                {
                                                    _2882 = false;
                                                    _2884 = p.sdfOrigin.data[0].w;
                                                    int _2926 = 0;
                                                    for (;;)
                                                    {
                                                        bool _2938_ladder_break = false;
                                                        {
                                                            if (!(_2926 < int(p.field.x)))
                                                            {
                                                                _2938_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                vec3 _2955 = (_2902 - p.sdfOrigin.data[_2926].xyz) / vec3(p.sdfOrigin.data[_2926].w);
                                                                if (any(lessThan(_2955, vec3(0.5))))
                                                                {
                                                                    _2927 = true;
                                                                }
                                                                else
                                                                {
                                                                    _2927 = any(greaterThan(_2955, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                                                }
                                                                if (!(_2927))
                                                                {
                                                                    _2882 = true;
                                                                    _2884 = p.sdfOrigin.data[_2926].w;
                                                                    _2924 = true;
                                                                    _2925 = ((textureLod(_4820, vec3(_2955.x / float(int(p.field.y)), _2955.y / float(int(p.field.y)), ((float(_2926) * float(int(p.field.y))) + _2955.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_2926];
                                                                    _2938_ladder_break = true;
                                                                }
                                                            }
                                                        }
                                                        if (_2938_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _2926++;
                                                        continue;
                                                    }
                                                    if (!(_2924))
                                                    {
                                                        _2924 = true;
                                                        _2925 = 1000000.0;
                                                    }
                                                }
                                                if (!_2882)
                                                {
                                                    break;
                                                }
                                                _2072 = _2884;
                                                _2880++;
                                                _2881 += max(_2925, 0.0);
                                                continue;
                                            }
                                            _2071 = _2529 + (_154 * (_2881 + (_2072 * 0.25)));
                                            _2497 = true;
                                            _2498 = true;
                                            break;
                                        }
                                    }
                                    _2499 = true;
                                    _2500 = _2502;
                                    _2502 += max(_2609, _2507 * 0.25);
                                    _2501 = _2609;
                                    _2503++;
                                    continue;
                                }
                                if (!(_2497))
                                {
                                    _2497 = true;
                                    _2498 = false;
                                }
                            }
                        }
                        if (!_2498)
                        {
                            if (!_2307)
                            {
                                uint _3049 = uvec2(textureSize(_4813, 0)).y;
                                float _3071 = acos(clamp(sqrt(max(0.0, (p.trace.w * p.trace.w) - 40449600.0)) / p.trace.w, -1.0, 1.0));
                                float _3072 = 3.1415927410125732421875 - _3071;
                                float _3075 = acos(clamp(_154.z, -1.0, 1.0));
                                float _3061;
                                if (_3075 < _3072)
                                {
                                    _3061 = (1.0 - sqrt(max(0.0, 1.0 - (_3075 / _3072)))) * 0.5;
                                }
                                else
                                {
                                    _3061 = (sqrt(max(0.0, (_3075 - _3072) / max(_3071, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                }
                                float _3095 = atan(_154.y, _154.x);
                                float _3062;
                                if (_3095 < 0.0)
                                {
                                    _3062 = _3095 + 6.283185482025146484375;
                                }
                                else
                                {
                                    _3062 = _3095;
                                }
                                vec2 _3104 = vec2(_3062 * 0.15915493667125701904296875, _3061);
                                float _3058 = float(_3049);
                                _3104.y = min(_3104.y, (floor(_3058 * 0.5) - 1.0) / max(_3058 - 1.0, 1.0));
                                uvec2 _3123 = uvec2(textureSize(_4813, 0));
                                vec2 _3130 = vec2(float(_3123.x), float(_3123.y));
                                _2069 = textureLod(_4815, (_3104 * ((_3130 - vec2(1.0)) / _3130)) + (vec2(0.5) / _3130), 0.0).xyz * smoothstep(-0.100000001490116119384765625, 0.25, _154.z);
                            }
                            else
                            {
                                gRoute = 3;
                                uint _3167 = uvec2(textureSize(_4813, 0)).y;
                                float _3189 = acos(clamp(sqrt(max(0.0, (p.trace.w * p.trace.w) - 40449600.0)) / p.trace.w, -1.0, 1.0));
                                float _3190 = 3.1415927410125732421875 - _3189;
                                float _3193 = acos(clamp(_154.z, -1.0, 1.0));
                                float _3179;
                                if (_3193 < _3190)
                                {
                                    _3179 = (1.0 - sqrt(max(0.0, 1.0 - (_3193 / _3190)))) * 0.5;
                                }
                                else
                                {
                                    _3179 = (sqrt(max(0.0, (_3193 - _3190) / max(_3189, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                }
                                float _3213 = atan(_154.y, _154.x);
                                float _3180;
                                if (_3213 < 0.0)
                                {
                                    _3180 = _3213 + 6.283185482025146484375;
                                }
                                else
                                {
                                    _3180 = _3213;
                                }
                                vec2 _3222 = vec2(_3180 * 0.15915493667125701904296875, _3179);
                                float _3176 = float(_3167);
                                _3222.y = min(_3222.y, (floor(_3176 * 0.5) - 1.0) / max(_3176 - 1.0, 1.0));
                                uvec2 _3241 = uvec2(textureSize(_4813, 0));
                                vec2 _3248 = vec2(float(_3241.x), float(_3241.y));
                                _2069 = vec3(dot(textureLod(_4815, (_3222 * ((_3248 - vec2(1.0)) / _3248)) + (vec2(0.5) / _3248), 0.0).xyz * smoothstep(-0.100000001490116119384765625, 0.25, _154.z), vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * 0.300000011920928955078125) + p.ambient.xyz;
                            }
                        }
                        else
                        {
                            float _3271 = max(_2072, 0.001000000047497451305389404296875);
                            vec3 _3272 = vec3(_3271, 0.0, 0.0);
                            vec3 _3273 = _2071 + _3272;
                            _3304 = false;
                            float _3305;
                            {
                                int _3306 = 0;
                                bool _3307;
                                for (;;)
                                {
                                    bool _3318_ladder_break = false;
                                    {
                                        if (!(_3306 < int(p.field.x)))
                                        {
                                            _3318_ladder_break = true;
                                        }
                                        else
                                        {
                                            vec3 _3335 = (_3273 - p.sdfOrigin.data[_3306].xyz) / vec3(p.sdfOrigin.data[_3306].w);
                                            if (any(lessThan(_3335, vec3(0.5))))
                                            {
                                                _3307 = true;
                                            }
                                            else
                                            {
                                                _3307 = any(greaterThan(_3335, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                            }
                                            if (!(_3307))
                                            {
                                                _3304 = true;
                                                _3305 = ((textureLod(_4820, vec3(_3335.x / float(int(p.field.y)), _3335.y / float(int(p.field.y)), ((float(_3306) * float(int(p.field.y))) + _3335.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_3306];
                                                _3318_ladder_break = true;
                                            }
                                        }
                                    }
                                    if (_3318_ladder_break)
                                    {
                                        break;
                                    }
                                    _3306++;
                                    continue;
                                }
                                if (!(_3304))
                                {
                                    _3304 = true;
                                    _3305 = 1000000.0;
                                }
                            }
                            vec3 _3275 = _2071 - _3272;
                            _3418 = false;
                            float _3419;
                            {
                                int _3420 = 0;
                                bool _3421;
                                for (;;)
                                {
                                    bool _3432_ladder_break = false;
                                    {
                                        if (!(_3420 < int(p.field.x)))
                                        {
                                            _3432_ladder_break = true;
                                        }
                                        else
                                        {
                                            vec3 _3449 = (_3275 - p.sdfOrigin.data[_3420].xyz) / vec3(p.sdfOrigin.data[_3420].w);
                                            if (any(lessThan(_3449, vec3(0.5))))
                                            {
                                                _3421 = true;
                                            }
                                            else
                                            {
                                                _3421 = any(greaterThan(_3449, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                            }
                                            if (!(_3421))
                                            {
                                                _3418 = true;
                                                _3419 = ((textureLod(_4820, vec3(_3449.x / float(int(p.field.y)), _3449.y / float(int(p.field.y)), ((float(_3420) * float(int(p.field.y))) + _3449.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_3420];
                                                _3432_ladder_break = true;
                                            }
                                        }
                                    }
                                    if (_3432_ladder_break)
                                    {
                                        break;
                                    }
                                    _3420++;
                                    continue;
                                }
                                if (!(_3418))
                                {
                                    _3418 = true;
                                    _3419 = 1000000.0;
                                }
                            }
                            vec3 _3278 = vec3(0.0, _3271, 0.0);
                            vec3 _3279 = _2071 + _3278;
                            _3532 = false;
                            float _3533;
                            {
                                int _3534 = 0;
                                bool _3535;
                                for (;;)
                                {
                                    bool _3546_ladder_break = false;
                                    {
                                        if (!(_3534 < int(p.field.x)))
                                        {
                                            _3546_ladder_break = true;
                                        }
                                        else
                                        {
                                            vec3 _3563 = (_3279 - p.sdfOrigin.data[_3534].xyz) / vec3(p.sdfOrigin.data[_3534].w);
                                            if (any(lessThan(_3563, vec3(0.5))))
                                            {
                                                _3535 = true;
                                            }
                                            else
                                            {
                                                _3535 = any(greaterThan(_3563, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                            }
                                            if (!(_3535))
                                            {
                                                _3532 = true;
                                                _3533 = ((textureLod(_4820, vec3(_3563.x / float(int(p.field.y)), _3563.y / float(int(p.field.y)), ((float(_3534) * float(int(p.field.y))) + _3563.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_3534];
                                                _3546_ladder_break = true;
                                            }
                                        }
                                    }
                                    if (_3546_ladder_break)
                                    {
                                        break;
                                    }
                                    _3534++;
                                    continue;
                                }
                                if (!(_3532))
                                {
                                    _3532 = true;
                                    _3533 = 1000000.0;
                                }
                            }
                            vec3 _3281 = _2071 - _3278;
                            _3646 = false;
                            float _3647;
                            {
                                int _3648 = 0;
                                bool _3649;
                                for (;;)
                                {
                                    bool _3660_ladder_break = false;
                                    {
                                        if (!(_3648 < int(p.field.x)))
                                        {
                                            _3660_ladder_break = true;
                                        }
                                        else
                                        {
                                            vec3 _3677 = (_3281 - p.sdfOrigin.data[_3648].xyz) / vec3(p.sdfOrigin.data[_3648].w);
                                            if (any(lessThan(_3677, vec3(0.5))))
                                            {
                                                _3649 = true;
                                            }
                                            else
                                            {
                                                _3649 = any(greaterThan(_3677, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                            }
                                            if (!(_3649))
                                            {
                                                _3646 = true;
                                                _3647 = ((textureLod(_4820, vec3(_3677.x / float(int(p.field.y)), _3677.y / float(int(p.field.y)), ((float(_3648) * float(int(p.field.y))) + _3677.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_3648];
                                                _3660_ladder_break = true;
                                            }
                                        }
                                    }
                                    if (_3660_ladder_break)
                                    {
                                        break;
                                    }
                                    _3648++;
                                    continue;
                                }
                                if (!(_3646))
                                {
                                    _3646 = true;
                                    _3647 = 1000000.0;
                                }
                            }
                            vec3 _3284 = vec3(0.0, 0.0, _3271);
                            vec3 _3285 = _2071 + _3284;
                            _3760 = false;
                            float _3761;
                            {
                                int _3762 = 0;
                                bool _3763;
                                for (;;)
                                {
                                    bool _3774_ladder_break = false;
                                    {
                                        if (!(_3762 < int(p.field.x)))
                                        {
                                            _3774_ladder_break = true;
                                        }
                                        else
                                        {
                                            vec3 _3791 = (_3285 - p.sdfOrigin.data[_3762].xyz) / vec3(p.sdfOrigin.data[_3762].w);
                                            if (any(lessThan(_3791, vec3(0.5))))
                                            {
                                                _3763 = true;
                                            }
                                            else
                                            {
                                                _3763 = any(greaterThan(_3791, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                            }
                                            if (!(_3763))
                                            {
                                                _3760 = true;
                                                _3761 = ((textureLod(_4820, vec3(_3791.x / float(int(p.field.y)), _3791.y / float(int(p.field.y)), ((float(_3762) * float(int(p.field.y))) + _3791.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_3762];
                                                _3774_ladder_break = true;
                                            }
                                        }
                                    }
                                    if (_3774_ladder_break)
                                    {
                                        break;
                                    }
                                    _3762++;
                                    continue;
                                }
                                if (!(_3760))
                                {
                                    _3760 = true;
                                    _3761 = 1000000.0;
                                }
                            }
                            vec3 _3287 = _2071 - _3284;
                            _3874 = false;
                            float _3875;
                            {
                                int _3876 = 0;
                                bool _3877;
                                for (;;)
                                {
                                    bool _3888_ladder_break = false;
                                    {
                                        if (!(_3876 < int(p.field.x)))
                                        {
                                            _3888_ladder_break = true;
                                        }
                                        else
                                        {
                                            vec3 _3905 = (_3287 - p.sdfOrigin.data[_3876].xyz) / vec3(p.sdfOrigin.data[_3876].w);
                                            if (any(lessThan(_3905, vec3(0.5))))
                                            {
                                                _3877 = true;
                                            }
                                            else
                                            {
                                                _3877 = any(greaterThan(_3905, vec3(float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5, float(int(p.field.y)) - 0.5)));
                                            }
                                            if (!(_3877))
                                            {
                                                _3874 = true;
                                                _3875 = ((textureLod(_4820, vec3(_3905.x / float(int(p.field.y)), _3905.y / float(int(p.field.y)), ((float(_3876) * float(int(p.field.y))) + _3905.z) / float(int(p.field.y) * int(p.field.x))), 0.0).x * 2.0) - 1.0) * p.sdfRange.data[_3876];
                                                _3888_ladder_break = true;
                                            }
                                        }
                                    }
                                    if (_3888_ladder_break)
                                    {
                                        break;
                                    }
                                    _3876++;
                                    continue;
                                }
                                if (!(_3874))
                                {
                                    _3874 = true;
                                    _3875 = 1000000.0;
                                }
                            }
                            vec3 _3290 = vec3(_3305 - _3419, _3533 - _3647, _3761 - _3875);
                            float _3291 = dot(_3290, _3290);
                            bool _3267;
                            if (_3291 > 9.9999999600419720025001879548654e-13)
                            {
                                _3267 = _3291 < 1000000.0;
                            }
                            else
                            {
                                _3267 = false;
                            }
                            vec3 _3268;
                            if (_3267)
                            {
                                _3268 = _3290 * inversesqrt(_3291);
                            }
                            else
                            {
                                _3268 = vec3(0.0);
                            }
                            vec3 _2073;
                            if (dot(_3268, _3268) <= 0.0)
                            {
                                _2073 = -_154;
                            }
                            else
                            {
                                _2073 = _3268;
                            }
                            vec3 _2123 = _2071 + p.cacheOrigin.xyz;
                            float _2126 = 0.25 + (_2072 * 2.0);
                            vec3 _2074;
                            bool _3993;
                            {
                                _2074 = vec3(0.0);
                                if (int(p.cacheGrid.w) <= 0)
                                {
                                    _3993 = false;
                                }
                                else
                                {
                                    ivec3 _4035 = ivec3(floor((_2123 - p.cacheGrid.xyz) / vec3(p.cacheOrigin.w)));
                                    int _4388 = int((((uint(_4035.x) * 2376512323u) ^ (uint(_4035.y) * 3625334849u)) ^ (uint(_4035.z) * 3407524639u)) & uint(int(p.cacheGrid.w) - 1));
                                    uvec2 _4040 = uvec2(textureSize(_4823, 0));
                                    vec2 _4047 = vec2(float(_4040.x), float(_4040.y));
                                    int _4050 = int(p.cacheGrid.w);
                                    int _3996 = 0;
                                    float _3997 = 0.0;
                                    ivec2 _3998;
                                    bool _4009;
                                    bool _4010;
                                    vec3 _4012[4];
                                    float _4015;
                                    ivec2 _4389;
                                    for (;;)
                                    {
                                        if (!(_3996 <= uCells._member0[_4050].y))
                                        {
                                            break;
                                        }
                                        bool _4063 = _3996 == 0;
                                        if (_4063)
                                        {
                                            _3998 = uCells._member0[_4388];
                                        }
                                        else
                                        {
                                            int _4393 = uCells._member0[_4050].x + ((_3996 - 1) * 6);
                                            float _4410 = intBitsToFloat(uCellPages_1._member0[_4393 + 3]) + _2126;
                                            vec3 _4411 = _2123 - vec3(intBitsToFloat(uCellPages_1._member0[_4393]), intBitsToFloat(uCellPages_1._member0[_4393 + 1]), intBitsToFloat(uCellPages_1._member0[_4393 + 2]));
                                            if (dot(_4411, _4411) > (_4410 * _4410))
                                            {
                                                _4389 = ivec2(0);
                                            }
                                            else
                                            {
                                                _4389 = ivec2(uCellPages_1._member0[_4393 + 4], uCellPages_1._member0[_4393 + 5]);
                                            }
                                            _3998 = _4389;
                                        }
                                        int _3999 = 0;
                                        float _4000 = _3997;
                                        for (;;)
                                        {
                                            bool _4074_ladder_break = false;
                                            {
                                                if (!(_3999 < _3998.y))
                                                {
                                                    _4074_ladder_break = true;
                                                }
                                                else
                                                {
                                                    int _4085 = _3998.x + _3999;
                                                    int _4088 = uCellPages_1._member0[_4085] * 10;
                                                    if (!((uPages_1._member0[_4088 + 5].w > 0.5) == _4063))
                                                    {
                                                        int _4097 = _4088 + 9;
                                                        vec3 _4101 = _2123 - uPages_1._member0[_4097].xyz;
                                                        float _4103 = uPages_1._member0[_4097].w + _2126;
                                                        if (!(dot(_4101, _4101) > (_4103 * _4103)))
                                                        {
                                                            int _4427 = uCellPages_1._member0[_4085] * 10;
                                                            vec4 _4007 = uPages_1._member0[_4427];
                                                            vec4 _4008 = uPages_1._member0[_4427 + 1];
                                                            int _4433 = _4427 + 2;
                                                            int _4436 = _4427 + 3;
                                                            int _4439 = _4427 + 4;
                                                            int _4445 = _4427 + 6;
                                                            int _4448 = _4427 + 7;
                                                            int _4451 = _4427 + 8;
                                                            vec3 _4458 = _2123 - vec3(uPages_1._member0[_4445].w, uPages_1._member0[_4448].w, uPages_1._member0[_4451].w);
                                                            vec3 _4471 = vec3(dot(uPages_1._member0[_4445].xyz, _4458), dot(uPages_1._member0[_4448].xyz, _4458), dot(uPages_1._member0[_4451].xyz, _4458)) - uPages_1._member0[_4433].xyz;
                                                            vec2 _4475 = max(vec2(uPages_1._member0[_4433].w, uPages_1._member0[_4436].w), vec2(9.9999999747524270787835121154785e-07));
                                                            vec2 _4002 = _4475;
                                                            vec2 _4001 = vec2(dot(_4471, uPages_1._member0[_4436].xyz), dot(_4471, uPages_1._member0[_4439].xyz)) / _4475;
                                                            float _4003 = -dot(_4471, uPages_1._member0[_4427 + 5].xyz);
                                                            float _4004 = uPages_1._member0[_4439].w;
                                                            float _4112 = dot(uPages_1._member0[_4427 + 5].xyz, vec3(dot(uPages_1._member0[_4445].xyz, _2073), dot(uPages_1._member0[_4448].xyz, _2073), dot(uPages_1._member0[_4451].xyz, _2073)));
                                                            if (!(_4112 < 0.100000001490116119384765625))
                                                            {
                                                                float _4117 = 0.5 * _2126;
                                                                vec2 _4120 = vec2(_4117) / _4002;
                                                                if (abs(_4001.x) > (1.0 + _4120.x))
                                                                {
                                                                    _4009 = true;
                                                                }
                                                                else
                                                                {
                                                                    _4009 = abs(_4001.y) > (1.0 + _4120.y);
                                                                }
                                                                if (!(_4009))
                                                                {
                                                                    if (_4003 < (_2126 * (-0.5)))
                                                                    {
                                                                        _4010 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _4010 = _4003 > (_4004 + _4117);
                                                                    }
                                                                    if (!(_4010))
                                                                    {
                                                                        vec2 _4164 = (_4008.xy + (((clamp(_4001, vec2(-1.0), vec2(1.0)) * 0.5) + vec2(0.5)) * _4008.zw)) - vec2(0.5);
                                                                        vec2 _4165 = floor(_4164);
                                                                        vec2 _4166 = _4164 - _4165;
                                                                        vec2 _4172 = _4007.zw / max(_4008.zw, vec2(9.9999999747524270787835121154785e-07));
                                                                        float _4176 = _4003 / max(_4004, 9.9999999747524270787835121154785e-07);
                                                                        float _4180 = _2126 / max(0.5 * _4004, 9.9999999747524270787835121154785e-07);
                                                                        vec4 _4011 = vec4(0.0);
                                                                        int _4014 = 0;
                                                                        for (;;)
                                                                        {
                                                                            if (!(_4014 < 4))
                                                                            {
                                                                                break;
                                                                            }
                                                                            vec2 _4195 = vec2(float(_4014 & 1), float(_4014 >> 1));
                                                                            vec2 _4198 = (_4165 + _4195) + vec2(0.5);
                                                                            vec2 _4199 = _4198 / _4047;
                                                                            if (textureLod(_4825, _4199, 0.0).w >= 0.5)
                                                                            {
                                                                                _4015 = 1.0;
                                                                            }
                                                                            else
                                                                            {
                                                                                _4015 = 0.0;
                                                                            }
                                                                            vec2 _4228 = mix(vec2(1.0) - _4166, _4166, _4195);
                                                                            _4011[_4014] = ((_4228.x * _4228.y) * _4015) * (1.0 - clamp((abs(_4176 - textureLod(_4827, _4199, 0.0).x) - _4180) / (0.25 * _4180), 0.0, 1.0));
                                                                            _4012[_4014] = textureLod(_4829, (floor(_4007.xy + ((_4198 - _4008.xy) * _4172)) + vec2(0.5)) / vec3(p.cacheAtlas.xy, 0.0).xy, 0.0).xyz;
                                                                            _4014++;
                                                                            continue;
                                                                        }
                                                                        float _4277 = ((_4011.x + _4011.y) + _4011.z) + _4011.w;
                                                                        float _4278 = (_4112 * _4112) * _4277;
                                                                        if (!(_4278 <= 0.0))
                                                                        {
                                                                            _2074 += (max(((((_4012[0] * _4011.x) + (_4012[1] * _4011.y)) + (_4012[2] * _4011.z)) + (_4012[3] * _4011.w)) / vec3(_4277), vec3(0.0)) * _4278);
                                                                            _4000 += _4278;
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            if (_4074_ladder_break)
                                            {
                                                break;
                                            }
                                            _3999++;
                                            continue;
                                        }
                                        _3996++;
                                        _3997 = _4000;
                                        continue;
                                    }
                                    if (_3997 <= 0.0)
                                    {
                                        _2074 = vec3(0.0);
                                        _3993 = false;
                                    }
                                    else
                                    {
                                        _2074 /= vec3(_3997);
                                        _3993 = true;
                                    }
                                }
                            }
                            vec3 _2075;
                            if (_3993)
                            {
                                gRoute = 2;
                                _2075 = _2074 * p.cacheAtlas.z;
                            }
                            else
                            {
                                _2075 = vec3(0.0);
                            }
                            if (!_3993)
                            {
                                gRoute = 3;
                                if (_2307)
                                {
                                    uint _4615 = uvec2(textureSize(_4813, 0)).y;
                                    float _4637 = acos(clamp(sqrt(max(0.0, (p.trace.w * p.trace.w) - 40449600.0)) / p.trace.w, -1.0, 1.0));
                                    float _4638 = 3.1415927410125732421875 - _4637;
                                    float _4641 = acos(clamp(_154.z, -1.0, 1.0));
                                    float _4627;
                                    if (_4641 < _4638)
                                    {
                                        _4627 = (1.0 - sqrt(max(0.0, 1.0 - (_4641 / _4638)))) * 0.5;
                                    }
                                    else
                                    {
                                        _4627 = (sqrt(max(0.0, (_4641 - _4638) / max(_4637, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                    }
                                    float _4661 = atan(_154.y, _154.x);
                                    float _4628;
                                    if (_4661 < 0.0)
                                    {
                                        _4628 = _4661 + 6.283185482025146484375;
                                    }
                                    else
                                    {
                                        _4628 = _4661;
                                    }
                                    vec2 _4670 = vec2(_4628 * 0.15915493667125701904296875, _4627);
                                    float _4624 = float(_4615);
                                    _4670.y = min(_4670.y, (floor(_4624 * 0.5) - 1.0) / max(_4624 - 1.0, 1.0));
                                    uvec2 _4689 = uvec2(textureSize(_4813, 0));
                                    vec2 _4696 = vec2(float(_4689.x), float(_4689.y));
                                    _2073 = vec3(dot(textureLod(_4815, (_4670 * ((_4696 - vec2(1.0)) / _4696)) + (vec2(0.5) / _4696), 0.0).xyz * smoothstep(-0.100000001490116119384765625, 0.25, _154.z), vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * 0.300000011920928955078125) + p.ambient.xyz;
                                }
                                else
                                {
                                    uint _4497 = uvec2(textureSize(_4813, 0)).y;
                                    float _4519 = acos(clamp(sqrt(max(0.0, (p.trace.w * p.trace.w) - 40449600.0)) / p.trace.w, -1.0, 1.0));
                                    float _4520 = 3.1415927410125732421875 - _4519;
                                    float _4523 = acos(clamp(_154.z, -1.0, 1.0));
                                    float _4509;
                                    if (_4523 < _4520)
                                    {
                                        _4509 = (1.0 - sqrt(max(0.0, 1.0 - (_4523 / _4520)))) * 0.5;
                                    }
                                    else
                                    {
                                        _4509 = (sqrt(max(0.0, (_4523 - _4520) / max(_4519, 9.9999999747524270787835121154785e-07))) * 0.5) + 0.5;
                                    }
                                    float _4543 = atan(_154.y, _154.x);
                                    float _4510;
                                    if (_4543 < 0.0)
                                    {
                                        _4510 = _4543 + 6.283185482025146484375;
                                    }
                                    else
                                    {
                                        _4510 = _4543;
                                    }
                                    vec2 _4552 = vec2(_4510 * 0.15915493667125701904296875, _4509);
                                    float _4506 = float(_4497);
                                    _4552.y = min(_4552.y, (floor(_4506 * 0.5) - 1.0) / max(_4506 - 1.0, 1.0));
                                    uvec2 _4571 = uvec2(textureSize(_4813, 0));
                                    vec2 _4578 = vec2(float(_4571.x), float(_4571.y));
                                    _2073 = textureLod(_4815, (_4552 * ((_4578 - vec2(1.0)) / _4578)) + (vec2(0.5) / _4578), 0.0).xyz * smoothstep(-0.100000001490116119384765625, 0.25, _154.z);
                                }
                                _2069 = _2073;
                            }
                            else
                            {
                                float _2153 = max(_2075.x, max(_2075.y, _2075.z));
                                if (_2153 > p.trace.z)
                                {
                                    _2075 *= (p.trace.z / _2153);
                                }
                                _2069 = _2075;
                            }
                        }
                    }
                }
                if (p.debug.w > 3.5)
                {
                    imageStore(uOut, ivec2(gl_GlobalInvocationID.xy), vec4((normalW * 0.5) + vec3(0.5), 1.0));
                }
                else
                {
                    if (p.debug.w > 2.5)
                    {
                        imageStore(uOut, ivec2(gl_GlobalInvocationID.xy), vec4((_154 * 0.5) + vec3(0.5), 1.0));
                    }
                    else
                    {
                        if (p.debug.w > 0.5)
                        {
                            _7 = p.debug.w < 1.5;
                        }
                        else
                        {
                            _7 = false;
                        }
                        if (_7)
                        {
                            vec3 _4713;
                            {
                                if (gRoute == 1)
                                {
                                    _4713 = vec3(0.0, 1.0, 0.0);
                                }
                                else
                                {
                                    if (gRoute == 2)
                                    {
                                        _4713 = vec3(0.0, 0.20000000298023223876953125, 1.0);
                                    }
                                    else
                                    {
                                        if (gRoute == 3)
                                        {
                                            _4713 = vec3(1.0, 0.0, 0.0);
                                        }
                                        else
                                        {
                                            if (gRoute == 4)
                                            {
                                                _4713 = vec3(1.0, 0.800000011920928955078125, 0.0);
                                            }
                                            else
                                            {
                                                if (gRoute == 5)
                                                {
                                                    _4713 = vec3(1.0);
                                                }
                                                else
                                                {
                                                    _4713 = vec3(0.25);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            imageStore(uOut, ivec2(gl_GlobalInvocationID.xy), vec4(_4713, 1.0));
                        }
                        else
                        {
                            if (p.debug.x > 0.5)
                            {
                                imageStore(uOut, ivec2(gl_GlobalInvocationID.xy), vec4(_2069, 1.0));
                            }
                            else
                            {
                                imageStore(uOut, ivec2(gl_GlobalInvocationID.xy), vec4((_2069 * ((2.0 * (0.039999999105930328369140625 + _162)) / (_162 + 1.03999996185302734375))) * p.cacheAtlas.w, 1.0));
                            }
                        }
                    }
                }
            }
        }
    }
}

