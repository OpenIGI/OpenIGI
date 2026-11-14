#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
    vec4 range;
} p;

layout(binding = 0, std430) restrict buffer RWStructuredBuffer
{
    uint _member0[];
} uBins;

layout(binding = 0) uniform sampler2D _211;
layout(binding = 1) uniform sampler2D _214;

shared uint sHistogram[64];

void main()
{
    atomicExchange(sHistogram[gl_LocalInvocationIndex], 0u);
    barrier();
    ivec2 _57 = ivec2(gl_GlobalInvocationID.xy);
    bool _7;
    if (_57.x < int(p.size.x))
    {
        _7 = _57.y < int(p.size.y);
    }
    else
    {
        _7 = false;
    }
    if (_7)
    {
        uvec2 vecSize = uvec2(textureSize(_214, 0));
        if (texelFetch(_214, ivec3(ivec2(((vec2(_57) + vec2(0.5)) * vec2(float(vecSize.x), float(vecSize.y))) / p.size.xy), 0).xy, 0).w >= 0.5)
        {
            float _123 = dot(texelFetch(_211, ivec3(_57, 0).xy, 0).xyz, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875));
            float position = clamp((p.range.x * log2(max(_123, p.range.z))) + p.range.y, 0.0, 1.0) * 63.0;
            uint low = uint(position);
            float _150 = fract(position);
            if (low == 0u)
            {
                _7 = _123 <= 1.0000000116860974230803549289703e-07;
            }
            else
            {
                _7 = false;
            }
            float lower;
            if (_7)
            {
                lower = 0.0;
            }
            else
            {
                lower = 1.0 - _150;
            }
            uint lowCount = uint((lower * p.range.w) + 0.5);
            uint highCount = uint((_150 * p.range.w) + 0.5);
            if (lowCount > 0u)
            {
                uint _180 = atomicAdd(sHistogram[low], lowCount);
            }
            if (highCount > 0u)
            {
                uint _185 = atomicAdd(sHistogram[min((low + 1u), 63u)], highCount);
            }
        }
    }
    barrier();
    uint total = atomicAdd(sHistogram[gl_LocalInvocationIndex], 0u);
    if (total > 0u)
    {
        uint _202 = atomicAdd(uBins._member0[gl_LocalInvocationIndex], total);
    }
}

