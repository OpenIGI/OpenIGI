#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform GhostParams_std140
{
    vec4 size;
    vec4 depth;
    vec4 unproject;
    vec4 look;
    vec4 tint;
    vec4 hot;
    vec4 cloak;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uTarget;
layout(binding = 0) uniform sampler2D _4665;
layout(binding = 2) uniform sampler2D _4668;
layout(binding = 3) uniform sampler2D _4671;
layout(binding = 1) uniform sampler2D _4674;

void main()
{
    {
        if (!(any(greaterThanEqual(gl_GlobalInvocationID.xy, uvec2(p.size.xy)))))
        {
            ivec2 _100 = ivec2(gl_GlobalInvocationID.xy);
            vec3 _1258 = texelFetch(_4665, ivec3(clamp(_100, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz;
            float _133 = clamp(p.look.z, 0.0, 1.0);
            float _142 = clamp(p.cloak.x, 0.0, 1.0);
            float _145 = clamp(p.cloak.y, 0.0, 1.0);
            float strength = p.tint.w / max(p.look.y, 0.001000000047497451305389404296875);
            ivec2 at = _100;
            ivec2 torn;
            int split;
            bool solid;
            if (_133 > 0.0)
            {
                vec3 _1263 = fract(vec2(p.look.w, 1.7000000476837158203125).xyx * 0.103100001811981201171875);
                vec3 _1269 = _1263 + vec3(dot(_1263, _1263.yzx + vec3(33.3300018310546875)));
                int blockSize = 6 + int(fract((_1269.x + _1269.y) * _1269.z) * 10.0);
                ivec2 block = _100 / ivec2(blockSize);
                float _196 = float(block.y);
                vec3 _1280 = fract(vec2(_196, p.look.w).xyx * 0.103100001811981201171875);
                vec3 _1286 = _1280 + vec3(dot(_1280, _1280.yzx + vec3(33.3300018310546875)));
                if (fract((_1286.x + _1286.y) * _1286.z) < (_133 * 0.5))
                {
                    vec3 _1297 = fract(vec2(_196, p.look.w + 7.30000019073486328125).xyx * 0.103100001811981201171875);
                    vec3 _1303 = _1297 + vec3(dot(_1297, _1297.yzx + vec3(33.3300018310546875)));
                    ivec2 _4657 = at;
                    _4657.x = _4657.x + int((((fract((_1303.x + _1303.y) * _1303.z) - 0.5) * _133) * 4.0) * float(blockSize));
                    at = _4657;
                }
                ivec2 _219 = at;
                vec3 _1314 = fract((vec2(block) + vec2(p.look.w * 0.37000000476837158203125, p.look.w)).xyx * 0.103100001811981201171875);
                vec3 _1320 = _1314 + vec3(dot(_1314, _1314.yzx + vec3(33.3300018310546875)));
                bool solid_1 = fract((_1320.x + _1320.y) * _1320.z) < (_133 * 0.89999997615814208984375);
                if (solid_1)
                {
                    at = ((at / ivec2(blockSize)) * ivec2(blockSize)) + ivec2(blockSize / 2);
                }
                vec3 _1331 = fract(vec2(p.look.w, 3.099999904632568359375).xyx * 0.103100001811981201171875);
                vec3 _1337 = _1331 + vec3(dot(_1331, _1331.yzx + vec3(33.3300018310546875)));
                torn = _219;
                split = int(_133 * (2.0 + (fract((_1337.x + _1337.y) * _1337.z) * 6.0)));
                solid = solid_1;
            }
            else
            {
                torn = _100;
                split = 0;
                solid = false;
            }
            float _4481;
            vec2 _4482;
            float _4483;
            float _4484;
            float _4485;
            float _4486;
            {
                bool _1347;
                if (any(lessThan(at, ivec2(0))))
                {
                    _1347 = true;
                }
                else
                {
                    _1347 = any(greaterThanEqual(at, ivec2(p.size.xy)));
                }
                if (_1347)
                {
                    _4481 = 0.0;
                    _4482 = vec2(0.0);
                    _4483 = 0.0;
                    _4484 = 0.0;
                    _4485 = 0.0;
                    _4486 = 0.0;
                }
                else
                {
                    ivec2 _1373 = ivec3(at, 0).xy;
                    vec4 _1375 = texelFetch(_4668, _1373, 0);
                    vec4 _1378 = texelFetch(_4671, _1373, 0);
                    _4481 = _1375.w;
                    _4482 = _1375.xy;
                    _4483 = _1375.z;
                    _4484 = _1378.x;
                    _4485 = _1378.y;
                    _4486 = _1378.z;
                }
            }
            float _4545 = _4481;
            vec2 _4546 = _4482;
            float _4547 = _4483;
            float _4548 = _4484;
            float _4549 = _4485;
            float _4550 = _4486;
            bool _20;
            if (_4481 > 0.0)
            {
                uint _1401 = uint(_100.y);
                uint _1403 = (uint(_100.x) & 3u) ^ (_1401 & 3u);
                _20 = ((float(((((_1403 & 1u) << 3u) | ((_1401 & 1u) << 2u)) | (_1403 & 2u)) | ((_1401 & 2u) >> 1u)) + 0.5) * 0.0625) >= smoothstep(0.699999988079071044921875, 2.0, _4547);
            }
            else
            {
                _20 = false;
            }
            if (_20)
            {
                _4545 = 0.0;
            }
            vec3 rest;
            if (_4545 <= 0.0)
            {
                ivec2 _1426 = torn + ivec2(round(vec2(4.0, 0.0)));
                float _4429;
                float _4431;
                {
                    bool _1560;
                    if (any(lessThan(_1426, ivec2(0))))
                    {
                        _1560 = true;
                    }
                    else
                    {
                        _1560 = any(greaterThanEqual(_1426, ivec2(p.size.xy)));
                    }
                    if (_1560)
                    {
                        _4429 = 0.0;
                        _4431 = 0.0;
                    }
                    else
                    {
                        vec4 _1588 = texelFetch(_4668, ivec3(_1426, 0).xy, 0);
                        _4429 = _1588.w;
                        _4431 = _1588.z;
                    }
                }
                float _1417;
                if (_4429 > 0.0)
                {
                    _1417 = min(1000000.0, _4431);
                }
                else
                {
                    _1417 = 1000000.0;
                }
                ivec2 _1442 = torn + ivec2(round(vec2(2.8284270763397216796875)));
                float _4377;
                float _4379;
                {
                    bool _1611;
                    if (any(lessThan(_1442, ivec2(0))))
                    {
                        _1611 = true;
                    }
                    else
                    {
                        _1611 = any(greaterThanEqual(_1442, ivec2(p.size.xy)));
                    }
                    if (_1611)
                    {
                        _4377 = 0.0;
                        _4379 = 0.0;
                    }
                    else
                    {
                        vec4 _1639 = texelFetch(_4668, ivec3(_1442, 0).xy, 0);
                        _4377 = _1639.w;
                        _4379 = _1639.z;
                    }
                }
                if (_4377 > 0.0)
                {
                    _1417 = min(_1417, _4379);
                }
                ivec2 _1459 = torn + ivec2(round(vec2(-1.7484555314695171546190977096558e-07, 4.0)));
                float _4325;
                float _4327;
                {
                    bool _1662;
                    if (any(lessThan(_1459, ivec2(0))))
                    {
                        _1662 = true;
                    }
                    else
                    {
                        _1662 = any(greaterThanEqual(_1459, ivec2(p.size.xy)));
                    }
                    if (_1662)
                    {
                        _4325 = 0.0;
                        _4327 = 0.0;
                    }
                    else
                    {
                        vec4 _1690 = texelFetch(_4668, ivec3(_1459, 0).xy, 0);
                        _4325 = _1690.w;
                        _4327 = _1690.z;
                    }
                }
                if (_4325 > 0.0)
                {
                    _1417 = min(_1417, _4327);
                }
                ivec2 _1476 = torn + ivec2(round(vec2(-2.8284270763397216796875, 2.8284270763397216796875)));
                float _4273;
                float _4275;
                {
                    bool _1713;
                    if (any(lessThan(_1476, ivec2(0))))
                    {
                        _1713 = true;
                    }
                    else
                    {
                        _1713 = any(greaterThanEqual(_1476, ivec2(p.size.xy)));
                    }
                    if (_1713)
                    {
                        _4273 = 0.0;
                        _4275 = 0.0;
                    }
                    else
                    {
                        vec4 _1741 = texelFetch(_4668, ivec3(_1476, 0).xy, 0);
                        _4273 = _1741.w;
                        _4275 = _1741.z;
                    }
                }
                if (_4273 > 0.0)
                {
                    _1417 = min(_1417, _4275);
                }
                ivec2 _1493 = torn + ivec2(round(vec2(-4.0, -3.4969110629390343092381954193115e-07)));
                float _4221;
                float _4223;
                {
                    bool _1764;
                    if (any(lessThan(_1493, ivec2(0))))
                    {
                        _1764 = true;
                    }
                    else
                    {
                        _1764 = any(greaterThanEqual(_1493, ivec2(p.size.xy)));
                    }
                    if (_1764)
                    {
                        _4221 = 0.0;
                        _4223 = 0.0;
                    }
                    else
                    {
                        vec4 _1792 = texelFetch(_4668, ivec3(_1493, 0).xy, 0);
                        _4221 = _1792.w;
                        _4223 = _1792.z;
                    }
                }
                if (_4221 > 0.0)
                {
                    _1417 = min(_1417, _4223);
                }
                ivec2 _1510 = torn + ivec2(round(vec2(-2.8284265995025634765625, -2.8284275531768798828125)));
                float _4169;
                float _4171;
                {
                    bool _1815;
                    if (any(lessThan(_1510, ivec2(0))))
                    {
                        _1815 = true;
                    }
                    else
                    {
                        _1815 = any(greaterThanEqual(_1510, ivec2(p.size.xy)));
                    }
                    if (_1815)
                    {
                        _4169 = 0.0;
                        _4171 = 0.0;
                    }
                    else
                    {
                        vec4 _1843 = texelFetch(_4668, ivec3(_1510, 0).xy, 0);
                        _4169 = _1843.w;
                        _4171 = _1843.z;
                    }
                }
                if (_4169 > 0.0)
                {
                    _1417 = min(_1417, _4171);
                }
                ivec2 _1527 = torn + ivec2(round(vec2(4.7699522554012219188734889030457e-08, -4.0)));
                float _4117;
                float _4119;
                {
                    bool _1866;
                    if (any(lessThan(_1527, ivec2(0))))
                    {
                        _1866 = true;
                    }
                    else
                    {
                        _1866 = any(greaterThanEqual(_1527, ivec2(p.size.xy)));
                    }
                    if (_1866)
                    {
                        _4117 = 0.0;
                        _4119 = 0.0;
                    }
                    else
                    {
                        vec4 _1894 = texelFetch(_4668, ivec3(_1527, 0).xy, 0);
                        _4117 = _1894.w;
                        _4119 = _1894.z;
                    }
                }
                if (_4117 > 0.0)
                {
                    _1417 = min(_1417, _4119);
                }
                ivec2 _1544 = torn + ivec2(round(vec2(2.8284280300140380859375, -2.8284261226654052734375)));
                float _4063;
                float _4065;
                {
                    bool _1917;
                    if (any(lessThan(_1544, ivec2(0))))
                    {
                        _1917 = true;
                    }
                    else
                    {
                        _1917 = any(greaterThanEqual(_1544, ivec2(p.size.xy)));
                    }
                    if (_1917)
                    {
                        _4063 = 0.0;
                        _4065 = 0.0;
                    }
                    else
                    {
                        vec4 _1945 = texelFetch(_4668, ivec3(_1544, 0).xy, 0);
                        _4063 = _1945.w;
                        _4065 = _1945.z;
                    }
                }
                if (_4063 > 0.0)
                {
                    _1417 = min(_1417, _4065);
                }
                vec2 _1556 = vec2(((((((_4429 + _4377) + _4325) + _4273) + _4221) + _4169) + _4117) + _4063, _1417);
                vec3 light_1 = p.tint.xyz * ((_1556.x * 0.01200000010430812835693359375) * smoothstep(0.699999988079071044921875, 2.0, _1556.y));
                if (split != 0)
                {
                    ivec2 _585 = ivec2(split, 0);
                    ivec2 _587 = torn + _585;
                    float _1966;
                    {
                        bool _2051;
                        if (any(lessThan(_587, ivec2(0))))
                        {
                            _2051 = true;
                        }
                        else
                        {
                            _2051 = any(greaterThanEqual(_587, ivec2(p.size.xy)));
                        }
                        float _2052;
                        if (_2051)
                        {
                            _2052 = 0.0;
                        }
                        else
                        {
                            _2052 = texelFetch(_4668, ivec3(_587, 0).xy, 0).w;
                        }
                        if (_2052 <= 0.0)
                        {
                            _1966 = 0.0;
                        }
                        else
                        {
                            ivec2 _1981 = _587 + ivec2(round(vec2(1.5, 0.0)));
                            bool _2077;
                            if (any(lessThan(_1981, ivec2(0))))
                            {
                                _2077 = true;
                            }
                            else
                            {
                                _2077 = any(greaterThanEqual(_1981, ivec2(p.size.xy)));
                            }
                            float _2078;
                            if (_2077)
                            {
                                _2078 = 0.0;
                            }
                            else
                            {
                                _2078 = texelFetch(_4668, ivec3(_1981, 0).xy, 0).w;
                            }
                            ivec2 _1989 = _587 + ivec2(round(vec2(1.0606601238250732421875)));
                            bool _2103;
                            if (any(lessThan(_1989, ivec2(0))))
                            {
                                _2103 = true;
                            }
                            else
                            {
                                _2103 = any(greaterThanEqual(_1989, ivec2(p.size.xy)));
                            }
                            float _2104;
                            if (_2103)
                            {
                                _2104 = 0.0;
                            }
                            else
                            {
                                _2104 = texelFetch(_4668, ivec3(_1989, 0).xy, 0).w;
                            }
                            ivec2 _1998 = _587 + ivec2(round(vec2(-6.556707887739321449771523475647e-08, 1.5)));
                            bool _2129;
                            if (any(lessThan(_1998, ivec2(0))))
                            {
                                _2129 = true;
                            }
                            else
                            {
                                _2129 = any(greaterThanEqual(_1998, ivec2(p.size.xy)));
                            }
                            float _2130;
                            if (_2129)
                            {
                                _2130 = 0.0;
                            }
                            else
                            {
                                _2130 = texelFetch(_4668, ivec3(_1998, 0).xy, 0).w;
                            }
                            ivec2 _2007 = _587 + ivec2(round(vec2(-1.0606601238250732421875, 1.0606601238250732421875)));
                            bool _2155;
                            if (any(lessThan(_2007, ivec2(0))))
                            {
                                _2155 = true;
                            }
                            else
                            {
                                _2155 = any(greaterThanEqual(_2007, ivec2(p.size.xy)));
                            }
                            float _2156;
                            if (_2155)
                            {
                                _2156 = 0.0;
                            }
                            else
                            {
                                _2156 = texelFetch(_4668, ivec3(_2007, 0).xy, 0).w;
                            }
                            ivec2 _2016 = _587 + ivec2(round(vec2(-1.5, -1.3113415775478642899543046951294e-07)));
                            bool _2181;
                            if (any(lessThan(_2016, ivec2(0))))
                            {
                                _2181 = true;
                            }
                            else
                            {
                                _2181 = any(greaterThanEqual(_2016, ivec2(p.size.xy)));
                            }
                            float _2182;
                            if (_2181)
                            {
                                _2182 = 0.0;
                            }
                            else
                            {
                                _2182 = texelFetch(_4668, ivec3(_2016, 0).xy, 0).w;
                            }
                            ivec2 _2025 = _587 + ivec2(round(vec2(-1.06066000461578369140625, -1.06066036224365234375)));
                            bool _2207;
                            if (any(lessThan(_2025, ivec2(0))))
                            {
                                _2207 = true;
                            }
                            else
                            {
                                _2207 = any(greaterThanEqual(_2025, ivec2(p.size.xy)));
                            }
                            float _2208;
                            if (_2207)
                            {
                                _2208 = 0.0;
                            }
                            else
                            {
                                _2208 = texelFetch(_4668, ivec3(_2025, 0).xy, 0).w;
                            }
                            ivec2 _2034 = _587 + ivec2(round(vec2(1.7887320069576162495650351047516e-08, -1.5)));
                            bool _2233;
                            if (any(lessThan(_2034, ivec2(0))))
                            {
                                _2233 = true;
                            }
                            else
                            {
                                _2233 = any(greaterThanEqual(_2034, ivec2(p.size.xy)));
                            }
                            float _2234;
                            if (_2233)
                            {
                                _2234 = 0.0;
                            }
                            else
                            {
                                _2234 = texelFetch(_4668, ivec3(_2034, 0).xy, 0).w;
                            }
                            ivec2 _2043 = _587 + ivec2(round(vec2(1.06066048145294189453125, -1.06065976619720458984375)));
                            bool _2259;
                            if (any(lessThan(_2043, ivec2(0))))
                            {
                                _2259 = true;
                            }
                            else
                            {
                                _2259 = any(greaterThanEqual(_2043, ivec2(p.size.xy)));
                            }
                            float _2260;
                            if (_2259)
                            {
                                _2260 = 0.0;
                            }
                            else
                            {
                                _2260 = texelFetch(_4668, ivec3(_2043, 0).xy, 0).w;
                            }
                            _1966 = clamp((8.0 - (((((((_2078 + _2104) + _2130) + _2156) + _2182) + _2208) + _2234) + _2260)) * 0.5, 0.0, 1.0);
                        }
                    }
                    ivec2 _719 = torn - _585;
                    float _2286;
                    {
                        bool _2371;
                        if (any(lessThan(_719, ivec2(0))))
                        {
                            _2371 = true;
                        }
                        else
                        {
                            _2371 = any(greaterThanEqual(_719, ivec2(p.size.xy)));
                        }
                        float _2372;
                        if (_2371)
                        {
                            _2372 = 0.0;
                        }
                        else
                        {
                            _2372 = texelFetch(_4668, ivec3(_719, 0).xy, 0).w;
                        }
                        if (_2372 <= 0.0)
                        {
                            _2286 = 0.0;
                        }
                        else
                        {
                            ivec2 _2301 = _719 + ivec2(round(vec2(1.5, 0.0)));
                            bool _2397;
                            if (any(lessThan(_2301, ivec2(0))))
                            {
                                _2397 = true;
                            }
                            else
                            {
                                _2397 = any(greaterThanEqual(_2301, ivec2(p.size.xy)));
                            }
                            float _2398;
                            if (_2397)
                            {
                                _2398 = 0.0;
                            }
                            else
                            {
                                _2398 = texelFetch(_4668, ivec3(_2301, 0).xy, 0).w;
                            }
                            ivec2 _2309 = _719 + ivec2(round(vec2(1.0606601238250732421875)));
                            bool _2423;
                            if (any(lessThan(_2309, ivec2(0))))
                            {
                                _2423 = true;
                            }
                            else
                            {
                                _2423 = any(greaterThanEqual(_2309, ivec2(p.size.xy)));
                            }
                            float _2424;
                            if (_2423)
                            {
                                _2424 = 0.0;
                            }
                            else
                            {
                                _2424 = texelFetch(_4668, ivec3(_2309, 0).xy, 0).w;
                            }
                            ivec2 _2318 = _719 + ivec2(round(vec2(-6.556707887739321449771523475647e-08, 1.5)));
                            bool _2449;
                            if (any(lessThan(_2318, ivec2(0))))
                            {
                                _2449 = true;
                            }
                            else
                            {
                                _2449 = any(greaterThanEqual(_2318, ivec2(p.size.xy)));
                            }
                            float _2450;
                            if (_2449)
                            {
                                _2450 = 0.0;
                            }
                            else
                            {
                                _2450 = texelFetch(_4668, ivec3(_2318, 0).xy, 0).w;
                            }
                            ivec2 _2327 = _719 + ivec2(round(vec2(-1.0606601238250732421875, 1.0606601238250732421875)));
                            bool _2475;
                            if (any(lessThan(_2327, ivec2(0))))
                            {
                                _2475 = true;
                            }
                            else
                            {
                                _2475 = any(greaterThanEqual(_2327, ivec2(p.size.xy)));
                            }
                            float _2476;
                            if (_2475)
                            {
                                _2476 = 0.0;
                            }
                            else
                            {
                                _2476 = texelFetch(_4668, ivec3(_2327, 0).xy, 0).w;
                            }
                            ivec2 _2336 = _719 + ivec2(round(vec2(-1.5, -1.3113415775478642899543046951294e-07)));
                            bool _2501;
                            if (any(lessThan(_2336, ivec2(0))))
                            {
                                _2501 = true;
                            }
                            else
                            {
                                _2501 = any(greaterThanEqual(_2336, ivec2(p.size.xy)));
                            }
                            float _2502;
                            if (_2501)
                            {
                                _2502 = 0.0;
                            }
                            else
                            {
                                _2502 = texelFetch(_4668, ivec3(_2336, 0).xy, 0).w;
                            }
                            ivec2 _2345 = _719 + ivec2(round(vec2(-1.06066000461578369140625, -1.06066036224365234375)));
                            bool _2527;
                            if (any(lessThan(_2345, ivec2(0))))
                            {
                                _2527 = true;
                            }
                            else
                            {
                                _2527 = any(greaterThanEqual(_2345, ivec2(p.size.xy)));
                            }
                            float _2528;
                            if (_2527)
                            {
                                _2528 = 0.0;
                            }
                            else
                            {
                                _2528 = texelFetch(_4668, ivec3(_2345, 0).xy, 0).w;
                            }
                            ivec2 _2354 = _719 + ivec2(round(vec2(1.7887320069576162495650351047516e-08, -1.5)));
                            bool _2553;
                            if (any(lessThan(_2354, ivec2(0))))
                            {
                                _2553 = true;
                            }
                            else
                            {
                                _2553 = any(greaterThanEqual(_2354, ivec2(p.size.xy)));
                            }
                            float _2554;
                            if (_2553)
                            {
                                _2554 = 0.0;
                            }
                            else
                            {
                                _2554 = texelFetch(_4668, ivec3(_2354, 0).xy, 0).w;
                            }
                            ivec2 _2363 = _719 + ivec2(round(vec2(1.06066048145294189453125, -1.06065976619720458984375)));
                            bool _2579;
                            if (any(lessThan(_2363, ivec2(0))))
                            {
                                _2579 = true;
                            }
                            else
                            {
                                _2579 = any(greaterThanEqual(_2363, ivec2(p.size.xy)));
                            }
                            float _2580;
                            if (_2579)
                            {
                                _2580 = 0.0;
                            }
                            else
                            {
                                _2580 = texelFetch(_4668, ivec3(_2363, 0).xy, 0).w;
                            }
                            _2286 = clamp((8.0 - (((((((_2398 + _2424) + _2450) + _2476) + _2502) + _2528) + _2554) + _2580)) * 0.5, 0.0, 1.0);
                        }
                    }
                    rest = light_1 + (vec3(p.tint.x * _1966, 0.0, p.tint.z * _2286) * 0.800000011920928955078125);
                }
                else
                {
                    rest = light_1;
                }
                imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), vec4(_1258 + (rest * strength), 1.0));
            }
            else
            {
                float _2606;
                {
                    bool _2691;
                    if (any(lessThan(torn, ivec2(0))))
                    {
                        _2691 = true;
                    }
                    else
                    {
                        _2691 = any(greaterThanEqual(torn, ivec2(p.size.xy)));
                    }
                    float _2692;
                    if (_2691)
                    {
                        _2692 = 0.0;
                    }
                    else
                    {
                        _2692 = texelFetch(_4668, ivec3(torn, 0).xy, 0).w;
                    }
                    if (_2692 <= 0.0)
                    {
                        _2606 = 0.0;
                    }
                    else
                    {
                        ivec2 _2621 = torn + ivec2(round(vec2(1.5, 0.0)));
                        bool _2717;
                        if (any(lessThan(_2621, ivec2(0))))
                        {
                            _2717 = true;
                        }
                        else
                        {
                            _2717 = any(greaterThanEqual(_2621, ivec2(p.size.xy)));
                        }
                        float _2718;
                        if (_2717)
                        {
                            _2718 = 0.0;
                        }
                        else
                        {
                            _2718 = texelFetch(_4668, ivec3(_2621, 0).xy, 0).w;
                        }
                        ivec2 _2629 = torn + ivec2(round(vec2(1.0606601238250732421875)));
                        bool _2743;
                        if (any(lessThan(_2629, ivec2(0))))
                        {
                            _2743 = true;
                        }
                        else
                        {
                            _2743 = any(greaterThanEqual(_2629, ivec2(p.size.xy)));
                        }
                        float _2744;
                        if (_2743)
                        {
                            _2744 = 0.0;
                        }
                        else
                        {
                            _2744 = texelFetch(_4668, ivec3(_2629, 0).xy, 0).w;
                        }
                        ivec2 _2638 = torn + ivec2(round(vec2(-6.556707887739321449771523475647e-08, 1.5)));
                        bool _2769;
                        if (any(lessThan(_2638, ivec2(0))))
                        {
                            _2769 = true;
                        }
                        else
                        {
                            _2769 = any(greaterThanEqual(_2638, ivec2(p.size.xy)));
                        }
                        float _2770;
                        if (_2769)
                        {
                            _2770 = 0.0;
                        }
                        else
                        {
                            _2770 = texelFetch(_4668, ivec3(_2638, 0).xy, 0).w;
                        }
                        ivec2 _2647 = torn + ivec2(round(vec2(-1.0606601238250732421875, 1.0606601238250732421875)));
                        bool _2795;
                        if (any(lessThan(_2647, ivec2(0))))
                        {
                            _2795 = true;
                        }
                        else
                        {
                            _2795 = any(greaterThanEqual(_2647, ivec2(p.size.xy)));
                        }
                        float _2796;
                        if (_2795)
                        {
                            _2796 = 0.0;
                        }
                        else
                        {
                            _2796 = texelFetch(_4668, ivec3(_2647, 0).xy, 0).w;
                        }
                        ivec2 _2656 = torn + ivec2(round(vec2(-1.5, -1.3113415775478642899543046951294e-07)));
                        bool _2821;
                        if (any(lessThan(_2656, ivec2(0))))
                        {
                            _2821 = true;
                        }
                        else
                        {
                            _2821 = any(greaterThanEqual(_2656, ivec2(p.size.xy)));
                        }
                        float _2822;
                        if (_2821)
                        {
                            _2822 = 0.0;
                        }
                        else
                        {
                            _2822 = texelFetch(_4668, ivec3(_2656, 0).xy, 0).w;
                        }
                        ivec2 _2665 = torn + ivec2(round(vec2(-1.06066000461578369140625, -1.06066036224365234375)));
                        bool _2847;
                        if (any(lessThan(_2665, ivec2(0))))
                        {
                            _2847 = true;
                        }
                        else
                        {
                            _2847 = any(greaterThanEqual(_2665, ivec2(p.size.xy)));
                        }
                        float _2848;
                        if (_2847)
                        {
                            _2848 = 0.0;
                        }
                        else
                        {
                            _2848 = texelFetch(_4668, ivec3(_2665, 0).xy, 0).w;
                        }
                        ivec2 _2674 = torn + ivec2(round(vec2(1.7887320069576162495650351047516e-08, -1.5)));
                        bool _2873;
                        if (any(lessThan(_2674, ivec2(0))))
                        {
                            _2873 = true;
                        }
                        else
                        {
                            _2873 = any(greaterThanEqual(_2674, ivec2(p.size.xy)));
                        }
                        float _2874;
                        if (_2873)
                        {
                            _2874 = 0.0;
                        }
                        else
                        {
                            _2874 = texelFetch(_4668, ivec3(_2674, 0).xy, 0).w;
                        }
                        ivec2 _2683 = torn + ivec2(round(vec2(1.06066048145294189453125, -1.06065976619720458984375)));
                        bool _2899;
                        if (any(lessThan(_2683, ivec2(0))))
                        {
                            _2899 = true;
                        }
                        else
                        {
                            _2899 = any(greaterThanEqual(_2683, ivec2(p.size.xy)));
                        }
                        float _2900;
                        if (_2899)
                        {
                            _2900 = 0.0;
                        }
                        else
                        {
                            _2900 = texelFetch(_4668, ivec3(_2683, 0).xy, 0).w;
                        }
                        _2606 = clamp((8.0 - (((((((_2718 + _2744) + _2770) + _2796) + _2822) + _2848) + _2874) + _2900)) * 0.5, 0.0, 1.0);
                    }
                }
                float _752 = _4548 - (1.14999997615814208984375 - (fract(p.look.x * 0.2857142984867095947265625) * 1.2999999523162841796875));
                float _763 = exp(-(144.0 * (_752 * _752)));
                vec4 _2948 = texelFetch(_4674, ivec3(clamp(ivec2((vec2(_100) + vec2(0.5)) * p.depth.zw), ivec2(0), ivec2(p.depth.xy) - ivec2(1)), 0).xy, 0);
                float _2949 = _2948.x;
                float _2928;
                if (_2949 > 0.0)
                {
                    _2928 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _2949)) * (p.unproject.w - p.unproject.z)));
                }
                else
                {
                    _2928 = p.unproject.w;
                }
                bool hidden = (_4547 - _2928) > (0.25 + (_4547 * 0.00999999977648258209228515625));
                vec3 _2978 = fract(vec2(float(_100.y), p.look.w * 1.309999942779541015625).xyx * 0.103100001811981201171875);
                vec3 _2984 = _2978 + vec3(dot(_2978, _2978.yzx + vec3(33.3300018310546875)));
                float signal;
                if (fract((_2984.x + _2984.y) * _2984.z) < (_133 * 0.02999999932944774627685546875))
                {
                    signal = _133;
                }
                else
                {
                    signal = 0.0;
                }
                float burn;
                if (_142 < 0.999000012874603271484375)
                {
                    burn = clamp(1.0 - ((_142 - _4549) * 20.0), 0.0, 1.0);
                }
                else
                {
                    burn = 0.0;
                }
                float light;
                if (hidden)
                {
                    uint _2997 = uint(_100.y);
                    uint _2999 = (uint(_100.x) & 3u) ^ (_2997 & 3u);
                    if (((float(((((_2999 & 1u) << 3u) | ((_2997 & 1u) << 2u)) | (_2999 & 2u)) | ((_2997 & 2u) >> 1u)) + 0.5) * 0.0625) < 0.14000000059604644775390625)
                    {
                        light = 1.0;
                    }
                    else
                    {
                        light = 0.0;
                    }
                    rest = _1258 + ((p.tint.xyz * (((_2606 * (0.449999988079071044921875 + (0.300000011920928955078125 * _763))) + (light * 0.100000001490116119384765625)) + (burn * 0.60000002384185791015625))) * strength);
                }
                else
                {
                    if (solid)
                    {
                        ivec2 _894 = ivec2(split, 0);
                        ivec2 _896 = torn + _894;
                        float _3014;
                        {
                            bool _3099;
                            if (any(lessThan(_896, ivec2(0))))
                            {
                                _3099 = true;
                            }
                            else
                            {
                                _3099 = any(greaterThanEqual(_896, ivec2(p.size.xy)));
                            }
                            float _3100;
                            if (_3099)
                            {
                                _3100 = 0.0;
                            }
                            else
                            {
                                _3100 = texelFetch(_4668, ivec3(_896, 0).xy, 0).w;
                            }
                            if (_3100 <= 0.0)
                            {
                                _3014 = 0.0;
                            }
                            else
                            {
                                ivec2 _3029 = _896 + ivec2(round(vec2(1.5, 0.0)));
                                bool _3125;
                                if (any(lessThan(_3029, ivec2(0))))
                                {
                                    _3125 = true;
                                }
                                else
                                {
                                    _3125 = any(greaterThanEqual(_3029, ivec2(p.size.xy)));
                                }
                                float _3126;
                                if (_3125)
                                {
                                    _3126 = 0.0;
                                }
                                else
                                {
                                    _3126 = texelFetch(_4668, ivec3(_3029, 0).xy, 0).w;
                                }
                                ivec2 _3037 = _896 + ivec2(round(vec2(1.0606601238250732421875)));
                                bool _3151;
                                if (any(lessThan(_3037, ivec2(0))))
                                {
                                    _3151 = true;
                                }
                                else
                                {
                                    _3151 = any(greaterThanEqual(_3037, ivec2(p.size.xy)));
                                }
                                float _3152;
                                if (_3151)
                                {
                                    _3152 = 0.0;
                                }
                                else
                                {
                                    _3152 = texelFetch(_4668, ivec3(_3037, 0).xy, 0).w;
                                }
                                ivec2 _3046 = _896 + ivec2(round(vec2(-6.556707887739321449771523475647e-08, 1.5)));
                                bool _3177;
                                if (any(lessThan(_3046, ivec2(0))))
                                {
                                    _3177 = true;
                                }
                                else
                                {
                                    _3177 = any(greaterThanEqual(_3046, ivec2(p.size.xy)));
                                }
                                float _3178;
                                if (_3177)
                                {
                                    _3178 = 0.0;
                                }
                                else
                                {
                                    _3178 = texelFetch(_4668, ivec3(_3046, 0).xy, 0).w;
                                }
                                ivec2 _3055 = _896 + ivec2(round(vec2(-1.0606601238250732421875, 1.0606601238250732421875)));
                                bool _3203;
                                if (any(lessThan(_3055, ivec2(0))))
                                {
                                    _3203 = true;
                                }
                                else
                                {
                                    _3203 = any(greaterThanEqual(_3055, ivec2(p.size.xy)));
                                }
                                float _3204;
                                if (_3203)
                                {
                                    _3204 = 0.0;
                                }
                                else
                                {
                                    _3204 = texelFetch(_4668, ivec3(_3055, 0).xy, 0).w;
                                }
                                ivec2 _3064 = _896 + ivec2(round(vec2(-1.5, -1.3113415775478642899543046951294e-07)));
                                bool _3229;
                                if (any(lessThan(_3064, ivec2(0))))
                                {
                                    _3229 = true;
                                }
                                else
                                {
                                    _3229 = any(greaterThanEqual(_3064, ivec2(p.size.xy)));
                                }
                                float _3230;
                                if (_3229)
                                {
                                    _3230 = 0.0;
                                }
                                else
                                {
                                    _3230 = texelFetch(_4668, ivec3(_3064, 0).xy, 0).w;
                                }
                                ivec2 _3073 = _896 + ivec2(round(vec2(-1.06066000461578369140625, -1.06066036224365234375)));
                                bool _3255;
                                if (any(lessThan(_3073, ivec2(0))))
                                {
                                    _3255 = true;
                                }
                                else
                                {
                                    _3255 = any(greaterThanEqual(_3073, ivec2(p.size.xy)));
                                }
                                float _3256;
                                if (_3255)
                                {
                                    _3256 = 0.0;
                                }
                                else
                                {
                                    _3256 = texelFetch(_4668, ivec3(_3073, 0).xy, 0).w;
                                }
                                ivec2 _3082 = _896 + ivec2(round(vec2(1.7887320069576162495650351047516e-08, -1.5)));
                                bool _3281;
                                if (any(lessThan(_3082, ivec2(0))))
                                {
                                    _3281 = true;
                                }
                                else
                                {
                                    _3281 = any(greaterThanEqual(_3082, ivec2(p.size.xy)));
                                }
                                float _3282;
                                if (_3281)
                                {
                                    _3282 = 0.0;
                                }
                                else
                                {
                                    _3282 = texelFetch(_4668, ivec3(_3082, 0).xy, 0).w;
                                }
                                ivec2 _3091 = _896 + ivec2(round(vec2(1.06066048145294189453125, -1.06065976619720458984375)));
                                bool _3307;
                                if (any(lessThan(_3091, ivec2(0))))
                                {
                                    _3307 = true;
                                }
                                else
                                {
                                    _3307 = any(greaterThanEqual(_3091, ivec2(p.size.xy)));
                                }
                                float _3308;
                                if (_3307)
                                {
                                    _3308 = 0.0;
                                }
                                else
                                {
                                    _3308 = texelFetch(_4668, ivec3(_3091, 0).xy, 0).w;
                                }
                                _3014 = clamp((8.0 - (((((((_3126 + _3152) + _3178) + _3204) + _3230) + _3256) + _3282) + _3308)) * 0.5, 0.0, 1.0);
                            }
                        }
                        ivec2 _903 = torn - _894;
                        float _3334;
                        {
                            bool _3419;
                            if (any(lessThan(_903, ivec2(0))))
                            {
                                _3419 = true;
                            }
                            else
                            {
                                _3419 = any(greaterThanEqual(_903, ivec2(p.size.xy)));
                            }
                            float _3420;
                            if (_3419)
                            {
                                _3420 = 0.0;
                            }
                            else
                            {
                                _3420 = texelFetch(_4668, ivec3(_903, 0).xy, 0).w;
                            }
                            if (_3420 <= 0.0)
                            {
                                _3334 = 0.0;
                            }
                            else
                            {
                                ivec2 _3349 = _903 + ivec2(round(vec2(1.5, 0.0)));
                                bool _3445;
                                if (any(lessThan(_3349, ivec2(0))))
                                {
                                    _3445 = true;
                                }
                                else
                                {
                                    _3445 = any(greaterThanEqual(_3349, ivec2(p.size.xy)));
                                }
                                float _3446;
                                if (_3445)
                                {
                                    _3446 = 0.0;
                                }
                                else
                                {
                                    _3446 = texelFetch(_4668, ivec3(_3349, 0).xy, 0).w;
                                }
                                ivec2 _3357 = _903 + ivec2(round(vec2(1.0606601238250732421875)));
                                bool _3471;
                                if (any(lessThan(_3357, ivec2(0))))
                                {
                                    _3471 = true;
                                }
                                else
                                {
                                    _3471 = any(greaterThanEqual(_3357, ivec2(p.size.xy)));
                                }
                                float _3472;
                                if (_3471)
                                {
                                    _3472 = 0.0;
                                }
                                else
                                {
                                    _3472 = texelFetch(_4668, ivec3(_3357, 0).xy, 0).w;
                                }
                                ivec2 _3366 = _903 + ivec2(round(vec2(-6.556707887739321449771523475647e-08, 1.5)));
                                bool _3497;
                                if (any(lessThan(_3366, ivec2(0))))
                                {
                                    _3497 = true;
                                }
                                else
                                {
                                    _3497 = any(greaterThanEqual(_3366, ivec2(p.size.xy)));
                                }
                                float _3498;
                                if (_3497)
                                {
                                    _3498 = 0.0;
                                }
                                else
                                {
                                    _3498 = texelFetch(_4668, ivec3(_3366, 0).xy, 0).w;
                                }
                                ivec2 _3375 = _903 + ivec2(round(vec2(-1.0606601238250732421875, 1.0606601238250732421875)));
                                bool _3523;
                                if (any(lessThan(_3375, ivec2(0))))
                                {
                                    _3523 = true;
                                }
                                else
                                {
                                    _3523 = any(greaterThanEqual(_3375, ivec2(p.size.xy)));
                                }
                                float _3524;
                                if (_3523)
                                {
                                    _3524 = 0.0;
                                }
                                else
                                {
                                    _3524 = texelFetch(_4668, ivec3(_3375, 0).xy, 0).w;
                                }
                                ivec2 _3384 = _903 + ivec2(round(vec2(-1.5, -1.3113415775478642899543046951294e-07)));
                                bool _3549;
                                if (any(lessThan(_3384, ivec2(0))))
                                {
                                    _3549 = true;
                                }
                                else
                                {
                                    _3549 = any(greaterThanEqual(_3384, ivec2(p.size.xy)));
                                }
                                float _3550;
                                if (_3549)
                                {
                                    _3550 = 0.0;
                                }
                                else
                                {
                                    _3550 = texelFetch(_4668, ivec3(_3384, 0).xy, 0).w;
                                }
                                ivec2 _3393 = _903 + ivec2(round(vec2(-1.06066000461578369140625, -1.06066036224365234375)));
                                bool _3575;
                                if (any(lessThan(_3393, ivec2(0))))
                                {
                                    _3575 = true;
                                }
                                else
                                {
                                    _3575 = any(greaterThanEqual(_3393, ivec2(p.size.xy)));
                                }
                                float _3576;
                                if (_3575)
                                {
                                    _3576 = 0.0;
                                }
                                else
                                {
                                    _3576 = texelFetch(_4668, ivec3(_3393, 0).xy, 0).w;
                                }
                                ivec2 _3402 = _903 + ivec2(round(vec2(1.7887320069576162495650351047516e-08, -1.5)));
                                bool _3601;
                                if (any(lessThan(_3402, ivec2(0))))
                                {
                                    _3601 = true;
                                }
                                else
                                {
                                    _3601 = any(greaterThanEqual(_3402, ivec2(p.size.xy)));
                                }
                                float _3602;
                                if (_3601)
                                {
                                    _3602 = 0.0;
                                }
                                else
                                {
                                    _3602 = texelFetch(_4668, ivec3(_3402, 0).xy, 0).w;
                                }
                                ivec2 _3411 = _903 + ivec2(round(vec2(1.06066048145294189453125, -1.06065976619720458984375)));
                                bool _3627;
                                if (any(lessThan(_3411, ivec2(0))))
                                {
                                    _3627 = true;
                                }
                                else
                                {
                                    _3627 = any(greaterThanEqual(_3411, ivec2(p.size.xy)));
                                }
                                float _3628;
                                if (_3627)
                                {
                                    _3628 = 0.0;
                                }
                                else
                                {
                                    _3628 = texelFetch(_4668, ivec3(_3411, 0).xy, 0).w;
                                }
                                _3334 = clamp((8.0 - (((((((_3446 + _3472) + _3498) + _3524) + _3550) + _3576) + _3602) + _3628)) * 0.5, 0.0, 1.0);
                            }
                        }
                        float _4579 = _4550;
                        float _3653;
                        if ((_100.y & 1) == 0)
                        {
                            _3653 = 1.0;
                        }
                        else
                        {
                            _3653 = 0.819999992847442626953125;
                        }
                        rest = mix(_1258, ((((p.tint.xyz * (0.2199999988079071044921875 + (floor((clamp(dot(vec3(_4546, -sqrt(clamp(1.0 - dot(_4546, _4546), 0.0, 1.0))), normalize(vec3(-0.3499999940395355224609375, 0.550000011920928955078125, -0.75))), 0.0, 1.0) * 3.0) + 0.5) * 0.2599999904632568359375))) * _3653) * 0.89999997615814208984375) + ((p.hot.xyz * (_4579 * _4579)) * 0.3499999940395355224609375)) * strength, vec3(0.89999997615814208984375)) + (((vec3(p.tint.x * _3014, p.tint.y * _2606, p.tint.z * _3334) * 0.89999997615814208984375) + ((p.hot.xyz * signal) * 0.5)) * strength);
                    }
                    else
                    {
                        float _1017 = 1.0 - _145;
                        vec2 _1021 = vec2(_100);
                        vec2 centre = _1021 + vec2(0.5);
                        vec2 offset = vec2(_4546.x, -_4546.y) * ((((2.0 + (7.0 * _4550)) * (0.800000011920928955078125 + (0.4000000059604644775390625 * ((sin(((p.look.x * 2.2999999523162841796875) + (_4548 * 20.0)) + (_4549 * 6.283185482025146484375)) * 0.5) + 0.5)))) * (p.size.y * 0.000925925909541547298431396484375)) * (1.0 + (_1017 * 1.5)));
                        vec2 _3701 = (centre + (offset * 1.2000000476837158203125)) - vec2(0.5);
                        vec2 _3702 = floor(_3701);
                        vec2 _3703 = _3701 - _3702;
                        ivec2 _3704 = ivec2(_3702);
                        vec3 _3709 = vec3(_3703.x);
                        vec2 _3782 = (centre + offset) - vec2(0.5);
                        vec2 _3783 = floor(_3782);
                        vec2 _3784 = _3782 - _3783;
                        ivec2 _3785 = ivec2(_3783);
                        vec3 _3790 = vec3(_3784.x);
                        vec2 _3863 = (centre + (offset * 0.800000011920928955078125)) - vec2(0.5);
                        vec2 _3864 = floor(_3863);
                        vec2 _3865 = _3863 - _3864;
                        ivec2 _3866 = ivec2(_3864);
                        vec3 _3871 = vec3(_3865.x);
                        vec3 bent = vec3(mix(mix(texelFetch(_4665, ivec3(clamp(_3704, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, texelFetch(_4665, ivec3(clamp(_3704 + ivec2(1, 0), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, _3709), mix(texelFetch(_4665, ivec3(clamp(_3704 + ivec2(0, 1), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, texelFetch(_4665, ivec3(clamp(_3704 + ivec2(1), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, _3709), vec3(_3703.y)).x, mix(mix(texelFetch(_4665, ivec3(clamp(_3785, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, texelFetch(_4665, ivec3(clamp(_3785 + ivec2(1, 0), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, _3790), mix(texelFetch(_4665, ivec3(clamp(_3785 + ivec2(0, 1), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, texelFetch(_4665, ivec3(clamp(_3785 + ivec2(1), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, _3790), vec3(_3784.y)).y, mix(mix(texelFetch(_4665, ivec3(clamp(_3866, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, texelFetch(_4665, ivec3(clamp(_3866 + ivec2(1, 0), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, _3871), mix(texelFetch(_4665, ivec3(clamp(_3866 + ivec2(0, 1), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, texelFetch(_4665, ivec3(clamp(_3866 + ivec2(1), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, _3871), vec3(_3865.y)).z);
                        vec2 _1091 = _1021 / vec2(max(5.0, (((0.0900000035762786865234375 * p.unproject.y) * p.size.y) * 0.5) / max(_4547, 0.100000001490116119384765625)));
                        vec2 _3949 = (_1091 - (vec2(1.0, 1.73205077648162841796875) * floor(_1091 * vec2(1.0, 0.57735025882720947265625)))) - vec2(0.5, 0.866025388240814208984375);
                        vec2 _3950 = _1091 - vec2(0.5, 0.866025388240814208984375);
                        vec2 _3955 = (_3950 - (vec2(1.0, 1.73205077648162841796875) * floor(_3950 * vec2(1.0, 0.57735025882720947265625)))) - vec2(0.5, 0.866025388240814208984375);
                        vec2 _3941;
                        if (dot(_3949, _3949) < dot(_3955, _3955))
                        {
                            _3941 = _3949;
                        }
                        else
                        {
                            _3941 = _3955;
                        }
                        vec2 _3963 = abs(_3941);
                        float contour = _2606 * (0.2199999988079071044921875 + (0.949999988079071044921875 * _763));
                        float light_2 = ((contour + ((_4550 * _4550) * (0.0599999986588954925537109375 + (0.25 * _763)))) + ((smoothstep(0.4000000059604644775390625, 0.4799999892711639404296875, max(dot(_3963, vec2(0.5, 0.866025388240814208984375)), _3963.x)) * _763) * 0.3499999940395355224609375)) + 0.0199999995529651641845703125;
                        if (_145 < 1.0)
                        {
                            float outward = abs(_4548 - 0.5) * 2.0;
                            float _1154 = outward - _145;
                            if (outward > _145)
                            {
                                float _4583 = _4550;
                                float _3973;
                                if ((_100.y & 1) == 0)
                                {
                                    _3973 = 1.0;
                                }
                                else
                                {
                                    _3973 = 0.819999992847442626953125;
                                }
                                rest = (((((p.tint.xyz * (0.2199999988079071044921875 + (floor((clamp(dot(vec3(_4546, -sqrt(clamp(1.0 - dot(_4546, _4546), 0.0, 1.0))), normalize(vec3(-0.3499999940395355224609375, 0.550000011920928955078125, -0.75))), 0.0, 1.0) * 3.0) + 0.5) * 0.2599999904632568359375))) * _3973) * 0.89999997615814208984375) + ((p.hot.xyz * (_4583 * _4583)) * 0.3499999940395355224609375)) * 0.3499999940395355224609375) * _1017;
                            }
                            else
                            {
                                rest = vec3(0.0);
                            }
                            light = light_2 + ((exp(-(100.0 * (_1154 * _1154))) * _1017) * 0.800000011920928955078125);
                        }
                        else
                        {
                            rest = vec3(0.0);
                            light = light_2;
                        }
                        rest = mix(bent, p.tint.xyz * (dot(bent, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) / max(dot(p.tint.xyz, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)), 0.001000000047497451305389404296875)), vec3(0.14000000059604644775390625)) + ((((mix(p.tint.xyz, p.hot.xyz, vec3(clamp((contour - 0.699999988079071044921875) * 2.5, 0.0, 1.0))) * light) + rest) + ((p.hot.xyz * signal) * 0.4000000059604644775390625)) * strength);
                    }
                }
                if (burn > 0.0)
                {
                    solid = !hidden;
                }
                else
                {
                    solid = false;
                }
                if (solid)
                {
                    float _4587 = _4550;
                    float _4018;
                    if ((_100.y & 1) == 0)
                    {
                        _4018 = 1.0;
                    }
                    else
                    {
                        _4018 = 0.819999992847442626953125;
                    }
                    rest = mix(rest, ((((p.tint.xyz * (0.2199999988079071044921875 + (floor((clamp(dot(vec3(_4546, -sqrt(clamp(1.0 - dot(_4546, _4546), 0.0, 1.0))), normalize(vec3(-0.3499999940395355224609375, 0.550000011920928955078125, -0.75))), 0.0, 1.0) * 3.0) + 0.5) * 0.2599999904632568359375))) * _4018) * 0.89999997615814208984375) + ((p.hot.xyz * (_4587 * _4587)) * 0.3499999940395355224609375)) * strength, vec3(burn * 0.60000002384185791015625)) + (((p.hot.xyz * burn) * 1.2000000476837158203125) * strength);
                }
                imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), vec4(rest, 1.0));
            }
        }
    }
}

