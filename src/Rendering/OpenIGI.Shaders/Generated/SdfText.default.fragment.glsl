#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_65
#define SPIRV_CROSS_CONSTANT_ID_65 0
#endif
const int cTargetEncodes = SPIRV_CROSS_CONSTANT_ID_65;
const bool _328 = (cTargetEncodes != 0);

layout(binding = 0, std140) uniform SdfTextPush_std140
{
    vec2 projectionSize;
    vec4 range;
    vec4 outline;
    vec4 glow;
    vec4 glowWidth;
} push;

layout(binding = 1) uniform sampler2D _500;
layout(binding = 0) uniform sampler2D _503;

layout(location = 0) in vec2 input_texCoord;
layout(location = 1) in vec4 input_color;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    vec4 sampled = texture(_500, input_texCoord);
    float d = sampled.w;
    uvec2 _408 = uvec2(textureSize(_503, 0));
    vec2 _430 = fwidth(input_texCoord);
    float _427 = max(0.5 * dot(vec2(push.range.x, push.range.x) / vec2(float(_408.x), float(_408.y)), vec2(1.0) / _430), 1.0);
    float _187 = clamp(push.range.y, 0.0, 1.0);
    float _436 = fract(sin(dot(floor(input_texCoord * 1024.0) + vec2(push.range.z * 17.0, push.range.z * 5.0), vec2(127.09999847412109375, 311.70001220703125))) * 43758.546875);
    bool _215 = _187 > 0.0;
    bool _108;
    if (_215)
    {
        _108 = _436 < _187;
    }
    else
    {
        _108 = false;
    }
    if (_108)
    {
        discard;
    }
    float ember;
    if (_215)
    {
        ember = 1.0 - smoothstep(_187, _187 + 0.1599999964237213134765625, _436);
    }
    else
    {
        ember = 0.0;
    }
    vec4 result = vec4(0.0);
    if (push.glow.w > 0.0)
    {
        _108 = push.glowWidth.x > 0.0;
    }
    else
    {
        _108 = false;
    }
    if (_108)
    {
        result = vec4(push.glow.xyz, smoothstep(0.5 - push.glowWidth.x, 0.5, d) * push.glow.w);
    }
    if (push.outline.w > 0.0)
    {
        result = mix(result, vec4(push.outline.xyz, 1.0), vec4(clamp(((d - (0.5 - push.outline.w)) * _427) + 0.5, 0.0, 1.0)));
    }
    vec4 _309 = mix(result, vec4(mix(input_color.xyz, vec3(1.0, 0.86000001430511474609375, 0.62000000476837158203125), vec3(ember * 0.89999997615814208984375)), 1.0), vec4(clamp((((d - 0.5) - (_187 * 0.2199999988079071044921875)) * _427) + 0.5, 0.0, 1.0)));
    _309.w = _309.w * input_color.w;
    result = _309;
    vec4 _437;
    if (_328)
    {
        _437 = vec4(mix(pow(max((_309.xyz + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _309.xyz * vec3(0.077399380505084991455078125), step(_309.xyz, vec3(0.040449999272823333740234375))), _309.w);
    }
    else
    {
        _437 = _309;
    }
    entryPointParam_fragmentMain = _437;
}

