#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
    vec4 control;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uDilated;
layout(binding = 0) uniform sampler2D _405;

void main()
{
    {
        ivec2 _86 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 _96 = ivec2(p.size.xy);
        int _98 = _96.x;
        bool _7;
        if (_86.x >= _98)
        {
            _7 = true;
        }
        else
        {
            _7 = _86.y >= _96.y;
        }
        if (!(_7))
        {
            vec4 sampled = texelFetch(_405, ivec3(_86, 0).xy, 0);
            float _335 = length(sampled.zw);
            float _332;
            if (_335 > 0.0)
            {
                _332 = atan(sampled.w, sampled.z);
            }
            else
            {
                _332 = 0.0;
            }
            float _348 = length(sampled.xy);
            float _345;
            if (_348 > 0.0)
            {
                _345 = atan(sampled.y, sampled.x);
            }
            else
            {
                _345 = 0.0;
            }
            vec2 fastest = vec2(_335, _332);
            vec2 slowest = vec2(_348, _345);
            int y = -3;
            bool _19;
            bool _20;
            bool _21;
            bool _22;
            vec2 fastest_2;
            vec2 slowest_2;
            float _358;
            float _371;
            for (;;)
            {
                if (!(y <= 3))
                {
                    break;
                }
                vec2 fastest_1 = fastest;
                vec2 slowest_1 = slowest;
                int x = -3;
                for (;;)
                {
                    bool _37_ladder_break = false;
                    {
                        if (!(x <= 3))
                        {
                            _37_ladder_break = true;
                        }
                        else
                        {
                            if (x == 0)
                            {
                                _7 = y == 0;
                            }
                            else
                            {
                                _7 = false;
                            }
                            if (!(_7))
                            {
                                ivec2 neighbour = _86 + ivec2(x, y);
                                int _182 = neighbour.x;
                                if (_182 < 0)
                                {
                                    _19 = true;
                                }
                                else
                                {
                                    _19 = neighbour.y < 0;
                                }
                                if (_19)
                                {
                                    _20 = true;
                                }
                                else
                                {
                                    _20 = _182 >= _98;
                                }
                                if (_20)
                                {
                                    _21 = true;
                                }
                                else
                                {
                                    _21 = neighbour.y >= _96.y;
                                }
                                if (!(_21))
                                {
                                    vec4 sampled_1 = texelFetch(_405, ivec3(neighbour, 0).xy, 0);
                                    vec2 velocity = sampled_1.zw * p.control.x;
                                    float _221 = dot(velocity, velocity);
                                    float _224 = inversesqrt(_221 + 9.9999999392252902907785028219223e-09);
                                    vec2 direction = velocity * _224;
                                    float _227 = direction.x;
                                    float _229 = direction.y;
                                    float margin = 0.9900000095367431640625 * (abs(_227) + abs(_229));
                                    vec2 offset = vec2(float(x), float(y));
                                    if (abs(dot(direction, offset)) < ((_221 * _224) + margin))
                                    {
                                        _22 = abs(dot(vec2(-_229, _227), offset)) < margin;
                                    }
                                    else
                                    {
                                        _22 = false;
                                    }
                                    if (_22)
                                    {
                                        float _361 = length(velocity);
                                        if (_361 > 0.0)
                                        {
                                            _358 = atan(velocity.y, velocity.x);
                                        }
                                        else
                                        {
                                            _358 = 0.0;
                                        }
                                        vec2 _370 = vec2(_361, _358);
                                        float _374 = length(sampled_1.xy);
                                        if (_374 > 0.0)
                                        {
                                            _371 = atan(sampled_1.y, sampled_1.x);
                                        }
                                        else
                                        {
                                            _371 = 0.0;
                                        }
                                        vec2 _383 = vec2(_374, _371);
                                        if (fastest_1.x > _370.x)
                                        {
                                            fastest_2 = fastest_1;
                                        }
                                        else
                                        {
                                            fastest_2 = _370;
                                        }
                                        if (slowest_1.x > _383.x)
                                        {
                                            slowest_2 = _383;
                                        }
                                        else
                                        {
                                            slowest_2 = slowest_1;
                                        }
                                    }
                                    else
                                    {
                                        fastest_2 = fastest_1;
                                        slowest_2 = slowest_1;
                                    }
                                    fastest_1 = fastest_2;
                                    slowest_1 = slowest_2;
                                }
                            }
                        }
                    }
                    if (_37_ladder_break)
                    {
                        break;
                    }
                    x++;
                    continue;
                }
                fastest = fastest_1;
                slowest = slowest_1;
                y++;
                continue;
            }
            imageStore(uDilated, ivec2(gl_GlobalInvocationID.xy), vec4(vec2(cos(slowest.y), sin(slowest.y)) * slowest.x, vec2(cos(fastest.y), sin(fastest.y)) * fastest.x));
        }
    }
}

