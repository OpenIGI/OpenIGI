#version 430
#extension GL_EXT_texture_shadow_lod : enable
#ifdef GL_EXT_texture_shadow_lod
#define spvShadowLod0(s, c) textureLod(s, c, 0.0)
#else
#define spvShadowLod0(s, c) textureGrad(s, c, vec2(0.0), vec2(0.0))
#endif
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

struct _Array_std140_vector_float_4_160
{
    vec4 data[160];
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

uniform vec2 _1761_comp[12] = vec2[](vec2(-0.3260000050067901611328125, -0.4059999883174896240234375), vec2(-0.839999973773956298828125, -0.07400000095367431640625), vec2(-0.69599997997283935546875, 0.4569999873638153076171875), vec2(-0.20299999415874481201171875, 0.620999991893768310546875), vec2(0.96200001239776611328125, -0.194999992847442626953125), vec2(0.472999989986419677734375, -0.4799999892711639404296875), vec2(0.518999993801116943359375, 0.767000019550323486328125), vec2(0.185000002384185791015625, -0.89300000667572021484375), vec2(0.507000029087066650390625, 0.064000003039836883544921875), vec2(0.89600002765655517578125, 0.412000000476837158203125), vec2(-0.3219999969005584716796875, -0.933000028133392333984375), vec2(-0.791999995708465576171875, -0.597999989986419677734375));

struct _Array_std140_vector_float_4_3840
{
    vec4 data[3840];
};

layout(binding = 0, std140) uniform Params_std140
{
    layout(row_major) mat4 viewToWorld;
    vec4 unproject;
    vec4 resolution;
    vec4 cameraFrame;
    vec4 sunLight;
    vec4 lampControl;
    vec4 lampSource;
    _Array_std140_vector_float_4_160 lampPositionRadius;
    _Array_std140_vector_float_4_160 lampColourIntensity;
    _Array_std140_vector_float_4_160 lampDirection;
    _Array_std140_vector_float_4_160 lampRect;
} p;

layout(binding = 0, std430) restrict readonly buffer StructuredBuffer
{
    uint _member0[];
} uLampGrid;

layout(binding = 1, std140) uniform FieldParams_std140
{
    _Array_std140_vector_float_4_4 origin;
    vec4 range;
    ivec4 levels;
    layout(row_major) _Array_std140_matrix_float_4_4_240 mapMatrix;
    _Array_std140_vector_float_4_120 mapDepth;
    _Array_std140_vector_int_4_40 mapSlot;
} f;

layout(binding = 2, std140) uniform CoarseParams_std140
{
    _Array_std140_vector_float_4_3840 record;
} c;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uVisibility;
layout(binding = 0) uniform sampler2D _5007;
layout(binding = 1) uniform sampler2D _5009;
layout(binding = 3) uniform sampler2D _5012;
layout(binding = 4) uniform sampler2DArray _5015;
layout(binding = 5) uniform sampler2DArrayShadow _5017;
layout(binding = 6) uniform sampler2DArray _5020;
layout(binding = 2) uniform sampler3D _5022;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    bool _4767 = false;
    bool _4581 = false;
    bool _4395 = false;
    bool _4084 = false;
    {
        ivec2 _209 = ivec2(gl_GlobalInvocationID.xy);
        bool lampGridded;
        if (_209.x >= int(p.resolution.x))
        {
            lampGridded = true;
        }
        else
        {
            lampGridded = _209.y >= int(p.resolution.y);
        }
        if (!(lampGridded))
        {
            int lampCount = int(p.lampControl.x + 0.5);
            if (lampCount <= 0)
            {
                imageStore(uVisibility, ivec2(gl_GlobalInvocationID.xy), vec4(1.0, 0.0, 0.0, 0.0));
            }
            else
            {
                vec2 uv = (vec2(_209) + vec2(0.5)) * p.resolution.zw;
                vec4 _3023 = textureLod(_5007, uv, 0.0);
                float _3024 = _3023.x;
                if (_3024 <= 0.0)
                {
                    imageStore(uVisibility, ivec2(gl_GlobalInvocationID.xy), vec4(1.0, 0.0, 0.0, 0.0));
                }
                else
                {
                    float _3053 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _3024)) * (p.unproject.w - p.unproject.z)));
                    vec2 _3030 = (uv * 2.0) - vec2(1.0);
                    vec3 _3040 = vec3((_3030.x * _3053) / p.unproject.x, ((-_3030.y) * _3053) / p.unproject.y, _3053);
                    vec3 _349 = normalize((textureLod(_5009, uv, 0.0).xyz * 2.0) - vec3(1.0));
                    float _362 = _3040.z;
                    float originLift = 0.07999999821186065673828125 + (0.0005000000237487256526947021484375 * _362);
                    vec3 _368 = ((vec4(_3040, 1.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz + p.cameraFrame.xyz) + (_349 * originLift);
                    bool rawNoise = p.lampControl.w > 0.5;
                    float frame;
                    if (rawNoise)
                    {
                        frame = 0.0;
                    }
                    else
                    {
                        frame = float(uint(p.cameraFrame.w) % 65536u);
                    }
                    vec3 pixelRandom;
                    if (rawNoise)
                    {
                        uvec3 _3134 = (uvec3(gl_GlobalInvocationID.xy, uint(p.cameraFrame.w) ^ 1435814677u) * uvec3(1664525u)) + uvec3(1013904223u);
                        _3134.x = _3134.x + (_3134.y * _3134.z);
                        _3134.y = _3134.y + (_3134.z * _3134.x);
                        _3134.z = _3134.z + (_3134.x * _3134.y);
                        uvec3 _3161 = _3134 ^ (_3134 >> uvec3(16u));
                        _3161.x = _3161.x + (_3161.y * _3161.z);
                        _3161.y = _3161.y + (_3161.z * _3161.x);
                        _3161.z = _3161.z + (_3161.x * _3161.y);
                        pixelRandom = vec3(_3161 >> uvec3(8u)) * 5.9604644775390625e-08;
                    }
                    else
                    {
                        uvec3 _3069 = (uvec3(gl_GlobalInvocationID.xy, 2654435769u) * uvec3(1664525u)) + uvec3(1013904223u);
                        _3069.x = _3069.x + (_3069.y * _3069.z);
                        _3069.y = _3069.y + (_3069.z * _3069.x);
                        _3069.z = _3069.z + (_3069.x * _3069.y);
                        uvec3 _3096 = _3069 ^ (_3069 >> uvec3(16u));
                        _3096.x = _3096.x + (_3096.y * _3096.z);
                        _3096.y = _3096.y + (_3096.z * _3096.x);
                        _3096.z = _3096.z + (_3096.x * _3096.y);
                        pixelRandom = vec3(_3096 >> uvec3(8u)) * 5.9604644775390625e-08;
                    }
                    float _500 = fract(pixelRandom.x + (frame * 0.61803400516510009765625));
                    int k = 0;
                    int chosen[2];
                    float pick[2];
                    for (;;)
                    {
                        if (!(k < 2))
                        {
                            break;
                        }
                        chosen[k] = -1;
                        pick[k] = fract(_500 + (float(k) * 0.5));
                        k++;
                        continue;
                    }
                    uint lampFirst = 0u;
                    uint lampListed = 0u;
                    uint lampCell;
                    bool _3183;
                    {
                        lampCell = 0u;
                        bool _3184;
                        if (uLampGrid._member0[0] == 0u)
                        {
                            _3184 = true;
                        }
                        else
                        {
                            _3184 = uLampGrid._member0[1] == 0u;
                        }
                        if (_3184)
                        {
                            _3183 = false;
                        }
                        else
                        {
                            float _3205 = uintBitsToFloat(uLampGrid._member0[3]);
                            int _3185;
                            if (_362 <= _3205)
                            {
                                _3185 = 0;
                            }
                            else
                            {
                                _3185 = int(floor(log2(_362 / _3205) * uintBitsToFloat(uLampGrid._member0[4])));
                            }
                            lampCell = (((uint(clamp(_3185, 0, int(uLampGrid._member0[2]) - 1)) * uLampGrid._member0[1]) + uint(clamp(int(floor(uv.y * uintBitsToFloat(uLampGrid._member0[6]))), 0, int(uLampGrid._member0[1]) - 1))) * uLampGrid._member0[0]) + uint(clamp(int(floor(uv.x * uintBitsToFloat(uLampGrid._member0[5]))), 0, int(uLampGrid._member0[0]) - 1));
                            _3183 = true;
                        }
                    }
                    if (_3183)
                    {
                        uint _3253 = lampCell * 32u;
                        uint _3254 = 8u + _3253;
                        lampListed = uLampGrid._member0[_3254];
                        lampFirst = _3253 + 9u;
                        lampGridded = uLampGrid._member0[_3254] != 4294967295u;
                    }
                    else
                    {
                        lampGridded = false;
                    }
                    if (lampGridded)
                    {
                        k = int(lampListed);
                    }
                    else
                    {
                        k = lampCount;
                    }
                    int n = 0;
                    float weightSum = 0.0;
                    float sunlit;
                    float visibleSum;
                    int i;
                    float _3267;
                    float _3302;
                    for (;;)
                    {
                        bool _86_ladder_break = false;
                        {
                            if (!(n < k))
                            {
                                _86_ladder_break = true;
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
                                vec3 toLightView = p.lampPositionRadius.data[i].xyz - _3040;
                                float _690 = dot(toLightView, toLightView);
                                {
                                    float _3271 = p.lampPositionRadius.data[i].w * p.lampPositionRadius.data[i].w;
                                    if (_690 >= _3271)
                                    {
                                        _3267 = 0.0;
                                    }
                                    else
                                    {
                                        float _3276 = _690 / max(_3271, 9.9999997473787516355514526367188e-05);
                                        float _3279 = clamp(1.0 - (_3276 * _3276), 0.0, 1.0);
                                        _3267 = _3279 * _3279;
                                    }
                                }
                                if (!(_3267 <= 0.0))
                                {
                                    vec3 toCentre = (vec4(toLightView, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz;
                                    vec3 centreDir = toCentre * inversesqrt(max(_690, 9.9999999392252902907785028219223e-09));
                                    if (dot(p.lampDirection.data[i].xyz, p.lampDirection.data[i].xyz) > 0.25)
                                    {
                                        float window = _3267 * max(dot(normalize((vec4(p.lampDirection.data[i].xyz, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz), -centreDir), 0.0);
                                        if (!(window <= 0.0))
                                        {
                                            sunlit = window;
                                            float _766 = clamp(p.lampDirection.data[i].w, 0.0500000007450580596923828125, 1.0);
                                            vec3 halfAxis = (vec4(p.lampRect.data[i].xyz, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz;
                                            if (dot(halfAxis, halfAxis) > 9.9999997473787516355514526367188e-05)
                                            {
                                                float _783 = clamp(p.lampRect.data[i].w, 0.0199999995529651641845703125, _766);
                                                {
                                                    vec3 _3306 = toCentre - halfAxis;
                                                    vec3 _3307 = toCentre + halfAxis;
                                                    float _3309 = max(dot(_3306, _3306), 9.9999999392252902907785028219223e-09);
                                                    float _3311 = max(dot(_3307, _3307), 9.9999999392252902907785028219223e-09);
                                                    float _3313 = max(dot(toCentre, toCentre), 9.9999999392252902907785028219223e-09);
                                                    float _3316 = dot(_349, _3306) * inversesqrt(_3309);
                                                    float _3319 = dot(_349, _3307) * inversesqrt(_3311);
                                                    if (max(_3316, _3319) <= 0.0)
                                                    {
                                                        _3302 = 0.0;
                                                    }
                                                    else
                                                    {
                                                        float _3324 = _783 * _783;
                                                        _3302 = max(max(clamp(_3316, 0.0, 1.0) / max(_3309, _3324), clamp(_3319, 0.0, 1.0) / max(_3311, _3324)), clamp(dot(_349, toCentre) * inversesqrt(_3313), 0.0, 1.0) / max(_3313, _3324));
                                                    }
                                                }
                                                visibleSum = _3302;
                                            }
                                            else
                                            {
                                                visibleSum = clamp(dot(_349, centreDir), 0.0, 1.0) / max(_690, _766 * _766);
                                            }
                                            float weight = ((2.0 * sunlit) * visibleSum) * dot(p.lampColourIntensity.data[i].xyz * p.lampColourIntensity.data[i].w, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875));
                                            if (!(weight <= 9.9999999747524270787835121154785e-07))
                                            {
                                                float weightSum_1 = weightSum + weight;
                                                float _873 = weight / max(weightSum_1, 9.9999999747524270787835121154785e-07);
                                                int k_1 = 0;
                                                for (;;)
                                                {
                                                    if (!(k_1 < 2))
                                                    {
                                                        break;
                                                    }
                                                    if (pick[k_1] < _873)
                                                    {
                                                        chosen[k_1] = i;
                                                        pick[k_1] /= max(_873, 9.9999999747524270787835121154785e-07);
                                                    }
                                                    else
                                                    {
                                                        pick[k_1] = (pick[k_1] - _873) / max(1.0 - _873, 9.9999999747524270787835121154785e-07);
                                                    }
                                                    pick[k_1] = min(pick[k_1], 0.999999940395355224609375);
                                                    k_1++;
                                                    continue;
                                                }
                                                weightSum = weightSum_1;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        sunlit = _3267;
                                        float _766 = clamp(p.lampDirection.data[i].w, 0.0500000007450580596923828125, 1.0);
                                        vec3 halfAxis = (vec4(p.lampRect.data[i].xyz, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz;
                                        if (dot(halfAxis, halfAxis) > 9.9999997473787516355514526367188e-05)
                                        {
                                            float _783 = clamp(p.lampRect.data[i].w, 0.0199999995529651641845703125, _766);
                                            {
                                                vec3 _3306 = toCentre - halfAxis;
                                                vec3 _3307 = toCentre + halfAxis;
                                                float _3309 = max(dot(_3306, _3306), 9.9999999392252902907785028219223e-09);
                                                float _3311 = max(dot(_3307, _3307), 9.9999999392252902907785028219223e-09);
                                                float _3313 = max(dot(toCentre, toCentre), 9.9999999392252902907785028219223e-09);
                                                float _3316 = dot(_349, _3306) * inversesqrt(_3309);
                                                float _3319 = dot(_349, _3307) * inversesqrt(_3311);
                                                if (max(_3316, _3319) <= 0.0)
                                                {
                                                    _3302 = 0.0;
                                                }
                                                else
                                                {
                                                    float _3324 = _783 * _783;
                                                    _3302 = max(max(clamp(_3316, 0.0, 1.0) / max(_3309, _3324), clamp(_3319, 0.0, 1.0) / max(_3311, _3324)), clamp(dot(_349, toCentre) * inversesqrt(_3313), 0.0, 1.0) / max(_3313, _3324));
                                                }
                                            }
                                            visibleSum = _3302;
                                        }
                                        else
                                        {
                                            visibleSum = clamp(dot(_349, centreDir), 0.0, 1.0) / max(_690, _766 * _766);
                                        }
                                        float weight = ((2.0 * sunlit) * visibleSum) * dot(p.lampColourIntensity.data[i].xyz * p.lampColourIntensity.data[i].w, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875));
                                        if (!(weight <= 9.9999999747524270787835121154785e-07))
                                        {
                                            float weightSum_1 = weightSum + weight;
                                            float _873 = weight / max(weightSum_1, 9.9999999747524270787835121154785e-07);
                                            int k_1 = 0;
                                            for (;;)
                                            {
                                                if (!(k_1 < 2))
                                                {
                                                    break;
                                                }
                                                if (pick[k_1] < _873)
                                                {
                                                    chosen[k_1] = i;
                                                    pick[k_1] /= max(_873, 9.9999999747524270787835121154785e-07);
                                                }
                                                else
                                                {
                                                    pick[k_1] = (pick[k_1] - _873) / max(1.0 - _873, 9.9999999747524270787835121154785e-07);
                                                }
                                                pick[k_1] = min(pick[k_1], 0.999999940395355224609375);
                                                k_1++;
                                                continue;
                                            }
                                            weightSum = weightSum_1;
                                        }
                                    }
                                }
                            }
                        }
                        if (_86_ladder_break)
                        {
                            break;
                        }
                        n++;
                        continue;
                    }
                    if (p.sunLight.w > 0.0)
                    {
                        lampGridded = weightSum > 0.0;
                    }
                    else
                    {
                        lampGridded = false;
                    }
                    if (lampGridded)
                    {
                        sunlit = (p.sunLight.w * max(dot(_349, p.sunLight.xyz), 0.0)) * clamp(texelFetch(_5012, ivec3(_209, 0).xy, 0).x, 0.0, 1.0);
                    }
                    else
                    {
                        sunlit = 0.0;
                    }
                    if (weightSum <= 0.0)
                    {
                        lampGridded = true;
                    }
                    else
                    {
                        lampGridded = weightSum < p.lampSource.w;
                    }
                    if (lampGridded)
                    {
                        lampGridded = true;
                    }
                    else
                    {
                        lampGridded = weightSum < (0.00999999977648258209228515625 * sunlit);
                    }
                    if (lampGridded)
                    {
                        imageStore(uVisibility, ivec2(gl_GlobalInvocationID.xy), vec4(1.0, 0.0, 0.0, 0.0));
                    }
                    else
                    {
                        k = 0;
                        visibleSum = 0.0;
                        float mappedLitSum = 0.0;
                        float blocked = 0.0;
                        float penumbraSum = 0.0;
                        float fieldRays = 0.0;
                        float nearMissSum = 0.0;
                        float penumbra;
                        vec3 emitting;
                        vec3 axis;
                        bool _43;
                        vec3 up;
                        bool _45;
                        bool onRect;
                        float blocked_1;
                        float penumbraSum_1;
                        float t;
                        float mapped;
                        float sourceWidth;
                        float _3342;
                        float _3343;
                        vec3 _3344;
                        vec4 _3345;
                        mat4 _3346;
                        int _3347;
                        int _3348;
                        int _3349;
                        bool _3351;
                        bool _3352;
                        vec3 _3353;
                        vec4 _3354;
                        mat4 _3355;
                        float _3360;
                        vec2 _3713;
                        vec2 _3729;
                        float _3745;
                        float _3788;
                        float _3823;
                        bool _3824;
                        bool _4085;
                        bool _4088;
                        bool _4089;
                        bool _4090;
                        bool _4092;
                        bool _4097;
                        float _4098;
                        float _4099;
                        bool _4100;
                        float _4101;
                        float _4102;
                        bool _4103;
                        bool _4104;
                        bool _4105;
                        bool _4106;
                        bool _4107;
                        float _4396;
                        bool _4398;
                        float _4582;
                        bool _4584;
                        float _4768;
                        bool _4770;
                        for (;;)
                        {
                            bool _138_ladder_break = false;
                            {
                                if (!(k < 2))
                                {
                                    _138_ladder_break = true;
                                }
                                else
                                {
                                    vec3 lampCentre = (vec4(p.lampPositionRadius.data[chosen[k]].xyz, 1.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz + p.cameraFrame.xyz;
                                    float _1017 = clamp(p.lampDirection.data[chosen[k]].w, 0.0199999995529651641845703125, p.lampSource.x);
                                    if (dot(p.lampDirection.data[chosen[k]].xyz, p.lampDirection.data[chosen[k]].xyz) > 0.25)
                                    {
                                        vec3 _1027 = normalize((vec4(p.lampDirection.data[chosen[k]].xyz, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz);
                                        emitting = _1027 * 0.0199999995529651641845703125;
                                        axis = _1027;
                                    }
                                    else
                                    {
                                        emitting = vec3(0.0);
                                        axis = normalize(lampCentre - _368);
                                    }
                                    vec2 _1048 = fract(pixelRandom.yz + (vec2(0.75487768650054931640625, 0.56984031200408935546875) * ((frame * 2.0) + float(k))));
                                    bool _1056 = dot(emitting, emitting) > 0.0;
                                    if (_1056)
                                    {
                                        lampGridded = p.lampRect.data[chosen[k]].w > 0.0;
                                    }
                                    else
                                    {
                                        lampGridded = false;
                                    }
                                    if (lampGridded)
                                    {
                                        _43 = dot(p.lampRect.data[chosen[k]].xyz, p.lampRect.data[chosen[k]].xyz) > 0.0;
                                    }
                                    else
                                    {
                                        _43 = false;
                                    }
                                    if (_43)
                                    {
                                        vec3 halfLength = (vec4(p.lampRect.data[chosen[k]].xyz, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz;
                                        vec2 uv_1 = (_1048 * 2.0) - vec2(1.0);
                                        up = ((lampCentre + emitting) + (halfLength * uv_1.x)) + ((normalize(cross(axis, halfLength)) * p.lampRect.data[chosen[k]].w) * uv_1.y);
                                    }
                                    else
                                    {
                                        if (abs(axis.z) < 0.89999997615814208984375)
                                        {
                                            up = vec3(0.0, 0.0, 1.0);
                                        }
                                        else
                                        {
                                            up = vec3(1.0, 0.0, 0.0);
                                        }
                                        vec3 _1113 = normalize(cross(up, axis));
                                        float spread = sqrt(_1048.x) * _1017;
                                        float around = 6.283185482025146484375 * _1048.y;
                                        up = ((lampCentre + emitting) + (_1113 * (spread * cos(around)))) + (cross(axis, _1113) * (spread * sin(around)));
                                    }
                                    vec3 toTarget = up - _368;
                                    float _1136 = length(toTarget);
                                    if (_1056)
                                    {
                                        _45 = p.lampRect.data[chosen[k]].w > 0.0;
                                    }
                                    else
                                    {
                                        _45 = false;
                                    }
                                    if (_45)
                                    {
                                        onRect = dot(p.lampRect.data[chosen[k]].xyz, p.lampRect.data[chosen[k]].xyz) > 0.0;
                                    }
                                    else
                                    {
                                        onRect = false;
                                    }
                                    if (onRect)
                                    {
                                        penumbra = 0.0500000007450580596923828125;
                                    }
                                    else
                                    {
                                        penumbra = clamp(_1017 * 0.5, 0.0500000007450580596923828125, 0.20000000298023223876953125);
                                    }
                                    if (_1136 <= (penumbra + 0.0199999995529651641845703125))
                                    {
                                        visibleSum += 1.0;
                                        blocked_1 = blocked;
                                        penumbraSum_1 = penumbraSum;
                                    }
                                    else
                                    {
                                        float nearMiss = 0.0;
                                        if (onRect)
                                        {
                                            weightSum = length(p.lampRect.data[chosen[k]].xyz) + p.lampRect.data[chosen[k]].w;
                                        }
                                        else
                                        {
                                            weightSum = 2.0 * _1017;
                                        }
                                        {
                                            int _3367 = chosen[k] >> 2;
                                            int _3370 = chosen[k] & 3;
                                            if (f.mapSlot.data[_3367][_3370] < 0)
                                            {
                                                _3342 = -1.0;
                                            }
                                            else
                                            {
                                                int _3376 = f.mapSlot.data[_3367][_3370] & 255;
                                                vec4 _3381 = vec4(_368 - p.cameraFrame.xyz, 1.0);
                                                vec4 _3382 = _3381 * spvWorkaroundRowMajor(f.mapMatrix.data[_3376]);
                                                if ((f.mapSlot.data[_3367][_3370] & 256) != 0)
                                                {
                                                    bool _3350 = false;
                                                    _3344 = vec3(0.0);
                                                    _3345 = _3382;
                                                    _3346 = spvWorkaroundRowMajor(f.mapMatrix.data[_3376]);
                                                    _3347 = 0;
                                                    _3348 = _3376;
                                                    for (;;)
                                                    {
                                                        if (_3347 < 6)
                                                        {
                                                            _3351 = !_3350;
                                                        }
                                                        else
                                                        {
                                                            _3351 = false;
                                                        }
                                                        if (!_3351)
                                                        {
                                                            break;
                                                        }
                                                        int _3439 = _3348 + _3347;
                                                        vec4 _3442 = _3381 * spvWorkaroundRowMajor(f.mapMatrix.data[_3439]);
                                                        if (_3442.w > 9.9999997473787516355514526367188e-05)
                                                        {
                                                            vec3 _3448 = _3442.xyz / vec3(_3442.w);
                                                            if (all(lessThanEqual(abs(_3448.xy), vec2(1.0))))
                                                            {
                                                                _3352 = true;
                                                                _3353 = _3448;
                                                                _3354 = _3442;
                                                                _3355 = spvWorkaroundRowMajor(f.mapMatrix.data[_3439]);
                                                                _3349 = _3439;
                                                            }
                                                            else
                                                            {
                                                                _3352 = _3350;
                                                                _3353 = _3344;
                                                                _3354 = _3345;
                                                                _3355 = _3346;
                                                                _3349 = _3348;
                                                            }
                                                            _3350 = _3352;
                                                            _3344 = _3353;
                                                            _3345 = _3354;
                                                            _3346 = _3355;
                                                            _3348 = _3349;
                                                        }
                                                        _3347++;
                                                        continue;
                                                    }
                                                    if (!_3350)
                                                    {
                                                        _3342 = -2.0;
                                                    }
                                                    else
                                                    {
                                                        if (_3344.z < (-1.0))
                                                        {
                                                            _3342 = 1.0;
                                                        }
                                                        else
                                                        {
                                                            vec3 _3490 = vec3(_3346[0].xyz);
                                                            float _3492 = max(length(_3490), 0.001000000047497451305389404296875);
                                                            int _3733 = _3348 >> 1;
                                                            if ((_3348 & 1) != 0)
                                                            {
                                                                _3729 = f.mapDepth.data[_3733].zw;
                                                            }
                                                            else
                                                            {
                                                                _3729 = f.mapDepth.data[_3733].xy;
                                                            }
                                                            float _3498 = _3729.y / ((-1.0) - _3729.x);
                                                            vec2 _3507 = vec2((_3344.x * 0.5) + 0.5, 0.5 - (_3344.y * 0.5));
                                                            uvec3 _3509 = uvec3(textureSize(_5015, 0));
                                                            uint _3510 = _3509.x;
                                                            float _3515 = float(max(_3510, 1u));
                                                            ivec2 _3521 = ivec2(int(_3510), int(_3509.y));
                                                            vec3 _3544 = vec3(dot(_3490, _349) / _3492, dot(vec3(_3346[1].xyz), _349) / _3492, dot(vec3(_3346[3].xyz), _349));
                                                            float _3545 = dot(_3544, vec3((_3344.x * _3345.w) / _3492, (_3344.y * _3345.w) / _3492, _3345.w));
                                                            float _3555 = (0.014999999664723873138427734375 + (0.0030000000260770320892333984375 * _3345.w)) + (1.5 * ((_3345.w * (2.0 / _3492)) / float(max(_3510, 1u))));
                                                            float _3556 = 2.0 / _3492;
                                                            float _3557 = weightSum / _3556;
                                                            float _3560 = max(_3345.w * 0.25, _3498 + 0.00999999977648258209228515625);
                                                            float _3569 = clamp(((_3557 * 0.5) * max(_3345.w - _3560, 0.0)) / (_3560 * _3345.w), 2.0 / _3515, 32.0 / _3515);
                                                            _3347 = -2;
                                                            _3343 = 0.0;
                                                            float _3359 = 0.0;
                                                            for (;;)
                                                            {
                                                                if (!(_3347 <= 2))
                                                                {
                                                                    break;
                                                                }
                                                                _3349 = -2;
                                                                _3360 = _3343;
                                                                float _3361 = _3359;
                                                                for (;;)
                                                                {
                                                                    if (!(_3349 <= 2))
                                                                    {
                                                                        break;
                                                                    }
                                                                    vec2 _3595 = _3507 + (vec2(float(_3349), float(_3347)) * (_3569 * 0.5));
                                                                    vec4 _3606 = texelFetch(_5015, ivec4(clamp(ivec2(_3595 * vec2(_3521)), ivec2(0), _3521 - ivec2(1)), _3348, 0).xyz, 0);
                                                                    float _3607 = _3606.x;
                                                                    float _3758 = dot(_3544, vec3(vec2((_3595.x * 2.0) - 1.0, 1.0 - (_3595.y * 2.0)) / vec2(_3492), 1.0));
                                                                    if (abs(_3758) > 9.9999997473787516355514526367188e-05)
                                                                    {
                                                                        _3745 = _3545 / _3758;
                                                                    }
                                                                    else
                                                                    {
                                                                        _3745 = _3345.w;
                                                                    }
                                                                    float _3765 = 2.0 * ((length(_3595 - _3507) * _3556) * _3345.w);
                                                                    if (_3607 < (((_3729.x + (_3729.y / max(max(clamp(_3745, _3345.w - _3765, _3345.w + _3765) - _3555, _3498 + 0.004999999888241291046142578125), 9.9999997473787516355514526367188e-05))) * 0.5) + 0.5))
                                                                    {
                                                                        _3360 += (_3729.y / min(((_3607 * 2.0) - 1.0) - _3729.x, -9.9999999747524270787835121154785e-07));
                                                                        _3361 += 1.0;
                                                                    }
                                                                    _3349++;
                                                                    continue;
                                                                }
                                                                _3347++;
                                                                _3343 = _3360;
                                                                _3359 = _3361;
                                                                continue;
                                                            }
                                                            if (_3359 <= 0.0)
                                                            {
                                                                _3342 = 1.0;
                                                            }
                                                            else
                                                            {
                                                                float _3646 = max(_3343 / _3359, 0.0500000007450580596923828125);
                                                                float _3654 = clamp(((_3557 * max(_3345.w - _3646, 0.0)) / (_3646 * _3345.w)) * 0.5, 1.0 / _3515, 24.0 / _3515);
                                                                float _3661 = fract(52.98291778564453125 * fract(dot(_3507 * vec2(_3521), vec2(0.067110560834407806396484375, 0.005837149918079376220703125)))) * 6.283185482025146484375;
                                                                float _3662 = sin(_3661);
                                                                float _3663 = cos(_3661);
                                                                _3349 = 0;
                                                                _3360 = 0.0;
                                                                for (;;)
                                                                {
                                                                    if (!(_3349 < 12))
                                                                    {
                                                                        break;
                                                                    }
                                                                    vec2 _3684 = vec2((_1761_comp[_3349].x * _3663) - (_1761_comp[_3349].y * _3662), (_1761_comp[_3349].x * _3662) + (_1761_comp[_3349].y * _3663)) * _3654;
                                                                    vec2 _3685 = _3507 + _3684;
                                                                    float _3801 = dot(_3544, vec3(vec2((_3685.x * 2.0) - 1.0, 1.0 - (_3685.y * 2.0)) / vec2(_3492), 1.0));
                                                                    if (abs(_3801) > 9.9999997473787516355514526367188e-05)
                                                                    {
                                                                        _3788 = _3545 / _3801;
                                                                    }
                                                                    else
                                                                    {
                                                                        _3788 = _3345.w;
                                                                    }
                                                                    float _3808 = 2.0 * ((length(_3684) * _3556) * _3345.w);
                                                                    _3349++;
                                                                    _3360 += spvShadowLod0(_5017, vec4(vec3(_3685, float(_3348)), ((_3729.x + (_3729.y / max(max(clamp(_3788, _3345.w - _3808, _3345.w + _3808) - _3555, _3498 + 0.004999999888241291046142578125), 9.9999997473787516355514526367188e-05))) * 0.5) + 0.5));
                                                                    continue;
                                                                }
                                                                _3342 = _3360 * 0.083333335816860198974609375;
                                                            }
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    int _3717 = _3376 >> 1;
                                                    if ((f.mapSlot.data[_3367][_3370] & 1) != 0)
                                                    {
                                                        _3713 = f.mapDepth.data[_3717].zw;
                                                    }
                                                    else
                                                    {
                                                        _3713 = f.mapDepth.data[_3717].xy;
                                                    }
                                                    float _3390 = _3713.y / ((-1.0) - _3713.x);
                                                    if (_3382.w <= _3390)
                                                    {
                                                        _3342 = 1.0;
                                                    }
                                                    else
                                                    {
                                                        vec3 _3397 = _3382.xyz / vec3(_3382.w);
                                                        if (any(greaterThan(abs(_3397.xy), vec2(0.980000019073486328125))))
                                                        {
                                                            vec3 _3414 = vec3(_3382.xy / vec2(max(length(vec3(spvWorkaroundRowMajor(f.mapMatrix.data[_3376])[0].xyz)), 0.001000000047497451305389404296875)), _3382.w - _3390);
                                                            if (dot(_3414, _3414) < 2.25)
                                                            {
                                                                _3343 = 1.0;
                                                            }
                                                            else
                                                            {
                                                                _3343 = -2.0;
                                                            }
                                                            _3342 = _3343;
                                                        }
                                                        else
                                                        {
                                                            _3345 = _3382;
                                                            _3346 = spvWorkaroundRowMajor(f.mapMatrix.data[_3376]);
                                                            _3344 = _3397;
                                                            _3348 = _3376;
                                                            vec3 _3490 = vec3(_3346[0].xyz);
                                                            float _3492 = max(length(_3490), 0.001000000047497451305389404296875);
                                                            int _3733 = _3348 >> 1;
                                                            if ((_3348 & 1) != 0)
                                                            {
                                                                _3729 = f.mapDepth.data[_3733].zw;
                                                            }
                                                            else
                                                            {
                                                                _3729 = f.mapDepth.data[_3733].xy;
                                                            }
                                                            float _3498 = _3729.y / ((-1.0) - _3729.x);
                                                            vec2 _3507 = vec2((_3344.x * 0.5) + 0.5, 0.5 - (_3344.y * 0.5));
                                                            uvec3 _3509 = uvec3(textureSize(_5015, 0));
                                                            uint _3510 = _3509.x;
                                                            float _3515 = float(max(_3510, 1u));
                                                            ivec2 _3521 = ivec2(int(_3510), int(_3509.y));
                                                            vec3 _3544 = vec3(dot(_3490, _349) / _3492, dot(vec3(_3346[1].xyz), _349) / _3492, dot(vec3(_3346[3].xyz), _349));
                                                            float _3545 = dot(_3544, vec3((_3344.x * _3345.w) / _3492, (_3344.y * _3345.w) / _3492, _3345.w));
                                                            float _3555 = (0.014999999664723873138427734375 + (0.0030000000260770320892333984375 * _3345.w)) + (1.5 * ((_3345.w * (2.0 / _3492)) / float(max(_3510, 1u))));
                                                            float _3556 = 2.0 / _3492;
                                                            float _3557 = weightSum / _3556;
                                                            float _3560 = max(_3345.w * 0.25, _3498 + 0.00999999977648258209228515625);
                                                            float _3569 = clamp(((_3557 * 0.5) * max(_3345.w - _3560, 0.0)) / (_3560 * _3345.w), 2.0 / _3515, 32.0 / _3515);
                                                            _3347 = -2;
                                                            _3343 = 0.0;
                                                            float _3359 = 0.0;
                                                            for (;;)
                                                            {
                                                                if (!(_3347 <= 2))
                                                                {
                                                                    break;
                                                                }
                                                                _3349 = -2;
                                                                _3360 = _3343;
                                                                float _3361 = _3359;
                                                                for (;;)
                                                                {
                                                                    if (!(_3349 <= 2))
                                                                    {
                                                                        break;
                                                                    }
                                                                    vec2 _3595 = _3507 + (vec2(float(_3349), float(_3347)) * (_3569 * 0.5));
                                                                    vec4 _3606 = texelFetch(_5015, ivec4(clamp(ivec2(_3595 * vec2(_3521)), ivec2(0), _3521 - ivec2(1)), _3348, 0).xyz, 0);
                                                                    float _3607 = _3606.x;
                                                                    float _3758 = dot(_3544, vec3(vec2((_3595.x * 2.0) - 1.0, 1.0 - (_3595.y * 2.0)) / vec2(_3492), 1.0));
                                                                    if (abs(_3758) > 9.9999997473787516355514526367188e-05)
                                                                    {
                                                                        _3745 = _3545 / _3758;
                                                                    }
                                                                    else
                                                                    {
                                                                        _3745 = _3345.w;
                                                                    }
                                                                    float _3765 = 2.0 * ((length(_3595 - _3507) * _3556) * _3345.w);
                                                                    if (_3607 < (((_3729.x + (_3729.y / max(max(clamp(_3745, _3345.w - _3765, _3345.w + _3765) - _3555, _3498 + 0.004999999888241291046142578125), 9.9999997473787516355514526367188e-05))) * 0.5) + 0.5))
                                                                    {
                                                                        _3360 += (_3729.y / min(((_3607 * 2.0) - 1.0) - _3729.x, -9.9999999747524270787835121154785e-07));
                                                                        _3361 += 1.0;
                                                                    }
                                                                    _3349++;
                                                                    continue;
                                                                }
                                                                _3347++;
                                                                _3343 = _3360;
                                                                _3359 = _3361;
                                                                continue;
                                                            }
                                                            if (_3359 <= 0.0)
                                                            {
                                                                _3342 = 1.0;
                                                            }
                                                            else
                                                            {
                                                                float _3646 = max(_3343 / _3359, 0.0500000007450580596923828125);
                                                                float _3654 = clamp(((_3557 * max(_3345.w - _3646, 0.0)) / (_3646 * _3345.w)) * 0.5, 1.0 / _3515, 24.0 / _3515);
                                                                float _3661 = fract(52.98291778564453125 * fract(dot(_3507 * vec2(_3521), vec2(0.067110560834407806396484375, 0.005837149918079376220703125)))) * 6.283185482025146484375;
                                                                float _3662 = sin(_3661);
                                                                float _3663 = cos(_3661);
                                                                _3349 = 0;
                                                                _3360 = 0.0;
                                                                for (;;)
                                                                {
                                                                    if (!(_3349 < 12))
                                                                    {
                                                                        break;
                                                                    }
                                                                    vec2 _3684 = vec2((_1761_comp[_3349].x * _3663) - (_1761_comp[_3349].y * _3662), (_1761_comp[_3349].x * _3662) + (_1761_comp[_3349].y * _3663)) * _3654;
                                                                    vec2 _3685 = _3507 + _3684;
                                                                    float _3801 = dot(_3544, vec3(vec2((_3685.x * 2.0) - 1.0, 1.0 - (_3685.y * 2.0)) / vec2(_3492), 1.0));
                                                                    if (abs(_3801) > 9.9999997473787516355514526367188e-05)
                                                                    {
                                                                        _3788 = _3545 / _3801;
                                                                    }
                                                                    else
                                                                    {
                                                                        _3788 = _3345.w;
                                                                    }
                                                                    float _3808 = 2.0 * ((length(_3684) * _3556) * _3345.w);
                                                                    _3349++;
                                                                    _3360 += spvShadowLod0(_5017, vec4(vec3(_3685, float(_3348)), ((_3729.x + (_3729.y / max(max(clamp(_3788, _3345.w - _3808, _3345.w + _3808) - _3555, _3498 + 0.004999999888241291046142578125), 9.9999997473787516355514526367188e-05))) * 0.5) + 0.5));
                                                                    continue;
                                                                }
                                                                _3342 = _3360 * 0.083333335816860198974609375;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        if (_3342 == (-1.0))
                                        {
                                            vec3 _1847 = _368 - p.cameraFrame.xyz;
                                            {
                                                if (chosen[k] < 0)
                                                {
                                                    _3824 = true;
                                                }
                                                else
                                                {
                                                    _3824 = chosen[k] >= 160;
                                                }
                                                if (_3824)
                                                {
                                                    _3823 = -1.0;
                                                }
                                                else
                                                {
                                                    int _3848 = chosen[k] * 6;
                                                    if (c.record.data[(chosen[k] * 24) + 3].z <= 0.0)
                                                    {
                                                        _3823 = -1.0;
                                                    }
                                                    else
                                                    {
                                                        uvec3 _3860 = uvec3(textureSize(_5020, 0));
                                                        uint _3825 = _3860.x;
                                                        uint _3826 = _3860.y;
                                                        float _3828 = 0.25;
                                                        int _3829 = 0;
                                                        for (;;)
                                                        {
                                                            if (!(_3829 < 6))
                                                            {
                                                                break;
                                                            }
                                                            int _3875 = ((_3848 + _3829) * 4) + 2;
                                                            _3828 = max(_3828, dot(c.record.data[_3875].xyz, _1847) + c.record.data[_3875].w);
                                                            _3829++;
                                                            continue;
                                                        }
                                                        vec3 _3899 = _1847 + (_349 * (0.0500000007450580596923828125 + (1.5 * ((_3828 * 2.0) / float(max(_3825, 1u))))));
                                                        float _3830 = -1000000015047466219876688855040.0;
                                                        int _3831 = _3848;
                                                        _3829 = 0;
                                                        for (;;)
                                                        {
                                                            if (!(_3829 < 6))
                                                            {
                                                                break;
                                                            }
                                                            int _3909 = _3848 + _3829;
                                                            int _3911 = (_3909 * 4) + 2;
                                                            float _3917 = dot(c.record.data[_3911].xyz, _3899) + c.record.data[_3911].w;
                                                            if (_3917 > _3830)
                                                            {
                                                                _3830 = _3917;
                                                                _3831 = _3909;
                                                            }
                                                            _3829++;
                                                            continue;
                                                        }
                                                        int _3929 = _3831 * 4;
                                                        int _3930 = _3929 + 3;
                                                        if (c.record.data[_3930].z <= 0.0)
                                                        {
                                                            _3823 = -1.0;
                                                        }
                                                        else
                                                        {
                                                            int _3939 = _3929 + 1;
                                                            if (_3830 <= (c.record.data[_3930].y / ((-1.0) - c.record.data[_3930].x)))
                                                            {
                                                                _3823 = 1.0;
                                                            }
                                                            else
                                                            {
                                                                vec2 _3961 = vec2(dot(c.record.data[_3929].xyz, _3899) + c.record.data[_3929].w, dot(c.record.data[_3939].xyz, _3899) + c.record.data[_3939].w) / vec2(_3830);
                                                                ivec2 _3973 = ivec2(int(_3825), int(_3826));
                                                                float _3987 = (0.0500000007450580596923828125 + (0.004999999888241291046142578125 * _3830)) + (2.0 * ((_3830 * (2.0 / max(length(c.record.data[_3929].xyz), 0.001000000047497451305389404296875))) / float(max(_3825, 1u))));
                                                                int _3990 = int(c.record.data[_3930].w + 0.5);
                                                                vec2 _3994 = (vec2((_3961.x * 0.5) + 0.5, 0.5 - (_3961.y * 0.5)) * vec2(_3973)) - vec2(0.5);
                                                                vec2 _3995 = floor(_3994);
                                                                ivec2 _3998 = ivec2(_3995) - ivec2(1);
                                                                vec2 _3999 = _3994 - _3995;
                                                                int _3834 = 0;
                                                                float _3832 = 0.0;
                                                                float _3833 = 0.0;
                                                                for (;;)
                                                                {
                                                                    if (!(_3834 < 4))
                                                                    {
                                                                        break;
                                                                    }
                                                                    int _3836 = 0;
                                                                    float _3835 = _3833;
                                                                    for (;;)
                                                                    {
                                                                        if (!(_3836 < 4))
                                                                        {
                                                                            break;
                                                                        }
                                                                        int _4018 = _3836;
                                                                        vec2 _4083 = clamp(vec2(1.0) - (abs(vec2(float(_4018) - 1.0, float(_3834) - 1.0) - _3999) * vec2(0.5)), vec2(0.0), vec2(1.0));
                                                                        float _4050 = _4083.x * _4083.y;
                                                                        if ((c.record.data[_3930].y / min(((texelFetch(_5020, ivec4(clamp(_3998 + ivec2(_4018, _3834), ivec2(0), _3973 - ivec2(1)), _3990, 0).xyz, 0).x * 2.0) - 1.0) - c.record.data[_3930].x, -9.9999999747524270787835121154785e-07)) >= (_3830 - _3987))
                                                                        {
                                                                            _3828 = _4050;
                                                                        }
                                                                        else
                                                                        {
                                                                            _3828 = 0.0;
                                                                        }
                                                                        _3836++;
                                                                        _3832 += _3828;
                                                                        _3835 += _4050;
                                                                        continue;
                                                                    }
                                                                    _3834++;
                                                                    _3833 = _3835;
                                                                    continue;
                                                                }
                                                                _3823 = _3832 / max(_3833, 9.9999997473787516355514526367188e-05);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            mapped = _3823;
                                        }
                                        else
                                        {
                                            mapped = _3342;
                                        }
                                        if (mapped >= 0.0)
                                        {
                                            if (mapped < 1.0)
                                            {
                                                float _2205 = 1.0 - mapped;
                                                blocked_1 = blocked + _2205;
                                                penumbraSum_1 = penumbraSum + (_2205 * 0.0500000007450580596923828125);
                                            }
                                            else
                                            {
                                                blocked_1 = blocked;
                                                penumbraSum_1 = penumbraSum;
                                            }
                                            visibleSum += mapped;
                                            mappedLitSum += mapped;
                                        }
                                        else
                                        {
                                            vec3 rayDirection = toTarget / vec3(_1136);
                                            vec3 _2228 = _368 - p.cameraFrame.xyz;
                                            float _2230 = _1136 - penumbra;
                                            float _2232 = max(dot(rayDirection, _349), 0.0);
                                            if (_1056)
                                            {
                                                sunlit = dot(-rayDirection, axis);
                                            }
                                            else
                                            {
                                                sunlit = 0.0;
                                            }
                                            _4084 = false;
                                            {
                                                t = 0.0;
                                                float _4111 = min(_2230, 1.0);
                                                float _4086 = 0.0;
                                                int _4087 = 1;
                                                for (;;)
                                                {
                                                    if (!(_4087 <= 16))
                                                    {
                                                        break;
                                                    }
                                                    float _4123 = _4111 * (float(_4087) * 0.0625);
                                                    vec4 _4129 = spvWorkaroundRowMajor(p.viewToWorld) * vec4(_2228 + (rayDirection * _4123), 0.0);
                                                    float _4131 = _4129.z;
                                                    if (_4131 <= p.unproject.z)
                                                    {
                                                        break;
                                                    }
                                                    float _4364 = _4129.z;
                                                    vec2 _4375 = (vec2((_4129.x * p.unproject.x) / _4364, ((-_4129.y) * p.unproject.y) / _4364) * 0.5) + vec2(0.5);
                                                    if (any(lessThan(_4375, vec2(0.0))))
                                                    {
                                                        _4088 = true;
                                                    }
                                                    else
                                                    {
                                                        _4088 = any(greaterThan(_4375, vec2(1.0)));
                                                    }
                                                    if (_4088)
                                                    {
                                                        break;
                                                    }
                                                    vec4 _4380 = textureLod(_5007, _4375, 0.0);
                                                    float _4381 = _4380.x;
                                                    if (_4381 > 0.0)
                                                    {
                                                        float _4394 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _4381)) * (p.unproject.w - p.unproject.z)));
                                                        float _4155 = _4131 - _4394;
                                                        if (_4155 > 0.0199999995529651641845703125)
                                                        {
                                                            _4089 = _4155 < max(0.0500000007450580596923828125, 0.00999999977648258209228515625 * _4394);
                                                        }
                                                        else
                                                        {
                                                            _4089 = false;
                                                        }
                                                        if (_4089)
                                                        {
                                                            t = _4123;
                                                            _4084 = true;
                                                            _4085 = true;
                                                            break;
                                                        }
                                                    }
                                                    _4086 = _4123;
                                                    _4087++;
                                                    continue;
                                                }
                                                if (!(_4084))
                                                {
                                                    if (f.levels.x <= 0)
                                                    {
                                                        _4084 = true;
                                                        _4085 = false;
                                                    }
                                                    else
                                                    {
                                                        float _4186 = 0.5 * f.origin.data[0].w;
                                                        float _4188 = max(_4086, _4186);
                                                        float _4189 = _2230 - _4186;
                                                        vec3 _4191 = _2228 + (rayDirection * _4188);
                                                        _4395 = false;
                                                        {
                                                            _4090 = false;
                                                            int _4397 = 0;
                                                            for (;;)
                                                            {
                                                                bool _4409_ladder_break = false;
                                                                {
                                                                    if (!(_4397 < f.levels.x))
                                                                    {
                                                                        _4409_ladder_break = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        vec3 _4425 = (_4191 - f.origin.data[_4397].xyz) / vec3(f.origin.data[_4397].w);
                                                                        float _4429 = float(f.levels.y) - 0.5;
                                                                        if (any(lessThan(_4425, vec3(0.5))))
                                                                        {
                                                                            _4398 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _4398 = any(greaterThan(_4425, vec3(_4429)));
                                                                        }
                                                                        if (!(_4398))
                                                                        {
                                                                            _4090 = true;
                                                                            float _4540 = ((textureLod(_5022, vec3(_4425.x / float(f.levels.y), _4425.y / float(f.levels.y), ((float(_4397) * float(f.levels.y)) + _4425.z) / float(f.levels.y * f.levels.x)), 0.0).x * 2.0) - 1.0) * f.range[_4397];
                                                                            vec3 _4447 = min(_4425 - vec3(0.5), vec3(_4429) - _4425);
                                                                            float _4455 = clamp(1.0 - (min(_4447.x, min(_4447.y, _4447.z)) * 0.125), 0.0, 1.0);
                                                                            if (_4455 <= 0.0)
                                                                            {
                                                                                _4398 = true;
                                                                            }
                                                                            else
                                                                            {
                                                                                _4398 = (_4397 + 1) >= f.levels.x;
                                                                            }
                                                                            if (_4398)
                                                                            {
                                                                                _4395 = true;
                                                                                _4396 = _4540;
                                                                                _4409_ladder_break = true;
                                                                            }
                                                                            else
                                                                            {
                                                                                int _4469 = _4397 + 1;
                                                                                vec3 _4476 = (_4191 - f.origin.data[_4469].xyz) / vec3(f.origin.data[_4469].w);
                                                                                if (any(lessThan(_4476, vec3(0.5))))
                                                                                {
                                                                                    _4398 = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4398 = any(greaterThan(_4476, vec3(_4429)));
                                                                                }
                                                                                if (_4398)
                                                                                {
                                                                                    _4395 = true;
                                                                                    _4396 = _4540;
                                                                                    _4409_ladder_break = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4395 = true;
                                                                                    _4396 = mix(_4540, ((textureLod(_5022, vec3(_4476.x / float(f.levels.y), _4476.y / float(f.levels.y), ((float(_4469) * float(f.levels.y)) + _4476.z) / float(f.levels.y * f.levels.x)), 0.0).x * 2.0) - 1.0) * f.range[_4469], _4455);
                                                                                    _4409_ladder_break = true;
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                                if (_4409_ladder_break)
                                                                {
                                                                    break;
                                                                }
                                                                _4397++;
                                                                continue;
                                                            }
                                                            if (!(_4395))
                                                            {
                                                                _4395 = true;
                                                                _4396 = 1000000.0;
                                                            }
                                                        }
                                                        vec3 _4194 = _2228 + (rayDirection * _4189);
                                                        _4581 = false;
                                                        {
                                                            _4092 = false;
                                                            int _4583 = 0;
                                                            for (;;)
                                                            {
                                                                bool _4595_ladder_break = false;
                                                                {
                                                                    if (!(_4583 < f.levels.x))
                                                                    {
                                                                        _4595_ladder_break = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        vec3 _4611 = (_4194 - f.origin.data[_4583].xyz) / vec3(f.origin.data[_4583].w);
                                                                        float _4615 = float(f.levels.y) - 0.5;
                                                                        if (any(lessThan(_4611, vec3(0.5))))
                                                                        {
                                                                            _4584 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _4584 = any(greaterThan(_4611, vec3(_4615)));
                                                                        }
                                                                        if (!(_4584))
                                                                        {
                                                                            _4092 = true;
                                                                            float _4726 = ((textureLod(_5022, vec3(_4611.x / float(f.levels.y), _4611.y / float(f.levels.y), ((float(_4583) * float(f.levels.y)) + _4611.z) / float(f.levels.y * f.levels.x)), 0.0).x * 2.0) - 1.0) * f.range[_4583];
                                                                            vec3 _4633 = min(_4611 - vec3(0.5), vec3(_4615) - _4611);
                                                                            float _4641 = clamp(1.0 - (min(_4633.x, min(_4633.y, _4633.z)) * 0.125), 0.0, 1.0);
                                                                            if (_4641 <= 0.0)
                                                                            {
                                                                                _4584 = true;
                                                                            }
                                                                            else
                                                                            {
                                                                                _4584 = (_4583 + 1) >= f.levels.x;
                                                                            }
                                                                            if (_4584)
                                                                            {
                                                                                _4581 = true;
                                                                                _4582 = _4726;
                                                                                _4595_ladder_break = true;
                                                                            }
                                                                            else
                                                                            {
                                                                                int _4655 = _4583 + 1;
                                                                                vec3 _4662 = (_4194 - f.origin.data[_4655].xyz) / vec3(f.origin.data[_4655].w);
                                                                                if (any(lessThan(_4662, vec3(0.5))))
                                                                                {
                                                                                    _4584 = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4584 = any(greaterThan(_4662, vec3(_4615)));
                                                                                }
                                                                                if (_4584)
                                                                                {
                                                                                    _4581 = true;
                                                                                    _4582 = _4726;
                                                                                    _4595_ladder_break = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4581 = true;
                                                                                    _4582 = mix(_4726, ((textureLod(_5022, vec3(_4662.x / float(f.levels.y), _4662.y / float(f.levels.y), ((float(_4655) * float(f.levels.y)) + _4662.z) / float(f.levels.y * f.levels.x)), 0.0).x * 2.0) - 1.0) * f.range[_4655], _4641);
                                                                                    _4595_ladder_break = true;
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                                if (_4595_ladder_break)
                                                                {
                                                                    break;
                                                                }
                                                                _4583++;
                                                                continue;
                                                            }
                                                            if (!(_4581))
                                                            {
                                                                _4581 = true;
                                                                _4582 = 1000000.0;
                                                            }
                                                        }
                                                        _4088 = true;
                                                        float _4094 = 0.0;
                                                        int _4096 = 0;
                                                        float _4095 = _4188;
                                                        for (;;)
                                                        {
                                                            bool _4198_ladder_break = false;
                                                            {
                                                                if (_4096 < 64)
                                                                {
                                                                    _4089 = _4095 < _4189;
                                                                }
                                                                else
                                                                {
                                                                    _4089 = false;
                                                                }
                                                                if (!_4089)
                                                                {
                                                                    _4198_ladder_break = true;
                                                                }
                                                                else
                                                                {
                                                                    vec3 _4212 = _2228 + (rayDirection * _4095);
                                                                    _4767 = false;
                                                                    {
                                                                        _4097 = false;
                                                                        _4098 = f.origin.data[0].w;
                                                                        int _4769 = 0;
                                                                        for (;;)
                                                                        {
                                                                            bool _4781_ladder_break = false;
                                                                            {
                                                                                if (!(_4769 < f.levels.x))
                                                                                {
                                                                                    _4781_ladder_break = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    vec3 _4797 = (_4212 - f.origin.data[_4769].xyz) / vec3(f.origin.data[_4769].w);
                                                                                    float _4801 = float(f.levels.y) - 0.5;
                                                                                    if (any(lessThan(_4797, vec3(0.5))))
                                                                                    {
                                                                                        _4770 = true;
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _4770 = any(greaterThan(_4797, vec3(_4801)));
                                                                                    }
                                                                                    if (!(_4770))
                                                                                    {
                                                                                        _4097 = true;
                                                                                        float _4912 = ((textureLod(_5022, vec3(_4797.x / float(f.levels.y), _4797.y / float(f.levels.y), ((float(_4769) * float(f.levels.y)) + _4797.z) / float(f.levels.y * f.levels.x)), 0.0).x * 2.0) - 1.0) * f.range[_4769];
                                                                                        _4098 = f.origin.data[_4769].w;
                                                                                        vec3 _4819 = min(_4797 - vec3(0.5), vec3(_4801) - _4797);
                                                                                        float _4827 = clamp(1.0 - (min(_4819.x, min(_4819.y, _4819.z)) * 0.125), 0.0, 1.0);
                                                                                        if (_4827 <= 0.0)
                                                                                        {
                                                                                            _4770 = true;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _4770 = (_4769 + 1) >= f.levels.x;
                                                                                        }
                                                                                        if (_4770)
                                                                                        {
                                                                                            _4767 = true;
                                                                                            _4768 = _4912;
                                                                                            _4781_ladder_break = true;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            int _4841 = _4769 + 1;
                                                                                            vec3 _4848 = (_4212 - f.origin.data[_4841].xyz) / vec3(f.origin.data[_4841].w);
                                                                                            if (any(lessThan(_4848, vec3(0.5))))
                                                                                            {
                                                                                                _4770 = true;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                _4770 = any(greaterThan(_4848, vec3(_4801)));
                                                                                            }
                                                                                            if (_4770)
                                                                                            {
                                                                                                _4767 = true;
                                                                                                _4768 = _4912;
                                                                                                _4781_ladder_break = true;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                _4098 = mix(f.origin.data[_4769].w, f.origin.data[_4841].w, _4827);
                                                                                                _4767 = true;
                                                                                                _4768 = mix(_4912, ((textureLod(_5022, vec3(_4848.x / float(f.levels.y), _4848.y / float(f.levels.y), ((float(_4841) * float(f.levels.y)) + _4848.z) / float(f.levels.y * f.levels.x)), 0.0).x * 2.0) - 1.0) * f.range[_4841], _4827);
                                                                                                _4781_ladder_break = true;
                                                                                            }
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                            if (_4781_ladder_break)
                                                                            {
                                                                                break;
                                                                            }
                                                                            _4769++;
                                                                            continue;
                                                                        }
                                                                        if (!(_4767))
                                                                        {
                                                                            _4767 = true;
                                                                            _4768 = 1000000.0;
                                                                        }
                                                                    }
                                                                    if (!_4097)
                                                                    {
                                                                        _4084 = true;
                                                                        _4085 = false;
                                                                        _4198_ladder_break = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        if (_4088)
                                                                        {
                                                                            if (_4094 <= 0.0)
                                                                            {
                                                                                _4099 = _4095 + (1.5 * _4098);
                                                                            }
                                                                            else
                                                                            {
                                                                                _4099 = _4094;
                                                                            }
                                                                            if (_4768 <= 0.0)
                                                                            {
                                                                                _4100 = _4095 < _4099;
                                                                            }
                                                                            else
                                                                            {
                                                                                _4100 = false;
                                                                            }
                                                                            if (_4100)
                                                                            {
                                                                                _4095 += (_4098 * 0.25);
                                                                            }
                                                                            else
                                                                            {
                                                                                _4100 = false;
                                                                                float _4257 = 0.25 * _4098;
                                                                                if (_4090)
                                                                                {
                                                                                    _4101 = _4396 + ((_4095 - _4188) * _2232);
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4101 = originLift + (_4095 * _2232);
                                                                                }
                                                                                float _4270 = _1136 - _4095;
                                                                                bool _4271 = sunlit <= 0.0;
                                                                                if (_4271)
                                                                                {
                                                                                    _4102 = 1000000000.0;
                                                                                }
                                                                                else
                                                                                {
                                                                                    if (_4092)
                                                                                    {
                                                                                        _4086 = _4582 + ((_4189 - _4095) * sunlit);
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _4086 = _4270 * sunlit;
                                                                                    }
                                                                                    _4102 = _4086;
                                                                                }
                                                                                if (_4271)
                                                                                {
                                                                                    _4103 = _4270 < (1.5 * _4098);
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4103 = false;
                                                                                }
                                                                                bool _4291 = _4768 < ((0.5 * _4098) * clamp(_4095 / (2.0 * _4098), 0.0, 1.0));
                                                                                if (_4291)
                                                                                {
                                                                                    if (_4103)
                                                                                    {
                                                                                        _4104 = true;
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _4104 = _4768 >= (_4101 - _4257);
                                                                                    }
                                                                                    if (_4104)
                                                                                    {
                                                                                        _4105 = true;
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _4105 = _4768 >= (_4102 - (0.25 * _4098));
                                                                                    }
                                                                                    _4104 = _4105;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4104 = false;
                                                                                }
                                                                                if (_4104)
                                                                                {
                                                                                    nearMiss = 1.0;
                                                                                }
                                                                                if (!_4103)
                                                                                {
                                                                                    _4105 = _4291;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4105 = false;
                                                                                }
                                                                                if (_4105)
                                                                                {
                                                                                    _4106 = _4768 < (_4101 - _4257);
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4106 = false;
                                                                                }
                                                                                if (_4106)
                                                                                {
                                                                                    _4107 = _4768 < (_4102 - (0.25 * _4098));
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4107 = false;
                                                                                }
                                                                                if (_4107)
                                                                                {
                                                                                    t = _4095;
                                                                                    _4084 = true;
                                                                                    _4085 = true;
                                                                                    _4198_ladder_break = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4088 = _4100;
                                                                                    _4095 += max(_4768, _4098 * 0.25);
                                                                                }
                                                                            }
                                                                        }
                                                                        else
                                                                        {
                                                                            _4100 = _4088;
                                                                            _4099 = _4094;
                                                                            float _4257 = 0.25 * _4098;
                                                                            if (_4090)
                                                                            {
                                                                                _4101 = _4396 + ((_4095 - _4188) * _2232);
                                                                            }
                                                                            else
                                                                            {
                                                                                _4101 = originLift + (_4095 * _2232);
                                                                            }
                                                                            float _4270 = _1136 - _4095;
                                                                            bool _4271 = sunlit <= 0.0;
                                                                            if (_4271)
                                                                            {
                                                                                _4102 = 1000000000.0;
                                                                            }
                                                                            else
                                                                            {
                                                                                if (_4092)
                                                                                {
                                                                                    _4086 = _4582 + ((_4189 - _4095) * sunlit);
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4086 = _4270 * sunlit;
                                                                                }
                                                                                _4102 = _4086;
                                                                            }
                                                                            if (_4271)
                                                                            {
                                                                                _4103 = _4270 < (1.5 * _4098);
                                                                            }
                                                                            else
                                                                            {
                                                                                _4103 = false;
                                                                            }
                                                                            bool _4291 = _4768 < ((0.5 * _4098) * clamp(_4095 / (2.0 * _4098), 0.0, 1.0));
                                                                            if (_4291)
                                                                            {
                                                                                if (_4103)
                                                                                {
                                                                                    _4104 = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4104 = _4768 >= (_4101 - _4257);
                                                                                }
                                                                                if (_4104)
                                                                                {
                                                                                    _4105 = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4105 = _4768 >= (_4102 - (0.25 * _4098));
                                                                                }
                                                                                _4104 = _4105;
                                                                            }
                                                                            else
                                                                            {
                                                                                _4104 = false;
                                                                            }
                                                                            if (_4104)
                                                                            {
                                                                                nearMiss = 1.0;
                                                                            }
                                                                            if (!_4103)
                                                                            {
                                                                                _4105 = _4291;
                                                                            }
                                                                            else
                                                                            {
                                                                                _4105 = false;
                                                                            }
                                                                            if (_4105)
                                                                            {
                                                                                _4106 = _4768 < (_4101 - _4257);
                                                                            }
                                                                            else
                                                                            {
                                                                                _4106 = false;
                                                                            }
                                                                            if (_4106)
                                                                            {
                                                                                _4107 = _4768 < (_4102 - (0.25 * _4098));
                                                                            }
                                                                            else
                                                                            {
                                                                                _4107 = false;
                                                                            }
                                                                            if (_4107)
                                                                            {
                                                                                t = _4095;
                                                                                _4084 = true;
                                                                                _4085 = true;
                                                                                _4198_ladder_break = true;
                                                                            }
                                                                            else
                                                                            {
                                                                                _4088 = _4100;
                                                                                _4095 += max(_4768, _4098 * 0.25);
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                            if (_4198_ladder_break)
                                                            {
                                                                break;
                                                            }
                                                            _4094 = _4099;
                                                            _4096++;
                                                            continue;
                                                        }
                                                        if (!(_4084))
                                                        {
                                                            _4084 = true;
                                                            _4085 = false;
                                                        }
                                                    }
                                                }
                                            }
                                            if (!_4085)
                                            {
                                                sourceWidth = visibleSum + 1.0;
                                                blocked_1 = blocked;
                                                penumbraSum_1 = penumbraSum;
                                            }
                                            else
                                            {
                                                if (onRect)
                                                {
                                                    sourceWidth = length(p.lampRect.data[chosen[k]].xyz) + p.lampRect.data[chosen[k]].w;
                                                }
                                                else
                                                {
                                                    sourceWidth = 2.0 * _1017;
                                                }
                                                float _2890 = sourceWidth;
                                                sourceWidth = visibleSum;
                                                blocked_1 = blocked + 1.0;
                                                penumbraSum_1 = penumbraSum + ((_2890 * t) / max(_1136 - t, 0.0500000007450580596923828125));
                                            }
                                            visibleSum = sourceWidth;
                                            fieldRays += 1.0;
                                            nearMissSum += nearMiss;
                                        }
                                    }
                                }
                            }
                            if (_138_ladder_break)
                            {
                                break;
                            }
                            k++;
                            blocked = blocked_1;
                            penumbraSum = penumbraSum_1;
                            continue;
                        }
                        if (blocked > 0.0)
                        {
                            penumbra = clamp(penumbraSum / blocked, 0.00200000009499490261077880859375, 1.0);
                        }
                        else
                        {
                            penumbra = 0.00200000009499490261077880859375;
                        }
                        if (f.levels.z == 2)
                        {
                            imageStore(uVisibility, ivec2(gl_GlobalInvocationID.xy), vec4(mappedLitSum * 0.5, 0.00200000009499490261077880859375 + (fieldRays * 0.5), 0.0, 0.0));
                        }
                        else
                        {
                            if (f.levels.z != 0)
                            {
                                imageStore(uVisibility, ivec2(gl_GlobalInvocationID.xy), vec4(blocked * 0.5, max(nearMissSum * 0.5, 0.00200000009499490261077880859375), 0.0, 0.0));
                            }
                            else
                            {
                                if (rawNoise)
                                {
                                    frame = _362;
                                }
                                else
                                {
                                    frame = 0.0;
                                }
                                imageStore(uVisibility, ivec2(gl_GlobalInvocationID.xy), vec4(visibleSum * 0.5, penumbra, frame, 0.0));
                            }
                        }
                    }
                }
            }
        }
    }
}

