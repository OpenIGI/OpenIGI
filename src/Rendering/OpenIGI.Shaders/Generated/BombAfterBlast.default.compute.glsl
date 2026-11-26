#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform AfterBlastParams_std140
{
    vec4 size;
    vec4 depth;
    vec4 unproject;
    vec4 blast;
    vec4 shell;
    vec4 reach;
    vec4 haze;
    vec4 tuning;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uTarget;
layout(binding = 0) uniform sampler2D _556;
layout(binding = 1) uniform sampler2D _559;

void main()
{
    {
        if (!(any(greaterThanEqual(gl_GlobalInvocationID.xy, uvec2(p.size.xy)))))
        {
            ivec2 _63 = ivec2(gl_GlobalInvocationID.xy);
            vec2 _64 = vec2(_63);
            vec2 uv = (_64 + vec2(0.5)) * p.size.zw;
            vec3 _435 = texelFetch(_556, ivec3(clamp(_63, ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz;
            vec4 _456 = texelFetch(_559, ivec3(clamp(ivec2((vec2(_63) + vec2(0.5)) * p.depth.zw), ivec2(0), ivec2(p.depth.xy) - ivec2(1)), 0).xy, 0);
            float _457 = _456.x;
            vec3 surface;
            if (_457 > 0.0)
            {
                float _486 = ((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * _457)) * (p.unproject.w - p.unproject.z)));
                vec2 _463 = (uv * 2.0) - vec2(1.0);
                surface = vec3((_463.x * _486) / p.unproject.x, ((-_463.y) * _486) / p.unproject.y, _486);
            }
            else
            {
                surface = vec3(0.0, 0.0, p.unproject.w * 4.0);
            }
            float _190 = distance(surface, p.blast.xyz);
            float _192 = length(surface);
            float _202 = smoothstep(p.blast.w - p.shell.y, p.blast.w, _190);
            float shell = (((smoothstep(p.blast.w + p.shell.x, p.blast.w, _190) * _202) * _202) * smoothstep(p.reach.x, p.reach.y, _192)) * smoothstep(p.reach.w, p.reach.z, _192);
            vec3 sum;
            if (shell > 0.001000000047497451305389404296875)
            {
                vec3 _487;
                if (p.blast.z > 0.00999999977648258209228515625)
                {
                    _487 = p.blast.xyz;
                }
                else
                {
                    _487 = -p.blast.xyz;
                }
                float _498 = max(_487.z, 0.00999999977648258209228515625);
                vec2 toBomb = ((vec2((_487.x * p.unproject.x) / _498, ((-_487.y) * p.unproject.y) / _498) * 0.5) + vec2(0.5)) - uv;
                float _265 = length(toBomb);
                vec2 _12;
                if (_265 > 9.9999997473787516355514526367188e-06)
                {
                    _12 = toBomb / vec2(_265);
                }
                else
                {
                    _12 = vec2(0.0);
                }
                float _281 = (shell * p.shell.z) * min(_265 * 2.0, 1.0);
                int _287 = max(int(p.tuning.z), 1);
                vec3 _521 = fract((_64 + vec2(p.tuning.y * 7.309999942779541015625)).xyx * 0.103100001811981201171875);
                vec3 _527 = _521 + vec3(dot(_521, _521.yzx + vec3(33.3300018310546875)));
                float _317 = fract((_527.x + _527.y) * _527.z) - 0.5;
                int i = 0;
                sum = vec3(0.0);
                for (;;)
                {
                    if (!(i < _287))
                    {
                        break;
                    }
                    int _324 = i;
                    float _327 = float(_287);
                    i++;
                    sum += texelFetch(_556, ivec3(clamp(ivec2(clamp(mix(uv + ((_12 * _281) * ((((float(_324) + 0.5) / _327) - 0.5) + (_317 / _327))), vec2(0.5), vec2(0.0199999995529651641845703125 * shell)), vec2(0.0), vec2(1.0)) * p.size.xy), ivec2(0), ivec2(p.size.xy) - ivec2(1)), 0).xy, 0).xyz;
                    continue;
                }
                vec3 streaked = sum / vec3(float(_287));
                sum = mix(_435, streaked + ((streaked * pow(clamp(dot(streaked, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)) * p.tuning.x, 0.0, 1.0), 6.0)) * 1.5), vec3(clamp(shell * p.shell.w, 0.0, 1.0)));
            }
            else
            {
                sum = _435;
            }
            float _394 = smoothstep(p.blast.w, p.blast.w - p.reach.w, length(p.blast.xyz));
            imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), vec4(mix(sum, p.haze.xyz, vec3((_394 * _394) * p.haze.w)), 1.0));
        }
    }
}

