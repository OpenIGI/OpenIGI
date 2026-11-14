#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _57 = (cClipYDown != 0);
uniform vec2 _173_frag[16] = vec2[](vec2(-0.612999975681304931640625, -0.61699998378753662109375), vec2(0.17000000178813934326171875, -0.039999999105930328369140625), vec2(-0.2989999949932098388671875, 0.73500001430511474609375), vec2(0.644999980926513671875, 0.507000029087066650390625), vec2(0.421000003814697265625, -0.73500001430511474609375), vec2(-0.859000027179718017578125, 0.1180000007152557373046875), vec2(0.902000010013580322265625, -0.11699999868869781494140625), vec2(-0.1829999983310699462890625, -0.29100000858306884765625), vec2(0.21899999678134918212890625, 0.4180000126361846923828125), vec2(-0.5559999942779541015625, 0.342999994754791259765625), vec2(0.70200002193450927734375, 0.12899999320507049560546875), vec2(-0.114000000059604644775390625, 0.976000010967254638671875), vec2(-0.3829999864101409912109375, -0.912000000476837158203125), vec2(0.512000024318695068359375, -0.3720000088214874267578125), vec2(-0.762000024318695068359375, -0.2899999916553497314453125), vec2(0.0610000006854534149169921875, -0.665000021457672119140625));
#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _256 = (cSamplerDecodes != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_65
#define SPIRV_CROSS_CONSTANT_ID_65 0
#endif
const int cTargetEncodes = SPIRV_CROSS_CONSTANT_ID_65;
const bool _553 = (cTargetEncodes != 0);

layout(binding = 0, std140) uniform LoadingPush_std140
{
    float time;
    float blur;
    float desaturate;
    float vignette;
    float zoom;
    float grain;
    float scanline;
    float opacity;
    float letterbox;
    float hasPicture;
    float aspect;
    float sweep;
    float reserved0;
    float reserved1;
    float reserved2;
    float reserved3;
} p;

