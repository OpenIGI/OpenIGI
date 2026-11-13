#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    layout(row_major) mat4 viewToWorld;
    layout(row_major) mat4 prevWorldToView;
    vec4 unproject;
    vec4 resolution;
    vec4 cameraDelta;
    vec4 prevProjection;
    vec4 jitter;
} p;

layout(binding = 0, std430) restrict readonly buffer StructuredBuffer
{
    uint _member0[];
} uOccluder;

layout(binding = 0, r8) uniform restrict writeonly image2D uRejection;
layout(binding = 1) uniform sampler2D _2269;
layout(binding = 3) uniform sampler2D _2271;
layout(binding = 2) uniform sampler2D _2274;
layout(binding = 0) uniform sampler2D _2277;
layout(binding = 4) uniform sampler2D _2280;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    {
        ivec2 _302 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 _314 = ivec2(p.resolution.xy);
        bool _7;
        if (_302.x >= _314.x)
        {
            _7 = true;
        }
        else
        {
            _7 = _302.y >= _314.y;
        }
        if (!(_7))
        {
            ivec2 _332 = _314 - ivec2(1);
            vec2 rasterUv = (vec2(_302) + vec2(0.5)) * p.resolution.zw;
            vec4 sampled = textureLod(_2269, rasterUv, 0.0);
            float d = sampled.x;
            vec2 currentUv = rasterUv - (p.jitter.xy * p.resolution.zw);
            vec2 prevRasterUv = (currentUv - textureLod(_2271, rasterUv, 0.0).xy) + (p.jitter.zw * p.resolution.zw);
            if (any(lessThan(prevRasterUv, vec2(0.0))))
            {
                _7 = true;
            }
            else
            {
                _7 = any(greaterThan(prevRasterUv, vec2(1.0)));
            }
            if (_7)
            {
                imageStore(uRejection, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
            }
            else
            {
                ivec2 _403 = ivec2(prevRasterUv * p.resolution.xy);
                float _1411;
                if (d <= 0.0)
                {
                    _1411 = p.unproject.w;
                }
                else
                {
                    _1411 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * d)) * (p.unproject.w - p.unproject.z)));
                }
                vec2 _463 = texelFetch(_2274, ivec3(_302, 0).xy, 0).xy * p.resolution.xy;
                ivec2 _472;
                float eOcc;
                {
                    _472 = ivec3(clamp(_302 + ivec2(1, 0), ivec2(0), _332), 0).xy;
                    if (length((texelFetch(_2274, _472, 0).xy * p.resolution.xy) - _463) > 0.5)
                    {
                        eOcc = 0.0;
                    }
                    else
                    {
                        vec4 sampled_1 = texelFetch(_2277, _472, 0);
                        float _489 = sampled_1.x;
                        float _1442;
                        if (_489 <= 0.0)
                        {
                            _1442 = p.unproject.w;
                        }
                        else
                        {
                            _1442 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _489)) * (p.unproject.w - p.unproject.z)));
                        }
                        eOcc = max(0.0, abs(_1442 - _1411));
                    }
                }
                ivec2 _505;
                {
                    _505 = ivec3(clamp(_302 + ivec2(-1, 0), ivec2(0), _332), 0).xy;
                    if (!(length((texelFetch(_2274, _505, 0).xy * p.resolution.xy) - _463) > 0.5))
                    {
                        vec4 sampled_2 = texelFetch(_2277, _505, 0);
                        float _521 = sampled_2.x;
                        float _1473;
                        if (_521 <= 0.0)
                        {
                            _1473 = p.unproject.w;
                        }
                        else
                        {
                            _1473 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _521)) * (p.unproject.w - p.unproject.z)));
                        }
                        eOcc = max(eOcc, abs(_1473 - _1411));
                    }
                }
                ivec2 _537;
                {
                    _537 = ivec3(clamp(_302 + ivec2(0, 1), ivec2(0), _332), 0).xy;
                    if (!(length((texelFetch(_2274, _537, 0).xy * p.resolution.xy) - _463) > 0.5))
                    {
                        vec4 sampled_3 = texelFetch(_2277, _537, 0);
                        float _553 = sampled_3.x;
                        float _1504;
                        if (_553 <= 0.0)
                        {
                            _1504 = p.unproject.w;
                        }
                        else
                        {
                            _1504 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _553)) * (p.unproject.w - p.unproject.z)));
                        }
                        eOcc = max(eOcc, abs(_1504 - _1411));
                    }
                }
                ivec2 _569;
                {
                    _569 = ivec3(clamp(_302 + ivec2(0, -1), ivec2(0), _332), 0).xy;
                    if (!(length((texelFetch(_2274, _569, 0).xy * p.resolution.xy) - _463) > 0.5))
                    {
                        vec4 sampled_4 = texelFetch(_2277, _569, 0);
                        float _585 = sampled_4.x;
                        float _1535;
                        if (_585 <= 0.0)
                        {
                            _1535 = p.unproject.w;
                        }
                        else
                        {
                            _1535 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _585)) * (p.unproject.w - p.unproject.z)));
                        }
                        eOcc = max(eOcc, abs(_1535 - _1411));
                    }
                }
                float eSurf;
                if (d <= 0.0)
                {
                    if (texelFetch(_2280, ivec3(clamp(_403 + ivec2(-1), ivec2(0), _332), 0).xy, 0).x <= 0.0)
                    {
                        eSurf = 1.0;
                    }
                    else
                    {
                        eSurf = 0.0;
                    }
                    if (texelFetch(_2280, ivec3(clamp(_403 + ivec2(0, -1), ivec2(0), _332), 0).xy, 0).x <= 0.0)
                    {
                        eSurf = 1.0;
                    }
                    if (texelFetch(_2280, ivec3(clamp(_403 + ivec2(1, -1), ivec2(0), _332), 0).xy, 0).x <= 0.0)
                    {
                        eSurf = 1.0;
                    }
                    if (texelFetch(_2280, ivec3(clamp(_403 + ivec2(-1, 0), ivec2(0), _332), 0).xy, 0).x <= 0.0)
                    {
                        eSurf = 1.0;
                    }
                    if (texelFetch(_2280, ivec3(clamp(_403, ivec2(0), _332), 0).xy, 0).x <= 0.0)
                    {
                        eSurf = 1.0;
                    }
                    if (texelFetch(_2280, ivec3(clamp(_403 + ivec2(1, 0), ivec2(0), _332), 0).xy, 0).x <= 0.0)
                    {
                        eSurf = 1.0;
                    }
                    if (texelFetch(_2280, ivec3(clamp(_403 + ivec2(-1, 1), ivec2(0), _332), 0).xy, 0).x <= 0.0)
                    {
                        eSurf = 1.0;
                    }
                    if (texelFetch(_2280, ivec3(clamp(_403 + ivec2(0, 1), ivec2(0), _332), 0).xy, 0).x <= 0.0)
                    {
                        eSurf = 1.0;
                    }
                    if (texelFetch(_2280, ivec3(clamp(_403 + ivec2(1), ivec2(0), _332), 0).xy, 0).x <= 0.0)
                    {
                        eSurf = 1.0;
                    }
                    ivec2 _1570 = _314 - ivec2(1);
                    vec2 _1576 = (prevRasterUv * p.resolution.xy) - vec2(0.5);
                    vec2 _1577 = floor(_1576);
                    vec2 _1578 = _1576 - _1577;
                    ivec2 _1579 = ivec2(_1577);
                    float _1580 = _1578.x;
                    float _1581 = 1.0 - _1580;
                    float _1582 = _1578.y;
                    float _1583 = 1.0 - _1582;
                    float _1584 = _1581 * _1583;
                    ivec2 _1585 = clamp(_1579, ivec2(0), _1570);
                    int _1587 = _314.x;
                    int _1590 = (_1585.y * _1587) + _1585.x;
                    float _1566;
                    if (uOccluder._member0[_1590] == 0u)
                    {
                        _1566 = _1584;
                    }
                    else
                    {
                        float _1596 = uintBitsToFloat(~uOccluder._member0[_1590]);
                        _1566 = _1584 * clamp(2.0 - ((p.prevProjection.w - _1596) / (max(6.0 * (_1596 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eOcc)), 0.0, 1.0);
                    }
                    float _1609 = _1580 * _1583;
                    ivec2 _1611 = clamp(_1579 + ivec2(1, 0), ivec2(0), _1570);
                    int _1615 = (_1611.y * _1587) + _1611.x;
                    if (uOccluder._member0[_1615] == 0u)
                    {
                        _1566 += _1609;
                    }
                    else
                    {
                        float _1621 = uintBitsToFloat(~uOccluder._member0[_1615]);
                        _1566 += (_1609 * clamp(2.0 - ((p.prevProjection.w - _1621) / (max(6.0 * (_1621 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eOcc)), 0.0, 1.0));
                    }
                    float _1638 = _1581 * _1582;
                    ivec2 _1640 = clamp(_1579 + ivec2(0, 1), ivec2(0), _1570);
                    int _1644 = (_1640.y * _1587) + _1640.x;
                    if (uOccluder._member0[_1644] == 0u)
                    {
                        _1566 += _1638;
                    }
                    else
                    {
                        float _1650 = uintBitsToFloat(~uOccluder._member0[_1644]);
                        _1566 += (_1638 * clamp(2.0 - ((p.prevProjection.w - _1650) / (max(6.0 * (_1650 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eOcc)), 0.0, 1.0));
                    }
                    float _1667 = _1580 * _1582;
                    ivec2 _1669 = clamp(_1579 + ivec2(1), ivec2(0), _1570);
                    int _1673 = (_1669.y * _1587) + _1669.x;
                    if (uOccluder._member0[_1673] == 0u)
                    {
                        _1566 += _1667;
                    }
                    else
                    {
                        float _1679 = uintBitsToFloat(~uOccluder._member0[_1673]);
                        _1566 += (_1667 * clamp(2.0 - ((p.prevProjection.w - _1679) / (max(6.0 * (_1679 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eOcc)), 0.0, 1.0));
                    }
                    imageStore(uRejection, ivec2(gl_GlobalInvocationID.xy), vec4(min(eSurf, _1566), 0.0, 0.0, 0.0));
                }
                else
                {
                    float _1783 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * d)) * (p.unproject.w - p.unproject.z)));
                    vec2 _1751 = (currentUv * 2.0) - vec2(1.0);
                    vec3 _1764 = vec3((_1751.x * _1783) / p.unproject.x, ((-_1751.y) * _1783) / p.unproject.y, _1783);
                    vec4 _1798 = vec4((vec4(_1764, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz + p.cameraDelta.xyz, 0.0) * spvWorkaroundRowMajor(p.prevWorldToView);
                    float _1799 = _1798.z;
                    if (_1799 <= p.prevProjection.z)
                    {
                        imageStore(uRejection, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
                    }
                    else
                    {
                        {
                            vec4 sampled_5 = texelFetch(_2277, _472, 0);
                            float nd = sampled_5.x;
                            if (nd <= 0.0)
                            {
                                eSurf = 0.0;
                            }
                            else
                            {
                                eSurf = max(0.0, abs((((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * nd)) * (p.unproject.w - p.unproject.z)))) - _1764.z));
                            }
                        }
                        {
                            vec4 sampled_6 = texelFetch(_2277, _505, 0);
                            float nd_1 = sampled_6.x;
                            if (!(nd_1 <= 0.0))
                            {
                                eSurf = max(eSurf, abs((((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * nd_1)) * (p.unproject.w - p.unproject.z)))) - _1764.z));
                            }
                        }
                        {
                            vec4 sampled_7 = texelFetch(_2277, _537, 0);
                            float nd_2 = sampled_7.x;
                            if (!(nd_2 <= 0.0))
                            {
                                eSurf = max(eSurf, abs((((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * nd_2)) * (p.unproject.w - p.unproject.z)))) - _1764.z));
                            }
                        }
                        {
                            vec4 sampled_8 = texelFetch(_2277, _569, 0);
                            float nd_3 = sampled_8.x;
                            if (!(nd_3 <= 0.0))
                            {
                                eSurf = max(eSurf, abs((((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * nd_3)) * (p.unproject.w - p.unproject.z)))) - _1764.z));
                            }
                        }
                        float agreement;
                        {
                            vec4 sampled_9 = texelFetch(_2280, ivec3(clamp(_403 + ivec2(-1), ivec2(0), _332), 0).xy, 0);
                            float prevD = sampled_9.x;
                            if (prevD <= 0.0)
                            {
                                agreement = 0.0;
                            }
                            else
                            {
                                float _1886 = ((2.0 * p.prevProjection.z) * p.prevProjection.w) / ((p.prevProjection.w + p.prevProjection.z) - ((1.0 - (2.0 * prevD)) * (p.prevProjection.w - p.prevProjection.z)));
                                agreement = max(0.0, clamp(2.0 - (abs(_1886 - _1799) / (max(6.0 * (_1886 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eSurf)), 0.0, 1.0));
                            }
                        }
                        {
                            vec4 sampled_10 = texelFetch(_2280, ivec3(clamp(_403 + ivec2(0, -1), ivec2(0), _332), 0).xy, 0);
                            float prevD_1 = sampled_10.x;
                            if (!(prevD_1 <= 0.0))
                            {
                                float _1909 = ((2.0 * p.prevProjection.z) * p.prevProjection.w) / ((p.prevProjection.w + p.prevProjection.z) - ((1.0 - (2.0 * prevD_1)) * (p.prevProjection.w - p.prevProjection.z)));
                                agreement = max(agreement, clamp(2.0 - (abs(_1909 - _1799) / (max(6.0 * (_1909 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eSurf)), 0.0, 1.0));
                            }
                        }
                        {
                            vec4 sampled_11 = texelFetch(_2280, ivec3(clamp(_403 + ivec2(1, -1), ivec2(0), _332), 0).xy, 0);
                            float prevD_2 = sampled_11.x;
                            if (!(prevD_2 <= 0.0))
                            {
                                float _1932 = ((2.0 * p.prevProjection.z) * p.prevProjection.w) / ((p.prevProjection.w + p.prevProjection.z) - ((1.0 - (2.0 * prevD_2)) * (p.prevProjection.w - p.prevProjection.z)));
                                agreement = max(agreement, clamp(2.0 - (abs(_1932 - _1799) / (max(6.0 * (_1932 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eSurf)), 0.0, 1.0));
                            }
                        }
                        {
                            vec4 sampled_12 = texelFetch(_2280, ivec3(clamp(_403 + ivec2(-1, 0), ivec2(0), _332), 0).xy, 0);
                            float prevD_3 = sampled_12.x;
                            if (!(prevD_3 <= 0.0))
                            {
                                float _1955 = ((2.0 * p.prevProjection.z) * p.prevProjection.w) / ((p.prevProjection.w + p.prevProjection.z) - ((1.0 - (2.0 * prevD_3)) * (p.prevProjection.w - p.prevProjection.z)));
                                agreement = max(agreement, clamp(2.0 - (abs(_1955 - _1799) / (max(6.0 * (_1955 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eSurf)), 0.0, 1.0));
                            }
                        }
                        {
                            vec4 sampled_13 = texelFetch(_2280, ivec3(clamp(_403, ivec2(0), _332), 0).xy, 0);
                            float prevD_4 = sampled_13.x;
                            if (!(prevD_4 <= 0.0))
                            {
                                float _1978 = ((2.0 * p.prevProjection.z) * p.prevProjection.w) / ((p.prevProjection.w + p.prevProjection.z) - ((1.0 - (2.0 * prevD_4)) * (p.prevProjection.w - p.prevProjection.z)));
                                agreement = max(agreement, clamp(2.0 - (abs(_1978 - _1799) / (max(6.0 * (_1978 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eSurf)), 0.0, 1.0));
                            }
                        }
                        {
                            vec4 sampled_14 = texelFetch(_2280, ivec3(clamp(_403 + ivec2(1, 0), ivec2(0), _332), 0).xy, 0);
                            float prevD_5 = sampled_14.x;
                            if (!(prevD_5 <= 0.0))
                            {
                                float _2001 = ((2.0 * p.prevProjection.z) * p.prevProjection.w) / ((p.prevProjection.w + p.prevProjection.z) - ((1.0 - (2.0 * prevD_5)) * (p.prevProjection.w - p.prevProjection.z)));
                                agreement = max(agreement, clamp(2.0 - (abs(_2001 - _1799) / (max(6.0 * (_2001 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eSurf)), 0.0, 1.0));
                            }
                        }
                        {
                            vec4 sampled_15 = texelFetch(_2280, ivec3(clamp(_403 + ivec2(-1, 1), ivec2(0), _332), 0).xy, 0);
                            float prevD_6 = sampled_15.x;
                            if (!(prevD_6 <= 0.0))
                            {
                                float _2024 = ((2.0 * p.prevProjection.z) * p.prevProjection.w) / ((p.prevProjection.w + p.prevProjection.z) - ((1.0 - (2.0 * prevD_6)) * (p.prevProjection.w - p.prevProjection.z)));
                                agreement = max(agreement, clamp(2.0 - (abs(_2024 - _1799) / (max(6.0 * (_2024 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eSurf)), 0.0, 1.0));
                            }
                        }
                        {
                            vec4 sampled_16 = texelFetch(_2280, ivec3(clamp(_403 + ivec2(0, 1), ivec2(0), _332), 0).xy, 0);
                            float prevD_7 = sampled_16.x;
                            if (!(prevD_7 <= 0.0))
                            {
                                float _2047 = ((2.0 * p.prevProjection.z) * p.prevProjection.w) / ((p.prevProjection.w + p.prevProjection.z) - ((1.0 - (2.0 * prevD_7)) * (p.prevProjection.w - p.prevProjection.z)));
                                agreement = max(agreement, clamp(2.0 - (abs(_2047 - _1799) / (max(6.0 * (_2047 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eSurf)), 0.0, 1.0));
                            }
                        }
                        {
                            vec4 sampled_17 = texelFetch(_2280, ivec3(clamp(_403 + ivec2(1), ivec2(0), _332), 0).xy, 0);
                            float prevD_8 = sampled_17.x;
                            if (!(prevD_8 <= 0.0))
                            {
                                float _2070 = ((2.0 * p.prevProjection.z) * p.prevProjection.w) / ((p.prevProjection.w + p.prevProjection.z) - ((1.0 - (2.0 * prevD_8)) * (p.prevProjection.w - p.prevProjection.z)));
                                agreement = max(agreement, clamp(2.0 - (abs(_2070 - _1799) / (max(6.0 * (_2070 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eSurf)), 0.0, 1.0));
                            }
                        }
                        ivec2 _2087 = _314 - ivec2(1);
                        vec2 _2093 = (prevRasterUv * p.resolution.xy) - vec2(0.5);
                        vec2 _2094 = floor(_2093);
                        vec2 _2095 = _2093 - _2094;
                        ivec2 _2096 = ivec2(_2094);
                        float _2097 = _2095.x;
                        float _2098 = 1.0 - _2097;
                        float _2099 = _2095.y;
                        float _2100 = 1.0 - _2099;
                        float _2101 = _2098 * _2100;
                        ivec2 _2102 = clamp(_2096, ivec2(0), _2087);
                        int _2104 = _314.x;
                        int _2107 = (_2102.y * _2104) + _2102.x;
                        float _2083;
                        if (uOccluder._member0[_2107] == 0u)
                        {
                            _2083 = _2101;
                        }
                        else
                        {
                            float _2113 = uintBitsToFloat(~uOccluder._member0[_2107]);
                            _2083 = _2101 * clamp(2.0 - ((_1799 - _2113) / (max(6.0 * (_2113 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eOcc)), 0.0, 1.0);
                        }
                        float _2126 = _2097 * _2100;
                        ivec2 _2128 = clamp(_2096 + ivec2(1, 0), ivec2(0), _2087);
                        int _2132 = (_2128.y * _2104) + _2128.x;
                        if (uOccluder._member0[_2132] == 0u)
                        {
                            _2083 += _2126;
                        }
                        else
                        {
                            float _2138 = uintBitsToFloat(~uOccluder._member0[_2132]);
                            _2083 += (_2126 * clamp(2.0 - ((_1799 - _2138) / (max(6.0 * (_2138 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eOcc)), 0.0, 1.0));
                        }
                        float _2155 = _2098 * _2099;
                        ivec2 _2157 = clamp(_2096 + ivec2(0, 1), ivec2(0), _2087);
                        int _2161 = (_2157.y * _2104) + _2157.x;
                        if (uOccluder._member0[_2161] == 0u)
                        {
                            _2083 += _2155;
                        }
                        else
                        {
                            float _2167 = uintBitsToFloat(~uOccluder._member0[_2161]);
                            _2083 += (_2155 * clamp(2.0 - ((_1799 - _2167) / (max(6.0 * (_2167 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eOcc)), 0.0, 1.0));
                        }
                        float _2184 = _2097 * _2099;
                        ivec2 _2186 = clamp(_2096 + ivec2(1), ivec2(0), _2087);
                        int _2190 = (_2186.y * _2104) + _2186.x;
                        if (uOccluder._member0[_2190] == 0u)
                        {
                            _2083 += _2184;
                        }
                        else
                        {
                            float _2196 = uintBitsToFloat(~uOccluder._member0[_2190]);
                            _2083 += (_2184 * clamp(2.0 - ((_1799 - _2196) / (max(6.0 * (_2196 / max((0.5 * p.resolution.y) * p.unproject.y, 9.9999999747524270787835121154785e-07)), 9.9999997473787516355514526367188e-05) + eOcc)), 0.0, 1.0));
                        }
                        imageStore(uRejection, ivec2(gl_GlobalInvocationID.xy), vec4(min(agreement, _2083), 0.0, 0.0, 0.0));
                    }
                }
            }
        }
    }
}

