#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
    vec4 control;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uTarget;
layout(binding = 0) uniform sampler2D _142;

void main()
{
    {
        ivec2 _22 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 _34 = ivec2(p.size.xy);
        bool _7;
        if (_22.x >= _34.x)
        {
            _7 = true;
        }
        else
        {
            _7 = _22.y >= _34.y;
        }
        if (!(_7))
        {
            vec2 uv = (vec2(_22) + vec2(0.5)) * p.size.zw;
            vec2 step = p.control.xy * p.size.zw;
            vec2 _81 = step * 1.384615421295166015625;
            vec2 _102 = step * 3.23076915740966796875;
            imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), vec4(max(((((textureLod(_142, uv, 0.0).xyz * 0.2270270287990570068359375) + (textureLod(_142, uv + _81, 0.0).xyz * 0.3162162303924560546875)) + (textureLod(_142, uv - _81, 0.0).xyz * 0.3162162303924560546875)) + (textureLod(_142, uv + _102, 0.0).xyz * 0.0702702701091766357421875)) + (textureLod(_142, uv - _102, 0.0).xyz * 0.0702702701091766357421875), vec3(0.0)), 1.0));
        }
    }
}

