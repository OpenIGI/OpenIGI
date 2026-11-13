#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Globals_std140
{
    vec4 uUnproject;
    vec4 uPrevUnproject;
    vec3 uViewToWorld0;
    vec3 uViewToWorld1;
    vec3 uViewToWorld2;
    vec3 uCameraToCache;
    vec3 uPrevWorldToView0;
    vec3 uPrevWorldToView1;
    vec3 uPrevWorldToView2;
    vec3 uPrevCameraToCache;
    vec3 uProbeWorldToView0;
    vec3 uProbeWorldToView1;
    vec3 uProbeWorldToView2;
    vec3 uProbeCameraToCache;
    int uScreenWidth;
    int uScreenHeight;
    int uHasHistory;
    int uMaxFrames;
    int uProbe;
    int uSceneDepth;
    int uSceneNormal;
    int uHistory;
    int uHistoryPosition;
    int uProbePosition;
    int uVelocity;
    int uProbeStep;
    int uProbeFrame;
    int uHasVelocity;
} g;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uAccum;
layout(binding = 1, rgba16f) uniform restrict writeonly image2D uAccumPosition;
layout(binding = 1) uniform sampler2D _2443;
layout(binding = 2) uniform sampler2D _2445;
layout(binding = 0) uniform sampler2D _2448;
layout(binding = 5) uniform sampler2D _2451;
layout(binding = 6) uniform sampler2D _2453;
layout(binding = 4) uniform sampler2D _2456;
layout(binding = 3) uniform sampler2D _2459;

