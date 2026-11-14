#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _57 = (cClipYDown != 0);

layout(binding = 0, std140) uniform HudFxParams_std140
{
    vec4 frame;
    vec4 visor;
    vec4 lines;
    vec4 shake;
    vec4 damage;
    vec4 damageColour;
    vec4 alert;
} p;

layout(binding = 0) uniform sampler2D _1103;
layout(binding = 1) uniform sampler2D _1105;
layout(binding = 2) uniform sampler2D _1107;
layout(binding = 3) uniform sampler2D _1109;
layout(binding = 4) uniform sampler2D _1111;
layout(binding = 5) uniform sampler2D _1113;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) in vec2 input_uv;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    {
        float _139 = clamp(p.visor.x, 0.0, 1.0);
        if (_139 <= 0.0)
        {
            vec4 _833;
            {
                bool _834;
                if (any(lessThan(input_uv, vec2(0.0))))
                {
                    _834 = true;
                }
                else
                {
                    _834 = any(greaterThan(input_uv, vec2(1.0)));
                }
                if (_834)
                {
                    _833 = vec4(0.0);
                }
                else
                {
                    _833 = textureLod(_1103, input_uv, 0.0);
                }
            }
            entryPointParam_fragmentMain = _833;
        }
        else
        {
            vec2 centred = ((input_uv - ((p.shake.xy * p.frame.zw) * _139)) * 2.0) - vec2(1.0);
            float _200 = dot(centred, centred);
            vec2 centred_1 = centred * (1.0 + ((p.visor.y * _139) * _200));
            vec2 uv = (centred_1 * 0.5) + vec2(0.5);
            vec2 fringe = (centred_1 * (((p.visor.z * _139) * _200) * 0.5)) * p.frame.zw;
            vec2 _220 = uv + fringe;
            vec4 _858;
            {
                bool _859;
                if (any(lessThan(_220, vec2(0.0))))
                {
                    _859 = true;
                }
                else
                {
                    _859 = any(greaterThan(_220, vec2(1.0)));
                }
                if (_859)
                {
                    _858 = vec4(0.0);
                }
                else
                {
                    _858 = textureLod(_1103, _220, 0.0);
                }
            }
            vec4 _883;
            {
                bool _884;
                if (any(lessThan(uv, vec2(0.0))))
                {
                    _884 = true;
                }
                else
                {
                    _884 = any(greaterThan(uv, vec2(1.0)));
                }
                if (_884)
                {
                    _883 = vec4(0.0);
                }
                else
                {
                    _883 = textureLod(_1103, uv, 0.0);
                }
            }
            vec2 _223 = uv - fringe;
            vec4 _908;
            {
                bool _909;
                if (any(lessThan(_223, vec2(0.0))))
                {
                    _909 = true;
                }
                else
                {
                    _909 = any(greaterThan(_223, vec2(1.0)));
                }
                if (_909)
                {
                    _908 = vec4(0.0);
                }
                else
                {
                    _908 = textureLod(_1103, _223, 0.0);
                }
            }
            vec4 _233 = vec4(_858.x, _883.y, _908.z, max(_883.w, max(_858.w, _908.w)));
            vec3 bloom = (max((((textureLod(_1105, uv, 0.0) * 0.5) + (textureLod(_1107, uv, 0.0) * 0.300000011920928955078125)) + (textureLod(_1109, uv, 0.0) * 0.20000000298023223876953125)).xyz - vec3(0.449999988079071044921875), vec3(0.0)) * 1.8181817531585693359375) * (p.visor.w * _139);
            vec3 _274 = _233.xyz + bloom;
            vec4 _1080 = _233;
            _1080.x = _274.x;
            _1080.y = _274.y;
            _1080.z = _274.z;
            _1080.w = clamp(_1080.w + (dot(bloom, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625)) * 0.5), 0.0, 1.0);
            float _295 = uv.y;
            float y = _295 * p.frame.y;
            vec3 _333 = _1080.xyz * ((1.0 - ((p.lines.y * _139) * (0.5 - (0.5 * cos((y * (p.lines.x * 0.000925925909541547298431396484375)) * 6.283185482025146484375))))) + ((0.0599999986588954925537109375 * _139) * exp(-pow((fract(_295 - (p.lines.w * 0.07999999821186065673828125)) - 0.5) * 9.0, 2.0))));
            vec4 _1088 = _1080;
            _1088.x = _333.x;
            _1088.y = _333.y;
            _1088.z = _333.z;
            vec4 colour = _1088;
            float _343 = clamp(p.lines.z, 0.0, 1.0);
            if (_343 < 1.0)
            {
                float _348 = floor(y * 0.16666667163372039794921875);
                colour *= (step(fract(sin(_348 * 17.7960262298583984375) * 43758.546875), _343) * mix(0.3499999940395355224609375, 1.0, step(0.5 - (_343 * 0.5), fract(sin((_348 + floor(p.lines.w * 30.0)) * 12.98980045318603515625) * 43758.546875))));
            }
            float aspect = p.frame.x * p.frame.w;
            vec2 _387 = input_uv - vec2(0.5);
            vec2 _388 = vec2(aspect, 1.0);
            vec2 fromMiddle = _387 * _388;
            float _394 = floor(p.lines.w * 24.0);
            vec2 qa = input_uv * _388;
            vec2 tr = qa - vec2(aspect, 0.0);
            vec2 bl = qa - vec2(0.0, 1.0);
            vec2 br = qa - _388;
            float hit = clamp(((((p.damage.x * exp(dot(qa, qa) * (-2.599999904632568359375))) + (p.damage.y * exp(dot(tr, tr) * (-2.599999904632568359375)))) + (p.damage.z * exp(dot(bl, bl) * (-2.599999904632568359375)))) + (p.damage.w * exp(dot(br, br) * (-2.599999904632568359375)))) * 1.60000002384185791015625, 0.0, 1.0) * _139;
            bool _84;
            if (hit > 0.0)
            {
                _84 = p.damageColour.w > 0.5;
            }
            else
            {
                _84 = false;
            }
            vec3 torn;
            float tear;
            if (_84)
            {
                float _465 = floor((input_uv.y * 1080.0) / (14.0 + (10.0 * fract(sin((floor(input_uv.y * 31.0) + _394) * 12.98980045318603515625) * 43758.546875))));
                vec2 suv = input_uv + vec2(((step(0.62000000476837158203125, fract(sin(((_465 * 3.1700000762939453125) + _394) * 12.98980045318603515625) * 43758.546875)) * (fract(sin((_465 + (_394 * 1.309999942779541015625)) * 12.98980045318603515625) * 43758.546875) - 0.5)) * 0.0500000007450580596923828125) * hit, 0.0);
                vec2 split = (normalize(fromMiddle + vec2(9.9999997473787516355514526367188e-05)) * vec2(1.0 / aspect, 1.0)) * (0.01200000010430812835693359375 * hit);
                vec3 _532 = mix(vec3(textureLod(_1111, suv + split, 0.0).x, textureLod(_1111, suv, 0.0).y, textureLod(_1111, suv - split, 0.0).z), textureLod(_1113, suv, 0.0).xyz, vec3(0.4000000059604644775390625 * hit));
                float _533 = dot(_532, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875));
                vec3 torn_1 = (mix(_532, vec3(_533), vec3(0.5 * hit)) * (1.0 - (0.119999997317790985107421875 * hit))) + (p.damageColour.xyz * ((_533 * 0.25) * hit));
                if (p.damageColour.w > 1.5)
                {
                    vec3 _987 = max(torn_1, vec3(0.0));
                    bvec3 _989 = lessThanEqual(_987, vec3(0.003130800090730190277099609375));
                    vec3 _990 = _987 * 12.9200000762939453125;
                    vec3 _994 = (pow(_987, vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875);
                    torn = vec3(_989.x ? _990.x : _994.x, _989.y ? _990.y : _994.y, _989.z ? _990.z : _994.z);
                }
                else
                {
                    torn = torn_1;
                }
                tear = clamp(hit * 1.5, 0.0, 1.0);
            }
            else
            {
                torn = vec3(0.0);
                tear = 0.0;
            }
            float wheel = clamp(p.shake.z, 0.0, 1.0) * _139;
            if (wheel > 0.0)
            {
                _84 = p.damageColour.w > 0.5;
            }
            else
            {
                _84 = false;
            }
            if (_84)
            {
                int i = 0;
                vec3 smear = vec3(0.0);
                for (;;)
                {
                    if (!(i < 10))
                    {
                        break;
                    }
                    int _616 = i;
                    i++;
                    smear += textureLod(_1111, vec2(0.5) + (_387 * (1.0 - ((float(_616) * 0.0077777779661118984222412109375) * wheel))), 0.0).xyz;
                    continue;
                }
                vec3 _653 = mix(smear * 0.100000001490116119384765625, textureLod(_1113, input_uv, 0.0).xyz, vec3(0.3499999940395355224609375 * wheel));
                float _654 = dot(_653, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875));
                float grey = (_654 * _654) * (3.0 - (2.0 * _654));
                vec2 _683 = input_uv * p.frame.xy;
                uint _1007 = ((uint(_683.x) * 1664525u) ^ (uint(_683.y) * 1013904223u)) ^ (uint(_394) * 2654435761u);
                uint _1010 = (_1007 ^ (_1007 >> uint(16))) * 2146121005u;
                uint _1013 = (_1010 ^ (_1010 >> uint(15))) * 2221713035u;
                vec3 drained_1 = (mix(_653, vec3(grey, grey * 0.98500001430511474609375, grey * 0.959999978542327880859375), vec3(0.920000016689300537109375 * wheel)) * ((1.0 - (0.300000011920928955078125 * wheel)) * (1.0 - ((0.60000002384185791015625 * wheel) * smoothstep(0.25, 0.85000002384185791015625, length(fromMiddle)))))) + vec3((((float(_1013 ^ (_1013 >> uint(16))) * 2.3283064365386962890625e-10) - 0.5) * 0.02500000037252902984619140625) * wheel);
                vec3 drained;
                if (p.damageColour.w > 1.5)
                {
                    vec3 _1020 = max(drained_1, vec3(0.0));
                    bvec3 _1022 = lessThanEqual(_1020, vec3(0.003130800090730190277099609375));
                    vec3 _1023 = _1020 * 12.9200000762939453125;
                    vec3 _1027 = (pow(_1020, vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875);
                    drained = vec3(_1022.x ? _1023.x : _1027.x, _1022.y ? _1023.y : _1027.y, _1022.z ? _1023.z : _1027.z);
                }
                else
                {
                    drained = drained_1;
                }
                torn = mix(torn * tear, drained, vec3(wheel)) / vec3(max(max(tear, wheel), 9.9999997473787516355514526367188e-05));
                tear = max(tear, wheel);
            }
            float dim;
            if (p.damageColour.w > 0.5)
            {
                dim = 0.0;
            }
            else
            {
                dim = hit * 0.1500000059604644775390625;
            }
            vec4 _1030;
            {
                bool _1031;
                if (any(lessThan(input_uv, vec2(0.0))))
                {
                    _1031 = true;
                }
                else
                {
                    _1031 = any(greaterThan(input_uv, vec2(1.0)));
                }
                if (_1031)
                {
                    _1030 = vec4(0.0);
                }
                else
                {
                    _1030 = textureLod(_1103, input_uv, 0.0);
                }
            }
            vec4 _766 = mix(_1030, colour, vec4(_139));
            vec3 _775 = _766.xyz + (torn * (tear * (1.0 - _766.w)));
            vec4 _1094 = _766;
            _1094.x = _775.x;
            _1094.y = _775.y;
            _1094.z = _775.z;
            _1094.w = _1094.w + (max(tear, dim) * (1.0 - _1094.w));
            entryPointParam_fragmentMain = _1094;
        }
    }
}

