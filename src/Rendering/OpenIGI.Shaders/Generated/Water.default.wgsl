struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct WaterPush_std140_0
{
    @align(16) modelViewProjection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) eyeFacing_0 : vec4<f32>,
    @align(16) diffuseScale_0 : vec4<f32>,
    @align(16) specularFog_0 : vec4<f32>,
    @align(16) fogNormalScale_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : WaterPush_std140_0;
@binding(0) @group(1) var uEnvironment_0 : texture_cube<f32>;

@binding(1) @group(1) var uEnvironmentSampler_0 : sampler;

@id(12) override cLinearScene_0 : i32 = i32(0);

@id(66) override cSamplerDecodes_0 : i32 = i32(0);

@id(64) override cClipYDown_0 : i32 = i32(1);

@id(1) override cSunX_0 : f32 = 0.0f;

@id(2) override cSunY_0 : f32 = 0.0f;

@id(3) override cSunZ_0 : f32 = 0.0f;

@id(9) override cSunRed_0 : f32 = 0.0f;

@id(10) override cSunGreen_0 : f32 = 0.0f;

@id(11) override cSunBlue_0 : f32 = 0.0f;

@id(6) override cAmbientRed_0 : f32 = 0.0f;

@id(7) override cAmbientGreen_0 : f32 = 0.0f;

@id(8) override cAmbientBlue_0 : f32 = 0.0f;

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

struct WaterVaryings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) colour0_0 : vec4<f32>,
    @location(1) colour1_0 : vec3<f32>,
    @location(2) reflection_0 : vec3<f32>,
    @location(3) fog_0 : f32,
};

