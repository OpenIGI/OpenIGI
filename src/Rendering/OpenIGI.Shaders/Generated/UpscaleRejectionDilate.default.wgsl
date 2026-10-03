struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
};

@binding(3) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uRejection_0 : texture_2d<f32>;

@binding(2) @group(0) var uBlend_0 : texture_storage_2d<rgba32float, write>;

var<workgroup> sMedian_0 : array<f32, i32(100)>;

fn Median3_0( a_0 : f32,  b_0 : f32,  c_0 : f32) -> f32
{
    return max(min(a_0, b_0), min(max(a_0, b_0), c_0));
}

fn MedianAt_0( c_1 : vec2<i32>,  size_1 : vec2<i32>) -> f32
{
    const _S1 : vec2<i32> = vec2<i32>(i32(0), i32(0));
    var _S2 : vec2<i32> = size_1 - vec2<i32>(i32(1));
    var _S3 : vec3<i32> = vec3<i32>(clamp(c_1 + vec2<i32>(i32(-1), i32(-1)), _S1, _S2), i32(0));
    var _S4 : vec3<i32> = vec3<i32>(clamp(c_1 + vec2<i32>(i32(0), i32(-1)), _S1, _S2), i32(0));
    var _S5 : vec3<i32> = vec3<i32>(clamp(c_1 + vec2<i32>(i32(1), i32(-1)), _S1, _S2), i32(0));
    var rows_0 : array<f32, i32(3)>;
    var _S6 : f32 = Median3_0((textureLoad((uRejection_0), ((_S3)).xy, ((_S3)).z)).x, (textureLoad((uRejection_0), ((_S4)).xy, ((_S4)).z)).x, (textureLoad((uRejection_0), ((_S5)).xy, ((_S5)).z)).x);
    rows_0[i32(0)] = _S6;
    var _S7 : vec3<i32> = vec3<i32>(clamp(c_1 + vec2<i32>(i32(-1), i32(0)), _S1, _S2), i32(0));
    var _S8 : vec3<i32> = vec3<i32>(clamp(c_1, _S1, _S2), i32(0));
    var _S9 : vec3<i32> = vec3<i32>(clamp(c_1 + vec2<i32>(i32(1), i32(0)), _S1, _S2), i32(0));
    var _S10 : f32 = Median3_0((textureLoad((uRejection_0), ((_S7)).xy, ((_S7)).z)).x, (textureLoad((uRejection_0), ((_S8)).xy, ((_S8)).z)).x, (textureLoad((uRejection_0), ((_S9)).xy, ((_S9)).z)).x);
    rows_0[i32(1)] = _S10;
    var _S11 : vec3<i32> = vec3<i32>(clamp(c_1 + vec2<i32>(i32(-1), i32(1)), _S1, _S2), i32(0));
    var _S12 : vec3<i32> = vec3<i32>(clamp(c_1 + vec2<i32>(i32(0), i32(1)), _S1, _S2), i32(0));
    var _S13 : vec3<i32> = vec3<i32>(clamp(c_1 + vec2<i32>(i32(1), i32(1)), _S1, _S2), i32(0));
    var _S14 : f32 = Median3_0((textureLoad((uRejection_0), ((_S11)).xy, ((_S11)).z)).x, (textureLoad((uRejection_0), ((_S12)).xy, ((_S12)).z)).x, (textureLoad((uRejection_0), ((_S13)).xy, ((_S13)).z)).x);
    rows_0[i32(2)] = _S14;
    return Median3_0(_S6, _S10, _S14);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>, @builtin(workgroup_id) groupId_0 : vec3<u32>, @builtin(local_invocation_id) localId_0 : vec3<u32>, @builtin(local_invocation_index) localIndex_0 : u32)
{
    var _S15 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S16 : vec2<i32> = vec2<i32>(p_0.size_0.xy);
    const _S17 : vec2<i32> = vec2<i32>(i32(1), i32(1));
    var _S18 : vec2<i32> = vec2<i32>(groupId_0.xy) * vec2<i32>(i32(8)) - _S17;
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
        var _S19 : i32 = i32(i_0);
        sMedian_0[i_0] = MedianAt_0(_S18 + vec2<i32>(_S19 % i32(10), _S19 / i32(10)), _S16);
        i_0 = i_0 + u32(64);
    }
    workgroupBarrier();
    var _S20 : bool;
    if((_S15.x) >= (_S16.x))
    {
        _S20 = true;
    }
    else
    {
        _S20 = (_S15.y) >= (_S16.y);
    }
    if(_S20)
    {
        return;
    }
    var _S21 : vec2<i32> = vec2<i32>(localId_0.xy) + _S17;
    var _S22 : i32 = _S21.y;
    var _S23 : i32 = _S21.x;
    var _S24 : i32 = (_S22 + i32(-1)) * i32(10) + _S23;
    var _S25 : i32 = _S22 * i32(10) + _S23;
    var _S26 : i32 = (_S22 + i32(1)) * i32(10) + _S23;
    textureStore((uBlend_0), (vec2<u32>(_S15)), (vec4<f32>(min(min(min(min(min(min(min(min(min(1.0f, sMedian_0[_S24 + i32(-1)]), sMedian_0[_S24]), sMedian_0[_S24 + i32(1)]), sMedian_0[_S25 + i32(-1)]), sMedian_0[_S25]), sMedian_0[_S25 + i32(1)]), sMedian_0[_S26 + i32(-1)]), sMedian_0[_S26]), sMedian_0[_S26 + i32(1)]), 0.0f, 0.0f, 0.0f)));
    return;
}

