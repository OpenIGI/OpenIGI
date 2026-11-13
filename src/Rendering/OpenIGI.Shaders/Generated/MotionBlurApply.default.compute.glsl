#version 430
layout(local_size_x = 16, local_size_y = 16, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
    vec4 control;
} p;

layout(binding = 0, rgba16f) uniform restrict writeonly image2D uTarget;
layout(binding = 3) uniform sampler2D _603;
layout(binding = 0) uniform sampler2D _605;
layout(binding = 1) uniform sampler2D _607;
layout(binding = 2) uniform sampler2D _609;

shared uint sGroupSamples;

void main()
{
    {
        ivec2 _71 = ivec2(gl_GlobalInvocationID.xy);
        vec2 _73 = vec2(_71);
        float _460 = fract(52.98291778564453125 * fract((0.067110560834407806396484375 * _73.x) + (0.005837149918079376220703125 * _73.y)));
        vec2 _467 = _73 + vec2(12.0780429840087890625, 9.1174449920654296875);
        vec4 sampled = textureLod(_603, clamp(((_73 * vec2(0.0625)) + ((vec2(_460, fract(52.98291778564453125 * fract((0.067110560834407806396484375 * _467.x) + (0.005837149918079376220703125 * _467.y)))) - vec2(0.5)) * 0.5)) / p.control.xy, vec2(0.0), vec2(1.0) - (vec2(0.5) / p.control.xy)), 0.0);
        vec2 fastest = sampled.zw;
        vec2 slowest = sampled.xy;
        float _151 = length(fastest);
        if (gl_LocalInvocationIndex == 0u)
        {
            atomicExchange(sGroupSamples, 0u);
        }
        barrier();
        uint _174 = atomicMax(sGroupSamples, uint(clamp(4.0 * ceil(_151 * 0.25), 4.0, p.control.z)));
        barrier();
        uint samples = atomicAdd(sGroupSamples, 0u);
        uint halfSamples = samples / 2u;
        bool _7;
        if (_71.x >= int(p.size.x))
        {
            _7 = true;
        }
        else
        {
            _7 = _71.y >= int(p.size.y);
        }
        if (!(_7))
        {
            vec2 uv = (_73 + vec2(0.5)) * p.size.zw;
            vec4 sampled_1 = textureLod(_605, uv, 0.0);
            if (_151 < 0.5)
            {
                imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), sampled_1);
            }
            else
            {
                vec2 _222 = fastest * p.size.zw;
                float _223 = float(samples);
                float _226 = clamp((_151 / _223) - 1.0, 0.0, 1.0);
                float sides = float(halfSamples);
                uint s;
                vec4 colour;
                if (dot(slowest, slowest) > ((0.4000000059604644775390625 * _151) * _151))
                {
                    s = 0u;
                    colour = vec4(0.0);
                    for (;;)
                    {
                        if (!(s < halfSamples))
                        {
                            break;
                        }
                        uint _240 = s;
                        float _241 = float(_240);
                        vec2 _249 = clamp(uv + (_222 * ((_241 + _460) / sides)), vec2(0.0), vec2(1.0));
                        vec2 _277 = clamp(uv - (_222 * (((_241 + 1.0) - _460) / sides)), vec2(0.0), vec2(1.0));
                        s++;
                        colour = (colour + mix(textureLod(_605, _249, 0.0), textureLod(_607, _249, 0.0), vec4(_226))) + mix(textureLod(_605, _277, 0.0), textureLod(_607, _277, 0.0), vec4(_226));
                        continue;
                    }
                    imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), colour * (1.0 / _223));
                }
                else
                {
                    vec4 sampled_2 = textureLod(_609, uv, 0.0);
                    float _303 = sides / _151;
                    s = 0u;
                    colour = vec4(0.0);
                    float weightSum = 0.0;
                    float usedAhead;
                    bool _18;
                    float usedBehind;
                    for (;;)
                    {
                        if (!(s < halfSamples))
                        {
                            break;
                        }
                        float _312 = float(s);
                        vec2 _320 = clamp(uv + (_222 * ((_312 + _460) / sides)), vec2(0.0), vec2(1.0));
                        vec2 _325 = clamp(uv - (_222 * (((_312 + 1.0) - _460) / sides)), vec2(0.0), vec2(1.0));
                        vec4 sampled_3 = textureLod(_609, _320, 0.0);
                        vec4 sampled_4 = textureLod(_609, _325, 0.0);
                        float _539 = sampled_3.z - sampled_2.z;
                        float _550 = (clamp(0.5 + _539, 0.0, 1.0) * clamp((_303 * sampled_2.x) - max(_312 + (-0.5), 0.0), 0.0, 1.0)) + (clamp(0.5 - _539, 0.0, 1.0) * clamp((_303 * sampled_3.x) - max(_312 + (-0.5), 0.0), 0.0, 1.0));
                        float _569 = sampled_4.z - sampled_2.z;
                        float _580 = (clamp(0.5 + _569, 0.0, 1.0) * clamp((_303 * sampled_2.x) - max(_312 + (-0.5), 0.0), 0.0, 1.0)) + (clamp(0.5 - _569, 0.0, 1.0) * clamp((_303 * sampled_4.x) - max(_312 + (-0.5), 0.0), 0.0, 1.0));
                        bool furtherAhead = sampled_3.z > sampled_4.z;
                        bool slowerAhead = sampled_3.x < sampled_4.x;
                        if (furtherAhead)
                        {
                            _7 = slowerAhead;
                        }
                        else
                        {
                            _7 = false;
                        }
                        if (_7)
                        {
                            usedAhead = _580;
                        }
                        else
                        {
                            usedAhead = _550;
                        }
                        if (furtherAhead)
                        {
                            _18 = true;
                        }
                        else
                        {
                            _18 = slowerAhead;
                        }
                        if (_18)
                        {
                            usedBehind = _580;
                        }
                        else
                        {
                            usedBehind = _550;
                        }
                        s++;
                        colour += ((mix(textureLod(_605, _320, 0.0), textureLod(_607, _320, 0.0), vec4(_226)) * usedAhead) + (mix(textureLod(_605, _325, 0.0), textureLod(_607, _325, 0.0), vec4(_226)) * usedBehind));
                        weightSum += (usedAhead + usedBehind);
                        continue;
                    }
                    float normalise = 0.5 / sides;
                    imageStore(uTarget, ivec2(gl_GlobalInvocationID.xy), (colour * normalise) + (sampled_1 * max(1.0 - (weightSum * normalise), 0.0)));
                }
            }
        }
    }
}

