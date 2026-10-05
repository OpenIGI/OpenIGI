struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) sun_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
    @align(16) sizes_0 : vec4<f32>,
    @align(16) occlusion_0 : vec4<f32>,
};

@binding(6) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uSource_0 : texture_2d<f32>;

@binding(1) @group(0) var uSourceSampler_0 : sampler;

@binding(2) @group(0) var uDepth_0 : texture_depth_2d;

@binding(5) @group(0) var uTarget_0 : texture_storage_2d<rgba16float, write>;

@binding(3) @group(0) var uShafts_0 : texture_2d<f32>;

@binding(4) @group(0) var uShaftsSampler_0 : sampler;

fn SceneDistance_0( uv_0 : vec2<f32>) -> f32
{
    var _S1 : vec3<i32> = vec3<i32>(clamp(vec2<i32>(uv_0 * p_0.sizes_0.zw), vec2<i32>(i32(0), i32(0)), vec2<i32>(p_0.sizes_0.zw) - vec2<i32>(i32(1))), i32(0));
    var depth_0 : f32 = (textureLoad((uDepth_0), ((_S1)).xy, ((_S1)).z));
    if(depth_0 <= 0.0f)
    {
        return p_0.occlusion_0.y;
    }
    var near_0 : f32 = p_0.occlusion_0.x;
    var far_0 : f32 = p_0.occlusion_0.y;
    return 2.0f * near_0 * far_0 / (far_0 + near_0 - (1.0f - 2.0f * depth_0) * (far_0 - near_0));
}

fn HazeAt_0( uv_1 : vec2<f32>) -> f32
{
    return smoothstep(40.0f, 160.0f, SceneDistance_0(uv_1));
}

fn SunDistance_0( uv_2 : vec2<f32>) -> f32
{
    return length((p_0.sun_0.xy - uv_2) * vec2<f32>(p_0.size_0.x / p_0.size_0.y, 1.0f));
}

