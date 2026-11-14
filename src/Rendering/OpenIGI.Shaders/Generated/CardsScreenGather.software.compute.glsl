#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

struct _Array_std140_vector_float_4_4
{
    vec4 data[4];
};

struct _Array_std140_vector_float_4_7
{
    vec4 data[7];
};

struct _Array_std140_float4
{
    float data[4];
};

float _8165;

layout(binding = 0, std140) uniform SLANG_ParameterGroup_Globals_std140
{
    _Array_std140_vector_float_4_4 uClipmapOrigin;
    _Array_std140_vector_float_4_7 uSkySh;
    vec4 uUnproject;
    vec3 uViewToWorld0;
    vec3 uViewToWorld1;
    vec3 uViewToWorld2;
    vec3 uCameraToCache;
    vec3 uTraceOrigin;
    vec3 uAtlasSize;
    vec3 uLookupMin;
    vec3 uSkyColour;
    vec3 uGroundSun;
    _Array_std140_float4 uClipmapRange;
    int uShadowLevels;
    int uShadowResolution;
    int uLookupBuckets;
    float uLookupCell;
    int uScreenWidth;
    int uScreenHeight;
    int uProbeStep;
    float uMaxRayIntensity;
    int uScreenTraceSteps;
    float uScreenTraceDistance;
    float uScreenThickness;
    int uSkyShValid;
    float uBounceScale;
    int uDebugSource;
    int uFrame;
    vec3 uColourWorldToView0;
    vec3 uColourWorldToView1;
    vec3 uColourWorldToView2;
    vec3 uColourCameraToCache;
    vec4 uTerrainMap0;
    vec4 uTerrainMap1;
    vec4 uShelterMap0;
    vec4 uShelterMap1;
    vec4 uShelterMap2;
} Globals;

layout(binding = 1, std430) restrict buffer RWStructuredBuffer
{
    ivec2 _member0[];
} uCells;

layout(binding = 2, std430) restrict buffer uCellPages
{
    int _member0[];
} uCellPages_1;

layout(binding = 0, std430) restrict readonly buffer StructuredBuffer
{
    vec4 _member0[];
} uPages;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uIndirect;
layout(binding = 5) uniform sampler2D _8170;
layout(binding = 6) uniform sampler2D _8172;
layout(binding = 7) uniform sampler2D _8174;
layout(binding = 3) uniform sampler3D _8176;
layout(binding = 8) uniform sampler2D _8179;
layout(binding = 0) uniform sampler2D _8182;
layout(binding = 1) uniform sampler2D _8184;
layout(binding = 2) uniform sampler2D _8186;
layout(binding = 4) uniform sampler2D _8188;
layout(binding = 9) uniform sampler2D _8191;

