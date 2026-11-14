#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _57 = (cClipYDown != 0);

layout(binding = 0, std140) uniform SilhouetteParams_std140
{
    vec4 frame;
    vec4 vignettes;
    vec4 fill0;
    vec4 outline0;
    vec4 fill1;
    vec4 outline1;
    vec4 fill2;
    vec4 outline2;
} p;

layout(binding = 0) uniform sampler2D _822;

#ifdef GL_ARB_shader_draw_parameters
#define SPIRV_Cross_BaseVertex gl_BaseVertexARB
#else
uniform int SPIRV_Cross_BaseVertex;
#endif
layout(location = 0) out vec4 entryPointParam_fragmentMain;

void main()
{
    {
        vec2 _547 = ((gl_FragCoord.xy - (p.frame.xy * 0.5)) / vec2(p.frame.y)) * p.vignettes.xy;
        float _548 = _547.x;
        float _552 = _547.y;
        float _561 = (((16.0 * clamp(0.5 + _548, 0.0, 1.0)) * clamp(0.5 + _552, 0.0, 1.0)) * clamp(0.5 - _548, 0.0, 1.0)) * clamp(0.5 - _552, 0.0, 1.0);
        vec2 _573 = ((gl_FragCoord.xy - (p.frame.xy * 0.5)) / vec2(p.frame.y)) * p.vignettes.zw;
        float _574 = _573.x;
        float _578 = _573.y;
        float _587 = (((16.0 * clamp(0.5 + _574, 0.0, 1.0)) * clamp(0.5 + _578, 0.0, 1.0)) * clamp(0.5 - _574, 0.0, 1.0)) * clamp(0.5 - _578, 0.0, 1.0);
        if (max(_561, _587) <= 0.0)
        {
            entryPointParam_fragmentMain = vec4(0.0);
        }
        else
        {
            ivec2 _193 = ivec2(gl_FragCoord.xy);
            vec4 _589;
            {
                bool _590;
                if (any(lessThan(_193, ivec2(0))))
                {
                    _590 = true;
                }
                else
                {
                    _590 = any(greaterThanEqual(_193, ivec2(p.frame.xy)));
                }
                if (_590)
                {
                    _589 = vec4(0.0);
                }
                else
                {
                    _589 = texelFetch(_822, ivec3(_193, 0).xy, 0);
                }
            }
            bvec3 inside = greaterThan(_589.xyz, vec3(0.5));
            vec3 n = vec3(0.0);
            vec3 least = vec3(100.0);
            int dy = 2;
            vec2 s0 = vec2(0.0);
            vec2 s1 = vec2(0.0);
            vec2 s2 = vec2(0.0);
            bool _96;
            vec2 s0_2;
            vec2 s1_2;
            vec2 s2_2;
            vec4 _616;
            bool _617;
            for (;;)
            {
                if (!(dy >= (-2)))
                {
                    break;
                }
                int dx = 2;
                vec2 s0_1 = s0;
                vec2 s1_1 = s1;
                vec2 s2_1 = s2;
                for (;;)
                {
                    bool _110_ladder_break = false;
                    {
                        if (!(dx >= (-2)))
                        {
                            _110_ladder_break = true;
                        }
                        else
                        {
                            if (dx == 0)
                            {
                                _96 = dy == 0;
                            }
                            else
                            {
                                _96 = false;
                            }
                            if (!(_96))
                            {
                                ivec2 _280 = _193 + ivec2(dx, dy);
                                {
                                    if (any(lessThan(_280, ivec2(0))))
                                    {
                                        _617 = true;
                                    }
                                    else
                                    {
                                        _617 = any(greaterThanEqual(_280, ivec2(p.frame.xy)));
                                    }
                                    if (_617)
                                    {
                                        _616 = vec4(0.0);
                                    }
                                    else
                                    {
                                        _616 = texelFetch(_822, ivec3(_280, 0).xy, 0);
                                    }
                                }
                                bvec3 there = greaterThan(_616.xyz, vec3(0.5));
                                vec2 offset = vec2(float(dx), float(dy));
                                float _290 = dot(offset, offset);
                                if (there.x != inside.x)
                                {
                                    vec3 _802 = n;
                                    _802.x = _802.x + 1.0;
                                    n = _802;
                                    vec3 _302 = least;
                                    _302.x = min(_302.x, _290);
                                    least = _302;
                                    s0_2 = s0_1 + offset;
                                }
                                else
                                {
                                    s0_2 = s0_1;
                                }
                                if (there.y != inside.y)
                                {
                                    vec3 _807 = n;
                                    _807.y = _807.y + 1.0;
                                    n = _807;
                                    vec3 _322 = least;
                                    _322.y = min(_322.y, _290);
                                    least = _322;
                                    s1_2 = s1_1 + offset;
                                }
                                else
                                {
                                    s1_2 = s1_1;
                                }
                                if (there.z != inside.z)
                                {
                                    vec3 _812 = n;
                                    _812.z = _812.z + 1.0;
                                    n = _812;
                                    vec3 _342 = least;
                                    _342.z = min(_342.z, _290);
                                    least = _342;
                                    s2_2 = s2_1 + offset;
                                }
                                else
                                {
                                    s2_2 = s2_1;
                                }
                                s0_1 = s0_2;
                                s1_1 = s1_2;
                                s2_1 = s2_2;
                            }
                        }
                    }
                    if (_110_ladder_break)
                    {
                        break;
                    }
                    dx--;
                    continue;
                }
                dy--;
                s0 = s0_1;
                s1 = s1_1;
                s2 = s2_1;
                continue;
            }
            vec4 result = vec4(0.0);
            float _642;
            if (n.z > 0.0)
            {
                _642 = length(s2) / n.z;
            }
            else
            {
                _642 = 100.0;
            }
            float _657 = p.frame.z * max((0.89999997615814208984375 * max(_642, sqrt(least.z))) - 1.0, 0.0);
            float _662 = p.outline2.w * exp(((-4.0) * _657) * _657);
            float _643;
            if (inside.z)
            {
                _643 = p.fill2.w;
            }
            else
            {
                _643 = 0.0;
            }
            vec3 _669 = p.fill2.xyz * _643;
            float _680 = (1.0 - ((1.0 - _643) * (1.0 - _662))) * _587;
            float _683 = 1.0 - _680;
            result = vec4(((_669 + ((p.outline2.xyz - _669) * _662)) * _587) + (result.xyz * _683), _680 + (result.w * _683));
            float _691;
            if (n.y > 0.0)
            {
                _691 = length(s1) / n.y;
            }
            else
            {
                _691 = 100.0;
            }
            float _706 = p.frame.z * max((0.89999997615814208984375 * max(_691, sqrt(least.y))) - 1.0, 0.0);
            float _711 = p.outline1.w * exp(((-4.0) * _706) * _706);
            float _692;
            if (inside.y)
            {
                _692 = p.fill1.w;
            }
            else
            {
                _692 = 0.0;
            }
            vec3 _718 = p.fill1.xyz * _692;
            float _729 = (1.0 - ((1.0 - _692) * (1.0 - _711))) * _587;
            float _732 = 1.0 - _729;
            result = vec4(((_718 + ((p.outline1.xyz - _718) * _711)) * _587) + (result.xyz * _732), _729 + (result.w * _732));
            float _740;
            if (n.x > 0.0)
            {
                _740 = length(s0) / n.x;
            }
            else
            {
                _740 = 100.0;
            }
            float _755 = p.frame.z * max((0.89999997615814208984375 * max(_740, sqrt(least.x))) - 1.0, 0.0);
            float _760 = p.outline0.w * exp(((-4.0) * _755) * _755);
            float _741;
            if (inside.x)
            {
                _741 = p.fill0.w;
            }
            else
            {
                _741 = 0.0;
            }
            vec3 _767 = p.fill0.xyz * _741;
            float _778 = (1.0 - ((1.0 - _741) * (1.0 - _760))) * _561;
            vec4 _779 = result;
            float _781 = 1.0 - _778;
            vec4 _788 = vec4(((_767 + ((p.outline0.xyz - _767) * _760)) * _561) + (_779.xyz * _781), _778 + (_779.w * _781));
            result = _788;
            entryPointParam_fragmentMain = _788;
        }
    }
}

