#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _57 = (cClipYDown != 0);

struct _Array_std140_vector_float_4_7
{
    vec4 data[7];
};

layout(binding = 0, std140) uniform ScopeGlintParams_std140
{
    vec4 frame;
    _Array_std140_vector_float_4_7 glints;
} p;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) in vec2 input_uv;
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    vec2 _123 = input_uv * p.frame.xy;
    int _127 = min(int(p.frame.w), 7);
    int i = 0;
    vec3 light = vec3(0.0);
    bool _88;
    bool _89;
    for (;;)
    {
        bool _92_ladder_break = false;
        {
            if (!(i < _127))
            {
                _92_ladder_break = true;
            }
            else
            {
                if (p.glints.data[i].z <= 0.0)
                {
                    _88 = true;
                }
                else
                {
                    _88 = p.glints.data[i].w <= 0.0;
                }
                if (!(_88))
                {
                    vec2 d = (_123 - p.glints.data[i].xy) / vec2(p.glints.data[i].z);
                    if (abs(d.x) > 6.0)
                    {
                        _89 = true;
                    }
                    else
                    {
                        _89 = abs(d.y) > 4.0;
                    }
                    if (!(_89))
                    {
                        float _179 = float(i);
                        float _320 = dot(d, d);
                        float _324 = (p.frame.z * 3.099999904632568359375) + (_179 * 11.326000213623046875);
                        float _366 = floor(_324);
                        float _367 = fract(_324);
                        float _329 = (p.frame.z * 11.0) + (_179 * 1.618000030517578125);
                        float _384 = floor(_329);
                        float _385 = fract(_329);
                        vec3 light_1 = light + ((((vec3(1.0, 0.949999988079071044921875, 0.86000001430511474609375) * (exp(_320 * (-70.0)) * 3.0)) + (vec3(1.0, 0.949999988079071044921875, 0.86000001430511474609375) * ((exp(_320 * (-2.7999999523162841796875)) * 0.319999992847442626953125) + (exp(sqrt(_320) * (-1.2999999523162841796875)) * 0.100000001490116119384765625)))) + (vec3(0.920000016689300537109375, 0.949999988079071044921875, 1.0) * ((exp(abs(d.y) * (-40.0)) * exp(abs(d.x) * (-1.5))) * 0.300000011920928955078125))) * (p.glints.data[i].w * ((0.7200000286102294921875 + (0.20000000298023223876953125 * mix(fract(sin(_366 * 12.98980045318603515625) * 43758.546875), fract(sin((_366 + 1.0) * 12.98980045318603515625) * 43758.546875), (_367 * _367) * (3.0 - (2.0 * _367))))) + (0.07999999821186065673828125 * mix(fract(sin(_384 * 12.98980045318603515625) * 43758.546875), fract(sin((_384 + 1.0) * 12.98980045318603515625) * 43758.546875), (_385 * _385) * (3.0 - (2.0 * _385)))))));
                        light = light_1;
                    }
                }
            }
        }
        if (_92_ladder_break)
        {
            break;
        }
        i++;
        continue;
    }
    entryPointParam_fragmentMain = vec4(light, 0.0);
}

