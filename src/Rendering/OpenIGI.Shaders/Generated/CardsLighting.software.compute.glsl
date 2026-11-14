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

uniform ivec2 _1714_comp[4] = ivec2[](ivec2(0), ivec2(1, 0), ivec2(0, 1), ivec2(1));
uniform ivec2 _1905_comp[4] = ivec2[](ivec2(-1, 0), ivec2(1, 0), ivec2(0, -1), ivec2(0, 1));

layout(binding = 0, std140) uniform SLANG_ParameterGroup_Globals_std140
{
    _Array_std140_vector_float_4_4 uClipmapOrigin;
    _Array_std140_vector_float_4_7 uSkySh;
    vec3 uAtlasSize;
    vec3 uLookupMin;
    vec3 uSunDirection;
    vec3 uSunColour;
    vec3 uSkyColour;
    vec3 uActiveCentre;
    vec3 uProbeMin;
    vec3 uTraceOrigin;
    _Array_std140_float4 uClipmapRange;
    int uShadowLevels;
    int uShadowResolution;
    int uLookupBuckets;
    float uLookupCell;
    int uTileCount;
    int uTileOffset;
    int uUseActiveList;
    int uActiveCount;
    int uTileStride;
    float uActiveRadius;
    int uLightCount;
    float uLocalScale;
    int uProbeWidth;
    int uProbeHeight;
    int uTemporalIndex;
    int uMaxFrames;
    int uJitterStride;
    int uMode;
    float uBoost;
    int uSkyShValid;
    int uProbe;
    float uProbeInvExtent;
} Globals;

layout(binding = 6, std430) restrict readonly buffer StructuredBuffer
{
    int _member0[];
} uActiveTiles;

layout(binding = 1, std430) restrict readonly buffer uTiles
{
    ivec4 _member0[];
} uTiles_1;

layout(binding = 0, std430) restrict readonly buffer uPages
{
    vec4 _member0[];
} uPages_1;

layout(binding = 5, std430) restrict buffer RWStructuredBuffer
{
    vec4 _member0[];
} uProbePos;

layout(binding = 3, std430) restrict buffer uCells
{
    ivec2 _member0[];
} uCells_1;

layout(binding = 4, std430) restrict buffer uCellPages
{
    int _member0[];
} uCellPages_1;

layout(binding = 2, std430) restrict readonly buffer uLights
{
    vec4 _member0[];
} uLights_1;

layout(binding = 7, std430) restrict readonly buffer uLampMapRecords
{
    vec4 _member0[];
} uLampMapRecords_1;

layout(binding = 0, rgba16f) uniform restrict image2D uDirect;
layout(binding = 0) uniform sampler2D _6805;
layout(binding = 1) uniform sampler2D _6807;
layout(binding = 2) uniform sampler2D _6809;
layout(binding = 3) uniform sampler2D _6811;
layout(binding = 4) uniform sampler3D _6813;
layout(binding = 6) uniform sampler2D _6815;
layout(binding = 5) uniform sampler2D _6818;
layout(binding = 7) uniform sampler2D _6820;
layout(binding = 8) uniform sampler2DArray _6823;