layout(binding = 0) uniform sampler2D _769;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) in vec2 input_uv;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    vec3 sum;
    if (p.hasPicture > 0.5)
    {
        vec2 _127 = ((input_uv - vec2(0.5)) / vec2(max(p.zoom, 0.5))) + vec2(0.5);
        float radius = p.blur * 0.02199999988079071044921875;
        vec2 _138 = vec2(radius / max(p.aspect, 0.100000001490116119384765625), radius);
        float turn = fract(sin(dot(input_uv * vec2(1919.0, 1079.0), vec2(127.09999847412109375, 311.70001220703125))) * 43758.546875) * 6.283185482025146484375;
        float _159 = cos(turn);
        float _160 = sin(turn);
        mat2 _165 = mat2(vec2(_159, _160), vec2(-_160, _159));
        int i = 0;
        sum = vec3(0.0);
        vec3 _696;
        for (;;)
        {
            if (!(i < 16))
            {
                break;
            }
            vec4 sampled = texture(_769, clamp(_127 + ((_165 * _173_frag[i]) * _138), vec2(0.00200000009499490261077880859375), vec2(0.99800002574920654296875)));
            vec3 _245 = sampled.xyz;
            if (_256)
            {
                _696 = mix((pow(max(_245, vec3(0.0)), vec3(0.4166666567325592041015625)) * 1.05499994754791259765625) - vec3(0.054999999701976776123046875), _245 * 12.9200000762939453125, step(_245, vec3(0.003130800090730190277099609375)));
            }
            else
            {
                _696 = _245;
            }
            i++;
            sum += _696;
            continue;
        }
        sum *= vec3(0.0625);
    }
    else
    {
        vec2 _613 = (input_uv * vec2(3.0 * p.aspect, 3.0)) + vec2(p.time * 0.02500000037252902984619140625, p.time * (-0.0170000009238719940185546875));
        int _614 = 0;
        float _615 = 0.5;
        float _616 = 0.0;
        for (;;)
        {
            if (!(_614 < 4))
            {
                break;
            }
            vec2 _627 = _613;
            vec2 _647 = floor(_627);
            vec2 _648 = fract(_627);
            vec2 _653 = (_648 * _648) * (vec2(3.0) - (_648 * 2.0));
            float _661 = _653.x;
            float _629 = _615;
            _613 = (_627 * 2.0299999713897705078125) + vec2(17.0, 9.0);
            _614++;
            _615 = _629 * 0.5;
            _616 += (_629 * mix(mix(fract(sin(dot(_647, vec2(127.09999847412109375, 311.70001220703125))) * 43758.546875), fract(sin(dot(_647 + vec2(1.0, 0.0), vec2(127.09999847412109375, 311.70001220703125))) * 43758.546875), _661), mix(fract(sin(dot(_647 + vec2(0.0, 1.0), vec2(127.09999847412109375, 311.70001220703125))) * 43758.546875), fract(sin(dot(_647 + vec2(1.0), vec2(127.09999847412109375, 311.70001220703125))) * 43758.546875), _661), _653.y));
            continue;
        }
        vec2 _417 = fract(input_uv * vec2(20.0 * p.aspect, 20.0));
        float _418 = _417.x;
        float _419 = _417.y;
        sum = mix(vec3(0.02999999932944774627685546875, 0.0379999987781047821044921875, 0.0500000007450580596923828125), vec3(0.104999996721744537353515625, 0.12999999523162841796875, 0.16500000655651092529296875), vec3((_616 * 0.85000002384185791015625) + ((1.0 - input_uv.y) * 0.20000000298023223876953125))) + ((vec3(0.550000011920928955078125, 0.699999988079071044921875, 0.89999997615814208984375) * (1.0 - smoothstep(0.0, 0.0500000007450580596923828125, min(min(_418, _419), min(1.0 - _418, 1.0 - _419))))) * 0.04500000178813934326171875);
    }
    vec3 _443 = vec3(dot(sum, vec3(0.2989999949932098388671875, 0.58700001239776611328125, 0.114000000059604644775390625)));
    float band = (input_uv.x + (input_uv.y * 0.3499999940395355224609375)) - p.sweep;
    float _529 = p.letterbox - 0.0030000000260770320892333984375;
    float _531 = p.letterbox + 0.0030000000260770320892333984375;
    vec4 _542 = vec4(max((((((mix(sum, mix(_443, _443 * vec3(0.839999973773956298828125, 0.939999997615814208984375, 1.10000002384185791015625), vec3(0.64999997615814208984375)), vec3(p.desaturate)) * mix(1.0, 0.519999980926513671875, p.desaturate)) + ((vec3(0.085000000894069671630859375, 0.100000001490116119384765625, 0.125) * exp(((-band) * band) * 36.0)) * p.desaturate)) * mix(1.0, smoothstep(0.980000019073486328125, 0.2800000011920928955078125, length((input_uv - vec2(0.5)) * vec2(1.10000002384185791015625, 1.0))), p.vignette)) * (1.0 - (p.scanline * (0.5 + (0.5 * sin(input_uv.y * 1800.0)))))) + vec3((fract(sin(dot((input_uv * vec2(1920.0, 1080.0)) + vec2(fract(p.time * 7.309999942779541015625)), vec2(127.09999847412109375, 311.70001220703125))) * 43758.546875) - 0.5) * p.grain)) * (smoothstep(_529, _531, input_uv.y) * smoothstep(_529, _531, 1.0 - input_uv.y)), vec3(0.0)), p.opacity);
    vec4 _720;
    if (_553)
    {
        vec3 _725 = _542.xyz;
        _720 = vec4(mix(pow(max((_725 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _725 * vec3(0.077399380505084991455078125), step(_725, vec3(0.040449999272823333740234375))), p.opacity);
    }
    else
    {
        _720 = _542;
    }
    entryPointParam_fragmentMain = _720;
}

