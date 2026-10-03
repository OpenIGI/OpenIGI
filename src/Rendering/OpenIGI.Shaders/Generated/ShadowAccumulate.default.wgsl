struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct Params_std140_0
{
    @align(16) viewToWorld_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) prevWorldToView_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) resolution_0 : vec4<f32>,
    @align(16) cameraDelta_0 : vec4<f32>,
    @align(16) prevProjection_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
    @align(16) jitter_0 : vec4<f32>,
};

@binding(6) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(4) @group(0) var uShadow_0 : texture_storage_2d<rgba32float, read_write>;

@binding(0) @group(0) var uDepth_0 : texture_2d<f32>;

@binding(5) @group(0) var uHistory_0 : texture_storage_2d<rgba16float, write>;

@binding(7) @group(0) var uVelocity_0 : texture_2d<f32>;

@binding(2) @group(0) var uPrevHistory_0 : texture_2d<f32>;

@binding(3) @group(0) var uPrevHistorySampler_0 : sampler;

var<workgroup> gCurrent_0 : array<array<f32, i32(8)>, i32(8)>;

fn ViewPositionAt_0( uv_0 : vec2<f32>,  depth_0 : f32) -> vec3<f32>
{
    var near_0 : f32 = p_0.unproject_0.z;
    var far_0 : f32 = p_0.unproject_0.w;
    var viewZ_0 : f32 = 2.0f * near_0 * far_0 / (far_0 + near_0 - (1.0f - 2.0f * depth_0) * (far_0 - near_0));
    var ndc_0 : vec2<f32> = uv_0 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / p_0.unproject_0.x, - ndc_0.y * viewZ_0 / p_0.unproject_0.y, viewZ_0);
}

