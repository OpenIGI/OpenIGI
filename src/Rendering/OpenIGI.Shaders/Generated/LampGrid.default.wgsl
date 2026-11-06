struct Params_std140_0
{
    @align(16) shape_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) depth_0 : vec4<f32>,
    @align(16) positionRadius_0 : array<vec4<f32>, i32(160)>,
};

@binding(0) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(1) @group(0) var<storage, read_write> uLampGrid_0 : array<u32>;

fn CornerAt_0( u_0 : f32,  v_0 : f32,  z_0 : f32) -> vec3<f32>
{
    return vec3<f32>((u_0 * 2.0f - 1.0f) * z_0 / p_0.unproject_0.x, - (v_0 * 2.0f - 1.0f) * z_0 / p_0.unproject_0.y, z_0);
}

@compute
@workgroup_size(64, 1, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var tilesX_0 : u32 = u32(p_0.shape_0.x);
    var tilesY_0 : u32 = u32(p_0.shape_0.y);
    var slices_0 : u32 = u32(p_0.shape_0.z);
    var _S1 : i32 = i32(p_0.shape_0.w + 0.5f);
    var _S2 : u32 = id_0.x;
    if(_S2 == u32(0))
    {
        uLampGrid_0[i32(0)] = tilesX_0;
        uLampGrid_0[i32(1)] = tilesY_0;
        uLampGrid_0[i32(2)] = slices_0;
        uLampGrid_0[i32(3)] = (bitcast<u32>((p_0.depth_0.x)));
        uLampGrid_0[i32(4)] = (bitcast<u32>((p_0.depth_0.y)));
        uLampGrid_0[i32(5)] = (bitcast<u32>((p_0.depth_0.z)));
        uLampGrid_0[i32(6)] = (bitcast<u32>((p_0.depth_0.w)));
        uLampGrid_0[i32(7)] = u32(0);
    }
    var _S3 : u32 = tilesX_0 * tilesY_0;
    if(_S2 >= (_S3 * slices_0))
    {
        return;
    }
    var tx_0 : u32 = _S2 % tilesX_0;
    var _S4 : u32 = _S2 / tilesX_0;
    var ty_0 : u32 = _S4 % tilesY_0;
    var sz_0 : u32 = _S2 / _S3;
    var _S5 : f32 = f32(tx_0) / p_0.depth_0.z;
    var _S6 : f32 = f32(tx_0 + u32(1)) / p_0.depth_0.z;
    var _S7 : f32 = f32(ty_0) / p_0.depth_0.w;
    var _S8 : f32 = f32(ty_0 + u32(1)) / p_0.depth_0.w;
    var _S9 : f32;
    if(sz_0 == u32(0))
    {
        _S9 = 0.0f;
    }
    else
    {
        _S9 = p_0.depth_0.x * exp2(f32(sz_0) / p_0.depth_0.y);
    }
    var _S10 : u32 = sz_0 + u32(1);
    var _S11 : f32;
    if(_S10 >= slices_0)
    {
        _S11 = max(p_0.unproject_0.w, p_0.depth_0.x) * 1.00999999046325684f;
    }
    else
    {
        _S11 = p_0.depth_0.x * exp2(f32(_S10) / p_0.depth_0.y);
    }
    const _S12 : vec3<f32> = vec3<f32>(1.00000001504746622e+30f, 1.00000001504746622e+30f, 1.00000001504746622e+30f);
    const _S13 : vec3<f32> = vec3<f32>(-1.00000001504746622e+30f, -1.00000001504746622e+30f, -1.00000001504746622e+30f);
    var corner_0 : i32 = i32(0);
    var lo_0 : vec3<f32> = _S12;
    var hi_0 : vec3<f32> = _S13;
    for(;;)
    {
        if(corner_0 < i32(8))
        {
        }
        else
        {
            break;
        }
        var _S14 : f32;
        if(((corner_0 & (i32(1)))) != i32(0))
        {
            _S14 = _S6;
        }
        else
        {
            _S14 = _S5;
        }
        var _S15 : f32;
        if(((corner_0 & (i32(2)))) != i32(0))
        {
            _S15 = _S8;
        }
        else
        {
            _S15 = _S7;
        }
        var _S16 : f32;
        if(((corner_0 & (i32(4)))) != i32(0))
        {
            _S16 = _S11;
        }
        else
        {
            _S16 = _S9;
        }
        var at_0 : vec3<f32> = CornerAt_0(_S14, _S15, _S16);
        var _S17 : vec3<f32> = min(lo_0, at_0);
        var _S18 : vec3<f32> = max(hi_0, at_0);
        corner_0 = corner_0 + i32(1);
        lo_0 = _S17;
        hi_0 = _S18;
    }
    var _S19 : vec3<f32> = vec3<f32>(0.00100000004749745f);
    var margin_0 : vec3<f32> = (abs(lo_0) + abs(hi_0)) * _S19 + _S19;
    var lo_1 : vec3<f32> = lo_0 - margin_0;
    var hi_1 : vec3<f32> = hi_0 + margin_0;
    var base_0 : u32 = u32(8) + _S2 * u32(32);
    var i_0 : i32 = i32(0);
    var count_0 : u32 = u32(0);
    for(;;)
    {
        if(i_0 < _S1)
        {
        }
        else
        {
            break;
        }
        var reach_0 : f32 = p_0.positionRadius_0[i_0].w * 1.0010000467300415f + 0.00100000004749745f;
        var _S20 : vec3<f32> = p_0.positionRadius_0[i_0].xyz;
        var nearest_0 : vec3<f32> = clamp(_S20, lo_1, hi_1) - _S20;
        if((dot(nearest_0, nearest_0)) < (reach_0 * reach_0))
        {
            if(count_0 < u32(31))
            {
                uLampGrid_0[base_0 + u32(1) + count_0] = u32(i_0);
            }
            count_0 = count_0 + u32(1);
        }
        i_0 = i_0 + i32(1);
    }
    if(count_0 > u32(31))
    {
        count_0 = u32(4294967295);
    }
    uLampGrid_0[base_0] = count_0;
    return;
}

