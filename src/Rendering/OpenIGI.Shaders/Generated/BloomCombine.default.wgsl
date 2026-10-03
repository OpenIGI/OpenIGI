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

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S1 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S2 : vec2<i32> = vec2<i32>(p_0.size_0.xy);
    var _S3 : bool;
    if((_S1.x) >= (_S2.x))
    {
        _S3 = true;
    }
    else
    {
        _S3 = (_S1.y) >= (_S2.y);
    }
    if(_S3)
    {
        return;
    }
    var uv_0 : vec2<f32> = (vec2<f32>(_S1) + vec2<f32>(0.5f)) * p_0.size_0.zw;
    textureStore((uTarget_0), (vec2<u32>(_S1)), (vec4<f32>(max(((textureSampleLevel((uLevel0_0), (uLevel0Sampler_0), (uv_0), (0.0f))).xyz * vec3<f32>(p_0.tintA_0.x) + (textureSampleLevel((uLevel1_0), (uLevel1Sampler_0), (uv_0), (0.0f))).xyz * vec3<f32>(p_0.tintA_0.y) + (textureSampleLevel((uLevel2_0), (uLevel2Sampler_0), (uv_0), (0.0f))).xyz * vec3<f32>(p_0.tintA_0.z) + (textureSampleLevel((uLevel3_0), (uLevel3Sampler_0), (uv_0), (0.0f))).xyz * vec3<f32>(p_0.tintA_0.w) + (textureSampleLevel((uLevel4_0), (uLevel4Sampler_0), (uv_0), (0.0f))).xyz * vec3<f32>(p_0.tintB_0.x) + (textureSampleLevel((uLevel5_0), (uLevel5Sampler_0), (uv_0), (0.0f))).xyz * vec3<f32>(p_0.tintB_0.y)) * vec3<f32>(p_0.tintB_0.z), vec3<f32>(0.0f, 0.0f, 0.0f)), 1.0f)));
    return;
}