void main()
{
    bool _6632 = false;
    bool _6556 = false;
    bool _6195 = false;
    bool _6114 = false;
    bool _5779 = false;
    bool _5698 = false;
    bool _5591 = false;
    bool _5481 = false;
    bool _5389 = false;
    bool _4145 = false;
    bool _4064 = false;
    {
        ivec4 entry;
        int at;
        vec4 lightPage;
        vec4 cardPage;
        bool _3634;
        {
            entry = ivec4(0);
            at = 0;
            lightPage = vec4(0.0);
            cardPage = vec4(0.0);
            int _3647 = (int(gl_WorkGroupID.x) * max(Globals.uTileStride, 1)) + Globals.uTileOffset;
            int _3635;
            if (Globals.uUseActiveList != 0)
            {
                if (_3647 >= Globals.uActiveCount)
                {
                    _3634 = false;
                }
                else
                {
                    _3635 = uActiveTiles._member0[_3647];
                    if (_3635 >= Globals.uTileCount)
                    {
                        _3634 = false;
                    }
                    else
                    {
                        entry = uTiles_1._member0[_3635];
                        int _3671 = uTiles_1._member0[_3635].x * 10;
                        at = _3671;
                        if (uPages_1._member0[_3671 + 5].w > 0.5)
                        {
                            _3634 = false;
                        }
                        else
                        {
                            if (Globals.uActiveRadius > 0.0)
                            {
                                int _3684 = at + 9;
                                vec3 _3690 = uPages_1._member0[_3684].xyz - Globals.uActiveCentre;
                                float _3693 = Globals.uActiveRadius + uPages_1._member0[_3684].w;
                                if (dot(_3690, _3690) > (_3693 * _3693))
                                {
                                    _3634 = false;
                                }
                                else
                                {
                                    lightPage = uPages_1._member0[at];
                                    cardPage = uPages_1._member0[at + 1];
                                    _3634 = true;
                                }
                            }
                            else
                            {
                                lightPage = uPages_1._member0[at];
                                cardPage = uPages_1._member0[at + 1];
                                _3634 = true;
                            }
                        }
                    }
                }
            }
            else
            {
                _3635 = _3647;
                if (_3635 >= Globals.uTileCount)
                {
                    _3634 = false;
                }
                else
                {
                    entry = uTiles_1._member0[_3635];
                    int _3671 = uTiles_1._member0[_3635].x * 10;
                    at = _3671;
                    if (uPages_1._member0[_3671 + 5].w > 0.5)
                    {
                        _3634 = false;
                    }
                    else
                    {
                        if (Globals.uActiveRadius > 0.0)
                        {
                            int _3684 = at + 9;
                            vec3 _3690 = uPages_1._member0[_3684].xyz - Globals.uActiveCentre;
                            float _3693 = Globals.uActiveRadius + uPages_1._member0[_3684].w;
                            if (dot(_3690, _3690) > (_3693 * _3693))
                            {
                                _3634 = false;
                            }
                            else
                            {
                                lightPage = uPages_1._member0[at];
                                cardPage = uPages_1._member0[at + 1];
                                _3634 = true;
                            }
                        }
                        else
                        {
                            lightPage = uPages_1._member0[at];
                            cardPage = uPages_1._member0[at + 1];
                            _3634 = true;
                        }
                    }
                }
            }
        }
        if (!(!_3634))
        {
            ivec2 within = (entry.yz * ivec2(8)) + ivec2(gl_LocalInvocationID.xy);
            bool _18;
            if (within.x >= int(lightPage.z))
            {
                _18 = true;
            }
            else
            {
                _18 = within.y >= int(lightPage.w);
            }
            if (!(_18))
            {
                vec2 _3719 = (vec2(within) + vec2(0.5)) / vec2(lightPage.zw);
                vec2 uv = _3719;
                uvec2 _3721 = uvec2(textureSize(_6805, 0));
                vec2 _3734 = (cardPage.xy + (_3719 * cardPage.zw)) / vec2(float(_3721.x), float(_3721.y));
                vec2 cardUv = _3734;
                vec4 _3738 = textureLod(_6807, _3734, 0.0);
                ivec2 pageAt = ivec2(int(lightPage.x), int(lightPage.y));
                if (_3738.w < 0.5)
                {
                    imageStore(uDirect, ivec2(uvec2(pageAt + within)), vec4(0.0));
                }
                else
                {
                    int _3741 = at + 2;
                    int _3744 = at + 3;
                    int _3747 = at + 4;
                    int _3753 = at + 6;
                    int _3756 = at + 7;
                    int _3759 = at + 8;
                    vec3 _3771 = normalize((textureLod(_6809, cardUv, 0.0).xyz * 2.0) - vec3(1.0));
                    vec3 _3799 = ((uPages_1._member0[_3741].xyz + ((uPages_1._member0[_3744].xyz * ((uv.x * 2.0) - 1.0)) * uPages_1._member0[_3741].w)) + ((uPages_1._member0[_3747].xyz * ((uv.y * 2.0) - 1.0)) * uPages_1._member0[_3744].w)) - (uPages_1._member0[at + 5].xyz * (textureLod(_6811, cardUv, 0.0).x * uPages_1._member0[_3747].w));
                    vec3 _3803 = vec3(uPages_1._member0[_3753].x, uPages_1._member0[_3756].x, uPages_1._member0[_3759].x);
                    vec3 _3808 = vec3(uPages_1._member0[_3753].y, uPages_1._member0[_3756].y, uPages_1._member0[_3759].y);
                    vec3 _3813 = vec3(uPages_1._member0[_3753].z, uPages_1._member0[_3756].z, uPages_1._member0[_3759].z);
                    vec3 world = vec3(dot(_3803, _3799), dot(_3808, _3799), dot(_3813, _3799)) + vec3(uPages_1._member0[_3753].w, uPages_1._member0[_3756].w, uPages_1._member0[_3759].w);
                    vec3 worldNormal = normalize(vec3(dot(_3803, _3771), dot(_3808, _3771), dot(_3813, _3771)));
                    ivec2 texelAt = pageAt + within;
                    float visibility;
                    if (Globals.uMode == 1)
                    {
                        if (((texelAt.x | texelAt.y) & 3) != 0)
                        {
                            imageStore(uDirect, ivec2(uvec2(texelAt)), vec4(0.0));
                        }
                        else
                        {
                            ivec2 probeCoord = texelAt >> ivec2(2);
                            uProbePos._member0[(probeCoord.y * Globals.uProbeWidth) + probeCoord.x] = vec4(world, 1.0);
                            vec2 _3827;
                            {
                                if (Globals.uTemporalIndex <= 0)
                                {
                                    _3827 = vec2(0.5);
                                }
                                else
                                {
                                    float _3842 = float(((uint(probeCoord.x) * 73856093u) ^ (uint(probeCoord.y) * 19349663u)) & 65535u);
                                    int _3848 = (Globals.uTemporalIndex / max(Globals.uJitterStride, 1)) & 65535;
                                    uint _3863 = uint(_3848);
                                    uint _3866 = (_3863 << 16u) | (_3863 >> 16u);
                                    uint _3871 = ((_3866 & 1431655765u) << 1u) | ((_3866 & 2863311530u) >> 1u);
                                    uint _3876 = ((_3871 & 858993459u) << 2u) | ((_3871 & 3435973836u) >> 2u);
                                    uint _3881 = ((_3876 & 252645135u) << 4u) | ((_3876 & 4042322160u) >> 4u);
                                    _3827 = fract(vec2(fract(float(_3848) * 0.61803400516510009765625), float(((_3881 & 16711935u) << 8u) | ((_3881 & 4278255360u) >> 8u)) * 2.3283064365386962890625e-10) + vec2(_3842 * 1.52587890625e-05, fract(_3842 * 2.4689239580766297876834869384766e-05)));
                                }
                            }
                            vec3 _3890;
                            {
                                bool _3951;
                                if (Globals.uShadowLevels > 0)
                                {
                                    _3951 = Globals.uLookupBuckets > 0;
                                }
                                else
                                {
                                    _3951 = false;
                                }
                                if (!_3951)
                                {
                                    _3890 = vec3(0.0);
                                }
                                else
                                {
                                    vec3 _6742;
                                    vec3 _6743;
                                    vec3 _6744;
                                    {
                                        vec3 _6757 = worldNormal;
                                        if (worldNormal.z < (-0.99999988079071044921875))
                                        {
                                            _6742 = vec3(0.0, -1.0, 0.0);
                                            _6743 = vec3(-1.0, 0.0, 0.0);
                                            _6744 = _6757;
                                        }
                                        else
                                        {
                                            float _3979 = 1.0 / (1.0 + worldNormal.z);
                                            float _3981 = -worldNormal.x;
                                            float _3984 = (_3981 * worldNormal.y) * _3979;
                                            _6742 = vec3(1.0 - ((worldNormal.x * worldNormal.x) * _3979), _3984, _3981);
                                            _6743 = vec3(_3984, 1.0 - ((worldNormal.y * worldNormal.y) * _3979), -worldNormal.y);
                                            _6744 = _6757;
                                        }
                                    }
                                    int _3891 = 0;
                                    vec4 _3892 = vec4(0.0);
                                    vec4 _3893 = vec4(0.0);
                                    vec4 _3894 = vec4(0.0);
                                    vec3 _3999;
                                    vec2 _4001;
                                    float _4065;
                                    bool _4068;
                                    bool _4069;
                                    float _4071;
                                    float _4146;
                                    bool _4148;
                                    bool _4260;
                                    bool _4266;
                                    ivec2 _4267;
                                    bool _4271;
                                    bool _4280;
                                    bool _4281;
                                    bool _4282;
                                    bool _4283;
                                    bool _4284;
                                    ivec2 _4514;
                                    for (;;)
                                    {
                                        if (!(_3891 < 16))
                                        {
                                            break;
                                        }
                                        int _3919 = _3891;
                                        vec2 _4011 = (vec2(float(_3919 - 4 * (_3919 / 4)), float(_3919 / 4)) + _3827) * vec2(0.25);
                                        float _4013 = 6.283185482025146484375 * _4011.x;
                                        float _4014 = _4011.y;
                                        float _4018 = sqrt(max(0.0, 1.0 - (_4014 * _4014)));
                                        vec3 _4063 = ((_6742 * (_4018 * cos(_4013))) + (_6743 * (_4018 * sin(_4013)))) + (_6744 * _4014);
                                        vec3 _3895 = vec3(0.0);
                                        _4064 = false;
                                        {
                                            _3999 = world;
                                            if (Globals.uShadowLevels <= 0)
                                            {
                                                _4064 = true;
                                                _4065 = 1.0;
                                            }
                                            else
                                            {
                                                vec3 _4088 = world + ((worldNormal * Globals.uClipmapOrigin.data[0].w) * 1.5);
                                                float _4099 = (Globals.uClipmapOrigin.data[0].w * float(Globals.uShadowResolution)) * float(1 << (Globals.uShadowLevels - 1));
                                                int _4066 = 0;
                                                float _4067 = 0.0;
                                                for (;;)
                                                {
                                                    if (_4066 < 64)
                                                    {
                                                        _4068 = _4067 < _4099;
                                                    }
                                                    else
                                                    {
                                                        _4068 = false;
                                                    }
                                                    if (!_4068)
                                                    {
                                                        break;
                                                    }
                                                    vec3 _4116 = _4088 + (_4063 * _4067);
                                                    _4145 = false;
                                                    {
                                                        _4069 = false;
                                                        _4071 = Globals.uClipmapOrigin.data[0].w;
                                                        int _4147 = 0;
                                                        for (;;)
                                                        {
                                                            bool _4159_ladder_break = false;
                                                            {
                                                                if (!(_4147 < Globals.uShadowLevels))
                                                                {
                                                                    _4159_ladder_break = true;
                                                                }
                                                                else
                                                                {
                                                                    vec3 _4174 = (_4116 - Globals.uClipmapOrigin.data[_4147].xyz) / vec3(Globals.uClipmapOrigin.data[_4147].w);
                                                                    if (any(lessThan(_4174, vec3(0.5))))
                                                                    {
                                                                        _4148 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _4148 = any(greaterThan(_4174, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                    }
                                                                    if (!(_4148))
                                                                    {
                                                                        _4069 = true;
                                                                        _4071 = Globals.uClipmapOrigin.data[_4147].w;
                                                                        _4145 = true;
                                                                        _4146 = ((textureLod(_6813, vec3(_4174.x / float(Globals.uShadowResolution), _4174.y / float(Globals.uShadowResolution), ((float(_4147) * float(Globals.uShadowResolution)) + _4174.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_4147];
                                                                        _4159_ladder_break = true;
                                                                    }
                                                                }
                                                            }
                                                            if (_4159_ladder_break)
                                                            {
                                                                break;
                                                            }
                                                            _4147++;
                                                            continue;
                                                        }
                                                        if (!(_4145))
                                                        {
                                                            _4145 = true;
                                                            _4146 = 1000000.0;
                                                        }
                                                    }
                                                    if (!_4069)
                                                    {
                                                        _4064 = true;
                                                        _4065 = 1.0;
                                                        break;
                                                    }
                                                    float _4250 = (0.5 * _4071) * clamp(_4067 / max(2.0 * _4071, 9.9999999747524270787835121154785e-07), 0.0, 1.0);
                                                    float _4072 = max((_4067 + _4146) - _4250, 0.0);
                                                    if (_4146 < _4250)
                                                    {
                                                        _3999 = _4088 + (_4063 * _4072);
                                                        _4064 = true;
                                                        _4065 = 0.0;
                                                        break;
                                                    }
                                                    _4066++;
                                                    _4067 += max(_4146, _4071 * 0.25);
                                                    continue;
                                                }
                                                if (!(_4064))
                                                {
                                                    _4064 = true;
                                                    _4065 = 1.0;
                                                }
                                            }
                                        }
                                        if (_4065 <= 0.5)
                                        {
                                            {
                                                _4001 = vec2(0.0);
                                                float _4002 = 1000000000.0;
                                                if (Globals.uLookupBuckets <= 0)
                                                {
                                                    _4260 = false;
                                                }
                                                else
                                                {
                                                    ivec3 _4301 = ivec3(floor((_3999 - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                                                    int _4513 = int((((uint(_4301.x) * 2376512323u) ^ (uint(_4301.y) * 3625334849u)) ^ (uint(_4301.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                                                    uvec2 _4306 = uvec2(textureSize(_6805, 0));
                                                    vec2 _4313 = vec2(float(_4306.x), float(_4306.y));
                                                    bool _4263 = false;
                                                    bool _4264 = false;
                                                    int _4265 = 0;
                                                    for (;;)
                                                    {
                                                        if (_4265 <= uCells_1._member0[Globals.uLookupBuckets].y)
                                                        {
                                                            _4266 = !_4263;
                                                        }
                                                        else
                                                        {
                                                            _4266 = false;
                                                        }
                                                        if (!_4266)
                                                        {
                                                            break;
                                                        }
                                                        bool _4333 = _4265 == 0;
                                                        if (_4333)
                                                        {
                                                            _4267 = uCells_1._member0[_4513];
                                                        }
                                                        else
                                                        {
                                                            int _4518 = uCells_1._member0[Globals.uLookupBuckets].x + ((_4265 - 1) * 6);
                                                            float _4534 = intBitsToFloat(uCellPages_1._member0[_4518 + 3]);
                                                            vec3 _4536 = _3999 - vec3(intBitsToFloat(uCellPages_1._member0[_4518]), intBitsToFloat(uCellPages_1._member0[_4518 + 1]), intBitsToFloat(uCellPages_1._member0[_4518 + 2]));
                                                            if (dot(_4536, _4536) > (_4534 * _4534))
                                                            {
                                                                _4514 = ivec2(0);
                                                            }
                                                            else
                                                            {
                                                                _4514 = ivec2(uCellPages_1._member0[_4518 + 4], uCellPages_1._member0[_4518 + 5]);
                                                            }
                                                            _4267 = _4514;
                                                        }
                                                        bool _4268 = _4263;
                                                        bool _4269 = _4264;
                                                        int _4270 = 0;
                                                        for (;;)
                                                        {
                                                            bool _4345_ladder_break = false;
                                                            {
                                                                if (_4270 < _4267.y)
                                                                {
                                                                    _4271 = !_4268;
                                                                }
                                                                else
                                                                {
                                                                    _4271 = false;
                                                                }
                                                                if (!_4271)
                                                                {
                                                                    _4345_ladder_break = true;
                                                                }
                                                                else
                                                                {
                                                                    int _4362 = _4267.x + _4270;
                                                                    int _4365 = uCellPages_1._member0[_4362] * 10;
                                                                    if (!((uPages_1._member0[_4365 + 5].w > 0.5) == _4333))
                                                                    {
                                                                        int _4374 = _4365 + 9;
                                                                        vec3 _4378 = _3999 - uPages_1._member0[_4374].xyz;
                                                                        if (!(dot(_4378, _4378) > (uPages_1._member0[_4374].w * uPages_1._member0[_4374].w)))
                                                                        {
                                                                            vec3 _4385 = -_4063;
                                                                            int _4552 = uCellPages_1._member0[_4362] * 10;
                                                                            vec4 _4278 = uPages_1._member0[_4552];
                                                                            vec4 _4279 = uPages_1._member0[_4552 + 1];
                                                                            int _4558 = _4552 + 2;
                                                                            int _4561 = _4552 + 3;
                                                                            int _4564 = _4552 + 4;
                                                                            int _4570 = _4552 + 6;
                                                                            int _4573 = _4552 + 7;
                                                                            int _4576 = _4552 + 8;
                                                                            vec3 _4583 = _3999 - vec3(uPages_1._member0[_4570].w, uPages_1._member0[_4573].w, uPages_1._member0[_4576].w);
                                                                            vec3 _4277 = vec3(dot(uPages_1._member0[_4570].xyz, _4385), dot(uPages_1._member0[_4573].xyz, _4385), dot(uPages_1._member0[_4576].xyz, _4385));
                                                                            vec3 _4596 = vec3(dot(uPages_1._member0[_4570].xyz, _4583), dot(uPages_1._member0[_4573].xyz, _4583), dot(uPages_1._member0[_4576].xyz, _4583)) - uPages_1._member0[_4558].xyz;
                                                                            vec2 _4606 = vec2(dot(_4596, uPages_1._member0[_4561].xyz), dot(_4596, uPages_1._member0[_4564].xyz)) / max(vec2(uPages_1._member0[_4558].w, uPages_1._member0[_4561].w), vec2(9.9999999747524270787835121154785e-07));
                                                                            vec2 _4272 = _4606;
                                                                            float _4274 = -dot(_4596, uPages_1._member0[_4552 + 5].xyz);
                                                                            float _4275 = uPages_1._member0[_4564].w;
                                                                            vec3 _4276 = uPages_1._member0[_4552 + 5].xyz;
                                                                            if (abs(_4606.x) > 1.0)
                                                                            {
                                                                                _4280 = true;
                                                                            }
                                                                            else
                                                                            {
                                                                                _4280 = abs(_4272.y) > 1.0;
                                                                            }
                                                                            if (!(_4280))
                                                                            {
                                                                                if (dot(_4063, _4063) > 0.5)
                                                                                {
                                                                                    _4281 = dot(_4276, _4277) < (-0.0500000007450580596923828125);
                                                                                }
                                                                                else
                                                                                {
                                                                                    _4281 = false;
                                                                                }
                                                                                if (!(_4281))
                                                                                {
                                                                                    if (_4274 < (-0.25))
                                                                                    {
                                                                                        _4282 = true;
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _4282 = _4274 > (_4275 + 0.25);
                                                                                    }
                                                                                    if (!(_4282))
                                                                                    {
                                                                                        vec2 _4429 = (_4272 * 0.5) + vec2(0.5);
                                                                                        vec2 _4436 = (_4279.xy + (_4429 * _4279.zw)) / _4313;
                                                                                        if (!(textureLod(_6807, _4436, 0.0).w < 0.5))
                                                                                        {
                                                                                            float _4456 = abs((textureLod(_6811, _4436, 0.0).x * _4275) - _4274);
                                                                                            if (_4456 < _4002)
                                                                                            {
                                                                                                _4002 = _4456;
                                                                                                _4001 = (_4278.xy + (_4429 * _4278.zw)) / Globals.uAtlasSize.xy;
                                                                                                _4283 = _4456 < 0.0199999995529651641845703125;
                                                                                                _4284 = true;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                _4283 = _4268;
                                                                                                _4284 = _4269;
                                                                                            }
                                                                                            _4268 = _4283;
                                                                                            _4269 = _4284;
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                            if (_4345_ladder_break)
                                                            {
                                                                break;
                                                            }
                                                            _4270++;
                                                            continue;
                                                        }
                                                        _4263 = _4268;
                                                        _4264 = _4269;
                                                        _4265++;
                                                        continue;
                                                    }
                                                    _4260 = _4264;
                                                }
                                            }
                                            if (_4260)
                                            {
                                                _3895 = textureLod(_6815, _4001, 0.0).xyz * smoothstep(1.5 * Globals.uClipmapOrigin.data[0].w, 2.0 * Globals.uClipmapOrigin.data[0].w, length(_3999 - world));
                                            }
                                        }
                                        vec4 _4619 = vec4(0.2820949852466583251953125, (-0.48860299587249755859375) * _4063.y, 0.48860299587249755859375 * _4063.z, (-0.48860299587249755859375) * _4063.x);
                                        _3891++;
                                        _3892 += (_4619 * _3895.x);
                                        _3893 += (_4619 * _3895.y);
                                        _3894 += (_4619 * _3895.z);
                                        continue;
                                    }
                                    vec4 _4645 = vec4(0.2820949852466583251953125, (-0.48860299587249755859375) * worldNormal.y, 0.48860299587249755859375 * worldNormal.z, (-0.48860299587249755859375) * worldNormal.x);
                                    vec4 _4636 = vec4(_4645.x * 3.1415927410125732421875, _4645.yzw * 2.094395160675048828125);
                                    _3890 = max(vec3(dot(_3892, _4636), dot(_3893, _4636), dot(_3894, _4636)) * 0.3926990926265716552734375, vec3(0.0));
                                }
                            }
                            vec4 sampled = imageLoad(uDirect, texelAt);
                            if (Globals.uTemporalIndex > 0)
                            {
                                visibility = sampled.w;
                            }
                            else
                            {
                                visibility = 0.0;
                            }
                            float limit = float(Globals.uMaxFrames);
                            imageStore(uDirect, ivec2(uvec2(texelAt)), vec4(mix(sampled.xyz, _3890, vec3(1.0 / (1.0 + min(visibility, limit)))), min(visibility + 1.0, limit + 1.0)));
                        }
                    }
                    else
                    {
                        int light;
                        vec3 lit;
                        if (Globals.uMode == 2)
                        {
                            ivec2 _1694 = pageAt + ivec2(int(lightPage.z), int(lightPage.w));
                            ivec2 probe00 = texelAt >> ivec2(2);
                            vec2 _1705 = (vec2(texelAt - (probe00 * ivec2(4))) + vec2(1.0)) * vec2(0.16666667163372039794921875);
                            light = 0;
                            lit = vec3(0.0);
                            visibility = 0.0;
                            vec3 position;
                            bool _4647;
                            bool _4648;
                            float _4697;
                            vec3 _4715;
                            bool _4774;
                            bool _4775;
                            float _4824;
                            for (;;)
                            {
                                bool _59_ladder_break = false;
                                {
                                    if (!(light < 4))
                                    {
                                        _59_ladder_break = true;
                                    }
                                    else
                                    {
                                        ivec2 probeCoord_1 = probe00 + _1714_comp[light];
                                        {
                                            position = vec3(0.0);
                                            if (any(lessThan(probeCoord_1, ivec2(0))))
                                            {
                                                _4648 = true;
                                            }
                                            else
                                            {
                                                _4648 = probeCoord_1.x >= Globals.uProbeWidth;
                                            }
                                            if (_4648)
                                            {
                                                _4648 = true;
                                            }
                                            else
                                            {
                                                _4648 = probeCoord_1.y >= Globals.uProbeHeight;
                                            }
                                            if (_4648)
                                            {
                                                _4647 = false;
                                            }
                                            else
                                            {
                                                ivec2 _4673 = probeCoord_1 * ivec2(4);
                                                if (any(lessThan(_4673, pageAt)))
                                                {
                                                    _4648 = true;
                                                }
                                                else
                                                {
                                                    _4648 = any(greaterThanEqual(_4673, _1694));
                                                }
                                                if (_4648)
                                                {
                                                    _4647 = false;
                                                }
                                                else
                                                {
                                                    int _4689 = (probeCoord_1.y * Globals.uProbeWidth) + probeCoord_1.x;
                                                    position = uProbePos._member0[_4689].xyz;
                                                    _4647 = uProbePos._member0[_4689].w >= 0.5;
                                                }
                                            }
                                        }
                                        if (!(!_4647))
                                        {
                                            vec3 _4700 = world - position;
                                            if ((abs(dot(_4700, worldNormal)) / (length(_4700) + 0.00999999977648258209228515625)) < 0.257759988307952880859375)
                                            {
                                                _4697 = 1.0;
                                            }
                                            else
                                            {
                                                _4697 = 0.0;
                                            }
                                            if (!(_4697 <= 0.0))
                                            {
                                                vec2 _1840 = mix(vec2(1.0) - _1705, _1705, vec2(_1714_comp[light]));
                                                float w = _1840.x * _1840.y;
                                                int _4711 = 0;
                                                vec3 _4712 = textureLod(_6820, (vec2(probeCoord_1 * ivec2(4)) + vec2(0.5)) / Globals.uAtlasSize.xy, 0.0).xyz * 2.0;
                                                float _4713 = 2.0;
                                                for (;;)
                                                {
                                                    bool _4722_ladder_break = false;
                                                    {
                                                        if (!(_4711 < 4))
                                                        {
                                                            _4722_ladder_break = true;
                                                        }
                                                        else
                                                        {
                                                            ivec2 _4731 = probeCoord_1 + _1905_comp[_4711];
                                                            {
                                                                _4715 = vec3(0.0);
                                                                if (any(lessThan(_4731, ivec2(0))))
                                                                {
                                                                    _4775 = true;
                                                                }
                                                                else
                                                                {
                                                                    _4775 = _4731.x >= Globals.uProbeWidth;
                                                                }
                                                                if (_4775)
                                                                {
                                                                    _4775 = true;
                                                                }
                                                                else
                                                                {
                                                                    _4775 = _4731.y >= Globals.uProbeHeight;
                                                                }
                                                                if (_4775)
                                                                {
                                                                    _4774 = false;
                                                                }
                                                                else
                                                                {
                                                                    ivec2 _4800 = _4731 * ivec2(4);
                                                                    if (any(lessThan(_4800, pageAt)))
                                                                    {
                                                                        _4775 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _4775 = any(greaterThanEqual(_4800, _1694));
                                                                    }
                                                                    if (_4775)
                                                                    {
                                                                        _4774 = false;
                                                                    }
                                                                    else
                                                                    {
                                                                        int _4816 = (_4731.y * Globals.uProbeWidth) + _4731.x;
                                                                        _4715 = uProbePos._member0[_4816].xyz;
                                                                        _4774 = uProbePos._member0[_4816].w >= 0.5;
                                                                    }
                                                                }
                                                            }
                                                            if (!(!_4774))
                                                            {
                                                                vec3 _4827 = world - _4715;
                                                                if ((abs(dot(_4827, worldNormal)) / (length(_4827) + 0.00999999977648258209228515625)) < 0.257759988307952880859375)
                                                                {
                                                                    _4824 = 1.0;
                                                                }
                                                                else
                                                                {
                                                                    _4824 = 0.0;
                                                                }
                                                                if (!(_4824 <= 0.0))
                                                                {
                                                                    _4712 += textureLod(_6820, (vec2(_4731 * ivec2(4)) + vec2(0.5)) / Globals.uAtlasSize.xy, 0.0).xyz;
                                                                    _4713 += 1.0;
                                                                }
                                                            }
                                                        }
                                                    }
                                                    if (_4722_ladder_break)
                                                    {
                                                        break;
                                                    }
                                                    _4711++;
                                                    continue;
                                                }
                                                lit += ((_4712 / vec3(_4713)) * w);
                                                visibility += w;
                                            }
                                        }
                                    }
                                }
                                if (_59_ladder_break)
                                {
                                    break;
                                }
                                light++;
                                continue;
                            }
                            vec3 indirect;
                            if (visibility > 0.0)
                            {
                                indirect = lit / vec3(visibility);
                            }
                            else
                            {
                                indirect = textureLod(_6820, (vec2(probe00 * ivec2(4)) + vec2(0.5)) / Globals.uAtlasSize.xy, 0.0).xyz;
                            }
                            vec3 _2041 = _3738.xyz;
                            vec3 _4873;
                            {
                                if (Globals.uBoost >= 1.0)
                                {
                                    _4873 = _2041;
                                }
                                else
                                {
                                    float _4880 = dot(_2041, vec3(0.300000011920928955078125, 0.589999973773956298828125, 0.10999999940395355224609375));
                                    _4873 = min(clamp(_2041 * (pow(max(_4880, 0.0), Globals.uBoost) / max(_4880, 9.9999999392252902907785028219223e-09)), vec3(0.0), vec3(1.0)), vec3(0.9900000095367431640625));
                                }
                            }
                            imageStore(uDirect, ivec2(uvec2(texelAt)), vec4((texelFetch(_6818, ivec3(texelAt, 0).xy, 0).xyz + indirect) * (_4873 * vec3(0.3183098733425140380859375)), 1.0));
                        }
                        else
                        {
                            if (Globals.uMode == 3)
                            {
                                vec3 _2099 = _3738.xyz;
                                vec3 _4894;
                                {
                                    if (Globals.uBoost >= 1.0)
                                    {
                                        _4894 = _2099;
                                    }
                                    else
                                    {
                                        float _4901 = dot(_2099, vec3(0.300000011920928955078125, 0.589999973773956298828125, 0.10999999940395355224609375));
                                        _4894 = min(clamp(_2099 * (pow(max(_4901, 0.0), Globals.uBoost) / max(_4901, 9.9999999392252902907785028219223e-09)), vec3(0.0), vec3(1.0)), vec3(0.9900000095367431640625));
                                    }
                                }
                                imageStore(uDirect, ivec2(uvec2(texelAt)), vec4(texelFetch(_6818, ivec3(texelAt, 0).xy, 0).xyz * (_4894 * vec3(0.3183098733425140380859375)), 1.0));
                            }
                            else
                            {
                                if (Globals.uProbe != 0)
                                {
                                    vec4 _4915;
                                    {
                                        if (Globals.uProbe == 5)
                                        {
                                            if (((texelAt.x | texelAt.y) & 7) != 0)
                                            {
                                                _4915 = vec4(0.0);
                                            }
                                            else
                                            {
                                                vec2 _4916;
                                                bool _5038;
                                                {
                                                    _4916 = vec2(0.0);
                                                    float _4918 = 1000000000.0;
                                                    if (Globals.uLookupBuckets <= 0)
                                                    {
                                                        _5038 = false;
                                                    }
                                                    else
                                                    {
                                                        ivec3 _5079 = ivec3(floor((world - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                                                        int _5291 = int((((uint(_5079.x) * 2376512323u) ^ (uint(_5079.y) * 3625334849u)) ^ (uint(_5079.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                                                        uvec2 _5084 = uvec2(textureSize(_6805, 0));
                                                        vec2 _5091 = vec2(float(_5084.x), float(_5084.y));
                                                        bool _5041 = false;
                                                        bool _5042 = false;
                                                        int _5043 = 0;
                                                        bool _5044;
                                                        ivec2 _5045;
                                                        bool _5049;
                                                        bool _5058;
                                                        bool _5059;
                                                        bool _5060;
                                                        bool _5061;
                                                        bool _5062;
                                                        ivec2 _5292;
                                                        for (;;)
                                                        {
                                                            if (_5043 <= uCells_1._member0[Globals.uLookupBuckets].y)
                                                            {
                                                                _5044 = !_5041;
                                                            }
                                                            else
                                                            {
                                                                _5044 = false;
                                                            }
                                                            if (!_5044)
                                                            {
                                                                break;
                                                            }
                                                            bool _5111 = _5043 == 0;
                                                            if (_5111)
                                                            {
                                                                _5045 = uCells_1._member0[_5291];
                                                            }
                                                            else
                                                            {
                                                                int _5296 = uCells_1._member0[Globals.uLookupBuckets].x + ((_5043 - 1) * 6);
                                                                float _5312 = intBitsToFloat(uCellPages_1._member0[_5296 + 3]);
                                                                vec3 _5314 = world - vec3(intBitsToFloat(uCellPages_1._member0[_5296]), intBitsToFloat(uCellPages_1._member0[_5296 + 1]), intBitsToFloat(uCellPages_1._member0[_5296 + 2]));
                                                                if (dot(_5314, _5314) > (_5312 * _5312))
                                                                {
                                                                    _5292 = ivec2(0);
                                                                }
                                                                else
                                                                {
                                                                    _5292 = ivec2(uCellPages_1._member0[_5296 + 4], uCellPages_1._member0[_5296 + 5]);
                                                                }
                                                                _5045 = _5292;
                                                            }
                                                            bool _5046 = _5041;
                                                            bool _5047 = _5042;
                                                            int _5048 = 0;
                                                            for (;;)
                                                            {
                                                                bool _5123_ladder_break = false;
                                                                {
                                                                    if (_5048 < _5045.y)
                                                                    {
                                                                        _5049 = !_5046;
                                                                    }
                                                                    else
                                                                    {
                                                                        _5049 = false;
                                                                    }
                                                                    if (!_5049)
                                                                    {
                                                                        _5123_ladder_break = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        int _5140 = _5045.x + _5048;
                                                                        int _5143 = uCellPages_1._member0[_5140] * 10;
                                                                        if (!((uPages_1._member0[_5143 + 5].w > 0.5) == _5111))
                                                                        {
                                                                            int _5152 = _5143 + 9;
                                                                            vec3 _5156 = world - uPages_1._member0[_5152].xyz;
                                                                            if (!(dot(_5156, _5156) > (uPages_1._member0[_5152].w * uPages_1._member0[_5152].w)))
                                                                            {
                                                                                int _5330 = uCellPages_1._member0[_5140] * 10;
                                                                                vec4 _5057 = uPages_1._member0[_5330 + 1];
                                                                                int _5336 = _5330 + 2;
                                                                                int _5339 = _5330 + 3;
                                                                                int _5342 = _5330 + 4;
                                                                                int _5348 = _5330 + 6;
                                                                                int _5351 = _5330 + 7;
                                                                                int _5354 = _5330 + 8;
                                                                                vec3 _5361 = world - vec3(uPages_1._member0[_5348].w, uPages_1._member0[_5351].w, uPages_1._member0[_5354].w);
                                                                                vec3 _5055 = vec3(dot(uPages_1._member0[_5348].xyz, vec3(-0.0)), dot(uPages_1._member0[_5351].xyz, vec3(-0.0)), dot(uPages_1._member0[_5354].xyz, vec3(-0.0)));
                                                                                vec3 _5374 = vec3(dot(uPages_1._member0[_5348].xyz, _5361), dot(uPages_1._member0[_5351].xyz, _5361), dot(uPages_1._member0[_5354].xyz, _5361)) - uPages_1._member0[_5336].xyz;
                                                                                vec2 _5384 = vec2(dot(_5374, uPages_1._member0[_5339].xyz), dot(_5374, uPages_1._member0[_5342].xyz)) / max(vec2(uPages_1._member0[_5336].w, uPages_1._member0[_5339].w), vec2(9.9999999747524270787835121154785e-07));
                                                                                vec2 _5050 = _5384;
                                                                                float _5052 = -dot(_5374, uPages_1._member0[_5330 + 5].xyz);
                                                                                float _5053 = uPages_1._member0[_5342].w;
                                                                                vec3 _5054 = uPages_1._member0[_5330 + 5].xyz;
                                                                                if (abs(_5384.x) > 1.0)
                                                                                {
                                                                                    _5058 = true;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _5058 = abs(_5050.y) > 1.0;
                                                                                }
                                                                                if (!(_5058))
                                                                                {
                                                                                    if (false)
                                                                                    {
                                                                                        _5059 = dot(_5054, _5055) < (-0.0500000007450580596923828125);
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _5059 = false;
                                                                                    }
                                                                                    if (!(_5059))
                                                                                    {
                                                                                        if (_5052 < (-0.25))
                                                                                        {
                                                                                            _5060 = true;
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            _5060 = _5052 > (_5053 + 0.25);
                                                                                        }
                                                                                        if (!(_5060))
                                                                                        {
                                                                                            vec2 _5214 = (_5057.xy + (((_5050 * 0.5) + vec2(0.5)) * _5057.zw)) / _5091;
                                                                                            if (!(textureLod(_6807, _5214, 0.0).w < 0.5))
                                                                                            {
                                                                                                float _5234 = abs((textureLod(_6811, _5214, 0.0).x * _5053) - _5052);
                                                                                                if (_5234 < _4918)
                                                                                                {
                                                                                                    _4918 = _5234;
                                                                                                    _4916 = _5214;
                                                                                                    _5061 = _5234 < 0.0199999995529651641845703125;
                                                                                                    _5062 = true;
                                                                                                }
                                                                                                else
                                                                                                {
                                                                                                    _5061 = _5046;
                                                                                                    _5062 = _5047;
                                                                                                }
                                                                                                _5046 = _5061;
                                                                                                _5047 = _5062;
                                                                                            }
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                                if (_5123_ladder_break)
                                                                {
                                                                    break;
                                                                }
                                                                _5048++;
                                                                continue;
                                                            }
                                                            _5041 = _5046;
                                                            _5042 = _5047;
                                                            _5043++;
                                                            continue;
                                                        }
                                                        _5038 = _5042;
                                                    }
                                                }
                                                if (!_5038)
                                                {
                                                    _4915 = vec4(1.0, 0.0, 0.0, 1.0);
                                                }
                                                else
                                                {
                                                    uvec2 _4947 = uvec2(textureSize(_6805, 0));
                                                    vec2 _4958 = abs(_4916 - cardUv) * vec2(float(_4947.x), float(_4947.y));
                                                    vec4 _4921;
                                                    if (max(_4958.x, _4958.y) < 1.5)
                                                    {
                                                        _4921 = vec4(0.0, 1.0, 0.0, 1.0);
                                                    }
                                                    else
                                                    {
                                                        _4921 = vec4(1.0, 1.0, 0.0, 1.0);
                                                    }
                                                    _4915 = _4921;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (Globals.uProbe == 4)
                                            {
                                                _5389 = false;
                                                vec4 _5390;
                                                {
                                                    int _5409 = max((Globals.uShadowLevels - 1), 0);
                                                    vec3 _5415 = world + ((worldNormal * Globals.uClipmapOrigin.data[0].w) * 1.5);
                                                    float _5424 = (Globals.uClipmapOrigin.data[0].w * float(Globals.uShadowResolution)) * float(1 << (Globals.uShadowLevels - 1));
                                                    int _5391 = 0;
                                                    float _5392 = 0.0;
                                                    bool _5393;
                                                    bool _5394;
                                                    float _5396;
                                                    float _5482;
                                                    bool _5484;
                                                    for (;;)
                                                    {
                                                        if (_5391 < 64)
                                                        {
                                                            _5393 = _5392 < _5424;
                                                        }
                                                        else
                                                        {
                                                            _5393 = false;
                                                        }
                                                        if (!_5393)
                                                        {
                                                            break;
                                                        }
                                                        vec3 _5443 = _5415 + (Globals.uSunDirection * _5392);
                                                        _5481 = false;
                                                        {
                                                            _5394 = false;
                                                            _5396 = Globals.uClipmapOrigin.data[0].w;
                                                            int _5483 = 0;
                                                            for (;;)
                                                            {
                                                                bool _5495_ladder_break = false;
                                                                {
                                                                    if (!(_5483 < Globals.uShadowLevels))
                                                                    {
                                                                        _5495_ladder_break = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        vec3 _5510 = (_5443 - Globals.uClipmapOrigin.data[_5483].xyz) / vec3(Globals.uClipmapOrigin.data[_5483].w);
                                                                        if (any(lessThan(_5510, vec3(0.5))))
                                                                        {
                                                                            _5484 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _5484 = any(greaterThan(_5510, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                        }
                                                                        if (!(_5484))
                                                                        {
                                                                            _5394 = true;
                                                                            _5396 = Globals.uClipmapOrigin.data[_5483].w;
                                                                            _5481 = true;
                                                                            _5482 = ((textureLod(_6813, vec3(_5510.x / float(Globals.uShadowResolution), _5510.y / float(Globals.uShadowResolution), ((float(_5483) * float(Globals.uShadowResolution)) + _5510.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_5483];
                                                                            _5495_ladder_break = true;
                                                                        }
                                                                    }
                                                                }
                                                                if (_5495_ladder_break)
                                                                {
                                                                    break;
                                                                }
                                                                _5483++;
                                                                continue;
                                                            }
                                                            if (!(_5481))
                                                            {
                                                                _5481 = true;
                                                                _5482 = 1000000.0;
                                                            }
                                                        }
                                                        if (!_5394)
                                                        {
                                                            _5389 = true;
                                                            _5390 = vec4(1.0, 0.0, _5392 / _5424, 1.0);
                                                            break;
                                                        }
                                                        if (_5482 < ((0.5 * _5396) * clamp(_5392 / max(2.0 * _5396, 9.9999999747524270787835121154785e-07), 0.0, 1.0)))
                                                        {
                                                            _5389 = true;
                                                            _5390 = vec4(0.0, _5396 / Globals.uClipmapOrigin.data[_5409].w, _5392 / _5424, 1.0);
                                                            break;
                                                        }
                                                        _5391++;
                                                        _5392 += max(_5482, _5396 * 0.25);
                                                        continue;
                                                    }
                                                    if (!(_5389))
                                                    {
                                                        _5389 = true;
                                                        _5390 = vec4(0.5, 0.0, clamp(_5392 / _5424, 0.0, 1.0), 1.0);
                                                    }
                                                }
                                                _4915 = _5390;
                                            }
                                            else
                                            {
                                                if (Globals.uProbe == 2)
                                                {
                                                    _4915 = vec4(clamp((world - Globals.uProbeMin) * Globals.uProbeInvExtent, vec3(0.0), vec3(1.0)), 1.0);
                                                }
                                                else
                                                {
                                                    if (Globals.uProbe == 3)
                                                    {
                                                        _4915 = vec4((worldNormal * 0.5) + vec3(0.5), 1.0);
                                                    }
                                                    else
                                                    {
                                                        vec3 _5000 = world + ((worldNormal * Globals.uClipmapOrigin.data[0].w) * 1.5);
                                                        _5591 = false;
                                                        bool _4922;
                                                        float _4923;
                                                        float _5592;
                                                        {
                                                            _4922 = false;
                                                            _4923 = 0.0;
                                                            int _5593 = 0;
                                                            bool _5594;
                                                            for (;;)
                                                            {
                                                                bool _5605_ladder_break = false;
                                                                {
                                                                    if (!(_5593 < Globals.uShadowLevels))
                                                                    {
                                                                        _5605_ladder_break = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        vec3 _5620 = (_5000 - Globals.uClipmapOrigin.data[_5593].xyz) / vec3(Globals.uClipmapOrigin.data[_5593].w);
                                                                        if (any(lessThan(_5620, vec3(0.5))))
                                                                        {
                                                                            _5594 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _5594 = any(greaterThan(_5620, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                        }
                                                                        if (!(_5594))
                                                                        {
                                                                            _4922 = true;
                                                                            vec4 _5667 = textureLod(_6813, vec3(_5620.x / float(Globals.uShadowResolution), _5620.y / float(Globals.uShadowResolution), ((float(_5593) * float(Globals.uShadowResolution)) + _5620.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0);
                                                                            float _5669 = _5667.x;
                                                                            _4923 = _5669;
                                                                            _5591 = true;
                                                                            _5592 = ((_5669 * 2.0) - 1.0) * Globals.uClipmapRange.data[_5593];
                                                                            _5605_ladder_break = true;
                                                                        }
                                                                    }
                                                                }
                                                                if (_5605_ladder_break)
                                                                {
                                                                    break;
                                                                }
                                                                _5593++;
                                                                continue;
                                                            }
                                                            if (!(_5591))
                                                            {
                                                                _5591 = true;
                                                                _5592 = 1000000.0;
                                                            }
                                                        }
                                                        float _4925;
                                                        if (Globals.uShadowLevels > 0)
                                                        {
                                                            _4925 = clamp(Globals.uClipmapRange.data[0] / (Globals.uClipmapOrigin.data[0].w * 4.0), 0.0, 1.0);
                                                        }
                                                        else
                                                        {
                                                            _4925 = 0.0;
                                                        }
                                                        float _4926;
                                                        if (_4922)
                                                        {
                                                            _4926 = clamp(_5592 / (Globals.uClipmapOrigin.data[0].w * 4.0), 0.0, 1.0);
                                                        }
                                                        else
                                                        {
                                                            _4926 = 1.0;
                                                        }
                                                        float _4927;
                                                        if (_4922)
                                                        {
                                                            _4927 = 1.0;
                                                        }
                                                        else
                                                        {
                                                            _4927 = 0.5;
                                                        }
                                                        _4915 = vec4(_4925, _4923, _4926, _4927);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    imageStore(uDirect, ivec2(uvec2(texelAt)), _4915);
                                }
                                else
                                {
                                    float _2656 = max(dot(worldNormal, Globals.uSunDirection), 0.0);
                                    if (_2656 > 0.0)
                                    {
                                        _5698 = false;
                                        float _5699;
                                        {
                                            if (Globals.uShadowLevels <= 0)
                                            {
                                                _5698 = true;
                                                _5699 = 1.0;
                                            }
                                            else
                                            {
                                                vec3 _5722 = world + ((worldNormal * Globals.uClipmapOrigin.data[0].w) * 1.5);
                                                float _5733 = (Globals.uClipmapOrigin.data[0].w * float(Globals.uShadowResolution)) * float(1 << (Globals.uShadowLevels - 1));
                                                int _5700 = 0;
                                                float _5701 = 0.0;
                                                bool _5702;
                                                bool _5703;
                                                float _5705;
                                                float _5780;
                                                bool _5782;
                                                for (;;)
                                                {
                                                    if (_5700 < 64)
                                                    {
                                                        _5702 = _5701 < _5733;
                                                    }
                                                    else
                                                    {
                                                        _5702 = false;
                                                    }
                                                    if (!_5702)
                                                    {
                                                        break;
                                                    }
                                                    vec3 _5750 = _5722 + (Globals.uSunDirection * _5701);
                                                    _5779 = false;
                                                    {
                                                        _5703 = false;
                                                        _5705 = Globals.uClipmapOrigin.data[0].w;
                                                        int _5781 = 0;
                                                        for (;;)
                                                        {
                                                            bool _5793_ladder_break = false;
                                                            {
                                                                if (!(_5781 < Globals.uShadowLevels))
                                                                {
                                                                    _5793_ladder_break = true;
                                                                }
                                                                else
                                                                {
                                                                    vec3 _5808 = (_5750 - Globals.uClipmapOrigin.data[_5781].xyz) / vec3(Globals.uClipmapOrigin.data[_5781].w);
                                                                    if (any(lessThan(_5808, vec3(0.5))))
                                                                    {
                                                                        _5782 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _5782 = any(greaterThan(_5808, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                    }
                                                                    if (!(_5782))
                                                                    {
                                                                        _5703 = true;
                                                                        _5705 = Globals.uClipmapOrigin.data[_5781].w;
                                                                        _5779 = true;
                                                                        _5780 = ((textureLod(_6813, vec3(_5808.x / float(Globals.uShadowResolution), _5808.y / float(Globals.uShadowResolution), ((float(_5781) * float(Globals.uShadowResolution)) + _5808.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_5781];
                                                                        _5793_ladder_break = true;
                                                                    }
                                                                }
                                                            }
                                                            if (_5793_ladder_break)
                                                            {
                                                                break;
                                                            }
                                                            _5781++;
                                                            continue;
                                                        }
                                                        if (!(_5779))
                                                        {
                                                            _5779 = true;
                                                            _5780 = 1000000.0;
                                                        }
                                                    }
                                                    if (!_5703)
                                                    {
                                                        _5698 = true;
                                                        _5699 = 1.0;
                                                        break;
                                                    }
                                                    if (_5780 < ((0.5 * _5705) * clamp(_5701 / max(2.0 * _5705, 9.9999999747524270787835121154785e-07), 0.0, 1.0)))
                                                    {
                                                        _5698 = true;
                                                        _5699 = 0.0;
                                                        break;
                                                    }
                                                    _5700++;
                                                    _5701 += max(_5780, _5705 * 0.25);
                                                    continue;
                                                }
                                                if (!(_5698))
                                                {
                                                    _5698 = true;
                                                    _5699 = 1.0;
                                                }
                                            }
                                        }
                                        visibility = _5699;
                                    }
                                    else
                                    {
                                        visibility = 1.0;
                                    }
                                    vec3 _5890;
                                    {
                                        if (Globals.uShadowLevels <= 0)
                                        {
                                            vec3 _5971;
                                            {
                                                if (Globals.uSkyShValid == 0)
                                                {
                                                    _5971 = Globals.uSkyColour * ((worldNormal.z * 0.5) + 0.5);
                                                }
                                                else
                                                {
                                                    vec4 _5986 = vec4(worldNormal, 1.0);
                                                    vec4 _5989 = _5986.xyzz * _5986.yzzx;
                                                    _5971 = max(vec3(0.0), (vec3(dot(Globals.uSkySh.data[0], _5986), dot(Globals.uSkySh.data[1], _5986), dot(Globals.uSkySh.data[2], _5986)) + vec3(dot(Globals.uSkySh.data[3], _5989), dot(Globals.uSkySh.data[4], _5989), dot(Globals.uSkySh.data[5], _5989))) + (Globals.uSkySh.data[6].xyz * ((worldNormal.x * worldNormal.x) - (worldNormal.y * worldNormal.y))));
                                                }
                                            }
                                            _5890 = _5971 * 3.1415927410125732421875;
                                        }
                                        else
                                        {
                                            vec3 _5891;
                                            if (abs(worldNormal.z) < 0.89999997615814208984375)
                                            {
                                                _5891 = vec3(0.0, 0.0, 1.0);
                                            }
                                            else
                                            {
                                                _5891 = vec3(1.0, 0.0, 0.0);
                                            }
                                            vec3 _5912 = normalize(cross(worldNormal, _5891));
                                            vec3 _5913 = cross(worldNormal, _5912);
                                            vec3 _5915 = floor(world * 8.0);
                                            float _5919 = fract(sin(dot(_5915, vec3(12.98980045318603515625, 78.233001708984375, 37.71900177001953125))) * 43758.546875);
                                            float _5923 = fract(sin(dot(_5915, vec3(39.34600067138671875, 11.1350002288818359375, 83.154998779296875))) * 24634.634765625);
                                            int _5893 = 0;
                                            vec3 _5892 = vec3(0.0);
                                            vec3 _6055;
                                            float _6115;
                                            bool _6118;
                                            bool _6119;
                                            float _6121;
                                            float _6196;
                                            bool _6198;
                                            for (;;)
                                            {
                                                if (!(_5893 < 8))
                                                {
                                                    break;
                                                }
                                                float _5935 = (float(_5893) + _5923) * 0.125;
                                                uint _6028 = uint(_5893);
                                                uint _6031 = (_6028 << 16u) | (_6028 >> 16u);
                                                uint _6036 = ((_6031 & 1431655765u) << 1u) | ((_6031 & 2863311530u) >> 1u);
                                                uint _6041 = ((_6036 & 858993459u) << 2u) | ((_6036 & 3435973836u) >> 2u);
                                                uint _6046 = ((_6041 & 252645135u) << 4u) | ((_6041 & 4042322160u) >> 4u);
                                                float _5940 = sqrt(_5935);
                                                float _5941 = 6.283185482025146484375 * fract((float(((_6046 & 16711935u) << 8u) | ((_6046 & 4278255360u) >> 8u)) * 2.3283064365386962890625e-10) + _5919);
                                                vec3 _5954 = normalize(((_5912 * (_5940 * cos(_5941))) + (_5913 * (_5940 * sin(_5941)))) + (worldNormal * sqrt(max(0.0, 1.0 - _5935))));
                                                {
                                                    if (Globals.uSkyShValid == 0)
                                                    {
                                                        _6055 = Globals.uSkyColour * ((_5954.z * 0.5) + 0.5);
                                                    }
                                                    else
                                                    {
                                                        vec4 _6070 = vec4(_5954, 1.0);
                                                        vec4 _6073 = _6070.xyzz * _6070.yzzx;
                                                        float _6099 = _5954.x;
                                                        float _6101 = _5954.y;
                                                        _6055 = max(vec3(0.0), (vec3(dot(Globals.uSkySh.data[0], _6070), dot(Globals.uSkySh.data[1], _6070), dot(Globals.uSkySh.data[2], _6070)) + vec3(dot(Globals.uSkySh.data[3], _6073), dot(Globals.uSkySh.data[4], _6073), dot(Globals.uSkySh.data[5], _6073))) + (Globals.uSkySh.data[6].xyz * ((_6099 * _6099) - (_6101 * _6101))));
                                                    }
                                                }
                                                _6114 = false;
                                                {
                                                    if (Globals.uShadowLevels <= 0)
                                                    {
                                                        _6114 = true;
                                                        _6115 = 1.0;
                                                    }
                                                    else
                                                    {
                                                        vec3 _6138 = world + ((worldNormal * Globals.uClipmapOrigin.data[0].w) * 1.5);
                                                        float _6149 = (Globals.uClipmapOrigin.data[0].w * float(Globals.uShadowResolution)) * float(1 << (Globals.uShadowLevels - 1));
                                                        int _6116 = 0;
                                                        float _6117 = 0.0;
                                                        for (;;)
                                                        {
                                                            if (_6116 < 64)
                                                            {
                                                                _6118 = _6117 < _6149;
                                                            }
                                                            else
                                                            {
                                                                _6118 = false;
                                                            }
                                                            if (!_6118)
                                                            {
                                                                break;
                                                            }
                                                            vec3 _6166 = _6138 + (_5954 * _6117);
                                                            _6195 = false;
                                                            {
                                                                _6119 = false;
                                                                _6121 = Globals.uClipmapOrigin.data[0].w;
                                                                int _6197 = 0;
                                                                for (;;)
                                                                {
                                                                    bool _6209_ladder_break = false;
                                                                    {
                                                                        if (!(_6197 < Globals.uShadowLevels))
                                                                        {
                                                                            _6209_ladder_break = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            vec3 _6224 = (_6166 - Globals.uClipmapOrigin.data[_6197].xyz) / vec3(Globals.uClipmapOrigin.data[_6197].w);
                                                                            if (any(lessThan(_6224, vec3(0.5))))
                                                                            {
                                                                                _6198 = true;
                                                                            }
                                                                            else
                                                                            {
                                                                                _6198 = any(greaterThan(_6224, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                            }
                                                                            if (!(_6198))
                                                                            {
                                                                                _6119 = true;
                                                                                _6121 = Globals.uClipmapOrigin.data[_6197].w;
                                                                                _6195 = true;
                                                                                _6196 = ((textureLod(_6813, vec3(_6224.x / float(Globals.uShadowResolution), _6224.y / float(Globals.uShadowResolution), ((float(_6197) * float(Globals.uShadowResolution)) + _6224.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_6197];
                                                                                _6209_ladder_break = true;
                                                                            }
                                                                        }
                                                                    }
                                                                    if (_6209_ladder_break)
                                                                    {
                                                                        break;
                                                                    }
                                                                    _6197++;
                                                                    continue;
                                                                }
                                                                if (!(_6195))
                                                                {
                                                                    _6195 = true;
                                                                    _6196 = 1000000.0;
                                                                }
                                                            }
                                                            if (!_6119)
                                                            {
                                                                _6114 = true;
                                                                _6115 = 1.0;
                                                                break;
                                                            }
                                                            if (_6196 < ((0.5 * _6121) * clamp(_6117 / max(2.0 * _6121, 9.9999999747524270787835121154785e-07), 0.0, 1.0)))
                                                            {
                                                                _6114 = true;
                                                                _6115 = 0.0;
                                                                break;
                                                            }
                                                            _6116++;
                                                            _6117 += max(_6196, _6121 * 0.25);
                                                            continue;
                                                        }
                                                        if (!(_6114))
                                                        {
                                                            _6114 = true;
                                                            _6115 = 1.0;
                                                        }
                                                    }
                                                }
                                                _5893++;
                                                _5892 += (_6055 * _6115);
                                                continue;
                                            }
                                            _5890 = _5892 * 0.3926990926265716552734375;
                                        }
                                    }
                                    light = 0;
                                    lit = ((Globals.uSunColour * _2656) * visibility) + _5890;
                                    float aim;
                                    float reaches;
                                    float _6306;
                                    float _6557;
                                    bool _6560;
                                    bool _6561;
                                    float _6563;
                                    float _6633;
                                    bool _6635;
                                    for (;;)
                                    {
                                        bool _84_ladder_break = false;
                                        {
                                            if (!(light < Globals.uLightCount))
                                            {
                                                _84_ladder_break = true;
                                            }
                                            else
                                            {
                                                int _2972 = light * 3;
                                                int _2976 = _2972 + 1;
                                                int _2979 = _2972 + 2;
                                                vec3 delta = uLights_1._member0[_2972].xyz - world;
                                                float _2985 = dot(delta, delta);
                                                float _2989 = max(uLights_1._member0[_2972].w * 3.0, 2.0);
                                                float reachSquared = _2989 * _2989;
                                                if (!(_2985 >= reachSquared))
                                                {
                                                    vec3 toLight = delta * inversesqrt(max(_2985, 9.9999999747524270787835121154785e-07));
                                                    float _2999 = max(dot(worldNormal, toLight), 0.0);
                                                    if (!(_2999 <= 0.0))
                                                    {
                                                        float _3009 = clamp(1.0 - ((_2985 * _2985) / (reachSquared * reachSquared)), 0.0, 1.0);
                                                        if (dot(uLights_1._member0[_2979].xyz, uLights_1._member0[_2979].xyz) > 0.25)
                                                        {
                                                            aim = max(dot(normalize(uLights_1._member0[_2979].xyz), -toLight), 0.0);
                                                        }
                                                        else
                                                        {
                                                            aim = 1.0;
                                                        }
                                                        float contribution = (((((uLights_1._member0[_2976].w * Globals.uLocalScale) * _2999) * (1.0 / (_2985 + 1.0))) * _3009) * _3009) * aim;
                                                        if (!((contribution * dot(uLights_1._member0[_2976].xyz, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875))) < 9.9999997473787516355514526367188e-06))
                                                        {
                                                            int _3045 = floatBitsToInt(uLights_1._member0[_2979].w);
                                                            {
                                                                if (_3045 < 0)
                                                                {
                                                                    _6306 = -1.0;
                                                                }
                                                                else
                                                                {
                                                                    uint _6307 = uvec3(textureSize(_6823, 0)).x;
                                                                    float _6310 = 0.25;
                                                                    int _6311 = 0;
                                                                    for (;;)
                                                                    {
                                                                        if (!(_6311 < 6))
                                                                        {
                                                                            break;
                                                                        }
                                                                        int _6344 = ((_3045 + _6311) * 5) + 3;
                                                                        _6310 = max(_6310, dot(uLampMapRecords_1._member0[_6344].xyz, world) + uLampMapRecords_1._member0[_6344].w);
                                                                        _6311++;
                                                                        continue;
                                                                    }
                                                                    float _6359 = _6310;
                                                                    vec3 _6368 = world + (worldNormal * (0.0500000007450580596923828125 + (1.5 * ((_6359 * 2.0) / float(max(_6307, 1u))))));
                                                                    float _6312 = -1000000015047466219876688855040.0;
                                                                    int _6313 = _3045;
                                                                    _6311 = 0;
                                                                    for (;;)
                                                                    {
                                                                        if (!(_6311 < 6))
                                                                        {
                                                                            break;
                                                                        }
                                                                        int _6378 = _3045 + _6311;
                                                                        int _6380 = (_6378 * 5) + 3;
                                                                        float _6386 = dot(uLampMapRecords_1._member0[_6380].xyz, _6368) + uLampMapRecords_1._member0[_6380].w;
                                                                        if (_6386 > _6312)
                                                                        {
                                                                            _6312 = _6386;
                                                                            _6313 = _6378;
                                                                        }
                                                                        _6311++;
                                                                        continue;
                                                                    }
                                                                    int _6398 = _6313 * 5;
                                                                    int _6399 = _6398 + 4;
                                                                    if (uLampMapRecords_1._member0[_6399].z <= 0.0)
                                                                    {
                                                                        _6306 = -1.0;
                                                                    }
                                                                    else
                                                                    {
                                                                        int _6408 = _6398 + 1;
                                                                        if (_6312 <= (uLampMapRecords_1._member0[_6399].y / ((-1.0) - uLampMapRecords_1._member0[_6399].x)))
                                                                        {
                                                                            _6306 = 1.0;
                                                                        }
                                                                        else
                                                                        {
                                                                            vec2 _6430 = vec2(dot(uLampMapRecords_1._member0[_6398].xyz, _6368) + uLampMapRecords_1._member0[_6398].w, dot(uLampMapRecords_1._member0[_6408].xyz, _6368) + uLampMapRecords_1._member0[_6408].w) / vec2(_6312);
                                                                            uvec3 _6439 = uvec3(textureSize(_6823, 0));
                                                                            uint _6440 = _6439.x;
                                                                            ivec2 _6447 = ivec2(int(_6440), int(_6439.y));
                                                                            float _6461 = (0.0500000007450580596923828125 + (0.004999999888241291046142578125 * _6312)) + (2.0 * ((_6312 * (2.0 / max(length(uLampMapRecords_1._member0[_6398].xyz), 0.001000000047497451305389404296875))) / float(max(_6440, 1u))));
                                                                            vec2 _6465 = (vec2((_6430.x * 0.5) + 0.5, 0.5 - (_6430.y * 0.5)) * vec2(_6447)) - vec2(0.5);
                                                                            vec2 _6466 = floor(_6465);
                                                                            ivec2 _6469 = ivec2(_6466) - ivec2(1);
                                                                            vec2 _6470 = _6465 - _6466;
                                                                            int _6319 = 0;
                                                                            float _6317 = 0.0;
                                                                            float _6318 = 0.0;
                                                                            for (;;)
                                                                            {
                                                                                if (!(_6319 < 4))
                                                                                {
                                                                                    break;
                                                                                }
                                                                                int _6321 = 0;
                                                                                float _6320 = _6318;
                                                                                for (;;)
                                                                                {
                                                                                    if (!(_6321 < 4))
                                                                                    {
                                                                                        break;
                                                                                    }
                                                                                    int _6489 = _6321;
                                                                                    vec2 _6555 = clamp(vec2(1.0) - (abs(vec2(float(_6489) - 1.0, float(_6319) - 1.0) - _6470) * vec2(0.5)), vec2(0.0), vec2(1.0));
                                                                                    float _6522 = _6555.x * _6555.y;
                                                                                    if ((uLampMapRecords_1._member0[_6399].y / min(((texelFetch(_6823, ivec4(clamp(_6469 + ivec2(_6489, _6319), ivec2(0), _6447 - ivec2(1)), _6313, 0).xyz, 0).x * 2.0) - 1.0) - uLampMapRecords_1._member0[_6399].x, -9.9999999747524270787835121154785e-07)) >= (_6312 - _6461))
                                                                                    {
                                                                                        _6310 = _6522;
                                                                                    }
                                                                                    else
                                                                                    {
                                                                                        _6310 = 0.0;
                                                                                    }
                                                                                    _6321++;
                                                                                    _6317 += _6310;
                                                                                    _6320 += _6522;
                                                                                    continue;
                                                                                }
                                                                                _6319++;
                                                                                _6318 = _6320;
                                                                                continue;
                                                                            }
                                                                            _6306 = _6317 / max(_6318, 9.9999997473787516355514526367188e-05);
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                            if (_6306 < 0.0)
                                                            {
                                                                _6556 = false;
                                                                {
                                                                    if (Globals.uShadowLevels <= 0)
                                                                    {
                                                                        _6556 = true;
                                                                        _6557 = 1.0;
                                                                    }
                                                                    else
                                                                    {
                                                                        vec3 _6580 = world + ((worldNormal * Globals.uClipmapOrigin.data[0].w) * 1.5);
                                                                        vec3 _6581 = uLights_1._member0[_2972].xyz - _6580;
                                                                        float _6582 = length(_6581);
                                                                        float _6584 = _6582 - max(0.119999997317790985107421875, Globals.uClipmapOrigin.data[0].w);
                                                                        if (_6584 <= 0.0)
                                                                        {
                                                                            _6556 = true;
                                                                            _6557 = 1.0;
                                                                        }
                                                                        else
                                                                        {
                                                                            vec3 _6589 = _6581 / vec3(_6582);
                                                                            int _6558 = 0;
                                                                            float _6559 = 0.0;
                                                                            for (;;)
                                                                            {
                                                                                if (_6558 < 48)
                                                                                {
                                                                                    _6560 = _6559 < _6584;
                                                                                }
                                                                                else
                                                                                {
                                                                                    _6560 = false;
                                                                                }
                                                                                if (!_6560)
                                                                                {
                                                                                    break;
                                                                                }
                                                                                vec3 _6606 = _6580 + (_6589 * _6559);
                                                                                _6632 = false;
                                                                                {
                                                                                    _6561 = false;
                                                                                    _6563 = Globals.uClipmapOrigin.data[0].w;
                                                                                    int _6634 = 0;
                                                                                    for (;;)
                                                                                    {
                                                                                        bool _6646_ladder_break = false;
                                                                                        {
                                                                                            if (!(_6634 < Globals.uShadowLevels))
                                                                                            {
                                                                                                _6646_ladder_break = true;
                                                                                            }
                                                                                            else
                                                                                            {
                                                                                                vec3 _6661 = (_6606 - Globals.uClipmapOrigin.data[_6634].xyz) / vec3(Globals.uClipmapOrigin.data[_6634].w);
                                                                                                if (any(lessThan(_6661, vec3(0.5))))
                                                                                                {
                                                                                                    _6635 = true;
                                                                                                }
                                                                                                else
                                                                                                {
                                                                                                    _6635 = any(greaterThan(_6661, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                                                                }
                                                                                                if (!(_6635))
                                                                                                {
                                                                                                    _6561 = true;
                                                                                                    _6563 = Globals.uClipmapOrigin.data[_6634].w;
                                                                                                    _6632 = true;
                                                                                                    _6633 = ((textureLod(_6813, vec3(_6661.x / float(Globals.uShadowResolution), _6661.y / float(Globals.uShadowResolution), ((float(_6634) * float(Globals.uShadowResolution)) + _6661.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_6634];
                                                                                                    _6646_ladder_break = true;
                                                                                                }
                                                                                            }
                                                                                        }
                                                                                        if (_6646_ladder_break)
                                                                                        {
                                                                                            break;
                                                                                        }
                                                                                        _6634++;
                                                                                        continue;
                                                                                    }
                                                                                    if (!(_6632))
                                                                                    {
                                                                                        _6632 = true;
                                                                                        _6633 = 1000000.0;
                                                                                    }
                                                                                }
                                                                                if (!_6561)
                                                                                {
                                                                                    _6556 = true;
                                                                                    _6557 = 1.0;
                                                                                    break;
                                                                                }
                                                                                if (_6633 < ((0.5 * _6563) * clamp(_6559 / max(2.0 * _6563, 9.9999999747524270787835121154785e-07), 0.0, 1.0)))
                                                                                {
                                                                                    _6556 = true;
                                                                                    _6557 = 0.0;
                                                                                    break;
                                                                                }
                                                                                _6558++;
                                                                                _6559 += max(_6633, _6563 * 0.25);
                                                                                continue;
                                                                            }
                                                                            if (!(_6556))
                                                                            {
                                                                                _6556 = true;
                                                                                _6557 = 1.0;
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                                reaches = _6557;
                                                            }
                                                            else
                                                            {
                                                                reaches = _6306;
                                                            }
                                                            if (!(reaches <= 0.0))
                                                            {
                                                                lit += (uLights_1._member0[_2976].xyz * (contribution * reaches));
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        if (_84_ladder_break)
                                        {
                                            break;
                                        }
                                        light++;
                                        continue;
                                    }
                                    imageStore(uDirect, ivec2(uvec2(texelAt)), vec4(lit, 1.0));
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

