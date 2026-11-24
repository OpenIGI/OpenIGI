#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform GhostParams_std140
{
    vec4 size;
    vec4 depth;
    vec4 unproject;
    vec4 look;
    vec4 tint;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uTarget;
layout(binding = 0) uniform sampler2D _2829;
layout(binding = 1) uniform sampler2D _2832;
layout(binding = 2) uniform sampler2D _2835;

void main()
{
    {
        if (!(any(greaterThanEqual(gl_GlobalInvocationID.xy, uvec2(p.size.xy)))))
        {
            ivec2 _73 = ivec2(gl_GlobalInvocationID.xy);
            vec3 _693 = texelFetch(_2829, ivec3(clamp(_73, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz;
            float _107 = clamp(p.look.z, 0.0, 1.0);
            ivec2 at = _73;
            float noiseLine;
            int split;
            bool dropped;
            if (_107 > 0.0)
            {
                vec3 _698 = fract(vec2(p.look.w, 1.7000000476837158203125).xyx * 0.103100001811981201171875);
                vec3 _704 = _698 + vec3(dot(_698, _698.yzx + vec3(33.3300018310546875)));
                float _146 = float(_73.y);
                float _148 = floor(_146 / (4.0 + floor(fract((_704.x + _704.y) * _704.z) * 28.0)));
                vec3 _715 = fract(vec2(_148, p.look.w).xyx * 0.103100001811981201171875);
                vec3 _721 = _715 + vec3(dot(_715, _715.yzx + vec3(33.3300018310546875)));
                if (fract((_721.x + _721.y) * _721.z) < (_107 * 0.699999988079071044921875))
                {
                    vec3 _732 = fract(vec2(_148, p.look.w + 7.30000019073486328125).xyx * 0.103100001811981201171875);
                    vec3 _738 = _732 + vec3(dot(_732, _732.yzx + vec3(33.3300018310546875)));
                    ivec2 _2821 = at;
                    _2821.x = _2821.x + int((((fract((_738.x + _738.y) * _738.z) - 0.5) * _107) * 0.14000000059604644775390625) * p.size.x);
                    at = _2821;
                }
                vec3 _749 = fract(vec2(p.look.w, 3.099999904632568359375).xyx * 0.103100001811981201171875);
                vec3 _755 = _749 + vec3(dot(_749, _749.yzx + vec3(33.3300018310546875)));
                vec3 _766 = fract((floor(vec2(_73) * vec2(0.0416666679084300994873046875)) + vec2(p.look.w, p.look.w * 0.37000000476837158203125)).xyx * 0.103100001811981201171875);
                vec3 _772 = _766 + vec3(dot(_766, _766.yzx + vec3(33.3300018310546875)));
                vec3 _783 = fract(vec2(_146, p.look.w * 1.309999942779541015625).xyx * 0.103100001811981201171875);
                vec3 _789 = _783 + vec3(dot(_783, _783.yzx + vec3(33.3300018310546875)));
                if (fract((_789.x + _789.y) * _789.z) < (_107 * 0.039999999105930328369140625))
                {
                    noiseLine = 1.0;
                }
                else
                {
                    noiseLine = 0.0;
                }
                split = int(_107 * (3.0 + (fract((_755.x + _755.y) * _755.z) * 12.0)));
                dropped = fract((_772.x + _772.y) * _772.z) < (_107 * 0.2199999988079071044921875);
            }
            else
            {
                split = 0;
                dropped = false;
                noiseLine = 0.0;
            }
            vec4 _816 = texelFetch(_2832, ivec3(clamp(ivec2((vec2(_73) + vec2(0.5)) * p.depth.zw), ivec2(0), ivec2(p.depth.xy) - ivec2(1)), 0).xy, 0);
            float _817 = _816.x;
            float _796;
            if (_817 > 0.0)
            {
                _796 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _817)) * (p.unproject.w - p.unproject.z)));
            }
            else
            {
                _796 = p.unproject.w;
            }
            vec2 _843;
            {
                vec4 _1018;
                {
                    bool _1019;
                    if (any(lessThan(at, ivec2(0))))
                    {
                        _1019 = true;
                    }
                    else
                    {
                        _1019 = any(greaterThanEqual(at, ivec2(p.size.xy)));
                    }
                    if (_1019)
                    {
                        _1018 = vec4(0.0);
                    }
                    else
                    {
                        _1018 = texelFetch(_2835, ivec3(at, 0).xy, 0);
                    }
                }
                ivec2 _855 = at + ivec2(round(vec2(1.5, 0.0)));
                vec4 _1045;
                {
                    bool _1046;
                    if (any(lessThan(_855, ivec2(0))))
                    {
                        _1046 = true;
                    }
                    else
                    {
                        _1046 = any(greaterThanEqual(_855, ivec2(p.size.xy)));
                    }
                    if (_1046)
                    {
                        _1045 = vec4(0.0);
                    }
                    else
                    {
                        _1045 = texelFetch(_2835, ivec3(_855, 0).xy, 0);
                    }
                }
                ivec2 _861 = at + ivec2(round(vec2(4.0, 0.0)));
                vec4 _1072;
                {
                    bool _1073;
                    if (any(lessThan(_861, ivec2(0))))
                    {
                        _1073 = true;
                    }
                    else
                    {
                        _1073 = any(greaterThanEqual(_861, ivec2(p.size.xy)));
                    }
                    if (_1073)
                    {
                        _1072 = vec4(0.0);
                    }
                    else
                    {
                        _1072 = texelFetch(_2835, ivec3(_861, 0).xy, 0);
                    }
                }
                ivec2 _870 = at + ivec2(round(vec2(1.0606601238250732421875)));
                vec4 _1099;
                {
                    bool _1100;
                    if (any(lessThan(_870, ivec2(0))))
                    {
                        _1100 = true;
                    }
                    else
                    {
                        _1100 = any(greaterThanEqual(_870, ivec2(p.size.xy)));
                    }
                    if (_1100)
                    {
                        _1099 = vec4(0.0);
                    }
                    else
                    {
                        _1099 = texelFetch(_2835, ivec3(_870, 0).xy, 0);
                    }
                }
                ivec2 _877 = at + ivec2(round(vec2(2.8284270763397216796875)));
                vec4 _1126;
                {
                    bool _1127;
                    if (any(lessThan(_877, ivec2(0))))
                    {
                        _1127 = true;
                    }
                    else
                    {
                        _1127 = any(greaterThanEqual(_877, ivec2(p.size.xy)));
                    }
                    if (_1127)
                    {
                        _1126 = vec4(0.0);
                    }
                    else
                    {
                        _1126 = texelFetch(_2835, ivec3(_877, 0).xy, 0);
                    }
                }
                ivec2 _887 = at + ivec2(round(vec2(-6.556707887739321449771523475647e-08, 1.5)));
                vec4 _1153;
                {
                    bool _1154;
                    if (any(lessThan(_887, ivec2(0))))
                    {
                        _1154 = true;
                    }
                    else
                    {
                        _1154 = any(greaterThanEqual(_887, ivec2(p.size.xy)));
                    }
                    if (_1154)
                    {
                        _1153 = vec4(0.0);
                    }
                    else
                    {
                        _1153 = texelFetch(_2835, ivec3(_887, 0).xy, 0);
                    }
                }
                ivec2 _894 = at + ivec2(round(vec2(-1.7484555314695171546190977096558e-07, 4.0)));
                vec4 _1180;
                {
                    bool _1181;
                    if (any(lessThan(_894, ivec2(0))))
                    {
                        _1181 = true;
                    }
                    else
                    {
                        _1181 = any(greaterThanEqual(_894, ivec2(p.size.xy)));
                    }
                    if (_1181)
                    {
                        _1180 = vec4(0.0);
                    }
                    else
                    {
                        _1180 = texelFetch(_2835, ivec3(_894, 0).xy, 0);
                    }
                }
                ivec2 _904 = at + ivec2(round(vec2(-1.0606601238250732421875, 1.0606601238250732421875)));
                vec4 _1207;
                {
                    bool _1208;
                    if (any(lessThan(_904, ivec2(0))))
                    {
                        _1208 = true;
                    }
                    else
                    {
                        _1208 = any(greaterThanEqual(_904, ivec2(p.size.xy)));
                    }
                    if (_1208)
                    {
                        _1207 = vec4(0.0);
                    }
                    else
                    {
                        _1207 = texelFetch(_2835, ivec3(_904, 0).xy, 0);
                    }
                }
                ivec2 _911 = at + ivec2(round(vec2(-2.8284270763397216796875, 2.8284270763397216796875)));
                vec4 _1234;
                {
                    bool _1235;
                    if (any(lessThan(_911, ivec2(0))))
                    {
                        _1235 = true;
                    }
                    else
                    {
                        _1235 = any(greaterThanEqual(_911, ivec2(p.size.xy)));
                    }
                    if (_1235)
                    {
                        _1234 = vec4(0.0);
                    }
                    else
                    {
                        _1234 = texelFetch(_2835, ivec3(_911, 0).xy, 0);
                    }
                }
                ivec2 _921 = at + ivec2(round(vec2(-1.5, -1.3113415775478642899543046951294e-07)));
                vec4 _1261;
                {
                    bool _1262;
                    if (any(lessThan(_921, ivec2(0))))
                    {
                        _1262 = true;
                    }
                    else
                    {
                        _1262 = any(greaterThanEqual(_921, ivec2(p.size.xy)));
                    }
                    if (_1262)
                    {
                        _1261 = vec4(0.0);
                    }
                    else
                    {
                        _1261 = texelFetch(_2835, ivec3(_921, 0).xy, 0);
                    }
                }
                ivec2 _928 = at + ivec2(round(vec2(-4.0, -3.4969110629390343092381954193115e-07)));
                vec4 _1288;
                {
                    bool _1289;
                    if (any(lessThan(_928, ivec2(0))))
                    {
                        _1289 = true;
                    }
                    else
                    {
                        _1289 = any(greaterThanEqual(_928, ivec2(p.size.xy)));
                    }
                    if (_1289)
                    {
                        _1288 = vec4(0.0);
                    }
                    else
                    {
                        _1288 = texelFetch(_2835, ivec3(_928, 0).xy, 0);
                    }
                }
                ivec2 _938 = at + ivec2(round(vec2(-1.06066000461578369140625, -1.06066036224365234375)));
                vec4 _1315;
                {
                    bool _1316;
                    if (any(lessThan(_938, ivec2(0))))
                    {
                        _1316 = true;
                    }
                    else
                    {
                        _1316 = any(greaterThanEqual(_938, ivec2(p.size.xy)));
                    }
                    if (_1316)
                    {
                        _1315 = vec4(0.0);
                    }
                    else
                    {
                        _1315 = texelFetch(_2835, ivec3(_938, 0).xy, 0);
                    }
                }
                ivec2 _945 = at + ivec2(round(vec2(-2.8284265995025634765625, -2.8284275531768798828125)));
                vec4 _1342;
                {
                    bool _1343;
                    if (any(lessThan(_945, ivec2(0))))
                    {
                        _1343 = true;
                    }
                    else
                    {
                        _1343 = any(greaterThanEqual(_945, ivec2(p.size.xy)));
                    }
                    if (_1343)
                    {
                        _1342 = vec4(0.0);
                    }
                    else
                    {
                        _1342 = texelFetch(_2835, ivec3(_945, 0).xy, 0);
                    }
                }
                ivec2 _955 = at + ivec2(round(vec2(1.7887320069576162495650351047516e-08, -1.5)));
                vec4 _1369;
                {
                    bool _1370;
                    if (any(lessThan(_955, ivec2(0))))
                    {
                        _1370 = true;
                    }
                    else
                    {
                        _1370 = any(greaterThanEqual(_955, ivec2(p.size.xy)));
                    }
                    if (_1370)
                    {
                        _1369 = vec4(0.0);
                    }
                    else
                    {
                        _1369 = texelFetch(_2835, ivec3(_955, 0).xy, 0);
                    }
                }
                ivec2 _962 = at + ivec2(round(vec2(4.7699522554012219188734889030457e-08, -4.0)));
                vec4 _1396;
                {
                    bool _1397;
                    if (any(lessThan(_962, ivec2(0))))
                    {
                        _1397 = true;
                    }
                    else
                    {
                        _1397 = any(greaterThanEqual(_962, ivec2(p.size.xy)));
                    }
                    if (_1397)
                    {
                        _1396 = vec4(0.0);
                    }
                    else
                    {
                        _1396 = texelFetch(_2835, ivec3(_962, 0).xy, 0);
                    }
                }
                ivec2 _972 = at + ivec2(round(vec2(1.06066048145294189453125, -1.06065976619720458984375)));
                vec4 _1423;
                {
                    bool _1424;
                    if (any(lessThan(_972, ivec2(0))))
                    {
                        _1424 = true;
                    }
                    else
                    {
                        _1424 = any(greaterThanEqual(_972, ivec2(p.size.xy)));
                    }
                    if (_1424)
                    {
                        _1423 = vec4(0.0);
                    }
                    else
                    {
                        _1423 = texelFetch(_2835, ivec3(_972, 0).xy, 0);
                    }
                }
                ivec2 _979 = at + ivec2(round(vec2(2.8284280300140380859375, -2.8284261226654052734375)));
                vec4 _1450;
                {
                    bool _1451;
                    if (any(lessThan(_979, ivec2(0))))
                    {
                        _1451 = true;
                    }
                    else
                    {
                        _1451 = any(greaterThanEqual(_979, ivec2(p.size.xy)));
                    }
                    if (_1451)
                    {
                        _1450 = vec4(0.0);
                    }
                    else
                    {
                        _1450 = texelFetch(_2835, ivec3(_979, 0).xy, 0);
                    }
                }
                if (_1018.w <= 0.0)
                {
                    _843 = vec2((((((((_1072.w + _1126.w) + _1180.w) + _1234.w) + _1288.w) + _1342.w) + _1396.w) + _1450.w) * 0.0350000001490116119384765625, 0.0);
                }
                else
                {
                    float _995 = (1.0 / max(_1018.z, 9.9999997473787516355514526367188e-05)) - 1.0;
                    float _844;
                    if ((_995 - _796) <= (0.25 + (_995 * 0.00999999977648258209228515625)))
                    {
                        _844 = 1.0;
                    }
                    else
                    {
                        _844 = 0.3300000131130218505859375;
                    }
                    _843 = vec2((((0.07999999821186065673828125 + (0.800000011920928955078125 * _1018.x)) + (0.5 * _1018.y)) + (1.2000000476837158203125 * clamp((8.0 - (((((((_1045.w + _1099.w) + _1153.w) + _1207.w) + _1261.w) + _1315.w) + _1369.w) + _1423.w)) * 0.5, 0.0, 1.0))) * _844, _844);
                }
            }
            bool _517 = split != 0;
            float red;
            if (_517)
            {
                ivec2 _522 = at + ivec2(split, 0);
                vec2 _1477;
                {
                    vec4 _1652;
                    {
                        bool _1653;
                        if (any(lessThan(_522, ivec2(0))))
                        {
                            _1653 = true;
                        }
                        else
                        {
                            _1653 = any(greaterThanEqual(_522, ivec2(p.size.xy)));
                        }
                        if (_1653)
                        {
                            _1652 = vec4(0.0);
                        }
                        else
                        {
                            _1652 = texelFetch(_2835, ivec3(_522, 0).xy, 0);
                        }
                    }
                    ivec2 _1489 = _522 + ivec2(round(vec2(1.5, 0.0)));
                    vec4 _1679;
                    {
                        bool _1680;
                        if (any(lessThan(_1489, ivec2(0))))
                        {
                            _1680 = true;
                        }
                        else
                        {
                            _1680 = any(greaterThanEqual(_1489, ivec2(p.size.xy)));
                        }
                        if (_1680)
                        {
                            _1679 = vec4(0.0);
                        }
                        else
                        {
                            _1679 = texelFetch(_2835, ivec3(_1489, 0).xy, 0);
                        }
                    }
                    ivec2 _1495 = _522 + ivec2(round(vec2(4.0, 0.0)));
                    vec4 _1706;
                    {
                        bool _1707;
                        if (any(lessThan(_1495, ivec2(0))))
                        {
                            _1707 = true;
                        }
                        else
                        {
                            _1707 = any(greaterThanEqual(_1495, ivec2(p.size.xy)));
                        }
                        if (_1707)
                        {
                            _1706 = vec4(0.0);
                        }
                        else
                        {
                            _1706 = texelFetch(_2835, ivec3(_1495, 0).xy, 0);
                        }
                    }
                    ivec2 _1504 = _522 + ivec2(round(vec2(1.0606601238250732421875)));
                    vec4 _1733;
                    {
                        bool _1734;
                        if (any(lessThan(_1504, ivec2(0))))
                        {
                            _1734 = true;
                        }
                        else
                        {
                            _1734 = any(greaterThanEqual(_1504, ivec2(p.size.xy)));
                        }
                        if (_1734)
                        {
                            _1733 = vec4(0.0);
                        }
                        else
                        {
                            _1733 = texelFetch(_2835, ivec3(_1504, 0).xy, 0);
                        }
                    }
                    ivec2 _1511 = _522 + ivec2(round(vec2(2.8284270763397216796875)));
                    vec4 _1760;
                    {
                        bool _1761;
                        if (any(lessThan(_1511, ivec2(0))))
                        {
                            _1761 = true;
                        }
                        else
                        {
                            _1761 = any(greaterThanEqual(_1511, ivec2(p.size.xy)));
                        }
                        if (_1761)
                        {
                            _1760 = vec4(0.0);
                        }
                        else
                        {
                            _1760 = texelFetch(_2835, ivec3(_1511, 0).xy, 0);
                        }
                    }
                    ivec2 _1521 = _522 + ivec2(round(vec2(-6.556707887739321449771523475647e-08, 1.5)));
                    vec4 _1787;
                    {
                        bool _1788;
                        if (any(lessThan(_1521, ivec2(0))))
                        {
                            _1788 = true;
                        }
                        else
                        {
                            _1788 = any(greaterThanEqual(_1521, ivec2(p.size.xy)));
                        }
                        if (_1788)
                        {
                            _1787 = vec4(0.0);
                        }
                        else
                        {
                            _1787 = texelFetch(_2835, ivec3(_1521, 0).xy, 0);
                        }
                    }
                    ivec2 _1528 = _522 + ivec2(round(vec2(-1.7484555314695171546190977096558e-07, 4.0)));
                    vec4 _1814;
                    {
                        bool _1815;
                        if (any(lessThan(_1528, ivec2(0))))
                        {
                            _1815 = true;
                        }
                        else
                        {
                            _1815 = any(greaterThanEqual(_1528, ivec2(p.size.xy)));
                        }
                        if (_1815)
                        {
                            _1814 = vec4(0.0);
                        }
                        else
                        {
                            _1814 = texelFetch(_2835, ivec3(_1528, 0).xy, 0);
                        }
                    }
                    ivec2 _1538 = _522 + ivec2(round(vec2(-1.0606601238250732421875, 1.0606601238250732421875)));
                    vec4 _1841;
                    {
                        bool _1842;
                        if (any(lessThan(_1538, ivec2(0))))
                        {
                            _1842 = true;
                        }
                        else
                        {
                            _1842 = any(greaterThanEqual(_1538, ivec2(p.size.xy)));
                        }
                        if (_1842)
                        {
                            _1841 = vec4(0.0);
                        }
                        else
                        {
                            _1841 = texelFetch(_2835, ivec3(_1538, 0).xy, 0);
                        }
                    }
                    ivec2 _1545 = _522 + ivec2(round(vec2(-2.8284270763397216796875, 2.8284270763397216796875)));
                    vec4 _1868;
                    {
                        bool _1869;
                        if (any(lessThan(_1545, ivec2(0))))
                        {
                            _1869 = true;
                        }
                        else
                        {
                            _1869 = any(greaterThanEqual(_1545, ivec2(p.size.xy)));
                        }
                        if (_1869)
                        {
                            _1868 = vec4(0.0);
                        }
                        else
                        {
                            _1868 = texelFetch(_2835, ivec3(_1545, 0).xy, 0);
                        }
                    }
                    ivec2 _1555 = _522 + ivec2(round(vec2(-1.5, -1.3113415775478642899543046951294e-07)));
                    vec4 _1895;
                    {
                        bool _1896;
                        if (any(lessThan(_1555, ivec2(0))))
                        {
                            _1896 = true;
                        }
                        else
                        {
                            _1896 = any(greaterThanEqual(_1555, ivec2(p.size.xy)));
                        }
                        if (_1896)
                        {
                            _1895 = vec4(0.0);
                        }
                        else
                        {
                            _1895 = texelFetch(_2835, ivec3(_1555, 0).xy, 0);
                        }
                    }
                    ivec2 _1562 = _522 + ivec2(round(vec2(-4.0, -3.4969110629390343092381954193115e-07)));
                    vec4 _1922;
                    {
                        bool _1923;
                        if (any(lessThan(_1562, ivec2(0))))
                        {
                            _1923 = true;
                        }
                        else
                        {
                            _1923 = any(greaterThanEqual(_1562, ivec2(p.size.xy)));
                        }
                        if (_1923)
                        {
                            _1922 = vec4(0.0);
                        }
                        else
                        {
                            _1922 = texelFetch(_2835, ivec3(_1562, 0).xy, 0);
                        }
                    }
                    ivec2 _1572 = _522 + ivec2(round(vec2(-1.06066000461578369140625, -1.06066036224365234375)));
                    vec4 _1949;
                    {
                        bool _1950;
                        if (any(lessThan(_1572, ivec2(0))))
                        {
                            _1950 = true;
                        }
                        else
                        {
                            _1950 = any(greaterThanEqual(_1572, ivec2(p.size.xy)));
                        }
                        if (_1950)
                        {
                            _1949 = vec4(0.0);
                        }
                        else
                        {
                            _1949 = texelFetch(_2835, ivec3(_1572, 0).xy, 0);
                        }
                    }
                    ivec2 _1579 = _522 + ivec2(round(vec2(-2.8284265995025634765625, -2.8284275531768798828125)));
                    vec4 _1976;
                    {
                        bool _1977;
                        if (any(lessThan(_1579, ivec2(0))))
                        {
                            _1977 = true;
                        }
                        else
                        {
                            _1977 = any(greaterThanEqual(_1579, ivec2(p.size.xy)));
                        }
                        if (_1977)
                        {
                            _1976 = vec4(0.0);
                        }
                        else
                        {
                            _1976 = texelFetch(_2835, ivec3(_1579, 0).xy, 0);
                        }
                    }
                    ivec2 _1589 = _522 + ivec2(round(vec2(1.7887320069576162495650351047516e-08, -1.5)));
                    vec4 _2003;
                    {
                        bool _2004;
                        if (any(lessThan(_1589, ivec2(0))))
                        {
                            _2004 = true;
                        }
                        else
                        {
                            _2004 = any(greaterThanEqual(_1589, ivec2(p.size.xy)));
                        }
                        if (_2004)
                        {
                            _2003 = vec4(0.0);
                        }
                        else
                        {
                            _2003 = texelFetch(_2835, ivec3(_1589, 0).xy, 0);
                        }
                    }
                    ivec2 _1596 = _522 + ivec2(round(vec2(4.7699522554012219188734889030457e-08, -4.0)));
                    vec4 _2030;
                    {
                        bool _2031;
                        if (any(lessThan(_1596, ivec2(0))))
                        {
                            _2031 = true;
                        }
                        else
                        {
                            _2031 = any(greaterThanEqual(_1596, ivec2(p.size.xy)));
                        }
                        if (_2031)
                        {
                            _2030 = vec4(0.0);
                        }
                        else
                        {
                            _2030 = texelFetch(_2835, ivec3(_1596, 0).xy, 0);
                        }
                    }
                    ivec2 _1606 = _522 + ivec2(round(vec2(1.06066048145294189453125, -1.06065976619720458984375)));
                    vec4 _2057;
                    {
                        bool _2058;
                        if (any(lessThan(_1606, ivec2(0))))
                        {
                            _2058 = true;
                        }
                        else
                        {
                            _2058 = any(greaterThanEqual(_1606, ivec2(p.size.xy)));
                        }
                        if (_2058)
                        {
                            _2057 = vec4(0.0);
                        }
                        else
                        {
                            _2057 = texelFetch(_2835, ivec3(_1606, 0).xy, 0);
                        }
                    }
                    ivec2 _1613 = _522 + ivec2(round(vec2(2.8284280300140380859375, -2.8284261226654052734375)));
                    vec4 _2084;
                    {
                        bool _2085;
                        if (any(lessThan(_1613, ivec2(0))))
                        {
                            _2085 = true;
                        }
                        else
                        {
                            _2085 = any(greaterThanEqual(_1613, ivec2(p.size.xy)));
                        }
                        if (_2085)
                        {
                            _2084 = vec4(0.0);
                        }
                        else
                        {
                            _2084 = texelFetch(_2835, ivec3(_1613, 0).xy, 0);
                        }
                    }
                    if (_1652.w <= 0.0)
                    {
                        _1477 = vec2((((((((_1706.w + _1760.w) + _1814.w) + _1868.w) + _1922.w) + _1976.w) + _2030.w) + _2084.w) * 0.0350000001490116119384765625, 0.0);
                    }
                    else
                    {
                        float _1629 = (1.0 / max(_1652.z, 9.9999997473787516355514526367188e-05)) - 1.0;
                        float _1478;
                        if ((_1629 - _796) <= (0.25 + (_1629 * 0.00999999977648258209228515625)))
                        {
                            _1478 = 1.0;
                        }
                        else
                        {
                            _1478 = 0.3300000131130218505859375;
                        }
                        _1477 = vec2((((0.07999999821186065673828125 + (0.800000011920928955078125 * _1652.x)) + (0.5 * _1652.y)) + (1.2000000476837158203125 * clamp((8.0 - (((((((_1679.w + _1733.w) + _1787.w) + _1841.w) + _1895.w) + _1949.w) + _2003.w) + _2057.w)) * 0.5, 0.0, 1.0))) * _1478, _1478);
                    }
                }
                red = _1477.x;
            }
            else
            {
                red = _843.x;
            }
            float blue;
            if (_517)
            {
                ivec2 _534 = at - ivec2(split, 0);
                vec2 _2111;
                {
                    vec4 _2286;
                    {
                        bool _2287;
                        if (any(lessThan(_534, ivec2(0))))
                        {
                            _2287 = true;
                        }
                        else
                        {
                            _2287 = any(greaterThanEqual(_534, ivec2(p.size.xy)));
                        }
                        if (_2287)
                        {
                            _2286 = vec4(0.0);
                        }
                        else
                        {
                            _2286 = texelFetch(_2835, ivec3(_534, 0).xy, 0);
                        }
                    }
                    ivec2 _2123 = _534 + ivec2(round(vec2(1.5, 0.0)));
                    vec4 _2313;
                    {
                        bool _2314;
                        if (any(lessThan(_2123, ivec2(0))))
                        {
                            _2314 = true;
                        }
                        else
                        {
                            _2314 = any(greaterThanEqual(_2123, ivec2(p.size.xy)));
                        }
                        if (_2314)
                        {
                            _2313 = vec4(0.0);
                        }
                        else
                        {
                            _2313 = texelFetch(_2835, ivec3(_2123, 0).xy, 0);
                        }
                    }
                    ivec2 _2129 = _534 + ivec2(round(vec2(4.0, 0.0)));
                    vec4 _2340;
                    {
                        bool _2341;
                        if (any(lessThan(_2129, ivec2(0))))
                        {
                            _2341 = true;
                        }
                        else
                        {
                            _2341 = any(greaterThanEqual(_2129, ivec2(p.size.xy)));
                        }
                        if (_2341)
                        {
                            _2340 = vec4(0.0);
                        }
                        else
                        {
                            _2340 = texelFetch(_2835, ivec3(_2129, 0).xy, 0);
                        }
                    }
                    ivec2 _2138 = _534 + ivec2(round(vec2(1.0606601238250732421875)));
                    vec4 _2367;
                    {
                        bool _2368;
                        if (any(lessThan(_2138, ivec2(0))))
                        {
                            _2368 = true;
                        }
                        else
                        {
                            _2368 = any(greaterThanEqual(_2138, ivec2(p.size.xy)));
                        }
                        if (_2368)
                        {
                            _2367 = vec4(0.0);
                        }
                        else
                        {
                            _2367 = texelFetch(_2835, ivec3(_2138, 0).xy, 0);
                        }
                    }
                    ivec2 _2145 = _534 + ivec2(round(vec2(2.8284270763397216796875)));
                    vec4 _2394;
                    {
                        bool _2395;
                        if (any(lessThan(_2145, ivec2(0))))
                        {
                            _2395 = true;
                        }
                        else
                        {
                            _2395 = any(greaterThanEqual(_2145, ivec2(p.size.xy)));
                        }
                        if (_2395)
                        {
                            _2394 = vec4(0.0);
                        }
                        else
                        {
                            _2394 = texelFetch(_2835, ivec3(_2145, 0).xy, 0);
                        }
                    }
                    ivec2 _2155 = _534 + ivec2(round(vec2(-6.556707887739321449771523475647e-08, 1.5)));
                    vec4 _2421;
                    {
                        bool _2422;
                        if (any(lessThan(_2155, ivec2(0))))
                        {
                            _2422 = true;
                        }
                        else
                        {
                            _2422 = any(greaterThanEqual(_2155, ivec2(p.size.xy)));
                        }
                        if (_2422)
                        {
                            _2421 = vec4(0.0);
                        }
                        else
                        {
                            _2421 = texelFetch(_2835, ivec3(_2155, 0).xy, 0);
                        }
                    }
                    ivec2 _2162 = _534 + ivec2(round(vec2(-1.7484555314695171546190977096558e-07, 4.0)));
                    vec4 _2448;
                    {
                        bool _2449;
                        if (any(lessThan(_2162, ivec2(0))))
                        {
                            _2449 = true;
                        }
                        else
                        {
                            _2449 = any(greaterThanEqual(_2162, ivec2(p.size.xy)));
                        }
                        if (_2449)
                        {
                            _2448 = vec4(0.0);
                        }
                        else
                        {
                            _2448 = texelFetch(_2835, ivec3(_2162, 0).xy, 0);
                        }
                    }
                    ivec2 _2172 = _534 + ivec2(round(vec2(-1.0606601238250732421875, 1.0606601238250732421875)));
                    vec4 _2475;
                    {
                        bool _2476;
                        if (any(lessThan(_2172, ivec2(0))))
                        {
                            _2476 = true;
                        }
                        else
                        {
                            _2476 = any(greaterThanEqual(_2172, ivec2(p.size.xy)));
                        }
                        if (_2476)
                        {
                            _2475 = vec4(0.0);
                        }
                        else
                        {
                            _2475 = texelFetch(_2835, ivec3(_2172, 0).xy, 0);
                        }
                    }
                    ivec2 _2179 = _534 + ivec2(round(vec2(-2.8284270763397216796875, 2.8284270763397216796875)));
                    vec4 _2502;
                    {
                        bool _2503;
                        if (any(lessThan(_2179, ivec2(0))))
                        {
                            _2503 = true;
                        }
                        else
                        {
                            _2503 = any(greaterThanEqual(_2179, ivec2(p.size.xy)));
                        }
                        if (_2503)
                        {
                            _2502 = vec4(0.0);
                        }
                        else
                        {
                            _2502 = texelFetch(_2835, ivec3(_2179, 0).xy, 0);
                        }
                    }
                    ivec2 _2189 = _534 + ivec2(round(vec2(-1.5, -1.3113415775478642899543046951294e-07)));
                    vec4 _2529;
                    {
                        bool _2530;
                        if (any(lessThan(_2189, ivec2(0))))
                        {
                            _2530 = true;
                        }
                        else
                        {
                            _2530 = any(greaterThanEqual(_2189, ivec2(p.size.xy)));
                        }
                        if (_2530)
                        {
                            _2529 = vec4(0.0);
                        }
                        else
                        {
                            _2529 = texelFetch(_2835, ivec3(_2189, 0).xy, 0);
                        }
                    }
                    ivec2 _2196 = _534 + ivec2(round(vec2(-4.0, -3.4969110629390343092381954193115e-07)));
                    vec4 _2556;
                    {
                        bool _2557;
                        if (any(lessThan(_2196, ivec2(0))))
                        {
                            _2557 = true;
                        }
                        else
                        {
                            _2557 = any(greaterThanEqual(_2196, ivec2(p.size.xy)));
                        }
                        if (_2557)
                        {
                            _2556 = vec4(0.0);
                        }
                        else
                        {
                            _2556 = texelFetch(_2835, ivec3(_2196, 0).xy, 0);
                        }
                    }
                    ivec2 _2206 = _534 + ivec2(round(vec2(-1.06066000461578369140625, -1.06066036224365234375)));
                    vec4 _2583;
                    {
                        bool _2584;
                        if (any(lessThan(_2206, ivec2(0))))
                        {
                            _2584 = true;
                        }
                        else
                        {
                            _2584 = any(greaterThanEqual(_2206, ivec2(p.size.xy)));
                        }
                        if (_2584)
                        {
                            _2583 = vec4(0.0);
                        }
                        else
                        {
                            _2583 = texelFetch(_2835, ivec3(_2206, 0).xy, 0);
                        }
                    }
                    ivec2 _2213 = _534 + ivec2(round(vec2(-2.8284265995025634765625, -2.8284275531768798828125)));
                    vec4 _2610;
                    {
                        bool _2611;
                        if (any(lessThan(_2213, ivec2(0))))
                        {
                            _2611 = true;
                        }
                        else
                        {
                            _2611 = any(greaterThanEqual(_2213, ivec2(p.size.xy)));
                        }
                        if (_2611)
                        {
                            _2610 = vec4(0.0);
                        }
                        else
                        {
                            _2610 = texelFetch(_2835, ivec3(_2213, 0).xy, 0);
                        }
                    }
                    ivec2 _2223 = _534 + ivec2(round(vec2(1.7887320069576162495650351047516e-08, -1.5)));
                    vec4 _2637;
                    {
                        bool _2638;
                        if (any(lessThan(_2223, ivec2(0))))
                        {
                            _2638 = true;
                        }
                        else
                        {
                            _2638 = any(greaterThanEqual(_2223, ivec2(p.size.xy)));
                        }
                        if (_2638)
                        {
                            _2637 = vec4(0.0);
                        }
                        else
                        {
                            _2637 = texelFetch(_2835, ivec3(_2223, 0).xy, 0);
                        }
                    }
                    ivec2 _2230 = _534 + ivec2(round(vec2(4.7699522554012219188734889030457e-08, -4.0)));
                    vec4 _2664;
                    {
                        bool _2665;
                        if (any(lessThan(_2230, ivec2(0))))
                        {
                            _2665 = true;
                        }
                        else
                        {
                            _2665 = any(greaterThanEqual(_2230, ivec2(p.size.xy)));
                        }
                        if (_2665)
                        {
                            _2664 = vec4(0.0);
                        }
                        else
                        {
                            _2664 = texelFetch(_2835, ivec3(_2230, 0).xy, 0);
                        }
                    }
                    ivec2 _2240 = _534 + ivec2(round(vec2(1.06066048145294189453125, -1.06065976619720458984375)));
                    vec4 _2691;
                    {
                        bool _2692;
                        if (any(lessThan(_2240, ivec2(0))))
                        {
                            _2692 = true;
                        }
                        else
                        {
                            _2692 = any(greaterThanEqual(_2240, ivec2(p.size.xy)));
                        }
                        if (_2692)
                        {
                            _2691 = vec4(0.0);
                        }
                        else
                        {
                            _2691 = texelFetch(_2835, ivec3(_2240, 0).xy, 0);
                        }
                    }
                    ivec2 _2247 = _534 + ivec2(round(vec2(2.8284280300140380859375, -2.8284261226654052734375)));
                    vec4 _2718;
                    {
                        bool _2719;
                        if (any(lessThan(_2247, ivec2(0))))
                        {
                            _2719 = true;
                        }
                        else
                        {
                            _2719 = any(greaterThanEqual(_2247, ivec2(p.size.xy)));
                        }
                        if (_2719)
                        {
                            _2718 = vec4(0.0);
                        }
                        else
                        {
                            _2718 = texelFetch(_2835, ivec3(_2247, 0).xy, 0);
                        }
                    }
                    if (_2286.w <= 0.0)
                    {
                        _2111 = vec2((((((((_2340.w + _2394.w) + _2448.w) + _2502.w) + _2556.w) + _2610.w) + _2664.w) + _2718.w) * 0.0350000001490116119384765625, 0.0);
                    }
                    else
                    {
                        float _2263 = (1.0 / max(_2286.z, 9.9999997473787516355514526367188e-05)) - 1.0;
                        float _2112;
                        if ((_2263 - _796) <= (0.25 + (_2263 * 0.00999999977648258209228515625)))
                        {
                            _2112 = 1.0;
                        }
                        else
                        {
                            _2112 = 0.3300000131130218505859375;
                        }
                        _2111 = vec2((((0.07999999821186065673828125 + (0.800000011920928955078125 * _2286.x)) + (0.5 * _2286.y)) + (1.2000000476837158203125 * clamp((8.0 - (((((((_2313.w + _2367.w) + _2421.w) + _2475.w) + _2529.w) + _2583.w) + _2637.w) + _2691.w)) * 0.5, 0.0, 1.0))) * _2112, _2112);
                    }
                }
                blue = _2111.x;
            }
            else
            {
                blue = _843.x;
            }
            if (max(max(_843.x, red), blue) <= 0.0)
            {
                imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), vec4(_693, 1.0));
            }
            else
            {
                float _559 = floor(p.look.x * 30.0);
                vec3 _2748 = fract((vec2(_73) + vec2(_559 * 7.13000011444091796875, _559 * 3.71000003814697265625)).xyx * 0.103100001811981201171875);
                vec3 _2754 = _2748 + vec3(dot(_2748, _2748.yzx + vec3(33.3300018310546875)));
                float _2760 = fract((_2754.x + _2754.y) * _2754.z);
                float raster;
                if ((_73.y - 3 * (_73.y / 3)) == 0)
                {
                    raster = 0.699999988079071044921875;
                }
                else
                {
                    raster = 1.0;
                }
                float shimmer;
                if (dropped)
                {
                    shimmer = 0.0;
                }
                else
                {
                    shimmer = ((0.85000002384185791015625 + (0.300000011920928955078125 * _2760)) * raster) * (0.89999997615814208984375 + ((0.100000001490116119384765625 * sin(p.look.x * 23.0)) * sin(p.look.x * 7.30000019073486328125)));
                }
                float _624 = noiseLine;
                if (dropped)
                {
                    noiseLine = 0.0;
                }
                else
                {
                    noiseLine = 1.0;
                }
                imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), vec4((_693 * (1.0 - ((0.2199999988079071044921875 * _843.y) * noiseLine))) + (((vec3(mix(p.tint.x, 0.85000002384185791015625, clamp(red - 0.800000011920928955078125, 0.0, 1.0)) * red, mix(p.tint.y, 0.9700000286102294921875, clamp(_843.x - 0.800000011920928955078125, 0.0, 1.0)) * _843.x, mix(p.tint.z, 1.0, clamp(blue - 0.800000011920928955078125, 0.0, 1.0)) * blue) + (((vec3(0.85000002384185791015625, 0.9700000286102294921875, 1.0) * _624) * (0.4000000059604644775390625 + (0.60000002384185791015625 * _2760))) * _107)) * shimmer) * (p.tint.w / max(p.look.y, 0.001000000047497451305389404296875))), 1.0));
            }
        }
    }
}

