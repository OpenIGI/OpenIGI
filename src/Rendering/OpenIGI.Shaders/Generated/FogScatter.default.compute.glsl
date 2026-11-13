#version 430
layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

struct _Array_std140_matrix_float_4_4_4
{
    mat4 data[4];
};

struct _Array_std140_vector_float_4_8
{
    vec4 data[8];
};

struct _Array_std140_vector_float_4_160
{
    vec4 data[160];
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

struct _Array_std140_vector_float_4_4
{
    vec4 data[4];
};

struct _Array_std140_matrix_float_4_4_240
{
    mat4 data[240];
};

struct _Array_std140_vector_float_4_120
{
    vec4 data[120];
};

struct _Array_std140_vector_int_4_40
{
    ivec4 data[40];
};

layout(binding = 0, std140) uniform FogParams_std140
{
    layout(row_major) mat4 viewToWorld;
    layout(row_major) mat4 prevWorldToView;
    layout(row_major) _Array_std140_matrix_float_4_4_4 cascade;
    vec4 cascadeSplits;
    vec4 grid;
    vec4 unproject;
    vec4 prevUnproject;
    vec4 zParams;
    vec4 medium;
    vec4 march;
    vec4 albedo;
    vec4 sun;
    vec4 sunColour;
    vec4 ambient;
    vec4 camera;
    vec4 jitter;
    vec4 cameraWorld;
    vec4 shelter0;
    vec4 shelter1;
    vec4 shelter2;
    _Array_std140_vector_float_4_8 volumeCentre;
    _Array_std140_vector_float_4_8 volumeExtent;
    _Array_std140_vector_float_4_160 lampPosition;
    _Array_std140_vector_float_4_160 lampColour;
    FlashlightParams_std140 flashlight;
    vec4 phase;
    vec4 dust;
    vec4 records;
    vec4 fieldMisc;
    _Array_std140_vector_float_4_4 fieldOrigin;
    vec4 fieldRange;
    _Array_std140_vector_float_4_160 lampDirection;
} p;

layout(binding = 1, std140) uniform FogLampMapParams_std140
{
    _Array_std140_vector_float_4_4 origin;
    vec4 range;
    ivec4 levels;
    layout(row_major) _Array_std140_matrix_float_4_4_240 mapMatrix;
    _Array_std140_vector_float_4_120 mapDepth;
    _Array_std140_vector_int_4_40 mapSlot;
} uFogLampMapParams;

layout(binding = 0, rgba16f) uniform restrict writeonly image3D uScattering;
layout(binding = 3) uniform sampler2D _4712;
layout(binding = 4) uniform sampler3D _4714;
layout(binding = 0) uniform sampler2DArray _4717;
layout(binding = 5) uniform sampler2DArray _4720;
layout(binding = 1) uniform sampler2D _4723;
layout(binding = 2) uniform sampler3D _4725;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    bool _4413 = false;
    bool _4334 = false;
    bool _3789 = false;
    bool _3700 = false;
    bool _3473 = false;
    {
        ivec3 _118 = ivec3(gl_GlobalInvocationID);
        if (!(any(greaterThanEqual(_118, ivec3(p.grid.xyz + vec3(0.5))))))
        {
            vec3 _2443 = fract((vec3(_118) + vec3(0.37000000476837158203125)) * 0.103100001811981201171875);
            vec3 _2449 = _2443 + vec3(dot(_2443, _2443.zyx + vec3(31.31999969482421875)));
            vec3 _2459 = fract((vec3(_118.yzx) + vec3(11.1000003814697265625)) * 0.103100001811981201171875);
            vec3 _2465 = _2459 + vec3(dot(_2459, _2459.zyx + vec3(31.31999969482421875)));
            vec3 _2475 = fract((vec3(_118.zxy) + vec3(23.700000762939453125)) * 0.103100001811981201171875);
            vec3 _2481 = _2475 + vec3(dot(_2475, _2475.zyx + vec3(31.31999969482421875)));
            vec3 _192 = fract(p.jitter.xyz + vec3(fract((_2449.x + _2449.y) * _2449.z), fract((_2465.x + _2465.y) * _2465.z), fract((_2481.x + _2481.y) * _2481.z)));
            vec2 _196 = vec2(_118.xy);
            float _203 = float(_118.z);
            float _2500 = (exp2((_203 + _192.z) / p.zParams.z) - p.zParams.y) / p.zParams.x;
            vec2 _2505 = (((_196 + _192.xy) / p.grid.xy) * 2.0) - vec2(1.0);
            vec3 _2518 = vec3((_2505.x * _2500) / p.unproject.x, ((-_2505.y) * _2500) / p.unproject.y, _2500);
            vec4 _256 = vec4(_2518, 0.0) * spvWorkaroundRowMajor(p.viewToWorld);
            vec3 world = _256.xyz;
            bool _2520;
            {
                if (p.shelter1.w < 0.5)
                {
                    _2520 = false;
                }
                else
                {
                    vec3 _2534 = world + p.cameraWorld.xyz;
                    ivec2 _2544 = ivec2(floor((_2534.xy - p.shelter0.xy) * p.shelter0.z));
                    int _2545 = _2544.x;
                    bool _2521;
                    if (_2545 < 0)
                    {
                        _2521 = true;
                    }
                    else
                    {
                        _2521 = _2544.y < 0;
                    }
                    if (_2521)
                    {
                        _2521 = true;
                    }
                    else
                    {
                        _2521 = _2545 >= int(p.shelter2.x);
                    }
                    if (_2521)
                    {
                        _2521 = true;
                    }
                    else
                    {
                        _2521 = _2544.y >= int(p.shelter2.y);
                    }
                    if (_2521)
                    {
                        _2520 = false;
                    }
                    else
                    {
                        vec4 _2577 = texelFetch(_4712, ivec3(_2544, 0).xy, 0);
                        float _2588 = floor((_2577.z * 255.0) + 0.5) + (floor((_2577.y * 255.0) + 0.5) * 256.0);
                        if (_2588 < 0.5)
                        {
                            _2520 = false;
                        }
                        else
                        {
                            float _2598 = p.shelter0.w + ((_2588 - 1.0) * p.shelter1.x);
                            float _2599 = _2534.z;
                            if (_2598 > (_2599 + p.shelter1.y))
                            {
                                _2521 = _2598 < (_2599 + p.shelter1.z);
                            }
                            else
                            {
                                _2521 = false;
                            }
                            _2520 = _2521;
                        }
                    }
                }
            }
            float _2615;
            {
                if (_2520)
                {
                    _2615 = 0.0;
                }
                else
                {
                    float _2646 = 1.0;
                    int _2647 = 0;
                    float _2648;
                    for (;;)
                    {
                        bool _2653_ladder_break = false;
                        {
                            if (!(_2647 < 8))
                            {
                                _2653_ladder_break = true;
                            }
                            else
                            {
                                if (p.volumeExtent.data[_2647].x <= 0.0)
                                {
                                    _2648 = _2646;
                                }
                                else
                                {
                                    vec3 _2676 = abs(world - p.volumeCentre.data[_2647].xyz);
                                    if (p.volumeCentre.data[_2647].w > 0.5)
                                    {
                                        _2648 = length(_2676) / max(p.volumeExtent.data[_2647].x, 0.001000000047497451305389404296875);
                                    }
                                    else
                                    {
                                        _2648 = max(_2676.x / max(p.volumeExtent.data[_2647].x, 0.001000000047497451305389404296875), max(_2676.y / max(p.volumeExtent.data[_2647].y, 0.001000000047497451305389404296875), _2676.z / max(p.volumeExtent.data[_2647].z, 0.001000000047497451305389404296875)));
                                    }
                                    _2648 = mix(_2646, p.volumeExtent.data[_2647].w, 1.0 - smoothstep(0.699999988079071044921875, 1.0, _2648));
                                }
                            }
                        }
                        if (_2653_ladder_break)
                        {
                            break;
                        }
                        _2646 = _2648;
                        _2647++;
                        continue;
                    }
                    float _2721;
                    {
                        if (p.dust.w <= 0.0)
                        {
                            _2721 = 1.0;
                        }
                        else
                        {
                            vec3 _2738 = world + (fract(p.cameraWorld.xyz * vec3(0.000244140625)) * 4096.0);
                            vec3 _2742 = vec3(0.60000002384185791015625, 0.25, 0.039999999105930328369140625) * p.phase.z;
                            vec3 _2745 = (_2738 + _2742) * vec3(0.0416666679084300994873046875);
                            vec3 _2762 = floor(_2745);
                            vec3 _2763 = fract(_2745);
                            vec3 _2768 = (_2763 * _2763) * (vec3(3.0) - (_2763 * 2.0));
                            vec3 _2797 = fract(_2762 * 0.103100001811981201171875);
                            vec3 _2803 = _2797 + vec3(dot(_2797, _2797.zyx + vec3(31.31999969482421875)));
                            vec3 _2813 = fract((_2762 + vec3(1.0, 0.0, 0.0)) * 0.103100001811981201171875);
                            vec3 _2819 = _2813 + vec3(dot(_2813, _2813.zyx + vec3(31.31999969482421875)));
                            vec3 _2829 = fract((_2762 + vec3(0.0, 1.0, 0.0)) * 0.103100001811981201171875);
                            vec3 _2835 = _2829 + vec3(dot(_2829, _2829.zyx + vec3(31.31999969482421875)));
                            vec3 _2845 = fract((_2762 + vec3(1.0, 1.0, 0.0)) * 0.103100001811981201171875);
                            vec3 _2851 = _2845 + vec3(dot(_2845, _2845.zyx + vec3(31.31999969482421875)));
                            vec3 _2861 = fract((_2762 + vec3(0.0, 0.0, 1.0)) * 0.103100001811981201171875);
                            vec3 _2867 = _2861 + vec3(dot(_2861, _2861.zyx + vec3(31.31999969482421875)));
                            vec3 _2877 = fract((_2762 + vec3(1.0, 0.0, 1.0)) * 0.103100001811981201171875);
                            vec3 _2883 = _2877 + vec3(dot(_2877, _2877.zyx + vec3(31.31999969482421875)));
                            vec3 _2893 = fract((_2762 + vec3(0.0, 1.0, 1.0)) * 0.103100001811981201171875);
                            vec3 _2899 = _2893 + vec3(dot(_2893, _2893.zyx + vec3(31.31999969482421875)));
                            vec3 _2909 = fract((_2762 + vec3(1.0)) * 0.103100001811981201171875);
                            vec3 _2915 = _2909 + vec3(dot(_2909, _2909.zyx + vec3(31.31999969482421875)));
                            float _2784 = _2768.x;
                            float _2787 = _2768.y;
                            float _2747 = mix(mix(mix(fract((_2803.x + _2803.y) * _2803.z), fract((_2819.x + _2819.y) * _2819.z), _2784), mix(fract((_2835.x + _2835.y) * _2835.z), fract((_2851.x + _2851.y) * _2851.z), _2784), _2787), mix(mix(fract((_2867.x + _2867.y) * _2867.z), fract((_2883.x + _2883.y) * _2883.z), _2784), mix(fract((_2899.x + _2899.y) * _2899.z), fract((_2915.x + _2915.y) * _2915.z), _2784), _2787), _2768.z) * 0.699999988079071044921875;
                            vec3 _2751 = (_2738 + (_2742 * 1.7000000476837158203125)) * vec3(0.111111111938953399658203125);
                            vec3 _2924 = floor(_2751);
                            vec3 _2925 = fract(_2751);
                            vec3 _2930 = (_2925 * _2925) * (vec3(3.0) - (_2925 * 2.0));
                            vec3 _2959 = fract(_2924 * 0.103100001811981201171875);
                            vec3 _2965 = _2959 + vec3(dot(_2959, _2959.zyx + vec3(31.31999969482421875)));
                            vec3 _2975 = fract((_2924 + vec3(1.0, 0.0, 0.0)) * 0.103100001811981201171875);
                            vec3 _2981 = _2975 + vec3(dot(_2975, _2975.zyx + vec3(31.31999969482421875)));
                            vec3 _2991 = fract((_2924 + vec3(0.0, 1.0, 0.0)) * 0.103100001811981201171875);
                            vec3 _2997 = _2991 + vec3(dot(_2991, _2991.zyx + vec3(31.31999969482421875)));
                            vec3 _3007 = fract((_2924 + vec3(1.0, 1.0, 0.0)) * 0.103100001811981201171875);
                            vec3 _3013 = _3007 + vec3(dot(_3007, _3007.zyx + vec3(31.31999969482421875)));
                            vec3 _3023 = fract((_2924 + vec3(0.0, 0.0, 1.0)) * 0.103100001811981201171875);
                            vec3 _3029 = _3023 + vec3(dot(_3023, _3023.zyx + vec3(31.31999969482421875)));
                            vec3 _3039 = fract((_2924 + vec3(1.0, 0.0, 1.0)) * 0.103100001811981201171875);
                            vec3 _3045 = _3039 + vec3(dot(_3039, _3039.zyx + vec3(31.31999969482421875)));
                            vec3 _3055 = fract((_2924 + vec3(0.0, 1.0, 1.0)) * 0.103100001811981201171875);
                            vec3 _3061 = _3055 + vec3(dot(_3055, _3055.zyx + vec3(31.31999969482421875)));
                            vec3 _3071 = fract((_2924 + vec3(1.0)) * 0.103100001811981201171875);
                            vec3 _3077 = _3071 + vec3(dot(_3071, _3071.zyx + vec3(31.31999969482421875)));
                            float _2946 = _2930.x;
                            float _2949 = _2930.y;
                            float _2753 = mix(mix(mix(fract((_2965.x + _2965.y) * _2965.z), fract((_2981.x + _2981.y) * _2981.z), _2946), mix(fract((_2997.x + _2997.y) * _2997.z), fract((_3013.x + _3013.y) * _3013.z), _2946), _2949), mix(mix(fract((_3029.x + _3029.y) * _3029.z), fract((_3045.x + _3045.y) * _3045.z), _2946), mix(fract((_3061.x + _3061.y) * _3061.z), fract((_3077.x + _3077.y) * _3077.z), _2946), _2949), _2930.z) * 0.300000011920928955078125;
                            _2721 = mix(1.0, 0.300000011920928955078125 + (1.39999997615814208984375 * (_2747 + _2753)), p.dust.w);
                        }
                    }
                    _2615 = max(((p.medium.x * mix(exp((-max(_256.z - p.medium.z, 0.0)) * p.medium.y), 1.0, p.march.x)) * _2646) * _2721, 0.0);
                }
            }
            float _3085;
            {
                float _3092 = max(p.dust.y, 1.0);
                if (_2520)
                {
                    _3085 = max(p.dust.z, 0.0);
                }
                else
                {
                    float _3110;
                    {
                        if (p.dust.w <= 0.0)
                        {
                            _3110 = 1.0;
                        }
                        else
                        {
                            vec3 _3127 = world + (fract(p.cameraWorld.xyz * vec3(0.000244140625)) * 4096.0);
                            vec3 _3131 = vec3(0.60000002384185791015625, 0.25, 0.039999999105930328369140625) * p.phase.z;
                            vec3 _3134 = (_3127 + _3131) * vec3(0.0416666679084300994873046875);
                            vec3 _3151 = floor(_3134);
                            vec3 _3152 = fract(_3134);
                            vec3 _3157 = (_3152 * _3152) * (vec3(3.0) - (_3152 * 2.0));
                            vec3 _3186 = fract(_3151 * 0.103100001811981201171875);
                            vec3 _3192 = _3186 + vec3(dot(_3186, _3186.zyx + vec3(31.31999969482421875)));
                            vec3 _3202 = fract((_3151 + vec3(1.0, 0.0, 0.0)) * 0.103100001811981201171875);
                            vec3 _3208 = _3202 + vec3(dot(_3202, _3202.zyx + vec3(31.31999969482421875)));
                            vec3 _3218 = fract((_3151 + vec3(0.0, 1.0, 0.0)) * 0.103100001811981201171875);
                            vec3 _3224 = _3218 + vec3(dot(_3218, _3218.zyx + vec3(31.31999969482421875)));
                            vec3 _3234 = fract((_3151 + vec3(1.0, 1.0, 0.0)) * 0.103100001811981201171875);
                            vec3 _3240 = _3234 + vec3(dot(_3234, _3234.zyx + vec3(31.31999969482421875)));
                            vec3 _3250 = fract((_3151 + vec3(0.0, 0.0, 1.0)) * 0.103100001811981201171875);
                            vec3 _3256 = _3250 + vec3(dot(_3250, _3250.zyx + vec3(31.31999969482421875)));
                            vec3 _3266 = fract((_3151 + vec3(1.0, 0.0, 1.0)) * 0.103100001811981201171875);
                            vec3 _3272 = _3266 + vec3(dot(_3266, _3266.zyx + vec3(31.31999969482421875)));
                            vec3 _3282 = fract((_3151 + vec3(0.0, 1.0, 1.0)) * 0.103100001811981201171875);
                            vec3 _3288 = _3282 + vec3(dot(_3282, _3282.zyx + vec3(31.31999969482421875)));
                            vec3 _3298 = fract((_3151 + vec3(1.0)) * 0.103100001811981201171875);
                            vec3 _3304 = _3298 + vec3(dot(_3298, _3298.zyx + vec3(31.31999969482421875)));
                            float _3173 = _3157.x;
                            float _3176 = _3157.y;
                            float _3136 = mix(mix(mix(fract((_3192.x + _3192.y) * _3192.z), fract((_3208.x + _3208.y) * _3208.z), _3173), mix(fract((_3224.x + _3224.y) * _3224.z), fract((_3240.x + _3240.y) * _3240.z), _3173), _3176), mix(mix(fract((_3256.x + _3256.y) * _3256.z), fract((_3272.x + _3272.y) * _3272.z), _3173), mix(fract((_3288.x + _3288.y) * _3288.z), fract((_3304.x + _3304.y) * _3304.z), _3173), _3176), _3157.z) * 0.699999988079071044921875;
                            vec3 _3140 = (_3127 + (_3131 * 1.7000000476837158203125)) * vec3(0.111111111938953399658203125);
                            vec3 _3313 = floor(_3140);
                            vec3 _3314 = fract(_3140);
                            vec3 _3319 = (_3314 * _3314) * (vec3(3.0) - (_3314 * 2.0));
                            vec3 _3348 = fract(_3313 * 0.103100001811981201171875);
                            vec3 _3354 = _3348 + vec3(dot(_3348, _3348.zyx + vec3(31.31999969482421875)));
                            vec3 _3364 = fract((_3313 + vec3(1.0, 0.0, 0.0)) * 0.103100001811981201171875);
                            vec3 _3370 = _3364 + vec3(dot(_3364, _3364.zyx + vec3(31.31999969482421875)));
                            vec3 _3380 = fract((_3313 + vec3(0.0, 1.0, 0.0)) * 0.103100001811981201171875);
                            vec3 _3386 = _3380 + vec3(dot(_3380, _3380.zyx + vec3(31.31999969482421875)));
                            vec3 _3396 = fract((_3313 + vec3(1.0, 1.0, 0.0)) * 0.103100001811981201171875);
                            vec3 _3402 = _3396 + vec3(dot(_3396, _3396.zyx + vec3(31.31999969482421875)));
                            vec3 _3412 = fract((_3313 + vec3(0.0, 0.0, 1.0)) * 0.103100001811981201171875);
                            vec3 _3418 = _3412 + vec3(dot(_3412, _3412.zyx + vec3(31.31999969482421875)));
                            vec3 _3428 = fract((_3313 + vec3(1.0, 0.0, 1.0)) * 0.103100001811981201171875);
                            vec3 _3434 = _3428 + vec3(dot(_3428, _3428.zyx + vec3(31.31999969482421875)));
                            vec3 _3444 = fract((_3313 + vec3(0.0, 1.0, 1.0)) * 0.103100001811981201171875);
                            vec3 _3450 = _3444 + vec3(dot(_3444, _3444.zyx + vec3(31.31999969482421875)));
                            vec3 _3460 = fract((_3313 + vec3(1.0)) * 0.103100001811981201171875);
                            vec3 _3466 = _3460 + vec3(dot(_3460, _3460.zyx + vec3(31.31999969482421875)));
                            float _3335 = _3319.x;
                            float _3338 = _3319.y;
                            float _3142 = mix(mix(mix(fract((_3354.x + _3354.y) * _3354.z), fract((_3370.x + _3370.y) * _3370.z), _3335), mix(fract((_3386.x + _3386.y) * _3386.z), fract((_3402.x + _3402.y) * _3402.z), _3335), _3338), mix(mix(fract((_3418.x + _3418.y) * _3418.z), fract((_3434.x + _3434.y) * _3434.z), _3335), mix(fract((_3450.x + _3450.y) * _3450.z), fract((_3466.x + _3466.y) * _3466.z), _3335), _3338), _3319.z) * 0.300000011920928955078125;
                            _3110 = mix(1.0, 0.300000011920928955078125 + (1.39999997615814208984375 * (_3136 + _3142)), p.dust.w);
                        }
                    }
                    _3085 = max((p.dust.x * (1.0 - smoothstep(0.5 * _3092, _3092, _2500))) * _3110, 0.0);
                }
            }
            vec3 _648 = normalize(_2518);
            float _649 = _2615 + _3085;
            vec3 direct;
            if (_649 > 0.0)
            {
                _3473 = false;
                float _3474;
                {
                    int _3494 = int(p.albedo.w + 0.5);
                    if (_3494 <= 0)
                    {
                        _3473 = true;
                        _3474 = 1.0;
                    }
                    else
                    {
                        int _3500 = clamp(_3494 - 1, 0, 3);
                        float _3475;
                        if (p.cascadeSplits[_3500] > 0.0)
                        {
                            _3475 = clamp((_2518.z - (p.cascadeSplits[_3500] * 0.550000011920928955078125)) / max(p.cascadeSplits[_3500] * 0.449999988079071044921875, 0.001000000047497451305389404296875), 0.0, 1.0);
                        }
                        else
                        {
                            _3475 = 1.0;
                        }
                        float _3476;
                        if (_3475 > 0.0)
                        {
                            float _3672;
                            {
                                if (int(p.fieldMisc.x + 0.5) <= 0)
                                {
                                    _3672 = 1.0;
                                }
                                else
                                {
                                    vec3 _3692 = normalize((vec4(p.sun.xyz, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz);
                                    _3700 = false;
                                    float _3701;
                                    {
                                        if (int(p.fieldMisc.x + 0.5) <= 0)
                                        {
                                            _3700 = true;
                                            _3701 = 1.0;
                                        }
                                        else
                                        {
                                            float _3743 = (p.fieldOrigin.data[0].w * float(int(p.fieldMisc.y + 0.5))) * float(1 << (int(p.fieldMisc.x + 0.5) - 1));
                                            int _3702 = 0;
                                            float _3703 = 0.0;
                                            bool _3704;
                                            bool _3705;
                                            float _3707;
                                            float _3790;
                                            bool _3792;
                                            for (;;)
                                            {
                                                if (_3702 < 64)
                                                {
                                                    _3704 = _3703 < _3743;
                                                }
                                                else
                                                {
                                                    _3704 = false;
                                                }
                                                if (!_3704)
                                                {
                                                    break;
                                                }
                                                vec3 _3760 = world + (_3692 * _3703);
                                                _3789 = false;
                                                {
                                                    _3705 = false;
                                                    _3707 = p.fieldOrigin.data[0].w;
                                                    int _3791 = 0;
                                                    for (;;)
                                                    {
                                                        bool _3803_ladder_break = false;
                                                        {
                                                            if (!(_3791 < int(p.fieldMisc.x + 0.5)))
                                                            {
                                                                _3803_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                vec3 _3821 = (_3760 - p.fieldOrigin.data[_3791].xyz) / vec3(p.fieldOrigin.data[_3791].w);
                                                                if (any(lessThan(_3821, vec3(0.5))))
                                                                {
                                                                    _3792 = true;
                                                                }
                                                                else
                                                                {
                                                                    _3792 = any(greaterThan(_3821, vec3(float(int(p.fieldMisc.y + 0.5)) - 0.5, float(int(p.fieldMisc.y + 0.5)) - 0.5, float(int(p.fieldMisc.y + 0.5)) - 0.5)));
                                                                }
                                                                if (!(_3792))
                                                                {
                                                                    _3705 = true;
                                                                    _3707 = p.fieldOrigin.data[_3791].w;
                                                                    _3789 = true;
                                                                    _3790 = ((textureLod(_4714, vec3(_3821.x / float(int(p.fieldMisc.y + 0.5)), _3821.y / float(int(p.fieldMisc.y + 0.5)), ((float(_3791) * float(int(p.fieldMisc.y + 0.5))) + _3821.z) / float(int(p.fieldMisc.y + 0.5) * int(p.fieldMisc.x + 0.5))), 0.0).x * 2.0) - 1.0) * p.fieldRange[_3791];
                                                                    _3803_ladder_break = true;
                                                                }
                                                            }
                                                        }
                                                        if (_3803_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _3791++;
                                                        continue;
                                                    }
                                                    if (!(_3789))
                                                    {
                                                        _3789 = true;
                                                        _3790 = 1000000.0;
                                                    }
                                                }
                                                if (!_3705)
                                                {
                                                    _3700 = true;
                                                    _3701 = 1.0;
                                                    break;
                                                }
                                                if (_3790 < ((0.5 * _3707) * clamp(_3703 / max(2.0 * _3707, 9.9999999747524270787835121154785e-07), 0.0, 1.0)))
                                                {
                                                    _3700 = true;
                                                    _3701 = 0.0;
                                                    break;
                                                }
                                                _3702++;
                                                _3703 += max(_3790, _3707 * 0.25);
                                                continue;
                                            }
                                            if (!(_3700))
                                            {
                                                _3700 = true;
                                                _3701 = 1.0;
                                            }
                                        }
                                    }
                                    _3672 = _3701;
                                }
                            }
                            _3476 = _3672;
                        }
                        else
                        {
                            _3476 = 1.0;
                        }
                        if (_3475 >= 1.0)
                        {
                            _3473 = true;
                            _3474 = _3476;
                        }
                        else
                        {
                            int _3477 = 0;
                            bool _3478;
                            float _3486;
                            for (;;)
                            {
                                bool _3527_ladder_break = false;
                                {
                                    if (!(_3477 < 4))
                                    {
                                        _3527_ladder_break = true;
                                    }
                                    else
                                    {
                                        if (_3477 >= _3494)
                                        {
                                            _3478 = true;
                                        }
                                        else
                                        {
                                            _3478 = _2518.z > p.cascadeSplits[_3477];
                                        }
                                        if (!(_3478))
                                        {
                                            vec4 _3552 = vec4(world, 1.0) * spvWorkaroundRowMajor(p.cascade.data[_3477]);
                                            vec3 _3556 = _3552.xyz / vec3(_3552.w);
                                            if (!(any(greaterThan(abs(_3556.xy), vec2(1.0)))))
                                            {
                                                float _3576 = ((_3556.z * 0.5) + 0.5) - p.sunColour.w;
                                                uvec3 _3578 = uvec3(textureSize(_4717, 0));
                                                vec2 _3586 = vec2(float(_3578.x), float(_3578.y));
                                                vec2 _3589 = (vec2((_3556.x * 0.5) + 0.5, 0.5 - (_3556.y * 0.5)) * _3586) - vec2(0.5);
                                                ivec2 _3591 = ivec2(floor(_3589));
                                                vec2 _3592 = fract(_3589);
                                                int _3483 = 0;
                                                float _3482 = 0.0;
                                                for (;;)
                                                {
                                                    if (!(_3483 < 2))
                                                    {
                                                        break;
                                                    }
                                                    int _3485 = 0;
                                                    float _3484 = _3482;
                                                    for (;;)
                                                    {
                                                        if (!(_3485 < 2))
                                                        {
                                                            break;
                                                        }
                                                        if (_3576 <= texelFetch(_4717, ivec4(clamp(_3591 + ivec2(_3485, _3483), ivec2(0), ivec2(_3586) - ivec2(1)), _3477, 0).xyz, 0).x)
                                                        {
                                                            _3486 = 1.0;
                                                        }
                                                        else
                                                        {
                                                            _3486 = 0.0;
                                                        }
                                                        float _3628 = _3592.x;
                                                        int _3630 = _3485;
                                                        float _3635 = _3592.y;
                                                        _3485++;
                                                        _3484 += ((_3486 * mix(1.0 - _3628, _3628, float(_3630))) * mix(1.0 - _3635, _3635, float(_3483)));
                                                        continue;
                                                    }
                                                    _3483++;
                                                    _3482 = _3484;
                                                    continue;
                                                }
                                                _3473 = true;
                                                _3474 = mix(_3482, _3476, _3475);
                                                _3527_ladder_break = true;
                                            }
                                        }
                                    }
                                }
                                if (_3527_ladder_break)
                                {
                                    break;
                                }
                                _3477++;
                                continue;
                            }
                            if (!(_3473))
                            {
                                _3473 = true;
                                _3474 = _3476;
                            }
                        }
                    }
                }
                float _1251 = dot(_648, p.sun.xyz);
                float _3942 = p.medium.w * p.medium.w;
                float _3954 = p.phase.x * p.phase.x;
                int rankedLamp[4] = int[](-1, -1, -1, -1);
                vec3 rankedLight[4] = vec3[](vec3(0.0), vec3(0.0), vec3(0.0), vec3(0.0));
                float rankedLuminance[4] = float[](0.0, 0.0, 0.0, 0.0);
                int _1307 = min(int(round(p.camera.w)), 160);
                int i = 0;
                direct = ((p.sunColour.xyz * p.march.z) * mix(mix((1.0 - _3942) / (12.56637096405029296875 * pow(max((1.0 + _3942) - ((2.0 * p.medium.w) * _1251), 9.9999997473787516355514526367188e-05), 1.5)), (1.0 - _3954) / (12.56637096405029296875 * pow(max((1.0 + _3954) - ((2.0 * p.phase.x) * _1251), 9.9999997473787516355514526367188e-05), 1.5)), p.phase.y), 0.079577468335628509521484375, p.march.y)) * _3474;
                int r;
                bool _25;
                bool _26;
                float attenuation;
                bool _3965;
                bool _3966;
                float _4101;
                bool _4102;
                vec4 _4103;
                vec3 _4104;
                int _4105;
                int _4106;
                int _4107;
                bool _4108;
                bool _4109;
                vec4 _4110;
                vec3 _4111;
                vec2 _4112;
                float _4117;
                for (;;)
                {
                    bool _41_ladder_break = false;
                    {
                        if (!(i < _1307))
                        {
                            _41_ladder_break = true;
                        }
                        else
                        {
                            vec3 toLight = p.lampPosition.data[i].xyz - _2518;
                            float _1323 = dot(toLight, toLight);
                            if (p.lampPosition.data[i].w <= 0.0)
                            {
                                _25 = true;
                            }
                            else
                            {
                                _25 = _1323 >= (p.lampPosition.data[i].w * p.lampPosition.data[i].w);
                            }
                            if (!(_25))
                            {
                                {
                                    if (p.shelter1.w < 0.5)
                                    {
                                        _3965 = false;
                                    }
                                    else
                                    {
                                        vec3 _3979 = ((vec4(p.lampPosition.data[i].xyz, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz - vec3(0.0, 0.0, 1.0)) + p.cameraWorld.xyz;
                                        ivec2 _3989 = ivec2(floor((_3979.xy - p.shelter0.xy) * p.shelter0.z));
                                        int _3990 = _3989.x;
                                        if (_3990 < 0)
                                        {
                                            _3966 = true;
                                        }
                                        else
                                        {
                                            _3966 = _3989.y < 0;
                                        }
                                        if (_3966)
                                        {
                                            _3966 = true;
                                        }
                                        else
                                        {
                                            _3966 = _3990 >= int(p.shelter2.x);
                                        }
                                        if (_3966)
                                        {
                                            _3966 = true;
                                        }
                                        else
                                        {
                                            _3966 = _3989.y >= int(p.shelter2.y);
                                        }
                                        if (_3966)
                                        {
                                            _3965 = false;
                                        }
                                        else
                                        {
                                            vec4 _4022 = texelFetch(_4712, ivec3(_3989, 0).xy, 0);
                                            float _4033 = floor((_4022.z * 255.0) + 0.5) + (floor((_4022.y * 255.0) + 0.5) * 256.0);
                                            if (_4033 < 0.5)
                                            {
                                                _3965 = false;
                                            }
                                            else
                                            {
                                                float _4043 = p.shelter0.w + ((_4033 - 1.0) * p.shelter1.x);
                                                float _4044 = _3979.z;
                                                if (_4043 > (_4044 + p.shelter1.y))
                                                {
                                                    _3966 = _4043 < (_4044 + p.shelter1.z);
                                                }
                                                else
                                                {
                                                    _3966 = false;
                                                }
                                                _3965 = _3966;
                                            }
                                        }
                                    }
                                }
                                if (_3965)
                                {
                                    _26 = !_2520;
                                }
                                else
                                {
                                    _26 = false;
                                }
                                if (!(_26))
                                {
                                    float ratio = _1323 / (p.lampPosition.data[i].w * p.lampPosition.data[i].w);
                                    float _1356 = clamp(1.0 - (ratio * ratio), 0.0, 1.0);
                                    float attenuation_1 = (_1356 * _1356) / (_1323 + 1.0);
                                    vec3 direction = toLight / vec3(sqrt(max(_1323, 9.9999999392252902907785028219223e-09)));
                                    if (dot(p.lampDirection.data[i].xyz, p.lampDirection.data[i].xyz) > 0.25)
                                    {
                                        attenuation = attenuation_1 * max(dot(normalize(p.lampDirection.data[i].xyz), -direction), 0.0);
                                    }
                                    else
                                    {
                                        attenuation = attenuation_1;
                                    }
                                    float _1396 = dot(_648, direction);
                                    float _4078 = p.medium.w * p.medium.w;
                                    float _4090 = p.phase.x * p.phase.x;
                                    vec3 lampLight = ((p.lampColour.data[i].xyz * p.lampColour.data[i].w) * attenuation) * mix(mix((1.0 - _4078) / (12.56637096405029296875 * pow(max((1.0 + _4078) - ((2.0 * p.medium.w) * _1396), 9.9999997473787516355514526367188e-05), 1.5)), (1.0 - _4090) / (12.56637096405029296875 * pow(max((1.0 + _4090) - ((2.0 * p.phase.x) * _1396), 9.9999997473787516355514526367188e-05), 1.5)), p.phase.y), 0.079577468335628509521484375, p.march.y);
                                    {
                                        if (i < 0)
                                        {
                                            _4102 = true;
                                        }
                                        else
                                        {
                                            _4102 = i >= 160;
                                        }
                                        if (_4102)
                                        {
                                            _4101 = -1.0;
                                        }
                                        else
                                        {
                                            int _4130 = i >> 2;
                                            int _4133 = i & 3;
                                            if (uFogLampMapParams.mapSlot.data[_4130][_4133] < 0)
                                            {
                                                _4101 = -1.0;
                                            }
                                            else
                                            {
                                                int _4139 = uFogLampMapParams.mapSlot.data[_4130][_4133] & 255;
                                                if ((uFogLampMapParams.mapSlot.data[_4130][_4133] & 256) != 0)
                                                {
                                                    _4102 = false;
                                                    _4103 = vec4(0.0, 0.0, 0.0, 1.0);
                                                    _4104 = vec3(0.0);
                                                    _4105 = 0;
                                                    _4106 = _4139;
                                                    for (;;)
                                                    {
                                                        if (_4105 < 6)
                                                        {
                                                            _4108 = !_4102;
                                                        }
                                                        else
                                                        {
                                                            _4108 = false;
                                                        }
                                                        if (!_4108)
                                                        {
                                                            break;
                                                        }
                                                        int _4180 = _4106 + _4105;
                                                        vec4 _4185 = vec4(world, 1.0) * spvWorkaroundRowMajor(uFogLampMapParams.mapMatrix.data[_4180]);
                                                        float _4186 = _4185.w;
                                                        if (_4186 > 9.9999997473787516355514526367188e-05)
                                                        {
                                                            vec3 _4191 = _4185.xyz / vec3(_4186);
                                                            if (all(lessThanEqual(abs(_4191.xy), vec2(1.0))))
                                                            {
                                                                _4109 = true;
                                                                _4110 = _4185;
                                                                _4111 = _4191;
                                                                _4107 = _4180;
                                                            }
                                                            else
                                                            {
                                                                _4109 = _4102;
                                                                _4110 = _4103;
                                                                _4111 = _4104;
                                                                _4107 = _4106;
                                                            }
                                                            _4102 = _4109;
                                                            _4103 = _4110;
                                                            _4104 = _4111;
                                                            _4106 = _4107;
                                                        }
                                                        _4105++;
                                                        continue;
                                                    }
                                                    if (!_4102)
                                                    {
                                                        _4101 = -1.0;
                                                    }
                                                    else
                                                    {
                                                        int _4221 = _4106 >> 1;
                                                        if ((_4106 & 1) != 0)
                                                        {
                                                            _4112 = uFogLampMapParams.mapDepth.data[_4221].zw;
                                                        }
                                                        else
                                                        {
                                                            _4112 = uFogLampMapParams.mapDepth.data[_4221].xy;
                                                        }
                                                        float _4238 = _4112.y / ((-1.0) - _4112.x);
                                                        if (_4103.w <= _4238)
                                                        {
                                                            _4101 = 1.0;
                                                        }
                                                        else
                                                        {
                                                            float _4253 = ((_4112.x + (_4112.y / max(max(_4103.w - (0.0500000007450580596923828125 + (0.00999999977648258209228515625 * _4103.w)), _4238 + 0.004999999888241291046142578125), 9.9999997473787516355514526367188e-05))) * 0.5) + 0.5;
                                                            uvec3 _4255 = uvec3(textureSize(_4720, 0));
                                                            uint _4256 = _4255.x;
                                                            uint _4257 = _4255.y;
                                                            ivec2 _4277 = ivec2(floor((vec2((_4104.x * 0.5) + 0.5, 0.5 - (_4104.y * 0.5)) * vec2(float(_4256), float(_4257))) - vec2(0.5)));
                                                            ivec2 _4284 = ivec2(int(_4256) - 1, int(_4257) - 1);
                                                            _4105 = 0;
                                                            float _4116 = 0.0;
                                                            for (;;)
                                                            {
                                                                if (!(_4105 < 2))
                                                                {
                                                                    break;
                                                                }
                                                                _4107 = 0;
                                                                for (;;)
                                                                {
                                                                    if (!(_4107 < 2))
                                                                    {
                                                                        break;
                                                                    }
                                                                    if (texelFetch(_4720, ivec4(clamp(_4277 + ivec2(_4107, _4105), ivec2(0), _4284), _4106, 0).xyz, 0).x >= _4253)
                                                                    {
                                                                        _4117 = 0.25;
                                                                    }
                                                                    else
                                                                    {
                                                                        _4117 = 0.0;
                                                                    }
                                                                    _4107++;
                                                                    _4116 += _4117;
                                                                    continue;
                                                                }
                                                                _4105++;
                                                                continue;
                                                            }
                                                            _4101 = _4116;
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    vec4 _4148 = vec4(world, 1.0) * spvWorkaroundRowMajor(uFogLampMapParams.mapMatrix.data[_4139]);
                                                    float _4149 = _4148.w;
                                                    if (_4149 <= 9.9999997473787516355514526367188e-05)
                                                    {
                                                        _4101 = -1.0;
                                                    }
                                                    else
                                                    {
                                                        vec3 _4155 = _4148.xyz / vec3(_4149);
                                                        if (any(greaterThan(abs(_4155.xy), vec2(1.0))))
                                                        {
                                                            _4101 = -1.0;
                                                        }
                                                        else
                                                        {
                                                            _4103 = _4148;
                                                            _4104 = _4155;
                                                            _4106 = _4139;
                                                            int _4221 = _4106 >> 1;
                                                            if ((_4106 & 1) != 0)
                                                            {
                                                                _4112 = uFogLampMapParams.mapDepth.data[_4221].zw;
                                                            }
                                                            else
                                                            {
                                                                _4112 = uFogLampMapParams.mapDepth.data[_4221].xy;
                                                            }
                                                            float _4238 = _4112.y / ((-1.0) - _4112.x);
                                                            if (_4103.w <= _4238)
                                                            {
                                                                _4101 = 1.0;
                                                            }
                                                            else
                                                            {
                                                                float _4253 = ((_4112.x + (_4112.y / max(max(_4103.w - (0.0500000007450580596923828125 + (0.00999999977648258209228515625 * _4103.w)), _4238 + 0.004999999888241291046142578125), 9.9999997473787516355514526367188e-05))) * 0.5) + 0.5;
                                                                uvec3 _4255 = uvec3(textureSize(_4720, 0));
                                                                uint _4256 = _4255.x;
                                                                uint _4257 = _4255.y;
                                                                ivec2 _4277 = ivec2(floor((vec2((_4104.x * 0.5) + 0.5, 0.5 - (_4104.y * 0.5)) * vec2(float(_4256), float(_4257))) - vec2(0.5)));
                                                                ivec2 _4284 = ivec2(int(_4256) - 1, int(_4257) - 1);
                                                                _4105 = 0;
                                                                float _4116 = 0.0;
                                                                for (;;)
                                                                {
                                                                    if (!(_4105 < 2))
                                                                    {
                                                                        break;
                                                                    }
                                                                    _4107 = 0;
                                                                    for (;;)
                                                                    {
                                                                        if (!(_4107 < 2))
                                                                        {
                                                                            break;
                                                                        }
                                                                        if (texelFetch(_4720, ivec4(clamp(_4277 + ivec2(_4107, _4105), ivec2(0), _4284), _4106, 0).xyz, 0).x >= _4253)
                                                                        {
                                                                            _4117 = 0.25;
                                                                        }
                                                                        else
                                                                        {
                                                                            _4117 = 0.0;
                                                                        }
                                                                        _4107++;
                                                                        _4116 += _4117;
                                                                        continue;
                                                                    }
                                                                    _4105++;
                                                                    continue;
                                                                }
                                                                _4101 = _4116;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (_4101 >= 0.0)
                                    {
                                        direct += (lampLight * _4101);
                                    }
                                    else
                                    {
                                        float _1760 = dot(lampLight, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875));
                                        r = 2;
                                        int r_1 = 1;
                                        for (;;)
                                        {
                                            if (!(r_1 >= 0))
                                            {
                                                break;
                                            }
                                            if (_1760 > rankedLuminance[r_1])
                                            {
                                                r = r_1;
                                            }
                                            r_1--;
                                            continue;
                                        }
                                        if (r < 2)
                                        {
                                            int r_2 = 1;
                                            for (;;)
                                            {
                                                if (!(r_2 > r))
                                                {
                                                    break;
                                                }
                                                int _1796 = r_2 - 1;
                                                rankedLamp[r_2] = rankedLamp[_1796];
                                                rankedLight[r_2] = rankedLight[_1796];
                                                rankedLuminance[r_2] = rankedLuminance[_1796];
                                                r_2 = _1796;
                                                continue;
                                            }
                                            rankedLamp[r] = i;
                                            rankedLight[r] = lampLight;
                                            rankedLuminance[r] = _1760;
                                        }
                                        direct += lampLight;
                                    }
                                }
                            }
                        }
                    }
                    if (_41_ladder_break)
                    {
                        break;
                    }
                    i++;
                    continue;
                }
                r = 0;
                vec3 sky;
                float _4335;
                bool _4338;
                bool _4339;
                float _4341;
                float _4414;
                bool _4416;
                for (;;)
                {
                    if (!(r < 2))
                    {
                        break;
                    }
                    if (rankedLamp[r] >= 0)
                    {
                        _4334 = false;
                        {
                            if (int(p.fieldMisc.x + 0.5) <= 0)
                            {
                                _4334 = true;
                                _4335 = 1.0;
                            }
                            else
                            {
                                vec3 _4362 = (vec4(p.lampPosition.data[rankedLamp[r]].xyz, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz - world;
                                float _4363 = length(_4362);
                                float _4365 = _4363 - max(clamp(p.lampDirection.data[rankedLamp[r]].w * 0.5, 0.0500000007450580596923828125, 0.20000000298023223876953125), p.fieldOrigin.data[0].w);
                                if (_4365 <= 0.0)
                                {
                                    _4334 = true;
                                    _4335 = 1.0;
                                }
                                else
                                {
                                    vec3 _4370 = _4362 / vec3(_4363);
                                    int _4336 = 0;
                                    float _4337 = 0.0;
                                    for (;;)
                                    {
                                        if (_4336 < 48)
                                        {
                                            _4338 = _4337 < _4365;
                                        }
                                        else
                                        {
                                            _4338 = false;
                                        }
                                        if (!_4338)
                                        {
                                            break;
                                        }
                                        vec3 _4387 = world + (_4370 * _4337);
                                        _4413 = false;
                                        {
                                            _4339 = false;
                                            _4341 = p.fieldOrigin.data[0].w;
                                            int _4415 = 0;
                                            for (;;)
                                            {
                                                bool _4427_ladder_break = false;
                                                {
                                                    if (!(_4415 < int(p.fieldMisc.x + 0.5)))
                                                    {
                                                        _4427_ladder_break = true;
                                                    }
                                                    else
                                                    {
                                                        vec3 _4445 = (_4387 - p.fieldOrigin.data[_4415].xyz) / vec3(p.fieldOrigin.data[_4415].w);
                                                        if (any(lessThan(_4445, vec3(0.5))))
                                                        {
                                                            _4416 = true;
                                                        }
                                                        else
                                                        {
                                                            _4416 = any(greaterThan(_4445, vec3(float(int(p.fieldMisc.y + 0.5)) - 0.5, float(int(p.fieldMisc.y + 0.5)) - 0.5, float(int(p.fieldMisc.y + 0.5)) - 0.5)));
                                                        }
                                                        if (!(_4416))
                                                        {
                                                            _4339 = true;
                                                            _4341 = p.fieldOrigin.data[_4415].w;
                                                            _4413 = true;
                                                            _4414 = ((textureLod(_4714, vec3(_4445.x / float(int(p.fieldMisc.y + 0.5)), _4445.y / float(int(p.fieldMisc.y + 0.5)), ((float(_4415) * float(int(p.fieldMisc.y + 0.5))) + _4445.z) / float(int(p.fieldMisc.y + 0.5) * int(p.fieldMisc.x + 0.5))), 0.0).x * 2.0) - 1.0) * p.fieldRange[_4415];
                                                            _4427_ladder_break = true;
                                                        }
                                                    }
                                                }
                                                if (_4427_ladder_break)
                                                {
                                                    break;
                                                }
                                                _4415++;
                                                continue;
                                            }
                                            if (!(_4413))
                                            {
                                                _4413 = true;
                                                _4414 = 1000000.0;
                                            }
                                        }
                                        if (!_4339)
                                        {
                                            _4334 = true;
                                            _4335 = 1.0;
                                            break;
                                        }
                                        if (_4414 < ((0.5 * _4341) * clamp(_4337 / max(2.0 * _4341, 9.9999999747524270787835121154785e-07), 0.0, 1.0)))
                                        {
                                            _4334 = true;
                                            _4335 = 0.0;
                                            break;
                                        }
                                        _4336++;
                                        _4337 += max(_4414, _4341 * 0.25);
                                        continue;
                                    }
                                    if (!(_4334))
                                    {
                                        _4334 = true;
                                        _4335 = 1.0;
                                    }
                                }
                            }
                        }
                        sky = direct - (rankedLight[r] * (1.0 - _4335));
                    }
                    else
                    {
                        sky = direct;
                    }
                    r++;
                    direct = sky;
                    continue;
                }
                if (p.ambient.w > 0.5)
                {
                    vec3 _2155 = (-(vec4(_648, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz) * p.medium.w;
                    vec4 _4549 = vec4(_2155, 1.0);
                    vec4 _4565 = _4549.xyzz * _4549.yzzx;
                    float _4583 = _2155.x;
                    float _4585 = _2155.y;
                    sky = max(vec3(0.0), (vec3(dot(texelFetch(_4723, ivec2(0), 0), _4549), dot(texelFetch(_4723, ivec2(1, 0), 0), _4549), dot(texelFetch(_4723, ivec2(2, 0), 0), _4549)) + vec3(dot(texelFetch(_4723, ivec2(3, 0), 0), _4565), dot(texelFetch(_4723, ivec2(4, 0), 0), _4565), dot(texelFetch(_4723, ivec2(5, 0), 0), _4565))) + (texelFetch(_4723, ivec2(6, 0), 0).xyz * ((_4583 * _4583) - (_4585 * _4585))));
                }
                else
                {
                    sky = p.ambient.xyz;
                }
                direct = ((max(direct, vec3(0.0)) * _649) + ((sky * p.march.w) * _2615)) * p.albedo.xyz;
            }
            else
            {
                direct = vec3(0.0);
            }
            vec4 result_1 = vec4(direct, _649);
            vec4 result;
            if (p.jitter.w > 0.5)
            {
                float _4604 = (exp2((_203 + 0.5) / p.zParams.z) - p.zParams.y) / p.zParams.x;
                vec2 _4609 = (((_196 + vec2(0.5)) / p.grid.xy) * 2.0) - vec2(1.0);
                vec3 historyUvw;
                bool _4624;
                {
                    historyUvw = vec3(0.0);
                    vec4 _4636 = vec4((vec4(vec3((_4609.x * _4604) / p.unproject.x, ((-_4609.y) * _4604) / p.unproject.y, _4604), 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz + p.camera.xyz, 0.0) * spvWorkaroundRowMajor(p.prevWorldToView);
                    float _4638 = _4636.z;
                    if (_4638 <= p.prevUnproject.z)
                    {
                        _4624 = false;
                    }
                    else
                    {
                        vec3 _4665 = vec3((vec2((_4636.x * p.prevUnproject.x) / _4638, ((-_4636.y) * p.prevUnproject.y) / _4638) * 0.5) + vec2(0.5), (p.zParams.z * log2(max((p.zParams.x * _4638) + p.zParams.y, 9.9999999747524270787835121154785e-07))) * p.zParams.w);
                        historyUvw = _4665;
                        bool _4625;
                        if (all(greaterThanEqual(_4665, vec3(0.0))))
                        {
                            _4625 = all(lessThanEqual(historyUvw, vec3(1.0)));
                        }
                        else
                        {
                            _4625 = false;
                        }
                        _4624 = _4625;
                    }
                }
                if (_4624)
                {
                    result = mix(result_1, textureLod(_4725, historyUvw, 0.0), vec4(p.grid.w));
                }
                else
                {
                    result = result_1;
                }
            }
            else
            {
                result = result_1;
            }
            imageStore(uScattering, ivec3(gl_GlobalInvocationID), max(result, vec4(0.0)));
        }
    }
}

