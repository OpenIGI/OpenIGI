struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
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
    var step_0 : vec2<f32> = p_0.control_0.xy * p_0.size_0.zw;
    var _S4 : vec2<f32> = step_0 * vec2<f32>(1.38461542129516602f);
    var _S5 : vec3<f32> = vec3<f32>(0.31621623039245605f);
    var _S6 : vec2<f32> = step_0 * vec2<f32>(3.23076915740966797f);
    var _S7 : vec3<f32> = vec3<f32>(0.07027027010917664f);
    textureStore((uTarget_0), (vec2<u32>(_S1)), (vec4<f32>(max((textureSampleLevel((uSource_0), (uSourceSampler_0), (uv_0), (0.0f))).xyz * vec3<f32>(0.22702702879905701f) + (textureSampleLevel((uSource_0), (uSourceSampler_0), (uv_0 + _S4), (0.0f))).xyz * _S5 + (textureSampleLevel((uSource_0), (uSourceSampler_0), (uv_0 - _S4), (0.0f))).xyz * _S5 + (textureSampleLevel((uSource_0), (uSourceSampler_0), (uv_0 + _S6), (0.0f))).xyz * _S7 + (textureSampleLevel((uSource_0), (uSourceSampler_0), (uv_0 - _S6), (0.0f))).xyz * _S7, vec3<f32>(0.0f, 0.0f, 0.0f)), 1.0f)));
    return;
}

