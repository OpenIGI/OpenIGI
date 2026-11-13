#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _66 = (cClipYDown != 0);

layout(binding = 0, std140) uniform OutlinePush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 colour;
    vec4 control;
    vec4 viewport;
} push;

layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    entryPointParam_fragmentMain = push.colour;
}

