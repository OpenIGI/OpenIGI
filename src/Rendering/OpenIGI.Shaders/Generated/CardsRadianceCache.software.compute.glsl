#version 430
layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

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
    vec3 uSkyColour;
    vec3 uCacheOrigin;
    vec3 uCacheCount;
    vec3 uTraceOrigin;
    _Array_std140_float4 uClipmapRange;
    int uShadowLevels;
    int uShadowResolution;
    int uLookupBuckets;
    float uLookupCell;
    int uSkyShValid;
    float uBounceScale;
    float uCacheSpacing;
    int uSlabBegin;
    int uSlabEnd;
    float uAlpha;
    int uRayCount;
    int uSweep;
    float uCacheScale;
    float uSkyReject;
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

layout(binding = 0, rgba16f) uniform restrict image3D uCacheRed;
layout(binding = 1, rgba16f) uniform restrict image3D uCacheGreen;
layout(binding = 2, rgba16f) uniform restrict image3D uCacheBlue;
layout(binding = 3, rgba16f) uniform restrict image3D uCacheDistance;
layout(binding = 3) uniform sampler3D _1983;
layout(binding = 0) uniform sampler2D _1986;
layout(binding = 1) uniform sampler2D _1988;
layout(binding = 2) uniform sampler2D _1990;
layout(binding = 4) uniform sampler2D _1992;