fn AirAt_0( uv_3 : vec2<f32>) -> f32
{
    return 1.0f - exp(- SceneDistance_0(uv_3) / 80.0f);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S2 : vec2<i32> = vec2<i32>(id_0.xy);
    var inside_0 : bool;
    if((_S2.x) >= i32(p_0.size_0.x))
    {
        inside_0 = true;
    }
    else
    {
        inside_0 = (_S2.y) >= i32(p_0.size_0.y);
    }
    if(inside_0)
    {
        return;
    }
    var _S3 : vec2<f32> = vec2<f32>(0.5f);
    var uv_4 : vec2<f32> = (vec2<f32>(_S2) + _S3) * p_0.size_0.zw;
    var mode_0 : i32 = i32(p_0.sun_0.z + 0.5f);
    var r_0 : i32;
    var ring_0 : f32;
    var open_0 : f32;
    if(mode_0 == i32(0))
    {
        ring_0 = 1.0e+09f;
        r_0 = i32(0);
        for(;;)
        {
            if(r_0 < i32(4))
            {
            }
            else
            {
                break;
            }
            var _S4 : f32 = min(ring_0, dot((textureSampleLevel((uSource_0), (uSourceSampler_0), (saturate(p_0.sun_0.xy + vec2<f32>(f32((r_0 & (i32(1)))) * 2.0f - 1.0f, f32((r_0 >> (u32(1)))) * 2.0f - 1.0f) * vec2<f32>(p_0.size_0.y / p_0.size_0.x, 1.0f) * vec2<f32>(0.03500000014901161f))), (0.0f))).xyz, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f)));
            var _S5 : i32 = r_0 + i32(1);
            ring_0 = _S4;
            r_0 = _S5;
        }
        var _S6 : f32 = ring_0 * p_0.control_0.y;
        var _S7 : f32 = max(ring_0 * 4.0f, 0.00009999999747379f);
        const _S8 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        var k_0 : i32 = i32(0);
        var bright_0 : vec3<f32> = _S8;
        open_0 = 0.0f;
        for(;;)
        {
            if(k_0 < i32(4))
            {
            }
            else
            {
                break;
            }
            var at_0 : vec2<f32> = uv_4 + vec2<f32>(f32((k_0 & (i32(1)))) - 0.5f, f32((k_0 >> (u32(1)))) - 0.5f) * p_0.size_0.zw * _S3;
            var haze_0 : f32 = HazeAt_0(at_0);
            var colour_0 : vec3<f32> = (textureSampleLevel((uSource_0), (uSourceSampler_0), (at_0), (0.0f))).xyz;
            var luminance_0 : f32 = dot(colour_0, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f));
            var bright_1 : vec3<f32> = bright_0 + colour_0 * vec3<f32>((min(max(luminance_0 - _S6, 0.0f), _S7) / max(luminance_0, 0.00009999999747379f) * haze_0));
            var open_1 : f32 = open_0 + haze_0;
            k_0 = k_0 + i32(1);
            bright_0 = bright_1;
            open_0 = open_1;
        }
        textureStore((uTarget_0), (vec2<u32>(_S2)), (vec4<f32>(bright_0 * vec3<f32>((0.25f * pow(saturate(1.0f - SunDistance_0(uv_4) / max(p_0.control_0.z, 0.00100000004749745f)), 2.0f))), open_0 * 0.25f)));
        return;
    }
    if(mode_0 == i32(1))
    {
        inside_0 = true;
    }
    else
    {
        inside_0 = mode_0 == i32(2);
    }
    if(inside_0)
    {
        var _S9 : vec2<f32> = (p_0.sun_0.xy - uv_4) * vec2<f32>(p_0.sun_0.w);
        const _S10 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
        r_0 = i32(0);
        var at_1 : vec2<f32> = uv_4;
        ring_0 = 1.0f;
        var sum_0 : vec4<f32> = _S10;
        open_0 = 0.0f;
        for(;;)
        {
            if(r_0 < i32(16))
            {
            }
            else
            {
                break;
            }
            var tap_0 : vec4<f32> = (textureSampleLevel((uSource_0), (uSourceSampler_0), (at_1), (0.0f)));
            if((all((at_1 >= vec2<f32>(0.0f, 0.0f)))))
            {
                inside_0 = (all((at_1 <= vec2<f32>(1.0f, 1.0f))));
            }
            else
            {
                inside_0 = false;
            }
            var _S11 : vec3<f32> = tap_0.xyz;
            var _S12 : f32;
            if(inside_0)
            {
                _S12 = tap_0.w;
            }
            else
            {
                _S12 = 1.0f;
            }
            var sum_1 : vec4<f32> = sum_0 + vec4<f32>(_S11, _S12) * vec4<f32>(ring_0);
            var weightSum_0 : f32 = open_0 + ring_0;
            var weight_0 : f32 = ring_0 * 0.94999998807907104f;
            var at_2 : vec2<f32> = at_1 + _S9;
            r_0 = r_0 + i32(1);
            at_1 = at_2;
            ring_0 = weight_0;
            sum_0 = sum_1;
            open_0 = weightSum_0;
        }
        textureStore((uTarget_0), (vec2<u32>(_S2)), (sum_0 / vec4<f32>(max(open_0, 0.00009999999747379f))));
        return;
    }
    var shafts_0 : vec4<f32> = (textureSampleLevel((uShafts_0), (uShaftsSampler_0), (uv_4), (0.0f)));
    var haze_1 : f32 = AirAt_0(uv_4);
    textureStore((uTarget_0), (vec2<u32>(_S2)), (vec4<f32>((textureSampleLevel((uSource_0), (uSourceSampler_0), (uv_4), (0.0f))).xyz + shafts_0.xyz * vec3<f32>((p_0.control_0.x * p_0.control_0.w * haze_1)), saturate(1.0f - p_0.occlusion_0.z * p_0.control_0.w * pow(saturate(1.0f - SunDistance_0(uv_4) * 0.89999997615814209f), 1.5f) * haze_1 * saturate(1.0f - shafts_0.w)))));
    return;
}

