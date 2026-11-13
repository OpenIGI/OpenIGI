#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
    vec4 unproject;
    vec4 control;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uFlat;
layout(binding = 1, rgba16f) uniform restrict writeonly image2D uTile;
layout(binding = 0) uniform sampler2D _355;
layout(binding = 1) uniform sampler2D _357;

shared vec2 sFastest[256];
shared vec2 sSlowest[256];

void main()
{
    ivec2 _55 = ivec2(gl_GlobalInvocationID.xy);
    bool inFrame;
    if (_55.x < int(p.size.x))
    {
        inFrame = _55.y < int(p.size.y);
    }
    else
    {
        inFrame = false;
    }
    vec2 velocity;
    float viewDepth;
    if (inFrame)
    {
        vec2 uv = (vec2(_55) + vec2(0.5)) * p.size.zw;
        velocity = textureLod(_355, uv, 0.0).xy * p.control.xy;
        viewDepth = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * textureLod(_357, uv, 0.0).x)) * (p.unproject.w - p.unproject.z)));
    }
    else
    {
        velocity = vec2(0.0);
        viewDepth = 0.0;
    }
    float _325 = length(velocity);
    float _322;
    if (_325 > 0.0)
    {
        _322 = atan(velocity.y, velocity.x);
    }
    else
    {
        _322 = 0.0;
    }
    vec2 _334 = vec2(_325, _322);
    _334.x = min(_334.x, p.control.z);
    vec2 polar = _334;
    if (inFrame)
    {
        imageStore(uFlat, ivec2(gl_GlobalInvocationID.xy), vec4(polar.x, (polar.y * 0.15915493667125701904296875) + 0.5, viewDepth, 0.0));
    }
    uint lane = (gl_LocalInvocationID.y * 16u) + gl_LocalInvocationID.x;
    sFastest[lane] = polar;
    sSlowest[lane] = polar;
    barrier();
    uint stride = 128u;
    vec2 _18;
    for (;;)
    {
        if (!(stride > 0u))
        {
            break;
        }
        if (lane < stride)
        {
            uint _240 = lane + stride;
            if (sFastest[lane].x > sFastest[_240].x)
            {
                velocity = sFastest[lane];
            }
            else
            {
                velocity = sFastest[_240];
            }
            sFastest[lane] = velocity;
            if (sSlowest[lane].x > sSlowest[_240].x)
            {
                _18 = sSlowest[_240];
            }
            else
            {
                _18 = sSlowest[lane];
            }
            sSlowest[lane] = _18;
        }
        barrier();
        stride = stride >> 1u;
        continue;
    }
    if (lane == 0u)
    {
        imageStore(uTile, ivec2(gl_WorkGroupID.xy), vec4(vec2(cos(sSlowest[0].y), sin(sSlowest[0].y)) * sSlowest[0].x, vec2(cos(sFastest[0].y), sin(sFastest[0].y)) * sFastest[0].x));
    }
}

