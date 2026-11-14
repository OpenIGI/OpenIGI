#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
    vec4 unproject;
    vec4 control;
} p;

layout(binding = 0, rg8) uniform restrict writeonly image2D uRelaxed;
layout(binding = 0) uniform sampler2D _1336;
layout(binding = 1) uniform sampler2D _1339;
layout(binding = 2) uniform sampler2D _1341;

shared float sLine[100];

void main()
{
    {
        ivec2 _45 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 _56 = ivec2(p.size.xy);
        ivec2 _64 = (ivec2(gl_WorkGroupID.xy) * ivec2(8)) - ivec2(1);
        uint i = gl_LocalInvocationIndex;
        float _712;
        float _714;
        float _715;
        int _716;
        float _987;
        float _1024;
        float _1061;
        float _1098;
        float _1135;
        float _1172;
        float _1209;
        float _1246;
        float _1283;
        for (;;)
        {
            if (!(i < 100u))
            {
                break;
            }
            int _76 = int(i);
            ivec2 _88 = _64 + ivec2(_76 - 10 * (_76 / 10), _76 / 10);
            {
                if (texelFetch(_1336, ivec3(clamp(_88, ivec2(0), _56 - ivec2(1)), 0).xy, 0).x <= 0.0)
                {
                    _712 = 0.0;
                }
                else
                {
                    vec4 _996 = texelFetch(_1336, ivec3(clamp(_88 + ivec2(-1), ivec2(0), _56 - ivec2(1)), 0).xy, 0);
                    float _998 = _996.x;
                    if (_998 <= 0.0)
                    {
                        _987 = p.unproject.w;
                    }
                    else
                    {
                        _987 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _998)) * (p.unproject.w - p.unproject.z)));
                    }
                    float _1320 = _987;
                    vec4 _1033 = texelFetch(_1336, ivec3(clamp(_88 + ivec2(0, -1), ivec2(0), _56 - ivec2(1)), 0).xy, 0);
                    float _1035 = _1033.x;
                    if (_1035 <= 0.0)
                    {
                        _1024 = p.unproject.w;
                    }
                    else
                    {
                        _1024 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1035)) * (p.unproject.w - p.unproject.z)));
                    }
                    float _1321 = _1024;
                    vec4 _1070 = texelFetch(_1336, ivec3(clamp(_88 + ivec2(1, -1), ivec2(0), _56 - ivec2(1)), 0).xy, 0);
                    float _1072 = _1070.x;
                    if (_1072 <= 0.0)
                    {
                        _1061 = p.unproject.w;
                    }
                    else
                    {
                        _1061 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1072)) * (p.unproject.w - p.unproject.z)));
                    }
                    float _1322 = _1061;
                    vec4 _1107 = texelFetch(_1336, ivec3(clamp(_88 + ivec2(-1, 0), ivec2(0), _56 - ivec2(1)), 0).xy, 0);
                    float _1109 = _1107.x;
                    if (_1109 <= 0.0)
                    {
                        _1098 = p.unproject.w;
                    }
                    else
                    {
                        _1098 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1109)) * (p.unproject.w - p.unproject.z)));
                    }
                    float _1323 = _1098;
                    vec4 _1144 = texelFetch(_1336, ivec3(clamp(_88, ivec2(0), _56 - ivec2(1)), 0).xy, 0);
                    float _1146 = _1144.x;
                    if (_1146 <= 0.0)
                    {
                        _1135 = p.unproject.w;
                    }
                    else
                    {
                        _1135 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1146)) * (p.unproject.w - p.unproject.z)));
                    }
                    float _1324 = _1135;
                    vec4 _1181 = texelFetch(_1336, ivec3(clamp(_88 + ivec2(1, 0), ivec2(0), _56 - ivec2(1)), 0).xy, 0);
                    float _1183 = _1181.x;
                    if (_1183 <= 0.0)
                    {
                        _1172 = p.unproject.w;
                    }
                    else
                    {
                        _1172 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1183)) * (p.unproject.w - p.unproject.z)));
                    }
                    float _1325 = _1172;
                    vec4 _1218 = texelFetch(_1336, ivec3(clamp(_88 + ivec2(-1, 1), ivec2(0), _56 - ivec2(1)), 0).xy, 0);
                    float _1220 = _1218.x;
                    if (_1220 <= 0.0)
                    {
                        _1209 = p.unproject.w;
                    }
                    else
                    {
                        _1209 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1220)) * (p.unproject.w - p.unproject.z)));
                    }
                    float _1326 = _1209;
                    vec4 _1255 = texelFetch(_1336, ivec3(clamp(_88 + ivec2(0, 1), ivec2(0), _56 - ivec2(1)), 0).xy, 0);
                    float _1257 = _1255.x;
                    if (_1257 <= 0.0)
                    {
                        _1246 = p.unproject.w;
                    }
                    else
                    {
                        _1246 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1257)) * (p.unproject.w - p.unproject.z)));
                    }
                    float _1327 = _1246;
                    vec4 _1292 = texelFetch(_1336, ivec3(clamp(_88 + ivec2(1), ivec2(0), _56 - ivec2(1)), 0).xy, 0);
                    float _1294 = _1292.x;
                    if (_1294 <= 0.0)
                    {
                        _1283 = p.unproject.w;
                    }
                    else
                    {
                        _1283 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1294)) * (p.unproject.w - p.unproject.z)));
                    }
                    float _1328 = _1283;
                    float _762 = _1135 * p.control.x;
                    if ((_987 - _1098) > _762)
                    {
                        _714 = 1.0;
                    }
                    else
                    {
                        _714 = 0.0;
                    }
                    if ((_1326 - _1323) > _762)
                    {
                        _715 = 1.0;
                    }
                    else
                    {
                        _715 = 0.0;
                    }
                    float _775 = _714;
                    float _776 = _715;
                    if ((_1320 - _1321) > _762)
                    {
                        _714 = 1.0;
                    }
                    else
                    {
                        _714 = 0.0;
                    }
                    if ((_1322 - _1321) > _762)
                    {
                        _715 = 1.0;
                    }
                    else
                    {
                        _715 = 0.0;
                    }
                    float _792 = _714;
                    float _793 = _715;
                    if ((_1321 - _1324) > _762)
                    {
                        _714 = 1.0;
                    }
                    else
                    {
                        _714 = 0.0;
                    }
                    if ((_1327 - _1324) > _762)
                    {
                        _715 = 1.0;
                    }
                    else
                    {
                        _715 = 0.0;
                    }
                    float _810 = _714;
                    float _811 = _715;
                    if ((_1323 - _1324) > _762)
                    {
                        _714 = 1.0;
                    }
                    else
                    {
                        _714 = 0.0;
                    }
                    if ((_1325 - _1324) > _762)
                    {
                        _715 = 1.0;
                    }
                    else
                    {
                        _715 = 0.0;
                    }
                    float _827 = _714;
                    float _828 = _715;
                    if ((_1322 - _1325) > _762)
                    {
                        _714 = 1.0;
                    }
                    else
                    {
                        _714 = 0.0;
                    }
                    if ((_1328 - _1325) > _762)
                    {
                        _715 = 1.0;
                    }
                    else
                    {
                        _715 = 0.0;
                    }
                    float _845 = _714;
                    float _846 = _715;
                    if ((_1326 - _1327) > _762)
                    {
                        _714 = 1.0;
                    }
                    else
                    {
                        _714 = 0.0;
                    }
                    if ((_1328 - _1327) > _762)
                    {
                        _715 = 1.0;
                    }
                    else
                    {
                        _715 = 0.0;
                    }
                    float _862 = _714;
                    float _863 = _715;
                    if ((_810 + _811) < 2.0)
                    {
                        _714 = 0.0;
                    }
                    else
                    {
                        _714 = clamp((((_775 + _776) + (_845 + _846)) - 2.0) * 0.5, 0.0, 1.0);
                    }
                    if ((_827 + _828) < 2.0)
                    {
                        _715 = 0.0;
                    }
                    else
                    {
                        _715 = clamp((((_792 + _793) + (_862 + _863)) - 2.0) * 0.5, 0.0, 1.0);
                    }
                    bool _896 = abs(_1324 - _1320) > _762;
                    if (_896 != (abs(_1324 - _1323) > _762))
                    {
                        _716 = 1;
                    }
                    else
                    {
                        _716 = 0;
                    }
                    bool _906 = abs(_1324 - _1321) > _762;
                    if (_906 != _896)
                    {
                        _716++;
                    }
                    bool _917 = abs(_1324 - _1322) > _762;
                    if (_917 != _906)
                    {
                        _716++;
                    }
                    bool _928 = abs(_1324 - _1325) > _762;
                    if (_928 != _917)
                    {
                        _716++;
                    }
                    bool _939 = abs(_1324 - _1328) > _762;
                    if (_939 != _928)
                    {
                        _716++;
                    }
                    bool _950 = abs(_1324 - _1327) > _762;
                    if (_950 != _939)
                    {
                        _716++;
                    }
                    bool _961 = abs(_1324 - _1326) > _762;
                    if (_961 != _950)
                    {
                        _716++;
                    }
                    if ((abs(_1324 - _1323) > _762) != _961)
                    {
                        _716++;
                    }
                    _712 = max(clamp(_714 + _715, 0.0, 1.0), clamp((float(_716) - 2.0) * 0.5, 0.0, 1.0));
                }
            }
            sLine[i] = _712;
            i += 64u;
            continue;
        }
        barrier();
        bool _10;
        if (_45.x >= _56.x)
        {
            _10 = true;
        }
        else
        {
            _10 = _45.y >= _56.y;
        }
        if (!(_10))
        {
            float believable;
            if (p.control.z > 0.5)
            {
                believable = texelFetch(_1339, ivec3(_45, 0).xy, 0).x;
            }
            else
            {
                believable = 1.0;
            }
            ivec2 _610 = ivec2(gl_LocalInvocationID.xy) + ivec2(1);
            int _612 = _610.y;
            int _615 = _610.x;
            int _616 = ((_612 + (-1)) * 10) + _615;
            int _629 = (_612 * 10) + _615;
            int _643 = ((_612 + 1) * 10) + _615;
            float _654 = max(max(max(max(max(max(max(max(max(0.0, sLine[_616 + (-1)]), sLine[_616]), sLine[_616 + 1]), sLine[_629 + (-1)]), sLine[_629]), sLine[_629 + 1]), sLine[_643 + (-1)]), sLine[_643]), sLine[_643 + 1]);
            float speedPx;
            if (p.control.w > 0.5)
            {
                speedPx = length(textureLod(_1341, (vec2(_45) + vec2(0.5)) * p.size.zw, 0.0).xy * p.size.xy);
            }
            else
            {
                speedPx = 0.0;
            }
            imageStore(uRelaxed, ivec2(gl_GlobalInvocationID.xy), vec4(mix(believable, 1.0, (_654 * p.control.y) * clamp(1.0 - speedPx, 0.0, 1.0)), _654, 0.0, 0.0));
        }
    }
}

