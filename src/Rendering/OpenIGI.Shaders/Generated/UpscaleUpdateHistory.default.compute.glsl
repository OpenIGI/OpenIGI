#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 _input;
    vec4 history;
    vec4 control;
    vec4 control2;
    vec4 control3;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uHistoryColour;
layout(binding = 1, r8) uniform restrict writeonly image2D uHistoryMetadata;
layout(binding = 1) uniform sampler2D _2588;
layout(binding = 9) uniform sampler2D _2590;
layout(binding = 2) uniform sampler2D _2592;
layout(binding = 5) uniform sampler2D _2594;
layout(binding = 7) uniform sampler2D _2596;
layout(binding = 8) uniform sampler2D _2598;
layout(binding = 6) uniform sampler2D _2601;
layout(binding = 0) uniform sampler2D _2604;
layout(binding = 3) uniform sampler2D _2606;
layout(binding = 4) uniform sampler2D _2608;

void main()
{
    {
        ivec2 _326 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 _336 = ivec2(p.history.xy);
        bool offScreen;
        if (_326.x >= _336.x)
        {
            offScreen = true;
        }
        else
        {
            offScreen = _326.y >= _336.y;
        }
        if (!(offScreen))
        {
            float _356 = max(1.0, p.control.y);
            ivec2 _373 = ivec2(p._input.xy);
            vec2 inputPos_1 = ((vec2(_326) + vec2(0.5)) * p._input.xy) / p.history.xy;
            vec2 uv = inputPos_1 / p._input.xy;
            vec4 _404 = textureGather(_2590, uv);
            bool foreground = max(max(_404.x, _404.y), max(_404.z, _404.w)) >= 0.89999997615814208984375;
            vec2 sj;
            if (foreground)
            {
                sj = vec2(0.0);
            }
            else
            {
                sj = vec2(p.control.w, p.control2.x);
            }
            vec2 uvJ = uv + (sj / p._input.xy);
            if (p.control.x < 0.5)
            {
                imageStore(uHistoryColour, ivec2(gl_GlobalInvocationID.xy), vec4(max(textureLod(_2588, uv, 0.0).xyz, vec3(0.0)), 1.0));
                imageStore(uHistoryMetadata, ivec2(gl_GlobalInvocationID.xy), vec4(1.0, 0.0, 0.0, 0.0));
            }
            else
            {
                vec2 v = textureLod(_2592, uvJ, 0.0).xy;
                vec2 prevUv = uv - v;
                vec2 grid;
                if (abs(p.control.z - p.control3.w) > 9.9999997473787516355514526367188e-06)
                {
                    grid = p.history.xy / vec2(max(p.control3.w, 1.0));
                }
                else
                {
                    grid = p._input.xy;
                }
                float _481 = length(v * grid);
                if (any(lessThan(prevUv, vec2(0.0))))
                {
                    offScreen = true;
                }
                else
                {
                    offScreen = any(greaterThan(prevUv, vec2(1.0)));
                }
                float believable;
                if (p.control2.y > 0.5)
                {
                    believable = textureLod(_2594, uvJ, 0.0).x;
                }
                else
                {
                    believable = 1.0;
                }
                float shading;
                if (p.control2.w > 0.5)
                {
                    shading = textureLod(_2596, uvJ, 0.0).x;
                }
                else
                {
                    shading = 1.0;
                }
                float relaxClamp;
                if (p.control2.w > 0.5)
                {
                    relaxClamp = textureLod(_2598, uvJ, 0.0).x;
                }
                else
                {
                    relaxClamp = 1.0;
                }
                bool _17;
                if (offScreen)
                {
                    _17 = true;
                }
                else
                {
                    _17 = believable < 0.5;
                }
                if (_17)
                {
                    relaxClamp = 0.0;
                }
                float relaxClamp_1 = relaxClamp * believable;
                if (offScreen)
                {
                    _17 = true;
                }
                else
                {
                    _17 = foreground;
                }
                float refine;
                if (_17)
                {
                    refine = 0.0;
                }
                else
                {
                    refine = clamp(((16.0 * believable) * shading) - 13.0, 0.0, 1.0);
                }
                float _578 = mix(1.0, max(p.control.z, 1.0), refine);
                vec2 inputPos;
                float lerpAmt;
                if (p.control2.z > 0.5)
                {
                    if (offScreen)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = believable < 0.5;
                    }
                    if (_17)
                    {
                        lerpAmt = 1.0;
                    }
                    else
                    {
                        lerpAmt = clamp(1.0 - (4.0 * min(shading, relaxClamp_1)), 0.0, 1.0);
                    }
                    inputPos = inputPos_1 + (texelFetch(_2601, ivec3(clamp(ivec2(inputPos_1 + sj), ivec2(0), _373 - ivec2(1)), 0).xy, 0).xy * lerpAmt);
                }
                else
                {
                    inputPos = inputPos_1;
                }
                ivec2 _629 = ivec2(floor(inputPos - vec2(1.0)));
                float cov;
                vec3 sumColour;
                {
                    int _634 = _629.x;
                    if (_634 < 0)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = _629.y < 0;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = _634 >= _373.x;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = _629.y >= _373.y;
                    }
                    if (_17)
                    {
                        sumColour = vec3(0.0);
                        lerpAmt = 0.0;
                        cov = 0.0;
                    }
                    else
                    {
                        vec2 _673 = ((vec2(_629) + vec2(0.5)) - sj) - inputPos;
                        float _1879 = clamp((_578 * _578) * dot(_673, _673), 0.0, 1.0);
                        float _1886 = clamp(((1.0 - (1.89999997615814208984375 * _1879)) + ((0.89999997615814208984375 * _1879) * _1879)) + 0.004999999888241291046142578125, 0.0, 1.0);
                        vec3 _700 = max(texelFetch(_2604, ivec3(_629, 0).xy, 0).xyz, vec3(0.0));
                        vec3 _1890 = _700 * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                        float hw = _1886 * max(9.9999997473787516355514526367188e-05, 1.0 / (((_1890.x + (2.0 * _1890.y)) + _1890.z) + 4.0));
                        sumColour = _700 * hw;
                        lerpAmt = hw;
                        cov = _1886;
                    }
                }
                {
                    ivec2 kk = _629 + ivec2(1, 0);
                    int _740 = kk.x;
                    if (_740 < 0)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk.y < 0;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = _740 >= _373.x;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk.y >= _373.y;
                    }
                    if (!(_17))
                    {
                        vec2 _775 = ((vec2(kk) + vec2(0.5)) - sj) - inputPos;
                        float _1911 = clamp((_578 * _578) * dot(_775, _775), 0.0, 1.0);
                        float _1918 = clamp(((1.0 - (1.89999997615814208984375 * _1911)) + ((0.89999997615814208984375 * _1911) * _1911)) + 0.004999999888241291046142578125, 0.0, 1.0);
                        vec3 _783 = max(texelFetch(_2604, ivec3(kk, 0).xy, 0).xyz, vec3(0.0));
                        vec3 _1922 = _783 * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                        float hw_1 = _1918 * max(9.9999997473787516355514526367188e-05, 1.0 / (((_1922.x + (2.0 * _1922.y)) + _1922.z) + 4.0));
                        sumColour += (_783 * hw_1);
                        lerpAmt += hw_1;
                        cov += _1918;
                    }
                }
                {
                    ivec2 kk_1 = _629 + ivec2(2, 0);
                    int _803 = kk_1.x;
                    if (_803 < 0)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_1.y < 0;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = _803 >= _373.x;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_1.y >= _373.y;
                    }
                    if (!(_17))
                    {
                        vec2 _838 = ((vec2(kk_1) + vec2(0.5)) - sj) - inputPos;
                        float _1943 = clamp((_578 * _578) * dot(_838, _838), 0.0, 1.0);
                        float _1950 = clamp(((1.0 - (1.89999997615814208984375 * _1943)) + ((0.89999997615814208984375 * _1943) * _1943)) + 0.004999999888241291046142578125, 0.0, 1.0);
                        vec3 _846 = max(texelFetch(_2604, ivec3(kk_1, 0).xy, 0).xyz, vec3(0.0));
                        vec3 _1954 = _846 * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                        float hw_2 = _1950 * max(9.9999997473787516355514526367188e-05, 1.0 / (((_1954.x + (2.0 * _1954.y)) + _1954.z) + 4.0));
                        sumColour += (_846 * hw_2);
                        lerpAmt += hw_2;
                        cov += _1950;
                    }
                }
                {
                    ivec2 kk_2 = _629 + ivec2(0, 1);
                    int _873 = kk_2.x;
                    if (_873 < 0)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_2.y < 0;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = _873 >= _373.x;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_2.y >= _373.y;
                    }
                    if (!(_17))
                    {
                        vec2 _908 = ((vec2(kk_2) + vec2(0.5)) - sj) - inputPos;
                        float _1975 = clamp((_578 * _578) * dot(_908, _908), 0.0, 1.0);
                        float _1982 = clamp(((1.0 - (1.89999997615814208984375 * _1975)) + ((0.89999997615814208984375 * _1975) * _1975)) + 0.004999999888241291046142578125, 0.0, 1.0);
                        vec3 _916 = max(texelFetch(_2604, ivec3(kk_2, 0).xy, 0).xyz, vec3(0.0));
                        vec3 _1986 = _916 * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                        float hw_3 = _1982 * max(9.9999997473787516355514526367188e-05, 1.0 / (((_1986.x + (2.0 * _1986.y)) + _1986.z) + 4.0));
                        sumColour += (_916 * hw_3);
                        lerpAmt += hw_3;
                        cov += _1982;
                    }
                }
                {
                    ivec2 kk_3 = _629 + ivec2(1);
                    int _936 = kk_3.x;
                    if (_936 < 0)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_3.y < 0;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = _936 >= _373.x;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_3.y >= _373.y;
                    }
                    if (!(_17))
                    {
                        vec2 _971 = ((vec2(kk_3) + vec2(0.5)) - sj) - inputPos;
                        float _2007 = clamp((_578 * _578) * dot(_971, _971), 0.0, 1.0);
                        float _2014 = clamp(((1.0 - (1.89999997615814208984375 * _2007)) + ((0.89999997615814208984375 * _2007) * _2007)) + 0.004999999888241291046142578125, 0.0, 1.0);
                        vec3 _979 = max(texelFetch(_2604, ivec3(kk_3, 0).xy, 0).xyz, vec3(0.0));
                        vec3 _2018 = _979 * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                        float hw_4 = _2014 * max(9.9999997473787516355514526367188e-05, 1.0 / (((_2018.x + (2.0 * _2018.y)) + _2018.z) + 4.0));
                        sumColour += (_979 * hw_4);
                        lerpAmt += hw_4;
                        cov += _2014;
                    }
                }
                {
                    ivec2 kk_4 = _629 + ivec2(2, 1);
                    int _999 = kk_4.x;
                    if (_999 < 0)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_4.y < 0;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = _999 >= _373.x;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_4.y >= _373.y;
                    }
                    if (!(_17))
                    {
                        vec2 _1034 = ((vec2(kk_4) + vec2(0.5)) - sj) - inputPos;
                        float _2039 = clamp((_578 * _578) * dot(_1034, _1034), 0.0, 1.0);
                        float _2046 = clamp(((1.0 - (1.89999997615814208984375 * _2039)) + ((0.89999997615814208984375 * _2039) * _2039)) + 0.004999999888241291046142578125, 0.0, 1.0);
                        vec3 _1042 = max(texelFetch(_2604, ivec3(kk_4, 0).xy, 0).xyz, vec3(0.0));
                        vec3 _2050 = _1042 * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                        float hw_5 = _2046 * max(9.9999997473787516355514526367188e-05, 1.0 / (((_2050.x + (2.0 * _2050.y)) + _2050.z) + 4.0));
                        sumColour += (_1042 * hw_5);
                        lerpAmt += hw_5;
                        cov += _2046;
                    }
                }
                {
                    ivec2 kk_5 = _629 + ivec2(0, 2);
                    int _1069 = kk_5.x;
                    if (_1069 < 0)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_5.y < 0;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = _1069 >= _373.x;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_5.y >= _373.y;
                    }
                    if (!(_17))
                    {
                        vec2 _1104 = ((vec2(kk_5) + vec2(0.5)) - sj) - inputPos;
                        float _2071 = clamp((_578 * _578) * dot(_1104, _1104), 0.0, 1.0);
                        float _2078 = clamp(((1.0 - (1.89999997615814208984375 * _2071)) + ((0.89999997615814208984375 * _2071) * _2071)) + 0.004999999888241291046142578125, 0.0, 1.0);
                        vec3 _1112 = max(texelFetch(_2604, ivec3(kk_5, 0).xy, 0).xyz, vec3(0.0));
                        vec3 _2082 = _1112 * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                        float hw_6 = _2078 * max(9.9999997473787516355514526367188e-05, 1.0 / (((_2082.x + (2.0 * _2082.y)) + _2082.z) + 4.0));
                        sumColour += (_1112 * hw_6);
                        lerpAmt += hw_6;
                        cov += _2078;
                    }
                }
                {
                    ivec2 kk_6 = _629 + ivec2(1, 2);
                    int _1132 = kk_6.x;
                    if (_1132 < 0)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_6.y < 0;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = _1132 >= _373.x;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_6.y >= _373.y;
                    }
                    if (!(_17))
                    {
                        vec2 _1167 = ((vec2(kk_6) + vec2(0.5)) - sj) - inputPos;
                        float _2103 = clamp((_578 * _578) * dot(_1167, _1167), 0.0, 1.0);
                        float _2110 = clamp(((1.0 - (1.89999997615814208984375 * _2103)) + ((0.89999997615814208984375 * _2103) * _2103)) + 0.004999999888241291046142578125, 0.0, 1.0);
                        vec3 _1175 = max(texelFetch(_2604, ivec3(kk_6, 0).xy, 0).xyz, vec3(0.0));
                        vec3 _2114 = _1175 * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                        float hw_7 = _2110 * max(9.9999997473787516355514526367188e-05, 1.0 / (((_2114.x + (2.0 * _2114.y)) + _2114.z) + 4.0));
                        sumColour += (_1175 * hw_7);
                        lerpAmt += hw_7;
                        cov += _2110;
                    }
                }
                {
                    ivec2 kk_7 = _629 + ivec2(2);
                    int _1195 = kk_7.x;
                    if (_1195 < 0)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_7.y < 0;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = _1195 >= _373.x;
                    }
                    if (_17)
                    {
                        _17 = true;
                    }
                    else
                    {
                        _17 = kk_7.y >= _373.y;
                    }
                    if (!(_17))
                    {
                        vec2 _1230 = ((vec2(kk_7) + vec2(0.5)) - sj) - inputPos;
                        float _2135 = clamp((_578 * _578) * dot(_1230, _1230), 0.0, 1.0);
                        float _2142 = clamp(((1.0 - (1.89999997615814208984375 * _2135)) + ((0.89999997615814208984375 * _2135) * _2135)) + 0.004999999888241291046142578125, 0.0, 1.0);
                        vec3 _1238 = max(texelFetch(_2604, ivec3(kk_7, 0).xy, 0).xyz, vec3(0.0));
                        vec3 _2146 = _1238 * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                        float hw_8 = _2142 * max(9.9999997473787516355514526367188e-05, 1.0 / (((_2146.x + (2.0 * _2146.y)) + _2146.z) + 4.0));
                        lerpAmt += hw_8;
                        sumColour += (_1238 * hw_8);
                        cov += _2142;
                    }
                }
                vec3 current;
                if (lerpAmt > 9.9999999747524270787835121154785e-07)
                {
                    current = sumColour / vec3(lerpAmt);
                }
                else
                {
                    current = max(textureLod(_2588, uvJ, 0.0).xyz, vec3(0.0));
                }
                float _1274 = clamp(cov, 0.0, 1.0);
                if (foreground)
                {
                    imageStore(uHistoryColour, ivec2(gl_GlobalInvocationID.xy), vec4(current, 1.0));
                    imageStore(uHistoryMetadata, ivec2(gl_GlobalInvocationID.xy), vec4(_1274 / _356, 0.0, 0.0, 0.0));
                }
                else
                {
                    vec4 sampled = textureLod(_2588, uvJ + (vec2(-1.0) / p._input.xy), 0.0);
                    vec3 _2165 = max(sampled.xyz, vec3(0.0)) * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                    vec3 _2168 = _2165 / (_2165 + vec3(0.17000000178813934326171875));
                    vec3 _2169 = _2168 * _2168;
                    vec4 sampled_1 = textureLod(_2588, uvJ + (vec2(0.0, -1.0) / p._input.xy), 0.0);
                    vec3 _2179 = max(sampled_1.xyz, vec3(0.0)) * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                    vec3 _2182 = _2179 / (_2179 + vec3(0.17000000178813934326171875));
                    vec3 _2183 = _2182 * _2182;
                    vec4 sampled_2 = textureLod(_2588, uvJ + (vec2(1.0, -1.0) / p._input.xy), 0.0);
                    vec3 _2193 = max(sampled_2.xyz, vec3(0.0)) * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                    vec3 _2196 = _2193 / (_2193 + vec3(0.17000000178813934326171875));
                    vec3 _2197 = _2196 * _2196;
                    vec4 sampled_3 = textureLod(_2588, uvJ + (vec2(-1.0, 0.0) / p._input.xy), 0.0);
                    vec3 _2207 = max(sampled_3.xyz, vec3(0.0)) * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                    vec3 _2210 = _2207 / (_2207 + vec3(0.17000000178813934326171875));
                    vec3 _2211 = _2210 * _2210;
                    vec4 sampled_4 = textureLod(_2588, uvJ, 0.0);
                    vec3 _2221 = max(sampled_4.xyz, vec3(0.0)) * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                    vec3 _2224 = _2221 / (_2221 + vec3(0.17000000178813934326171875));
                    vec3 _2225 = _2224 * _2224;
                    vec4 sampled_5 = textureLod(_2588, uvJ + (vec2(1.0, 0.0) / p._input.xy), 0.0);
                    vec3 _2235 = max(sampled_5.xyz, vec3(0.0)) * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                    vec3 _2238 = _2235 / (_2235 + vec3(0.17000000178813934326171875));
                    vec3 _2239 = _2238 * _2238;
                    vec4 sampled_6 = textureLod(_2588, uvJ + (vec2(-1.0, 1.0) / p._input.xy), 0.0);
                    vec3 _2249 = max(sampled_6.xyz, vec3(0.0)) * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                    vec3 _2252 = _2249 / (_2249 + vec3(0.17000000178813934326171875));
                    vec3 _2253 = _2252 * _2252;
                    vec4 sampled_7 = textureLod(_2588, uvJ + (vec2(0.0, 1.0) / p._input.xy), 0.0);
                    vec3 _2263 = max(sampled_7.xyz, vec3(0.0)) * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                    vec3 _2266 = _2263 / (_2263 + vec3(0.17000000178813934326171875));
                    vec3 _2267 = _2266 * _2266;
                    vec4 sampled_8 = textureLod(_2588, uvJ + (vec2(1.0) / p._input.xy), 0.0);
                    vec3 _2277 = max(sampled_8.xyz, vec3(0.0)) * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                    vec3 _2280 = _2277 / (_2277 + vec3(0.17000000178813934326171875));
                    vec3 _2281 = _2280 * _2280;
                    vec3 m1 = (((((((_2169 + _2183) + _2197) + _2211) + _2225) + _2239) + _2253) + _2267) + _2281;
                    float _1475 = clamp(4.0 * max(0.0, max(max(max(max(max(max(max(max(max(0.0, sampled.w - 1.0), sampled_1.w - 1.0), sampled_2.w - 1.0), sampled_3.w - 1.0), sampled_4.w - 1.0), sampled_5.w - 1.0), sampled_6.w - 1.0), sampled_7.w - 1.0), sampled_8.w - 1.0)), 0.0, 1.0);
                    vec3 mu = m1 * vec3(0.111111111938953399658203125);
                    vec3 _1484 = sqrt(max(((((((((((_2169 * _2169) + (_2183 * _2183)) + (_2197 * _2197)) + (_2211 * _2211)) + (_2225 * _2225)) + (_2239 * _2239)) + (_2253 * _2253)) + (_2267 * _2267)) + (_2281 * _2281)) * vec3(0.111111111938953399658203125)) - (vec3(0.012345679104328155517578125) * (m1 * m1)), vec3(0.0)));
                    vec3 _1486 = max(min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), _2169), _2183), _2197), _2211), _2225), _2239), _2253), _2267), _2281), mu - _1484);
                    vec3 _1488 = min(max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), _2169), _2183), _2197), _2211), _2225), _2239), _2253), _2267), _2281), mu + _1484);
                    if (offScreen)
                    {
                        imageStore(uHistoryColour, ivec2(gl_GlobalInvocationID.xy), vec4(current, 1.0 - _1475));
                        imageStore(uHistoryMetadata, ivec2(gl_GlobalInvocationID.xy), vec4(1.0 / _356, 0.0, 0.0, 0.0));
                    }
                    else
                    {
                        if (believable < 0.5)
                        {
                            imageStore(uHistoryColour, ivec2(gl_GlobalInvocationID.xy), vec4(current, 1.0 - _1475));
                            imageStore(uHistoryMetadata, ivec2(gl_GlobalInvocationID.xy), vec4(_1274 / _356, 0.0, 0.0, 0.0));
                        }
                        else
                        {
                            vec2 _2294 = prevUv * p.history.xy;
                            vec2 _2297 = floor(_2294 - vec2(0.5));
                            vec2 _2299 = _2297 + vec2(0.5);
                            vec2 _2300 = _2294 - _2299;
                            vec2 _2301 = _2300 * 0.5;
                            vec2 _2307 = _2300 * (vec2(-0.5) + (_2300 * (vec2(1.0) - _2301)));
                            vec2 _2308 = _2300 * _2300;
                            vec2 _2309 = _2300 * 1.5;
                            vec2 _2320 = _2300 * (vec2(0.5) + (_2300 * (vec2(2.0) - _2309)));
                            vec2 _2323 = _2308 * (vec2(-0.5) + _2301);
                            vec2 _2324 = (vec2(1.0) + (_2308 * (vec2(-2.5) + _2309))) + _2320;
                            vec2 _2334 = (_2297 + vec2(-0.5)) * p.history.zw;
                            vec2 _2337 = (_2299 + (_2320 / max(_2324, vec2(9.9999997473787516355514526367188e-06)))) * p.history.zw;
                            vec2 _2340 = (_2297 + vec2(2.5)) * p.history.zw;
                            float _2341 = _2334.x;
                            float _2342 = _2337.x;
                            float _2343 = _2340.x;
                            float _2344 = _2334.y;
                            float _2345 = _2337.y;
                            float _2346 = _2340.y;
                            float _2347 = _2307.x;
                            float _2348 = _2324.x;
                            float _2349 = _2323.x;
                            float _2350 = _2307.y;
                            float _2351 = _2324.y;
                            float _2352 = _2323.y;
                            vec4 _2432 = ((((((((textureLod(_2606, _2334, 0.0) * (_2347 * _2350)) + (textureLod(_2606, vec2(_2342, _2344), 0.0) * (_2348 * _2350))) + (textureLod(_2606, vec2(_2343, _2344), 0.0) * (_2349 * _2350))) + (textureLod(_2606, vec2(_2341, _2345), 0.0) * (_2347 * _2351))) + (textureLod(_2606, _2337, 0.0) * (_2348 * _2351))) + (textureLod(_2606, vec2(_2343, _2345), 0.0) * (_2349 * _2351))) + (textureLod(_2606, vec2(_2341, _2346), 0.0) * (_2347 * _2352))) + (textureLod(_2606, vec2(_2342, _2346), 0.0) * (_2348 * _2352))) + (textureLod(_2606, _2340, 0.0) * (_2349 * _2352));
                            vec3 _1671 = max(_2432.xyz, vec3(0.0));
                            float count = _356 * textureLod(_2608, prevUv, 0.0).x;
                            bool _2433;
                            if (any(isnan(_1671)))
                            {
                                _2433 = true;
                            }
                            else
                            {
                                _2433 = any(isinf(_1671));
                            }
                            if (!(!_2433))
                            {
                                offScreen = true;
                            }
                            else
                            {
                                offScreen = count < 0.001000000047497451305389404296875;
                            }
                            if (offScreen)
                            {
                                imageStore(uHistoryColour, ivec2(gl_GlobalInvocationID.xy), vec4(current, 1.0 - _1475));
                                imageStore(uHistoryMetadata, ivec2(gl_GlobalInvocationID.xy), vec4(_1274 / _356, 0.0, 0.0, 0.0));
                            }
                            else
                            {
                                vec3 _2448 = _1671 * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                                vec3 _2451 = _2448 / (_2448 + vec3(0.17000000178813934326171875));
                                vec3 _2452 = _2451 * _2451;
                                vec3 centre = (_1486 + _1488) * 0.5;
                                vec3 e = _2452 - centre;
                                vec3 ratio = abs(e) / max((_1488 - _1486) * 0.5, vec3(9.9999997473787516355514526367188e-06));
                                float _1738 = max(ratio.x, max(ratio.y, ratio.z));
                                vec3 h;
                                if (_1738 > 1.0)
                                {
                                    h = centre + (e / vec3(_1738));
                                }
                                else
                                {
                                    h = _2452;
                                }
                                vec3 _2462 = sqrt(max(h, vec3(0.0)));
                                vec3 _2470 = ((_2462 * 0.17000000178813934326171875) / max(vec3(1.0) - _2462, vec3(9.9999997473787516355514526367188e-05))) / vec3(max(p.control3.x, 9.9999997473787516355514526367188e-05));
                                float _1768 = clamp(1.0 - _2432.w, 0.0, 1.0);
                                float _1770 = min(relaxClamp_1, 1.0 - _1768);
                                vec3 _2480 = _2470 * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                                float wClip = (1.0 - _1770) * max(9.9999997473787516355514526367188e-05, 1.0 / (((_2480.x + (2.0 * _2480.y)) + _2480.z) + 4.0));
                                vec3 _2499 = _1671 * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                                float wLin = _1770 * max(9.9999997473787516355514526367188e-05, 1.0 / (((_2499.x + (2.0 * _2499.y)) + _2499.z) + 4.0));
                                vec3 hist = ((_2470 * wClip) + (_1671 * wLin)) / vec3(max(wClip + wLin, 9.9999999747524270787835121154785e-07));
                                float _1790 = min(min(count + _1274, _356), mix(_356, 3.0, clamp(_481 * 0.125, 0.0, 1.0)));
                                float blend;
                                if (_1790 > 9.9999999747524270787835121154785e-07)
                                {
                                    blend = clamp(_1274 / _1790, 0.0, 1.0);
                                }
                                else
                                {
                                    blend = 1.0;
                                }
                                float _2522 = (current.x + (2.0 * current.y)) + current.z;
                                float _2530 = (hist.x + (2.0 * hist.y)) + hist.z;
                                float _1836 = max(max(max(blend, 1.0 - max(1.0 - (clamp(1.0 - (4.0 / _356), 0.0, 1.0) * clamp(_481, 0.0, 1.0)), clamp(abs(_2522 - _2530) / max(max(_2522, _2530), 9.9999997473787516355514526367188e-05), 0.0, 1.0))), clamp(2.0 * (0.5 - min(shading, _1770)), 0.0, 1.0)), 0.85000002384185791015625 * _1475);
                                vec3 _2534 = current * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                                float wc = _1836 * max(9.9999997473787516355514526367188e-05, 1.0 / (((_2534.x + (2.0 * _2534.y)) + _2534.z) + 4.0));
                                vec3 _2553 = hist * max(p.control3.x, 9.9999997473787516355514526367188e-05);
                                float wh = (1.0 - _1836) * max(9.9999997473787516355514526367188e-05, 1.0 / (((_2553.x + (2.0 * _2553.y)) + _2553.z) + 4.0));
                                imageStore(uHistoryColour, ivec2(gl_GlobalInvocationID.xy), vec4(max(((current * wc) + (hist * wh)) / vec3(max(wc + wh, 9.9999999747524270787835121154785e-07)), vec3(0.0)), 1.0 - max(_1475, 0.5 * _1768)));
                                imageStore(uHistoryMetadata, ivec2(gl_GlobalInvocationID.xy), vec4(min(_1790, max(_1274, 0.25) / max(_1836, 0.001000000047497451305389404296875)) / _356, 0.0, 0.0, 0.0));
                            }
                        }
                    }
                }
            }
        }
    }
}

