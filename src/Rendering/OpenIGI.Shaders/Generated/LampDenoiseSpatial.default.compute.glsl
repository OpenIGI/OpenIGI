#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    layout(row_major) mat4 viewToWorld;
    vec4 unproject;
    vec4 resolution;
    vec4 jitter;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uOutput;
layout(binding = 0) uniform sampler2D _2068;
layout(binding = 1) uniform sampler2D _2070;
layout(binding = 2) uniform sampler2D _2073;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    {
        ivec2 _434 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 _445 = ivec2(p.resolution.xy);
        int _446 = _434.x;
        bool inPenumbra;
        if (_446 >= _445.x)
        {
            inPenumbra = true;
        }
        else
        {
            inPenumbra = _434.y >= _445.y;
        }
        if (!(inPenumbra))
        {
            vec4 _1691 = texelFetch(_2068, ivec3(_434, 0).xy, 0);
            float _477 = _1691.y;
            if (_477 <= 0.0)
            {
                imageStore(uOutput, ivec2(gl_GlobalInvocationID.xy), vec4(1.0, 0.0, 0.0, 0.0));
            }
            else
            {
                float _490 = _1691.z;
                if (_490 <= 0.0)
                {
                    imageStore(uOutput, ivec2(gl_GlobalInvocationID.xy), vec4(_1691.x, _477, 0.0, 0.0));
                }
                else
                {
                    vec2 _1705 = ((vec2(_434) + vec2(0.5)) - p.jitter.xy) * p.resolution.zw;
                    vec4 _1710 = textureLod(_2070, _1705, 0.0);
                    float _1711 = _1710.x;
                    if (_1711 <= 0.0)
                    {
                        imageStore(uOutput, ivec2(gl_GlobalInvocationID.xy), vec4(_1691.x, _477, 0.0, 0.0));
                    }
                    else
                    {
                        float _1741 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1711)) * (p.unproject.w - p.unproject.z)));
                        vec2 _1718 = (_1705 * 2.0) - vec2(1.0);
                        vec3 _1728 = vec3((_1718.x * _1741) / p.unproject.x, ((-_1718.y) * _1741) / p.unproject.y, _1741);
                        vec3 _594 = (vec4(_1728, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz;
                        uvec2 _1747 = uvec2(textureSize(_2073, 0));
                        uint _1748 = _1747.x;
                        uint _1749 = _1747.y;
                        vec3 _1775 = normalize((texelFetch(_2073, ivec3(clamp(ivec2(floor(_1705 * vec2(float(_1748), float(_1749)))), ivec2(0), ivec2(int(_1748) - 1, int(_1749) - 1)), 0).xy, 0).xyz * 2.0) - vec3(1.0));
                        float disocclusion = 1.0 - clamp((_490 - 1.0) * 0.5, 0.0, 1.0);
                        ivec2 _643 = _445 - ivec2(1);
                        float _656;
                        float weightSum;
                        float visSum;
                        float visSquares;
                        {
                            vec4 _1781 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(-2), ivec2(0), _643), 0).xy, 0);
                            float _655 = _1781.y;
                            _656 = max(_477, _655);
                            if (_655 <= 0.0)
                            {
                                weightSum = 0.0;
                                visSum = 0.0;
                                visSquares = 0.0;
                            }
                            else
                            {
                                float _665 = _1781.x;
                                weightSum = 0.2916322648525238037109375;
                                visSum = _665 * 0.2916322648525238037109375;
                                visSquares = (_665 * _665) * 0.2916322648525238037109375;
                            }
                        }
                        float _684;
                        {
                            vec4 _1788 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(-1, -2), ivec2(0), _643), 0).xy, 0);
                            float _683 = _1788.y;
                            _684 = max(_656, _683);
                            if (!(_683 <= 0.0))
                            {
                                float _692 = _1788.x;
                                weightSum += 0.4629373252391815185546875;
                                visSum += (_692 * 0.4629373252391815185546875);
                                visSquares += ((_692 * _692) * 0.4629373252391815185546875);
                            }
                        }
                        float _714;
                        {
                            vec4 _1795 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(0, -2), ivec2(0), _643), 0).xy, 0);
                            float _713 = _1795.y;
                            _714 = max(_684, _713);
                            if (!(_713 <= 0.0))
                            {
                                float _722 = _1795.x;
                                weightSum += 0.54002988338470458984375;
                                visSum += (_722 * 0.54002988338470458984375);
                                visSquares += ((_722 * _722) * 0.54002988338470458984375);
                            }
                        }
                        float _744;
                        {
                            vec4 _1802 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(1, -2), ivec2(0), _643), 0).xy, 0);
                            float _743 = _1802.y;
                            _744 = max(_714, _743);
                            if (!(_743 <= 0.0))
                            {
                                float _751 = _1802.x;
                                weightSum += 0.4629373252391815185546875;
                                visSum += (_751 * 0.4629373252391815185546875);
                                visSquares += ((_751 * _751) * 0.4629373252391815185546875);
                            }
                        }
                        float _773;
                        {
                            vec4 _1809 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(2, -2), ivec2(0), _643), 0).xy, 0);
                            float _772 = _1809.y;
                            _773 = max(_744, _772);
                            if (!(_772 <= 0.0))
                            {
                                float _780 = _1809.x;
                                weightSum += 0.2916322648525238037109375;
                                visSum += (_780 * 0.2916322648525238037109375);
                                visSquares += ((_780 * _780) * 0.2916322648525238037109375);
                            }
                        }
                        float _809;
                        {
                            vec4 _1816 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(-2, -1), ivec2(0), _643), 0).xy, 0);
                            float _808 = _1816.y;
                            _809 = max(_773, _808);
                            if (!(_808 <= 0.0))
                            {
                                float _816 = _1816.x;
                                weightSum += 0.4629373252391815185546875;
                                visSum += (_816 * 0.4629373252391815185546875);
                                visSquares += ((_816 * _816) * 0.4629373252391815185546875);
                            }
                        }
                        float _838;
                        {
                            vec4 _1823 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(-1), ivec2(0), _643), 0).xy, 0);
                            float _837 = _1823.y;
                            _838 = max(_809, _837);
                            if (!(_837 <= 0.0))
                            {
                                float _846 = _1823.x;
                                weightSum += 0.73486721515655517578125;
                                visSum += (_846 * 0.73486721515655517578125);
                                visSquares += ((_846 * _846) * 0.73486721515655517578125);
                            }
                        }
                        float _868;
                        {
                            vec4 _1830 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(0, -1), ivec2(0), _643), 0).xy, 0);
                            float _867 = _1830.y;
                            _868 = max(_838, _867);
                            if (!(_867 <= 0.0))
                            {
                                float _876 = _1830.x;
                                weightSum += 0.857243955135345458984375;
                                visSum += (_876 * 0.857243955135345458984375);
                                visSquares += ((_876 * _876) * 0.857243955135345458984375);
                            }
                        }
                        float _898;
                        {
                            vec4 _1837 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(1, -1), ivec2(0), _643), 0).xy, 0);
                            float _897 = _1837.y;
                            _898 = max(_868, _897);
                            if (!(_897 <= 0.0))
                            {
                                float _905 = _1837.x;
                                weightSum += 0.73486721515655517578125;
                                visSum += (_905 * 0.73486721515655517578125);
                                visSquares += ((_905 * _905) * 0.73486721515655517578125);
                            }
                        }
                        float _927;
                        {
                            vec4 _1844 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(2, -1), ivec2(0), _643), 0).xy, 0);
                            float _926 = _1844.y;
                            _927 = max(_898, _926);
                            if (!(_926 <= 0.0))
                            {
                                float _934 = _1844.x;
                                weightSum += 0.4629373252391815185546875;
                                visSum += (_934 * 0.4629373252391815185546875);
                                visSquares += ((_934 * _934) * 0.4629373252391815185546875);
                            }
                        }
                        float _963;
                        {
                            vec4 _1851 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(-2, 0), ivec2(0), _643), 0).xy, 0);
                            float _962 = _1851.y;
                            _963 = max(_927, _962);
                            if (!(_962 <= 0.0))
                            {
                                float _970 = _1851.x;
                                weightSum += 0.54002988338470458984375;
                                visSum += (_970 * 0.54002988338470458984375);
                                visSquares += ((_970 * _970) * 0.54002988338470458984375);
                            }
                        }
                        float _992;
                        {
                            vec4 _1858 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(-1, 0), ivec2(0), _643), 0).xy, 0);
                            float _991 = _1858.y;
                            _992 = max(_963, _991);
                            if (!(_991 <= 0.0))
                            {
                                float _999 = _1858.x;
                                weightSum += 0.857243955135345458984375;
                                visSum += (_999 * 0.857243955135345458984375);
                                visSquares += ((_999 * _999) * 0.857243955135345458984375);
                            }
                        }
                        float _1019;
                        {
                            vec4 _1865 = texelFetch(_2068, ivec3(clamp(_434, ivec2(0), _643), 0).xy, 0);
                            float _1018 = _1865.y;
                            _1019 = max(_992, _1018);
                            if (!(_1018 <= 0.0))
                            {
                                float _1027 = _1865.x;
                                weightSum += 1.0;
                                visSum += _1027;
                                visSquares += (_1027 * _1027);
                            }
                        }
                        float _1049;
                        {
                            vec4 _1872 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(1, 0), ivec2(0), _643), 0).xy, 0);
                            float _1048 = _1872.y;
                            _1049 = max(_1019, _1048);
                            if (!(_1048 <= 0.0))
                            {
                                float _1056 = _1872.x;
                                weightSum += 0.857243955135345458984375;
                                visSum += (_1056 * 0.857243955135345458984375);
                                visSquares += ((_1056 * _1056) * 0.857243955135345458984375);
                            }
                        }
                        float _1078;
                        {
                            vec4 _1879 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(2, 0), ivec2(0), _643), 0).xy, 0);
                            float _1077 = _1879.y;
                            _1078 = max(_1049, _1077);
                            if (!(_1077 <= 0.0))
                            {
                                float _1085 = _1879.x;
                                weightSum += 0.54002988338470458984375;
                                visSum += (_1085 * 0.54002988338470458984375);
                                visSquares += ((_1085 * _1085) * 0.54002988338470458984375);
                            }
                        }
                        float _1114;
                        {
                            vec4 _1886 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(-2, 1), ivec2(0), _643), 0).xy, 0);
                            float _1113 = _1886.y;
                            _1114 = max(_1078, _1113);
                            if (!(_1113 <= 0.0))
                            {
                                float _1121 = _1886.x;
                                weightSum += 0.4629373252391815185546875;
                                visSum += (_1121 * 0.4629373252391815185546875);
                                visSquares += ((_1121 * _1121) * 0.4629373252391815185546875);
                            }
                        }
                        float _1143;
                        {
                            vec4 _1893 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(-1, 1), ivec2(0), _643), 0).xy, 0);
                            float _1142 = _1893.y;
                            _1143 = max(_1114, _1142);
                            if (!(_1142 <= 0.0))
                            {
                                float _1150 = _1893.x;
                                weightSum += 0.73486721515655517578125;
                                visSum += (_1150 * 0.73486721515655517578125);
                                visSquares += ((_1150 * _1150) * 0.73486721515655517578125);
                            }
                        }
                        float _1172;
                        {
                            vec4 _1900 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(0, 1), ivec2(0), _643), 0).xy, 0);
                            float _1171 = _1900.y;
                            _1172 = max(_1143, _1171);
                            if (!(_1171 <= 0.0))
                            {
                                float _1179 = _1900.x;
                                weightSum += 0.857243955135345458984375;
                                visSum += (_1179 * 0.857243955135345458984375);
                                visSquares += ((_1179 * _1179) * 0.857243955135345458984375);
                            }
                        }
                        float _1201;
                        {
                            vec4 _1907 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(1), ivec2(0), _643), 0).xy, 0);
                            float _1200 = _1907.y;
                            _1201 = max(_1172, _1200);
                            if (!(_1200 <= 0.0))
                            {
                                float _1208 = _1907.x;
                                weightSum += 0.73486721515655517578125;
                                visSum += (_1208 * 0.73486721515655517578125);
                                visSquares += ((_1208 * _1208) * 0.73486721515655517578125);
                            }
                        }
                        float _1230;
                        {
                            vec4 _1914 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(2, 1), ivec2(0), _643), 0).xy, 0);
                            float _1229 = _1914.y;
                            _1230 = max(_1201, _1229);
                            if (!(_1229 <= 0.0))
                            {
                                float _1237 = _1914.x;
                                weightSum += 0.4629373252391815185546875;
                                visSum += (_1237 * 0.4629373252391815185546875);
                                visSquares += ((_1237 * _1237) * 0.4629373252391815185546875);
                            }
                        }
                        float _1266;
                        {
                            vec4 _1921 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(-2, 2), ivec2(0), _643), 0).xy, 0);
                            float _1265 = _1921.y;
                            _1266 = max(_1230, _1265);
                            if (!(_1265 <= 0.0))
                            {
                                float _1273 = _1921.x;
                                weightSum += 0.2916322648525238037109375;
                                visSum += (_1273 * 0.2916322648525238037109375);
                                visSquares += ((_1273 * _1273) * 0.2916322648525238037109375);
                            }
                        }
                        float _1295;
                        {
                            vec4 _1928 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(-1, 2), ivec2(0), _643), 0).xy, 0);
                            float _1294 = _1928.y;
                            _1295 = max(_1266, _1294);
                            if (!(_1294 <= 0.0))
                            {
                                float _1302 = _1928.x;
                                weightSum += 0.4629373252391815185546875;
                                visSum += (_1302 * 0.4629373252391815185546875);
                                visSquares += ((_1302 * _1302) * 0.4629373252391815185546875);
                            }
                        }
                        float _1324;
                        {
                            vec4 _1935 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(0, 2), ivec2(0), _643), 0).xy, 0);
                            float _1323 = _1935.y;
                            _1324 = max(_1295, _1323);
                            if (!(_1323 <= 0.0))
                            {
                                float _1331 = _1935.x;
                                weightSum += 0.54002988338470458984375;
                                visSum += (_1331 * 0.54002988338470458984375);
                                visSquares += ((_1331 * _1331) * 0.54002988338470458984375);
                            }
                        }
                        float _1353;
                        {
                            vec4 _1942 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(1, 2), ivec2(0), _643), 0).xy, 0);
                            float _1352 = _1942.y;
                            _1353 = max(_1324, _1352);
                            if (!(_1352 <= 0.0))
                            {
                                float _1360 = _1942.x;
                                weightSum += 0.4629373252391815185546875;
                                visSum += (_1360 * 0.4629373252391815185546875);
                                visSquares += ((_1360 * _1360) * 0.4629373252391815185546875);
                            }
                        }
                        float _1382;
                        {
                            vec4 _1949 = texelFetch(_2068, ivec3(clamp(_434 + ivec2(2), ivec2(0), _643), 0).xy, 0);
                            float _1381 = _1949.y;
                            _1382 = max(_1353, _1381);
                            if (!(_1381 <= 0.0))
                            {
                                float _1389 = _1949.x;
                                weightSum += 0.2916322648525238037109375;
                                visSum += (_1389 * 0.2916322648525238037109375);
                                visSquares += ((_1389 * _1389) * 0.2916322648525238037109375);
                            }
                        }
                        float _1410 = weightSum;
                        float mean = visSum / max(_1410, 9.9999999747524270787835121154785e-07);
                        float sigma = sqrt(max((visSquares / max(_1410, 9.9999999747524270787835121154785e-07)) - (mean * mean), 0.0)) * mix(1.0, 2.0, disocclusion);
                        float _1434 = _1728.z;
                        float _1440 = clamp((0.5 * _1382) * (((p.unproject.x * 0.5) * p.resolution.x) / max(_1434, 0.001000000047497451305389404296875)), 1.0, 24.0);
                        if (mean > 0.0199999995529651641845703125)
                        {
                            inPenumbra = mean < 0.980000019073486328125;
                        }
                        else
                        {
                            inPenumbra = false;
                        }
                        if (inPenumbra)
                        {
                            inPenumbra = _1440 > 1.5;
                        }
                        else
                        {
                            inPenumbra = false;
                        }
                        vec2 _1463 = _1691.xy;
                        bool _13;
                        if (p.jitter.w > 0.5)
                        {
                            if (inPenumbra)
                            {
                                _13 = true;
                            }
                            else
                            {
                                _13 = (sigma / max(mean, 0.100000001490116119384765625)) > mix(0.1500000059604644775390625, 0.100000001490116119384765625, disocclusion);
                            }
                        }
                        else
                        {
                            _13 = false;
                        }
                        vec2 sum;
                        if (_13)
                        {
                            int _19;
                            if (inPenumbra)
                            {
                                _19 = 16;
                            }
                            else
                            {
                                _19 = int(round(mix(12.0, 24.0, disocclusion)));
                            }
                            if (inPenumbra)
                            {
                                weightSum = _1440;
                            }
                            else
                            {
                                weightSum = 8.0;
                            }
                            if (!inPenumbra)
                            {
                                inPenumbra = _490 >= 6.0;
                            }
                            else
                            {
                                inPenumbra = false;
                            }
                            uint seed = (uint(_446 & 1) | (uint(_434.y & 1) << uint(1))) | (uint(p.jitter.z) << uint(2));
                            uint _1955 = (seed ^ (seed >> uint(16))) * 2146121005u;
                            uint _1958 = (_1955 ^ (_1955 >> uint(15))) * 2221713035u;
                            float _1547 = (float((_1958 ^ (_1958 >> uint(16))) >> uint(8)) * 5.9604644775390625e-08) * 6.283185482025146484375;
                            int i = 0;
                            sum = _1463;
                            float total = 1.0;
                            float valueWeight;
                            for (;;)
                            {
                                bool _402_ladder_break = false;
                                {
                                    if (!(i < 24))
                                    {
                                        _402_ladder_break = true;
                                    }
                                    else
                                    {
                                        if (i >= _19)
                                        {
                                            _402_ladder_break = true;
                                        }
                                        else
                                        {
                                            float _1562 = float(i);
                                            float angle = _1547 + (_1562 * 2.3999631404876708984375);
                                            ivec2 _1582 = ivec2(floor((vec2(_434) + (vec2(cos(angle), sin(angle)) * (weightSum * sqrt((_1562 + 0.5) / float(_19))))) + vec2(0.5)));
                                            if (any(lessThan(_1582, ivec2(0))))
                                            {
                                                _13 = true;
                                            }
                                            else
                                            {
                                                _13 = any(greaterThanEqual(_1582, _445));
                                            }
                                            if (!(_13))
                                            {
                                                vec4 _1971 = texelFetch(_2068, ivec3(_1582, 0).xy, 0);
                                                if (!(_1971.y <= 0.0))
                                                {
                                                    vec2 _1985 = ((vec2(_1582) + vec2(0.5)) - p.jitter.xy) * p.resolution.zw;
                                                    vec4 _1990 = textureLod(_2070, _1985, 0.0);
                                                    float _1991 = _1990.x;
                                                    if (!(_1991 <= 0.0))
                                                    {
                                                        float _2020 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1991)) * (p.unproject.w - p.unproject.z)));
                                                        vec2 _1997 = (_1985 * 2.0) - vec2(1.0);
                                                        float planeDistance = abs(dot((vec4(vec3((_1997.x * _2020) / p.unproject.x, ((-_1997.y) * _2020) / p.unproject.y, _2020), 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz - _594, _1775)) / max(_1434, 9.9999997473787516355514526367188e-05);
                                                        uvec2 _2026 = uvec2(textureSize(_2073, 0));
                                                        uint _2027 = _2026.x;
                                                        uint _2028 = _2026.y;
                                                        if (inPenumbra)
                                                        {
                                                            valueWeight = exp2((-abs(_1691.x - _1971.x)) / max(sigma, 0.001000000047497451305389404296875));
                                                        }
                                                        else
                                                        {
                                                            valueWeight = 1.0;
                                                        }
                                                        float weight = (exp2(((-10000.0) * planeDistance) * planeDistance) * (1.0 - clamp(acos(clamp(dot(_1775, normalize((texelFetch(_2073, ivec3(clamp(ivec2(floor(_1985 * vec2(float(_2027), float(_2028)))), ivec2(0), ivec2(int(_2027) - 1, int(_2028) - 1)), 0).xy, 0).xyz * 2.0) - vec3(1.0))), -1.0, 1.0)), 0.0, 1.0))) * valueWeight;
                                                        sum += (_1971.xy * weight);
                                                        total += weight;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                if (_402_ladder_break)
                                {
                                    break;
                                }
                                i++;
                                continue;
                            }
                            sum /= vec2(total);
                        }
                        else
                        {
                            sum = _1463;
                        }
                        imageStore(uOutput, ivec2(gl_GlobalInvocationID.xy), vec4(clamp(sum.x, 0.0, 1.0), clamp(sum.y, 0.0, 1.0), _1434, 0.0));
                    }
                }
            }
        }
    }
}

