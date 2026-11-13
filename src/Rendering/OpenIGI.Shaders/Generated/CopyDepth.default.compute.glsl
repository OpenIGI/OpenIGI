#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, r32f) uniform restrict writeonly image2D uPreviousDepth;
layout(binding = 0) uniform sampler2D _80;

void main()
{
    {
        uvec2 vecSize = uvec2(imageSize(uPreviousDepth));
        uint height = vecSize.y;
        bool _11;
        if (gl_GlobalInvocationID.x >= vecSize.x)
        {
            _11 = true;
        }
        else
        {
            _11 = gl_GlobalInvocationID.y >= height;
        }
        if (!(_11))
        {
            imageStore(uPreviousDepth, ivec2(gl_GlobalInvocationID.xy), vec4(texelFetch(_80, ivec3(ivec2(gl_GlobalInvocationID.xy), 0).xy, 0).x, 0.0, 0.0, 0.0));
        }
    }
}

