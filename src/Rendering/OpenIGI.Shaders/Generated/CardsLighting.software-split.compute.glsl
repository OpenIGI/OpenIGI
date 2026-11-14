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

layout(binding = 5, std430) restrict readonly buffer StructuredBuffer
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

layout(binding = 2, std430) restrict buffer RWStructuredBuffer
{
    ivec2 _member0[];
} uCells;

layout(binding = 3, std430) restrict buffer uCellPages
{
    int _member0[];
} uCellPages_1;

layout(binding = 4, std430) restrict buffer uProbePos
{
    vec4 _member0[];
} uProbePos_1;

layout(binding = 0, rgba16f) uniform restrict image2D uDirect;
layout(binding = 0) uniform sampler2D _2797;
layout(binding = 1) uniform sampler2D _2799;
layout(binding = 2) uniform sampler2D _2801;
layout(binding = 3) uniform sampler2D _2803;
layout(binding = 4) uniform sampler3D _2805;
layout(binding = 5) uniform sampler2D _2807;

shared vec4 sBounceBasis[64];
shared vec3 sBounceRadiance[64];

void main()
{
    bool _2242 = false;
    bool _2161 = false;
    {
        ivec4 entry;
        int at;
        vec4 lightPage;
        vec4 cardPage;
        bool _1762;
        {
            entry = ivec4(0);
            at = 0;
            lightPage = vec4(0.0);
            cardPage = vec4(0.0);
            int _1775 = (int(gl_WorkGroupID.x) * max(Globals.uTileStride, 1)) + Globals.uTileOffset;
            int _1763;
            if (Globals.uUseActiveList != 0)
            {
                if (_1775 >= Globals.uActiveCount)
                {
                    _1762 = false;
                }
                else
                {
                    _1763 = uActiveTiles._member0[_1775];
                    if (_1763 >= Globals.uTileCount)
                    {
                        _1762 = false;
                    }
                    else
                    {
                        entry = uTiles_1._member0[_1763];
                        int _1799 = uTiles_1._member0[_1763].x * 10;
                        at = _1799;
                        if (uPages_1._member0[_1799 + 5].w > 0.5)
                        {
                            _1762 = false;
                        }
                        else
                        {
                            if (Globals.uActiveRadius > 0.0)
                            {
                                int _1812 = at + 9;
                                vec3 _1818 = uPages_1._member0[_1812].xyz - Globals.uActiveCentre;
                                float _1821 = Globals.uActiveRadius + uPages_1._member0[_1812].w;
                                if (dot(_1818, _1818) > (_1821 * _1821))
                                {
                                    _1762 = false;
                                }
                                else
                                {
                                    lightPage = uPages_1._member0[at];
                                    cardPage = uPages_1._member0[at + 1];
                                    _1762 = true;
                                }
                            }
                            else
                            {
                                lightPage = uPages_1._member0[at];
                                cardPage = uPages_1._member0[at + 1];
                                _1762 = true;
                            }
                        }
                    }
                }
            }
            else
            {
                _1763 = _1775;
                if (_1763 >= Globals.uTileCount)
                {
                    _1762 = false;
                }
                else
                {
                    entry = uTiles_1._member0[_1763];
                    int _1799 = uTiles_1._member0[_1763].x * 10;
                    at = _1799;
                    if (uPages_1._member0[_1799 + 5].w > 0.5)
                    {
                        _1762 = false;
                    }
                    else
                    {
                        if (Globals.uActiveRadius > 0.0)
                        {
                            int _1812 = at + 9;
                            vec3 _1818 = uPages_1._member0[_1812].xyz - Globals.uActiveCentre;
                            float _1821 = Globals.uActiveRadius + uPages_1._member0[_1812].w;
                            if (dot(_1818, _1818) > (_1821 * _1821))
                            {
                                _1762 = false;
                            }
                            else
                            {
                                lightPage = uPages_1._member0[at];
                                cardPage = uPages_1._member0[at + 1];
                                _1762 = true;
                            }
                        }
                        else
                        {
                            lightPage = uPages_1._member0[at];
                            cardPage = uPages_1._member0[at + 1];
                            _1762 = true;
                        }
                    }
                }
            }
        }
        if (!(!_1762))
        {
            vec4 _244 = lightPage;
            ivec2 pageAt = ivec2(int(_244.x), int(_244.y));
            int _254 = int(_244.z);
            int _257 = int(_244.w);
            ivec2 within = (entry.yz * ivec2(8)) + ivec2(gl_LocalInvocationID.xy);
            bool probeLive;
            if (within.x < _254)
            {
                probeLive = within.y < _257;
            }
            else
            {
                probeLive = false;
            }
            if (probeLive)
            {
                ivec2 texelAt = pageAt + within;
                uvec2 _1849 = uvec2(textureSize(_2797, 0));
                if (textureLod(_2799, (cardPage.xy + (((vec2(within) + vec2(0.5)) / vec2(lightPage.zw)) * cardPage.zw)) / vec2(float(_1849.x), float(_1849.y)), 0.0).w < 0.5)
                {
                    probeLive = true;
                }
                else
                {
                    probeLive = ((texelAt.x | texelAt.y) & 3) != 0;
                }
                if (probeLive)
                {
                    imageStore(uDirect, ivec2(uvec2(texelAt)), vec4(0.0));
                }
            }
            int lane = (int(gl_LocalInvocationID.y) * 8) + int(gl_LocalInvocationID.x);
            int probeIndex = lane / 16;
            int ray = lane - 16 * (lane / 16);
            ivec4 _367 = entry;
            ivec2 probeWithin = ((_367.yz * ivec2(8)) + ((ivec2(4) - ((pageAt + (_367.yz * ivec2(8))) & ivec2(3))) & ivec2(3))) + (ivec2(probeIndex - 2 * (probeIndex / 2), probeIndex / 2) * ivec2(4));
            ivec2 probeTexel = pageAt + probeWithin;
            if (probeWithin.x < _254)
            {
                probeLive = probeWithin.y < _257;
            }
            else
            {
                probeLive = false;
            }
            vec3 world = vec3(0.0);
            vec3 worldNormal = vec3(0.0, 0.0, 1.0);
            if (probeLive)
            {
                vec2 _1878 = (vec2(probeWithin) + vec2(0.5)) / vec2(lightPage.zw);
                vec2 uv = _1878;
                uvec2 _1880 = uvec2(textureSize(_2797, 0));
                vec2 _1893 = (cardPage.xy + (_1878 * cardPage.zw)) / vec2(float(_1880.x), float(_1880.y));
                vec2 cardUv = _1893;
                bool probeLive_1 = textureLod(_2799, _1893, 0.0).w >= 0.5;
                if (probeLive_1)
                {
                    int _1900 = at + 2;
                    int _1903 = at + 3;
                    int _1906 = at + 4;
                    int _1912 = at + 6;
                    int _1915 = at + 7;
                    int _1918 = at + 8;
                    vec3 _1930 = normalize((textureLod(_2801, cardUv, 0.0).xyz * 2.0) - vec3(1.0));
                    vec3 _1958 = ((uPages_1._member0[_1900].xyz + ((uPages_1._member0[_1903].xyz * ((uv.x * 2.0) - 1.0)) * uPages_1._member0[_1900].w)) + ((uPages_1._member0[_1906].xyz * ((uv.y * 2.0) - 1.0)) * uPages_1._member0[_1903].w)) - (uPages_1._member0[at + 5].xyz * (textureLod(_2803, cardUv, 0.0).x * uPages_1._member0[_1906].w));
                    vec3 _1962 = vec3(uPages_1._member0[_1912].x, uPages_1._member0[_1915].x, uPages_1._member0[_1918].x);
                    vec3 _1967 = vec3(uPages_1._member0[_1912].y, uPages_1._member0[_1915].y, uPages_1._member0[_1918].y);
                    vec3 _1972 = vec3(uPages_1._member0[_1912].z, uPages_1._member0[_1915].z, uPages_1._member0[_1918].z);
                    world = vec3(dot(_1962, _1958), dot(_1967, _1958), dot(_1972, _1958)) + vec3(uPages_1._member0[_1912].w, uPages_1._member0[_1915].w, uPages_1._member0[_1918].w);
                    worldNormal = normalize(vec3(dot(_1962, _1930), dot(_1967, _1930), dot(_1972, _1930)));
                }
                probeLive = probeLive_1;
            }
            ivec2 probeCoord = probeTexel >> ivec2(2);
            vec2 _1986;
            {
                if (Globals.uTemporalIndex <= 0)
                {
                    _1986 = vec2(0.5);
                }
                else
                {
                    float _2001 = float(((uint(probeCoord.x) * 73856093u) ^ (uint(probeCoord.y) * 19349663u)) & 65535u);
                    int _2007 = (Globals.uTemporalIndex / max(Globals.uJitterStride, 1)) & 65535;
                    uint _2022 = uint(_2007);
                    uint _2025 = (_2022 << 16u) | (_2022 >> 16u);
                    uint _2030 = ((_2025 & 1431655765u) << 1u) | ((_2025 & 2863311530u) >> 1u);
                    uint _2035 = ((_2030 & 858993459u) << 2u) | ((_2030 & 3435973836u) >> 2u);
                    uint _2040 = ((_2035 & 252645135u) << 4u) | ((_2035 & 4042322160u) >> 4u);
                    _1986 = fract(vec2(fract(float(_2007) * 0.61803400516510009765625), float(((_2040 & 16711935u) << 8u) | ((_2040 & 4278255360u) >> 8u)) * 2.3283064365386962890625e-10) + vec2(_2001 * 1.52587890625e-05, fract(_2001 * 2.4689239580766297876834869384766e-05)));
                }
            }
            bool gathers;
            if (probeLive)
            {
                bool _2048;
                if (Globals.uShadowLevels > 0)
                {
                    _2048 = Globals.uLookupBuckets > 0;
                }
                else
                {
                    _2048 = false;
                }
                gathers = _2048;
            }
            else
            {
                gathers = false;
            }
            if (gathers)
            {
                vec3 _2743;
                vec3 _2744;
                vec3 _2745;
                {
                    vec3 _2758 = worldNormal;
                    if (worldNormal.z < (-0.99999988079071044921875))
                    {
                        _2743 = vec3(0.0, -1.0, 0.0);
                        _2744 = vec3(-1.0, 0.0, 0.0);
                        _2745 = _2758;
                    }
                    else
                    {
                        float _2076 = 1.0 / (1.0 + worldNormal.z);
                        float _2078 = -worldNormal.x;
                        float _2081 = (_2078 * worldNormal.y) * _2076;
                        _2743 = vec3(1.0 - ((worldNormal.x * worldNormal.x) * _2076), _2081, _2078);
                        _2744 = vec3(_2081, 1.0 - ((worldNormal.y * worldNormal.y) * _2076), -worldNormal.y);
                        _2745 = _2758;
                    }
                }
                vec2 _2108 = (vec2(float(ray - 4 * (ray / 4)), float(ray / 4)) + _1986) * vec2(0.25);
                float _2110 = 6.283185482025146484375 * _2108.x;
                float _2111 = _2108.y;
                float _2115 = sqrt(max(0.0, 1.0 - (_2111 * _2111)));
                vec3 _2160 = ((_2743 * (_2115 * cos(_2110))) + (_2744 * (_2115 * sin(_2110)))) + (_2745 * _2111);
                vec3 radiance = vec3(0.0);
                _2161 = false;
                vec3 _2096;
                float _2162;
                {
                    _2096 = world;
                    if (Globals.uShadowLevels <= 0)
                    {
                        _2161 = true;
                        _2162 = 1.0;
                    }
                    else
                    {
                        vec3 _2185 = world + ((worldNormal * Globals.uClipmapOrigin.data[0].w) * 1.5);
                        float _2196 = (Globals.uClipmapOrigin.data[0].w * float(Globals.uShadowResolution)) * float(1 << (Globals.uShadowLevels - 1));
                        int _2163 = 0;
                        float _2164 = 0.0;
                        bool _2165;
                        bool _2166;
                        float _2168;
                        float _2243;
                        bool _2245;
                        for (;;)
                        {
                            if (_2163 < 64)
                            {
                                _2165 = _2164 < _2196;
                            }
                            else
                            {
                                _2165 = false;
                            }
                            if (!_2165)
                            {
                                break;
                            }
                            vec3 _2213 = _2185 + (_2160 * _2164);
                            _2242 = false;
                            {
                                _2166 = false;
                                _2168 = Globals.uClipmapOrigin.data[0].w;
                                int _2244 = 0;
                                for (;;)
                                {
                                    bool _2256_ladder_break = false;
                                    {
                                        if (!(_2244 < Globals.uShadowLevels))
                                        {
                                            _2256_ladder_break = true;
                                        }
                                        else
                                        {
                                            vec3 _2271 = (_2213 - Globals.uClipmapOrigin.data[_2244].xyz) / vec3(Globals.uClipmapOrigin.data[_2244].w);
                                            if (any(lessThan(_2271, vec3(0.5))))
                                            {
                                                _2245 = true;
                                            }
                                            else
                                            {
                                                _2245 = any(greaterThan(_2271, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                            }
                                            if (!(_2245))
                                            {
                                                _2166 = true;
                                                _2168 = Globals.uClipmapOrigin.data[_2244].w;
                                                _2242 = true;
                                                _2243 = ((textureLod(_2805, vec3(_2271.x / float(Globals.uShadowResolution), _2271.y / float(Globals.uShadowResolution), ((float(_2244) * float(Globals.uShadowResolution)) + _2271.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_2244];
                                                _2256_ladder_break = true;
                                            }
                                        }
                                    }
                                    if (_2256_ladder_break)
                                    {
                                        break;
                                    }
                                    _2244++;
                                    continue;
                                }
                                if (!(_2242))
                                {
                                    _2242 = true;
                                    _2243 = 1000000.0;
                                }
                            }
                            if (!_2166)
                            {
                                _2161 = true;
                                _2162 = 1.0;
                                break;
                            }
                            float _2347 = (0.5 * _2168) * clamp(_2164 / max(2.0 * _2168, 9.9999999747524270787835121154785e-07), 0.0, 1.0);
                            float _2169 = max((_2164 + _2243) - _2347, 0.0);
                            if (_2243 < _2347)
                            {
                                _2096 = _2185 + (_2160 * _2169);
                                _2161 = true;
                                _2162 = 0.0;
                                break;
                            }
                            _2163++;
                            _2164 += max(_2243, _2168 * 0.25);
                            continue;
                        }
                        if (!(_2161))
                        {
                            _2161 = true;
                            _2162 = 1.0;
                        }
                    }
                }
                if (_2162 <= 0.5)
                {
                    vec2 _2098;
                    bool _2357;
                    {
                        _2098 = vec2(0.0);
                        float _2099 = 1000000000.0;
                        if (Globals.uLookupBuckets <= 0)
                        {
                            _2357 = false;
                        }
                        else
                        {
                            ivec3 _2398 = ivec3(floor((_2096 - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                            int _2610 = int((((uint(_2398.x) * 2376512323u) ^ (uint(_2398.y) * 3625334849u)) ^ (uint(_2398.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                            uvec2 _2403 = uvec2(textureSize(_2797, 0));
                            vec2 _2410 = vec2(float(_2403.x), float(_2403.y));
                            bool _2360 = false;
                            bool _2361 = false;
                            int _2362 = 0;
                            bool _2363;
                            ivec2 _2364;
                            bool _2368;
                            bool _2377;
                            bool _2378;
                            bool _2379;
                            bool _2380;
                            bool _2381;
                            ivec2 _2611;
                            for (;;)
                            {
                                if (_2362 <= uCells._member0[Globals.uLookupBuckets].y)
                                {
                                    _2363 = !_2360;
                                }
                                else
                                {
                                    _2363 = false;
                                }
                                if (!_2363)
                                {
                                    break;
                                }
                                bool _2430 = _2362 == 0;
                                if (_2430)
                                {
                                    _2364 = uCells._member0[_2610];
                                }
                                else
                                {
                                    int _2615 = uCells._member0[Globals.uLookupBuckets].x + ((_2362 - 1) * 6);
                                    float _2631 = intBitsToFloat(uCellPages_1._member0[_2615 + 3]);
                                    vec3 _2633 = _2096 - vec3(intBitsToFloat(uCellPages_1._member0[_2615]), intBitsToFloat(uCellPages_1._member0[_2615 + 1]), intBitsToFloat(uCellPages_1._member0[_2615 + 2]));
                                    if (dot(_2633, _2633) > (_2631 * _2631))
                                    {
                                        _2611 = ivec2(0);
                                    }
                                    else
                                    {
                                        _2611 = ivec2(uCellPages_1._member0[_2615 + 4], uCellPages_1._member0[_2615 + 5]);
                                    }
                                    _2364 = _2611;
                                }
                                bool _2365 = _2360;
                                bool _2366 = _2361;
                                int _2367 = 0;
                                for (;;)
                                {
                                    bool _2442_ladder_break = false;
                                    {
                                        if (_2367 < _2364.y)
                                        {
                                            _2368 = !_2365;
                                        }
                                        else
                                        {
                                            _2368 = false;
                                        }
                                        if (!_2368)
                                        {
                                            _2442_ladder_break = true;
                                        }
                                        else
                                        {
                                            int _2459 = _2364.x + _2367;
                                            int _2462 = uCellPages_1._member0[_2459] * 10;
                                            if (!((uPages_1._member0[_2462 + 5].w > 0.5) == _2430))
                                            {
                                                int _2471 = _2462 + 9;
                                                vec3 _2475 = _2096 - uPages_1._member0[_2471].xyz;
                                                if (!(dot(_2475, _2475) > (uPages_1._member0[_2471].w * uPages_1._member0[_2471].w)))
                                                {
                                                    vec3 _2482 = -_2160;
                                                    int _2649 = uCellPages_1._member0[_2459] * 10;
                                                    vec4 _2375 = uPages_1._member0[_2649];
                                                    vec4 _2376 = uPages_1._member0[_2649 + 1];
                                                    int _2655 = _2649 + 2;
                                                    int _2658 = _2649 + 3;
                                                    int _2661 = _2649 + 4;
                                                    int _2667 = _2649 + 6;
                                                    int _2670 = _2649 + 7;
                                                    int _2673 = _2649 + 8;
                                                    vec3 _2680 = _2096 - vec3(uPages_1._member0[_2667].w, uPages_1._member0[_2670].w, uPages_1._member0[_2673].w);
                                                    vec3 _2374 = vec3(dot(uPages_1._member0[_2667].xyz, _2482), dot(uPages_1._member0[_2670].xyz, _2482), dot(uPages_1._member0[_2673].xyz, _2482));
                                                    vec3 _2693 = vec3(dot(uPages_1._member0[_2667].xyz, _2680), dot(uPages_1._member0[_2670].xyz, _2680), dot(uPages_1._member0[_2673].xyz, _2680)) - uPages_1._member0[_2655].xyz;
                                                    vec2 _2703 = vec2(dot(_2693, uPages_1._member0[_2658].xyz), dot(_2693, uPages_1._member0[_2661].xyz)) / max(vec2(uPages_1._member0[_2655].w, uPages_1._member0[_2658].w), vec2(9.9999999747524270787835121154785e-07));
                                                    vec2 _2369 = _2703;
                                                    float _2371 = -dot(_2693, uPages_1._member0[_2649 + 5].xyz);
                                                    float _2372 = uPages_1._member0[_2661].w;
                                                    vec3 _2373 = uPages_1._member0[_2649 + 5].xyz;
                                                    if (abs(_2703.x) > 1.0)
                                                    {
                                                        _2377 = true;
                                                    }
                                                    else
                                                    {
                                                        _2377 = abs(_2369.y) > 1.0;
                                                    }
                                                    if (!(_2377))
                                                    {
                                                        if (dot(_2160, _2160) > 0.5)
                                                        {
                                                            _2378 = dot(_2373, _2374) < (-0.0500000007450580596923828125);
                                                        }
                                                        else
                                                        {
                                                            _2378 = false;
                                                        }
                                                        if (!(_2378))
                                                        {
                                                            if (_2371 < (-0.25))
                                                            {
                                                                _2379 = true;
                                                            }
                                                            else
                                                            {
                                                                _2379 = _2371 > (_2372 + 0.25);
                                                            }
                                                            if (!(_2379))
                                                            {
                                                                vec2 _2526 = (_2369 * 0.5) + vec2(0.5);
                                                                vec2 _2533 = (_2376.xy + (_2526 * _2376.zw)) / _2410;
                                                                if (!(textureLod(_2799, _2533, 0.0).w < 0.5))
                                                                {
                                                                    float _2553 = abs((textureLod(_2803, _2533, 0.0).x * _2372) - _2371);
                                                                    if (_2553 < _2099)
                                                                    {
                                                                        _2099 = _2553;
                                                                        _2098 = (_2375.xy + (_2526 * _2375.zw)) / Globals.uAtlasSize.xy;
                                                                        _2380 = _2553 < 0.0199999995529651641845703125;
                                                                        _2381 = true;
                                                                    }
                                                                    else
                                                                    {
                                                                        _2380 = _2365;
                                                                        _2381 = _2366;
                                                                    }
                                                                    _2365 = _2380;
                                                                    _2366 = _2381;
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (_2442_ladder_break)
                                    {
                                        break;
                                    }
                                    _2367++;
                                    continue;
                                }
                                _2360 = _2365;
                                _2361 = _2366;
                                _2362++;
                                continue;
                            }
                            _2357 = _2361;
                        }
                    }
                    if (_2357)
                    {
                        radiance = textureLod(_2807, _2098, 0.0).xyz * smoothstep(1.5 * Globals.uClipmapOrigin.data[0].w, 2.0 * Globals.uClipmapOrigin.data[0].w, length(_2096 - world));
                    }
                }
                sBounceBasis[lane] = vec4(0.2820949852466583251953125, (-0.48860299587249755859375) * _2160.y, 0.48860299587249755859375 * _2160.z, (-0.48860299587249755859375) * _2160.x);
                sBounceRadiance[lane] = radiance;
            }
            barrier();
            if (ray != 0)
            {
                probeLive = true;
            }
            else
            {
                probeLive = !probeLive;
            }
            if (!(probeLive))
            {
                uProbePos_1._member0[(probeCoord.y * Globals.uProbeWidth) + probeCoord.x] = vec4(world, 1.0);
                vec3 gathered;
                if (gathers)
                {
                    int cell = 0;
                    vec4 red = vec4(0.0);
                    vec4 green = vec4(0.0);
                    vec4 blue = vec4(0.0);
                    for (;;)
                    {
                        if (!(cell < 16))
                        {
                            break;
                        }
                        int _1624 = cell;
                        int _1625 = lane + _1624;
                        cell++;
                        red += (sBounceBasis[_1625] * sBounceRadiance[_1625].x);
                        green += (sBounceBasis[_1625] * sBounceRadiance[_1625].y);
                        blue += (sBounceBasis[_1625] * sBounceRadiance[_1625].z);
                        continue;
                    }
                    vec4 _2742 = vec4(0.2820949852466583251953125, (-0.48860299587249755859375) * worldNormal.y, 0.48860299587249755859375 * worldNormal.z, (-0.48860299587249755859375) * worldNormal.x);
                    vec4 _2733 = vec4(_2742.x * 3.1415927410125732421875, _2742.yzw * 2.094395160675048828125);
                    gathered = max(vec3(dot(red, _2733), dot(green, _2733), dot(blue, _2733)) * 0.3926990926265716552734375, vec3(0.0));
                }
                else
                {
                    gathered = vec3(0.0);
                }
                vec4 sampled = imageLoad(uDirect, probeTexel);
                float traced;
                if (Globals.uTemporalIndex > 0)
                {
                    traced = sampled.w;
                }
                else
                {
                    traced = 0.0;
                }
                float limit = float(Globals.uMaxFrames);
                imageStore(uDirect, ivec2(uvec2(probeTexel)), vec4(mix(sampled.xyz, gathered, vec3(1.0 / (1.0 + min(traced, limit)))), min(traced + 1.0, limit + 1.0)));
            }
        }
    }
}

