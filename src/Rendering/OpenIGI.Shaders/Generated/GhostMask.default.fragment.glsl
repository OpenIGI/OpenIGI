#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _101 = (cClipYDown != 0);

layout(binding = 0, std140) uniform GhostMaskPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 rightRow;
    vec4 upRow;
    vec4 depthRow;
    vec4 control;
} push;

layout(location = 2) in vec3 input_bodyMetres;
layout(location = 0) in vec3 input_viewNormal;
layout(location = 1) in vec2 input_surface;
layout(location = 0) out vec4 entryPointParam_fragmentMain_shape;
layout(location = 1) out vec4 entryPointParam_fragmentMain_skin;

void main()
{
    vec3 _155 = input_bodyMetres * 4.0;
    vec3 _311 = floor(_155);
    vec3 _312 = fract(_155);
    vec3 _317 = (_312 * _312) * (vec3(3.0) - (_312 * 2.0));
    vec3 _346 = fract(_311 * vec3(0.103100001811981201171875, 0.10300000011920928955078125, 0.097300000488758087158203125));
    vec3 _352 = _346 + vec3(dot(_346, _346.yxz + vec3(33.3300018310546875)));
    vec3 _362 = fract((_311 + vec3(1.0, 0.0, 0.0)) * vec3(0.103100001811981201171875, 0.10300000011920928955078125, 0.097300000488758087158203125));
    vec3 _368 = _362 + vec3(dot(_362, _362.yxz + vec3(33.3300018310546875)));
    float _321 = _317.x;
    vec3 _378 = fract((_311 + vec3(0.0, 1.0, 0.0)) * vec3(0.103100001811981201171875, 0.10300000011920928955078125, 0.097300000488758087158203125));
    vec3 _384 = _378 + vec3(dot(_378, _378.yxz + vec3(33.3300018310546875)));
    vec3 _394 = fract((_311 + vec3(1.0, 1.0, 0.0)) * vec3(0.103100001811981201171875, 0.10300000011920928955078125, 0.097300000488758087158203125));
    vec3 _400 = _394 + vec3(dot(_394, _394.yxz + vec3(33.3300018310546875)));
    float _328 = _317.y;
    vec3 _410 = fract((_311 + vec3(0.0, 0.0, 1.0)) * vec3(0.103100001811981201171875, 0.10300000011920928955078125, 0.097300000488758087158203125));
    vec3 _416 = _410 + vec3(dot(_410, _410.yxz + vec3(33.3300018310546875)));
    vec3 _426 = fract((_311 + vec3(1.0, 0.0, 1.0)) * vec3(0.103100001811981201171875, 0.10300000011920928955078125, 0.097300000488758087158203125));
    vec3 _432 = _426 + vec3(dot(_426, _426.yxz + vec3(33.3300018310546875)));
    vec3 _442 = fract((_311 + vec3(0.0, 1.0, 1.0)) * vec3(0.103100001811981201171875, 0.10300000011920928955078125, 0.097300000488758087158203125));
    vec3 _448 = _442 + vec3(dot(_442, _442.yxz + vec3(33.3300018310546875)));
    vec3 _458 = fract((_311 + vec3(1.0)) * vec3(0.103100001811981201171875, 0.10300000011920928955078125, 0.097300000488758087158203125));
    vec3 _464 = _458 + vec3(dot(_458, _458.yxz + vec3(33.3300018310546875)));
    float _228 = 0.699999988079071044921875 * mix(mix(mix(fract((_352.x + _352.y) * _352.z), fract((_368.x + _368.y) * _368.z), _321), mix(fract((_384.x + _384.y) * _384.z), fract((_400.x + _400.y) * _400.z), _321), _328), mix(mix(fract((_416.x + _416.y) * _416.z), fract((_432.x + _432.y) * _432.z), _321), mix(fract((_448.x + _448.y) * _448.z), fract((_464.x + _464.y) * _464.z), _321), _328), _317.z);
    vec3 _474 = fract(floor(input_bodyMetres * 16.0) * vec3(0.103100001811981201171875, 0.10300000011920928955078125, 0.097300000488758087158203125));
    vec3 _480 = _474 + vec3(dot(_474, _474.yxz + vec3(33.3300018310546875)));
    float _237 = clamp(_228 + (0.300000011920928955078125 * fract((_480.x + _480.y) * _480.z)), 0.0, 1.0);
    if (_237 > (push.control.z * 1.00010001659393310546875))
    {
        discard;
    }
    float _248 = dot(input_viewNormal, input_viewNormal);
    vec3 n;
    if (_248 > 9.9999999600419720025001879548654e-13)
    {
        n = input_viewNormal * inversesqrt(_248);
    }
    else
    {
        n = vec3(0.0, 0.0, -1.0);
    }
    if (n.z > 0.0)
    {
        n = -n;
    }
    entryPointParam_fragmentMain_shape = vec4(n.xy, max(input_surface.x, 0.0), 1.0);
    entryPointParam_fragmentMain_skin = vec4(clamp(input_surface.y * 0.52631580829620361328125, 0.0, 1.0), _237, pow(clamp(1.0 - abs(n.z), 0.0, 1.0), 2.400000095367431640625), 0.0);
}

