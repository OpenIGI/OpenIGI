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
    @align(16) jitter_0 : vec4<f32>,
};

@binding(7) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uDepth_0 : texture_depth_2d;

@binding(2) @group(0) var uVelocity_0 : texture_2d<f32>;

@binding(4) @group(0) var uDilatedVelocity_0 : texture_storage_2d<rgba32float, write>;

@binding(5) @group(0) var uClosestDepth_0 : texture_storage_2d<r32float, write>;

@binding(6) @group(0) var<storage, read_write> uOccluder_0 : array<atomic<u32>>;

fn LoadDepth_0( at_0 : vec2<i32>,  size_0 : vec2<i32>) -> f32
{
    var _S1 : vec3<i32> = vec3<i32>(clamp(at_0, vec2<i32>(i32(0), i32(0)), size_0 - vec2<i32>(i32(1))), i32(0));
    return (textureLoad((uDepth_0), ((_S1)).xy, ((_S1)).z));
}

fn ViewDistance_0( d_0 : f32) -> f32
{
    var near_0 : f32 = p_0.unproject_0.z;
    var far_0 : f32 = p_0.unproject_0.w;
    return 2.0f * near_0 * far_0 / (far_0 + near_0 - (1.0f - 2.0f * d_0) * (far_0 - near_0));
}

fn PixelHeight_0( z_0 : f32) -> f32
{
    return z_0 / max(0.5f * p_0.resolution_0.y * p_0.unproject_0.y, 9.99999997475242708e-07f);
}

fn DeviceDepth_0( z_1 : f32) -> f32
{
    var near_1 : f32 = p_0.unproject_0.z;
    var far_1 : f32 = p_0.unproject_0.w;
    return (1.0f - (far_1 + near_1 - 2.0f * near_1 * far_1 / max(z_1, 9.99999997475242708e-07f)) / (far_1 - near_1)) * 0.5f;
}

fn ViewPosition_0( uv_0 : vec2<f32>,  d_1 : f32) -> vec3<f32>
{
    var z_2 : f32 = ViewDistance_0(d_1);
    var ndc_0 : vec2<f32> = vec2<f32>(2.0f) * uv_0 - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * z_2 / p_0.unproject_0.x, - ndc_0.y * z_2 / p_0.unproject_0.y, z_2);
}

