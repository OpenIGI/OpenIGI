#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
} p;

layout(binding = 0, r8) uniform restrict writeonly image2D uBlend;
layout(binding = 0) uniform sampler2D _402;

shared float sMedian[100];

void main()
{
    {
        ivec2 _35 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 _47 = ivec2(p.size.xy);
        ivec2 _55 = (ivec2(gl_WorkGroupID.xy) * ivec2(8)) - ivec2(1);
        uint i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 100u))
            {
                break;
            }
            int _67 = int(i);
            ivec2 _79 = _55 + ivec2(_67 - 10 * (_67 / 10), _67 / 10);
            ivec2 _301 = _47 - ivec2(1);
            vec4 _306 = texelFetch(_402, ivec3(clamp(_79 + ivec2(-1), ivec2(0), _301), 0).xy, 0);
            float _308 = _306.x;
            vec4 _314 = texelFetch(_402, ivec3(clamp(_79 + ivec2(0, -1), ivec2(0), _301), 0).xy, 0);
            float _316 = _314.x;
            float _329 = max(min(_308, _316), min(max(_308, _316), texelFetch(_402, ivec3(clamp(_79 + ivec2(1, -1), ivec2(0), _301), 0).xy, 0).x));
            vec4 _335 = texelFetch(_402, ivec3(clamp(_79 + ivec2(-1, 0), ivec2(0), _301), 0).xy, 0);
            float _337 = _335.x;
            vec4 _342 = texelFetch(_402, ivec3(clamp(_79, ivec2(0), _301), 0).xy, 0);
            float _344 = _342.x;
            float _357 = max(min(_337, _344), min(max(_337, _344), texelFetch(_402, ivec3(clamp(_79 + ivec2(1, 0), ivec2(0), _301), 0).xy, 0).x));
            vec4 _363 = texelFetch(_402, ivec3(clamp(_79 + ivec2(-1, 1), ivec2(0), _301), 0).xy, 0);
            float _365 = _363.x;
            vec4 _371 = texelFetch(_402, ivec3(clamp(_79 + ivec2(0, 1), ivec2(0), _301), 0).xy, 0);
            float _373 = _371.x;
            sMedian[i] = max(min(_329, _357), min(max(_329, _357), max(min(_365, _373), min(max(_365, _373), texelFetch(_402, ivec3(clamp(_79 + ivec2(1), ivec2(0), _301), 0).xy, 0).x))));
            i += 64u;
            continue;
        }
        barrier();
        bool _10;
        if (_35.x >= _47.x)
        {
            _10 = true;
        }
        else
        {
            _10 = _35.y >= _47.y;
        }
        if (!(_10))
        {
            ivec2 _236 = ivec2(gl_LocalInvocationID.xy) + ivec2(1);
            int _238 = _236.y;
            int _241 = _236.x;
            int _242 = ((_238 + (-1)) * 10) + _241;
            int _256 = (_238 * 10) + _241;
            int _270 = ((_238 + 1) * 10) + _241;
            imageStore(uBlend, ivec2(gl_GlobalInvocationID.xy), vec4(min(min(min(min(min(min(min(min(min(1.0, sMedian[_242 + (-1)]), sMedian[_242]), sMedian[_242 + 1]), sMedian[_256 + (-1)]), sMedian[_256]), sMedian[_256 + 1]), sMedian[_270 + (-1)]), sMedian[_270]), sMedian[_270 + 1]), 0.0, 0.0, 0.0));
        }
    }
}

