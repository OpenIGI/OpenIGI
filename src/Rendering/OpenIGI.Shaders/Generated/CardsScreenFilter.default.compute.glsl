#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

uniform ivec2 _205_comp[4] = ivec2[](ivec2(1, 0), ivec2(-1, 0), ivec2(0, 1), ivec2(0, -1));

layout(binding = 0, std140) uniform Globals_std140
{
    int uWidth;
    int uHeight;
    int uRadius;
    int uAccum;
    int uPosition;
    int uSceneNormal;
} g;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uFiltered;
layout(binding = 1) uniform sampler2D _480;
layout(binding = 0) uniform sampler2D _483;
layout(binding = 2) uniform sampler2D _485;

void main()
{
    {
        ivec2 _89 = ivec2(gl_GlobalInvocationID.xy);
        bool _7;
        if (_89.x >= g.uWidth)
        {
            _7 = true;
        }
        else
        {
            _7 = _89.y >= g.uHeight;
        }
        if (!(_7))
        {
            ivec2 _115 = ivec3(_89, 0).xy;
            vec4 sampled = texelFetch(_480, _115, 0);
            vec4 sampled_1 = texelFetch(_483, _115, 0);
            if (sampled.w <= 0.0)
            {
                imageStore(uFiltered, ivec2(gl_GlobalInvocationID.xy), sampled_1);
            }
            else
            {
                vec3 _138 = sampled.xyz;
                vec4 _432 = texture(_485, (vec2(_89) + vec2(0.5)) / vec2(float(g.uWidth), float(g.uHeight)));
                vec3 _418 = (_432.xyz * 2.0) - vec3(1.0);
                float _419 = length(_418);
                vec3 _399;
                if (_419 > 0.001000000047497451305389404296875)
                {
                    _399 = _418 / vec3(_419);
                }
                else
                {
                    _399 = vec3(0.0);
                }
                int i = 0;
                float spacing = 0.0;
                float spacingCount = 0.0;
                for (;;)
                {
                    if (!(i < 4))
                    {
                        break;
                    }
                    vec4 sampled_2 = texelFetch(_480, ivec3(clamp(_89 + _205_comp[i], ivec2(0), ivec2(g.uWidth - 1, g.uHeight - 1)), 0).xy, 0);
                    if (sampled_2.w > 0.0)
                    {
                        spacing += length(sampled_2.xyz - _138);
                        spacingCount += 1.0;
                    }
                    i++;
                    continue;
                }
                if (spacingCount > 0.0)
                {
                    spacing /= spacingCount;
                }
                else
                {
                    spacing = 1.0;
                }
                float _263 = ((2.0 * spacing) * spacing) + 9.9999999747524270787835121154785e-07;
                int y = -g.uRadius;
                vec3 sum = vec3(0.0);
                float weight = 0.0;
                vec3 filtered;
                float facing;
                vec3 _434;
                for (;;)
                {
                    if (!(y <= g.uRadius))
                    {
                        break;
                    }
                    int x = -g.uRadius;
                    filtered = sum;
                    float weight_1 = weight;
                    for (;;)
                    {
                        bool _58_ladder_break = false;
                        {
                            if (!(x <= g.uRadius))
                            {
                                _58_ladder_break = true;
                            }
                            else
                            {
                                ivec2 _298 = clamp(_89 + ivec2(x, y), ivec2(0), ivec2(g.uWidth - 1, g.uHeight - 1));
                                ivec2 _301 = ivec3(_298, 0).xy;
                                vec4 sampled_3 = texelFetch(_480, _301, 0);
                                if (!(sampled_3.w <= 0.0))
                                {
                                    vec4 sampled_4 = texelFetch(_483, _301, 0);
                                    vec3 delta = sampled_3.xyz - _138;
                                    vec4 _466 = texture(_485, (vec2(_298) + vec2(0.5)) / vec2(float(g.uWidth), float(g.uHeight)));
                                    vec3 _453 = (_466.xyz * 2.0) - vec3(1.0);
                                    float _454 = length(_453);
                                    if (_454 > 0.001000000047497451305389404296875)
                                    {
                                        _434 = _453 / vec3(_454);
                                    }
                                    else
                                    {
                                        _434 = vec3(0.0);
                                    }
                                    if (dot(_399, _399) < 0.5)
                                    {
                                        _7 = true;
                                    }
                                    else
                                    {
                                        _7 = dot(_434, _434) < 0.5;
                                    }
                                    if (_7)
                                    {
                                        facing = 1.0;
                                    }
                                    else
                                    {
                                        float _470 = max(dot(_399, _434), 0.0);
                                        float _471 = _470 * _470;
                                        facing = _471 * _471;
                                    }
                                    float w = (exp((-dot(delta, delta)) / _263) * (sampled_4.w + 1.0)) * facing;
                                    filtered += (sampled_4.xyz * w);
                                    weight_1 += w;
                                }
                            }
                        }
                        if (_58_ladder_break)
                        {
                            break;
                        }
                        x++;
                        continue;
                    }
                    y++;
                    sum = filtered;
                    weight = weight_1;
                    continue;
                }
                if (weight > 0.0)
                {
                    filtered = sum / vec3(weight);
                }
                else
                {
                    filtered = sampled_1.xyz;
                }
                imageStore(uFiltered, ivec2(gl_GlobalInvocationID.xy), vec4(filtered, sampled_1.w));
            }
        }
    }
}

