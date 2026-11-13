#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
    vec4 sun;
    vec4 control;
    vec4 sizes;
    vec4 occlusion;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uTarget;
layout(binding = 0) uniform sampler2D _647;
layout(binding = 1) uniform sampler2D _650;
layout(binding = 2) uniform sampler2D _652;

void main()
{
    {
        ivec2 _82 = ivec2(gl_GlobalInvocationID.xy);
        bool inside;
        if (_82.x >= int(p.size.x))
        {
            inside = true;
        }
        else
        {
            inside = _82.y >= int(p.size.y);
        }
        if (!(inside))
        {
            vec2 uv = (vec2(_82) + vec2(0.5)) * p.size.zw;
            int mode = int(p.sun.z + 0.5);
            int t;
            float weight;
            float weightSum;
            if (mode == 0)
            {
                weight = 1000000000.0;
                t = 0;
                for (;;)
                {
                    if (!(t < 4))
                    {
                        break;
                    }
                    weight = min(weight, dot(textureLod(_647, clamp(p.sun.xy + ((vec2((float(t & 1) * 2.0) - 1.0, (float(t >> 1) * 2.0) - 1.0) * vec2(p.size.y / p.size.x, 1.0)) * 0.0350000001490116119384765625), vec2(0.0), vec2(1.0)), 0.0).xyz, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875)));
                    t++;
                    continue;
                }
                float _199 = weight * p.control.y;
                float _203 = max(weight * 4.0, 9.9999997473787516355514526367188e-05);
                int k = 0;
                vec3 bright = vec3(0.0);
                weightSum = 0.0;
                float _517;
                for (;;)
                {
                    if (!(k < 4))
                    {
                        break;
                    }
                    int _212 = k;
                    vec2 at_1 = uv + ((vec2(float(_212 & 1) - 0.5, float(_212 >> 1) - 0.5) * p.size.zw) * 0.5);
                    {
                        vec4 _535 = texelFetch(_650, ivec3(clamp(ivec2(at_1 * p.sizes.zw), ivec2(0), ivec2(p.sizes.zw) - ivec2(1)), 0).xy, 0);
                        float _536 = _535.x;
                        if (_536 <= 0.0)
                        {
                            _517 = p.occlusion.y;
                        }
                        else
                        {
                            _517 = ((2.0 * p.occlusion.x) * p.occlusion.y) / ((p.occlusion.y + p.occlusion.x) - ((1.0 - (2.0 * _536)) * (p.occlusion.y - p.occlusion.x)));
                        }
                    }
                    float _515 = smoothstep(40.0, 160.0, _517);
                    vec3 colour = textureLod(_647, at_1, 0.0).xyz;
                    float _290 = dot(colour, vec3(0.2125999927520751953125, 0.715200006961822509765625, 0.072200000286102294921875));
                    k++;
                    bright += (colour * ((min(max(_290 - _199, 0.0), _203) / max(_290, 9.9999997473787516355514526367188e-05)) * _515));
                    weightSum += _515;
                    continue;
                }
                imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), vec4(bright * (0.25 * pow(clamp(1.0 - (length((p.sun.xy - uv) * vec2(p.size.x / p.size.y, 1.0)) / max(p.control.z, 0.001000000047497451305389404296875)), 0.0, 1.0), 2.0)), weightSum * 0.25));
            }
            else
            {
                if (mode == 1)
                {
                    inside = true;
                }
                else
                {
                    inside = mode == 2;
                }
                if (inside)
                {
                    vec2 _366 = (p.sun.xy - uv) * p.sun.w;
                    t = 0;
                    vec2 at = uv;
                    weight = 1.0;
                    vec4 sum = vec4(0.0);
                    weightSum = 0.0;
                    float _25;
                    for (;;)
                    {
                        if (!(t < 16))
                        {
                            break;
                        }
                        vec2 _380 = at;
                        vec4 sampled = textureLod(_647, _380, 0.0);
                        if (all(greaterThanEqual(_380, vec2(0.0))))
                        {
                            inside = all(lessThanEqual(at, vec2(1.0)));
                        }
                        else
                        {
                            inside = false;
                        }
                        if (inside)
                        {
                            _25 = sampled.w;
                        }
                        else
                        {
                            _25 = 1.0;
                        }
                        float _408 = weight;
                        t++;
                        at += _366;
                        weight = _408 * 0.949999988079071044921875;
                        sum += (vec4(sampled.xyz, _25) * _408);
                        weightSum += _408;
                        continue;
                    }
                    imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), sum / vec4(max(weightSum, 9.9999997473787516355514526367188e-05)));
                }
                else
                {
                    vec4 sampled_1 = textureLod(_652, uv, 0.0);
                    float _582;
                    {
                        vec4 _600 = texelFetch(_650, ivec3(clamp(ivec2(uv * p.sizes.zw), ivec2(0), ivec2(p.sizes.zw) - ivec2(1)), 0).xy, 0);
                        float _601 = _600.x;
                        if (_601 <= 0.0)
                        {
                            _582 = p.occlusion.y;
                        }
                        else
                        {
                            _582 = ((2.0 * p.occlusion.x) * p.occlusion.y) / ((p.occlusion.y + p.occlusion.x) - ((1.0 - (2.0 * _601)) * (p.occlusion.y - p.occlusion.x)));
                        }
                    }
                    float _580 = 1.0 - exp(_582 * (-0.012500000186264514923095703125));
                    imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), vec4(textureLod(_647, uv, 0.0).xyz + (sampled_1.xyz * ((p.control.x * p.control.w) * _580)), clamp(1.0 - ((((p.occlusion.z * p.control.w) * pow(clamp(1.0 - (length((p.sun.xy - uv) * vec2(p.size.x / p.size.y, 1.0)) * 0.89999997615814208984375), 0.0, 1.0), 1.5)) * _580) * clamp(1.0 - sampled_1.w, 0.0, 1.0)), 0.0, 1.0)));
                }
            }
        }
    }
}