void main()
{
    {
        ivec2 _121 = ivec2(gl_GlobalInvocationID.xy);
        bool ownMotion;
        if (_121.x >= g.uScreenWidth)
        {
            ownMotion = true;
        }
        else
        {
            ownMotion = _121.y >= g.uScreenHeight;
        }
        if (!(ownMotion))
        {
            vec2 uv = (vec2(_121) + vec2(0.5)) / vec2(float(g.uScreenWidth), float(g.uScreenHeight));
            vec4 _1382 = texture(_2443, uv);
            float _1383 = _1382.x;
            if (_1383 <= 0.0)
            {
                imageStore(uAccum, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
                imageStore(uAccumPosition, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
            }
            else
            {
                float _1410 = ((2.0 * g.uUnproject.z) * g.uUnproject.w) / ((g.uUnproject.w + g.uUnproject.z) - ((1.0 - (2.0 * _1383)) * (g.uUnproject.w - g.uUnproject.z)));
                vec2 _1413 = (uv * 2.0) - vec2(1.0);
                vec3 _1425 = vec3((_1413.x * _1410) / g.uUnproject.x, ((-1.0) * (_1413.y * _1410)) / g.uUnproject.y, _1410);
                float _264 = _1425.z;
                vec3 world = ((g.uCameraToCache + (g.uViewToWorld0 * _1425.x)) + (g.uViewToWorld1 * _1425.y)) + (g.uViewToWorld2 * _264);
                vec4 _1431 = texture(_2445, uv);
                vec3 storedNormal = (_1431.xyz * 2.0) - vec3(1.0);
                float _288 = length(storedNormal);
                vec3 pixelNormal;
                if (_288 > 0.001000000047497451305389404296875)
                {
                    pixelNormal = storedNormal / vec3(_288);
                }
                else
                {
                    pixelNormal = vec3(0.0);
                }
                vec3 _1434;
                {
                    uvec2 _1466 = uvec2(textureSize(_2448, 0));
                    ivec2 _1473 = ivec2(int(_1466.x), int(_1466.y));
                    vec3 _1497 = ((g.uProbeWorldToView0 * (world.x - g.uProbeCameraToCache.x)) + (g.uProbeWorldToView1 * (world.y - g.uProbeCameraToCache.y))) + (g.uProbeWorldToView2 * (world.z - g.uProbeCameraToCache.z));
                    vec2 _1437;
                    if (_1497.z > g.uUnproject.z)
                    {
                        float _1869 = _1497.z;
                        _1437 = (vec2((_1497.x * g.uUnproject.x) / _1869, ((-1.0) * (_1497.y * g.uUnproject.y)) / _1869) * 0.5) + vec2(0.5);
                    }
                    else
                    {
                        _1437 = uv;
                    }
                    int _1523 = max(g.uProbeStep, 1);
                    uint _1884 = uint(g.uProbeFrame);
                    uint _1885 = _1884 & 7u;
                    vec2 _1530 = (((_1437 * vec2(float(g.uScreenWidth), float(g.uScreenHeight))) - vec2(min(ivec2(floor(((vec2(float(_1885), float((((_1884 & 1u) << uint(2)) | (_1884 & 2u)) | ((_1885 >> uint(2)) & 1u))) + vec2(0.5)) * vec2(0.125)) * float(_1523))), ivec2(_1523 - 1)))) - vec2(0.5)) / vec2(float(max(g.uProbeStep, 1)));
                    vec2 _1531 = floor(_1530);
                    vec2 _1532 = _1530 - _1531;
                    bool _1534 = dot(pixelNormal, pixelNormal) > 0.5;
                    float _1438 = 3.3999999521443642490773241379936e+38;
                    vec3 _1441 = vec3(0.0);
                    int _1444 = 0;
                    vec3 _1442 = vec3(0.0);
                    float _1439 = 0.0;
                    vec3 _1443 = vec3(0.0);
                    float _1440 = 0.0;
                    float _1445;
                    float _1446;
                    float _1447;
                    float _1448;
                    float _1449;
                    float _1450;
                    float _1451;
                    vec3 _1452;
                    vec3 _1453;
                    vec3 _1454;
                    vec3 _1455;
                    int _1456;
                    bool _1457;
                    for (;;)
                    {
                        if (!(_1444 < 2))
                        {
                            break;
                        }
                        _1445 = _1438;
                        _1452 = _1441;
                        _1456 = 0;
                        _1453 = _1442;
                        _1446 = _1439;
                        _1454 = _1443;
                        _1447 = _1440;
                        for (;;)
                        {
                            bool _1551_ladder_break = false;
                            {
                                if (!(_1456 < 2))
                                {
                                    _1551_ladder_break = true;
                                }
                                else
                                {
                                    ivec2 _1561 = ivec2(_1531) + ivec2(_1456, _1444);
                                    if (any(lessThan(_1561, ivec2(0))))
                                    {
                                        _1457 = true;
                                    }
                                    else
                                    {
                                        _1457 = any(greaterThanEqual(_1561, _1473));
                                    }
                                    if (!(_1457))
                                    {
                                        ivec2 _1573 = ivec3(_1561, 0).xy;
                                        vec4 _1575 = texelFetch(_2451, _1573, 0);
                                        if (!(_1575.w <= 0.0))
                                        {
                                            vec3 _1584 = (_1575.xyz + g.uProbeCameraToCache) - world;
                                            if (_1534)
                                            {
                                                _1451 = abs(dot(_1584, pixelNormal));
                                            }
                                            else
                                            {
                                                _1451 = length(_1584);
                                            }
                                            float _1593 = _1451 / max(_264, 0.001000000047497451305389404296875);
                                            float _1594 = _1593 * _1593;
                                            if (_1456 == 0)
                                            {
                                                _1448 = 1.0 - _1532.x;
                                            }
                                            else
                                            {
                                                _1448 = _1532.x;
                                            }
                                            if (_1444 == 0)
                                            {
                                                _1449 = 1.0 - _1532.y;
                                            }
                                            else
                                            {
                                                _1449 = _1532.y;
                                            }
                                            float _1613 = _1448 * _1449;
                                            vec3 _1617 = texelFetch(_2448, _1573, 0).xyz;
                                            float _1620 = _1613 * exp2((-200000.0) * _1594);
                                            float _1623 = _1613 * exp2((-20000.0) * _1594);
                                            float _1634 = dot(_1584, _1584);
                                            if (_1634 < _1445)
                                            {
                                                _1450 = _1634;
                                                _1455 = _1617;
                                            }
                                            else
                                            {
                                                _1450 = _1445;
                                                _1455 = _1452;
                                            }
                                            _1445 = _1450;
                                            _1452 = _1455;
                                            _1453 += (_1617 * _1620);
                                            _1446 += _1620;
                                            _1454 += (_1617 * _1623);
                                            _1447 += _1623;
                                        }
                                    }
                                }
                            }
                            if (_1551_ladder_break)
                            {
                                break;
                            }
                            _1456++;
                            continue;
                        }
                        _1438 = _1445;
                        _1441 = _1452;
                        _1444++;
                        _1442 = _1453;
                        _1439 = _1446;
                        _1443 = _1454;
                        _1440 = _1447;
                        continue;
                    }
                    int _1458 = 0;
                    _1445 = 0.0;
                    for (;;)
                    {
                        if (!(_1458 < 4))
                        {
                            break;
                        }
                        if ((_1458 & 1) == 0)
                        {
                            _1438 = 1.0 - _1532.x;
                        }
                        else
                        {
                            _1438 = _1532.x;
                        }
                        if ((_1458 >> 1) == 0)
                        {
                            _1440 = 1.0 - _1532.y;
                        }
                        else
                        {
                            _1440 = _1532.y;
                        }
                        _1458++;
                        _1445 += (_1438 * _1440);
                        continue;
                    }
                    if (_1439 > (0.60000002384185791015625 * _1445))
                    {
                        _1434 = _1442 / vec3(_1439);
                    }
                    else
                    {
                        float _1709 = max(_264, 0.001000000047497451305389404296875);
                        _1444 = -1;
                        _1452 = vec3(0.0);
                        _1446 = 0.0;
                        _1453 = vec3(0.0);
                        _1447 = 0.0;
                        _1454 = vec3(0.0);
                        _1448 = 0.0;
                        for (;;)
                        {
                            if (!(_1444 <= 2))
                            {
                                break;
                            }
                            _1456 = -1;
                            _1455 = _1452;
                            _1449 = _1446;
                            vec3 _1460 = _1453;
                            _1450 = _1447;
                            vec3 _1461 = _1454;
                            float _1459 = _1448;
                            for (;;)
                            {
                                bool _1726_ladder_break = false;
                                {
                                    if (!(_1456 <= 2))
                                    {
                                        _1726_ladder_break = true;
                                    }
                                    else
                                    {
                                        ivec2 _1736 = ivec2(_1531) + ivec2(_1456, _1444);
                                        if (any(lessThan(_1736, ivec2(0))))
                                        {
                                            _1457 = true;
                                        }
                                        else
                                        {
                                            _1457 = any(greaterThanEqual(_1736, _1473));
                                        }
                                        if (!(_1457))
                                        {
                                            ivec2 _1748 = ivec3(_1736, 0).xy;
                                            vec4 _1750 = texelFetch(_2451, _1748, 0);
                                            if (!(_1750.w <= 0.0))
                                            {
                                                vec3 _1759 = (_1750.xyz + g.uProbeCameraToCache) - world;
                                                if (_1534)
                                                {
                                                    _1451 = abs(dot(_1759, pixelNormal));
                                                }
                                                else
                                                {
                                                    _1451 = length(_1759);
                                                }
                                                float _1767 = _1451 / _1709;
                                                vec2 _1774 = abs(vec2(float(_1456), float(_1444)) - _1532);
                                                float _1783 = max(1.0 - (_1774.x * 0.5), 0.0) * max(1.0 - (_1774.y * 0.5), 0.0);
                                                vec3 _1787 = texelFetch(_2448, _1748, 0).xyz;
                                                float _1791 = _1783 * exp2(((-200000.0) * _1767) * _1767);
                                                float _1800 = _1783 * exp2(((-20000.0) * _1767) * _1767);
                                                float _1807 = length(_1759) / _1709;
                                                float _1811 = _1783 / (1.0 + ((_1807 * _1807) * 400.0));
                                                _1455 += (_1787 * _1791);
                                                _1449 += _1791;
                                                _1460 += (_1787 * _1800);
                                                _1450 += _1800;
                                                _1461 += (_1787 * _1811);
                                                _1459 += _1811;
                                            }
                                        }
                                    }
                                }
                                if (_1726_ladder_break)
                                {
                                    break;
                                }
                                _1456++;
                                continue;
                            }
                            _1444++;
                            _1452 = _1455;
                            _1446 = _1449;
                            _1453 = _1460;
                            _1447 = _1450;
                            _1454 = _1461;
                            _1448 = _1459;
                            continue;
                        }
                        if (_1446 > 9.9999997473787516355514526367188e-05)
                        {
                            _1434 = _1452 / vec3(_1446);
                        }
                        else
                        {
                            if (_1447 > 9.9999997473787516355514526367188e-05)
                            {
                                _1434 = _1453 / vec3(_1447);
                            }
                            else
                            {
                                if (_1448 > 9.9999999747524270787835121154785e-07)
                                {
                                    _1441 = _1454 / vec3(_1448);
                                }
                                _1434 = _1441;
                            }
                        }
                    }
                }
                vec2 _971 = vec2(1.0) / vec2(float(g.uScreenWidth), float(g.uScreenHeight));
                float _975 = float(max(g.uProbeStep, 2));
                int s = 0;
                vec3 m1 = _1434;
                vec3 m2 = _1434 * _1434;
                float samples = 1.0;
                vec3 historySum;
                vec2 prevUv;
                vec3 _1962;
                vec2 _1965;
                float _1973;
                float _1974;
                float _1975;
                float _1976;
                float _1977;
                float _1978;
                float _1979;
                vec3 _1980;
                vec3 _1981;
                vec3 _1982;
                vec3 _1983;
                int _1984;
                bool _1985;
                for (;;)
                {
                    bool _42_ladder_break = false;
                    {
                        if (!(s < 4))
                        {
                            _42_ladder_break = true;
                        }
                        else
                        {
                            if (s == 0)
                            {
                                prevUv = vec2(1.0, 0.0);
                            }
                            else
                            {
                                if (s == 1)
                                {
                                    prevUv = vec2(-1.0, 0.0);
                                }
                                else
                                {
                                    if (s == 2)
                                    {
                                        prevUv = vec2(0.0, 1.0);
                                    }
                                    else
                                    {
                                        prevUv = vec2(0.0, -1.0);
                                    }
                                }
                            }
                            vec2 _1011 = clamp(uv + ((prevUv * _971) * _975), vec2(0.0), vec2(1.0));
                            vec4 _1911 = texture(_2443, _1011);
                            float _1912 = _1911.x;
                            if (!(_1912 <= 0.0))
                            {
                                float _1939 = ((2.0 * g.uUnproject.z) * g.uUnproject.w) / ((g.uUnproject.w + g.uUnproject.z) - ((1.0 - (2.0 * _1912)) * (g.uUnproject.w - g.uUnproject.z)));
                                vec2 _1942 = (_1011 * 2.0) - vec2(1.0);
                                vec3 _1954 = vec3((_1942.x * _1939) / g.uUnproject.x, ((-1.0) * (_1942.y * _1939)) / g.uUnproject.y, _1939);
                                vec3 nWorld = ((g.uCameraToCache + (g.uViewToWorld0 * _1954.x)) + (g.uViewToWorld1 * _1954.y)) + (g.uViewToWorld2 * _1954.z);
                                vec4 _1959 = texture(_2445, _1011);
                                vec3 nNormal = (_1959.xyz * 2.0) - vec3(1.0);
                                float _1039 = length(nNormal);
                                if (_1039 > 0.001000000047497451305389404296875)
                                {
                                    historySum = nNormal / vec3(_1039);
                                }
                                else
                                {
                                    historySum = vec3(0.0);
                                }
                                {
                                    uvec2 _1994 = uvec2(textureSize(_2448, 0));
                                    ivec2 _2001 = ivec2(int(_1994.x), int(_1994.y));
                                    vec3 _2025 = ((g.uProbeWorldToView0 * (nWorld.x - g.uProbeCameraToCache.x)) + (g.uProbeWorldToView1 * (nWorld.y - g.uProbeCameraToCache.y))) + (g.uProbeWorldToView2 * (nWorld.z - g.uProbeCameraToCache.z));
                                    if (_2025.z > g.uUnproject.z)
                                    {
                                        float _2397 = _2025.z;
                                        _1965 = (vec2((_2025.x * g.uUnproject.x) / _2397, ((-1.0) * (_2025.y * g.uUnproject.y)) / _2397) * 0.5) + vec2(0.5);
                                    }
                                    else
                                    {
                                        _1965 = _1011;
                                    }
                                    int _2051 = max(g.uProbeStep, 1);
                                    uint _2411 = uint(g.uProbeFrame);
                                    uint _2412 = _2411 & 7u;
                                    vec2 _2058 = (((_1965 * vec2(float(g.uScreenWidth), float(g.uScreenHeight))) - vec2(min(ivec2(floor(((vec2(float(_2412), float((((_2411 & 1u) << uint(2)) | (_2411 & 2u)) | ((_2412 >> uint(2)) & 1u))) + vec2(0.5)) * vec2(0.125)) * float(_2051))), ivec2(_2051 - 1)))) - vec2(0.5)) / vec2(float(max(g.uProbeStep, 1)));
                                    vec2 _2059 = floor(_2058);
                                    vec2 _2060 = _2058 - _2059;
                                    bool _2062 = dot(historySum, historySum) > 0.5;
                                    float _1966 = 3.3999999521443642490773241379936e+38;
                                    vec3 _1969 = vec3(0.0);
                                    int _1972 = 0;
                                    vec3 _1970 = vec3(0.0);
                                    float _1967 = 0.0;
                                    vec3 _1971 = vec3(0.0);
                                    float _1968 = 0.0;
                                    for (;;)
                                    {
                                        if (!(_1972 < 2))
                                        {
                                            break;
                                        }
                                        _1973 = _1966;
                                        _1980 = _1969;
                                        _1984 = 0;
                                        _1981 = _1970;
                                        _1974 = _1967;
                                        _1982 = _1971;
                                        _1975 = _1968;
                                        for (;;)
                                        {
                                            bool _2079_ladder_break = false;
                                            {
                                                if (!(_1984 < 2))
                                                {
                                                    _2079_ladder_break = true;
                                                }
                                                else
                                                {
                                                    ivec2 _2089 = ivec2(_2059) + ivec2(_1984, _1972);
                                                    if (any(lessThan(_2089, ivec2(0))))
                                                    {
                                                        _1985 = true;
                                                    }
                                                    else
                                                    {
                                                        _1985 = any(greaterThanEqual(_2089, _2001));
                                                    }
                                                    if (!(_1985))
                                                    {
                                                        ivec2 _2101 = ivec3(_2089, 0).xy;
                                                        vec4 _2103 = texelFetch(_2451, _2101, 0);
                                                        if (!(_2103.w <= 0.0))
                                                        {
                                                            vec3 _2112 = (_2103.xyz + g.uProbeCameraToCache) - nWorld;
                                                            if (_2062)
                                                            {
                                                                _1979 = abs(dot(_2112, historySum));
                                                            }
                                                            else
                                                            {
                                                                _1979 = length(_2112);
                                                            }
                                                            float _2121 = _1979 / max(_1954.z, 0.001000000047497451305389404296875);
                                                            float _2122 = _2121 * _2121;
                                                            if (_1984 == 0)
                                                            {
                                                                _1976 = 1.0 - _2060.x;
                                                            }
                                                            else
                                                            {
                                                                _1976 = _2060.x;
                                                            }
                                                            if (_1972 == 0)
                                                            {
                                                                _1977 = 1.0 - _2060.y;
                                                            }
                                                            else
                                                            {
                                                                _1977 = _2060.y;
                                                            }
                                                            float _2141 = _1976 * _1977;
                                                            vec3 _2145 = texelFetch(_2448, _2101, 0).xyz;
                                                            float _2148 = _2141 * exp2((-200000.0) * _2122);
                                                            float _2151 = _2141 * exp2((-20000.0) * _2122);
                                                            float _2162 = dot(_2112, _2112);
                                                            if (_2162 < _1973)
                                                            {
                                                                _1978 = _2162;
                                                                _1983 = _2145;
                                                            }
                                                            else
                                                            {
                                                                _1978 = _1973;
                                                                _1983 = _1980;
                                                            }
                                                            _1973 = _1978;
                                                            _1980 = _1983;
                                                            _1981 += (_2145 * _2148);
                                                            _1974 += _2148;
                                                            _1982 += (_2145 * _2151);
                                                            _1975 += _2151;
                                                        }
                                                    }
                                                }
                                            }
                                            if (_2079_ladder_break)
                                            {
                                                break;
                                            }
                                            _1984++;
                                            continue;
                                        }
                                        _1966 = _1973;
                                        _1969 = _1980;
                                        _1972++;
                                        _1970 = _1981;
                                        _1967 = _1974;
                                        _1971 = _1982;
                                        _1968 = _1975;
                                        continue;
                                    }
                                    int _1986 = 0;
                                    _1973 = 0.0;
                                    for (;;)
                                    {
                                        if (!(_1986 < 4))
                                        {
                                            break;
                                        }
                                        if ((_1986 & 1) == 0)
                                        {
                                            _1966 = 1.0 - _2060.x;
                                        }
                                        else
                                        {
                                            _1966 = _2060.x;
                                        }
                                        if ((_1986 >> 1) == 0)
                                        {
                                            _1968 = 1.0 - _2060.y;
                                        }
                                        else
                                        {
                                            _1968 = _2060.y;
                                        }
                                        _1986++;
                                        _1973 += (_1966 * _1968);
                                        continue;
                                    }
                                    if (_1967 > (0.60000002384185791015625 * _1973))
                                    {
                                        _1962 = _1970 / vec3(_1967);
                                    }
                                    else
                                    {
                                        float _2237 = max(_1954.z, 0.001000000047497451305389404296875);
                                        _1972 = -1;
                                        _1980 = vec3(0.0);
                                        _1974 = 0.0;
                                        _1981 = vec3(0.0);
                                        _1975 = 0.0;
                                        _1982 = vec3(0.0);
                                        _1976 = 0.0;
                                        for (;;)
                                        {
                                            if (!(_1972 <= 2))
                                            {
                                                break;
                                            }
                                            _1984 = -1;
                                            _1983 = _1980;
                                            _1977 = _1974;
                                            vec3 _1988 = _1981;
                                            _1978 = _1975;
                                            vec3 _1989 = _1982;
                                            float _1987 = _1976;
                                            for (;;)
                                            {
                                                bool _2254_ladder_break = false;
                                                {
                                                    if (!(_1984 <= 2))
                                                    {
                                                        _2254_ladder_break = true;
                                                    }
                                                    else
                                                    {
                                                        ivec2 _2264 = ivec2(_2059) + ivec2(_1984, _1972);
                                                        if (any(lessThan(_2264, ivec2(0))))
                                                        {
                                                            _1985 = true;
                                                        }
                                                        else
                                                        {
                                                            _1985 = any(greaterThanEqual(_2264, _2001));
                                                        }
                                                        if (!(_1985))
                                                        {
                                                            ivec2 _2276 = ivec3(_2264, 0).xy;
                                                            vec4 _2278 = texelFetch(_2451, _2276, 0);
                                                            if (!(_2278.w <= 0.0))
                                                            {
                                                                vec3 _2287 = (_2278.xyz + g.uProbeCameraToCache) - nWorld;
                                                                if (_2062)
                                                                {
                                                                    _1979 = abs(dot(_2287, historySum));
                                                                }
                                                                else
                                                                {
                                                                    _1979 = length(_2287);
                                                                }
                                                                float _2295 = _1979 / _2237;
                                                                vec2 _2302 = abs(vec2(float(_1984), float(_1972)) - _2060);
                                                                float _2311 = max(1.0 - (_2302.x * 0.5), 0.0) * max(1.0 - (_2302.y * 0.5), 0.0);
                                                                vec3 _2315 = texelFetch(_2448, _2276, 0).xyz;
                                                                float _2319 = _2311 * exp2(((-200000.0) * _2295) * _2295);
                                                                float _2328 = _2311 * exp2(((-20000.0) * _2295) * _2295);
                                                                float _2335 = length(_2287) / _2237;
                                                                float _2339 = _2311 / (1.0 + ((_2335 * _2335) * 400.0));
                                                                _1983 += (_2315 * _2319);
                                                                _1977 += _2319;
                                                                _1988 += (_2315 * _2328);
                                                                _1978 += _2328;
                                                                _1989 += (_2315 * _2339);
                                                                _1987 += _2339;
                                                            }
                                                        }
                                                    }
                                                }
                                                if (_2254_ladder_break)
                                                {
                                                    break;
                                                }
                                                _1984++;
                                                continue;
                                            }
                                            _1972++;
                                            _1980 = _1983;
                                            _1974 = _1977;
                                            _1981 = _1988;
                                            _1975 = _1978;
                                            _1982 = _1989;
                                            _1976 = _1987;
                                            continue;
                                        }
                                        if (_1974 > 9.9999997473787516355514526367188e-05)
                                        {
                                            _1962 = _1980 / vec3(_1974);
                                        }
                                        else
                                        {
                                            if (_1975 > 9.9999997473787516355514526367188e-05)
                                            {
                                                _1962 = _1981 / vec3(_1975);
                                            }
                                            else
                                            {
                                                if (_1976 > 9.9999999747524270787835121154785e-07)
                                                {
                                                    _1969 = _1982 / vec3(_1976);
                                                }
                                                _1962 = _1969;
                                            }
                                        }
                                    }
                                }
                                m1 += _1962;
                                m2 += (_1962 * _1962);
                                samples += 1.0;
                            }
                        }
                    }
                    if (_42_ladder_break)
                    {
                        break;
                    }
                    s++;
                    continue;
                }
                float _1069 = samples;
                vec3 mean = m1 / vec3(_1069);
                vec3 _1081 = sqrt(max((m2 / vec3(_1069)) - (mean * mean), vec3(0.0))) * 1.5;
                vec3 accumulated;
                float frames;
                if (g.uHasHistory != 0)
                {
                    vec3 prevView = ((g.uPrevWorldToView0 * (world.x - g.uPrevCameraToCache.x)) + (g.uPrevWorldToView1 * (world.y - g.uPrevCameraToCache.y))) + (g.uPrevWorldToView2 * (world.z - g.uPrevCameraToCache.z));
                    float _1118 = prevView.z;
                    if (_1118 > 0.0)
                    {
                        vec2 prevUv_1 = (vec2((prevView.x * g.uPrevUnproject.x) / _1118, ((-1.0) * (prevView.y * g.uPrevUnproject.y)) / _1118) * 0.5) + vec2(0.5);
                        if (g.uHasVelocity != 0)
                        {
                            vec2 motion = textureLod(_2453, uv, 0.0).xy;
                            vec2 apart = (motion - (uv - prevUv_1)) * vec2(float(g.uScreenWidth), float(g.uScreenHeight));
                            bool _1159 = dot(apart, apart) > 2.25;
                            if (_1159)
                            {
                                prevUv = uv - motion;
                            }
                            else
                            {
                                prevUv = prevUv_1;
                            }
                            ownMotion = _1159;
                        }
                        else
                        {
                            prevUv = prevUv_1;
                            ownMotion = false;
                        }
                        bool _25;
                        if (all(greaterThanEqual(prevUv, vec2(0.0))))
                        {
                            _25 = all(lessThanEqual(prevUv, vec2(1.0)));
                        }
                        else
                        {
                            _25 = false;
                        }
                        if (_25)
                        {
                            vec2 tc = (prevUv * vec2(float(g.uScreenWidth), float(g.uScreenHeight))) - vec2(0.5);
                            vec2 _1197 = floor(tc);
                            vec2 _1198 = tc - _1197;
                            float _1206 = (2.0 * _264) / (g.uUnproject.y * float(g.uScreenHeight));
                            int t = 0;
                            historySum = vec3(0.0);
                            float frameSum = 0.0;
                            float weightSum = 0.0;
                            bool _29;
                            for (;;)
                            {
                                bool _78_ladder_break = false;
                                {
                                    if (!(t < 4))
                                    {
                                        _78_ladder_break = true;
                                    }
                                    else
                                    {
                                        int _1216 = t & 1;
                                        int _1218 = t >> 1;
                                        ivec2 tap = ivec2(_1197) + ivec2(_1216, _1218);
                                        if (any(lessThan(tap, ivec2(0))))
                                        {
                                            _25 = true;
                                        }
                                        else
                                        {
                                            _25 = any(greaterThanEqual(tap, ivec2(g.uScreenWidth, g.uScreenHeight)));
                                        }
                                        if (!(_25))
                                        {
                                            ivec2 _1238 = ivec3(tap, 0).xy;
                                            vec4 sampled = texelFetch(_2456, _1238, 0);
                                            if (!(sampled.w <= 0.0))
                                            {
                                                if (!ownMotion)
                                                {
                                                    _29 = abs(dot((sampled.xyz + g.uPrevCameraToCache) - world, pixelNormal)) > max(0.0199999995529651641845703125, 4.0 * _1206);
                                                }
                                                else
                                                {
                                                    _29 = false;
                                                }
                                                if (!(_29))
                                                {
                                                    if (_1216 == 0)
                                                    {
                                                        samples = 1.0 - _1198.x;
                                                    }
                                                    else
                                                    {
                                                        samples = _1198.x;
                                                    }
                                                    if (_1218 == 0)
                                                    {
                                                        frames = 1.0 - _1198.y;
                                                    }
                                                    else
                                                    {
                                                        frames = _1198.y;
                                                    }
                                                    float w = samples * frames;
                                                    vec4 sampled_1 = texelFetch(_2459, _1238, 0);
                                                    historySum += (sampled_1.xyz * w);
                                                    frameSum += (sampled_1.w * w);
                                                    weightSum += w;
                                                }
                                            }
                                        }
                                    }
                                }
                                if (_78_ladder_break)
                                {
                                    break;
                                }
                                t++;
                                continue;
                            }
                            if (weightSum > 0.00999999977648258209228515625)
                            {
                                float _1324 = frameSum / weightSum;
                                float _1334 = min(_1324 + 1.0, float(g.uMaxFrames));
                                accumulated = mix(clamp(vec4(historySum / vec3(weightSum), _1324).xyz, mean - _1081, mean + _1081), _1434, vec3(1.0 / (1.0 + _1334)));
                                frames = _1334;
                            }
                            else
                            {
                                accumulated = _1434;
                                frames = 0.0;
                            }
                        }
                        else
                        {
                            accumulated = _1434;
                            frames = 0.0;
                        }
                    }
                    else
                    {
                        accumulated = _1434;
                        frames = 0.0;
                    }
                }
                else
                {
                    accumulated = _1434;
                    frames = 0.0;
                }
                imageStore(uAccum, ivec2(gl_GlobalInvocationID.xy), vec4(accumulated, frames));
                imageStore(uAccumPosition, ivec2(gl_GlobalInvocationID.xy), vec4(world - g.uCameraToCache, 1.0));
            }
        }
    }
}

