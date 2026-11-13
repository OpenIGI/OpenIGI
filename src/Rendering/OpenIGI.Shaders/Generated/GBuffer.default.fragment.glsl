#version 430

#ifndef SPIRV_CROSS_CONSTANT_ID_64
#define SPIRV_CROSS_CONSTANT_ID_64 1
#endif
const int cClipYDown = SPIRV_CROSS_CONSTANT_ID_64;
const bool _238 = (cClipYDown != 0);
#ifndef SPIRV_CROSS_CONSTANT_ID_0
#define SPIRV_CROSS_CONSTANT_ID_0 0.501960813999176025390625
#endif
const float cAlphaReference = SPIRV_CROSS_CONSTANT_ID_0;
#ifndef SPIRV_CROSS_CONSTANT_ID_66
#define SPIRV_CROSS_CONSTANT_ID_66 0
#endif
const int cSamplerDecodes = SPIRV_CROSS_CONSTANT_ID_66;
const bool _541 = (cSamplerDecodes != 0);

layout(binding = 1, std140) uniform GBufferPush_std140
{
    layout(row_major) mat4 modelViewProjection;
    vec4 normal0;
    vec4 normal1;
    vec4 normal2;
    vec4 material;
} push;

layout(binding = 0, std140) uniform SurfaceSettings_std140
{
    vec4 surface;
} frame;

layout(binding = 0) uniform sampler2D _1252;
layout(binding = 1) uniform sampler2D _1254;
layout(binding = 2) uniform sampler2D _1256;

layout(location = 0) in vec2 input_diffuseUv;
layout(location = 2) in vec4 input_color;
layout(location = 4) in float input_fade;
layout(location = 1) in vec2 input_lightmapUv;
layout(location = 0) out vec4 entryPointParam_fragmentMain_baseColour;
layout(location = 1) out vec4 entryPointParam_fragmentMain_normal;
layout(location = 2) out vec4 entryPointParam_fragmentMain_material;
layout(location = 3) out vec4 entryPointParam_fragmentMain_baked;
layout(location = 3) in vec3 input_worldNormal;
layout(location = 5) in vec3 input_surfacePosition;

