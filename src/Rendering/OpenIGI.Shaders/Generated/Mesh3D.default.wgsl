struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct MeshPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) markerExposure_0 : vec4<f32>,
    @align(16) normalKey0_0 : vec4<f32>,
    @align(16) normalKey1_0 : vec4<f32>,
    @align(16) normalKey2_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : MeshPush_std140_0;
@binding(0) @group(0) var uDiffuse_0 : texture_2d<f32>;

@binding(1) @group(0) var uDiffuseSampler_0 : sampler;

@binding(2) @group(0) var uLightmap_0 : texture_2d<f32>;

@binding(3) @group(0) var uLightmapSampler_0 : sampler;

@binding(0) @group(1) var uEnvironment_0 : texture_cube<f32>;

@binding(1) @group(1) var uEnvironmentSampler_0 : sampler;

@id(12) override cLinearScene_0 : i32 = i32(0);

@id(66) override cSamplerDecodes_0 : i32 = i32(0);

@id(64) override cClipYDown_0 : i32 = i32(1);

@id(1) override cReflective_0 : i32 = i32(0);

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

@id(6) override cSunAmbientRed_0 : f32 = 0.0f;

@id(7) override cSunAmbientGreen_0 : f32 = 0.0f;

@id(8) override cSunAmbientBlue_0 : f32 = 0.0f;

@id(9) override cSunRed_0 : f32 = 0.0f;

@id(10) override cSunGreen_0 : f32 = 0.0f;

@id(11) override cSunBlue_0 : f32 = 0.0f;

struct Varyings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) diffuseUv_0 : vec2<f32>,
    @location(1) lightmapUv_0 : vec2<f32>,
    @location(2) color_0 : vec4<f32>,
    @location(3) normal_0 : vec3<f32>,
    @location(4) fade_0 : f32,
    @location(5) fog_0 : f32,
    @location(6) reflection_0 : vec3<f32>,
};

