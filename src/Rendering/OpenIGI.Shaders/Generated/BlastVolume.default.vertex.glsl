#version 430

struct BlastSlot_std140
{
    vec4 centre;
    vec4 time;
    vec4 shape;
    vec4 fire;
    vec4 extra;
};

struct _Array_std140_BlastSlot3
{
    BlastSlot_std140 data[3];
};

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _58 = (cClipYDown != 0);
float _11262;

layout(binding = 1, std140) uniform BlastVolumePush_std140
{
    layout(row_major) mat4 modelView;
    vec4 draw;
} push;

layout(binding = 0, std140) uniform BlastVolumeParams_std140
{
    layout(row_major) mat4 projection;
    vec4 viewToWorld0;
    vec4 viewToWorld1;
    vec4 viewToWorld2;
    vec4 unproject;
    vec4 screen;
    vec4 sun;
    vec4 sunColour;
    vec4 ambient;
    _Array_std140_BlastSlot3 blasts;
} p;

layout(location = 0) in vec3 input_position;
layout(location = 0) out vec3 entryPointParam_vertexMain_viewPos;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec4 _27 = vec4(input_position, 1.0) * spvWorkaroundRowMajor(push.modelView);
    vec3 _10965 = _27.xyz;
    vec4 _42 = _27 * spvWorkaroundRowMajor(p.projection);
    vec4 clip = _42;
    float _46 = _42.y;
    float _2078;
    if (_58)
    {
        _2078 = -_46;
    }
    else
    {
        _2078 = _46;
    }
    vec4 _11214 = clip;
    _11214.y = _2078;
    _11214.z = (_11214.w - _11214.z) * 0.5;
    clip = _11214;
    gl_Position = _11214;
    entryPointParam_vertexMain_viewPos = _10965;
}

