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

@binding(3) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uDepth_0 : texture_depth_2d;

@binding(2) @group(0) var uVelocity_0 : texture_storage_2d<rgba32float, write>;

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

fn ViewDepthAt_0( depth_1 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S8 : f32 = unproject_1.z;
    var _S9 : f32 = unproject_1.w;
    return 2.0f * _S8 * _S9 / (_S9 + _S8 - (1.0f - 2.0f * depth_1) * (_S9 - _S8));
}

fn ViewPositionAt_0( uv_1 : vec2<f32>,  depth_2 : f32,  unproject_2 : vec4<f32>) -> vec3<f32>
{
    var viewZ_0 : f32 = ViewDepthAt_0(depth_2, unproject_2);
    var ndc_0 : vec2<f32> = uv_1 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / unproject_2.x, - ndc_0.y * viewZ_0 / unproject_2.y, viewZ_0);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S10 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S11 : vec2<i32> = vec2<i32>(p_0.resolution_0.xy);
    var _S12 : bool;
    if((_S10.x) >= (_S11.x))
    {
        _S12 = true;
    }
    else
    {
        _S12 = (_S10.y) >= (_S11.y);
    }
    if(_S12)
    {
        return;
    }
    var _S13 : vec2<f32> = vec2<f32>(0.5f);
    var rasterUv_0 : vec2<f32> = (vec2<f32>(_S10) + _S13) * p_0.resolution_0.zw;
    var depth_3 : f32 = DepthBilinearLoaded_0(uDepth_0, rasterUv_0);
    if(depth_3 <= 0.0f)
    {
        var skyUv_0 : vec2<f32> = rasterUv_0 - p_0.jitter_0.xy * p_0.resolution_0.zw;
        var ndc_1 : vec2<f32> = skyUv_0 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
        var prevDirection_0 : vec3<f32> = (((vec4<f32>((((vec4<f32>(vec3<f32>(ndc_1.x / p_0.unproject_0.x, - ndc_1.y / p_0.unproject_0.y, 1.0f), 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz, 0.0f)) * (mat4x4<f32>(p_0.prevWorldToView_0.data_0[i32(0)][i32(0)], p_0.prevWorldToView_0.data_0[i32(1)][i32(0)], p_0.prevWorldToView_0.data_0[i32(2)][i32(0)], p_0.prevWorldToView_0.data_0[i32(3)][i32(0)], p_0.prevWorldToView_0.data_0[i32(0)][i32(1)], p_0.prevWorldToView_0.data_0[i32(1)][i32(1)], p_0.prevWorldToView_0.data_0[i32(2)][i32(1)], p_0.prevWorldToView_0.data_0[i32(3)][i32(1)], p_0.prevWorldToView_0.data_0[i32(0)][i32(2)], p_0.prevWorldToView_0.data_0[i32(1)][i32(2)], p_0.prevWorldToView_0.data_0[i32(2)][i32(2)], p_0.prevWorldToView_0.data_0[i32(3)][i32(2)], p_0.prevWorldToView_0.data_0[i32(0)][i32(3)], p_0.prevWorldToView_0.data_0[i32(1)][i32(3)], p_0.prevWorldToView_0.data_0[i32(2)][i32(3)], p_0.prevWorldToView_0.data_0[i32(3)][i32(3)])))).xyz;
        var _S14 : f32 = prevDirection_0.z;
        if(_S14 <= 0.00009999999747379f)
        {
            textureStore((uVelocity_0), (vec2<u32>(_S10)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
            return;
        }
        textureStore((uVelocity_0), (vec2<u32>(_S10)), (vec4<f32>(skyUv_0 - (vec2<f32>(prevDirection_0.x * p_0.prevProjection_0.x / _S14, - prevDirection_0.y * p_0.prevProjection_0.y / _S14) * _S13 + _S13), 0.0f, 0.0f)));
        return;
    }
    if(depth_3 >= 0.89999997615814209f)
    {
        textureStore((uVelocity_0), (vec2<u32>(_S10)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var currentUv_0 : vec2<f32> = rasterUv_0 - p_0.jitter_0.xy * p_0.resolution_0.zw;
    var prevView_0 : vec3<f32> = (((vec4<f32>((((vec4<f32>(ViewPositionAt_0(currentUv_0, depth_3, p_0.unproject_0), 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + p_0.cameraDelta_0.xyz, 0.0f)) * (mat4x4<f32>(p_0.prevWorldToView_0.data_0[i32(0)][i32(0)], p_0.prevWorldToView_0.data_0[i32(1)][i32(0)], p_0.prevWorldToView_0.data_0[i32(2)][i32(0)], p_0.prevWorldToView_0.data_0[i32(3)][i32(0)], p_0.prevWorldToView_0.data_0[i32(0)][i32(1)], p_0.prevWorldToView_0.data_0[i32(1)][i32(1)], p_0.prevWorldToView_0.data_0[i32(2)][i32(1)], p_0.prevWorldToView_0.data_0[i32(3)][i32(1)], p_0.prevWorldToView_0.data_0[i32(0)][i32(2)], p_0.prevWorldToView_0.data_0[i32(1)][i32(2)], p_0.prevWorldToView_0.data_0[i32(2)][i32(2)], p_0.prevWorldToView_0.data_0[i32(3)][i32(2)], p_0.prevWorldToView_0.data_0[i32(0)][i32(3)], p_0.prevWorldToView_0.data_0[i32(1)][i32(3)], p_0.prevWorldToView_0.data_0[i32(2)][i32(3)], p_0.prevWorldToView_0.data_0[i32(3)][i32(3)])))).xyz;
    var _S15 : f32 = prevView_0.z;
    if(_S15 <= (p_0.prevProjection_0.z))
    {
        textureStore((uVelocity_0), (vec2<u32>(_S10)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    textureStore((uVelocity_0), (vec2<u32>(_S10)), (vec4<f32>(currentUv_0 - (vec2<f32>(prevView_0.x * p_0.prevProjection_0.x / _S15, - prevView_0.y * p_0.prevProjection_0.y / _S15) * _S13 + _S13), 0.0f, 0.0f)));
    return;
}

