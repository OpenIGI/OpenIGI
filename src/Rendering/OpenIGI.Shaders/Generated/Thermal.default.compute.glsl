#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform ThermalParams_std140
{
    vec4 size;
    vec4 depth;
    vec4 unproject;
    vec4 sensor;
    vec4 tuning;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uTarget;
layout(binding = 0) uniform sampler2D _2116;
layout(binding = 1) uniform sampler2D _2119;
layout(binding = 2) uniform sampler2D _2122;

void main()
{
    {
        if (!(any(greaterThanEqual(gl_GlobalInvocationID.xy, uvec2(p.size.xy)))))
        {
            ivec2 _62 = ivec2(gl_GlobalInvocationID.xy);
            vec2 _63 = vec2(_62);
            vec2 uv = (_63 + vec2(0.5)) * p.size.zw;
            vec3 _943 = texelFetch(_2116, ivec3(clamp(_62, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz;
            float _101 = clamp(p.sensor.x, 0.0, 1.0);
            if (_101 <= 0.0)
            {
                imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), vec4(_943, 1.0));
            }
            else
            {
                ivec2 _116 = _62 + ivec2(-1);
                vec4 _987 = texelFetch(_2119, ivec3(clamp(ivec2((vec2(_116) + vec2(0.5)) * p.depth.zw), ivec2(0), ivec2(p.depth.xy) - ivec2(1)), 0).xy, 0);
                float _988 = _987.x;
                float _989;
                if (_988 > 0.0)
                {
                    _989 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _988)) * (p.unproject.w - p.unproject.z)));
                }
                else
                {
                    _989 = p.unproject.w;
                }
                float _2031 = _989;
                ivec2 _212 = _62 + ivec2(0, -1);
                vec4 _1059 = texelFetch(_2119, ivec3(clamp(ivec2((vec2(_212) + vec2(0.5)) * p.depth.zw), ivec2(0), ivec2(p.depth.xy) - ivec2(1)), 0).xy, 0);
                float _1060 = _1059.x;
                float _1061;
                if (_1060 > 0.0)
                {
                    _1061 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1060)) * (p.unproject.w - p.unproject.z)));
                }
                else
                {
                    _1061 = p.unproject.w;
                }
                float _2032 = _1061;
                ivec2 _221 = _62 + ivec2(1, -1);
                vec4 _1131 = texelFetch(_2119, ivec3(clamp(ivec2((vec2(_221) + vec2(0.5)) * p.depth.zw), ivec2(0), ivec2(p.depth.xy) - ivec2(1)), 0).xy, 0);
                float _1132 = _1131.x;
                float _1133;
                if (_1132 > 0.0)
                {
                    _1133 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1132)) * (p.unproject.w - p.unproject.z)));
                }
                else
                {
                    _1133 = p.unproject.w;
                }
                float _2033 = _1133;
                ivec2 _230 = _62 + ivec2(-1, 0);
                vec4 _1203 = texelFetch(_2119, ivec3(clamp(ivec2((vec2(_230) + vec2(0.5)) * p.depth.zw), ivec2(0), ivec2(p.depth.xy) - ivec2(1)), 0).xy, 0);
                float _1204 = _1203.x;
                float _1205;
                if (_1204 > 0.0)
                {
                    _1205 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1204)) * (p.unproject.w - p.unproject.z)));
                }
                else
                {
                    _1205 = p.unproject.w;
                }
                float _2034 = _1205;
                float _1240 = dot(max(texelFetch(_2116, ivec3(clamp(_62, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)), vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * p.tuning.x;
                float _2026 = _1240;
                vec4 _1275 = texelFetch(_2119, ivec3(clamp(ivec2((vec2(_62) + vec2(0.5)) * p.depth.zw), ivec2(0), ivec2(p.depth.xy) - ivec2(1)), 0).xy, 0);
                float _1276 = _1275.x;
                float _1277;
                if (_1276 > 0.0)
                {
                    _1277 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1276)) * (p.unproject.w - p.unproject.z)));
                }
                else
                {
                    _1277 = p.unproject.w;
                }
                ivec2 _247 = _62 + ivec2(1, 0);
                vec4 _1347 = texelFetch(_2119, ivec3(clamp(ivec2((vec2(_247) + vec2(0.5)) * p.depth.zw), ivec2(0), ivec2(p.depth.xy) - ivec2(1)), 0).xy, 0);
                float _1348 = _1347.x;
                float _1349;
                if (_1348 > 0.0)
                {
                    _1349 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1348)) * (p.unproject.w - p.unproject.z)));
                }
                else
                {
                    _1349 = p.unproject.w;
                }
                float _2036 = _1349;
                ivec2 _257 = _62 + ivec2(-1, 1);
                vec4 _1419 = texelFetch(_2119, ivec3(clamp(ivec2((vec2(_257) + vec2(0.5)) * p.depth.zw), ivec2(0), ivec2(p.depth.xy) - ivec2(1)), 0).xy, 0);
                float _1420 = _1419.x;
                float _1421;
                if (_1420 > 0.0)
                {
                    _1421 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1420)) * (p.unproject.w - p.unproject.z)));
                }
                else
                {
                    _1421 = p.unproject.w;
                }
                float _2037 = _1421;
                ivec2 _267 = _62 + ivec2(0, 1);
                vec4 _1491 = texelFetch(_2119, ivec3(clamp(ivec2((vec2(_267) + vec2(0.5)) * p.depth.zw), ivec2(0), ivec2(p.depth.xy) - ivec2(1)), 0).xy, 0);
                float _1492 = _1491.x;
                float _1493;
                if (_1492 > 0.0)
                {
                    _1493 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1492)) * (p.unproject.w - p.unproject.z)));
                }
                else
                {
                    _1493 = p.unproject.w;
                }
                float _2038 = _1493;
                ivec2 _277 = _62 + ivec2(1);
                vec4 _1563 = texelFetch(_2119, ivec3(clamp(ivec2((vec2(_277) + vec2(0.5)) * p.depth.zw), ivec2(0), ivec2(p.depth.xy) - ivec2(1)), 0).xy, 0);
                float _1564 = _1563.x;
                float _1565;
                if (_1564 > 0.0)
                {
                    _1565 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _1564)) * (p.unproject.w - p.unproject.z)));
                }
                else
                {
                    _1565 = p.unproject.w;
                }
                float _2039 = _1565;
                float _287 = (((dot(max(texelFetch(_2116, ivec3(clamp(_212, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)), vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * p.tuning.x) + (dot(max(texelFetch(_2116, ivec3(clamp(_230, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)), vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * p.tuning.x)) + (dot(max(texelFetch(_2116, ivec3(clamp(_247, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)), vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * p.tuning.x)) + (dot(max(texelFetch(_2116, ivec3(clamp(_267, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)), vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * p.tuning.x);
                float _292 = (((dot(max(texelFetch(_2116, ivec3(clamp(_116, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)), vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * p.tuning.x) + (dot(max(texelFetch(_2116, ivec3(clamp(_221, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)), vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * p.tuning.x)) + (dot(max(texelFetch(_2116, ivec3(clamp(_257, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)), vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * p.tuning.x)) + (dot(max(texelFetch(_2116, ivec3(clamp(_277, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz, vec3(0.0)), vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * p.tuning.x);
                float signal = ((_1240 * 0.5) + (_287 * 0.09375)) + (_292 * 0.03125);
                bool sky = _1276 <= 0.0;
                float t;
                if (sky)
                {
                    t = mix(0.02999999932944774627685546875, 0.17000000178813934326171875, smoothstep(0.0500000007450580596923828125, 0.62000000476837158203125, uv.y));
                }
                else
                {
                    t = mix(mix(0.1599999964237213134765625, 0.5, clamp(log2(1.0 + (max(signal, 0.0) * 4.0)) * 0.43067657947540283203125, 0.0, 1.0)), 0.9700000286102294921875, smoothstep(2.5, 9.0, signal));
                }
                if (!sky)
                {
                    float local = ((_287 * 0.25) + (_292 * 0.25)) * 0.5;
                    float gx = ((log2(_2033 + 1.0) + (2.0 * log2(_2036 + 1.0))) + log2(_2039 + 1.0)) - ((log2(_2031 + 1.0) + (2.0 * log2(_2034 + 1.0))) + log2(_2037 + 1.0));
                    float gy = ((log2(_2037 + 1.0) + (2.0 * log2(_2038 + 1.0))) + log2(_2039 + 1.0)) - ((log2(_2031 + 1.0) + (2.0 * log2(_2032 + 1.0))) + log2(_2033 + 1.0));
                    t = mix(t + (clamp((mix(mix(0.1599999964237213134765625, 0.5, clamp(log2(1.0 + (max(_2026, 0.0) * 4.0)) * 0.43067657947540283203125, 0.0, 1.0)), 0.9700000286102294921875, smoothstep(2.5, 9.0, _2026)) - mix(mix(0.1599999964237213134765625, 0.5, clamp(log2(1.0 + (max(local, 0.0) * 4.0)) * 0.43067657947540283203125, 0.0, 1.0)), 0.9700000286102294921875, smoothstep(2.5, 9.0, local))) * 0.89999997615814208984375, -0.07999999821186065673828125, 0.07999999821186065673828125) + (clamp(sqrt((gx * gx) + (gy * gy)) * 0.3499999940395355224609375, 0.0, 1.0) * 0.0350000001490116119384765625)), 0.23999999463558197021484375, (1.0 - exp(_1277 * (-0.0011111111380159854888916015625))) * 0.64999997615814208984375);
                }
                vec4 _1641 = texelFetch(_2122, ivec3(clamp(_62, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0);
                float halo = ((((texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(3.0, 0.0))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y + (texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(7.0, 0.0))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y * 0.60000002384185791015625)) + texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(2.121320247650146484375))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y) + (texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(4.949747562408447265625))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y * 0.60000002384185791015625)) + texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(-1.3113415775478642899543046951294e-07, 3.0))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y) + (texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(-3.0597971090173814445734024047852e-07, 7.0))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y * 0.60000002384185791015625);
                float halo_1 = (((((halo + texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(-2.121320247650146484375, 2.121320247650146484375))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y) + (texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(-4.949747562408447265625, 4.949747562408447265625))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y * 0.60000002384185791015625)) + texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(-3.0, -2.6226831550957285799086093902588e-07))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y) + (texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(-7.0, -6.1195942180347628891468048095703e-07))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y * 0.60000002384185791015625)) + texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(-2.1213200092315673828125, -2.1213207244873046875))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y) + (texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(-4.949746608734130859375, -4.94974803924560546875))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y * 0.60000002384185791015625);
                if (_1641.w > 0.0)
                {
                    float body = (1.0 / max(_1641.z, 9.9999997473787516355514526367188e-05)) - 1.0;
                    float _14;
                    if (sky)
                    {
                        _14 = p.unproject.w;
                    }
                    else
                    {
                        _14 = _1277;
                    }
                    float gap = body - _14;
                    float tolerance = 0.20000000298023223876953125 + (body * 0.00999999977648258209228515625);
                    if (gap <= tolerance)
                    {
                        t = max(t, 0.5 + (0.5 * _1641.x));
                    }
                    else
                    {
                        float _628 = _1641.y;
                        t = mix(t, 0.4199999868869781494140625 + (0.36000001430511474609375 * _628), (_628 * (1.0 - smoothstep(0.0, p.tuning.z, gap - tolerance))) * 0.60000002384185791015625);
                    }
                }
                else
                {
                    t += (((((halo_1 + texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(3.5774640139152324991300702095032e-08, -3.0))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y) + (texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(8.3474162693164544180035591125488e-08, -7.0))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y * 0.60000002384185791015625)) + texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(2.1213209629058837890625, -2.1213195323944091796875))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y) + (texelFetch(_2122, ivec3(clamp(_62 + ivec2(round(vec2(4.949748992919921875, -4.949745655059814453125))), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).y * 0.60000002384185791015625)) * 0.009374999441206455230712890625);
                }
                float _661 = length((uv - vec2(0.5)) * vec2(p.size.x * p.size.w, 1.0));
                float _671 = clamp(p.sensor.w, 0.0, 1.0);
                vec3 _1871 = fract((_63 + vec2(p.tuning.w * 7.13000011444091796875, p.tuning.w * 3.71000003814697265625)).xyx * 0.103100001811981201171875);
                vec3 _1877 = _1871 + vec3(dot(_1871, _1871.yzx + vec3(33.3300018310546875)));
                float grain = fract((_1877.x + _1877.y) * _1877.z) - 0.5;
                vec3 _1888 = fract(vec2(float(_62.x), 17.0).xyx * 0.103100001811981201171875);
                vec3 _1894 = _1888 + vec3(dot(_1888, _1888.yzx + vec3(33.3300018310546875)));
                vec3 _1905 = fract(vec2(41.0, float(_62.y)).xyx * 0.103100001811981201171875);
                vec3 _1911 = _1905 + vec3(dot(_1905, _1905.yzx + vec3(33.3300018310546875)));
                float _743 = mix((t + (0.017999999225139617919921875 * (1.0 - smoothstep(0.0, 0.319999992847442626953125, _661)))) + ((((grain * 0.02999999932944774627685546875) * (p.tuning.y * (1.0 + (_671 * 4.0)))) + (((fract((_1894.x + _1894.y) * _1894.z) - 0.5) * 0.01200000010430812835693359375) * p.tuning.y)) + (((fract((_1911.x + _1911.y) * _1911.z) - 0.5) * 0.004999999888241291046142578125) * p.tuning.y)), 0.3400000035762786865234375 + (grain * 0.0199999995529651641845703125), smoothstep(0.62000000476837158203125, 0.800000011920928955078125, _671));
                int _747 = int(p.sensor.y + 0.5);
                vec3 _1919;
                {
                    if (_747 == 1)
                    {
                        _1919 = vec3(pow(1.0 - clamp(_743, 0.0, 1.0), 0.89999997615814208984375)) * vec3(0.9700000286102294921875, 0.9900000095367431640625, 1.0);
                    }
                    else
                    {
                        if (_747 == 2)
                        {
                            float _1948 = clamp(_743, 0.0, 1.0) * 7.0;
                            _1919 = mix(mix(mix(mix(mix(mix(mix(vec3(0.0, 0.0, 0.039999999105930328369140625), vec3(0.119999997317790985107421875, 0.00999999977648258209228515625, 0.300000011920928955078125), vec3(clamp(_1948, 0.0, 1.0))), vec3(0.4000000059604644775390625, 0.0199999995529651641845703125, 0.519999980926513671875), vec3(clamp(_1948 - 1.0, 0.0, 1.0))), vec3(0.7200000286102294921875, 0.07999999821186065673828125, 0.4000000059604644775390625), vec3(clamp(_1948 - 2.0, 0.0, 1.0))), vec3(0.89999997615814208984375, 0.300000011920928955078125, 0.100000001490116119384765625), vec3(clamp(_1948 - 3.0, 0.0, 1.0))), vec3(0.980000019073486328125, 0.579999983310699462890625, 0.0199999995529651641845703125), vec3(clamp(_1948 - 4.0, 0.0, 1.0))), vec3(1.0, 0.839999973773956298828125, 0.300000011920928955078125), vec3(clamp(_1948 - 5.0, 0.0, 1.0))), vec3(1.0, 0.9900000095367431640625, 0.86000001430511474609375), vec3(clamp(_1948 - 6.0, 0.0, 1.0)));
                        }
                        else
                        {
                            if (_747 == 3)
                            {
                                float _1979 = clamp(_743, 0.0, 1.0) * 7.0;
                                _1919 = mix(mix(mix(mix(mix(mix(mix(vec3(0.0, 0.0, 0.100000001490116119384765625), vec3(0.0500000007450580596923828125, 0.07999999821186065673828125, 0.550000011920928955078125), vec3(clamp(_1979, 0.0, 1.0))), vec3(0.0, 0.449999988079071044921875, 0.85000002384185791015625), vec3(clamp(_1979 - 1.0, 0.0, 1.0))), vec3(0.0, 0.75, 0.449999988079071044921875), vec3(clamp(_1979 - 2.0, 0.0, 1.0))), vec3(0.550000011920928955078125, 0.87999999523162841796875, 0.0500000007450580596923828125), vec3(clamp(_1979 - 3.0, 0.0, 1.0))), vec3(1.0, 0.699999988079071044921875, 0.0), vec3(clamp(_1979 - 4.0, 0.0, 1.0))), vec3(0.949999988079071044921875, 0.1500000059604644775390625, 0.0500000007450580596923828125), vec3(clamp(_1979 - 5.0, 0.0, 1.0))), vec3(1.0, 0.949999988079071044921875, 0.920000016689300537109375), vec3(clamp(_1979 - 6.0, 0.0, 1.0)));
                            }
                            else
                            {
                                _1919 = mix(vec3(0.014999999664723873138427734375, 0.0199999995529651641845703125, 0.02999999932944774627685546875), vec3(1.0, 1.0, 0.98500001430511474609375), vec3(pow(clamp(_743, 0.0, 1.0), 0.89999997615814208984375)));
                            }
                        }
                    }
                }
                vec3 _2009 = clamp(_1919 * (1.0 - (0.4000000059604644775390625 * smoothstep(0.4199999868869781494140625, 0.949999988079071044921875, _661))), vec3(0.0), vec3(1.0));
                imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), vec4(mix(_943, (mix(pow(max((_2009 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _2009 * vec3(0.077399380505084991455078125), step(_2009, vec3(0.040449999272823333740234375))) * 1.2999999523162841796875) / vec3(max(p.tuning.x, 0.001000000047497451305389404296875)), vec3(_101)), 1.0));
            }
        }
    }
}

