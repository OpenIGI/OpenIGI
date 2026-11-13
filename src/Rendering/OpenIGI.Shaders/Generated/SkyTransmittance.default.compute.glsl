#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
    vec4 sunDir;
    vec4 sunColour;
    vec4 misc;
    vec4 moonDir;
    vec4 moonColour;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uTransmittance;

void main()
{
    {
        ivec2 _26 = ivec2(gl_GlobalInvocationID.xy);
        bool _7;
        if (_26.x >= int(p.size.x))
        {
            _7 = true;
        }
        else
        {
            _7 = _26.y >= int(p.size.y);
        }
        if (!(_7))
        {
            vec2 uv = (vec2(_26) + vec2(0.5)) * p.size.zw;
            float _689 = uv.y;
            float _691 = 766799.9375 * (_689 * _689);
            float _693 = sqrt(_691 + 40449600.0);
            float viewHeight = _693;
            float _699 = (6420.0 - _693) + (uv.x * ((-5544.3291015625) + ((875.671142578125 * _689) + _693)));
            float _686;
            if (_699 == 0.0)
            {
                _686 = 1.0;
            }
            else
            {
                _686 = ((766799.9375 - _691) - (_699 * _699)) / ((2.0 * viewHeight) * _699);
            }
            float _714 = clamp(_686, -1.0, 1.0);
            vec3 origin = vec3(0.0, 0.0, viewHeight);
            vec3 direction = vec3(0.0, sqrt(max(0.0, 1.0 - (_714 * _714))), _714);
            vec3 _1140;
            {
                vec3 _1161 = vec3(0.0);
                if (dot(origin, origin) <= 40449600.0)
                {
                    _1140 = _1161;
                }
                else
                {
                    vec2 _944;
                    {
                        float _948 = dot(direction, direction);
                        float _949 = dot(origin, direction);
                        float _957 = (4.0 * (_949 * _949)) - ((4.0 * _948) * (dot(origin, origin) - 41216400.0));
                        if (_957 < 0.0)
                        {
                            _944 = vec2(-1.0);
                        }
                        else
                        {
                            float _961 = sqrt(_957);
                            float _962 = _949 * (-2.0);
                            float _964 = 2.0 * _948;
                            _944 = vec2((_962 - _961) / _964, (_962 + _961) / _964);
                        }
                    }
                    bool _718;
                    if (_944.x < 0.0)
                    {
                        _718 = _944.y < 0.0;
                    }
                    else
                    {
                        _718 = false;
                    }
                    if (_718)
                    {
                        _1140 = _1161;
                    }
                    else
                    {
                        vec2 _972;
                        {
                            float _976 = dot(direction, direction);
                            float _977 = dot(origin, direction);
                            float _985 = (4.0 * (_977 * _977)) - ((4.0 * _976) * (dot(origin, origin) - 40449600.0));
                            if (_985 < 0.0)
                            {
                                _972 = vec2(-1.0);
                            }
                            else
                            {
                                float _989 = sqrt(_985);
                                float _990 = _977 * (-2.0);
                                float _992 = 2.0 * _976;
                                _972 = vec2((_990 - _989) / _992, (_990 + _989) / _992);
                            }
                        }
                        if (_972.x < 0.0)
                        {
                            _718 = _972.y < 0.0;
                        }
                        else
                        {
                            _718 = false;
                        }
                        float _719;
                        float _720;
                        if (_718)
                        {
                            _719 = _944.y;
                            _720 = 0.0;
                        }
                        else
                        {
                            float _770 = max(0.0, min(_972.x, _972.y));
                            _719 = _770;
                            _720 = _770;
                        }
                        float _721;
                        float _722;
                        float _723;
                        if (false)
                        {
                            float _784 = mix(4.0, 128.0, clamp(_719 * 0.006666666828095912933349609375, 0.0, 1.0));
                            float _785 = floor(_784);
                            _721 = _784;
                            _722 = _785;
                            _723 = (_719 * _785) / _784;
                        }
                        else
                        {
                            _721 = 10.0;
                            _722 = 10.0;
                            _723 = _719;
                        }
                        float _724 = 0.0;
                        vec3 _725 = vec3(0.0);
                        float _729;
                        float _730;
                        float _1017;
                        for (;;)
                        {
                            if (!(_724 < _721))
                            {
                                break;
                            }
                            if (false)
                            {
                                float _812 = _724 / _722;
                                float _816 = (_724 + 1.0) / _722;
                                float _818 = _816 * _816;
                                float _820 = _723 * (_812 * _812);
                                if (_818 > 1.0)
                                {
                                    _729 = _719;
                                }
                                else
                                {
                                    _729 = _723 * _818;
                                }
                                float _828 = _729;
                                float _829 = _828 - _820;
                                _729 = _820 + (0.300000011920928955078125 * _829);
                                _730 = _829;
                            }
                            else
                            {
                                _729 = (_719 * (_724 + 0.300000011920928955078125)) / _721;
                                _730 = _719 / _721;
                            }
                            float _1023 = max(0.0, length(origin + (direction * _729)) - 6360.0);
                            if (_1023 < 25.0)
                            {
                                _1017 = clamp((_1023 * 0.066666670143604278564453125) - 0.666666686534881591796875, 0.0, 1.0);
                            }
                            else
                            {
                                _1017 = clamp((_1023 * (-0.066666670143604278564453125)) + 2.6666667461395263671875, 0.0, 1.0);
                            }
                            _724 += 1.0;
                            _725 += (((vec3(0.0044399998150765895843505859375 * exp(_1023 * (-0.833333313465118408203125))) + (vec3(0.00580199994146823883056640625, 0.0135580003261566162109375, 0.033100001513957977294921875) * exp(_1023 * (-0.125)))) + (vec3(0.0006500000017695128917694091796875, 0.001881000003777444362640380859375, 8.4999999671708792448043823242188e-05) * _1017)) * _730);
                            continue;
                        }
                        if (false)
                        {
                            _718 = _719 == _720;
                        }
                        else
                        {
                            _718 = false;
                        }
                        if (_718)
                        {
                            _718 = _720 > 0.0;
                        }
                        else
                        {
                            _718 = false;
                        }
                        _1161 = _725;
                        _1140 = _725;
                    }
                }
            }
            imageStore(uTransmittance, ivec2(gl_GlobalInvocationID.xy), vec4(sqrt(exp(-_1140)), 1.0));
        }
    }
}

