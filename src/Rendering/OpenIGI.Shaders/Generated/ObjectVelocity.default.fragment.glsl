#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _78 = (cClipYDown != 0);

layout(location = 1) in vec4 input_previousClip;
layout(location = 0) out vec4 entryPointParam_fragmentMain;
layout(location = 0) in vec4 input_currentClip;

void main()
{
    {
        if (input_previousClip.w < 0.001000000047497451305389404296875)
        {
            entryPointParam_fragmentMain = vec4(0.0);
        }
        else
        {
            vec2 _176 = input_currentClip.xy / vec2(input_currentClip.w);
            vec2 _190 = input_previousClip.xy / vec2(input_previousClip.w);
            entryPointParam_fragmentMain = vec4(vec2((_176.x * 0.5) + 0.5, (_176.y * (-0.5)) + 0.5) - vec2((_190.x * 0.5) + 0.5, (_190.y * (-0.5)) + 0.5), 0.0, 0.0);
        }
    }
}

