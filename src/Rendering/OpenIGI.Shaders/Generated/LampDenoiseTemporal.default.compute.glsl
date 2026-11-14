#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    layout(row_major) mat4 viewToWorld;
    layout(row_major) mat4 prevWorldToView;
    vec4 unproject;
    vec4 prevUnproject;
    vec4 resolution;
    vec4 cameraDelta;
    vec4 jitter;
    vec4 control;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uOutput;
layout(binding = 1) uniform sampler2D _3391;
layout(binding = 0) uniform sampler2D _3394;
layout(binding = 3) uniform sampler2D _3397;
layout(binding = 2) uniform sampler2D _3400;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    {
        ivec2 _116 = ivec2(gl_GlobalInvocationID.xy);
        bool ownMotion;
        if (_116.x >= int(p.resolution.x))
        {
            ownMotion = true;
        }
        else
        {
            ownMotion = _116.y >= int(p.resolution.y);
        }
        if (!(ownMotion))
        {
            vec2 _2057 = ((vec2(_116) + vec2(0.5)) - p.jitter.xy) * p.resolution.zw;
            vec4 _2062 = textureLod(_3391, _2057, 0.0);
            float _2063 = _2062.x;
            if (_2063 <= 0.0)
            {
                imageStore(uOutput, ivec2(gl_GlobalInvocationID.xy), vec4(1.0, 0.0, 0.0, 0.0));
            }
            else
            {
                float _2093 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _2063)) * (p.unproject.w - p.unproject.z)));
                vec2 _2070 = (_2057 * 2.0) - vec2(1.0);
                vec3 _2080 = vec3((_2070.x * _2093) / p.unproject.x, ((-_2070.y) * _2093) / p.unproject.y, _2093);
                vec4 _2107 = texelFetch(_3394, ivec3(clamp(_116, ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                vec2 current = _2107.xy;
                if (_2107.y <= 0.0)
                {
                    imageStore(uOutput, ivec2(gl_GlobalInvocationID.xy), vec4(1.0, 0.0, 1.0, _2080.z));
                }
                else
                {
                    bool _2131;
                    bool _2109;
                    vec2 _2110;
                    vec2 _2111;
                    float _2112;
                    {
                        _2131 = abs(-2) == 2;
                        if (_2131)
                        {
                            _2109 = _2131;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (_2109)
                        {
                            _2110 = vec2(0.0);
                            _2111 = vec2(0.0);
                            _2112 = 0.0;
                        }
                        else
                        {
                            vec4 _2955 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(-2), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2140 = _2955.xy;
                            if (_2955.y <= 0.0)
                            {
                                _2110 = vec2(0.0);
                                _2111 = vec2(0.0);
                                _2112 = 0.0;
                            }
                            else
                            {
                                _2110 = _2140;
                                _2111 = _2140 * _2140;
                                _2112 = 1.0;
                            }
                        }
                    }
                    bool _2156;
                    {
                        _2156 = abs(-1) == 2;
                        if (_2156)
                        {
                            _2109 = _2131;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _2969 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(-1, -2), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2165 = _2969.xy;
                            if (!(_2969.y <= 0.0))
                            {
                                _2110 += _2165;
                                _2111 += (_2165 * _2165);
                                _2112 += 1.0;
                            }
                        }
                    }
                    bool _2187;
                    {
                        _2187 = abs(0) == 2;
                        if (_2187)
                        {
                            _2109 = _2131;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _2983 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(0, -2), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2196 = _2983.xy;
                            if (!(_2983.y <= 0.0))
                            {
                                _2110 += _2196;
                                _2111 += (_2196 * _2196);
                                _2112 += 1.0;
                            }
                        }
                    }
                    bool _2218;
                    {
                        _2218 = abs(1) == 2;
                        if (_2218)
                        {
                            _2109 = _2131;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _2997 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(1, -2), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2227 = _2997.xy;
                            if (!(_2997.y <= 0.0))
                            {
                                _2110 += _2227;
                                _2111 += (_2227 * _2227);
                                _2112 += 1.0;
                            }
                        }
                    }
                    bool _2249;
                    {
                        _2249 = abs(2) == 2;
                        if (_2249)
                        {
                            _2109 = _2131;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3011 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(2, -2), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2258 = _3011.xy;
                            if (!(_3011.y <= 0.0))
                            {
                                _2110 += _2258;
                                _2111 += (_2258 * _2258);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2131)
                        {
                            _2109 = _2156;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3025 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(-2, -1), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2300 = _3025.xy;
                            if (!(_3025.y <= 0.0))
                            {
                                _2110 += _2300;
                                _2111 += (_2300 * _2300);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2156)
                        {
                            _2109 = _2156;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3039 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(-1), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2329 = _3039.xy;
                            if (!(_3039.y <= 0.0))
                            {
                                _2110 += _2329;
                                _2111 += (_2329 * _2329);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2187)
                        {
                            _2109 = _2156;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3053 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(0, -1), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2358 = _3053.xy;
                            if (!(_3053.y <= 0.0))
                            {
                                _2110 += _2358;
                                _2111 += (_2358 * _2358);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2218)
                        {
                            _2109 = _2156;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3067 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(1, -1), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2387 = _3067.xy;
                            if (!(_3067.y <= 0.0))
                            {
                                _2110 += _2387;
                                _2111 += (_2387 * _2387);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2249)
                        {
                            _2109 = _2156;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3081 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(2, -1), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2416 = _3081.xy;
                            if (!(_3081.y <= 0.0))
                            {
                                _2110 += _2416;
                                _2111 += (_2416 * _2416);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2131)
                        {
                            _2109 = _2187;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3095 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(-2, 0), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2458 = _3095.xy;
                            if (!(_3095.y <= 0.0))
                            {
                                _2110 += _2458;
                                _2111 += (_2458 * _2458);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2156)
                        {
                            _2109 = _2187;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3109 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(-1, 0), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2487 = _3109.xy;
                            if (!(_3109.y <= 0.0))
                            {
                                _2110 += _2487;
                                _2111 += (_2487 * _2487);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2187)
                        {
                            _2109 = _2187;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3123 = texelFetch(_3394, ivec3(clamp(_116, ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2515 = _3123.xy;
                            if (!(_3123.y <= 0.0))
                            {
                                _2110 += _2515;
                                _2111 += (_2515 * _2515);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2218)
                        {
                            _2109 = _2187;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3137 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(1, 0), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2544 = _3137.xy;
                            if (!(_3137.y <= 0.0))
                            {
                                _2110 += _2544;
                                _2111 += (_2544 * _2544);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2249)
                        {
                            _2109 = _2187;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3151 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(2, 0), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2573 = _3151.xy;
                            if (!(_3151.y <= 0.0))
                            {
                                _2110 += _2573;
                                _2111 += (_2573 * _2573);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2131)
                        {
                            _2109 = _2218;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3165 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(-2, 1), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2615 = _3165.xy;
                            if (!(_3165.y <= 0.0))
                            {
                                _2110 += _2615;
                                _2111 += (_2615 * _2615);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2156)
                        {
                            _2109 = _2218;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3179 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(-1, 1), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2644 = _3179.xy;
                            if (!(_3179.y <= 0.0))
                            {
                                _2110 += _2644;
                                _2111 += (_2644 * _2644);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2187)
                        {
                            _2109 = _2218;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3193 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(0, 1), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2673 = _3193.xy;
                            if (!(_3193.y <= 0.0))
                            {
                                _2110 += _2673;
                                _2111 += (_2673 * _2673);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2218)
                        {
                            _2109 = _2218;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3207 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(1), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2702 = _3207.xy;
                            if (!(_3207.y <= 0.0))
                            {
                                _2110 += _2702;
                                _2111 += (_2702 * _2702);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2249)
                        {
                            _2109 = _2218;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3221 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(2, 1), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2731 = _3221.xy;
                            if (!(_3221.y <= 0.0))
                            {
                                _2110 += _2731;
                                _2111 += (_2731 * _2731);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2131)
                        {
                            _2109 = _2249;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3235 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(-2, 2), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2773 = _3235.xy;
                            if (!(_3235.y <= 0.0))
                            {
                                _2110 += _2773;
                                _2111 += (_2773 * _2773);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2156)
                        {
                            _2109 = _2249;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3249 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(-1, 2), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2802 = _3249.xy;
                            if (!(_3249.y <= 0.0))
                            {
                                _2110 += _2802;
                                _2111 += (_2802 * _2802);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2187)
                        {
                            _2109 = _2249;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3263 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(0, 2), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2831 = _3263.xy;
                            if (!(_3263.y <= 0.0))
                            {
                                _2110 += _2831;
                                _2111 += (_2831 * _2831);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2218)
                        {
                            _2109 = _2249;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3277 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(1, 2), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2860 = _3277.xy;
                            if (!(_3277.y <= 0.0))
                            {
                                _2110 += _2860;
                                _2111 += (_2860 * _2860);
                                _2112 += 1.0;
                            }
                        }
                    }
                    {
                        if (_2249)
                        {
                            _2109 = _2249;
                        }
                        else
                        {
                            _2109 = false;
                        }
                        if (!(_2109))
                        {
                            vec4 _3291 = texelFetch(_3394, ivec3(clamp(_116 + ivec2(2), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                            vec2 _2889 = _3291.xy;
                            if (!(_3291.y <= 0.0))
                            {
                                _2112 += 1.0;
                                _2110 += _2889;
                                _2111 += (_2889 * _2889);
                            }
                        }
                    }
                    float _2912 = max(_2112, 1.0);
                    vec2 _2915 = _2110 / vec2(_2912);
                    vec2 _2923 = sqrt(max((_2111 / vec2(_2912)) - (_2915 * _2915), vec2(0.0))) * 1.25;
                    vec2 _2927 = min(_2915 - _2923, current);
                    vec2 _2929 = max(_2915 + _2923, current);
                    float _2931 = 1.5 / _2912;
                    float _2935 = _2915.x;
                    _2927.x = min(_2927.x, _2935 - _2931);
                    vec2 low = _2927;
                    _2929.x = max(_2929.x, _2935 + _2931);
                    vec2 high = _2929;
                    if (p.cameraDelta.w > 0.5)
                    {
                        ownMotion = p.control.x > 0.5;
                    }
                    else
                    {
                        ownMotion = false;
                    }
                    vec2 result;
                    float cap;
                    if (ownMotion)
                    {
                        vec4 _1653 = vec4((vec4(_2080, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz + p.cameraDelta.xyz, 0.0) * spvWorkaroundRowMajor(p.prevWorldToView);
                        float _1655 = _1653.z;
                        if (_1655 > p.prevUnproject.z)
                        {
                            vec2 prevUv_1 = (vec2((_1653.x * p.prevUnproject.x) / _1655, -((_1653.y * p.prevUnproject.y) / _1655)) * 0.5) + vec2(0.5);
                            vec2 prevUv;
                            if (p.control.y > 0.5)
                            {
                                uvec2 _3298 = uvec2(textureSize(_3397, 0));
                                uint _3299 = _3298.x;
                                uint _3300 = _3298.y;
                                vec2 _3322 = texelFetch(_3397, ivec3(clamp(ivec2(floor(_2057 * vec2(float(_3299), float(_3300)))), ivec2(0), ivec2(int(_3299) - 1, int(_3300) - 1)), 0).xy, 0).xy;
                                bool _1726 = length((_3322 - (_2057 - prevUv_1)) * p.resolution.xy) > 1.5;
                                if (_1726)
                                {
                                    prevUv = _2057 - _3322;
                                }
                                else
                                {
                                    prevUv = prevUv_1;
                                }
                                ownMotion = _1726;
                            }
                            else
                            {
                                prevUv = prevUv_1;
                                ownMotion = false;
                            }
                            vec2 historyUv = prevUv + (p.jitter.zw * p.resolution.zw);
                            bool _17;
                            if (all(greaterThanEqual(historyUv, vec2(0.0))))
                            {
                                _17 = all(lessThanEqual(historyUv, vec2(1.0)));
                            }
                            else
                            {
                                _17 = false;
                            }
                            if (_17)
                            {
                                vec2 position = (historyUv * p.resolution.xy) - vec2(0.5);
                                vec2 _1766 = floor(position);
                                vec2 _1767 = position - _1766;
                                ivec2 _1768 = ivec2(_1766);
                                float _1770 = 0.02999999932944774627685546875 * max(_1655, 1.0);
                                vec2 _1773 = vec2(1.0) - _1767;
                                vec4 _3335 = texelFetch(_3400, ivec3(clamp(_1768, ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                                bool _1799 = ownMotion;
                                bool _1800 = !_1799;
                                if (_1800)
                                {
                                    ownMotion = abs(_3335.w - _1655) > _1770;
                                }
                                else
                                {
                                    ownMotion = false;
                                }
                                float weight;
                                if (ownMotion)
                                {
                                    weight = 0.0;
                                }
                                else
                                {
                                    weight = _1773.x * _1773.y;
                                }
                                if (_3335.y <= 9.9999997473787516355514526367188e-05)
                                {
                                    ownMotion = true;
                                }
                                else
                                {
                                    ownMotion = _3335.z < 0.5;
                                }
                                if (ownMotion)
                                {
                                    weight = 0.0;
                                }
                                float _1831 = weight;
                                vec4 _3349 = texelFetch(_3400, ivec3(clamp(_1768 + ivec2(1, 0), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                                if (_1800)
                                {
                                    ownMotion = abs(_3349.w - _1655) > _1770;
                                }
                                else
                                {
                                    ownMotion = false;
                                }
                                float weight_1;
                                if (ownMotion)
                                {
                                    weight_1 = 0.0;
                                }
                                else
                                {
                                    weight_1 = _1767.x * _1773.y;
                                }
                                if (_3349.y <= 9.9999997473787516355514526367188e-05)
                                {
                                    ownMotion = true;
                                }
                                else
                                {
                                    ownMotion = _3349.z < 0.5;
                                }
                                if (ownMotion)
                                {
                                    weight_1 = 0.0;
                                }
                                float _1874 = weight;
                                vec4 _3363 = texelFetch(_3400, ivec3(clamp(_1768 + ivec2(0, 1), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                                if (_1800)
                                {
                                    ownMotion = abs(_3363.w - _1655) > _1770;
                                }
                                else
                                {
                                    ownMotion = false;
                                }
                                if (ownMotion)
                                {
                                    weight = 0.0;
                                }
                                else
                                {
                                    weight = _1773.x * _1767.y;
                                }
                                if (_3363.y <= 9.9999997473787516355514526367188e-05)
                                {
                                    ownMotion = true;
                                }
                                else
                                {
                                    ownMotion = _3363.z < 0.5;
                                }
                                if (ownMotion)
                                {
                                    weight = 0.0;
                                }
                                float _1915 = weight;
                                vec4 _3377 = texelFetch(_3400, ivec3(clamp(_1768 + ivec2(1), ivec2(0), ivec2(p.resolution.xy) - ivec2(1)), 0).xy, 0);
                                if (_1800)
                                {
                                    ownMotion = abs(_3377.w - _1655) > _1770;
                                }
                                else
                                {
                                    ownMotion = false;
                                }
                                if (ownMotion)
                                {
                                    weight = 0.0;
                                }
                                else
                                {
                                    weight = _1767.x * _1767.y;
                                }
                                if (_3377.y <= 9.9999997473787516355514526367188e-05)
                                {
                                    ownMotion = true;
                                }
                                else
                                {
                                    ownMotion = _3377.z < 0.5;
                                }
                                if (ownMotion)
                                {
                                    weight = 0.0;
                                }
                                float weightSum = ((_1874 + weight_1) + _1915) + weight;
                                if (weightSum > 0.001000000047497451305389404296875)
                                {
                                    vec3 history = ((((_3335.xyz * _1831) + (_3349.xyz * weight_1)) + (_3363.xyz * _1915)) + (_3377.xyz * weight)) / vec3(weightSum);
                                    float historyVis = history.x;
                                    float historyFrames = history.z;
                                    if (historyFrames > 0.0)
                                    {
                                        float _1978 = clamp(historyVis, low.x, high.x);
                                        float _1986 = clamp(1.0 - (abs(_1978 - historyVis) / max(((_2929 - _2927) * 0.5).x, 0.00999999977648258209228515625)), 0.0, 1.0);
                                        if (p.control.z > 0.5)
                                        {
                                            cap = 4.0;
                                        }
                                        else
                                        {
                                            cap = mix(4.0, 32.0, _1986);
                                        }
                                        float _2002 = min(historyFrames + 1.0, cap);
                                        result = mix(vec2(mix(_1978, historyVis, _1986), history.y), current, vec2(1.0 / _2002));
                                        cap = _2002;
                                    }
                                    else
                                    {
                                        result = current;
                                        cap = 1.0;
                                    }
                                }
                                else
                                {
                                    result = current;
                                    cap = 1.0;
                                }
                            }
                            else
                            {
                                result = current;
                                cap = 1.0;
                            }
                        }
                        else
                        {
                            result = current;
                            cap = 1.0;
                        }
                    }
                    else
                    {
                        result = current;
                        cap = 1.0;
                    }
                    imageStore(uOutput, ivec2(gl_GlobalInvocationID.xy), vec4(clamp(result.x, 0.0, 1.0), clamp(result.y, 0.0, 1.0), cap, _2080.z));
                }
            }
        }
    }
}

