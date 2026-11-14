#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _134 = (cSamplerDecodes != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_65
#define SPIRV_CROSS_CONSTANT_ID_65 0
#endif
const int cTargetEncodes = SPIRV_CROSS_CONSTANT_ID_65;
const bool _178 = (cTargetEncodes != 0);

layout(binding = 0, std140) uniform SpritePush_std140
{
    vec2 projectionSize;
} push;

layout(location = 0) in vec2 input_position;
layout(location = 1) in vec2 input_texCoord;
layout(location = 2) in vec4 input_colorBgra;
layout(location = 0) out vec2 entryPointParam_vertexMain_texCoord;
layout(location = 1) out vec4 entryPointParam_vertexMain_color;

void main()
{
    vec2 _308 = input_texCoord;
    vec4 _309 = input_colorBgra;
    vec2 _213 = ((input_position / vec2(push.projectionSize.x, abs(push.projectionSize.y))) * 2.0) - vec2(1.0);
    float _214;
    if (push.projectionSize.y < 0.0)
    {
        _214 = -_213.y;
    }
    else
    {
        _214 = _213.y;
    }
    vec2 _321 = _213;
    _321.y = _214;
    _213 = _321;
    gl_Position = vec4(_321, 0.0, 1.0);
    entryPointParam_vertexMain_texCoord = _308;
    entryPointParam_vertexMain_color = _309.zyxw;
}

