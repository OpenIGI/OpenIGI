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

layout(location = 0) in vec2 input_position;
layout(location = 1) in vec2 input_texCoord;
layout(location = 2) in vec4 input_colorBgra;
layout(location = 0) out vec2 entryPointParam_vertexMain_texCoord;
layout(location = 1) out vec4 entryPointParam_vertexMain_color;

void main()
{
    vec2 _479 = input_texCoord;
    vec4 _480 = input_colorBgra;
    vec2 _367 = ((input_position / vec2(push.projectionSize.x, abs(push.projectionSize.y))) * 2.0) - vec2(1.0);
    float _368;
    if (push.projectionSize.y < 0.0)
    {
        _368 = -_367.y;
    }
    else
    {
        _368 = _367.y;
    }
    vec2 _491 = _367;
    _491.y = _368;
    _367 = _491;
    gl_Position = vec4(_491, 0.0, 1.0);
    entryPointParam_vertexMain_texCoord = _479;
    entryPointParam_vertexMain_color = _480.zyxw;
}