fn SampleHistoryCatmullRom_0( uv_1 : vec2<f32>) -> vec4<f32>
{
    var invSize_0 : vec2<f32> = p_0.resolution_0.zw;
    var samplePos_0 : vec2<f32> = uv_1 * p_0.resolution_0.xy;
    var _S1 : vec2<f32> = vec2<f32>(0.5f);
    var texel1_0 : vec2<f32> = floor(samplePos_0 - _S1) + _S1;
    var f_0 : vec2<f32> = samplePos_0 - texel1_0;
    var _S2 : vec2<f32> = _S1 * f_0;
    var _S3 : vec2<f32> = vec2<f32>(1.0f);
    var _S4 : vec2<f32> = vec2<f32>(-0.5f);
    var w0_0 : vec2<f32> = f_0 * (_S4 + f_0 * (_S3 - _S2));
    var _S5 : vec2<f32> = f_0 * f_0;
    var _S6 : vec2<f32> = vec2<f32>(1.5f) * f_0;
    var _S7 : vec2<f32> = vec2<f32>(2.0f);
    var w2_0 : vec2<f32> = f_0 * (_S1 + f_0 * (_S7 - _S6));
    var w3_0 : vec2<f32> = _S5 * (_S4 + _S2);
    var w12_0 : vec2<f32> = _S3 + _S5 * (vec2<f32>(-2.5f) + _S6) + w2_0;
    var pos0_0 : vec2<f32> = (texel1_0 - _S3) * invSize_0;
    var pos3_0 : vec2<f32> = (texel1_0 + _S7) * invSize_0;
    var pos12_0 : vec2<f32> = (texel1_0 + w2_0 / max(w12_0, vec2<f32>(0.00000999999974738f, 0.00000999999974738f))) * invSize_0;
    var _S8 : f32 = pos0_0.x;
    var _S9 : f32 = pos0_0.y;
    var _S10 : vec4<f32> = vec4<f32>(w0_0.x);
    var _S11 : vec4<f32> = vec4<f32>(w0_0.y);
    var _S12 : f32 = pos12_0.x;
    var _S13 : vec4<f32> = vec4<f32>(w12_0.x);
    var _S14 : f32 = pos3_0.x;
    var _S15 : vec4<f32> = vec4<f32>(w3_0.x);
    var _S16 : f32 = pos12_0.y;
    var _S17 : vec4<f32> = vec4<f32>(w12_0.y);
    var _S18 : f32 = pos3_0.y;
    var _S19 : vec4<f32> = vec4<f32>(w3_0.y);
    return (textureSampleLevel((uPrevHistory_0), (uPrevHistorySampler_0), (vec2<f32>(_S8, _S9)), (0.0f))) * _S10 * _S11 + (textureSampleLevel((uPrevHistory_0), (uPrevHistorySampler_0), (vec2<f32>(_S12, _S9)), (0.0f))) * _S13 * _S11 + (textureSampleLevel((uPrevHistory_0), (uPrevHistorySampler_0), (vec2<f32>(_S14, _S9)), (0.0f))) * _S15 * _S11 + (textureSampleLevel((uPrevHistory_0), (uPrevHistorySampler_0), (vec2<f32>(_S8, _S16)), (0.0f))) * _S10 * _S17 + (textureSampleLevel((uPrevHistory_0), (uPrevHistorySampler_0), (vec2<f32>(_S12, _S16)), (0.0f))) * _S13 * _S17 + (textureSampleLevel((uPrevHistory_0), (uPrevHistorySampler_0), (vec2<f32>(_S14, _S16)), (0.0f))) * _S15 * _S17 + (textureSampleLevel((uPrevHistory_0), (uPrevHistorySampler_0), (vec2<f32>(_S8, _S18)), (0.0f))) * _S10 * _S19 + (textureSampleLevel((uPrevHistory_0), (uPrevHistorySampler_0), (vec2<f32>(_S12, _S18)), (0.0f))) * _S13 * _S19 + (textureSampleLevel((uPrevHistory_0), (uPrevHistorySampler_0), (vec2<f32>(_S14, _S18)), (0.0f))) * _S15 * _S19;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>, @builtin(local_invocation_id) local_0 : vec3<u32>)
{
    var currentMin_0 : f32;
    var currentMax_0 : f32;
    var _S20 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S21 : vec2<i32> = vec2<i32>(p_0.resolution_0.xy);
    var inside_0 : bool;
    if((_S20.x) < (_S21.x))
    {
        inside_0 = (_S20.y) < (_S21.y);
    }
    else
    {
        inside_0 = false;
    }
    var current_0 : f32;
    if(inside_0)
    {
        var _S22 : f32 = (textureLoad((uShadow_0), (vec2<i32>(vec2<u32>(_S20)))).x);
        current_0 = _S22;
    }
    else
    {
        current_0 = 1.0f;
    }
    gCurrent_0[local_0.y][local_0.x] = current_0;
    workgroupBarrier();
    if(!inside_0)
    {
        return;
    }
    var currentMin_1 : f32 = current_0;
    var currentMax_1 : f32 = current_0;
    var ny_0 : i32 = i32(-1);
    for(;;)
    {
        if(ny_0 <= i32(1))
        {
        }
        else
        {
            break;
        }
        currentMin_0 = currentMin_1;
        currentMax_0 = currentMax_1;
        var nx_0 : i32 = i32(-1);
        for(;;)
        {
            if(nx_0 <= i32(1))
            {
            }
            else
            {
                break;
            }
            var n_0 : vec2<i32> = vec2<i32>(local_0.xy) + vec2<i32>(nx_0, ny_0);
            if((all((n_0 >= vec2<i32>(i32(0), i32(0))))))
            {
                inside_0 = (all((n_0 < vec2<i32>(i32(8), i32(8)))));
            }
            else
            {
                inside_0 = false;
            }
            if(inside_0)
            {
                var _S23 : f32 = max(currentMax_0, gCurrent_0[n_0.y][n_0.x]);
                currentMin_0 = min(currentMin_0, gCurrent_0[n_0.y][n_0.x]);
                currentMax_0 = _S23;
            }
            nx_0 = nx_0 + i32(1);
        }
        var _S24 : i32 = ny_0 + i32(1);
        currentMin_1 = currentMin_0;
        currentMax_1 = currentMax_0;
        ny_0 = _S24;
    }
    var _S25 : vec3<i32> = vec3<i32>(_S20, i32(0));
    var depth_1 : f32 = (textureLoad((uDepth_0), ((_S25)).xy, ((_S25)).z)).x;
    var _S26 : vec2<f32> = vec2<f32>(0.5f);
    var uv_2 : vec2<f32> = (vec2<f32>(_S20) + _S26 - p_0.jitter_0.xy) * p_0.resolution_0.zw;
    if(depth_1 <= 0.0f)
    {
        textureStore((uHistory_0), (vec2<u32>(_S20)), (vec4<f32>(current_0, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var viewPos_0 : vec3<f32> = ViewPositionAt_0(uv_2, depth_1);
    var prevView_0 : vec3<f32> = (((vec4<f32>((((vec4<f32>(viewPos_0, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + p_0.cameraDelta_0.xyz, 0.0f)) * (mat4x4<f32>(p_0.prevWorldToView_0.data_0[i32(0)][i32(0)], p_0.prevWorldToView_0.data_0[i32(1)][i32(0)], p_0.prevWorldToView_0.data_0[i32(2)][i32(0)], p_0.prevWorldToView_0.data_0[i32(3)][i32(0)], p_0.prevWorldToView_0.data_0[i32(0)][i32(1)], p_0.prevWorldToView_0.data_0[i32(1)][i32(1)], p_0.prevWorldToView_0.data_0[i32(2)][i32(1)], p_0.prevWorldToView_0.data_0[i32(3)][i32(1)], p_0.prevWorldToView_0.data_0[i32(0)][i32(2)], p_0.prevWorldToView_0.data_0[i32(1)][i32(2)], p_0.prevWorldToView_0.data_0[i32(2)][i32(2)], p_0.prevWorldToView_0.data_0[i32(3)][i32(2)], p_0.prevWorldToView_0.data_0[i32(0)][i32(3)], p_0.prevWorldToView_0.data_0[i32(1)][i32(3)], p_0.prevWorldToView_0.data_0[i32(2)][i32(3)], p_0.prevWorldToView_0.data_0[i32(3)][i32(3)])))).xyz;
    if((p_0.control_0.y) > 0.5f)
    {
        inside_0 = (prevView_0.z) > (p_0.prevProjection_0.z);
    }
    else
    {
        inside_0 = false;
    }
    if(inside_0)
    {
        var _S27 : f32 = prevView_0.z;
        var prevUnjittered_0 : vec2<f32> = vec2<f32>(prevView_0.x * p_0.prevProjection_0.x / _S27, - prevView_0.y * p_0.prevProjection_0.y / _S27) * _S26 + _S26;
        var ownMotion_0 : bool;
        var prevUnjittered_1 : vec2<f32>;
        if((p_0.control_0.w) > 0.5f)
        {
            var motion_0 : vec2<f32> = (textureLoad((uVelocity_0), ((_S25)).xy, ((_S25)).z)).xy;
            var apart_0 : vec2<f32> = (motion_0 - (uv_2 - prevUnjittered_0)) * p_0.resolution_0.xy;
            var _S28 : bool = (dot(apart_0, apart_0)) > 2.25f;
            if(_S28)
            {
                prevUnjittered_1 = uv_2 - motion_0;
            }
            else
            {
                prevUnjittered_1 = prevUnjittered_0;
            }
            ownMotion_0 = _S28;
        }
        else
        {
            prevUnjittered_1 = prevUnjittered_0;
            ownMotion_0 = false;
        }
        var prevUv_0 : vec2<f32> = prevUnjittered_1 + p_0.jitter_0.zw * p_0.resolution_0.zw;
        if((all((prevUv_0 >= vec2<f32>(0.0f, 0.0f)))))
        {
            inside_0 = (all((prevUv_0 <= vec2<f32>(1.0f, 1.0f))));
        }
        else
        {
            inside_0 = false;
        }
        if(inside_0)
        {
            var history_0 : vec4<f32> = (textureSampleLevel((uPrevHistory_0), (uPrevHistorySampler_0), (prevUv_0), (0.0f)));
            history_0[i32(0)] = clamp(SampleHistoryCatmullRom_0(prevUv_0).x, currentMin_1, currentMax_1);
            var _S29 : vec4<f32> = (textureGather((i32(1)), (uPrevHistory_0), (uPrevHistorySampler_0), (prevUv_0)));
            var _S30 : f32 = min(min(abs(_S29.x - _S27), abs(_S29.y - _S27)), min(abs(_S29.z - _S27), abs(_S29.w - _S27)));
            var sameSurface_0 : bool;
            if(ownMotion_0)
            {
                sameSurface_0 = true;
            }
            else
            {
                sameSurface_0 = _S30 <= (_S27 * p_0.control_0.z);
            }
            if(sameSurface_0)
            {
                inside_0 = (history_0.z) > 0.0f;
            }
            else
            {
                inside_0 = false;
            }
            if(inside_0)
            {
                var _S31 : f32 = min(history_0.z + 1.0f, max(p_0.control_0.x, 1.0f));
                currentMin_0 = mix(history_0.x, current_0, 1.0f / _S31);
                currentMax_0 = _S31;
            }
            else
            {
                currentMin_0 = current_0;
                currentMax_0 = 1.0f;
            }
        }
        else
        {
            currentMin_0 = current_0;
            currentMax_0 = 1.0f;
        }
    }
    else
    {
        currentMin_0 = current_0;
        currentMax_0 = 1.0f;
    }
    var _S32 : vec2<u32> = vec2<u32>(_S20);
    textureStore((uShadow_0), (_S32), vec4<f32>((currentMin_0), 0, 0, 1));
    textureStore((uHistory_0), (_S32), (vec4<f32>(currentMin_0, viewPos_0.z, currentMax_0, 0.0f)));
    return;
}

