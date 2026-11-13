#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 target;
    vec4 control;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uTarget;
layout(binding = 0) uniform sampler2D _154;

void main()
{
    {
        ivec2 _29 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 _40 = ivec2(p.target.xy);
        bool _7;
        if (_29.x >= _40.x)
        {
            _7 = true;
        }
        else
        {
            _7 = _29.y >= _40.y;
        }
        if (!(_7))
        {
            vec2 uv = (vec2(_29) + vec2(0.5)) * p.target.zw;
            vec2 offset = p.target.zw * 0.5;
            float _66 = offset.x;
            float _67 = -_66;
            float _68 = offset.y;
            float _69 = -_68;
            vec3 _115 = max((((textureLod(_154, uv + vec2(_67, _69), 0.0).xyz + textureLod(_154, uv + vec2(_66, _69), 0.0).xyz) + textureLod(_154, uv + vec2(_67, _68), 0.0).xyz) + textureLod(_154, uv + offset, 0.0).xyz) * 0.25, vec3(0.0));
            vec3 colour;
            if (p.control.x > 0.0)
            {
                colour = _115 * clamp((dot(_115, vec3(0.300000011920928955078125, 0.589999973773956298828125, 0.10999999940395355224609375)) - p.control.x) * 0.5, 0.0, 1.0);
            }
            else
            {
                colour = _115;
            }
            imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), vec4(colour, 1.0));
        }
    }
}

