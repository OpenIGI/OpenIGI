struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

@binding(3) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uTile_0 : texture_2d<f32>;

@binding(2) @group(0) var uDilated_0 : texture_storage_2d<rgba16float, write>;

fn rsqrt_0( x_0 : f32) -> f32
{
    return 1.0f / sqrt(x_0);
}

fn ToPolar_0( v_0 : vec2<f32>) -> vec2<f32>
{
    var len_0 : f32 = length(v_0);
    var _S1 : f32;
    if(len_0 > 0.0f)
    {
        _S1 = atan2(v_0.y, v_0.x);
    }
    else
    {
        _S1 = 0.0f;
    }
    return vec2<f32>(len_0, _S1);
}

fn FromPolar_0( polar_0 : vec2<f32>) -> vec2<f32>
{
    var _S2 : f32 = polar_0.y;
    return vec2<f32>(cos(_S2), sin(_S2)) * vec2<f32>(polar_0.x);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S3 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S4 : vec2<i32> = vec2<i32>(p_0.size_0.xy);
    var _S5 : i32 = _S4.x;
    var _S6 : bool;
    if((_S3.x) >= _S5)
    {
        _S6 = true;
    }
    else
    {
        _S6 = (_S3.y) >= (_S4.y);
    }
    if(_S6)
    {
        return;
    }
    var _S7 : vec3<i32> = vec3<i32>(_S3, i32(0));
    var centre_0 : vec4<f32> = (textureLoad((uTile_0), ((_S7)).xy, ((_S7)).z));
    var _S8 : vec2<f32> = ToPolar_0(centre_0.xy);
    var fastest_0 : vec2<f32> = ToPolar_0(centre_0.zw);
    var slowest_0 : vec2<f32> = _S8;
    var y_0 : i32 = i32(-3);
    for(;;)
    {
        if(y_0 <= i32(3))
        {
        }
        else
        {
            break;
        }
        var fastest_1 : vec2<f32> = fastest_0;
        var slowest_1 : vec2<f32> = slowest_0;
        var x_1 : i32 = i32(-3);
        for(;;)
        {
            if(x_1 <= i32(3))
            {
            }
            else
            {
                break;
            }
            if(x_1 == i32(0))
            {
                _S6 = y_0 == i32(0);
            }
            else
            {
                _S6 = false;
            }
            if(_S6)
            {
                x_1 = x_1 + i32(1);
                continue;
            }
            var neighbour_0 : vec2<i32> = _S3 + vec2<i32>(x_1, y_0);
            var _S9 : i32 = neighbour_0.x;
            var _S10 : bool;
            if(_S9 < i32(0))
            {
                _S10 = true;
            }
            else
            {
                _S10 = (neighbour_0.y) < i32(0);
            }
            var _S11 : bool;
            if(_S10)
            {
                _S11 = true;
            }
            else
            {
                _S11 = _S9 >= _S5;
            }
            var _S12 : bool;
            if(_S11)
            {
                _S12 = true;
            }
            else
            {
                _S12 = (neighbour_0.y) >= (_S4.y);
            }
            if(_S12)
            {
                x_1 = x_1 + i32(1);
                continue;
            }
            var _S13 : vec3<i32> = vec3<i32>(neighbour_0, i32(0));
            var record_0 : vec4<f32> = (textureLoad((uTile_0), ((_S13)).xy, ((_S13)).z));
            var velocity_0 : vec2<f32> = record_0.zw * vec2<f32>(p_0.control_0.x);
            var lengthSq_0 : f32 = dot(velocity_0, velocity_0);
            var inverse_0 : f32 = rsqrt_0(lengthSq_0 + 9.99999993922529029e-09f);
            var direction_0 : vec2<f32> = velocity_0 * vec2<f32>(inverse_0);
            var _S14 : f32 = direction_0.x;
            var _S15 : f32 = direction_0.y;
            var margin_0 : f32 = 0.99000000953674316f * (abs(_S14) + abs(_S15));
            var offset_0 : vec2<f32> = vec2<f32>(f32(x_1), f32(y_0));
            var acrossDistance_0 : f32 = dot(vec2<f32>(- _S15, _S14), offset_0);
            var _S16 : bool;
            if((abs(dot(direction_0, offset_0))) < (lengthSq_0 * inverse_0 + margin_0))
            {
                _S16 = (abs(acrossDistance_0)) < margin_0;
            }
            else
            {
                _S16 = false;
            }
            var fastest_2 : vec2<f32>;
            var slowest_2 : vec2<f32>;
            if(_S16)
            {
                var neighbourFast_0 : vec2<f32> = ToPolar_0(velocity_0);
                var neighbourSlow_0 : vec2<f32> = ToPolar_0(record_0.xy);
                if((fastest_1.x) > (neighbourFast_0.x))
                {
                    fastest_2 = fastest_1;
                }
                else
                {
                    fastest_2 = neighbourFast_0;
                }
                if((slowest_1.x) > (neighbourSlow_0.x))
                {
                    slowest_2 = neighbourSlow_0;
                }
                else
                {
                    slowest_2 = slowest_1;
                }
            }
            else
            {
                fastest_2 = fastest_1;
                slowest_2 = slowest_1;
            }
            fastest_1 = fastest_2;
            slowest_1 = slowest_2;
            x_1 = x_1 + i32(1);
        }
        var _S17 : i32 = y_0 + i32(1);
        fastest_0 = fastest_1;
        slowest_0 = slowest_1;
        y_0 = _S17;
    }
    textureStore((uDilated_0), (vec2<u32>(_S3)), (vec4<f32>(FromPolar_0(slowest_0), FromPolar_0(fastest_0))));
    return;
}

