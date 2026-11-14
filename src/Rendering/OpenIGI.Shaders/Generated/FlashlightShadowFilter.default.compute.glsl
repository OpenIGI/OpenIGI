#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

uniform vec2 _245_comp[12] = vec2[](vec2(0.20409999787807464599609375, 0.0), vec2(-0.2676999866962432861328125, 0.2454999983310699462890625), vec2(0.03689999878406524658203125, -0.4941000044345855712890625), vec2(0.3420999944210052490234375, 0.4695999920368194580078125), vec2(-0.638899981975555419921875, -0.10629999637603759765625), vec2(0.620299994945526123046875, -0.3711999952793121337890625), vec2(-0.203299999237060546875, 0.73940002918243408203125), vec2(-0.3711999952793121337890625, -0.72500002384185791015625), vec2(0.811999976634979248046875, 0.22480000555515289306640625), vec2(-0.84549999237060546875, 0.328299999237060546875), vec2(0.3176000118255615234375, -0.891099989414215087890625), vec2(0.34189999103546142578125, 0.9398000240325927734375));

layout(binding = 0, std140) uniform Params_std140
{
    vec4 resolution;
    vec4 unproject;
    vec4 lens;
} p;

layout(binding = 0, rg16f) uniform restrict writeonly image2D uMask;
layout(binding = 0) uniform sampler2D _383;
layout(binding = 1) uniform sampler2D _386;

void main()
{
    {
        ivec2 _71 = ivec2(gl_GlobalInvocationID.xy);
        bool _7;
        if (_71.x >= int(p.resolution.x))
        {
            _7 = true;
        }
        else
        {
            _7 = _71.y >= int(p.resolution.y);
        }
        if (!(_7))
        {
            ivec2 _100 = ivec3(_71, 0).xy;
            vec4 sampled = texelFetch(_383, _100, 0);
            vec4 sampled_1 = texelFetch(_386, _100, 0);
            float _112 = sampled_1.x;
            if (_112 <= 0.0)
            {
                imageStore(uMask, ivec2(gl_GlobalInvocationID.xy), vec4(1.0, 0.0, 0.0, 0.0));
            }
            else
            {
                float _362 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _112)) * (p.unproject.w - p.unproject.z)));
                ivec2 _151 = ivec2(p.resolution.xy) - ivec2(1);
                float ratio = sampled.y;
                int y = -1;
                int i;
                for (;;)
                {
                    if (!(y <= 1))
                    {
                        break;
                    }
                    i = -1;
                    for (;;)
                    {
                        if (!(i <= 1))
                        {
                            break;
                        }
                        ratio = max(ratio, texelFetch(_383, ivec3(clamp(_71 + (ivec2(i, y) * ivec2(2)), ivec2(0), _151), 0).xy, 0).y);
                        i++;
                        continue;
                    }
                    y++;
                    continue;
                }
                if (ratio <= 0.0)
                {
                    imageStore(uMask, ivec2(gl_GlobalInvocationID.xy), vec4(sampled.x, 0.0, 0.0, 0.0));
                }
                else
                {
                    float _222 = clamp((p.lens.x * ratio) / max((2.0 * _362) / max(p.unproject.x * p.resolution.x, 9.9999997473787516355514526367188e-05), 9.9999999747524270787835121154785e-07), 1.0, 12.0);
                    float angle = fract(sin(dot(vec2(_71), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875) * 6.283185482025146484375;
                    float _235 = cos(angle);
                    float _236 = sin(angle);
                    i = 0;
                    float total = sampled.x;
                    float weights = 1.0;
                    for (;;)
                    {
                        bool _53_ladder_break = false;
                        {
                            if (!(i < 12))
                            {
                                _53_ladder_break = true;
                            }
                            else
                            {
                                ivec2 _300 = ivec3(clamp(_71 + ivec2(round(vec2((_245_comp[i].x * _235) - (_245_comp[i].y * _236), (_245_comp[i].x * _236) + (_245_comp[i].y * _235)) * _222)), ivec2(0), _151), 0).xy;
                                vec4 sampled_2 = texelFetch(_386, _300, 0);
                                float _303 = sampled_2.x;
                                if (!(_303 <= 0.0))
                                {
                                    float _317 = exp2((-abs((((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _303)) * (p.unproject.w - p.unproject.z)))) - _362)) / max(0.0199999995529651641845703125 * _362, 0.00999999977648258209228515625));
                                    total += (texelFetch(_383, _300, 0).x * _317);
                                    weights += _317;
                                }
                            }
                        }
                        if (_53_ladder_break)
                        {
                            break;
                        }
                        i++;
                        continue;
                    }
                    imageStore(uMask, ivec2(gl_GlobalInvocationID.xy), vec4(total / weights, ratio, 0.0, 0.0));
                }
            }
        }
    }
}

