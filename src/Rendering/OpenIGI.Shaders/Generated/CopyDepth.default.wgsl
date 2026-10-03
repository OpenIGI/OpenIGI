@binding(1) @group(0) var uPreviousDepth_0 : texture_storage_2d<r32float, write>;

@binding(0) @group(0) var uDepth_0 : texture_depth_2d;

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var width_0 : u32;
    var height_0 : u32;
    {var dim = textureDimensions((uPreviousDepth_0));((width_0)) = dim.x;((height_0)) = dim.y;};
    var _S1 : bool;
    if((id_0.x) >= width_0)
    {
        _S1 = true;
    }
    else
    {
        _S1 = (id_0.y) >= height_0;
    }
    if(_S1)
    {
        return;
    }
    var _S2 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S3 : vec3<i32> = vec3<i32>(_S2, i32(0));
    textureStore((uPreviousDepth_0), (vec2<u32>(_S2)), (vec4<f32>((textureLoad((uDepth_0), ((_S3)).xy, ((_S3)).z)), 0.0f, 0.0f, 0.0f)));
    return;
}

