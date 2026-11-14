#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

struct _Array_std140_vector_float_4_4
{
    vec4 data[4];
};

layout(binding = 0, std140) uniform ModPostParams_std140
{
    vec4 size;
    vec4 time;
    _Array_std140_vector_float_4_4 values;
    vec4 depth;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uTarget;
layout(binding = 0) uniform sampler2D _90;

void main()
{
    {
        if (!(any(greaterThanEqual(gl_GlobalInvocationID.xy, uvec2(p.size.xy)))))
        {
            vec4 sampled = texelFetch(_90, ivec2(gl_GlobalInvocationID.xy), 0);
            vec3 _54 = sampled.xyz;
            imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), vec4(mix(_54, vec3(dot(_54, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875))), vec3(clamp(p.values.data[0].x, 0.0, 1.0))), sampled.w));
        }
    }
}

