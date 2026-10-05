struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) sun_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
    @align(16) sizes_0 : vec4<f32>,
};

@binding(6) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(2) @group(0) var uDepth_0 : texture_depth_2d;

@binding(0) @group(0) var uSource_0 : texture_2d<f32>;

@binding(1) @group(0) var uSourceSampler_0 : sampler;

@binding(5) @group(0) var uTarget_0 : texture_storage_2d<rgba16float, write>;

@binding(3) @group(0) var uShafts_0 : texture_2d<f32>;

@binding(4) @group(0) var uShaftsSampler_0 : sampler;

fn SkyAt_0( uv_0 : vec2<f32>) -> bool
{
    var _S1 : vec3<i32> = vec3<i32>(clamp(vec2<i32>(uv_0 * p_0.sizes_0.zw), vec2<i32>(i32(0), i32(0)), vec2<i32>(p_0.sizes_0.zw) - vec2<i32>(i32(1))), i32(0));
    return (textureLoad((uDepth_0), ((_S1)).xy, ((_S1)).z)) <= 0.0f;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S2 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S3 : bool;
    if((_S2.x) >= i32(p_0.size_0.x))
    {
        _S3 = true;
    }
    else
    {
        _S3 = (_S2.y) >= i32(p_0.size_0.y);
    }
    if(_S3)
    {
        return;
    }
    var _S4 : vec2<f32> = vec2<f32>(0.5f);
    var uv_1 : vec2<f32> = (vec2<f32>(_S2) + _S4) * p_0.size_0.zw;
    var mode_0 : i32 = i32(p_0.sun_0.z + 0.5f);
    var k_0 : i32;
    var sum_0 : vec3<f32>;
    if(mode_0 == i32(0))
    {
        const _S5 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        k_0 = i32(0);
        sum_0 = _S5;
        for(;;)
        {
            if(k_0 < i32(4))
            {
            }
            else
            {
                break;
            }
            var at_0 : vec2<f32> = uv_1 + vec2<f32>(f32((k_0 & (i32(1)))) - 0.5f, f32((k_0 >> (u32(1)))) - 0.5f) * p_0.size_0.zw * _S4;
            if(SkyAt_0(at_0))
            {
                var sky_0 : vec3<f32> = (textureSampleLevel((uSource_0), (uSourceSampler_0), (at_0), (0.0f))).xyz;
                var luminance_0 : f32 = dot(sky_0, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f));
                sum_0 = sum_0 + sky_0 * vec3<f32>(saturate((luminance_0 - p_0.control_0.y) / max(luminance_0, 0.00009999999747379f)));
            }
            k_0 = k_0 + i32(1);
        }
        textureStore((uTarget_0), (vec2<u32>(_S2)), (vec4<f32>(sum_0 * vec3<f32>(0.25f) * vec3<f32>(pow(saturate(1.0f - length((p_0.sun_0.xy - uv_1) * vec2<f32>(p_0.size_0.x / p_0.size_0.y, 1.0f)) * 1.60000002384185791f), p_0.control_0.z)), 1.0f)));
        return;
    }
    if(mode_0 == i32(1))
    {
        _S3 = true;
    }
    else
    {
        _S3 = mode_0 == i32(2);
    }
    if(_S3)
    {
        var _S6 : vec2<f32> = (p_0.sun_0.xy - uv_1) * vec2<f32>(p_0.sun_0.w);
        const _S7 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        k_0 = i32(0);
        var at_1 : vec2<f32> = uv_1;
        var weight_0 : f32 = 1.0f;
        sum_0 = _S7;
        var weightSum_0 : f32 = 0.0f;
        for(;;)
        {
            if(k_0 < i32(16))
            {
            }
            else
            {
                break;
            }
            var sum_1 : vec3<f32> = sum_0 + (textureSampleLevel((uSource_0), (uSourceSampler_0), (at_1), (0.0f))).xyz * vec3<f32>(weight_0);
            var weightSum_1 : f32 = weightSum_0 + weight_0;
            var weight_1 : f32 = weight_0 * 0.93999999761581421f;
            var at_2 : vec2<f32> = at_1 + _S6;
            k_0 = k_0 + i32(1);
            at_1 = at_2;
            weight_0 = weight_1;
            sum_0 = sum_1;
            weightSum_0 = weightSum_1;
        }
        textureStore((uTarget_0), (vec2<u32>(_S2)), (vec4<f32>(sum_0 / vec3<f32>(max(weightSum_0, 0.00009999999747379f)), 1.0f)));
        return;
    }
    textureStore((uTarget_0), (vec2<u32>(_S2)), (vec4<f32>((textureSampleLevel((uSource_0), (uSourceSampler_0), (uv_1), (0.0f))).xyz + (textureSampleLevel((uShafts_0), (uShaftsSampler_0), (uv_1), (0.0f))).xyz * vec3<f32>(p_0.control_0.x) * vec3<f32>(p_0.control_0.w), 1.0f)));
    return;
}

