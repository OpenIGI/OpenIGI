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
    vec4 control;
    vec4 jitter;
} p;

layout(binding = 0, r8) uniform restrict image2D uShadow;
layout(binding = 1, rgba16f) uniform restrict writeonly image2D uHistory;
layout(binding = 0) uniform sampler2D _850;
layout(binding = 2) uniform sampler2D _853;
layout(binding = 1) uniform sampler2D _855;

shared float gCurrent[8][8];

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    {
        ivec2 _100 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 _110 = ivec2(p.resolution.xy);
        bool inside;
        if (_100.x < _110.x)
        {
            inside = _100.y < _110.y;
        }
        else
        {
            inside = false;
        }
        float current;
        if (inside)
        {
            current = imageLoad(uShadow, ivec2(gl_GlobalInvocationID.xy)).x;
        }
        else
        {
            current = 1.0;
        }
        gCurrent[gl_LocalInvocationID.y][gl_LocalInvocationID.x] = current;
        barrier();
        if (!(!inside))
        {
            float currentMin = current;
            float currentMax = current;
            int ny = -1;
            float accumulated;
            float count;
            float currentMin_1;
            float currentMax_1;
            for (;;)
            {
                if (!(ny <= 1))
                {
                    break;
                }
                accumulated = currentMin;
                count = currentMax;
                int nx = -1;
                for (;;)
                {
                    if (!(nx <= 1))
                    {
                        break;
                    }
                    ivec2 n = ivec2(gl_LocalInvocationID.xy) + ivec2(nx, ny);
                    if (all(greaterThanEqual(n, ivec2(0))))
                    {
                        inside = all(lessThan(n, ivec2(8)));
                    }
                    else
                    {
                        inside = false;
                    }
                    if (inside)
                    {
                        int _201 = n.y;
                        int _203 = n.x;
                        currentMin_1 = min(accumulated, gCurrent[_201][_203]);
                        currentMax_1 = max(count, gCurrent[_201][_203]);
                    }
                    else
                    {
                        currentMin_1 = accumulated;
                        currentMax_1 = count;
                    }
                    accumulated = currentMin_1;
                    count = currentMax_1;
                    nx++;
                    continue;
                }
                currentMin = accumulated;
                currentMax = count;
                ny++;
                continue;
            }
            ivec2 _243 = ivec3(_100, 0).xy;
            vec4 sampled = texelFetch(_850, _243, 0);
            float depth = sampled.x;
            vec2 uv = ((vec2(_100) + vec2(0.5)) - p.jitter.xy) * p.resolution.zw;
            if (depth <= 0.0)
            {
                imageStore(uHistory, ivec2(gl_GlobalInvocationID.xy), vec4(current, 0.0, 0.0, 0.0));
            }
            else
            {
                float _681 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * depth)) * (p.unproject.w - p.unproject.z)));
                vec2 _684 = (uv * 2.0) - vec2(1.0);
                vec3 _696 = vec3((_684.x * _681) / p.unproject.x, ((-_684.y) * _681) / p.unproject.y, _681);
                vec4 _328 = vec4((vec4(_696, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz + p.cameraDelta.xyz, 0.0) * spvWorkaroundRowMajor(p.prevWorldToView);
                if (p.control.y > 0.5)
                {
                    inside = _328.z > p.prevProjection.z;
                }
                else
                {
                    inside = false;
                }
                if (inside)
                {
                    float _353 = _328.z;
                    vec2 prevUnjittered_1 = (vec2((_328.x * p.prevProjection.x) / _353, ((-_328.y) * p.prevProjection.y) / _353) * 0.5) + vec2(0.5);
                    bool ownMotion;
                    vec2 prevUnjittered;
                    if (p.control.w > 0.5)
                    {
                        vec2 motion = texelFetch(_853, _243, 0).xy;
                        vec2 apart = (motion - (uv - prevUnjittered_1)) * p.resolution.xy;
                        bool _380 = dot(apart, apart) > 2.25;
                        if (_380)
                        {
                            prevUnjittered = uv - motion;
                        }
                        else
                        {
                            prevUnjittered = prevUnjittered_1;
                        }
                        ownMotion = _380;
                    }
                    else
                    {
                        prevUnjittered = prevUnjittered_1;
                        ownMotion = false;
                    }
                    vec2 prevUv = prevUnjittered + (p.jitter.zw * p.resolution.zw);
                    if (all(greaterThanEqual(prevUv, vec2(0.0))))
                    {
                        inside = all(lessThanEqual(prevUv, vec2(1.0)));
                    }
                    else
                    {
                        inside = false;
                    }
                    if (inside)
                    {
                        vec4 sampled_1 = textureLod(_855, prevUv, 0.0);
                        vec2 _704 = prevUv * p.resolution.xy;
                        vec2 _707 = floor(_704 - vec2(0.5));
                        vec2 _709 = _707 + vec2(0.5);
                        vec2 _710 = _704 - _709;
                        vec2 _711 = _710 * 0.5;
                        vec2 _717 = _710 * (vec2(-0.5) + (_710 * (vec2(1.0) - _711)));
                        vec2 _718 = _710 * _710;
                        vec2 _719 = _710 * 1.5;
                        vec2 _730 = _710 * (vec2(0.5) + (_710 * (vec2(2.0) - _719)));
                        vec2 _733 = _718 * (vec2(-0.5) + _711);
                        vec2 _734 = (vec2(1.0) + (_718 * (vec2(-2.5) + _719))) + _730;
                        vec2 _739 = (_707 + vec2(-0.5)) * p.resolution.zw;
                        vec2 _742 = (_707 + vec2(2.5)) * p.resolution.zw;
                        vec2 _744 = (_709 + (_730 / max(_734, vec2(9.9999997473787516355514526367188e-06)))) * p.resolution.zw;
                        float _745 = _739.x;
                        float _746 = _739.y;
                        float _753 = _717.x;
                        float _755 = _717.y;
                        float _757 = _744.x;
                        float _764 = _734.x;
                        float _768 = _742.x;
                        float _775 = _733.x;
                        float _779 = _744.y;
                        float _787 = _734.y;
                        float _808 = _742.y;
                        float _816 = _733.y;
                        sampled_1.x = clamp(((((((((((textureLod(_855, _739, 0.0) * _753) * _755) + ((textureLod(_855, vec2(_757, _746), 0.0) * _764) * _755)) + ((textureLod(_855, vec2(_768, _746), 0.0) * _775) * _755)) + ((textureLod(_855, vec2(_745, _779), 0.0) * _753) * _787)) + ((textureLod(_855, _744, 0.0) * _764) * _787)) + ((textureLod(_855, vec2(_768, _779), 0.0) * _775) * _787)) + ((textureLod(_855, vec2(_745, _808), 0.0) * _753) * _816)) + ((textureLod(_855, vec2(_757, _808), 0.0) * _764) * _816)) + ((textureLod(_855, _742, 0.0) * _775) * _816)).x, currentMin, currentMax);
                        vec4 history = sampled_1;
                        vec4 _582 = textureGather(_855, prevUv, 1);
                        bool sameSurface;
                        if (ownMotion)
                        {
                            sameSurface = true;
                        }
                        else
                        {
                            sameSurface = min(min(abs(_582.x - _353), abs(_582.y - _353)), min(abs(_582.z - _353), abs(_582.w - _353))) <= (_353 * p.control.z);
                        }
                        if (sameSurface)
                        {
                            inside = history.z > 0.0;
                        }
                        else
                        {
                            inside = false;
                        }
                        if (inside)
                        {
                            float _626 = min(history.z + 1.0, max(p.control.x, 1.0));
                            accumulated = mix(history.x, current, 1.0 / _626);
                            count = _626;
                        }
                        else
                        {
                            accumulated = current;
                            count = 1.0;
                        }
                    }
                    else
                    {
                        accumulated = current;
                        count = 1.0;
                    }
                }
                else
                {
                    accumulated = current;
                    count = 1.0;
                }
                imageStore(uShadow, ivec2(gl_GlobalInvocationID.xy), vec4(accumulated));
                imageStore(uHistory, ivec2(gl_GlobalInvocationID.xy), vec4(accumulated, _696.z, count, 0.0));
            }
        }
    }
}

