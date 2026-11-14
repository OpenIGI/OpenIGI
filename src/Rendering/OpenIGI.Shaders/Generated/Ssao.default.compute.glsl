#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

float _1282;

layout(binding = 0, std140) uniform Params_std140
{
    layout(row_major) mat4 viewToWorld;
    layout(row_major) mat4 prevWorldToView;
    vec4 resolution;
    vec4 fullResolution;
    vec4 unproject;
    vec4 params;
    vec4 cameraDelta;
    vec4 temporal;
} p;

layout(binding = 0, r8) uniform restrict writeonly image2D uOcclusion;
layout(binding = 0) uniform sampler2D _1287;
layout(binding = 3) uniform sampler2D _1290;
layout(binding = 2) uniform sampler2D _1292;
layout(binding = 1) uniform sampler2D _1294;

shared float gFresh[8][8];

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    {
        ivec2 _101 = ivec2(gl_GlobalInvocationID.xy);
        float ao;
        vec3 position;
        vec3 normal;
        bool _843;
        {
            ao = 1.0;
            position = vec3(0.0);
            normal = vec3(0.0, 0.0, 1.0);
            int _851 = _101.x;
            bool _844;
            if (_851 >= int(p.resolution.x))
            {
                _844 = true;
            }
            else
            {
                _844 = _101.y >= int(p.resolution.y);
            }
            if (_844)
            {
                _843 = false;
            }
            else
            {
                vec2 _868 = vec2(_101);
                vec2 _875 = ((_868 * 2.0) + vec2(0.5)) * p.fullResolution.zw;
                vec4 _1048 = textureLod(_1287, _875, 0.0);
                float _1049 = _1048.x;
                if (_1049 <= 0.0)
                {
                    _843 = false;
                }
                else
                {
                    float _1078 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1049)) * (p.unproject.w - p.unproject.z)));
                    vec2 _1055 = (_875 * 2.0) - vec2(1.0);
                    vec3 _1065 = vec3((_1055.x * _1078) / p.unproject.x, ((-_1055.y) * _1078) / p.unproject.y, _1078);
                    position = _1065;
                    vec2 _887 = _875 + vec2(p.fullResolution.z, 0.0);
                    float _1120 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * textureLod(_1287, _887, 0.0).x)) * (p.unproject.w - p.unproject.z)));
                    vec2 _1097 = (_887 * 2.0) - vec2(1.0);
                    vec2 _893 = _875 + vec2(0.0, p.fullResolution.w);
                    float _1162 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * textureLod(_1287, _893, 0.0).x)) * (p.unproject.w - p.unproject.z)));
                    vec2 _1139 = (_893 * 2.0) - vec2(1.0);
                    vec3 _897 = normalize(cross(vec3((_1097.x * _1120) / p.unproject.x, ((-_1097.y) * _1120) / p.unproject.y, _1120) - _1065, vec3((_1139.x * _1162) / p.unproject.x, ((-_1139.y) * _1162) / p.unproject.y, _1162) - _1065));
                    normal = _897;
                    if (dot(_897, _1065) > 0.0)
                    {
                        normal = -normal;
                    }
                    float _913 = fract(sin(dot(_868 + vec2(p.cameraDelta.w * 17.0, p.cameraDelta.w * 41.0), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875) * 6.283185482025146484375;
                    vec3 _916 = vec3(cos(_913), sin(_913), 0.0);
                    vec3 _922 = normalize(_916 - (normal * dot(_916, normal)));
                    vec3 _924 = cross(normal, _922);
                    float _943 = (1.5 * ((position.z * 2.0) / max(p.unproject.x * p.resolution.x, 0.001000000047497451305389404296875))) + ((p.params.z * 0.100000001490116119384765625) * p.params.x);
                    int _845 = 0;
                    float _846 = 0.0;
                    float _847;
                    for (;;)
                    {
                        bool _946_ladder_break = false;
                        {
                            if (!(_845 < 16))
                            {
                                _946_ladder_break = true;
                            }
                            else
                            {
                                float _953 = float(_845);
                                int _958 = _101.y;
                                float _1180 = fract(sin(dot(vec2(((_953 * 1.7000000476837158203125) + 0.5) + (p.cameraDelta.w * 0.3819999992847442626953125), float((_958 * 3) + _851)), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875);
                                float _972 = 6.283185482025146484375 * fract(sin(dot(vec2((_953 + 0.12999999523162841796875) + (p.cameraDelta.w * 0.617999970912933349609375), float((_851 * 3) + _958)), vec2(12.98980045318603515625, 78.233001708984375))) * 43758.546875);
                                float _973 = sqrt(_1180);
                                float _981 = _953 + 0.5;
                                vec3 _994 = position + (((((_922 * (_973 * cos(_972))) + (_924 * (_973 * sin(_972)))) + (normal * sqrt(max(1.0 - _1180, 0.0)))) * p.params.x) * mix(0.0199999995529651641845703125, 1.0, 0.00390625 * (_981 * _981)));
                                float _1189 = 2.0 * _994.z;
                                vec2 _1198 = vec2(0.5 + ((_994.x * p.unproject.x) / _1189), 0.5 - ((_994.y * p.unproject.y) / _1189));
                                if (any(lessThan(_1198, vec2(0.0))))
                                {
                                    _844 = true;
                                }
                                else
                                {
                                    _844 = any(greaterThan(_1198, vec2(1.0)));
                                }
                                if (!(_844))
                                {
                                    float _1240 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * textureLod(_1287, _1198, 0.0).x)) * (p.unproject.w - p.unproject.z)));
                                    vec2 _1217 = (_1198 * 2.0) - vec2(1.0);
                                    vec3 _1227 = vec3(_1282, _1282, _1240);
                                    float _1007 = _1227.z;
                                    if (_1007 <= (_994.z - _943))
                                    {
                                        _847 = 1.0;
                                    }
                                    else
                                    {
                                        _847 = 0.0;
                                    }
                                    _846 += (_847 * smoothstep(0.0, 1.0, p.params.x / max(abs(position.z - _1007), 9.9999997473787516355514526367188e-05)));
                                }
                            }
                        }
                        if (_946_ladder_break)
                        {
                            break;
                        }
                        _845++;
                        continue;
                    }
                    ao = pow(clamp(1.0 - ((_846 * 0.0625) * p.params.y), 0.0, 1.0), max(p.params.w, 0.00999999977648258209228515625));
                    _843 = true;
                }
            }
        }
        bool _1043 = _843;
        gFresh[gl_LocalInvocationID.y][gl_LocalInvocationID.x] = ao;
        barrier();
        bool ownMotion;
        if (_101.x >= int(p.resolution.x))
        {
            ownMotion = true;
        }
        else
        {
            ownMotion = _101.y >= int(p.resolution.y);
        }
        if (!(ownMotion))
        {
            if (!_1043)
            {
                imageStore(uOcclusion, ivec2(gl_GlobalInvocationID.xy), vec4(1.0));
            }
            else
            {
                float freshMin = ao;
                float freshMax = ao;
                int ny = -1;
                float freshMin_2;
                float freshMax_2;
                for (;;)
                {
                    if (!(ny <= 1))
                    {
                        break;
                    }
                    float freshMin_1 = freshMin;
                    float freshMax_1 = freshMax;
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
                            ownMotion = all(lessThan(n, ivec2(8)));
                        }
                        else
                        {
                            ownMotion = false;
                        }
                        if (ownMotion)
                        {
                            int _570 = n.y;
                            int _572 = n.x;
                            freshMin_2 = min(freshMin_1, gFresh[_570][_572]);
                            freshMax_2 = max(freshMax_1, gFresh[_570][_572]);
                        }
                        else
                        {
                            freshMin_2 = freshMin_1;
                            freshMax_2 = freshMax_1;
                        }
                        freshMin_1 = freshMin_2;
                        freshMax_1 = freshMax_2;
                        nx++;
                        continue;
                    }
                    freshMin = freshMin_1;
                    freshMax = freshMax_1;
                    ny++;
                    continue;
                }
                if (p.temporal.x > 0.5)
                {
                    vec4 _629 = vec4((vec4(position, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)).xyz + p.cameraDelta.xyz, 0.0) * spvWorkaroundRowMajor(p.prevWorldToView);
                    float _631 = _629.z;
                    if (_631 > p.unproject.z)
                    {
                        vec2 prevUv_1 = (vec2((_629.x * p.unproject.x) / _631, ((-_629.y) * p.unproject.y) / _631) * 0.5) + vec2(0.5);
                        vec2 prevUv;
                        if (p.temporal.y > 0.5)
                        {
                            vec2 fullUv = ((vec2(_101) * 2.0) + vec2(0.5)) * p.fullResolution.zw;
                            vec2 motion = texelFetch(_1290, ivec3(_101 * ivec2(2), 0).xy, 0).xy;
                            vec2 apart = (motion - (fullUv - prevUv_1)) * p.fullResolution.xy;
                            bool _682 = dot(apart, apart) > 2.25;
                            if (_682)
                            {
                                prevUv = fullUv - motion;
                            }
                            else
                            {
                                prevUv = prevUv_1;
                            }
                            ownMotion = _682;
                        }
                        else
                        {
                            ownMotion = false;
                            prevUv = prevUv_1;
                        }
                        bool _28;
                        if (ownMotion)
                        {
                            _28 = all(greaterThanEqual(prevUv, vec2(0.0)));
                        }
                        else
                        {
                            _28 = false;
                        }
                        if (_28)
                        {
                            _28 = all(lessThanEqual(prevUv, vec2(1.0)));
                        }
                        else
                        {
                            _28 = false;
                        }
                        if (_28)
                        {
                            ao = mix(clamp(textureLod(_1294, prevUv, 0.0).x, freshMin, freshMax), ao, 0.125);
                        }
                        else
                        {
                            if (!ownMotion)
                            {
                                ownMotion = all(greaterThanEqual(prevUv, vec2(0.0)));
                            }
                            else
                            {
                                ownMotion = false;
                            }
                            if (ownMotion)
                            {
                                ownMotion = all(lessThanEqual(prevUv, vec2(1.0)));
                            }
                            else
                            {
                                ownMotion = false;
                            }
                            if (ownMotion)
                            {
                                vec4 _1245 = textureLod(_1292, prevUv, 0.0);
                                float _1247 = _1245.x;
                                if (_1247 > 0.0)
                                {
                                    float _1276 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1247)) * (p.unproject.w - p.unproject.z)));
                                    vec2 _1253 = (prevUv * 2.0) - vec2(1.0);
                                    if (abs(dot(vec3((_1253.x * _1276) / p.unproject.x, ((-_1253.y) * _1276) / p.unproject.y, _1276) - _629.xyz, normalize(((vec4(normal, 0.0) * spvWorkaroundRowMajor(p.viewToWorld)) * spvWorkaroundRowMajor(p.prevWorldToView)).xyz))) < max(0.0500000007450580596923828125, 8.0 * ((2.0 * position.z) / (p.unproject.y * p.fullResolution.y))))
                                    {
                                        ao = mix(clamp(textureLod(_1294, prevUv, 0.0).x, freshMin, freshMax), ao, 0.125);
                                    }
                                }
                            }
                        }
                    }
                }
                imageStore(uOcclusion, ivec2(gl_GlobalInvocationID.xy), vec4(ao));
            }
        }
    }
}