struct vertexInput_0
{
    @location(0) position_1 : vec3<f32>,
    @location(1) normal_0 : vec3<f32>,
    @location(2) shallowness_0 : vec2<f32>,
    @location(3) lightmapUv_0 : vec2<f32>,
    @location(4) colorBgra_0 : vec4<f32>,
    @location(5) previousPosition_0 : vec3<f32>,
    @location(6) previousNormal_0 : vec3<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> WaterVaryings_0
{
    const sun_0 : vec3<f32> = vec3<f32>(cSunX_0, cSunY_0, cSunZ_0);
    const sunColour_0 : vec3<f32> = vec3<f32>(cSunRed_0, cSunGreen_0, cSunBlue_0);
    var normal_1 : vec3<f32> = _S2.normal_0 * vec3<f32>(push_0.fogNormalScale_0.w);
    var toEye_0 : vec3<f32> = normalize(push_0.eyeFacing_0.xyz - _S2.position_1);
    var lit_0 : vec3<f32> = sunColour_0 * vec3<f32>(dot(normal_1, sun_0)) * push_0.diffuseScale_0.xyz + vec3<f32>(cAmbientRed_0, cAmbientGreen_0, cAmbientBlue_0);
    var _S3 : f32 = max(dot(toEye_0, normal_1), push_0.eyeFacing_0.w);
    var output_0 : WaterVaryings_0;
    output_0.reflection_0 = (normal_1 * vec3<f32>((_S3 * 2.0f)) - toEye_0).xzy;
    var grazing_0 : f32 = 1.0f - _S3;
    var grazing2_0 : f32 = grazing_0 * grazing_0;
    var fresnel_0 : f32 = grazing2_0 * grazing2_0 * grazing_0 * 0.97963279485702515f + 0.02036719955503941f;
    var shallowness_1 : f32 = _S2.shallowness_0.x;
    var _S4 : f32 = 1.0f - fresnel_0;
    output_0.colour0_0 = saturate(vec4<f32>(fresnel_0, fresnel_0, fresnel_0, _S4 * shallowness_1));
    var glint_0 : f32 = dot(normalize(toEye_0 + sun_0), normal_1);
    var glint2_0 : f32 = glint_0 * glint_0;
    var glint8_0 : f32 = glint2_0 * glint2_0;
    output_0.colour1_0 = saturate((push_0.specularFog_0.xyz * vec3<f32>((glint8_0 * glint8_0 * glint_0)) * sunColour_0 * vec3<f32>(fresnel_0) + lit_0 * vec3<f32>(_S4)) * vec3<f32>((1.0f - shallowness_1)));
    var clip_0 : vec4<f32> = (((vec4<f32>(_S2.position_1, 1.0f)) * (mat4x4<f32>(push_0.modelViewProjection_0.data_0[i32(0)][i32(0)], push_0.modelViewProjection_0.data_0[i32(1)][i32(0)], push_0.modelViewProjection_0.data_0[i32(2)][i32(0)], push_0.modelViewProjection_0.data_0[i32(3)][i32(0)], push_0.modelViewProjection_0.data_0[i32(0)][i32(1)], push_0.modelViewProjection_0.data_0[i32(1)][i32(1)], push_0.modelViewProjection_0.data_0[i32(2)][i32(1)], push_0.modelViewProjection_0.data_0[i32(3)][i32(1)], push_0.modelViewProjection_0.data_0[i32(0)][i32(2)], push_0.modelViewProjection_0.data_0[i32(1)][i32(2)], push_0.modelViewProjection_0.data_0[i32(2)][i32(2)], push_0.modelViewProjection_0.data_0[i32(3)][i32(2)], push_0.modelViewProjection_0.data_0[i32(0)][i32(3)], push_0.modelViewProjection_0.data_0[i32(1)][i32(3)], push_0.modelViewProjection_0.data_0[i32(2)][i32(3)], push_0.modelViewProjection_0.data_0[i32(3)][i32(3)]))));
    var density_0 : f32 = push_0.specularFog_0.w;
    var _S5 : f32;
    if(density_0 > 0.0f)
    {
        _S5 = saturate(exp2(- density_0 * max(clip_0.w, 0.0f)));
    }
    else
    {
        _S5 = 1.0f;
    }
    output_0.fog_0 = _S5;
    clip_0[i32(1)] = OpenIgiClipY_0(clip_0.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    output_0.position_0 = clip_0;
    return output_0;
}

fn EncodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(vec3<f32>(1.0549999475479126f) * pow(max(c_0, vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(0.4166666567325592f, 0.4166666567325592f, 0.4166666567325592f)) - vec3<f32>(0.05499999970197678f), c_0 * vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.00313080009073019f, 0.00313080009073019f, 0.00313080009073019f)));
}

fn SampledDisplay_0( sampled_0 : vec3<f32>) -> vec3<f32>
{
    var _S6 : vec3<f32>;
    if(cSamplerDecodes_0 != i32(0))
    {
        _S6 = EncodeSrgb_0(sampled_0);
    }
    else
    {
        _S6 = sampled_0;
    }
    return _S6;
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
    @location(0) colour0_1 : vec4<f32>,
    @location(1) colour1_1 : vec3<f32>,
    @location(2) reflection_1 : vec3<f32>,
    @location(3) fog_1 : f32,
};

@fragment
fn fragmentMain( _S7 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> pixelOutput_0
{
    var shown_0 : vec3<f32> = mix(push_0.fogNormalScale_0.xyz, saturate(_S7.colour0_1.xyz * SampledDisplay_0((textureSample((uEnvironment_0), (uEnvironmentSampler_0), (_S7.reflection_1))).xyz) + _S7.colour1_1), vec3<f32>(_S7.fog_1));
    var shown_1 : vec3<f32>;
    if(cLinearScene_0 != i32(0))
    {
        shown_1 = DecodeSrgb_0(max(shown_0, vec3<f32>(0.0f, 0.0f, 0.0f))) * vec3<f32>(push_0.diffuseScale_0.w);
    }
    else
    {
        shown_1 = shown_0;
    }
    var _S8 : pixelOutput_0 = pixelOutput_0( vec4<f32>(shown_1, _S7.colour0_1.w) );
    return _S8;
}

