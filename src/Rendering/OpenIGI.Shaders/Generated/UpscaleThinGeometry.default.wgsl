struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

@binding(7) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uDepth_0 : texture_depth_2d;

@binding(2) @group(0) var uRejection_0 : texture_2d<f32>;

@binding(5) @group(0) var uVelocity_0 : texture_2d<f32>;

@binding(6) @group(0) var uVelocitySampler_0 : sampler;

@binding(4) @group(0) var uRelaxed_0 : texture_storage_2d<rgba32float, write>;

var<workgroup> sLine_0 : array<f32, i32(100)>;

fn ViewDepth_0( depth_0 : f32) -> f32
{
    var near_0 : f32 = p_0.unproject_0.z;
    var far_0 : f32 = p_0.unproject_0.w;
    return 2.0f * near_0 * far_0 / (far_0 + near_0 - (1.0f - 2.0f * depth_0) * (far_0 - near_0));
}

fn DepthAt_0( px_0 : vec2<i32>,  size_1 : vec2<i32>) -> f32
{
    var _S1 : vec3<i32> = vec3<i32>(clamp(px_0, vec2<i32>(i32(0), i32(0)), size_1 - vec2<i32>(i32(1))), i32(0));
    var d_0 : f32 = (textureLoad((uDepth_0), ((_S1)).xy, ((_S1)).z));
    var _S2 : f32;
    if(d_0 <= 0.0f)
    {
        _S2 = p_0.unproject_0.w;
    }
    else
    {
        _S2 = ViewDepth_0(d_0);
    }
    return _S2;
}