void main()
{
    bool _6312 = false;
    bool _6214 = false;
    bool _6116 = false;
    bool _6018 = false;
    bool _5920 = false;
    bool _5822 = false;
    bool _5724 = false;
    bool _4751 = false;
    bool _4641 = false;
    bool _4560 = false;
    {
        ivec2 _24 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 grid = (ivec2(Globals.uScreenWidth, Globals.uScreenHeight) + ivec2(Globals.uProbeStep - 1)) / ivec2(Globals.uProbeStep);
        bool _7;
        if (_24.x >= grid.x)
        {
            _7 = true;
        }
        else
        {
            _7 = _24.y >= grid.y;
        }
        if (!(_7))
        {
            vec2 uv = (vec2(min(((_24 * ivec2(Globals.uProbeStep)) + ivec2(Globals.uProbeStep / 2)), (ivec2(Globals.uScreenWidth, Globals.uScreenHeight) - ivec2(1)))) + vec2(0.5)) / vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight));
            vec4 _3521 = textureLod(_8170, uv, 0.0);
            float _3522 = _3521.x;
            if (_3522 <= 0.0)
            {
                imageStore(uIndirect, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
            }
            else
            {
                float _3541 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * _3522)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                vec2 _3544 = (uv * 2.0) - vec2(1.0);
                vec3 _3556 = vec3((_3544.x * _3541) / Globals.uUnproject.x, ((-1.0) * (_3544.y * _3541)) / Globals.uUnproject.y, _3541);
                vec3 world = ((Globals.uCameraToCache + (Globals.uViewToWorld0 * _3556.x)) + (Globals.uViewToWorld1 * _3556.y)) + (Globals.uViewToWorld2 * _3556.z);
                vec3 _209 = normalize((textureLod(_8172, uv, 0.0).xyz * 2.0) - vec3(1.0));
                vec3 _3558;
                {
                    vec3 _3564 = -normalize(world - Globals.uCameraToCache);
                    float _3565 = dot(_209, _3564);
                    if (_3565 >= 0.0)
                    {
                        _3558 = _209;
                    }
                    else
                    {
                        vec3 _3602;
                        {
                            vec2 _3616 = vec2(1.0) / vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight));
                            vec2 _3618 = vec2(_3616.x, 0.0);
                            vec2 _3619 = uv + _3618;
                            float _3701 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_8170, _3619, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                            vec2 _3704 = (_3619 * 2.0) - vec2(1.0);
                            vec3 _3716 = vec3((_3704.x * _3701) / Globals.uUnproject.x, ((-1.0) * (_3704.y * _3701)) / Globals.uUnproject.y, _3701);
                            vec2 _3623 = uv - _3618;
                            float _3738 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_8170, _3623, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                            vec2 _3741 = (_3623 * 2.0) - vec2(1.0);
                            vec3 _3753 = vec3((_3741.x * _3738) / Globals.uUnproject.x, ((-1.0) * (_3741.y * _3738)) / Globals.uUnproject.y, _3738);
                            vec2 _3628 = vec2(0.0, _3616.y);
                            vec2 _3629 = uv + _3628;
                            float _3775 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_8170, _3629, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                            vec2 _3778 = (_3629 * 2.0) - vec2(1.0);
                            vec3 _3790 = vec3((_3778.x * _3775) / Globals.uUnproject.x, ((-1.0) * (_3778.y * _3775)) / Globals.uUnproject.y, _3775);
                            vec2 _3633 = uv - _3628;
                            float _3812 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_8170, _3633, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                            vec2 _3815 = (_3633 * 2.0) - vec2(1.0);
                            vec3 _3827 = vec3((_3815.x * _3812) / Globals.uUnproject.x, ((-1.0) * (_3815.y * _3812)) / Globals.uUnproject.y, _3812);
                            float _3638 = _3556.z;
                            vec3 _3603;
                            if (abs(_3716.z - _3638) < abs(_3638 - _3753.z))
                            {
                                _3603 = _3716 - _3556;
                            }
                            else
                            {
                                _3603 = _3556 - _3753;
                            }
                            vec3 _3604;
                            if (abs(_3790.z - _3638) < abs(_3638 - _3827.z))
                            {
                                _3604 = _3790 - _3556;
                            }
                            else
                            {
                                _3604 = _3556 - _3827;
                            }
                            vec3 _3662 = _3603;
                            vec3 _3664 = cross(_3662, _3604);
                            float _3665 = dot(_3664, _3664);
                            if (_3665 <= 9.9999996826552253889678874634872e-21)
                            {
                                _3602 = vec3(0.0);
                            }
                            else
                            {
                                vec3 _3670 = _3664 * inversesqrt(_3665);
                                if (dot(_3670, _3556) > 0.0)
                                {
                                    _3603 = -_3670;
                                }
                                else
                                {
                                    _3603 = _3670;
                                }
                                _3602 = _3603;
                            }
                        }
                        vec3 _3583 = ((Globals.uViewToWorld0 * _3602.x) + (Globals.uViewToWorld1 * _3602.y)) + (Globals.uViewToWorld2 * _3602.z);
                        bool _3559;
                        if (dot(_3583, _3583) < 0.5)
                        {
                            _3559 = true;
                        }
                        else
                        {
                            _3559 = dot(_209, _3583) <= 0.0;
                        }
                        if (_3559)
                        {
                            _3558 = -_209;
                        }
                        else
                        {
                            _3558 = normalize(_209 - (_3564 * (2.0 * _3565)));
                        }
                    }
                }
                vec3 _3829;
                {
                    vec3 _8037;
                    vec3 _8038;
                    vec3 _8039;
                    {
                        vec3 _8052 = _3558;
                        if (_3558.z < (-0.99999988079071044921875))
                        {
                            _8037 = vec3(0.0, -1.0, 0.0);
                            _8038 = vec3(-1.0, 0.0, 0.0);
                            _8039 = _8052;
                        }
                        else
                        {
                            float _3899 = 1.0 / (1.0 + _3558.z);
                            float _3901 = -_3558.x;
                            float _3904 = (_3901 * _3558.y) * _3899;
                            _8037 = vec3(1.0 - ((_3558.x * _3558.x) * _3899), _3904, _3901);
                            _8038 = vec3(_3904, 1.0 - ((_3558.y * _3558.y) * _3899), -_3558.y);
                            _8039 = _8052;
                        }
                    }
                    vec3 _8138 = _8037;
                    vec3 _8139 = _8038;
                    vec3 _8140 = _8039;
                    vec3 _8072 = vec3(dot(Globals.uViewToWorld0, _3558), dot(Globals.uViewToWorld1, _3558), dot(Globals.uViewToWorld2, _3558));
                    vec3 _8073 = world;
                    bool _3866;
                    if (Globals.uShadowLevels > 0)
                    {
                        _3866 = Globals.uLookupBuckets > 0;
                    }
                    else
                    {
                        _3866 = false;
                    }
                    if (!_3866)
                    {
                        _3829 = vec3(0.0);
                    }
                    else
                    {
                        vec3 _8078 = vec3(0.0);
                        float _8079 = 0.0;
                        vec3 _8080 = vec3(0.0);
                        float _8081 = 0.0;
                        vec3 _8082 = vec3(0.0);
                        float _8083 = 0.0;
                        float _8084 = 0.0;
                        int _3832 = 0;
                        int _3944;
                        vec3 _3945;
                        bool _3946;
                        vec3 _3947;
                        float _3948;
                        vec3 _3949;
                        bool _3950;
                        vec3 _3951;
                        vec2 _3953;
                        float _3955;
                        vec3 _3956;
                        vec3 _3957;
                        bool _4210;
                        bool _4211;
                        vec3 _4212;
                        float _4215;
                        bool _4216;
                        vec3 _4542;
                        float _4561;
                        bool _4564;
                        bool _4565;
                        float _4567;
                        float _4642;
                        bool _4644;
                        float _4752;
                        bool _4753;
                        float _4871;
                        bool _4896;
                        float _4934;
                        float _4959;
                        bool _4984;
                        float _5022;
                        bool _5052;
                        ivec2 _5057;
                        bool _5068;
                        bool _5069;
                        vec3 _5071[4];
                        float _5074;
                        ivec2 _5439;
                        vec3 _5536;
                        vec3 _5555;
                        vec3 _5607;
                        vec3 _5658;
                        vec3 _5677;
                        bool _5678;
                        float _5680;
                        vec3 _5684;
                        bool _5727;
                        float _5823;
                        bool _5825;
                        float _5921;
                        bool _5923;
                        float _6019;
                        bool _6021;
                        float _6117;
                        bool _6119;
                        float _6215;
                        bool _6217;
                        float _6313;
                        bool _6315;
                        bool _6411;
                        bool _6477;
                        float _6515;
                        bool _6540;
                        bool _6556;
                        bool _6622;
                        float _6660;
                        bool _6685;
                        bool _6705;
                        ivec2 _6710;
                        bool _6721;
                        bool _6722;
                        vec3 _6724[4];
                        float _6727;
                        ivec2 _7092;
                        bool _7194;
                        bool _7200;
                        ivec2 _7201;
                        bool _7205;
                        bool _7214;
                        bool _7215;
                        bool _7216;
                        bool _7217;
                        bool _7218;
                        ivec2 _7448;
                        vec3 _7545;
                        vec3 _7563;
                        vec3 _7581;
                        for (;;)
                        {
                            if (!(_3832 < 16))
                            {
                                break;
                            }
                            vec3 _7949 = vec3(0.0);
                            float _7950 = 0.0;
                            vec3 _7951 = vec3(0.0);
                            float _7952 = 0.0;
                            bool _7953 = false;
                            bool _7954 = false;
                            bool _7955 = false;
                            vec2 _3981 = (vec2(float(_3832 - 4 * (_3832 / 4)), float(_3832 / 4)) + vec2(0.5)) * vec2(0.25);
                            float _3983 = 6.283185482025146484375 * _3981.x;
                            float _3985 = sqrt(_3981.y);
                            float _3989 = sqrt(max(0.0, 1.0 - (_3985 * _3985)));
                            vec3 _4196 = ((_8138 * (_3989 * cos(_3983))) + (_8139 * (_3989 * sin(_3983)))) + (_8140 * _3985);
                            if (Globals.uScreenTraceSteps > 0)
                            {
                                vec3 _4208 = vec3(dot(Globals.uViewToWorld0, _4196), dot(Globals.uViewToWorld1, _4196), dot(Globals.uViewToWorld2, _4196));
                                {
                                    _3947 = vec3(0.0);
                                    if (Globals.uScreenTraceSteps <= 0)
                                    {
                                        _4211 = true;
                                    }
                                    else
                                    {
                                        _4211 = _4208.z == 0.0;
                                    }
                                    if (_4211)
                                    {
                                        _4210 = false;
                                    }
                                    else
                                    {
                                        vec3 _4234 = _3556 + (_8072 * 0.0199999995529651641845703125);
                                        vec3 _4238 = _4234 + (_4208 * Globals.uScreenTraceDistance);
                                        if (_4238.z < (Globals.uUnproject.z + 0.00999999977648258209228515625))
                                        {
                                            _4212 = _4234 + (_4208 * (((Globals.uUnproject.z + 0.00999999977648258209228515625) - _4234.z) / _4208.z));
                                        }
                                        else
                                        {
                                            _4212 = _4238;
                                        }
                                        if (_4212.z <= Globals.uUnproject.z)
                                        {
                                            _4210 = false;
                                        }
                                        else
                                        {
                                            float _4425 = _4234.z;
                                            vec2 _4436 = (vec2((_4234.x * Globals.uUnproject.x) / _4425, ((-1.0) * (_4234.y * Globals.uUnproject.y)) / _4425) * 0.5) + vec2(0.5);
                                            vec2 _4267 = ((vec2((_4212.x * Globals.uUnproject.x) / _4212.z, ((-1.0) * (_4212.y * Globals.uUnproject.y)) / _4212.z) * 0.5) + vec2(0.5)) - _4436;
                                            int _4282 = int(clamp(length(_4267 / (vec2(1.0) / vec2(float(Globals.uScreenWidth), float(Globals.uScreenHeight)))), 1.0, float(Globals.uScreenTraceSteps)));
                                            float _4284 = 1.0 / float(_4282);
                                            float _4288 = fract(sin(dot(_4436, vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875);
                                            float _4213 = 0.0;
                                            int _4214 = 1;
                                            for (;;)
                                            {
                                                if (!(_4214 <= 256))
                                                {
                                                    _4215 = -1.0;
                                                    break;
                                                }
                                                if (_4214 > _4282)
                                                {
                                                    _4215 = -1.0;
                                                    break;
                                                }
                                                float _4305 = _4284 * (float(_4214) + _4288);
                                                if (_4305 > 1.0)
                                                {
                                                    _4215 = -1.0;
                                                    break;
                                                }
                                                vec2 _4310 = _4436 + (_4267 * _4305);
                                                if (any(lessThan(_4310, vec2(0.0))))
                                                {
                                                    _4211 = true;
                                                }
                                                else
                                                {
                                                    _4211 = any(greaterThan(_4310, vec2(1.0)));
                                                }
                                                if (_4211)
                                                {
                                                    _4215 = -1.0;
                                                    break;
                                                }
                                                float _4321 = _4234.z;
                                                float _4477 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_8170, _4310, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                                vec2 _4480 = (_4310 * 2.0) - vec2(1.0);
                                                vec3 _4492 = vec3(_8165, _8165, _4477);
                                                float _4329 = _4492.z;
                                                float _4330 = (1.0 / mix(1.0 / _4321, 1.0 / _4212.z, _4305)) - _4329;
                                                if (_4330 > 0.0)
                                                {
                                                    _4216 = _4330 < (Globals.uScreenThickness + (_4329 * 0.0199999995529651641845703125));
                                                }
                                                else
                                                {
                                                    _4216 = false;
                                                }
                                                if (_4216)
                                                {
                                                    _4215 = _4213;
                                                    float _4217 = _4305;
                                                    int _4218 = 0;
                                                    for (;;)
                                                    {
                                                        if (!(_4218 < 4))
                                                        {
                                                            break;
                                                        }
                                                        float _4352 = _4217;
                                                        float _4354 = (_4215 + _4352) * 0.5;
                                                        vec2 _4360 = _4436 + (_4267 * _4354);
                                                        float _4514 = ((2.0 * Globals.uUnproject.z) * Globals.uUnproject.w) / ((Globals.uUnproject.w + Globals.uUnproject.z) - ((1.0 - (2.0 * textureLod(_8170, _4360, 0.0).x)) * (Globals.uUnproject.w - Globals.uUnproject.z)));
                                                        vec2 _4517 = (_4360 * 2.0) - vec2(1.0);
                                                        if (((1.0 / mix(1.0 / _4321, 1.0 / _4212.z, _4354)) - vec3(_8165, _8165, _4514).z) > 0.0)
                                                        {
                                                            _4217 = _4354;
                                                        }
                                                        else
                                                        {
                                                            _4215 = _4354;
                                                        }
                                                        _4218++;
                                                        continue;
                                                    }
                                                    _4215 = _4217;
                                                    break;
                                                }
                                                _4213 = _4305;
                                                _4214++;
                                                continue;
                                            }
                                            if (_4215 <= 0.0)
                                            {
                                                _4210 = false;
                                            }
                                            else
                                            {
                                                vec2 _4390 = _4436 + (_4267 * _4215);
                                                vec3 _4399 = (textureLod(_8172, _4390, 0.0).xyz * 2.0) - vec3(1.0);
                                                if (dot(vec3(dot(Globals.uViewToWorld0, _4399), dot(Globals.uViewToWorld1, _4399), dot(Globals.uViewToWorld2, _4399)), _4208) >= 0.0)
                                                {
                                                    _4210 = false;
                                                }
                                                else
                                                {
                                                    vec4 _4408 = textureLod(_8174, _4390, 0.0);
                                                    if (_4408.w < 0.5)
                                                    {
                                                        _4210 = false;
                                                    }
                                                    else
                                                    {
                                                        vec3 _4414 = _4408.xyz;
                                                        float _4549 = max(_4408.x, max(_4408.y, _4408.z));
                                                        if (_4549 > Globals.uMaxRayIntensity)
                                                        {
                                                            _4542 = _4414 * (Globals.uMaxRayIntensity / _4549);
                                                        }
                                                        else
                                                        {
                                                            _4542 = _4414;
                                                        }
                                                        _3947 = _4542;
                                                        _4210 = true;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                if (_4210)
                                {
                                    _3944 = 1;
                                    _3945 = _3947;
                                }
                                else
                                {
                                    _3944 = 0;
                                    _3945 = vec3(0.0);
                                }
                                _3946 = _4210;
                            }
                            else
                            {
                                _3946 = false;
                                _3944 = 0;
                                _3945 = vec3(0.0);
                            }
                            if (!_3946)
                            {
                                _4560 = false;
                                {
                                    _3949 = _8073;
                                    if (Globals.uShadowLevels <= 0)
                                    {
                                        _4560 = true;
                                        _4561 = 1.0;
                                    }
                                    else
                                    {
                                        vec3 _4584 = _8073 + ((_3558 * Globals.uClipmapOrigin.data[0].w) * 1.5);
                                        float _4595 = (Globals.uClipmapOrigin.data[0].w * float(Globals.uShadowResolution)) * float(1 << (Globals.uShadowLevels - 1));
                                        int _4562 = 0;
                                        float _4563 = 0.0;
                                        for (;;)
                                        {
                                            if (_4562 < 64)
                                            {
                                                _4564 = _4563 < _4595;
                                            }
                                            else
                                            {
                                                _4564 = false;
                                            }
                                            if (!_4564)
                                            {
                                                break;
                                            }
                                            vec3 _4612 = _4584 + (_4196 * _4563);
                                            _4641 = false;
                                            {
                                                _4565 = false;
                                                _4567 = Globals.uClipmapOrigin.data[0].w;
                                                int _4643 = 0;
                                                for (;;)
                                                {
                                                    bool _4655_ladder_break = false;
                                                    {
                                                        if (!(_4643 < Globals.uShadowLevels))
                                                        {
                                                            _4655_ladder_break = true;
                                                        }
                                                        else
                                                        {
                                                            vec3 _4670 = (_4612 - Globals.uClipmapOrigin.data[_4643].xyz) / vec3(Globals.uClipmapOrigin.data[_4643].w);
                                                            if (any(lessThan(_4670, vec3(0.5))))
                                                            {
                                                                _4644 = true;
                                                            }
                                                            else
                                                            {
                                                                _4644 = any(greaterThan(_4670, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                            }
                                                            if (!(_4644))
                                                            {
                                                                _4565 = true;
                                                                _4567 = Globals.uClipmapOrigin.data[_4643].w;
                                                                _4641 = true;
                                                                _4642 = ((textureLod(_8176, vec3(_4670.x / float(Globals.uShadowResolution), _4670.y / float(Globals.uShadowResolution), ((float(_4643) * float(Globals.uShadowResolution)) + _4670.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_4643];
                                                                _4655_ladder_break = true;
                                                            }
                                                        }
                                                    }
                                                    if (_4655_ladder_break)
                                                    {
                                                        break;
                                                    }
                                                    _4643++;
                                                    continue;
                                                }
                                                if (!(_4641))
                                                {
                                                    _4641 = true;
                                                    _4642 = 1000000.0;
                                                }
                                            }
                                            if (!_4565)
                                            {
                                                _4560 = true;
                                                _4561 = 1.0;
                                                break;
                                            }
                                            float _4746 = (0.5 * _4567) * clamp(_4563 / max(2.0 * _4567, 9.9999999747524270787835121154785e-07), 0.0, 1.0);
                                            float _4568 = max((_4563 + _4642) - _4746, 0.0);
                                            if (_4642 < _4746)
                                            {
                                                _3949 = _4584 + (_4196 * _4568);
                                                _4560 = true;
                                                _4561 = 0.0;
                                                break;
                                            }
                                            _4562++;
                                            _4563 += max(_4642, _4567 * 0.25);
                                            continue;
                                        }
                                        if (!(_4560))
                                        {
                                            _4560 = true;
                                            _4561 = 1.0;
                                        }
                                    }
                                }
                                bool _4014 = _4561 <= 0.5;
                                vec3 _4016 = world + (_3558 * 0.100000001490116119384765625);
                                vec3 _4019 = _4016 + Globals.uTraceOrigin;
                                _4751 = false;
                                {
                                    if (Globals.uTerrainMap0.w < 0.5)
                                    {
                                        _4753 = true;
                                    }
                                    else
                                    {
                                        _4753 = _4196.z > (-0.00999999977648258209228515625);
                                    }
                                    if (_4753)
                                    {
                                        _4753 = true;
                                    }
                                    else
                                    {
                                        _4753 = Globals.uTerrainMap0.z <= 0.0;
                                    }
                                    if (_4753)
                                    {
                                        _4751 = true;
                                        _4752 = -1.0;
                                    }
                                    else
                                    {
                                        float _4782 = 0.5 / Globals.uTerrainMap0.z;
                                        float _4754 = 0.0;
                                        int _4755 = 1;
                                        for (;;)
                                        {
                                            if (!(_4755 <= 64))
                                            {
                                                break;
                                            }
                                            float _4794 = min(float(_4755) * _4782, 32.0);
                                            vec3 _4796 = _4019 + (_4196 * _4794);
                                            float _7925 = Globals.uTerrainMap1.z;
                                            float _7926 = Globals.uTerrainMap1.w;
                                            vec2 _7927 = Globals.uTerrainMap1.xy;
                                            ivec2 _4895 = ivec2(floor((_4796.xy - Globals.uTerrainMap0.xy) * Globals.uTerrainMap0.z));
                                            if (Globals.uTerrainMap0.w > 0.5)
                                            {
                                                _4896 = _4895.x >= 0;
                                            }
                                            else
                                            {
                                                _4896 = false;
                                            }
                                            if (_4896)
                                            {
                                                _4896 = _4895.y >= 0;
                                            }
                                            else
                                            {
                                                _4896 = false;
                                            }
                                            if (_4896)
                                            {
                                                _4896 = _4895.x < int(_7927.x);
                                            }
                                            else
                                            {
                                                _4896 = false;
                                            }
                                            if (_4896)
                                            {
                                                _4896 = _4895.y < int(_7927.y);
                                            }
                                            else
                                            {
                                                _4896 = false;
                                            }
                                            if (_4896)
                                            {
                                                vec4 _4881 = texelFetch(_8179, ivec3(_4895, 0).xy, 0);
                                                float _4946 = floor((_4881.z * 255.0) + 0.5) + (floor((_4881.y * 255.0) + 0.5) * 256.0);
                                                if (_4946 < 0.5)
                                                {
                                                    _4934 = -1000000015047466219876688855040.0;
                                                }
                                                else
                                                {
                                                    _4934 = _7925 + ((_4946 - 1.0) * _7926);
                                                }
                                                _4871 = _4934;
                                            }
                                            else
                                            {
                                                _4871 = -1000000015047466219876688855040.0;
                                            }
                                            if (_4796.z <= _4871)
                                            {
                                                float _4757 = _4754;
                                                float _4758 = _4794;
                                                int _4759 = 0;
                                                for (;;)
                                                {
                                                    if (!(_4759 < 4))
                                                    {
                                                        break;
                                                    }
                                                    float _4812 = _4758;
                                                    float _4814 = (_4757 + _4812) * 0.5;
                                                    vec3 _4816 = _4019 + (_4196 * _4814);
                                                    float _7937 = Globals.uTerrainMap1.z;
                                                    float _7938 = Globals.uTerrainMap1.w;
                                                    vec2 _7939 = Globals.uTerrainMap1.xy;
                                                    ivec2 _4983 = ivec2(floor((_4816.xy - Globals.uTerrainMap0.xy) * Globals.uTerrainMap0.z));
                                                    if (Globals.uTerrainMap0.w > 0.5)
                                                    {
                                                        _4984 = _4983.x >= 0;
                                                    }
                                                    else
                                                    {
                                                        _4984 = false;
                                                    }
                                                    if (_4984)
                                                    {
                                                        _4984 = _4983.y >= 0;
                                                    }
                                                    else
                                                    {
                                                        _4984 = false;
                                                    }
                                                    if (_4984)
                                                    {
                                                        _4984 = _4983.x < int(_7939.x);
                                                    }
                                                    else
                                                    {
                                                        _4984 = false;
                                                    }
                                                    if (_4984)
                                                    {
                                                        _4984 = _4983.y < int(_7939.y);
                                                    }
                                                    else
                                                    {
                                                        _4984 = false;
                                                    }
                                                    if (_4984)
                                                    {
                                                        vec4 _4969 = texelFetch(_8179, ivec3(_4983, 0).xy, 0);
                                                        float _5034 = floor((_4969.z * 255.0) + 0.5) + (floor((_4969.y * 255.0) + 0.5) * 256.0);
                                                        if (_5034 < 0.5)
                                                        {
                                                            _5022 = -1000000015047466219876688855040.0;
                                                        }
                                                        else
                                                        {
                                                            _5022 = _7937 + ((_5034 - 1.0) * _7938);
                                                        }
                                                        _4959 = _5022;
                                                    }
                                                    else
                                                    {
                                                        _4959 = -1000000015047466219876688855040.0;
                                                    }
                                                    if (_4816.z <= _4959)
                                                    {
                                                        _4758 = _4814;
                                                    }
                                                    else
                                                    {
                                                        _4757 = _4814;
                                                    }
                                                    _4759++;
                                                    continue;
                                                }
                                                _4751 = true;
                                                _4752 = _4758;
                                                break;
                                            }
                                            if (_4794 >= 32.0)
                                            {
                                                break;
                                            }
                                            _4754 = _4794;
                                            _4755++;
                                            continue;
                                        }
                                        if (!(_4751))
                                        {
                                            _4751 = true;
                                            _4752 = -1.0;
                                        }
                                    }
                                }
                                if (_4752 >= 0.0)
                                {
                                    if (!_4014)
                                    {
                                        _3950 = true;
                                    }
                                    else
                                    {
                                        _3950 = length(_3949 - world) > _4752;
                                    }
                                }
                                else
                                {
                                    _3950 = false;
                                }
                                if (_3950)
                                {
                                    vec3 _4037 = _4016 + (_4196 * _4752);
                                    float _4043 = 0.5 * Globals.uClipmapOrigin.data[0].w;
                                    {
                                        _3951 = vec3(0.0);
                                        if (Globals.uLookupBuckets <= 0)
                                        {
                                            _5052 = false;
                                        }
                                        else
                                        {
                                            ivec3 _5091 = ivec3(floor((_4037 - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                                            int _5438 = int((((uint(_5091.x) * 2376512323u) ^ (uint(_5091.y) * 3625334849u)) ^ (uint(_5091.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                                            uvec2 _5096 = uvec2(textureSize(_8182, 0));
                                            vec2 _5103 = vec2(float(_5096.x), float(_5096.y));
                                            int _5055 = 0;
                                            float _5056 = 0.0;
                                            for (;;)
                                            {
                                                if (!(_5055 <= uCells._member0[Globals.uLookupBuckets].y))
                                                {
                                                    break;
                                                }
                                                bool _5117 = _5055 == 0;
                                                if (_5117)
                                                {
                                                    _5057 = uCells._member0[_5438];
                                                }
                                                else
                                                {
                                                    int _5443 = uCells._member0[Globals.uLookupBuckets].x + ((_5055 - 1) * 6);
                                                    float _5460 = intBitsToFloat(uCellPages_1._member0[_5443 + 3]) + _4043;
                                                    vec3 _5461 = _4037 - vec3(intBitsToFloat(uCellPages_1._member0[_5443]), intBitsToFloat(uCellPages_1._member0[_5443 + 1]), intBitsToFloat(uCellPages_1._member0[_5443 + 2]));
                                                    if (dot(_5461, _5461) > (_5460 * _5460))
                                                    {
                                                        _5439 = ivec2(0);
                                                    }
                                                    else
                                                    {
                                                        _5439 = ivec2(uCellPages_1._member0[_5443 + 4], uCellPages_1._member0[_5443 + 5]);
                                                    }
                                                    _5057 = _5439;
                                                }
                                                int _5058 = 0;
                                                float _5059 = _5056;
                                                for (;;)
                                                {
                                                    bool _5128_ladder_break = false;
                                                    {
                                                        if (!(_5058 < _5057.y))
                                                        {
                                                            _5128_ladder_break = true;
                                                        }
                                                        else
                                                        {
                                                            int _5139 = _5057.x + _5058;
                                                            int _5142 = uCellPages_1._member0[_5139] * 10;
                                                            if (!((uPages._member0[_5142 + 5].w > 0.5) == _5117))
                                                            {
                                                                int _5151 = _5142 + 9;
                                                                vec3 _5155 = _4037 - uPages._member0[_5151].xyz;
                                                                float _5157 = uPages._member0[_5151].w + _4043;
                                                                if (!(dot(_5155, _5155) > (_5157 * _5157)))
                                                                {
                                                                    int _5477 = uCellPages_1._member0[_5139] * 10;
                                                                    vec4 _5066 = uPages._member0[_5477];
                                                                    vec4 _5067 = uPages._member0[_5477 + 1];
                                                                    int _5483 = _5477 + 2;
                                                                    int _5486 = _5477 + 3;
                                                                    int _5489 = _5477 + 4;
                                                                    int _5495 = _5477 + 6;
                                                                    int _5498 = _5477 + 7;
                                                                    int _5501 = _5477 + 8;
                                                                    vec3 _5508 = _4037 - vec3(uPages._member0[_5495].w, uPages._member0[_5498].w, uPages._member0[_5501].w);
                                                                    vec3 _5521 = vec3(dot(uPages._member0[_5495].xyz, _5508), dot(uPages._member0[_5498].xyz, _5508), dot(uPages._member0[_5501].xyz, _5508)) - uPages._member0[_5483].xyz;
                                                                    vec2 _5525 = max(vec2(uPages._member0[_5483].w, uPages._member0[_5486].w), vec2(9.9999999747524270787835121154785e-07));
                                                                    vec2 _5061 = _5525;
                                                                    vec2 _5060 = vec2(dot(_5521, uPages._member0[_5486].xyz), dot(_5521, uPages._member0[_5489].xyz)) / _5525;
                                                                    float _5062 = -dot(_5521, uPages._member0[_5477 + 5].xyz);
                                                                    float _5063 = uPages._member0[_5489].w;
                                                                    float _5166 = dot(uPages._member0[_5477 + 5].xyz, vec3(uPages._member0[_5495].z, uPages._member0[_5498].z, uPages._member0[_5501].z));
                                                                    if (!(_5166 < 0.100000001490116119384765625))
                                                                    {
                                                                        float _5171 = Globals.uClipmapOrigin.data[0].w * 0.25;
                                                                        vec2 _5174 = vec2(_5171) / _5061;
                                                                        if (abs(_5060.x) > (1.0 + _5174.x))
                                                                        {
                                                                            _5068 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _5068 = abs(_5060.y) > (1.0 + _5174.y);
                                                                        }
                                                                        if (!(_5068))
                                                                        {
                                                                            if (_5062 < (Globals.uClipmapOrigin.data[0].w * (-0.25)))
                                                                            {
                                                                                _5069 = true;
                                                                            }
                                                                            else
                                                                            {
                                                                                _5069 = _5062 > (_5063 + _5171);
                                                                            }
                                                                            if (!(_5069))
                                                                            {
                                                                                vec2 _5218 = (_5067.xy + (((clamp(_5060, vec2(-1.0), vec2(1.0)) * 0.5) + vec2(0.5)) * _5067.zw)) - vec2(0.5);
                                                                                vec2 _5219 = floor(_5218);
                                                                                vec2 _5220 = _5218 - _5219;
                                                                                vec2 _5226 = _5066.zw / max(_5067.zw, vec2(9.9999999747524270787835121154785e-07));
                                                                                float _5230 = _5062 / max(_5063, 9.9999999747524270787835121154785e-07);
                                                                                float _5234 = _4043 / max(0.5 * _5063, 9.9999999747524270787835121154785e-07);
                                                                                vec4 _5070 = vec4(0.0);
                                                                                int _5073 = 0;
                                                                                for (;;)
                                                                                {
                                                                                    if (!(_5073 < 4))
                                                                                    {
                                                                                        break;
                                                                                    }
                                                                                    vec2 _5249 = vec2(float(_5073 & 1), float(_5073 >> 1));
                                                                                    vec2 _5252 = (_5219 + _5249) + vec2(0.5);
                                                                                    vec2 _5253 = _5252 / _5103;
                                                                                    if (textureLod(_8184, _5253, 0.0).w >= 0.5)
                                                                                    {
                                                                                        _5074 = 1.0;
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _5074 = 0.0;
                                                                                    }
                                                                                    vec2 _5282 = mix(vec2(1.0) - _5220, _5220, _5249);
                                                                                    _5070[_5073] = ((_5282.x * _5282.y) * _5074) * (1.0 - clamp((abs(_5230 - textureLod(_8186, _5253, 0.0).x) - _5234) / (0.25 * _5234), 0.0, 1.0));
                                                                                    _5071[_5073] = textureLod(_8188, (floor(_5066.xy + ((_5252 - _5067.xy) * _5226)) + vec2(0.5)) / Globals.uAtlasSize.xy, 0.0).xyz;
                                                                                    _5073++;
                                                                                    continue;
                                                                                }
                                                                                float _5329 = ((_5070.x + _5070.y) + _5070.z) + _5070.w;
                                                                                float _5330 = (_5166 * _5166) * _5329;
                                                                                if (!(_5330 <= 0.0))
                                                                                {
                                                                                    _3951 += (max(((((_5071[0] * _5070.x) + (_5071[1] * _5070.y)) + (_5071[2] * _5070.z)) + (_5071[3] * _5070.w)) / vec3(_5329), vec3(0.0)) * _5330);
                                                                                    _5059 += _5330;
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                    if (_5128_ladder_break)
                                                    {
                                                        break;
                                                    }
                                                    _5058++;
                                                    continue;
                                                }
                                                _5055++;
                                                _5056 = _5059;
                                                continue;
                                            }
                                            if (_5056 <= 0.0)
                                            {
                                                _3951 = vec3(0.0);
                                                _5052 = false;
                                            }
                                            else
                                            {
                                                _3951 /= vec3(_5056);
                                                _5052 = true;
                                            }
                                        }
                                    }
                                    if (_5052)
                                    {
                                        vec3 _4049 = _3951 * Globals.uBounceScale;
                                        float _5543 = max(_4049.x, max(_4049.y, _4049.z));
                                        if (_5543 > Globals.uMaxRayIntensity)
                                        {
                                            _5536 = _4049 * (Globals.uMaxRayIntensity / _5543);
                                        }
                                        else
                                        {
                                            _5536 = _4049;
                                        }
                                        _7949 = _5536;
                                        _7950 = 1.0;
                                        _7953 = true;
                                        _3946 = true;
                                        _3944 = 2;
                                        _3945 = _4049;
                                    }
                                    _3950 = false;
                                }
                                else
                                {
                                    _3950 = _4014;
                                }
                                if (_3946)
                                {
                                    _3948 = 1.0;
                                }
                                else
                                {
                                    if (_3950)
                                    {
                                        float _4076 = 1.5 * Globals.uClipmapOrigin.data[0].w;
                                        {
                                            _5724 = false;
                                            {
                                                _5678 = false;
                                                _5680 = Globals.uClipmapOrigin.data[0].w;
                                                int _5726 = 0;
                                                for (;;)
                                                {
                                                    bool _5738_ladder_break = false;
                                                    {
                                                        if (!(_5726 < Globals.uShadowLevels))
                                                        {
                                                            _5738_ladder_break = true;
                                                        }
                                                        else
                                                        {
                                                            vec3 _5753 = (_3949 - Globals.uClipmapOrigin.data[_5726].xyz) / vec3(Globals.uClipmapOrigin.data[_5726].w);
                                                            if (any(lessThan(_5753, vec3(0.5))))
                                                            {
                                                                _5727 = true;
                                                            }
                                                            else
                                                            {
                                                                _5727 = any(greaterThan(_5753, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                            }
                                                            if (!(_5727))
                                                            {
                                                                _5678 = true;
                                                                _5680 = Globals.uClipmapOrigin.data[_5726].w;
                                                                _5724 = true;
                                                                _5738_ladder_break = true;
                                                            }
                                                        }
                                                    }
                                                    if (_5738_ladder_break)
                                                    {
                                                        break;
                                                    }
                                                    _5726++;
                                                    continue;
                                                }
                                                if (!(_5724))
                                                {
                                                    _5724 = true;
                                                }
                                            }
                                            if (!_5678)
                                            {
                                                _5677 = vec3(0.0);
                                            }
                                            else
                                            {
                                                float _5694 = 0.5 * _5680;
                                                vec3 _5695 = vec3(_5694, 0.0, 0.0);
                                                vec3 _5696 = _3949 + _5695;
                                                _5822 = false;
                                                {
                                                    int _5824 = 0;
                                                    for (;;)
                                                    {
                                                        bool _5836_ladder_break = false;
                                                        {
                                                            if (!(_5824 < Globals.uShadowLevels))
                                                            {
                                                                _5836_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                vec3 _5851 = (_5696 - Globals.uClipmapOrigin.data[_5824].xyz) / vec3(Globals.uClipmapOrigin.data[_5824].w);
                                                                if (any(lessThan(_5851, vec3(0.5))))
                                                                {
                                                                    _5825 = true;
                                                                }
                                                                else
                                                                {
                                                                    _5825 = any(greaterThan(_5851, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                }
                                                                if (!(_5825))
                                                                {
                                                                    _5822 = true;
                                                                    _5823 = ((textureLod(_8176, vec3(_5851.x / float(Globals.uShadowResolution), _5851.y / float(Globals.uShadowResolution), ((float(_5824) * float(Globals.uShadowResolution)) + _5851.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_5824];
                                                                    _5836_ladder_break = true;
                                                                }
                                                            }
                                                        }
                                                        if (_5836_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _5824++;
                                                        continue;
                                                    }
                                                    if (!(_5822))
                                                    {
                                                        _5822 = true;
                                                        _5823 = 1000000.0;
                                                    }
                                                }
                                                vec3 _5698 = _3949 - _5695;
                                                _5920 = false;
                                                {
                                                    int _5922 = 0;
                                                    for (;;)
                                                    {
                                                        bool _5934_ladder_break = false;
                                                        {
                                                            if (!(_5922 < Globals.uShadowLevels))
                                                            {
                                                                _5934_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                vec3 _5949 = (_5698 - Globals.uClipmapOrigin.data[_5922].xyz) / vec3(Globals.uClipmapOrigin.data[_5922].w);
                                                                if (any(lessThan(_5949, vec3(0.5))))
                                                                {
                                                                    _5923 = true;
                                                                }
                                                                else
                                                                {
                                                                    _5923 = any(greaterThan(_5949, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                }
                                                                if (!(_5923))
                                                                {
                                                                    _5920 = true;
                                                                    _5921 = ((textureLod(_8176, vec3(_5949.x / float(Globals.uShadowResolution), _5949.y / float(Globals.uShadowResolution), ((float(_5922) * float(Globals.uShadowResolution)) + _5949.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_5922];
                                                                    _5934_ladder_break = true;
                                                                }
                                                            }
                                                        }
                                                        if (_5934_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _5922++;
                                                        continue;
                                                    }
                                                    if (!(_5920))
                                                    {
                                                        _5920 = true;
                                                        _5921 = 1000000.0;
                                                    }
                                                }
                                                vec3 _5701 = vec3(0.0, _5694, 0.0);
                                                vec3 _5702 = _3949 + _5701;
                                                _6018 = false;
                                                {
                                                    int _6020 = 0;
                                                    for (;;)
                                                    {
                                                        bool _6032_ladder_break = false;
                                                        {
                                                            if (!(_6020 < Globals.uShadowLevels))
                                                            {
                                                                _6032_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                vec3 _6047 = (_5702 - Globals.uClipmapOrigin.data[_6020].xyz) / vec3(Globals.uClipmapOrigin.data[_6020].w);
                                                                if (any(lessThan(_6047, vec3(0.5))))
                                                                {
                                                                    _6021 = true;
                                                                }
                                                                else
                                                                {
                                                                    _6021 = any(greaterThan(_6047, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                }
                                                                if (!(_6021))
                                                                {
                                                                    _6018 = true;
                                                                    _6019 = ((textureLod(_8176, vec3(_6047.x / float(Globals.uShadowResolution), _6047.y / float(Globals.uShadowResolution), ((float(_6020) * float(Globals.uShadowResolution)) + _6047.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_6020];
                                                                    _6032_ladder_break = true;
                                                                }
                                                            }
                                                        }
                                                        if (_6032_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _6020++;
                                                        continue;
                                                    }
                                                    if (!(_6018))
                                                    {
                                                        _6018 = true;
                                                        _6019 = 1000000.0;
                                                    }
                                                }
                                                vec3 _5704 = _3949 - _5701;
                                                _6116 = false;
                                                {
                                                    int _6118 = 0;
                                                    for (;;)
                                                    {
                                                        bool _6130_ladder_break = false;
                                                        {
                                                            if (!(_6118 < Globals.uShadowLevels))
                                                            {
                                                                _6130_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                vec3 _6145 = (_5704 - Globals.uClipmapOrigin.data[_6118].xyz) / vec3(Globals.uClipmapOrigin.data[_6118].w);
                                                                if (any(lessThan(_6145, vec3(0.5))))
                                                                {
                                                                    _6119 = true;
                                                                }
                                                                else
                                                                {
                                                                    _6119 = any(greaterThan(_6145, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                }
                                                                if (!(_6119))
                                                                {
                                                                    _6116 = true;
                                                                    _6117 = ((textureLod(_8176, vec3(_6145.x / float(Globals.uShadowResolution), _6145.y / float(Globals.uShadowResolution), ((float(_6118) * float(Globals.uShadowResolution)) + _6145.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_6118];
                                                                    _6130_ladder_break = true;
                                                                }
                                                            }
                                                        }
                                                        if (_6130_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _6118++;
                                                        continue;
                                                    }
                                                    if (!(_6116))
                                                    {
                                                        _6116 = true;
                                                        _6117 = 1000000.0;
                                                    }
                                                }
                                                vec3 _5707 = vec3(0.0, 0.0, _5694);
                                                vec3 _5708 = _3949 + _5707;
                                                _6214 = false;
                                                {
                                                    int _6216 = 0;
                                                    for (;;)
                                                    {
                                                        bool _6228_ladder_break = false;
                                                        {
                                                            if (!(_6216 < Globals.uShadowLevels))
                                                            {
                                                                _6228_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                vec3 _6243 = (_5708 - Globals.uClipmapOrigin.data[_6216].xyz) / vec3(Globals.uClipmapOrigin.data[_6216].w);
                                                                if (any(lessThan(_6243, vec3(0.5))))
                                                                {
                                                                    _6217 = true;
                                                                }
                                                                else
                                                                {
                                                                    _6217 = any(greaterThan(_6243, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                }
                                                                if (!(_6217))
                                                                {
                                                                    _6214 = true;
                                                                    _6215 = ((textureLod(_8176, vec3(_6243.x / float(Globals.uShadowResolution), _6243.y / float(Globals.uShadowResolution), ((float(_6216) * float(Globals.uShadowResolution)) + _6243.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_6216];
                                                                    _6228_ladder_break = true;
                                                                }
                                                            }
                                                        }
                                                        if (_6228_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _6216++;
                                                        continue;
                                                    }
                                                    if (!(_6214))
                                                    {
                                                        _6214 = true;
                                                        _6215 = 1000000.0;
                                                    }
                                                }
                                                vec3 _5710 = _3949 - _5707;
                                                _6312 = false;
                                                {
                                                    int _6314 = 0;
                                                    for (;;)
                                                    {
                                                        bool _6326_ladder_break = false;
                                                        {
                                                            if (!(_6314 < Globals.uShadowLevels))
                                                            {
                                                                _6326_ladder_break = true;
                                                            }
                                                            else
                                                            {
                                                                vec3 _6341 = (_5710 - Globals.uClipmapOrigin.data[_6314].xyz) / vec3(Globals.uClipmapOrigin.data[_6314].w);
                                                                if (any(lessThan(_6341, vec3(0.5))))
                                                                {
                                                                    _6315 = true;
                                                                }
                                                                else
                                                                {
                                                                    _6315 = any(greaterThan(_6341, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                }
                                                                if (!(_6315))
                                                                {
                                                                    _6312 = true;
                                                                    _6313 = ((textureLod(_8176, vec3(_6341.x / float(Globals.uShadowResolution), _6341.y / float(Globals.uShadowResolution), ((float(_6314) * float(Globals.uShadowResolution)) + _6341.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_6314];
                                                                    _6326_ladder_break = true;
                                                                }
                                                            }
                                                        }
                                                        if (_6326_ladder_break)
                                                        {
                                                            break;
                                                        }
                                                        _6314++;
                                                        continue;
                                                    }
                                                    if (!(_6312))
                                                    {
                                                        _6312 = true;
                                                        _6313 = 1000000.0;
                                                    }
                                                }
                                                vec3 _5713 = vec3(_5823 - _5921, _6019 - _6117, _6215 - _6313);
                                                float _5714 = dot(_5713, _5713);
                                                if (_5714 < 1.0000000133514319600180897396058e-10)
                                                {
                                                    _5684 = vec3(0.0);
                                                }
                                                else
                                                {
                                                    _5684 = _5713 * inversesqrt(_5714);
                                                }
                                                _5677 = _5684;
                                            }
                                        }
                                        vec3 _4091 = world + Globals.uTraceOrigin;
                                        {
                                            float _7823 = Globals.uShelterMap1.z;
                                            float _7824 = Globals.uShelterMap1.w;
                                            float _7825 = Globals.uShelterMap2.x;
                                            float _7826 = Globals.uShelterMap2.y;
                                            ivec2 _6476 = ivec2(floor((_4091.xy - Globals.uShelterMap0.xy) * Globals.uShelterMap0.z));
                                            vec2 _7869 = Globals.uShelterMap1.xy;
                                            if (Globals.uShelterMap0.w > 0.5)
                                            {
                                                _6477 = _6476.x >= 0;
                                            }
                                            else
                                            {
                                                _6477 = false;
                                            }
                                            if (_6477)
                                            {
                                                _6477 = _6476.y >= 0;
                                            }
                                            else
                                            {
                                                _6477 = false;
                                            }
                                            if (_6477)
                                            {
                                                _6477 = _6476.x < int(_7869.x);
                                            }
                                            else
                                            {
                                                _6477 = false;
                                            }
                                            if (_6477)
                                            {
                                                _6477 = _6476.y < int(_7869.y);
                                            }
                                            else
                                            {
                                                _6477 = false;
                                            }
                                            if (!_6477)
                                            {
                                                _6411 = false;
                                            }
                                            else
                                            {
                                                vec4 _6460 = texelFetch(_8191, ivec3(_6476, 0).xy, 0);
                                                float _7873 = _7823;
                                                float _7874 = _7824;
                                                float _6527 = floor((_6460.z * 255.0) + 0.5) + (floor((_6460.y * 255.0) + 0.5) * 256.0);
                                                if (_6527 < 0.5)
                                                {
                                                    _6515 = -1000000015047466219876688855040.0;
                                                }
                                                else
                                                {
                                                    _6515 = _7873 + ((_6527 - 1.0) * _7874);
                                                }
                                                float _6463 = _4091.z;
                                                float _7878 = _7826;
                                                if (_6515 > (_6463 + _7825))
                                                {
                                                    _6540 = _6515 < (_6463 + _7878);
                                                }
                                                else
                                                {
                                                    _6540 = false;
                                                }
                                                _6411 = _6540;
                                            }
                                        }
                                        if (!_6411)
                                        {
                                            vec3 _4098 = _3949 + Globals.uTraceOrigin;
                                            {
                                                float _7761 = Globals.uShelterMap1.z;
                                                float _7762 = Globals.uShelterMap1.w;
                                                float _7763 = Globals.uShelterMap2.x;
                                                float _7764 = Globals.uShelterMap2.y;
                                                ivec2 _6621 = ivec2(floor((_4098.xy - Globals.uShelterMap0.xy) * Globals.uShelterMap0.z));
                                                vec2 _7809 = Globals.uShelterMap1.xy;
                                                if (Globals.uShelterMap0.w > 0.5)
                                                {
                                                    _6622 = _6621.x >= 0;
                                                }
                                                else
                                                {
                                                    _6622 = false;
                                                }
                                                if (_6622)
                                                {
                                                    _6622 = _6621.y >= 0;
                                                }
                                                else
                                                {
                                                    _6622 = false;
                                                }
                                                if (_6622)
                                                {
                                                    _6622 = _6621.x < int(_7809.x);
                                                }
                                                else
                                                {
                                                    _6622 = false;
                                                }
                                                if (_6622)
                                                {
                                                    _6622 = _6621.y < int(_7809.y);
                                                }
                                                else
                                                {
                                                    _6622 = false;
                                                }
                                                if (!_6622)
                                                {
                                                    _6556 = false;
                                                }
                                                else
                                                {
                                                    vec4 _6605 = texelFetch(_8191, ivec3(_6621, 0).xy, 0);
                                                    float _7813 = _7761;
                                                    float _7814 = _7762;
                                                    float _6672 = floor((_6605.z * 255.0) + 0.5) + (floor((_6605.y * 255.0) + 0.5) * 256.0);
                                                    if (_6672 < 0.5)
                                                    {
                                                        _6660 = -1000000015047466219876688855040.0;
                                                    }
                                                    else
                                                    {
                                                        _6660 = _7813 + ((_6672 - 1.0) * _7814);
                                                    }
                                                    float _6608 = _4098.z;
                                                    float _7818 = _7764;
                                                    if (_6660 > (_6608 + _7763))
                                                    {
                                                        _6685 = _6660 < (_6608 + _7818);
                                                    }
                                                    else
                                                    {
                                                        _6685 = false;
                                                    }
                                                    _6556 = _6685;
                                                }
                                            }
                                            _3946 = _6556;
                                        }
                                        else
                                        {
                                            _3946 = false;
                                        }
                                        if (_3946)
                                        {
                                            _7952 += 1.0;
                                            _7955 = true;
                                            _3955 = 0.0;
                                        }
                                        else
                                        {
                                            _3955 = smoothstep(_4076, 2.0 * Globals.uClipmapOrigin.data[0].w, length(_3949 - world)) * smoothstep(0.75 * Globals.uClipmapOrigin.data[0].w, _4076, dot(_3949 - world, _3558));
                                        }
                                        if (dot(_5677, _4196) > 0.0)
                                        {
                                            _3956 = -_5677;
                                        }
                                        else
                                        {
                                            _3956 = _5677;
                                        }
                                        if (_3955 <= 0.0)
                                        {
                                            _3948 = 0.0;
                                        }
                                        else
                                        {
                                            if (dot(_3956, _3956) > 0.5)
                                            {
                                                float _4123 = 0.5 * Globals.uClipmapOrigin.data[0].w;
                                                {
                                                    _3957 = vec3(0.0);
                                                    if (Globals.uLookupBuckets <= 0)
                                                    {
                                                        _6705 = false;
                                                    }
                                                    else
                                                    {
                                                        ivec3 _6744 = ivec3(floor((_3949 - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                                                        int _7091 = int((((uint(_6744.x) * 2376512323u) ^ (uint(_6744.y) * 3625334849u)) ^ (uint(_6744.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                                                        uvec2 _6749 = uvec2(textureSize(_8182, 0));
                                                        vec2 _6756 = vec2(float(_6749.x), float(_6749.y));
                                                        int _6708 = 0;
                                                        float _6709 = 0.0;
                                                        for (;;)
                                                        {
                                                            if (!(_6708 <= uCells._member0[Globals.uLookupBuckets].y))
                                                            {
                                                                break;
                                                            }
                                                            bool _6770 = _6708 == 0;
                                                            if (_6770)
                                                            {
                                                                _6710 = uCells._member0[_7091];
                                                            }
                                                            else
                                                            {
                                                                int _7096 = uCells._member0[Globals.uLookupBuckets].x + ((_6708 - 1) * 6);
                                                                float _7113 = intBitsToFloat(uCellPages_1._member0[_7096 + 3]) + _4123;
                                                                vec3 _7114 = _3949 - vec3(intBitsToFloat(uCellPages_1._member0[_7096]), intBitsToFloat(uCellPages_1._member0[_7096 + 1]), intBitsToFloat(uCellPages_1._member0[_7096 + 2]));
                                                                if (dot(_7114, _7114) > (_7113 * _7113))
                                                                {
                                                                    _7092 = ivec2(0);
                                                                }
                                                                else
                                                                {
                                                                    _7092 = ivec2(uCellPages_1._member0[_7096 + 4], uCellPages_1._member0[_7096 + 5]);
                                                                }
                                                                _6710 = _7092;
                                                            }
                                                            int _6711 = 0;
                                                            float _6712 = _6709;
                                                            for (;;)
                                                            {
                                                                bool _6781_ladder_break = false;
                                                                {
                                                                    if (!(_6711 < _6710.y))
                                                                    {
                                                                        _6781_ladder_break = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        int _6792 = _6710.x + _6711;
                                                                        int _6795 = uCellPages_1._member0[_6792] * 10;
                                                                        if (!((uPages._member0[_6795 + 5].w > 0.5) == _6770))
                                                                        {
                                                                            int _6804 = _6795 + 9;
                                                                            vec3 _6808 = _3949 - uPages._member0[_6804].xyz;
                                                                            float _6810 = uPages._member0[_6804].w + _4123;
                                                                            if (!(dot(_6808, _6808) > (_6810 * _6810)))
                                                                            {
                                                                                int _7130 = uCellPages_1._member0[_6792] * 10;
                                                                                vec4 _6719 = uPages._member0[_7130];
                                                                                vec4 _6720 = uPages._member0[_7130 + 1];
                                                                                int _7136 = _7130 + 2;
                                                                                int _7139 = _7130 + 3;
                                                                                int _7142 = _7130 + 4;
                                                                                int _7148 = _7130 + 6;
                                                                                int _7151 = _7130 + 7;
                                                                                int _7154 = _7130 + 8;
                                                                                vec3 _7161 = _3949 - vec3(uPages._member0[_7148].w, uPages._member0[_7151].w, uPages._member0[_7154].w);
                                                                                vec3 _7174 = vec3(dot(uPages._member0[_7148].xyz, _7161), dot(uPages._member0[_7151].xyz, _7161), dot(uPages._member0[_7154].xyz, _7161)) - uPages._member0[_7136].xyz;
                                                                                vec2 _7178 = max(vec2(uPages._member0[_7136].w, uPages._member0[_7139].w), vec2(9.9999999747524270787835121154785e-07));
                                                                                vec2 _6714 = _7178;
                                                                                vec2 _6713 = vec2(dot(_7174, uPages._member0[_7139].xyz), dot(_7174, uPages._member0[_7142].xyz)) / _7178;
                                                                                float _6715 = -dot(_7174, uPages._member0[_7130 + 5].xyz);
                                                                                float _6716 = uPages._member0[_7142].w;
                                                                                float _6819 = dot(uPages._member0[_7130 + 5].xyz, vec3(dot(uPages._member0[_7148].xyz, _3956), dot(uPages._member0[_7151].xyz, _3956), dot(uPages._member0[_7154].xyz, _3956)));
                                                                                if (!(_6819 < 0.100000001490116119384765625))
                                                                                {
                                                                                    float _6824 = Globals.uClipmapOrigin.data[0].w * 0.25;
                                                                                    vec2 _6827 = vec2(_6824) / _6714;
                                                                                    if (abs(_6713.x) > (1.0 + _6827.x))
                                                                                    {
                                                                                        _6721 = true;
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _6721 = abs(_6713.y) > (1.0 + _6827.y);
                                                                                    }
                                                                                    if (!(_6721))
                                                                                    {
                                                                                        if (_6715 < (Globals.uClipmapOrigin.data[0].w * (-0.25)))
                                                                                        {
                                                                                            _6722 = true;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _6722 = _6715 > (_6716 + _6824);
                                                                                        }
                                                                                        if (!(_6722))
                                                                                        {
                                                                                            vec2 _6871 = (_6720.xy + (((clamp(_6713, vec2(-1.0), vec2(1.0)) * 0.5) + vec2(0.5)) * _6720.zw)) - vec2(0.5);
                                                                                            vec2 _6872 = floor(_6871);
                                                                                            vec2 _6873 = _6871 - _6872;
                                                                                            vec2 _6879 = _6719.zw / max(_6720.zw, vec2(9.9999999747524270787835121154785e-07));
                                                                                            float _6883 = _6715 / max(_6716, 9.9999999747524270787835121154785e-07);
                                                                                            float _6887 = _4123 / max(0.5 * _6716, 9.9999999747524270787835121154785e-07);
                                                                                            vec4 _6723 = vec4(0.0);
                                                                                            int _6726 = 0;
                                                                                            for (;;)
                                                                                            {
                                                                                                if (!(_6726 < 4))
                                                                                                {
                                                                                                    break;
                                                                                                }
                                                                                                vec2 _6902 = vec2(float(_6726 & 1), float(_6726 >> 1));
                                                                                                vec2 _6905 = (_6872 + _6902) + vec2(0.5);
                                                                                                vec2 _6906 = _6905 / _6756;
                                                                                                if (textureLod(_8184, _6906, 0.0).w >= 0.5)
                                                                                                {
                                                                                                    _6727 = 1.0;
                                                                                                }
                                                                                                else
                                                                                                {
                                                                                                    _6727 = 0.0;
                                                                                                }
                                                                                                vec2 _6935 = mix(vec2(1.0) - _6873, _6873, _6902);
                                                                                                _6723[_6726] = ((_6935.x * _6935.y) * _6727) * (1.0 - clamp((abs(_6883 - textureLod(_8186, _6906, 0.0).x) - _6887) / (0.25 * _6887), 0.0, 1.0));
                                                                                                _6724[_6726] = textureLod(_8188, (floor(_6719.xy + ((_6905 - _6720.xy) * _6879)) + vec2(0.5)) / Globals.uAtlasSize.xy, 0.0).xyz;
                                                                                                _6726++;
                                                                                                continue;
                                                                                            }
                                                                                            float _6982 = ((_6723.x + _6723.y) + _6723.z) + _6723.w;
                                                                                            float _6983 = (_6819 * _6819) * _6982;
                                                                                            if (!(_6983 <= 0.0))
                                                                                            {
                                                                                                _3957 += (max(((((_6724[0] * _6723.x) + (_6724[1] * _6723.y)) + (_6724[2] * _6723.z)) + (_6724[3] * _6723.w)) / vec3(_6982), vec3(0.0)) * _6983);
                                                                                                _6712 += _6983;
                                                                                            }
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                                if (_6781_ladder_break)
                                                                {
                                                                    break;
                                                                }
                                                                _6711++;
                                                                continue;
                                                            }
                                                            _6708++;
                                                            _6709 = _6712;
                                                            continue;
                                                        }
                                                        if (_6709 <= 0.0)
                                                        {
                                                            _3957 = vec3(0.0);
                                                            _6705 = false;
                                                        }
                                                        else
                                                        {
                                                            _3957 /= vec3(_6709);
                                                            _6705 = true;
                                                        }
                                                    }
                                                }
                                                _3946 = _6705;
                                            }
                                            else
                                            {
                                                _3946 = false;
                                            }
                                            if (_3946)
                                            {
                                                vec3 _4154 = _3957 * Globals.uBounceScale;
                                                float _7570 = max(_4154.x, max(_4154.y, _4154.z));
                                                if (_7570 > Globals.uMaxRayIntensity)
                                                {
                                                    _7563 = _4154 * (Globals.uMaxRayIntensity / _7570);
                                                }
                                                else
                                                {
                                                    _7563 = _4154;
                                                }
                                                _7949 = _7563;
                                                _7950 = _3955;
                                                _7953 = true;
                                                _3944 = 2;
                                                _3945 = _4154;
                                                _3948 = _3955;
                                            }
                                            else
                                            {
                                                {
                                                    _3953 = vec2(0.0);
                                                    float _3954 = 1000000000.0;
                                                    if (Globals.uLookupBuckets <= 0)
                                                    {
                                                        _7194 = false;
                                                    }
                                                    else
                                                    {
                                                        ivec3 _7235 = ivec3(floor((_3949 - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                                                        int _7447 = int((((uint(_7235.x) * 2376512323u) ^ (uint(_7235.y) * 3625334849u)) ^ (uint(_7235.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                                                        uvec2 _7240 = uvec2(textureSize(_8182, 0));
                                                        vec2 _7247 = vec2(float(_7240.x), float(_7240.y));
                                                        bool _7197 = false;
                                                        bool _7198 = false;
                                                        int _7199 = 0;
                                                        for (;;)
                                                        {
                                                            if (_7199 <= uCells._member0[Globals.uLookupBuckets].y)
                                                            {
                                                                _7200 = !_7197;
                                                            }
                                                            else
                                                            {
                                                                _7200 = false;
                                                            }
                                                            if (!_7200)
                                                            {
                                                                break;
                                                            }
                                                            bool _7267 = _7199 == 0;
                                                            if (_7267)
                                                            {
                                                                _7201 = uCells._member0[_7447];
                                                            }
                                                            else
                                                            {
                                                                int _7452 = uCells._member0[Globals.uLookupBuckets].x + ((_7199 - 1) * 6);
                                                                float _7468 = intBitsToFloat(uCellPages_1._member0[_7452 + 3]);
                                                                vec3 _7470 = _3949 - vec3(intBitsToFloat(uCellPages_1._member0[_7452]), intBitsToFloat(uCellPages_1._member0[_7452 + 1]), intBitsToFloat(uCellPages_1._member0[_7452 + 2]));
                                                                if (dot(_7470, _7470) > (_7468 * _7468))
                                                                {
                                                                    _7448 = ivec2(0);
                                                                }
                                                                else
                                                                {
                                                                    _7448 = ivec2(uCellPages_1._member0[_7452 + 4], uCellPages_1._member0[_7452 + 5]);
                                                                }
                                                                _7201 = _7448;
                                                            }
                                                            bool _7202 = _7197;
                                                            bool _7203 = _7198;
                                                            int _7204 = 0;
                                                            for (;;)
                                                            {
                                                                bool _7279_ladder_break = false;
                                                                {
                                                                    if (_7204 < _7201.y)
                                                                    {
                                                                        _7205 = !_7202;
                                                                    }
                                                                    else
                                                                    {
                                                                        _7205 = false;
                                                                    }
                                                                    if (!_7205)
                                                                    {
                                                                        _7279_ladder_break = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        int _7296 = _7201.x + _7204;
                                                                        int _7299 = uCellPages_1._member0[_7296] * 10;
                                                                        if (!((uPages._member0[_7299 + 5].w > 0.5) == _7267))
                                                                        {
                                                                            int _7308 = _7299 + 9;
                                                                            vec3 _7312 = _3949 - uPages._member0[_7308].xyz;
                                                                            if (!(dot(_7312, _7312) > (uPages._member0[_7308].w * uPages._member0[_7308].w)))
                                                                            {
                                                                                vec3 _7319 = -_4196;
                                                                                int _7486 = uCellPages_1._member0[_7296] * 10;
                                                                                vec4 _7212 = uPages._member0[_7486];
                                                                                vec4 _7213 = uPages._member0[_7486 + 1];
                                                                                int _7492 = _7486 + 2;
                                                                                int _7495 = _7486 + 3;
                                                                                int _7498 = _7486 + 4;
                                                                                int _7504 = _7486 + 6;
                                                                                int _7507 = _7486 + 7;
                                                                                int _7510 = _7486 + 8;
                                                                                vec3 _7517 = _3949 - vec3(uPages._member0[_7504].w, uPages._member0[_7507].w, uPages._member0[_7510].w);
                                                                                vec3 _7211 = vec3(dot(uPages._member0[_7504].xyz, _7319), dot(uPages._member0[_7507].xyz, _7319), dot(uPages._member0[_7510].xyz, _7319));
                                                                                vec3 _7530 = vec3(dot(uPages._member0[_7504].xyz, _7517), dot(uPages._member0[_7507].xyz, _7517), dot(uPages._member0[_7510].xyz, _7517)) - uPages._member0[_7492].xyz;
                                                                                vec2 _7540 = vec2(dot(_7530, uPages._member0[_7495].xyz), dot(_7530, uPages._member0[_7498].xyz)) / max(vec2(uPages._member0[_7492].w, uPages._member0[_7495].w), vec2(9.9999999747524270787835121154785e-07));
                                                                                vec2 _7206 = _7540;
                                                                                float _7208 = -dot(_7530, uPages._member0[_7486 + 5].xyz);
                                                                                float _7209 = uPages._member0[_7498].w;
                                                                                vec3 _7210 = uPages._member0[_7486 + 5].xyz;
                                                                                if (abs(_7540.x) > 1.0)
                                                                                {
                                                                                    _7214 = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _7214 = abs(_7206.y) > 1.0;
                                                                                }
                                                                                if (!(_7214))
                                                                                {
                                                                                    if (dot(_4196, _4196) > 0.5)
                                                                                    {
                                                                                        _7215 = dot(_7210, _7211) < (-0.0500000007450580596923828125);
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _7215 = false;
                                                                                    }
                                                                                    if (!(_7215))
                                                                                    {
                                                                                        if (_7208 < (-0.25))
                                                                                        {
                                                                                            _7216 = true;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _7216 = _7208 > (_7209 + 0.25);
                                                                                        }
                                                                                        if (!(_7216))
                                                                                        {
                                                                                            vec2 _7363 = (_7206 * 0.5) + vec2(0.5);
                                                                                            vec2 _7370 = (_7213.xy + (_7363 * _7213.zw)) / _7247;
                                                                                            if (!(textureLod(_8184, _7370, 0.0).w < 0.5))
                                                                                            {
                                                                                                float _7390 = abs((textureLod(_8186, _7370, 0.0).x * _7209) - _7208);
                                                                                                if (_7390 < _3954)
                                                                                                {
                                                                                                    _3954 = _7390;
                                                                                                    _3953 = (_7212.xy + (_7363 * _7212.zw)) / Globals.uAtlasSize.xy;
                                                                                                    _7217 = _7390 < 0.0199999995529651641845703125;
                                                                                                    _7218 = true;
                                                                                                }
                                                                                                else
                                                                                                {
                                                                                                    _7217 = _7202;
                                                                                                    _7218 = _7203;
                                                                                                }
                                                                                                _7202 = _7217;
                                                                                                _7203 = _7218;
                                                                                            }
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                                if (_7279_ladder_break)
                                                                {
                                                                    break;
                                                                }
                                                                _7204++;
                                                                continue;
                                                            }
                                                            _7197 = _7202;
                                                            _7198 = _7203;
                                                            _7199++;
                                                            continue;
                                                        }
                                                        _7194 = _7198;
                                                    }
                                                }
                                                if (_7194)
                                                {
                                                    vec3 _4145 = textureLod(_8188, _3953, 0.0).xyz * Globals.uBounceScale;
                                                    float _7552 = max(_4145.x, max(_4145.y, _4145.z));
                                                    if (_7552 > Globals.uMaxRayIntensity)
                                                    {
                                                        _7545 = _4145 * (Globals.uMaxRayIntensity / _7552);
                                                    }
                                                    else
                                                    {
                                                        _7545 = _4145;
                                                    }
                                                    _7949 = _7545;
                                                    _7950 = _3955;
                                                    _7953 = true;
                                                    _3944 = 2;
                                                    _3945 = _4145;
                                                    _3948 = _3955;
                                                }
                                                else
                                                {
                                                    _7952 += _3955;
                                                    _7955 = true;
                                                    _3948 = 0.0;
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (_4196.z >= 0.0)
                                        {
                                            {
                                                if (Globals.uSkyShValid == 0)
                                                {
                                                    _5607 = Globals.uSkyColour;
                                                }
                                                else
                                                {
                                                    vec4 _5618 = vec4(_4196, 1.0);
                                                    vec4 _5621 = _5618.xyzz * _5618.yzzx;
                                                    float _5647 = _4196.x;
                                                    float _5649 = _4196.y;
                                                    _5607 = max(vec3(0.0), (vec3(dot(Globals.uSkySh.data[0], _5618), dot(Globals.uSkySh.data[1], _5618), dot(Globals.uSkySh.data[2], _5618)) + vec3(dot(Globals.uSkySh.data[3], _5621), dot(Globals.uSkySh.data[4], _5621), dot(Globals.uSkySh.data[5], _5621))) + (Globals.uSkySh.data[6].xyz * ((_5647 * _5647) - (_5649 * _5649))));
                                                }
                                            }
                                            _3945 = _5607;
                                        }
                                        else
                                        {
                                            {
                                                if (Globals.uSkyShValid == 0)
                                                {
                                                    _5555 = Globals.uSkyColour;
                                                }
                                                else
                                                {
                                                    vec4 _5566 = vec4(vec3(0.0, 0.0, 1.0), 1.0);
                                                    vec4 _5569 = _5566.xyzz * _5566.yzzx;
                                                    _5555 = max(vec3(0.0), vec3(dot(Globals.uSkySh.data[0], _5566), dot(Globals.uSkySh.data[1], _5566), dot(Globals.uSkySh.data[2], _5566)) + vec3(dot(Globals.uSkySh.data[3], _5569), dot(Globals.uSkySh.data[4], _5569), dot(Globals.uSkySh.data[5], _5569)));
                                                }
                                            }
                                            _3945 = (_5555 + Globals.uGroundSun) * 0.25;
                                        }
                                        float _5665 = max(_3945.x, max(_3945.y, _3945.z));
                                        if (_5665 > Globals.uMaxRayIntensity)
                                        {
                                            _5658 = _3945 * (Globals.uMaxRayIntensity / _5665);
                                        }
                                        else
                                        {
                                            _5658 = _3945;
                                        }
                                        _7951 = _5658;
                                        _7954 = true;
                                        _3944 = 3;
                                        _3948 = 1.0;
                                    }
                                }
                            }
                            else
                            {
                                _3948 = 1.0;
                            }
                            if (Globals.uDebugSource != 0)
                            {
                                _3946 = _3944 != Globals.uDebugSource;
                            }
                            else
                            {
                                _3946 = false;
                            }
                            if (_3946)
                            {
                                _3945 = vec3(0.0);
                            }
                            float _7588 = max(_3945.x, max(_3945.y, _3945.z));
                            if (_7588 > Globals.uMaxRayIntensity)
                            {
                                _7581 = _3945 * (Globals.uMaxRayIntensity / _7588);
                            }
                            else
                            {
                                _7581 = _3945;
                            }
                            vec3 _8106 = _7581;
                            float _8107 = _3948;
                            vec3 _8108 = _7949;
                            float _8109 = _7950;
                            vec3 _8110 = _7951;
                            float _8111 = _7952;
                            bool _8113 = _7954;
                            bool _8114 = _7955;
                            if (_7953)
                            {
                                _8080 += (_8108 * _8109);
                                _8081 += _8109;
                            }
                            if (_8114)
                            {
                                _8084 += _8111;
                            }
                            if (_8113)
                            {
                                _8082 += _8110;
                                _8083 += 1.0;
                            }
                            _8078 += (_8106 * _8107);
                            _8079 += _8107;
                            _3832++;
                            continue;
                        }
                        bool _7647;
                        if (_8084 > 0.0)
                        {
                            if (Globals.uDebugSource == 0)
                            {
                                _7647 = true;
                            }
                            else
                            {
                                _7647 = Globals.uDebugSource == 2;
                            }
                        }
                        else
                        {
                            _7647 = false;
                        }
                        vec3 _7648;
                        float _7649;
                        if (_7647)
                        {
                            if (_8081 > 0.0)
                            {
                                _7648 = _8080 / vec3(_8081);
                            }
                            else
                            {
                                if (_8083 > 0.0)
                                {
                                    _7648 = _8082 / vec3(_8083);
                                }
                                else
                                {
                                    vec3 _7708;
                                    {
                                        if (Globals.uSkyShValid == 0)
                                        {
                                            _7708 = Globals.uSkyColour;
                                        }
                                        else
                                        {
                                            vec4 _7719 = vec4(vec3(0.0, 0.0, 1.0), 1.0);
                                            vec4 _7722 = _7719.xyzz * _7719.yzzx;
                                            _7708 = max(vec3(0.0), vec3(dot(Globals.uSkySh.data[0], _7719), dot(Globals.uSkySh.data[1], _7719), dot(Globals.uSkySh.data[2], _7719)) + vec3(dot(Globals.uSkySh.data[3], _7722), dot(Globals.uSkySh.data[4], _7722), dot(Globals.uSkySh.data[5], _7722)));
                                        }
                                    }
                                    _7648 = _7708;
                                }
                                _7648 *= 0.25;
                            }
                            _7649 = _8079 + _8084;
                            _7648 = _8078 + (_7648 * _8084);
                        }
                        else
                        {
                            _7649 = _8079;
                            _7648 = _8078;
                        }
                        _3829 = max(_7648 * (3.1415927410125732421875 / max(_7649, 4.0)), vec3(0.0));
                    }
                }
                imageStore(uIndirect, ivec2(gl_GlobalInvocationID.xy), vec4(_3829, 1.0));
            }
        }
    }
}

