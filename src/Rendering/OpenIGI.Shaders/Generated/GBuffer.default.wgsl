struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct GBufferPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) normal0_0 : vec4<f32>,
    @align(16) normal1_0 : vec4<f32>,
    @align(16) normal2_0 : vec4<f32>,
    @align(16) material_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : GBufferPush_std140_0;
@binding(0) @group(0) var uDiffuse_0 : texture_2d<f32>;

@binding(1) @group(0) var uDiffuseSampler_0 : sampler;

@binding(2) @group(0) var uLightmap_0 : texture_2d<f32>;

@binding(3) @group(0) var uLightmapSampler_0 : sampler;

struct SurfaceSettings_std140_0
{
    @align(16) surface_0 : vec4<f32>,
};

@binding(0) @group(1) var<uniform> frame_0 : SurfaceSettings_std140_0;
@binding(4) @group(0) var uDetail_0 : texture_2d<f32>;

@binding(5) @group(0) var uDetailSampler_0 : sampler;

fn rsqrt_0( x_0 : f32) -> f32
{
    return 1.0f / sqrt(x_0);
}

@id(66) override cSamplerDecodes_0 : i32 = i32(0);

@id(64) override cClipYDown_0 : i32 = i32(1);

fn OpenIgiClipY_0( y_0 : f32) -> f32
{
    var _S1 : f32;
    if(cClipYDown_0 != i32(0))
    {
        _S1 = - y_0;
    }
    else
    {
        _S1 = y_0;
    }
    return _S1;
}

struct Varyings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) diffuseUv_0 : vec2<f32>,
    @location(1) lightmapUv_0 : vec2<f32>,
    @location(2) color_0 : vec4<f32>,
    @location(3) worldNormal_0 : vec3<f32>,
    @location(4) fade_0 : f32,
    @location(5) surfacePosition_0 : vec3<f32>,
};