fn LineAt_0( c_0 : vec2<i32>,  size_2 : vec2<i32>) -> f32
{
    var _S3 : vec3<i32> = vec3<i32>(clamp(c_0, vec2<i32>(i32(0), i32(0)), size_2 - vec2<i32>(i32(1))), i32(0));
    if((textureLoad((uDepth_0), ((_S3)).xy, ((_S3)).z)) <= 0.0f)
    {
        return 0.0f;
    }
    var d_1 : array<f32, i32(9)>;
    var _S4 : f32 = DepthAt_0(c_0 + vec2<i32>(i32(-1), i32(-1)), size_2);
    d_1[i32(0)] = _S4;
    d_1[i32(1)] = DepthAt_0(c_0 + vec2<i32>(i32(0), i32(-1)), size_2);
    d_1[i32(2)] = DepthAt_0(c_0 + vec2<i32>(i32(1), i32(-1)), size_2);
    var _S5 : f32 = DepthAt_0(c_0 + vec2<i32>(i32(-1), i32(0)), size_2);
    d_1[i32(3)] = _S5;
    var _S6 : f32 = DepthAt_0(c_0, size_2);
    d_1[i32(4)] = _S6;
    d_1[i32(5)] = DepthAt_0(c_0 + vec2<i32>(i32(1), i32(0)), size_2);
    d_1[i32(6)] = DepthAt_0(c_0 + vec2<i32>(i32(-1), i32(1)), size_2);
    d_1[i32(7)] = DepthAt_0(c_0 + vec2<i32>(i32(0), i32(1)), size_2);
    d_1[i32(8)] = DepthAt_0(c_0 + vec2<i32>(i32(1), i32(1)), size_2);
    var tolerance_0 : f32 = _S6 * p_0.control_0.x;
    var horizontal_0 : f32;
    if((_S4 - _S5) > tolerance_0)
    {
        horizontal_0 = 1.0f;
    }
    else
    {
        horizontal_0 = 0.0f;
    }
    var vertical_0 : f32;
    if((d_1[i32(6)] - d_1[i32(3)]) > tolerance_0)
    {
        vertical_0 = 1.0f;
    }
    else
    {
        vertical_0 = 0.0f;
    }
    var h_0 : f32 = horizontal_0 + vertical_0;
    if((d_1[i32(0)] - d_1[i32(1)]) > tolerance_0)
    {
        horizontal_0 = 1.0f;
    }
    else
    {
        horizontal_0 = 0.0f;
    }
    if((d_1[i32(2)] - d_1[i32(1)]) > tolerance_0)
    {
        vertical_0 = 1.0f;
    }
    else
    {
        vertical_0 = 0.0f;
    }
    var v_0 : f32 = horizontal_0 + vertical_0;
    if((d_1[i32(1)] - d_1[i32(4)]) > tolerance_0)
    {
        horizontal_0 = 1.0f;
    }
    else
    {
        horizontal_0 = 0.0f;
    }
    if((d_1[i32(7)] - d_1[i32(4)]) > tolerance_0)
    {
        vertical_0 = 1.0f;
    }
    else
    {
        vertical_0 = 0.0f;
    }
    var h_1 : f32 = horizontal_0 + vertical_0;
    if((d_1[i32(3)] - d_1[i32(4)]) > tolerance_0)
    {
        horizontal_0 = 1.0f;
    }
    else
    {
        horizontal_0 = 0.0f;
    }
    if((d_1[i32(5)] - d_1[i32(4)]) > tolerance_0)
    {
        vertical_0 = 1.0f;
    }
    else
    {
        vertical_0 = 0.0f;
    }
    var v_1 : f32 = horizontal_0 + vertical_0;
    if((d_1[i32(2)] - d_1[i32(5)]) > tolerance_0)
    {
        horizontal_0 = 1.0f;
    }
    else
    {
        horizontal_0 = 0.0f;
    }
    if((d_1[i32(8)] - d_1[i32(5)]) > tolerance_0)
    {
        vertical_0 = 1.0f;
    }
    else
    {
        vertical_0 = 0.0f;
    }
    var h_2 : f32 = horizontal_0 + vertical_0;
    if((d_1[i32(6)] - d_1[i32(7)]) > tolerance_0)
    {
        horizontal_0 = 1.0f;
    }
    else
    {
        horizontal_0 = 0.0f;
    }
    if((d_1[i32(8)] - d_1[i32(7)]) > tolerance_0)
    {
        vertical_0 = 1.0f;
    }
    else
    {
        vertical_0 = 0.0f;
    }
    var v_2 : f32 = horizontal_0 + vertical_0;
    if(h_1 < 2.0f)
    {
        horizontal_0 = 0.0f;
    }
    else
    {
        horizontal_0 = saturate((h_0 + h_2 - 2.0f) * 0.5f);
    }
    if(v_1 < 2.0f)
    {
        vertical_0 = 0.0f;
    }
    else
    {
        vertical_0 = saturate((v_0 + v_2 - 2.0f) * 0.5f);
    }
    var ridge_0 : f32 = saturate(horizontal_0 + vertical_0);
    var _S7 : bool = (abs(d_1[i32(4)] - d_1[i32(3)])) > tolerance_0;
    var differs_0 : bool = (abs(d_1[i32(4)] - d_1[i32(0)])) > tolerance_0;
    var transitions_0 : i32;
    if(differs_0 != _S7)
    {
        transitions_0 = i32(1);
    }
    else
    {
        transitions_0 = i32(0);
    }
    var differs_1 : bool = (abs(d_1[i32(4)] - d_1[i32(1)])) > tolerance_0;
    if(differs_1 != differs_0)
    {
        transitions_0 = transitions_0 + i32(1);
    }
    var differs_2 : bool = (abs(d_1[i32(4)] - d_1[i32(2)])) > tolerance_0;
    if(differs_2 != differs_1)
    {
        transitions_0 = transitions_0 + i32(1);
    }
    var differs_3 : bool = (abs(d_1[i32(4)] - d_1[i32(5)])) > tolerance_0;
    if(differs_3 != differs_2)
    {
        transitions_0 = transitions_0 + i32(1);
    }
    var differs_4 : bool = (abs(d_1[i32(4)] - d_1[i32(8)])) > tolerance_0;
    if(differs_4 != differs_3)
    {
        transitions_0 = transitions_0 + i32(1);
    }
    var differs_5 : bool = (abs(d_1[i32(4)] - d_1[i32(7)])) > tolerance_0;
    if(differs_5 != differs_4)
    {
        transitions_0 = transitions_0 + i32(1);
    }
    var differs_6 : bool = (abs(d_1[i32(4)] - d_1[i32(6)])) > tolerance_0;
    if(differs_6 != differs_5)
    {
        transitions_0 = transitions_0 + i32(1);
    }
    if(((abs(d_1[i32(4)] - d_1[i32(3)])) > tolerance_0) != differs_6)
    {
        transitions_0 = transitions_0 + i32(1);
    }
    return max(ridge_0, clamp((f32(transitions_0) - 2.0f) * 0.5f, 0.0f, 1.0f));
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>, @builtin(workgroup_id) groupId_0 : vec3<u32>, @builtin(local_invocation_id) localId_0 : vec3<u32>, @builtin(local_invocation_index) localIndex_0 : u32)
{
    var _S8 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S9 : vec2<i32> = vec2<i32>(p_0.size_0.xy);
    const _S10 : vec2<i32> = vec2<i32>(i32(1), i32(1));
    var _S11 : vec2<i32> = vec2<i32>(groupId_0.xy) * vec2<i32>(i32(8)) - _S10;
    var i_0 : u32 = localIndex_0;
    for(;;)
    {
        if(i_0 < u32(100))
        {
        }
        else
        {
            break;
        }
        var _S12 : i32 = i32(i_0);
        sLine_0[i_0] = LineAt_0(_S11 + vec2<i32>(_S12 % i32(10), _S12 / i32(10)), _S9);
        i_0 = i_0 + u32(64);
    }
    workgroupBarrier();
    var _S13 : bool;
    if((_S8.x) >= (_S9.x))
    {
        _S13 = true;
    }
    else
    {
        _S13 = (_S8.y) >= (_S9.y);
    }
    if(_S13)
    {
        return;
    }
    var believable_0 : f32;
    if((p_0.control_0.z) > 0.5f)
    {
        var _S14 : vec3<i32> = vec3<i32>(_S8, i32(0));
        believable_0 = (textureLoad((uRejection_0), ((_S14)).xy, ((_S14)).z)).x;
    }
    else
    {
        believable_0 = 1.0f;
    }
    var _S15 : vec2<i32> = vec2<i32>(localId_0.xy) + _S10;
    var _S16 : i32 = _S15.y;
    var _S17 : i32 = _S15.x;
    var _S18 : i32 = (_S16 + i32(-1)) * i32(10) + _S17;
    var _S19 : i32 = _S16 * i32(10) + _S17;
    var _S20 : i32 = (_S16 + i32(1)) * i32(10) + _S17;
    var _S21 : f32 = max(max(max(max(max(max(max(max(max(0.0f, sLine_0[_S18 + i32(-1)]), sLine_0[_S18]), sLine_0[_S18 + i32(1)]), sLine_0[_S19 + i32(-1)]), sLine_0[_S19]), sLine_0[_S19 + i32(1)]), sLine_0[_S20 + i32(-1)]), sLine_0[_S20]), sLine_0[_S20 + i32(1)]);
    var speedPx_0 : f32;
    if((p_0.control_0.w) > 0.5f)
    {
        speedPx_0 = length((textureSampleLevel((uVelocity_0), (uVelocitySampler_0), ((vec2<f32>(_S8) + vec2<f32>(0.5f)) * p_0.size_0.zw), (0.0f))).xy * p_0.size_0.xy);
    }
    else
    {
        speedPx_0 = 0.0f;
    }
    textureStore((uRelaxed_0), (vec2<u32>(_S8)), (vec4<f32>(mix(believable_0, 1.0f, _S21 * p_0.control_0.y * clamp(1.0f - speedPx_0, 0.0f, 1.0f)), _S21, 0.0f, 0.0f)));
    return;
}

