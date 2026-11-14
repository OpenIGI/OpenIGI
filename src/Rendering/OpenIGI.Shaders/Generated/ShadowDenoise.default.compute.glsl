#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

uniform vec2 _220_comp[8] = vec2[](vec2(0.939999997615814208984375, 0.34200000762939453125), vec2(-0.964999973773956298828125, -0.2619999945163726806640625), vec2(0.13500000536441802978515625, -0.4289999902248382568359375), vec2(-0.1369999945163726806640625, 0.4289999902248382568359375), vec2(0.3490000069141387939453125, -0.936999976634979248046875), vec2(-0.273999989032745361328125, 0.96200001239776611328125), vec2(-0.407000005245208740234375, -0.1920000016689300537109375), vec2(0.435000002384185791015625, 0.115999996662139892578125));

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
    vec4 unproject;
    vec4 sun;
} p;

layout(binding = 0, r8) uniform restrict writeonly image2D uShadow;
layout(binding = 0) uniform sampler2D _379;
layout(binding = 2) uniform sampler2D _382;
layout(binding = 5) uniform sampler2D _385;
layout(binding = 4) uniform sampler2D _388;
layout(binding = 1) uniform sampler2D _390;
layout(binding = 3) uniform sampler2D _392;
layout(binding = 6) uniform sampler2D _394;

void main()
{
    {
        ivec2 _57 = ivec2(gl_GlobalInvocationID.xy);
        ivec2 _67 = ivec2(p.size.xy);
        int _68 = _57.x;
        bool _7;
        if (_68 >= _67.x)
        {
            _7 = true;
        }
        else
        {
            _7 = _57.y >= _67.y;
        }
        if (!(_7))
        {
            ivec2 _85 = ivec3(_57, 0).xy;
            vec4 sampled = texelFetch(_379, _85, 0);
            vec4 sampled_1 = texelFetch(_382, _85, 0);
            float centreDepth = sampled_1.x;
            vec3 _109 = normalize((texelFetch(_385, _85, 0).xyz * 2.0) - vec3(1.0));
            if (centreDepth <= 0.0)
            {
                _7 = true;
            }
            else
            {
                _7 = sampled.y <= 0.0;
            }
            if (_7)
            {
                imageStore(uShadow, ivec2(gl_GlobalInvocationID.xy), vec4(sampled.x));
            }
            else
            {
                float _158 = max(((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * centreDepth)) * (p.unproject.w - p.unproject.z))), 9.9999997473787516355514526367188e-05);
                float pixelRadius = (((p.sun.x * sampled.y) * p.unproject.x) * (0.5 * p.size.x)) / _158;
                float _184 = mix(pixelRadius, max(2.0, 0.3499999940395355224609375 * pixelRadius), smoothstep(0.25, 0.89999997615814208984375, clamp(texelFetch(_388, _85, 0).z * p.sun.y, 0.0, 1.0)));
                if (_184 < 0.5)
                {
                    imageStore(uShadow, ivec2(gl_GlobalInvocationID.xy), vec4(sampled.x));
                }
                else
                {
                    float _193 = min(_184, 32.0);
                    vec2 _204 = (vec2(1.0) - (vec2(float(_68 & 1), float(_57.y & 1)) * 2.0)) * 0.319999992847442626953125;
                    vec2 _212 = (vec2(_57) + vec2(0.5)) * p.size.zw;
                    int i = 0;
                    float visibility = sampled.x;
                    float weightSum = 1.0;
                    float weight;
                    for (;;)
                    {
                        if (!(i < 8))
                        {
                            break;
                        }
                        vec2 _263 = clamp(_212 + (((_220_comp[i] + _204) * _193) * p.size.zw), p.size.zw * 0.5, vec2(1.0) - (p.size.zw * 0.5));
                        vec4 sampled_2 = textureLod(_392, _263, 0.0);
                        float tapDepth = sampled_2.x;
                        if (tapDepth > 0.0)
                        {
                            _7 = abs((((2.0 * p.unproject.z) * p.unproject.w) / ((p.unproject.w + p.unproject.z) - ((1.0 - (2.0 * tapDepth)) * (p.unproject.w - p.unproject.z)))) - _158) < (_158 * 0.0199999995529651641845703125);
                        }
                        else
                        {
                            _7 = false;
                        }
                        if (_7)
                        {
                            weight = 1.0;
                        }
                        else
                        {
                            weight = 0.0;
                        }
                        float _313 = clamp(dot(normalize((textureLod(_394, _263, 0.0).xyz * 2.0) - vec3(1.0)), _109), 0.0, 1.0);
                        float weight_1 = weight * (((_313 * _313) * _313) * _313);
                        i++;
                        visibility += (textureLod(_390, _263, 0.0).x * weight_1);
                        weightSum += weight_1;
                        continue;
                    }
                    imageStore(uShadow, ivec2(gl_GlobalInvocationID.xy), vec4(visibility / weightSum));
                }
            }
        }
    }
}