struct vertexInput_0
{
    @location(0) position_1 : vec3<f32>,
    @location(1) normal_1 : vec3<f32>,
    @location(2) diffuseUv_1 : vec2<f32>,
    @location(3) lightmapUv_1 : vec2<f32>,
    @location(4) colorBgra_0 : vec4<f32>,
    @location(5) previousPosition_0 : vec3<f32>,
    @location(6) previousNormal_0 : vec3<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> Varyings_0
{
    var _S3 : vec3<f32> = vec3<f32>(push_0.markerExposure_0.z);
    var localPosition_0 : vec3<f32> = _S2.position_1 + _S3 * (_S2.previousPosition_0 - _S2.position_1);
    var localNormal_0 : vec3<f32> = _S2.normal_1 + _S3 * (_S2.previousNormal_0 - _S2.normal_1);
    var output_0 : Varyings_0;
    output_0.reflection_0 = vec3<f32>(0.0f, 0.0f, 0.0f);
    if(cReflective_0 != i32(0))
    {
        var toEye_0 : vec3<f32> = normalize((vec3<f32>(0) - localPosition_0));
        var unitNormal_0 : vec3<f32> = normalize(localNormal_0);
        output_0.reflection_0 = vec3<f32>((2.0f * dot(toEye_0, unitNormal_0))) * unitNormal_0 - toEye_0;
    }
    output_0.fade_0 = 1.0f;
    var terrain_0 : f32;
    if((abs(push_0.markerExposure_0.x)) > 0.5f)
    {
        terrain_0 = push_0.normalKey0_0.w;
    }
    else
    {
        terrain_0 = 0.0f;
    }
    var _S4 : bool = terrain_0 > 0.5f;
    var fogDensity_0 : f32;
    var facing_0 : f32;
    var localPosition_1 : vec3<f32>;
    var diffuseUv_2 : vec2<f32>;
    if(_S4)
    {
        var ramp_0 : vec3<f32> = push_0.normalKey0_0.xyz;
        var target_0 : vec3<f32> = _S2.position_1 + push_0.normalKey1_0.xyz + _S2.normal_1;
        var morph_0 : f32 = clamp((max(abs(target_0.x), max(abs(target_0.y), abs(target_0.z))) * ramp_0.x - ramp_0.y) * ramp_0.z, 0.0f, 1.0f);
        var _S5 : vec3<f32> = _S2.position_1 + _S2.normal_1 * vec3<f32>(morph_0);
        var diffuseUv_3 : vec2<f32> = _S2.diffuseUv_1 + _S2.lightmapUv_1 * vec2<f32>(morph_0);
        var selector_0 : f32 = _S2.colorBgra_0.w * 3.0f;
        if(selector_0 < 0.5f)
        {
            fogDensity_0 = 0.0f;
        }
        else
        {
            if(selector_0 < 1.5f)
            {
                fogDensity_0 = morph_0;
            }
            else
            {
                if(selector_0 < 2.5f)
                {
                    fogDensity_0 = 1.0f - morph_0;
                }
                else
                {
                    fogDensity_0 = 1.0f;
                }
            }
        }
        if(terrain_0 > 2.5f)
        {
            fogDensity_0 = 1.0f;
        }
        else
        {
            if(terrain_0 > 1.5f)
            {
                facing_0 = 1.0f - morph_0;
            }
            else
            {
                facing_0 = morph_0;
            }
            fogDensity_0 = fogDensity_0 * facing_0;
        }
        output_0.fade_0 = fogDensity_0;
        localPosition_1 = _S5;
        diffuseUv_2 = diffuseUv_3;
    }
    else
    {
        localPosition_1 = localPosition_0;
        diffuseUv_2 = _S2.diffuseUv_1;
    }
    var clip_0 : vec4<f32> = (((vec4<f32>(localPosition_1, 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var _S6 : bool;
    if((abs(push_0.markerExposure_0.x)) > 0.5f)
    {
        _S6 = (push_0.markerExposure_0.x) < 1.5f;
    }
    else
    {
        _S6 = false;
    }
    if(_S6)
    {
        fogDensity_0 = push_0.normalKey2_0.w;
    }
    else
    {
        fogDensity_0 = 0.0f;
    }
    if(fogDensity_0 > 0.0f)
    {
        fogDensity_0 = clamp(exp(- fogDensity_0 * max(clip_0.w, 0.0f)), 0.0f, 1.0f);
    }
    else
    {
        fogDensity_0 = 1.0f;
    }
    output_0.fog_0 = fogDensity_0;
    clip_0[i32(1)] = OpenIgiClipY_0(clip_0.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    output_0.position_0 = clip_0;
    output_0.diffuseUv_0 = diffuseUv_2;
    output_0.lightmapUv_0 = _S2.lightmapUv_1;
    var _S7 : vec4<f32>;
    if(_S4)
    {
        _S7 = vec4<f32>(1.0f, 1.0f, 1.0f, 1.0f);
    }
    else
    {
        _S7 = _S2.colorBgra_0.zyxw;
    }
    output_0.color_0 = _S7;
    var sunModel_0 : vec3<f32> = push_0.normalKey0_0.xyz;
    if((abs(push_0.markerExposure_0.x)) > 0.5f)
    {
        _S6 = terrain_0 < 0.5f;
    }
    else
    {
        _S6 = false;
    }
    if(_S6)
    {
        _S6 = (push_0.normalKey0_0.w) >= 0.0f;
    }
    else
    {
        _S6 = false;
    }
    if(_S6)
    {
        _S6 = (dot(sunModel_0, sunModel_0)) > 9.99999993922529029e-09f;
    }
    else
    {
        _S6 = false;
    }
    if(_S6)
    {
        var normalLength_0 : f32 = length(localNormal_0);
        if(normalLength_0 > 0.0f)
        {
            facing_0 = dot(localNormal_0 / vec3<f32>(normalLength_0), sunModel_0);
        }
        else
        {
            facing_0 = 0.0f;
        }
        var _S8 : vec3<f32> = output_0.color_0.xyz * clamp(vec3<f32>(cSunAmbientRed_0, cSunAmbientGreen_0, cSunAmbientBlue_0) + vec3<f32>(cSunRed_0, cSunGreen_0, cSunBlue_0) * vec3<f32>(max(facing_0, 0.0f)), vec3<f32>(0.0f), vec3<f32>(1.0f));
        output_0.color_0.x = _S8.x;
        output_0.color_0.y = _S8.y;
        output_0.color_0.z = _S8.z;
    }
    output_0.normal_0 = vec3<f32>(dot(push_0.normalKey0_0.xyz, localNormal_0), dot(push_0.normalKey1_0.xyz, localNormal_0), dot(push_0.normalKey2_0.xyz, localNormal_0));
    return output_0;
}

@id(0) override cAlphaReference_0 : f32 = 0.50196081399917603f;

fn EncodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(vec3<f32>(1.0549999475479126f) * pow(max(c_0, vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(0.4166666567325592f, 0.4166666567325592f, 0.4166666567325592f)) - vec3<f32>(0.05499999970197678f), c_0 * vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.00313080009073019f, 0.00313080009073019f, 0.00313080009073019f)));
}

fn SampledDisplay_0( sampled_0 : vec3<f32>) -> vec3<f32>
{
    var _S9 : vec3<f32>;
    if(cSamplerDecodes_0 != i32(0))
    {
        _S9 = EncodeSrgb_0(sampled_0);
    }
    else
    {
        _S9 = sampled_0;
    }
    return _S9;
}

fn DecodeSrgb_0( c_1 : vec3<f32>) -> vec3<f32>
{
    return mix(pow(max((c_1 + vec3<f32>(0.05499999970197678f)) / vec3<f32>(1.0549999475479126f), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(2.40000009536743164f, 2.40000009536743164f, 2.40000009536743164f)), c_1 / vec3<f32>(12.92000007629394531f), step(c_1, vec3<f32>(0.04044999927282333f, 0.04044999927282333f, 0.04044999927282333f)));
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) diffuseUv_4 : vec2<f32>,
    @location(1) lightmapUv_2 : vec2<f32>,
    @location(2) color_1 : vec4<f32>,
    @location(3) normal_2 : vec3<f32>,
    @location(4) fade_1 : f32,
    @location(5) fog_1 : f32,
    @location(6) reflection_1 : vec3<f32>,
};

@fragment
fn fragmentMain( _S10 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> pixelOutput_0
{
    var diffuse_0 : vec4<f32> = (textureSample((uDiffuse_0), (uDiffuseSampler_0), (_S10.diffuseUv_4)));
    var marker_0 : f32 = push_0.markerExposure_0.x;
    var translucent_0 : bool = marker_0 < 0.0f;
    var _S11 : bool;
    if(!translucent_0)
    {
        _S11 = (diffuse_0.w) < cAlphaReference_0;
    }
    else
    {
        _S11 = false;
    }
    if(_S11)
    {
        discard;
    }
    var lightmap_0 : vec3<f32> = SampledDisplay_0((textureSample((uLightmap_0), (uLightmapSampler_0), (_S10.lightmapUv_2))).xyz);
    if((abs(push_0.normalKey0_0.w)) > 0.5f)
    {
        _S11 = true;
    }
    else
    {
        _S11 = marker_0 < 0.5f;
    }
    var modelTint_0 : vec3<f32>;
    if(_S11)
    {
        modelTint_0 = vec3<f32>(1.0f, 1.0f, 1.0f);
    }
    else
    {
        modelTint_0 = push_0.normalKey1_0.xyz;
    }
    var _S12 : bool = (abs(marker_0)) > 0.5f;
    if(_S12)
    {
        _S11 = cLinearScene_0 == i32(0);
    }
    else
    {
        _S11 = false;
    }
    var lightmapGain_0 : f32;
    if(_S11)
    {
        lightmapGain_0 = push_0.markerExposure_0.y;
    }
    else
    {
        lightmapGain_0 = 1.0f;
    }
    var base_0 : vec3<f32> = SampledDisplay_0(diffuse_0.xyz) * lightmap_0 * vec3<f32>(lightmapGain_0) * _S10.color_1.xyz * vec3<f32>(_S10.fade_1) * modelTint_0;
    var base_1 : vec3<f32>;
    if(cReflective_0 != i32(0))
    {
        base_1 = base_0 + SampledDisplay_0((textureSample((uEnvironment_0), (uEnvironmentSampler_0), (normalize(_S10.reflection_1).xzy))).xyz);
    }
    else
    {
        base_1 = base_0;
    }
    var alpha_0 : f32;
    if(translucent_0)
    {
        alpha_0 = diffuse_0.w * _S10.color_1.w;
    }
    else
    {
        if(marker_0 > 1.5f)
        {
            lightmapGain_0 = 0.0f;
        }
        else
        {
            lightmapGain_0 = marker_0;
        }
        alpha_0 = lightmapGain_0;
    }
    if(_S12)
    {
        var fogShare_0 : f32;
        if((push_0.normalKey0_0.w) < -0.5f)
        {
            fogShare_0 = _S10.color_1.x;
        }
        else
        {
            fogShare_0 = 1.0f;
        }
        var shown_0 : vec3<f32> = mix(push_0.normalKey2_0.xyz * vec3<f32>(fogShare_0), base_1, vec3<f32>(_S10.fog_1));
        if(cLinearScene_0 != i32(0))
        {
            var _S13 : pixelOutput_0 = pixelOutput_0( vec4<f32>(DecodeSrgb_0(max(shown_0, vec3<f32>(0.0f, 0.0f, 0.0f))) * vec3<f32>(push_0.markerExposure_0.y), alpha_0) );
            return _S13;
        }
        var _S14 : pixelOutput_0 = pixelOutput_0( vec4<f32>(shown_0, alpha_0) );
        return _S14;
    }
    if(cLinearScene_0 != i32(0))
    {
        base_1 = DecodeSrgb_0(max(base_1, vec3<f32>(0.0f, 0.0f, 0.0f)));
    }
    var keyDirection_0 : vec3<f32> = normalize(vec3<f32>(-0.34999999403953552f, 0.44999998807907104f, -0.81999999284744263f));
    var normal_3 : vec3<f32> = normalize(_S10.normal_2);
    const toEye_1 : vec3<f32> = vec3<f32>(0.0f, 0.0f, -1.0f);
    var keyColour_0 : vec3<f32> = vec3<f32>(push_0.normalKey0_0.w, push_0.normalKey1_0.w, push_0.normalKey2_0.w);
    var _S15 : vec3<f32> = vec3<f32>(max(dot(normal_3, keyDirection_0), 0.0f));
    var gloss_0 : f32 = exp2(7.05000019073486328f);
    var _S16 : vec3<f32> = mix(vec3<f32>(0.03999999910593033f, 0.03999999910593033f, 0.03999999910593033f), base_1, vec3<f32>(0.34999999403953552f)) * keyColour_0;
    var _S17 : pixelOutput_0 = pixelOutput_0( vec4<f32>((base_1 * vec3<f32>(0.64999997615814209f) * (vec3<f32>(push_0.markerExposure_0.w, push_0.markerExposure_0.w, push_0.markerExposure_0.w) + keyColour_0 * _S15 + keyColour_0 * vec3<f32>(0.25f) * vec3<f32>(max(dot(normal_3, normalize(vec3<f32>(0.55000001192092896f, -0.20000000298023224f, -0.55000001192092896f))), 0.0f))) + _S16 * vec3<f32>((pow(max(dot(normal_3, normalize(keyDirection_0 + toEye_1)), 0.0f), gloss_0) * (gloss_0 + 8.0f) / 25.0f)) * _S15 + _S16 * vec3<f32>(pow(1.0f - max(dot(normal_3, toEye_1), 0.0f), 4.0f)) * vec3<f32>(0.34999999403953552f) * vec3<f32>(0.60000002384185791f)) * vec3<f32>(push_0.markerExposure_0.y), alpha_0) );
    return _S17;
}