void main()
{
    {
        vec4 sampled = texture(_1252, input_diffuseUv);
        float alpha = sampled.w * input_color.w;
        float _1059 = fwidth(alpha);
        bool matte;
        if (push.normal2.w > 0.0)
        {
            matte = _1059 > 0.00200000009499490261077880859375;
        }
        else
        {
            matte = false;
        }
        if (matte)
        {
            if (fract(sin(dot(gl_FragCoord.xy, vec2(12.98980045318603515625, 78.233001708984375)) + push.normal2.w) * 43758.546875) > clamp(((alpha - cAlphaReference) / _1059) + 0.5, 0.0, 1.0))
            {
                discard;
            }
        }
        else
        {
            if (alpha < cAlphaReference)
            {
                discard;
            }
        }
        vec3 _530 = sampled.xyz;
        vec3 _1060;
        if (_541)
        {
            _1060 = _530;
        }
        else
        {
            _1060 = mix(pow(max((_530 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _530 * vec3(0.077399380505084991455078125), step(_530, vec3(0.040449999272823333740234375)));
        }
        vec3 albedo_1 = (_1060 * mix(pow(max((input_color.xyz + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), input_color.xyz * vec3(0.077399380505084991455078125), step(input_color.xyz, vec3(0.040449999272823333740234375)))) * input_fade;
        vec4 sampled_1 = texture(_1254, input_lightmapUv);
        vec3 _589 = sampled_1.xyz;
        vec3 _1092;
        if (_541)
        {
            _1092 = _589;
        }
        else
        {
            _1092 = mix(pow(max((_589 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _589 * vec3(0.077399380505084991455078125), step(_589, vec3(0.040449999272823333740234375)));
        }
        vec4 _1220;
        vec4 _1221;
        if (push.normal0.w > 3.5)
        {
            _1220 = vec4(0.0);
            _1221 = vec4(0.0);
            entryPointParam_fragmentMain_baseColour = vec4(0.0);
            entryPointParam_fragmentMain_normal = vec4(0.0);
            entryPointParam_fragmentMain_material = vec4(0.0);
            entryPointParam_fragmentMain_baked = vec4(_1060 * mix(pow(max((input_color.xyz + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), input_color.xyz * vec3(0.077399380505084991455078125), step(input_color.xyz, vec3(0.040449999272823333740234375))), 1.0);
        }
        else
        {
            vec3 baked;
            vec3 albedo;
            if (push.normal0.w > 2.5)
            {
                vec4 sampled_2 = texture(_1254, input_lightmapUv);
                vec3 _639 = sampled_2.xyz;
                vec3 _1124;
                if (_541)
                {
                    _1124 = _639;
                }
                else
                {
                    _1124 = mix(pow(max((_639 + vec3(0.054999999701976776123046875)) * vec3(0.947867333889007568359375), vec3(0.0)), vec3(2.400000095367431640625)), _639 * vec3(0.077399380505084991455078125), step(_639, vec3(0.040449999272823333740234375)));
                }
                baked = vec3(push.material.w, push.material.w, push.material.w);
                albedo = albedo_1 * _1124;
            }
            else
            {
                baked = _1092 * push.material.w;
                albedo = albedo_1;
            }
            vec3 _657 = normalize(input_worldNormal);
            bool authored = push.normal0.w < (-1.5);
            if (push.normal0.w < (-0.5))
            {
                matte = !authored;
            }
            else
            {
                matte = false;
            }
            float detailStrength;
            if (authored)
            {
                detailStrength = 1.0;
            }
            else
            {
                detailStrength = frame.surface.x;
            }
            vec3 normal;
            float occlusion;
            float metallic;
            float metallic_1;
            if (detailStrength > 0.0)
            {
                vec4 sampled_3 = texture(_1256, input_diffuseUv);
                vec3 _702 = vec3((sampled_3.xy * 2.0) - vec2(1.0), 0.0);
                vec2 _705 = _702.xy;
                _702.z = sqrt(max(1.0 - dot(_705, _705), 0.0));
                vec3 _1170 = dFdx(input_surfacePosition);
                vec3 _1173 = dFdy(input_surfacePosition);
                vec2 _1176 = dFdx(input_diffuseUv);
                vec2 _1179 = dFdy(input_diffuseUv);
                vec3 _1149 = cross(_1173, _657);
                vec3 _1150 = cross(_657, _1170);
                vec3 _1155 = (_1149 * _1176.x) + (_1150 * _1179.x);
                vec3 _1160 = (_1149 * _1176.y) + (_1150 * _1179.y);
                float _1165 = inversesqrt(max(max(dot(_1155, _1155), dot(_1160, _1160)), 9.9999999392252902907785028219223e-09));
                float _784 = detailStrength;
                float _788 = sampled_3.z;
                if (authored)
                {
                    detailStrength = clamp(_788, 0.0199999995529651641845703125, 1.0);
                }
                else
                {
                    detailStrength = clamp(push.material.x * mix(1.0, _788 * 2.0, clamp(frame.surface.x, 0.0, 1.0)), 0.0199999995529651641845703125, 1.0);
                }
                if (authored)
                {
                    occlusion = 1.0;
                    metallic = sampled_3.w;
                }
                else
                {
                    occlusion = sampled_3.w;
                    metallic = push.material.y;
                }
                float _814 = occlusion;
                float _815 = metallic;
                normal = normalize(mix(_657, (((_1155 * _1165) * _702.x) + ((_1160 * _1165) * _702.y)) + (_657 * _702.z), vec3(_784)));
                occlusion = detailStrength;
                metallic = _814;
                metallic_1 = _815;
            }
            else
            {
                normal = _657;
                occlusion = push.material.x;
                metallic = 1.0;
                metallic_1 = push.material.y;
            }
            bool _409;
            if (frame.surface.y != 1.0)
            {
                _409 = !matte;
            }
            else
            {
                _409 = false;
            }
            if (_409)
            {
                _409 = !authored;
            }
            else
            {
                _409 = false;
            }
            if (_409)
            {
                occlusion = min(occlusion * mix(1.0, frame.surface.y, smoothstep(0.550000011920928955078125, 0.85000002384185791015625, clamp(normal.z, 0.0, 1.0))), 1.0);
            }
            vec3 _1182 = dFdx(normal);
            vec3 _1185 = dFdy(normal);
            float _869 = occlusion;
            _1220 = vec4(albedo, metallic);
            _1221 = vec4((normalize(normal) * 0.5) + vec3(0.5), 0.0);
            int shadingModel;
            if (push.material.z > 0.0)
            {
                shadingModel = 0;
            }
            else
            {
                shadingModel = 1;
            }
            if (shadingModel == 0)
            {
                detailStrength = (128.0 + (clamp(push.material.z, 0.0625, 7.9375) * 16.0)) * 0.0039215688593685626983642578125;
            }
            else
            {
                detailStrength = 0.5;
            }
            float specular;
            if (push.normal0.w > 0.5)
            {
                specular = 0.0;
                occlusion = 1.0;
            }
            else
            {
                specular = detailStrength;
                occlusion = sqrt(min(1.0, (_869 * _869) + min((dot(_1182, _1182) + dot(_1185, _1185)) * 0.5, 0.180000007152557373046875)));
            }
            if (matte)
            {
                matte = shadingModel == 1;
            }
            else
            {
                matte = false;
            }
            if (matte)
            {
                metallic_1 = 0.0;
                specular = 0.0;
                occlusion = 1.0;
                shadingModel |= 16;
            }
            entryPointParam_fragmentMain_baseColour = _1220;
            entryPointParam_fragmentMain_normal = _1221;
            entryPointParam_fragmentMain_material = vec4(metallic_1, specular, max(occlusion, 0.015625), float(shadingModel) * 0.0039215688593685626983642578125);
            entryPointParam_fragmentMain_baked = vec4(baked, 1.0);
        }
    }
}

