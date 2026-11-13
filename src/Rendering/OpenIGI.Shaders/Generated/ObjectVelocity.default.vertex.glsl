#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _78 = (cClipYDown != 0);

layout(binding = 0, std140) uniform ObjectVelocityPush_std140
{
    layout(row_major) mat4 current;
    vec4 previousX;
    vec4 previousY;
    vec4 previousW;
    vec4 pose;
} push;

layout(location = 0) in vec3 position;
layout(location = 1) in vec3 previousPosition;
layout(location = 0) out vec4 entryPointParam_vertexMain_currentClip;
layout(location = 1) out vec4 entryPointParam_vertexMain_previousClip;

mat4 spvWorkaroundRowMajor(mat4 wrap) { return wrap; }

void main()
{
    vec3 poseDelta = previousPosition - position;
    vec4 was = vec4(position + (poseDelta * push.pose.y), 1.0);
    vec4 _44 = vec4(position + (poseDelta * push.pose.x), 1.0) * spvWorkaroundRowMajor(push.current);
    vec4 clip = _44;
    vec4 _200 = _44;
    vec4 _201 = vec4(dot(push.previousX, was), dot(push.previousY, was), 0.0, dot(push.previousW, was));
    float _66 = _44.y;
    float _162;
    if (_78)
    {
        _162 = -_66;
    }
    else
    {
        _162 = _66;
    }
    vec4 _207 = clip;
    _207.y = _162;
    _207.z = (_207.w - _207.z) * 0.5;
    clip = _207;
    gl_Position = _207;
    entryPointParam_vertexMain_currentClip = _200;
    entryPointParam_vertexMain_previousClip = _201;
}

