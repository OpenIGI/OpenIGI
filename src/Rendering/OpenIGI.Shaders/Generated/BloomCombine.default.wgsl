struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) tintA_0 : vec4<f32>,
    @align(16) tintB_0 : vec4<f32>,
};

@binding(13) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uLevel0_0 : texture_2d<f32>;

@binding(1) @group(0) var uLevel0Sampler_0 : sampler;

@binding(2) @group(0) var uLevel1_0 : texture_2d<f32>;

@binding(3) @group(0) var uLevel1Sampler_0 : sampler;

@binding(4) @group(0) var uLevel2_0 : texture_2d<f32>;

@binding(5) @group(0) var uLevel2Sampler_0 : sampler;

@binding(6) @group(0) var uLevel3_0 : texture_2d<f32>;

@binding(7) @group(0) var uLevel3Sampler_0 : sampler;

@binding(8) @group(0) var uLevel4_0 : texture_2d<f32>;

@binding(9) @group(0) var uLevel4Sampler_0 : sampler;

@binding(10) @group(0) var uLevel5_0 : texture_2d<f32>;

@binding(11) @group(0) var uLevel5Sampler_0 : sampler;

@binding(12) @group(0) var uTarget_0 : texture_storage_2d<rgba16float, write>;

fn SampleSmooth_0( level_0 : texture_2d<f32>,  levelSampler_0 : sampler,  uv_0 : vec2<f32>) -> vec3<f32>
{
    var width_0 : u32;
    var height_0 : u32;
    {var dim = textureDimensions((level_0));((width_0)) = dim.x;((height_0)) = dim.y;};
    var size_1 : vec2<f32> = vec2<f32>(f32(width_0), f32(height_0));
    var _S1 : vec2<f32> = vec2<f32>(0.5f);
    var texel_0 : vec2<f32> = uv_0 * size_1 - _S1;
    var base_0 : vec2<f32> = floor(texel_0);
    var f_0 : vec2<f32> = texel_0 - base_0;
    var f2_0 : vec2<f32> = f_0 * f_0;
    var f3_0 : vec2<f32> = f2_0 * f_0;
    var _S2 : vec2<f32> = vec2<f32>(3.0f);
    var _S3 : vec2<f32> = _S2 * f2_0;
    var _S4 : vec2<f32> = _S2 * f_0;
    var _S5 : vec2<f32> = vec2<f32>(1.0f);
    var _S6 : vec2<f32> = vec2<f32>(0.1666666716337204f);
    var w1_0 : vec2<f32> = _S6 * (_S2 * f3_0 - vec2<f32>(6.0f) * f2_0 + vec2<f32>(4.0f));
    var w3_0 : vec2<f32> = _S6 * f3_0;
    var g0_0 : vec2<f32> = _S6 * ((vec2<f32>(0) - f3_0) + _S3 - _S4 + _S5) + w1_0;
    var g1_0 : vec2<f32> = _S6 * (vec2<f32>(-3.0f) * f3_0 + _S3 + _S4 + _S5) + w3_0;
    var h0_0 : vec2<f32> = (w1_0 / g0_0 - _S5 + base_0 + _S1) / size_1;
    var h1_0 : vec2<f32> = (w3_0 / g1_0 + _S5 + base_0 + _S1) / size_1;
    var _S7 : f32 = h0_0.x;
    var _S8 : f32 = h0_0.y;
    var _S9 : vec3<f32> = vec3<f32>(g0_0.x);
    var _S10 : f32 = h1_0.x;
    var _S11 : vec3<f32> = vec3<f32>(g1_0.x);
    var _S12 : f32 = h1_0.y;
    return vec3<f32>(g0_0.y) * (_S9 * (textureSampleLevel((level_0), (levelSampler_0), (vec2<f32>(_S7, _S8)), (0.0f))).xyz + _S11 * (textureSampleLevel((level_0), (levelSampler_0), (vec2<f32>(_S10, _S8)), (0.0f))).xyz) + vec3<f32>(g1_0.y) * (_S9 * (textureSampleLevel((level_0), (levelSampler_0), (vec2<f32>(_S7, _S12)), (0.0f))).xyz + _S11 * (textureSampleLevel((level_0), (levelSampler_0), (vec2<f32>(_S10, _S12)), (0.0f))).xyz);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S13 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S14 : vec2<i32> = vec2<i32>(p_0.size_0.xy);
    var _S15 : bool;
    if((_S13.x) >= (_S14.x))
    {
        _S15 = true;
    }
    else
    {
        _S15 = (_S13.y) >= (_S14.y);
    }
    if(_S15)
    {
        return;
    }
    var uv_1 : vec2<f32> = (vec2<f32>(_S13) + vec2<f32>(0.5f)) * p_0.size_0.zw;
    textureStore((uTarget_0), (vec2<u32>(_S13)), (vec4<f32>(max(((textureSampleLevel((uLevel0_0), (uLevel0Sampler_0), (uv_1), (0.0f))).xyz * vec3<f32>(p_0.tintA_0.x) + SampleSmooth_0(uLevel1_0, uLevel1Sampler_0, uv_1) * vec3<f32>(p_0.tintA_0.y) + SampleSmooth_0(uLevel2_0, uLevel2Sampler_0, uv_1) * vec3<f32>(p_0.tintA_0.z) + SampleSmooth_0(uLevel3_0, uLevel3Sampler_0, uv_1) * vec3<f32>(p_0.tintA_0.w) + SampleSmooth_0(uLevel4_0, uLevel4Sampler_0, uv_1) * vec3<f32>(p_0.tintB_0.x) + SampleSmooth_0(uLevel5_0, uLevel5Sampler_0, uv_1) * vec3<f32>(p_0.tintB_0.y)) * vec3<f32>(p_0.tintB_0.z), vec3<f32>(0.0f, 0.0f, 0.0f)), 1.0f)));
    return;
}

