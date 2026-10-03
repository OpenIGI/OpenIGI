struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

@binding(6) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uVelocity_0 : texture_2d<f32>;

@binding(1) @group(0) var uVelocitySampler_0 : sampler;

@binding(2) @group(0) var uDepth_0 : texture_depth_2d;

@binding(3) @group(0) var uDepthSampler_0 : sampler;

@binding(4) @group(0) var uFlat_0 : texture_storage_2d<rgba16float, write>;

@binding(5) @group(0) var uTile_0 : texture_storage_2d<rgba16float, write>;

fn DepthBilinearLoaded_0( depth_0 : texture_depth_2d,  uv_0 : vec2<f32>) -> f32
{
    var width_0 : u32;
    var height_0 : u32;
    {var dim = textureDimensions((depth_0));((width_0)) = dim.x;((height_0)) = dim.y;};
    var last_0 : vec2<i32> = vec2<i32>(i32(width_0) - i32(1), i32(height_0) - i32(1));
    var position_0 : vec2<f32> = uv_0 * vec2<f32>(f32(width_0), f32(height_0)) - vec2<f32>(0.5f);
    var floored_0 : vec2<f32> = floor(position_0);
    var weight_0 : vec2<f32> = position_0 - floored_0;
    var _S1 : vec2<i32> = vec2<i32>(floored_0);
    const _S2 : vec2<i32> = vec2<i32>(i32(0), i32(0));
    var _S3 : vec3<i32> = vec3<i32>(clamp(_S1, _S2, last_0), i32(0));
    var _S4 : vec3<i32> = vec3<i32>(clamp(_S1 + vec2<i32>(i32(1), i32(0)), _S2, last_0), i32(0));
    var _S5 : vec3<i32> = vec3<i32>(clamp(_S1 + vec2<i32>(i32(0), i32(1)), _S2, last_0), i32(0));
    var _S6 : vec3<i32> = vec3<i32>(clamp(_S1 + vec2<i32>(i32(1), i32(1)), _S2, last_0), i32(0));
    var _S7 : f32 = weight_0.x;
    return mix(mix((textureLoad((depth_0), ((_S3)).xy, ((_S3)).z)), (textureLoad((depth_0), ((_S4)).xy, ((_S4)).z)), _S7), mix((textureLoad((depth_0), ((_S5)).xy, ((_S5)).z)), (textureLoad((depth_0), ((_S6)).xy, ((_S6)).z)), _S7), weight_0.y);
}

fn DepthBilinear_0( depth_1 : texture_depth_2d,  linear_0 : sampler,  uv_1 : vec2<f32>) -> f32
{
    return DepthBilinearLoaded_0(depth_1, uv_1);
}

fn ViewDistance_0( deviceDepth_0 : f32,  near_0 : f32,  far_0 : f32) -> f32
{
    return 2.0f * near_0 * far_0 / (far_0 + near_0 - (1.0f - 2.0f * deviceDepth_0) * (far_0 - near_0));
}

fn ToPolar_0( v_0 : vec2<f32>) -> vec2<f32>
{
    var len_0 : f32 = length(v_0);
    var _S8 : f32;
    if(len_0 > 0.0f)
    {
        _S8 = atan2(v_0.y, v_0.x);
    }
    else
    {
        _S8 = 0.0f;
    }
    return vec2<f32>(len_0, _S8);
}

var<workgroup> sFastest_0 : array<vec2<f32>, i32(256)>;

var<workgroup> sSlowest_0 : array<vec2<f32>, i32(256)>;

fn FromPolar_0( polar_0 : vec2<f32>) -> vec2<f32>
{
    var _S9 : f32 = polar_0.y;
    return vec2<f32>(cos(_S9), sin(_S9)) * vec2<f32>(polar_0.x);
}

@compute
@workgroup_size(16, 16, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>, @builtin(workgroup_id) groupId_0 : vec3<u32>, @builtin(local_invocation_id) localId_0 : vec3<u32>)
{
    var _S10 : vec2<i32> = vec2<i32>(id_0.xy);
    var inFrame_0 : bool;
    if((_S10.x) < i32(p_0.size_0.x))
    {
        inFrame_0 = (_S10.y) < i32(p_0.size_0.y);
    }
    else
    {
        inFrame_0 = false;
    }
    const _S11 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    var velocity_0 : vec2<f32>;
    var viewDepth_0 : f32;
    if(inFrame_0)
    {
        var uv_2 : vec2<f32> = (vec2<f32>(_S10) + vec2<f32>(0.5f)) * p_0.size_0.zw;
        var _S12 : f32 = ViewDistance_0(DepthBilinear_0(uDepth_0, uDepthSampler_0, uv_2), p_0.unproject_0.z, p_0.unproject_0.w);
        velocity_0 = (textureSampleLevel((uVelocity_0), (uVelocitySampler_0), (uv_2), (0.0f))).xy * p_0.control_0.xy;
        viewDepth_0 = _S12;
    }
    else
    {
        velocity_0 = _S11;
        viewDepth_0 = 0.0f;
    }
    var _S13 : vec2<f32> = ToPolar_0(velocity_0);
    var polar_1 : vec2<f32> = _S13;
    polar_1[i32(0)] = min(_S13.x, p_0.control_0.z);
    if(inFrame_0)
    {
        textureStore((uFlat_0), (vec2<u32>(_S10)), (vec4<f32>(polar_1.x, polar_1.y * 0.15915493667125702f + 0.5f, viewDepth_0, 0.0f)));
    }
    var lane_0 : u32 = localId_0.y * u32(16) + localId_0.x;
    sFastest_0[lane_0] = polar_1;
    sSlowest_0[lane_0] = polar_1;
    workgroupBarrier();
    var stride_0 : u32 = u32(128);
    for(;;)
    {
        if(stride_0 > u32(0))
        {
        }
        else
        {
            break;
        }
        if(lane_0 < stride_0)
        {
            var fastA_0 : vec2<f32> = sFastest_0[lane_0];
            var _S14 : u32 = lane_0 + stride_0;
            var fastB_0 : vec2<f32> = sFastest_0[_S14];
            if((sFastest_0[lane_0].x) > (sFastest_0[_S14].x))
            {
                velocity_0 = fastA_0;
            }
            else
            {
                velocity_0 = fastB_0;
            }
            sFastest_0[lane_0] = velocity_0;
            var slowA_0 : vec2<f32> = sSlowest_0[lane_0];
            var slowB_0 : vec2<f32> = sSlowest_0[_S14];
            var _S15 : vec2<f32>;
            if((sSlowest_0[lane_0].x) > (sSlowest_0[_S14].x))
            {
                _S15 = slowB_0;
            }
            else
            {
                _S15 = slowA_0;
            }
            sSlowest_0[lane_0] = _S15;
        }
        workgroupBarrier();
        stride_0 = (stride_0 >> (u32(1)));
    }
    if(lane_0 == u32(0))
    {
        textureStore((uTile_0), (vec2<u32>(vec2<i32>(groupId_0.xy))), (vec4<f32>(FromPolar_0(sSlowest_0[i32(0)]), FromPolar_0(sFastest_0[i32(0)]))));
    }
    return;
}

