struct Params_std140_0
{
    @align(16) target_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

@binding(3) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uSource_0 : texture_2d<f32>;

@binding(1) @group(0) var uSourceSampler_0 : sampler;

@binding(2) @group(0) var uTarget_0 : texture_storage_2d<rgba16float, write>;

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S1 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S2 : vec2<i32> = vec2<i32>(p_0.target_0.xy);
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
    var _S4 : vec2<f32> = vec2<f32>(0.5f);
    var uv_0 : vec2<f32> = (vec2<f32>(_S1) + _S4) * p_0.target_0.zw;
    var offset_0 : vec2<f32> = _S4 * p_0.target_0.zw;
    var _S5 : f32 = offset_0.x;
    var _S6 : f32 = - _S5;
    var _S7 : f32 = offset_0.y;
    var _S8 : f32 = - _S7;
    var colour_0 : vec3<f32> = max(((textureSampleLevel((uSource_0), (uSourceSampler_0), (uv_0 + vec2<f32>(_S6, _S8)), (0.0f))).xyz + (textureSampleLevel((uSource_0), (uSourceSampler_0), (uv_0 + vec2<f32>(_S5, _S8)), (0.0f))).xyz + (textureSampleLevel((uSource_0), (uSourceSampler_0), (uv_0 + vec2<f32>(_S6, _S7)), (0.0f))).xyz + (textureSampleLevel((uSource_0), (uSourceSampler_0), (uv_0 + vec2<f32>(_S5, _S7)), (0.0f))).xyz) * vec3<f32>(0.25f), vec3<f32>(0.0f, 0.0f, 0.0f));
    var threshold_0 : f32 = p_0.control_0.x;
    var colour_1 : vec3<f32>;
    if(threshold_0 > 0.0f)
    {
        colour_1 = colour_0 * vec3<f32>(saturate((dot(colour_0, vec3<f32>(0.30000001192092896f, 0.5899999737739563f, 0.10999999940395355f)) - threshold_0) * 0.5f));
    }
    else
    {
        colour_1 = colour_0;
    }
    textureStore((uTarget_0), (vec2<u32>(_S1)), (vec4<f32>(colour_1, 1.0f)));
    return;
}

