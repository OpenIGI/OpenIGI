#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
    vec4 control;
} p;

layout(binding = 0, rg16f) uniform restrict writeonly image2D uOffset;
layout(binding = 0) uniform sampler2D _1531;
layout(binding = 1) uniform sampler2D _1533;

void main()
{
    {
        ivec2 _209 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 _220 = ivec2(p.size.xy);
        bool doneP;
        if (_209.x >= _220.x)
        {
            doneP = true;
        }
        else
        {
            doneP = _209.y >= _220.y;
        }
        if (!(doneP))
        {
            float _929 = dot(max(texelFetch(_1531, ivec3(clamp(_209, ivec2(0), _220 - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
            float _931 = _929 / (0.5 + _929);
            float _951 = dot(max(texelFetch(_1531, ivec3(clamp(_209 + ivec2(0, -1), ivec2(0), _220 - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
            float _953 = _951 / (0.5 + _951);
            float _973 = dot(max(texelFetch(_1531, ivec3(clamp(_209 + ivec2(0, 1), ivec2(0), _220 - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
            float _975 = _973 / (0.5 + _973);
            float _995 = dot(max(texelFetch(_1531, ivec3(clamp(_209 + ivec2(1, 0), ivec2(0), _220 - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
            float _997 = _995 / (0.5 + _995);
            float _1017 = dot(max(texelFetch(_1531, ivec3(clamp(_209 + ivec2(-1, 0), ivec2(0), _220 - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
            float _1019 = _1017 / (0.5 + _1017);
            float _1039 = dot(max(texelFetch(_1531, ivec3(clamp(_209 + ivec2(1, -1), ivec2(0), _220 - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
            float _1041 = _1039 / (0.5 + _1039);
            float _1061 = dot(max(texelFetch(_1531, ivec3(clamp(_209 + ivec2(-1), ivec2(0), _220 - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
            float _1063 = _1061 / (0.5 + _1061);
            float _1083 = dot(max(texelFetch(_1531, ivec3(clamp(_209 + ivec2(1), ivec2(0), _220 - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
            float _1085 = _1083 / (0.5 + _1083);
            float _1105 = dot(max(texelFetch(_1531, ivec3(clamp(_209 + ivec2(-1, 1), ivec2(0), _220 - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
            float _1107 = _1105 / (0.5 + _1105);
            if ((max(max(max(_931, _953), max(_975, _997)), max(max(_1019, _1041), max(_1063, max(_1085, _1107)))) - min(min(min(_931, _953), min(_975, _997)), min(min(_1019, _1041), min(_1063, min(_1085, _1107))))) <= 0.0500000007450580596923828125)
            {
                imageStore(uOffset, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
            }
            else
            {
                float edgeL;
                ivec2 side;
                ivec2 walkDir;
                if (((abs(((_1041 + _1063) * 0.5) - _953) + abs(((_997 + _1019) * 0.5) - _931)) + abs(((_1085 + _1107) * 0.5) - _975)) > ((abs(((_1041 + _1085) * 0.5) - _997) + abs(((_953 + _975) * 0.5) - _931)) + abs(((_1063 + _1107) * 0.5) - _1019)))
                {
                    if (abs(_1019 - _931) > abs(_997 - _931))
                    {
                        edgeL = _1019;
                        side = ivec2(-1, 0);
                    }
                    else
                    {
                        edgeL = _997;
                        side = ivec2(1, 0);
                    }
                    walkDir = ivec2(0, 1);
                }
                else
                {
                    if (abs(_953 - _931) > abs(_975 - _931))
                    {
                        edgeL = _953;
                        side = ivec2(0, -1);
                    }
                    else
                    {
                        edgeL = _975;
                        side = ivec2(0, 1);
                    }
                    walkDir = ivec2(1, 0);
                }
                float _394 = abs(edgeL - _931);
                if ((_394 * 0.5) <= 0.0500000007450580596923828125)
                {
                    imageStore(uOffset, ivec2(gl_GlobalInvocationID.xy), vec4(0.0));
                }
                else
                {
                    vec2 _412 = ((vec2(_209) + vec2(0.5)) + (vec2(side) * 0.5)) * p.size.zw;
                    vec2 _417 = vec2(walkDir) * p.size.zw;
                    float _420 = (edgeL + _931) * 0.5;
                    float _421 = _394 * 0.25;
                    bool brighter = edgeL > _931;
                    vec2 _1113 = p.size.zw * 0.5;
                    float _1131 = dot(max(textureLod(_1533, clamp(_412 + _417, _1113, vec2(1.0) - _1113), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                    float _1133 = _1131 / (0.5 + _1131);
                    float _451 = _420 - _421;
                    bool highP;
                    bool lowP;
                    float lenP;
                    if (_1133 < _451)
                    {
                        doneP = true;
                        highP = false;
                        lenP = 0.0;
                        lowP = true;
                    }
                    else
                    {
                        bool _461 = _1133 > (_420 + _421);
                        if (_461)
                        {
                            lenP = 0.0;
                        }
                        else
                        {
                            lenP = 8.0;
                        }
                        doneP = _461;
                        highP = _461;
                        lowP = false;
                    }
                    vec2 _1139 = p.size.zw * 0.5;
                    float _1157 = dot(max(textureLod(_1533, clamp(_412 - _417, _1139, vec2(1.0) - _1139), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                    float _1159 = _1157 / (0.5 + _1157);
                    bool doneN;
                    bool highN;
                    bool lowN;
                    float lenN;
                    if (_1159 < _451)
                    {
                        doneN = true;
                        highN = false;
                        lenN = 0.0;
                        lowN = true;
                    }
                    else
                    {
                        bool _483 = _1159 > (_420 + _421);
                        if (_483)
                        {
                            lenN = 0.0;
                        }
                        else
                        {
                            lenN = 8.0;
                        }
                        doneN = _483;
                        highN = _483;
                        lowN = false;
                    }
                    if (!doneP)
                    {
                        vec2 _1165 = p.size.zw * 0.5;
                        float _1183 = dot(max(textureLod(_1533, clamp(_412 + (_417 * 2.0), _1165, vec2(1.0) - _1165), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1185 = _1183 / (0.5 + _1183);
                        if (_1185 < _451)
                        {
                            doneP = true;
                            lowP = true;
                            lenP = 1.0;
                        }
                        else
                        {
                            if (_1185 > (_420 + _421))
                            {
                                doneP = true;
                                highP = true;
                                lenP = 1.0;
                            }
                        }
                    }
                    if (!doneN)
                    {
                        vec2 _1191 = p.size.zw * 0.5;
                        float _1209 = dot(max(textureLod(_1533, clamp(_412 - (_417 * 2.0), _1191, vec2(1.0) - _1191), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1211 = _1209 / (0.5 + _1209);
                        if (_1211 < _451)
                        {
                            doneN = true;
                            lowN = true;
                            lenN = 1.0;
                        }
                        else
                        {
                            if (_1211 > (_420 + _421))
                            {
                                doneN = true;
                                highN = true;
                                lenN = 1.0;
                            }
                        }
                    }
                    if (!doneP)
                    {
                        vec2 _1217 = p.size.zw * 0.5;
                        float _1235 = dot(max(textureLod(_1533, clamp(_412 + (_417 * 3.0), _1217, vec2(1.0) - _1217), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1237 = _1235 / (0.5 + _1235);
                        if (_1237 < _451)
                        {
                            doneP = true;
                            lowP = true;
                            lenP = 2.0;
                        }
                        else
                        {
                            if (_1237 > (_420 + _421))
                            {
                                doneP = true;
                                highP = true;
                                lenP = 2.0;
                            }
                        }
                    }
                    if (!doneN)
                    {
                        vec2 _1243 = p.size.zw * 0.5;
                        float _1261 = dot(max(textureLod(_1533, clamp(_412 - (_417 * 3.0), _1243, vec2(1.0) - _1243), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1263 = _1261 / (0.5 + _1261);
                        if (_1263 < _451)
                        {
                            doneN = true;
                            lowN = true;
                            lenN = 2.0;
                        }
                        else
                        {
                            if (_1263 > (_420 + _421))
                            {
                                doneN = true;
                                highN = true;
                                lenN = 2.0;
                            }
                        }
                    }
                    if (!doneP)
                    {
                        vec2 _1269 = p.size.zw * 0.5;
                        float _1287 = dot(max(textureLod(_1533, clamp(_412 + (_417 * 4.0), _1269, vec2(1.0) - _1269), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1289 = _1287 / (0.5 + _1287);
                        if (_1289 < _451)
                        {
                            doneP = true;
                            lowP = true;
                            lenP = 3.0;
                        }
                        else
                        {
                            if (_1289 > (_420 + _421))
                            {
                                doneP = true;
                                highP = true;
                                lenP = 3.0;
                            }
                        }
                    }
                    if (!doneN)
                    {
                        vec2 _1295 = p.size.zw * 0.5;
                        float _1313 = dot(max(textureLod(_1533, clamp(_412 - (_417 * 4.0), _1295, vec2(1.0) - _1295), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1315 = _1313 / (0.5 + _1313);
                        if (_1315 < _451)
                        {
                            doneN = true;
                            lowN = true;
                            lenN = 3.0;
                        }
                        else
                        {
                            if (_1315 > (_420 + _421))
                            {
                                doneN = true;
                                highN = true;
                                lenN = 3.0;
                            }
                        }
                    }
                    if (!doneP)
                    {
                        vec2 _1321 = p.size.zw * 0.5;
                        float _1339 = dot(max(textureLod(_1533, clamp(_412 + (_417 * 5.0), _1321, vec2(1.0) - _1321), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1341 = _1339 / (0.5 + _1339);
                        if (_1341 < _451)
                        {
                            doneP = true;
                            lowP = true;
                            lenP = 4.0;
                        }
                        else
                        {
                            if (_1341 > (_420 + _421))
                            {
                                doneP = true;
                                highP = true;
                                lenP = 4.0;
                            }
                        }
                    }
                    if (!doneN)
                    {
                        vec2 _1347 = p.size.zw * 0.5;
                        float _1365 = dot(max(textureLod(_1533, clamp(_412 - (_417 * 5.0), _1347, vec2(1.0) - _1347), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1367 = _1365 / (0.5 + _1365);
                        if (_1367 < _451)
                        {
                            doneN = true;
                            lowN = true;
                            lenN = 4.0;
                        }
                        else
                        {
                            if (_1367 > (_420 + _421))
                            {
                                doneN = true;
                                highN = true;
                                lenN = 4.0;
                            }
                        }
                    }
                    if (!doneP)
                    {
                        vec2 _1373 = p.size.zw * 0.5;
                        float _1391 = dot(max(textureLod(_1533, clamp(_412 + (_417 * 6.0), _1373, vec2(1.0) - _1373), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1393 = _1391 / (0.5 + _1391);
                        if (_1393 < _451)
                        {
                            doneP = true;
                            lowP = true;
                            lenP = 5.0;
                        }
                        else
                        {
                            if (_1393 > (_420 + _421))
                            {
                                doneP = true;
                                highP = true;
                                lenP = 5.0;
                            }
                        }
                    }
                    if (!doneN)
                    {
                        vec2 _1399 = p.size.zw * 0.5;
                        float _1417 = dot(max(textureLod(_1533, clamp(_412 - (_417 * 6.0), _1399, vec2(1.0) - _1399), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1419 = _1417 / (0.5 + _1417);
                        if (_1419 < _451)
                        {
                            doneN = true;
                            lowN = true;
                            lenN = 5.0;
                        }
                        else
                        {
                            if (_1419 > (_420 + _421))
                            {
                                doneN = true;
                                highN = true;
                                lenN = 5.0;
                            }
                        }
                    }
                    if (!doneP)
                    {
                        vec2 _1425 = p.size.zw * 0.5;
                        float _1443 = dot(max(textureLod(_1533, clamp(_412 + (_417 * 7.0), _1425, vec2(1.0) - _1425), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1445 = _1443 / (0.5 + _1443);
                        if (_1445 < _451)
                        {
                            doneP = true;
                            lowP = true;
                            lenP = 6.0;
                        }
                        else
                        {
                            if (_1445 > (_420 + _421))
                            {
                                doneP = true;
                                highP = true;
                                lenP = 6.0;
                            }
                        }
                    }
                    if (!doneN)
                    {
                        vec2 _1451 = p.size.zw * 0.5;
                        float _1469 = dot(max(textureLod(_1533, clamp(_412 - (_417 * 7.0), _1451, vec2(1.0) - _1451), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1471 = _1469 / (0.5 + _1469);
                        if (_1471 < _451)
                        {
                            doneN = true;
                            lowN = true;
                            lenN = 6.0;
                        }
                        else
                        {
                            if (_1471 > (_420 + _421))
                            {
                                doneN = true;
                                highN = true;
                                lenN = 6.0;
                            }
                        }
                    }
                    if (!doneP)
                    {
                        vec2 _1477 = p.size.zw * 0.5;
                        float _1495 = dot(max(textureLod(_1533, clamp(_412 + (_417 * 8.0), _1477, vec2(1.0) - _1477), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1497 = _1495 / (0.5 + _1495);
                        if (_1497 < _451)
                        {
                            doneP = true;
                            lowP = true;
                            lenP = 7.0;
                        }
                        else
                        {
                            if (_1497 > (_420 + _421))
                            {
                                doneP = true;
                                highP = true;
                                lenP = 7.0;
                            }
                        }
                    }
                    if (!doneN)
                    {
                        vec2 _1503 = p.size.zw * 0.5;
                        float _1521 = dot(max(textureLod(_1533, clamp(_412 - (_417 * 8.0), _1503, vec2(1.0) - _1503), 0.0).xyz, vec3(0.0)) * p.control.x, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625));
                        float _1523 = _1521 / (0.5 + _1521);
                        if (_1523 < _451)
                        {
                            doneN = true;
                            lowN = true;
                            lenN = 7.0;
                        }
                        else
                        {
                            if (_1523 > (_420 + _421))
                            {
                                doneN = true;
                                highN = true;
                                lenN = 7.0;
                            }
                        }
                    }
                    bool incP;
                    if (brighter)
                    {
                        incP = highP;
                    }
                    else
                    {
                        incP = lowP;
                    }
                    bool decP;
                    if (brighter)
                    {
                        decP = lowP;
                    }
                    else
                    {
                        decP = highP;
                    }
                    bool incN;
                    if (brighter)
                    {
                        incN = highN;
                    }
                    else
                    {
                        incN = lowN;
                    }
                    bool decN;
                    if (brighter)
                    {
                        decN = lowN;
                    }
                    else
                    {
                        decN = highN;
                    }
                    float inv = 1.0 / ((1.0 + lenP) + lenN);
                    if (!doneP)
                    {
                        doneP = !doneN;
                    }
                    else
                    {
                        doneP = false;
                    }
                    float dist;
                    if (doneP)
                    {
                        dist = 0.0;
                    }
                    else
                    {
                        if (incP)
                        {
                            doneP = incN;
                        }
                        else
                        {
                            doneP = false;
                        }
                        if (doneP)
                        {
                            dist = 0.5;
                        }
                        else
                        {
                            if (decP)
                            {
                                doneP = decN;
                            }
                            else
                            {
                                doneP = false;
                            }
                            if (doneP)
                            {
                                dist = 0.0;
                            }
                            else
                            {
                                if (incN)
                                {
                                    dist = 0.5 - ((lenN + 0.5) * inv);
                                }
                                else
                                {
                                    if (incP)
                                    {
                                        dist = 0.5 - ((lenP + 0.5) * inv);
                                    }
                                    else
                                    {
                                        dist = 0.0;
                                    }
                                }
                            }
                        }
                    }
                    imageStore(uOffset, ivec2(gl_GlobalInvocationID.xy), vec4(float(side.x) * dist, float(side.y) * dist, 0.0, 0.0));
                }
            }
        }
    }
}

