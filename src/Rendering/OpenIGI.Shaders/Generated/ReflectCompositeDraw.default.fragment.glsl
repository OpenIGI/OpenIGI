#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _57 = (cClipYDown != 0);
uniform vec2 _193_frag[12] = vec2[](vec2(-0.3260000050067901611328125, -0.4059999883174896240234375), vec2(-0.839999973773956298828125, -0.07400000095367431640625), vec2(-0.69599997997283935546875, 0.4569999873638153076171875), vec2(-0.20299999415874481201171875, 0.620999991893768310546875), vec2(0.96200001239776611328125, -0.194999992847442626953125), vec2(0.472999989986419677734375, -0.4799999892711639404296875), vec2(0.518999993801116943359375, 0.767000019550323486328125), vec2(0.185000002384185791015625, -0.89300000667572021484375), vec2(0.507000029087066650390625, 0.064000003039836883544921875), vec2(0.89600002765655517578125, 0.412000000476837158203125), vec2(-0.3219999969005584716796875, -0.933000028133392333984375), vec2(-0.791999995708465576171875, -0.597999989986419677734375));

layout(binding = 0) uniform sampler2D _364;
layout(binding = 1) uniform sampler2D _367;
layout(binding = 2) uniform sampler2D _370;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    ivec2 _122 = ivec2(gl_FragCoord.xy);
    ivec2 _125 = ivec3(_122, 0).xy;
    vec4 sampled = texelFetch(_364, _125, 0);
    vec3 reflection = sampled.xyz;
    float _133 = sampled.w;
    vec3 sum;
    if (_133 >= 2.0)
    {
        float roughness = _133 - 2.0;
        float _140 = clamp((roughness * roughness) * 28.0, 0.0, 10.0);
        if (_140 >= 1.0)
        {
            uvec2 vecSize = uvec2(textureSize(_367, 0));
            ivec2 _160 = ivec2(int(vecSize.x) - 1, int(vecSize.y) - 1);
            vec4 sampled_1 = texelFetch(_367, _125, 0);
            float _163 = sampled_1.x;
            vec3 _172 = normalize((texelFetch(_370, _125, 0).xyz * 2.0) - vec3(1.0));
            float angle = fract(52.98291778564453125 * fract(dot(vec2(_122), vec2(0.067110560834407806396484375, 0.005837149918079376220703125)))) * 6.283185482025146484375;
            float _184 = sin(angle);
            float _185 = cos(angle);
            int i = 0;
            sum = reflection;
            float weightSum = 1.0;
            float sameDepth;
            for (;;)
            {
                bool _100_ladder_break = false;
                {
                    if (!(i < 12))
                    {
                        _100_ladder_break = true;
                    }
                    else
                    {
                        ivec2 _249 = ivec3(clamp(_122 + ivec2(round(vec2((_193_frag[i].x * _185) - (_193_frag[i].y * _184), (_193_frag[i].x * _184) + (_193_frag[i].y * _185)) * _140)), ivec2(0), _160), 0).xy;
                        vec4 sampled_2 = texelFetch(_364, _249, 0);
                        if (!(sampled_2.w < 2.0))
                        {
                            if (abs(texelFetch(_367, _249, 0).x - _163) <= (0.02999999932944774627685546875 * max(_163, 9.9999999747524270787835121154785e-07)))
                            {
                                sameDepth = 1.0;
                            }
                            else
                            {
                                sameDepth = 0.0;
                            }
                            float _281 = clamp(dot(normalize((texelFetch(_370, _249, 0).xyz * 2.0) - vec3(1.0)), _172), 0.0, 1.0);
                            float weight = (((sameDepth * _281) * _281) * _281) * _281;
                            sum += (sampled_2.xyz * weight);
                            weightSum += weight;
                        }
                    }
                }
                if (_100_ladder_break)
                {
                    break;
                }
                i++;
                continue;
            }
            sum /= vec3(weightSum);
        }
        else
        {
            sum = reflection;
        }
    }
    else
    {
        sum = reflection;
    }
    entryPointParam_fragmentMain = vec4(max(sum, vec3(0.0)), 0.0);
}