fn PreviousViewZ_0( viewPos_0 : vec3<f32>) -> f32
{
    return (((vec4<f32>((((vec4<f32>(viewPos_0, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + p_0.cameraDelta_0.xyz, 0.0f)) * (mat4x4<f32>(p_0.prevWorldToView_0.data_0[i32(0)][i32(0)], p_0.prevWorldToView_0.data_0[i32(1)][i32(0)], p_0.prevWorldToView_0.data_0[i32(2)][i32(0)], p_0.prevWorldToView_0.data_0[i32(3)][i32(0)], p_0.prevWorldToView_0.data_0[i32(0)][i32(1)], p_0.prevWorldToView_0.data_0[i32(1)][i32(1)], p_0.prevWorldToView_0.data_0[i32(2)][i32(1)], p_0.prevWorldToView_0.data_0[i32(3)][i32(1)], p_0.prevWorldToView_0.data_0[i32(0)][i32(2)], p_0.prevWorldToView_0.data_0[i32(1)][i32(2)], p_0.prevWorldToView_0.data_0[i32(2)][i32(2)], p_0.prevWorldToView_0.data_0[i32(3)][i32(2)], p_0.prevWorldToView_0.data_0[i32(0)][i32(3)], p_0.prevWorldToView_0.data_0[i32(1)][i32(3)], p_0.prevWorldToView_0.data_0[i32(2)][i32(3)], p_0.prevWorldToView_0.data_0[i32(3)][i32(3)])))).z;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S2 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S3 : vec2<i32> = vec2<i32>(p_0.resolution_0.xy);
    var _S4 : i32 = _S3.x;
    var _S5 : bool;
    if((_S2.x) >= _S4)
    {
        _S5 = true;
    }
    else
    {
        _S5 = (_S2.y) >= (_S3.y);
    }
    if(_S5)
    {
        return;
    }
    var c_0 : f32 = LoadDepth_0(_S2, _S3);
    var z_3 : f32 = ViewDistance_0(c_0);
    var _S6 : f32 = abs(DeviceDepth_0(z_3 + 2.0f * PixelHeight_0(z_3)) - c_0);
    const _S7 : vec2<i32> = vec2<i32>(i32(0), i32(0));
    const _S8 : vec2<i32> = vec2<i32>(i32(1), i32(0));
    const _S9 : vec2<i32> = vec2<i32>(i32(0), i32(1));
    var s0_0 : f32 = LoadDepth_0(_S2 + _S8, _S3);
    var s1_0 : f32 = LoadDepth_0(_S2 - _S8, _S3);
    var bestDepth_0 : f32;
    var bestOffset_0 : vec2<i32>;
    if(((s0_0 + s1_0) * 0.5f - c_0) > (max(0.25f * abs(s1_0 - s0_0), _S6)))
    {
        if(s0_0 > c_0)
        {
            bestDepth_0 = s0_0;
            bestOffset_0 = _S8;
        }
        else
        {
            bestDepth_0 = c_0;
            bestOffset_0 = _S7;
        }
        if(s1_0 > bestDepth_0)
        {
            var _S10 : vec2<i32> = (vec2<i32>(0) - _S8);
            bestDepth_0 = s1_0;
            bestOffset_0 = _S10;
        }
    }
    else
    {
        bestDepth_0 = c_0;
        bestOffset_0 = _S7;
    }
    var s0_1 : f32 = LoadDepth_0(_S2 + _S9, _S3);
    var s1_1 : f32 = LoadDepth_0(_S2 - _S9, _S3);
    if(((s0_1 + s1_1) * 0.5f - c_0) > (max(0.25f * abs(s1_1 - s0_1), _S6)))
    {
        if(s0_1 > bestDepth_0)
        {
            bestDepth_0 = s0_1;
            bestOffset_0 = _S9;
        }
        if(s1_1 > bestDepth_0)
        {
            var _S11 : vec2<i32> = (vec2<i32>(0) - _S9);
            bestDepth_0 = s1_1;
            bestOffset_0 = _S11;
        }
    }
    var _S12 : vec3<i32> = vec3<i32>(clamp(_S2 + bestOffset_0, _S7, _S3 - vec2<i32>(i32(1))), i32(0));
    var dilated_0 : vec2<f32> = (textureLoad((uVelocity_0), ((_S12)).xy, ((_S12)).z)).xy;
    var _S13 : vec2<u32> = vec2<u32>(_S2);
    textureStore((uDilatedVelocity_0), (_S13), (vec4<f32>(dilated_0, 0.0f, 0.0f)));
    textureStore((uClosestDepth_0), (_S13), (vec4<f32>(bestDepth_0, 0.0f, 0.0f, 0.0f)));
    var _S14 : vec2<f32> = vec2<f32>(0.5f);
    var currentUv_0 : vec2<f32> = (vec2<f32>(_S2) + _S14) * p_0.resolution_0.zw - p_0.jitter_0.xy * p_0.resolution_0.zw;
    var _S15 : f32 = p_0.prevProjection_0.w;
    var prevViewZ_0 : f32;
    if(c_0 > 0.0f)
    {
        prevViewZ_0 = PreviousViewZ_0(ViewPosition_0(currentUv_0, c_0));
    }
    else
    {
        prevViewZ_0 = _S15;
    }
    if(prevViewZ_0 > (p_0.prevProjection_0.z))
    {
        var _S16 : vec2<i32> = vec2<i32>(floor((currentUv_0 - dilated_0 + p_0.jitter_0.zw * p_0.resolution_0.zw) * p_0.resolution_0.xy - _S14));
        var _S17 : u32 = ~(bitcast<u32>((prevViewZ_0)));
        var _S18 : i32 = _S16.x;
        if(_S18 >= i32(0))
        {
            _S5 = (_S16.y) >= i32(0);
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            _S5 = _S18 < _S4;
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            _S5 = (_S16.y) < (_S3.y);
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            var _S19 : u32 = atomicMax(&(uOccluder_0[_S16.y * _S4 + _S18]), _S17);
        }
        var at_1 : vec2<i32> = _S16 + vec2<i32>(i32(1), i32(0));
        var _S20 : i32 = at_1.x;
        if(_S20 >= i32(0))
        {
            _S5 = (at_1.y) >= i32(0);
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            _S5 = _S20 < _S4;
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            _S5 = (at_1.y) < (_S3.y);
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            var _S21 : u32 = atomicMax(&(uOccluder_0[at_1.y * _S4 + _S20]), _S17);
        }
        var at_2 : vec2<i32> = _S16 + vec2<i32>(i32(0), i32(1));
        var _S22 : i32 = at_2.x;
        if(_S22 >= i32(0))
        {
            _S5 = (at_2.y) >= i32(0);
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            _S5 = _S22 < _S4;
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            _S5 = (at_2.y) < (_S3.y);
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            var _S23 : u32 = atomicMax(&(uOccluder_0[at_2.y * _S4 + _S22]), _S17);
        }
        var at_3 : vec2<i32> = _S16 + vec2<i32>(i32(1), i32(1));
        var _S24 : i32 = at_3.x;
        if(_S24 >= i32(0))
        {
            _S5 = (at_3.y) >= i32(0);
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            _S5 = _S24 < _S4;
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            _S5 = (at_3.y) < (_S3.y);
        }
        else
        {
            _S5 = false;
        }
        if(_S5)
        {
            var _S25 : u32 = atomicMax(&(uOccluder_0[at_3.y * _S4 + _S24]), _S17);
        }
    }
    return;
}