struct vertexInput_0
{
    @location(0) position_1 : vec3<f32>,
    @location(1) normal_0 : vec3<f32>,
    @location(2) diffuseUv_1 : vec2<f32>,
    @location(3) lightmapUv_1 : vec2<f32>,
    @location(4) colorBgra_0 : vec4<f32>,
    @location(5) surfaceNormal_0 : vec4<f32>,
    @location(6) previousPosition_0 : vec3<f32>,
    @location(7) previousNormal_0 : vec3<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> Varyings_0
{
    var output_0 : Varyings_0;
    var terrain_0 : f32 = push_0.normal0_0.w;
    var ramped_0 : bool;
    if(terrain_0 > 0.5f)
    {
        ramped_0 = terrain_0 < 2.5f;
    }
    else
    {
        ramped_0 = false;
    }
    var _S3 : vec3<f32> = vec3<f32>(push_0.normal1_0.w);
    var localPosition_0 : vec3<f32> = _S2.position_1 + _S3 * (_S2.previousPosition_0 - _S2.position_1);
    output_0.fade_0 = 1.0f;
    var morph_0 : f32;
    var localPosition_1 : vec3<f32>;
    if(ramped_0)
    {
        var ramp_0 : vec3<f32> = push_0.normal0_0.xyz;
        var target_0 : vec3<f32> = localPosition_0 + push_0.normal1_0.xyz + _S2.normal_0;
        var morph_1 : f32 = clamp((max(abs(target_0.x), max(abs(target_0.y), abs(target_0.z))) * ramp_0.x - ramp_0.y) * ramp_0.z, 0.0f, 1.0f);
        var localPosition_2 : vec3<f32> = localPosition_0 + _S2.normal_0 * vec3<f32>(morph_1);
        var selector_0 : f32 = _S2.colorBgra_0.w * 3.0f;
        if(selector_0 < 0.5f)
        {
            morph_0 = 0.0f;
        }
        else
        {
            if(selector_0 < 1.5f)
            {
                morph_0 = morph_1;
            }
            else
            {
                if(selector_0 < 2.5f)
                {
                    morph_0 = 1.0f - morph_1;
                }
                else
                {
                    morph_0 = 1.0f;
                }
            }
        }
        var _S4 : f32;
        if(terrain_0 > 1.5f)
        {
            _S4 = 1.0f - morph_1;
        }
        else
        {
            _S4 = morph_1;
        }
        output_0.fade_0 = morph_0 * _S4;
        morph_0 = morph_1;
        localPosition_1 = localPosition_2;
    }
    else
    {
        morph_0 = 0.0f;
        localPosition_1 = localPosition_0;
    }
    var _S5 : vec4<f32> = (((vec4<f32>(localPosition_1, 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S5;
    clip_0[i32(1)] = OpenIgiClipY_0(_S5.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    output_0.position_0 = clip_0;
    var rotate0_0 : vec3<f32> = push_0.normal0_0.xyz;
    var rotate1_0 : vec3<f32> = push_0.normal1_0.xyz;
    var rotate2_0 : vec3<f32> = push_0.normal2_0.xyz;
    var posedNormal_0 : vec3<f32> = _S2.normal_0 + _S3 * (_S2.previousNormal_0 - _S2.normal_0);
    var localNormal_0 : vec3<f32>;
    if((_S2.surfaceNormal_0.w) > 0.5f)
    {
        localNormal_0 = _S2.surfaceNormal_0.xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f);
    }
    else
    {
        localNormal_0 = posedNormal_0;
    }
    if(ramped_0)
    {
    }
    else
    {
        localNormal_0 = rotate0_0 * vec3<f32>(localNormal_0.x) + rotate1_0 * vec3<f32>(localNormal_0.y) + rotate2_0 * vec3<f32>(localNormal_0.z);
    }
    output_0.worldNormal_0 = localNormal_0;
    if(ramped_0)
    {
        localPosition_1 = localPosition_1 + push_0.normal1_0.xyz;
    }
    else
    {
        localPosition_1 = rotate0_0 * vec3<f32>(localPosition_1.x) + rotate1_0 * vec3<f32>(localPosition_1.y) + rotate2_0 * vec3<f32>(localPosition_1.z);
    }
    output_0.surfacePosition_0 = localPosition_1;
    var _S6 : vec2<f32>;
    if(ramped_0)
    {
        _S6 = _S2.diffuseUv_1 + _S2.lightmapUv_1 * vec2<f32>(morph_0);
    }
    else
    {
        _S6 = _S2.diffuseUv_1;
    }
    output_0.diffuseUv_0 = _S6;
    output_0.lightmapUv_0 = _S2.lightmapUv_1;
    var _S7 : vec4<f32>;
    if(ramped_0)
    {
        _S7 = vec4<f32>(1.0f, 1.0f, 1.0f, 1.0f);
    }
    else
    {
        _S7 = _S2.colorBgra_0.zyxw;
    }
    output_0.color_0 = _S7;
    return output_0;
}

@id(0) override cAlphaReference_0 : f32 = 0.50196081399917603f;

fn DecodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(pow(max((c_0 + vec3<f32>(0.05499999970197678f)) / vec3<f32>(1.0549999475479126f), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(2.40000009536743164f, 2.40000009536743164f, 2.40000009536743164f)), c_0 / vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.04044999927282333f, 0.04044999927282333f, 0.04044999927282333f)));
}

fn SampledLinear_0( sampled_0 : vec3<f32>) -> vec3<f32>
{
    var _S8 : vec3<f32>;
    if(cSamplerDecodes_0 != i32(0))
    {
        _S8 = sampled_0;
    }
    else
    {
        _S8 = DecodeSrgb_0(sampled_0);
    }
    return _S8;
}

fn CotangentFrame_0( normal_1 : vec3<f32>,  position_2 : vec3<f32>,  uv_0 : vec2<f32>,  tangentAxis_0 : ptr<function, vec3<f32>>,  bitangentAxis_0 : ptr<function, vec3<f32>>)
{
    var dUvX_0 : vec2<f32> = dpdx(uv_0);
    var dUvY_0 : vec2<f32> = dpdy(uv_0);
    var crossY_0 : vec3<f32> = cross(dpdy(position_2), normal_1);
    var crossX_0 : vec3<f32> = cross(normal_1, dpdx(position_2));
    var tangent_0 : vec3<f32> = crossY_0 * vec3<f32>(dUvX_0.x) + crossX_0 * vec3<f32>(dUvY_0.x);
    var bitangent_0 : vec3<f32> = crossY_0 * vec3<f32>(dUvX_0.y) + crossX_0 * vec3<f32>(dUvY_0.y);
    var _S9 : vec3<f32> = vec3<f32>(rsqrt_0(max(max(dot(tangent_0, tangent_0), dot(bitangent_0, bitangent_0)), 9.99999993922529029e-09f)));
    (*tangentAxis_0) = tangent_0 * _S9;
    (*bitangentAxis_0) = bitangent_0 * _S9;
    return;
}

fn EncodeGBufferNormal_0( worldNormal_1 : vec3<f32>,  flags_0 : f32) -> vec4<f32>
{
    var _S10 : vec3<f32> = vec3<f32>(0.5f);
    return vec4<f32>(normalize(worldNormal_1) * _S10 + _S10, flags_0);
}

fn EncodeEmissiveStrength_0( strength_0 : f32) -> f32
{
    return (128.0f + clamp(strength_0, 0.0625f, 7.9375f) * 16.0f) / 255.0f;
}

fn EncodeGBufferMaterial_0( metallic_0 : f32,  specular_0 : f32,  roughness_0 : f32,  shadingModel_0 : i32) -> vec4<f32>
{
    return vec4<f32>(metallic_0, specular_0, max(roughness_0, 0.015625f), f32(shadingModel_0) / 255.0f);
}

struct GBufferTargets_0
{
    @location(0) baseColour_0 : vec4<f32>,
    @location(1) normal_2 : vec4<f32>,
    @location(2) material_1 : vec4<f32>,
    @location(3) baked_0 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) diffuseUv_2 : vec2<f32>,
    @location(1) lightmapUv_2 : vec2<f32>,
    @location(2) color_1 : vec4<f32>,
    @location(3) worldNormal_2 : vec3<f32>,
    @location(4) fade_1 : f32,
    @location(5) surfacePosition_1 : vec3<f32>,
};

@fragment
fn fragmentMain( _S11 : pixelInput_0, @builtin(position) position_3 : vec4<f32>) -> GBufferTargets_0
{
    var output_1 : GBufferTargets_0;
    var diffuse_0 : vec4<f32> = (textureSample((uDiffuse_0), (uDiffuseSampler_0), (_S11.diffuseUv_2)));
    var alpha_0 : f32 = diffuse_0.w * _S11.color_1.w;
    var alphaGradient_0 : f32 = (fwidth((alpha_0)));
    var _S12 : bool;
    if((push_0.normal2_0.w) > 0.0f)
    {
        _S12 = alphaGradient_0 > 0.0020000000949949f;
    }
    else
    {
        _S12 = false;
    }
    if(_S12)
    {
        if((fract(sin(dot(position_3.xy, vec2<f32>(12.98980045318603516f, 78.233001708984375f)) + push_0.normal2_0.w) * 43758.546875f)) > (clamp((alpha_0 - cAlphaReference_0) / alphaGradient_0 + 0.5f, 0.0f, 1.0f)))
        {
            discard;
        }
    }
    else
    {
        if(alpha_0 < cAlphaReference_0)
        {
            discard;
        }
    }
    var _S13 : vec3<f32> = SampledLinear_0(diffuse_0.xyz) * DecodeSrgb_0(_S11.color_1.xyz);
    var albedo_0 : vec3<f32> = _S13 * vec3<f32>(_S11.fade_1);
    var _S14 : vec3<f32> = SampledLinear_0((textureSample((uLightmap_0), (uLightmapSampler_0), (_S11.lightmapUv_2))).xyz);
    var _S15 : vec3<f32> = _S14 * vec3<f32>(push_0.material_0.w);
    if((push_0.normal0_0.w) > 3.5f)
    {
        const _S16 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
        output_1.baseColour_0 = _S16;
        output_1.normal_2 = _S16;
        output_1.material_1 = _S16;
        output_1.baked_0 = vec4<f32>(_S13, 1.0f);
        return output_1;
    }
    var baked_1 : vec3<f32>;
    var albedo_1 : vec3<f32>;
    if((push_0.normal0_0.w) > 2.5f)
    {
        var albedo_2 : vec3<f32> = albedo_0 * _S14;
        baked_1 = vec3<f32>(push_0.material_0.w, push_0.material_0.w, push_0.material_0.w);
        albedo_1 = albedo_2;
    }
    else
    {
        baked_1 = _S15;
        albedo_1 = albedo_0;
    }
    var normal_3 : vec3<f32> = normalize(_S11.worldNormal_2);
    var matte_0 : bool = (push_0.normal0_0.w) < -0.5f;
    var roughness_1 : f32 = push_0.material_0.x;
    var _S17 : f32 = push_0.material_0.y;
    var normal_4 : vec3<f32>;
    var roughness_2 : f32;
    var occlusion_0 : f32;
    if((frame_0.surface_0.x) > 0.0f)
    {
        var detail_0 : vec4<f32> = (textureSample((uDetail_0), (uDetailSampler_0), (_S11.diffuseUv_2)));
        var _S18 : vec3<f32> = vec3<f32>(detail_0.xy * vec2<f32>(2.0f) - vec2<f32>(1.0f), 0.0f);
        var tangentNormal_0 : vec3<f32> = _S18;
        var _S19 : vec2<f32> = _S18.xy;
        tangentNormal_0[i32(2)] = sqrt(max(1.0f - dot(_S19, _S19), 0.0f));
        var tangentAxis_1 : vec3<f32>;
        var bitangentAxis_1 : vec3<f32>;
        CotangentFrame_0(normal_3, _S11.surfacePosition_1, _S11.diffuseUv_2, &(tangentAxis_1), &(bitangentAxis_1));
        var _S20 : f32 = clamp(roughness_1 * mix(1.0f, detail_0.z * 2.0f, saturate(frame_0.surface_0.x)), 0.01999999955296516f, 1.0f);
        var _S21 : f32 = detail_0.w;
        normal_4 = normalize(mix(normal_3, tangentAxis_1 * vec3<f32>(tangentNormal_0.x) + bitangentAxis_1 * vec3<f32>(tangentNormal_0.y) + normal_3 * vec3<f32>(tangentNormal_0.z), vec3<f32>(frame_0.surface_0.x)));
        roughness_2 = _S20;
        occlusion_0 = _S21;
    }
    else
    {
        normal_4 = normal_3;
        roughness_2 = roughness_1;
        occlusion_0 = 1.0f;
    }
    if((frame_0.surface_0.y) != 1.0f)
    {
        _S12 = !matte_0;
    }
    else
    {
        _S12 = false;
    }
    if(_S12)
    {
        roughness_2 = min(roughness_2 * mix(1.0f, frame_0.surface_0.y, smoothstep(0.55000001192092896f, 0.85000002384185791f, clamp(normal_4.z, 0.0f, 1.0f))), 1.0f);
    }
    var dNormalX_0 : vec3<f32> = dpdx(normal_4);
    var dNormalY_0 : vec3<f32> = dpdy(normal_4);
    var _S22 : f32 = sqrt(min(1.0f, roughness_2 * roughness_2 + min(2.0f * (0.25f * (dot(dNormalX_0, dNormalX_0) + dot(dNormalY_0, dNormalY_0))), 0.18000000715255737f)));
    output_1.baseColour_0 = vec4<f32>(albedo_1, occlusion_0);
    output_1.normal_2 = EncodeGBufferNormal_0(normal_4, 0.0f);
    var shadingModel_1 : i32;
    if((push_0.material_0.z) > 0.0f)
    {
        shadingModel_1 = i32(0);
    }
    else
    {
        shadingModel_1 = i32(1);
    }
    if(shadingModel_1 == i32(0))
    {
        roughness_2 = EncodeEmissiveStrength_0(push_0.material_0.z);
    }
    else
    {
        roughness_2 = 0.5f;
    }
    var specular_1 : f32;
    if((push_0.normal0_0.w) > 0.5f)
    {
        specular_1 = 0.0f;
        roughness_2 = 1.0f;
    }
    else
    {
        specular_1 = roughness_2;
        roughness_2 = _S22;
    }
    if(matte_0)
    {
        _S12 = shadingModel_1 == i32(1);
    }
    else
    {
        _S12 = false;
    }
    var metallic_1 : f32;
    if(_S12)
    {
        var shadingModel_2 : i32 = (shadingModel_1 | (i32(16)));
        metallic_1 = 0.0f;
        specular_1 = 0.0f;
        roughness_2 = 1.0f;
        shadingModel_1 = shadingModel_2;
    }
    else
    {
        metallic_1 = _S17;
    }
    output_1.material_1 = EncodeGBufferMaterial_0(metallic_1, specular_1, roughness_2, shadingModel_1);
    output_1.baked_0 = vec4<f32>(baked_1, 1.0f);
    return output_1;
}