void main()
{
    bool _1458 = false;
    bool _1377 = false;
    {
        ivec3 _100 = ivec3(Globals.uCacheCount + vec3(0.5));
        int _107 = int(gl_GlobalInvocationID.x);
        int _110 = int(gl_GlobalInvocationID.y);
        int _118 = Globals.uSlabBegin + int(gl_GlobalInvocationID.z);
        ivec3 probe = ivec3(_107, _110, _118);
        int _120 = _100.x;
        bool sky;
        if (_107 >= _120)
        {
            sky = true;
        }
        else
        {
            sky = _110 >= _100.y;
        }
        if (sky)
        {
            sky = true;
        }
        else
        {
            sky = _118 >= Globals.uSlabEnd;
        }
        if (sky)
        {
            sky = true;
        }
        else
        {
            sky = _118 >= _100.z;
        }
        if (!(sky))
        {
            if (Globals.uShadowLevels <= 0)
            {
                sky = true;
            }
            else
            {
                sky = Globals.uLookupBuckets <= 0;
            }
            if (!(sky))
            {
                vec3 _177 = Globals.uCacheOrigin + (vec3(probe) * Globals.uCacheSpacing);
                float _184 = fract(float(Globals.uSweep) * 0.61803400516510009765625);
                float _192 = Globals.uCacheSpacing * float(max(_120, max(_100.y, _100.z)));
                int ray = 0;
                float escaped = 0.0;
                vec3 s0 = vec3(0.0);
                vec3 s1 = vec3(0.0);
                vec3 s2 = vec3(0.0);
                vec3 s3 = vec3(0.0);
                vec3 c0 = vec3(0.0);
                vec3 c1 = vec3(0.0);
                vec3 c2 = vec3(0.0);
                vec3 c3 = vec3(0.0);
                float distSum = 0.0;
                float distSqSum = 0.0;
                float skyTrust;
                vec3 hit;
                vec3 radiance;
                vec2 litFound;
                float _1378;
                bool _1381;
                bool _1382;
                float _1384;
                float _1459;
                bool _1461;
                vec3 _1569;
                bool _1625;
                bool _1631;
                ivec2 _1632;
                bool _1636;
                bool _1645;
                bool _1646;
                bool _1647;
                bool _1648;
                bool _1649;
                ivec2 _1879;
                for (;;)
                {
                    if (!(ray < Globals.uRayCount))
                    {
                        break;
                    }
                    float _1359 = float(ray) + _184;
                    float _1365 = clamp(1.0 - (((2.0 * _1359) + 1.0) / float(Globals.uRayCount)), -1.0, 1.0);
                    float _1369 = sqrt(max(0.0, 1.0 - (_1365 * _1365)));
                    float _1371 = _1359 * 3.8832223415374755859375;
                    vec3 _1376 = vec3(_1369 * cos(_1371), _1369 * sin(_1371), _1365);
                    _1377 = false;
                    {
                        hit = _177;
                        if (Globals.uShadowLevels <= 0)
                        {
                            _1377 = true;
                            _1378 = 1.0;
                        }
                        else
                        {
                            vec3 _1401 = _177 + ((_1376 * Globals.uClipmapOrigin.data[0].w) * 1.5);
                            float _1412 = (Globals.uClipmapOrigin.data[0].w * float(Globals.uShadowResolution)) * float(1 << (Globals.uShadowLevels - 1));
                            int _1379 = 0;
                            float _1380 = 0.0;
                            for (;;)
                            {
                                if (_1379 < 64)
                                {
                                    _1381 = _1380 < _1412;
                                }
                                else
                                {
                                    _1381 = false;
                                }
                                if (!_1381)
                                {
                                    break;
                                }
                                vec3 _1429 = _1401 + (_1376 * _1380);
                                _1458 = false;
                                {
                                    _1382 = false;
                                    _1384 = Globals.uClipmapOrigin.data[0].w;
                                    int _1460 = 0;
                                    for (;;)
                                    {
                                        bool _1472_ladder_break = false;
                                        {
                                            if (!(_1460 < Globals.uShadowLevels))
                                            {
                                                _1472_ladder_break = true;
                                            }
                                            else
                                            {
                                                vec3 _1487 = (_1429 - Globals.uClipmapOrigin.data[_1460].xyz) / vec3(Globals.uClipmapOrigin.data[_1460].w);
                                                if (any(lessThan(_1487, vec3(0.5))))
                                                {
                                                    _1461 = true;
                                                }
                                                else
                                                {
                                                    _1461 = any(greaterThan(_1487, vec3(float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5, float(Globals.uShadowResolution) - 0.5)));
                                                }
                                                if (!(_1461))
                                                {
                                                    _1382 = true;
                                                    _1384 = Globals.uClipmapOrigin.data[_1460].w;
                                                    _1458 = true;
                                                    _1459 = ((textureLod(_1983, vec3(_1487.x / float(Globals.uShadowResolution), _1487.y / float(Globals.uShadowResolution), ((float(_1460) * float(Globals.uShadowResolution)) + _1487.z) / float(Globals.uShadowResolution * Globals.uShadowLevels)), 0.0).x * 2.0) - 1.0) * Globals.uClipmapRange.data[_1460];
                                                    _1472_ladder_break = true;
                                                }
                                            }
                                        }
                                        if (_1472_ladder_break)
                                        {
                                            break;
                                        }
                                        _1460++;
                                        continue;
                                    }
                                    if (!(_1458))
                                    {
                                        _1458 = true;
                                        _1459 = 1000000.0;
                                    }
                                }
                                if (!_1382)
                                {
                                    _1377 = true;
                                    _1378 = 1.0;
                                    break;
                                }
                                float _1563 = (0.5 * _1384) * clamp(_1380 / max(2.0 * _1384, 9.9999999747524270787835121154785e-07), 0.0, 1.0);
                                float _1385 = max((_1380 + _1459) - _1563, 0.0);
                                if (_1459 < _1563)
                                {
                                    hit = _1401 + (_1376 * _1385);
                                    _1377 = true;
                                    _1378 = 0.0;
                                    break;
                                }
                                _1379++;
                                _1380 += max(_1459, _1384 * 0.25);
                                continue;
                            }
                            if (!(_1377))
                            {
                                _1377 = true;
                                _1378 = 1.0;
                            }
                        }
                    }
                    if (_1378 <= 0.5)
                    {
                        {
                            litFound = vec2(0.0);
                            float agreement = 1000000000.0;
                            if (Globals.uLookupBuckets <= 0)
                            {
                                _1625 = false;
                            }
                            else
                            {
                                ivec3 _1666 = ivec3(floor((hit - Globals.uLookupMin) / vec3(Globals.uLookupCell)));
                                int _1878 = int((((uint(_1666.x) * 2376512323u) ^ (uint(_1666.y) * 3625334849u)) ^ (uint(_1666.z) * 3407524639u)) & uint(Globals.uLookupBuckets - 1));
                                uvec2 _1671 = uvec2(textureSize(_1986, 0));
                                vec2 _1678 = vec2(float(_1671.x), float(_1671.y));
                                bool _1628 = false;
                                bool _1629 = false;
                                int _1630 = 0;
                                for (;;)
                                {
                                    if (_1630 <= uCells._member0[Globals.uLookupBuckets].y)
                                    {
                                        _1631 = !_1628;
                                    }
                                    else
                                    {
                                        _1631 = false;
                                    }
                                    if (!_1631)
                                    {
                                        break;
                                    }
                                    bool _1698 = _1630 == 0;
                                    if (_1698)
                                    {
                                        _1632 = uCells._member0[_1878];
                                    }
                                    else
                                    {
                                        int _1883 = uCells._member0[Globals.uLookupBuckets].x + ((_1630 - 1) * 6);
                                        float _1899 = intBitsToFloat(uCellPages_1._member0[_1883 + 3]);
                                        vec3 _1901 = hit - vec3(intBitsToFloat(uCellPages_1._member0[_1883]), intBitsToFloat(uCellPages_1._member0[_1883 + 1]), intBitsToFloat(uCellPages_1._member0[_1883 + 2]));
                                        if (dot(_1901, _1901) > (_1899 * _1899))
                                        {
                                            _1879 = ivec2(0);
                                        }
                                        else
                                        {
                                            _1879 = ivec2(uCellPages_1._member0[_1883 + 4], uCellPages_1._member0[_1883 + 5]);
                                        }
                                        _1632 = _1879;
                                    }
                                    bool _1633 = _1628;
                                    bool _1634 = _1629;
                                    int _1635 = 0;
                                    for (;;)
                                    {
                                        bool _1710_ladder_break = false;
                                        {
                                            if (_1635 < _1632.y)
                                            {
                                                _1636 = !_1633;
                                            }
                                            else
                                            {
                                                _1636 = false;
                                            }
                                            if (!_1636)
                                            {
                                                _1710_ladder_break = true;
                                            }
                                            else
                                            {
                                                int _1727 = _1632.x + _1635;
                                                int _1730 = uCellPages_1._member0[_1727] * 10;
                                                if (!((uPages._member0[_1730 + 5].w > 0.5) == _1698))
                                                {
                                                    int _1739 = _1730 + 9;
                                                    vec3 _1743 = hit - uPages._member0[_1739].xyz;
                                                    if (!(dot(_1743, _1743) > (uPages._member0[_1739].w * uPages._member0[_1739].w)))
                                                    {
                                                        vec3 _1750 = -_1376;
                                                        int _1917 = uCellPages_1._member0[_1727] * 10;
                                                        vec4 _1643 = uPages._member0[_1917];
                                                        vec4 _1644 = uPages._member0[_1917 + 1];
                                                        int _1923 = _1917 + 2;
                                                        int _1926 = _1917 + 3;
                                                        int _1929 = _1917 + 4;
                                                        int _1935 = _1917 + 6;
                                                        int _1938 = _1917 + 7;
                                                        int _1941 = _1917 + 8;
                                                        vec3 _1948 = hit - vec3(uPages._member0[_1935].w, uPages._member0[_1938].w, uPages._member0[_1941].w);
                                                        vec3 _1642 = vec3(dot(uPages._member0[_1935].xyz, _1750), dot(uPages._member0[_1938].xyz, _1750), dot(uPages._member0[_1941].xyz, _1750));
                                                        vec3 _1961 = vec3(dot(uPages._member0[_1935].xyz, _1948), dot(uPages._member0[_1938].xyz, _1948), dot(uPages._member0[_1941].xyz, _1948)) - uPages._member0[_1923].xyz;
                                                        vec2 _1971 = vec2(dot(_1961, uPages._member0[_1926].xyz), dot(_1961, uPages._member0[_1929].xyz)) / max(vec2(uPages._member0[_1923].w, uPages._member0[_1926].w), vec2(9.9999999747524270787835121154785e-07));
                                                        vec2 _1637 = _1971;
                                                        float _1639 = -dot(_1961, uPages._member0[_1917 + 5].xyz);
                                                        float _1640 = uPages._member0[_1929].w;
                                                        vec3 _1641 = uPages._member0[_1917 + 5].xyz;
                                                        if (abs(_1971.x) > 1.0)
                                                        {
                                                            _1645 = true;
                                                        }
                                                        else
                                                        {
                                                            _1645 = abs(_1637.y) > 1.0;
                                                        }
                                                        if (!(_1645))
                                                        {
                                                            if (dot(_1376, _1376) > 0.5)
                                                            {
                                                                _1646 = dot(_1641, _1642) < (-0.0500000007450580596923828125);
                                                            }
                                                            else
                                                            {
                                                                _1646 = false;
                                                            }
                                                            if (!(_1646))
                                                            {
                                                                if (_1639 < (-0.25))
                                                                {
                                                                    _1647 = true;
                                                                }
                                                                else
                                                                {
                                                                    _1647 = _1639 > (_1640 + 0.25);
                                                                }
                                                                if (!(_1647))
                                                                {
                                                                    vec2 _1794 = (_1637 * 0.5) + vec2(0.5);
                                                                    vec2 _1801 = (_1644.xy + (_1794 * _1644.zw)) / _1678;
                                                                    if (!(textureLod(_1988, _1801, 0.0).w < 0.5))
                                                                    {
                                                                        float _1821 = abs((textureLod(_1990, _1801, 0.0).x * _1640) - _1639);
                                                                        if (_1821 < agreement)
                                                                        {
                                                                            agreement = _1821;
                                                                            litFound = (_1643.xy + (_1794 * _1643.zw)) / Globals.uAtlasSize.xy;
                                                                            _1648 = _1821 < 0.0199999995529651641845703125;
                                                                            _1649 = true;
                                                                        }
                                                                        else
                                                                        {
                                                                            _1648 = _1633;
                                                                            _1649 = _1634;
                                                                        }
                                                                        _1633 = _1648;
                                                                        _1634 = _1649;
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        if (_1710_ladder_break)
                                        {
                                            break;
                                        }
                                        _1635++;
                                        continue;
                                    }
                                    _1628 = _1633;
                                    _1629 = _1634;
                                    _1630++;
                                    continue;
                                }
                                _1625 = _1629;
                            }
                        }
                        if (_1625)
                        {
                            radiance = textureLod(_1992, litFound, 0.0).xyz * Globals.uBounceScale;
                        }
                        else
                        {
                            radiance = vec3(0.0);
                        }
                        sky = false;
                        skyTrust = length(hit - _177);
                    }
                    else
                    {
                        {
                            if (Globals.uSkyShValid == 0)
                            {
                                _1569 = Globals.uSkyColour;
                            }
                            else
                            {
                                vec4 _1580 = vec4(_1376, 1.0);
                                vec4 _1583 = _1580.xyzz * _1580.yzzx;
                                float _1609 = _1376.x;
                                float _1611 = _1376.y;
                                _1569 = max(vec3(0.0), (vec3(dot(Globals.uSkySh.data[0], _1580), dot(Globals.uSkySh.data[1], _1580), dot(Globals.uSkySh.data[2], _1580)) + vec3(dot(Globals.uSkySh.data[3], _1583), dot(Globals.uSkySh.data[4], _1583), dot(Globals.uSkySh.data[5], _1583))) + (Globals.uSkySh.data[6].xyz * ((_1609 * _1609) - (_1611 * _1611))));
                            }
                        }
                        radiance = _1569;
                        sky = true;
                        skyTrust = _192;
                        escaped += 1.0;
                    }
                    vec3 _1124 = min(radiance, vec3(8.0));
                    if (sky)
                    {
                        s0 += (_1124 * 0.2820949852466583251953125);
                        s1 += (_1124 * (0.48860299587249755859375 * _1376.x));
                        s2 += (_1124 * (0.48860299587249755859375 * _1376.y));
                        s3 += (_1124 * (0.48860299587249755859375 * _1376.z));
                    }
                    else
                    {
                        c0 += (_1124 * 0.2820949852466583251953125);
                        c1 += (_1124 * (0.48860299587249755859375 * _1376.x));
                        c2 += (_1124 * (0.48860299587249755859375 * _1376.y));
                        c3 += (_1124 * (0.48860299587249755859375 * _1376.z));
                    }
                    ray++;
                    distSum += skyTrust;
                    distSqSum += (skyTrust * skyTrust);
                    continue;
                }
                if (Globals.uSkyReject <= 0.0)
                {
                    skyTrust = 1.0;
                }
                else
                {
                    skyTrust = smoothstep(Globals.uSkyReject * 0.3300000131130218505859375, Globals.uSkyReject, escaped / float(Globals.uRayCount));
                }
                float scale = (12.56637096405029296875 / float(Globals.uRayCount)) * Globals.uCacheScale;
                vec3 c0_1 = (c0 + (s0 * skyTrust)) * scale;
                vec3 c1_1 = (c1 + (s1 * skyTrust)) * scale;
                vec3 c2_1 = (c2 + (s2 * skyTrust)) * scale;
                vec3 c3_1 = (c3 + (s3 * skyTrust)) * scale;
                float invN = 1.0 / float(Globals.uRayCount);
                vec4 red_1 = vec4(c0_1.x, c1_1.x, c2_1.x, c3_1.x);
                vec4 green_1 = vec4(c0_1.y, c1_1.y, c2_1.y, c3_1.y);
                vec4 blue_1 = vec4(c0_1.z, c1_1.z, c2_1.z, c3_1.z);
                vec4 moments_1 = vec4(distSum * invN, distSqSum * invN, 0.0, 0.0);
                vec4 red;
                vec4 green;
                vec4 blue;
                vec4 moments;
                if (Globals.uAlpha < 1.0)
                {
                    red = mix(imageLoad(uCacheRed, probe), red_1, vec4(Globals.uAlpha));
                    green = mix(imageLoad(uCacheGreen, probe), green_1, vec4(Globals.uAlpha));
                    blue = mix(imageLoad(uCacheBlue, probe), blue_1, vec4(Globals.uAlpha));
                    moments = mix(imageLoad(uCacheDistance, probe), moments_1, vec4(Globals.uAlpha));
                }
                else
                {
                    red = red_1;
                    green = green_1;
                    blue = blue_1;
                    moments = moments_1;
                }
                uvec3 _1314 = uvec3(probe);
                imageStore(uCacheRed, ivec3(_1314), red);
                imageStore(uCacheGreen, ivec3(_1314), green);
                imageStore(uCacheBlue, ivec3(_1314), blue);
                imageStore(uCacheDistance, ivec3(_1314), moments);
            }
        }
    }
}

