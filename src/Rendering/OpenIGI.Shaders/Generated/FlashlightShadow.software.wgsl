struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct Params_std140_0
{
    @align(16) viewToWorld_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) resolution_0 : vec4<f32>,
    @align(16) cameraFrame_0 : vec4<f32>,
    @align(16) sourceLens_0 : vec4<f32>,
    @align(16) axisCull_0 : vec4<f32>,
    @align(16) reach_0 : vec4<f32>,
    @align(16) records_0 : vec4<f32>,
};

@binding(8) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uDepth_0 : texture_depth_2d;

@binding(5) @group(0) var uRaw_0 : texture_storage_2d<rgba32float, write>;

@binding(2) @group(0) var uNormal_0 : texture_2d<f32>;

fn ViewDepthAt_0( depth_0 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S1 : f32 = unproject_1.z;
    var _S2 : f32 = unproject_1.w;
    return 2.0f * _S1 * _S2 / (_S2 + _S1 - (1.0f - 2.0f * depth_0) * (_S2 - _S1));
}

fn ViewPositionAt_0( uv_0 : vec2<f32>,  depth_1 : f32,  unproject_2 : vec4<f32>) -> vec3<f32>
{
    var viewZ_0 : f32 = ViewDepthAt_0(depth_1, unproject_2);
    var ndc_0 : vec2<f32> = uv_0 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / unproject_2.x, - ndc_0.y * viewZ_0 / unproject_2.y, viewZ_0);
}

fn DecodeGBufferNormal_0( packed_0 : vec4<f32>) -> vec3<f32>
{
    return normalize(packed_0.xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f));
}

fn Pcg3d_0( v_0 : vec3<u32>) -> vec3<u32>
{
    var _S3 : vec3<u32> = v_0 * vec3<u32>(u32(1664525)) + vec3<u32>(u32(1013904223));
    var _S4 : vec3<u32> = _S3;
    _S4[i32(0)] = _S4[i32(0)] + _S3.y * _S3.z;
    _S4[i32(1)] = _S4[i32(1)] + _S4.z * _S4.x;
    _S4[i32(2)] = _S4[i32(2)] + _S4.x * _S4.y;
    var _S5 : vec3<u32> = (_S4 ^ (((_S4 >> (vec3<u32>(u32(16)))))));
    _S4 = _S5;
    _S4[i32(0)] = _S4[i32(0)] + _S5.y * _S5.z;
    _S4[i32(1)] = _S4[i32(1)] + _S4.z * _S4.x;
    _S4[i32(2)] = _S4[i32(2)] + _S4.x * _S4.y;
    return _S4;
}

fn WhiteNoise3_0( px_0 : vec2<i32>,  frame_0 : u32,  salt_0 : u32) -> vec3<f32>
{
    return vec3<f32>(((Pcg3d_0(vec3<u32>(vec2<u32>(px_0), (frame_0 ^ ((salt_0 * u32(2654435769))))))) >> (vec3<u32>(u32(8))))) * vec3<f32>(5.9604644775390625e-08f);
}

fn PixelRandom_0( px_1 : vec2<i32>,  frame_1 : f32) -> vec3<f32>
{
    return fract(vec3<f32>(((Pcg3d_0(vec3<u32>(vec2<u32>(px_1), u32(1759714724)))) >> (vec3<u32>(u32(8))))) * vec3<f32>(5.9604644775390625e-08f) + vec3<f32>(frame_1) * vec3<f32>(0.75487768650054932f, 0.56984031200408936f, 0.6180340051651001f));
}

fn ProjectToUv_0( viewPosition_0 : vec3<f32>) -> vec2<f32>
{
    var _S6 : f32 = viewPosition_0.z;
    var _S7 : vec2<f32> = vec2<f32>(0.5f);
    return vec2<f32>(viewPosition_0.x * p_0.unproject_0.x / _S6, - viewPosition_0.y * p_0.unproject_0.y / _S6) * _S7 + _S7;
}

fn ScreenBlocked_0( from_0 : vec3<f32>,  direction_0 : vec3<f32>,  segment_0 : f32,  jitter_0 : f32,  hitT_0 : ptr<function, f32>) -> bool
{
    (*hitT_0) = 0.0f;
    var _S8 : i32 = i32(p_0.reach_0.z + 0.5f);
    var _S9 : vec2<i32> = vec2<i32>(p_0.resolution_0.xy) - vec2<i32>(i32(1));
    var i_0 : i32 = i32(0);
    for(;;)
    {
        if(i_0 < _S8)
        {
        }
        else
        {
            break;
        }
        var s_0 : f32 = (f32(i_0) + jitter_0) / f32(_S8);
        var t_0 : f32 = segment_0 * s_0 * s_0;
        var view_0 : vec3<f32> = from_0 + direction_0 * vec3<f32>(t_0);
        var _S10 : f32 = view_0.z;
        if(_S10 <= (p_0.unproject_0.z))
        {
            break;
        }
        var uv_1 : vec2<f32> = ProjectToUv_0(view_0);
        var _S11 : bool;
        if((any((uv_1 < vec2<f32>(0.0f, 0.0f)))))
        {
            _S11 = true;
        }
        else
        {
            _S11 = (any((uv_1 > vec2<f32>(1.0f, 1.0f))));
        }
        if(_S11)
        {
            break;
        }
        var _S12 : vec3<i32> = vec3<i32>(clamp(vec2<i32>(uv_1 * p_0.resolution_0.xy), vec2<i32>(i32(0), i32(0)), _S9), i32(0));
        var depth_2 : f32 = (textureLoad((uDepth_0), ((_S12)).xy, ((_S12)).z));
        if(depth_2 <= 0.0f)
        {
            i_0 = i_0 + i32(1);
            continue;
        }
        var sceneZ_0 : f32 = ViewDepthAt_0(depth_2, p_0.unproject_0);
        var behind_0 : f32 = _S10 - sceneZ_0;
        var _S13 : bool;
        if(behind_0 > (0.01999999955296516f + 0.0020000000949949f * sceneZ_0))
        {
            _S13 = behind_0 < (max(0.30000001192092896f, 0.05999999865889549f * sceneZ_0));
        }
        else
        {
            _S13 = false;
        }
        if(_S13)
        {
            (*hitT_0) = t_0;
            return true;
        }
        i_0 = i_0 + i32(1);
    }
    return false;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S14 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S15 : bool;
    if((_S14.x) >= i32(p_0.resolution_0.x))
    {
        _S15 = true;
    }
    else
    {
        _S15 = (_S14.y) >= i32(p_0.resolution_0.y);
    }
    if(_S15)
    {
        return;
    }
    var _S16 : vec3<i32> = vec3<i32>(_S14, i32(0));
    var depth_3 : f32 = (textureLoad((uDepth_0), ((_S16)).xy, ((_S16)).z));
    if(depth_3 <= 0.0f)
    {
        textureStore((uRaw_0), (vec2<u32>(_S14)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var viewPos_0 : vec3<f32> = ViewPositionAt_0((vec2<f32>(_S14) + vec2<f32>(0.5f)) * p_0.resolution_0.zw, depth_3, p_0.unproject_0);
    var toLens_0 : vec3<f32> = p_0.sourceLens_0.xyz - viewPos_0;
    var distance_0 : f32 = length(toLens_0);
    var toLensDir_0 : vec3<f32> = toLens_0 / vec3<f32>(max(distance_0, 0.00000999999974738f));
    if(distance_0 >= (p_0.reach_0.x))
    {
        _S15 = true;
    }
    else
    {
        _S15 = (dot(p_0.axisCull_0.xyz, (vec3<f32>(0) - toLensDir_0))) <= (p_0.axisCull_0.w);
    }
    if(_S15)
    {
        textureStore((uRaw_0), (vec2<u32>(_S14)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var normalView_0 : vec3<f32> = (((mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])) * (vec4<f32>(DecodeGBufferNormal_0((textureLoad((uNormal_0), ((_S16)).xy, ((_S16)).z))), 0.0f)))).xyz;
    if((dot(normalView_0, toLensDir_0)) <= 0.0f)
    {
        textureStore((uRaw_0), (vec2<u32>(_S14)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var frame_2 : f32 = p_0.cameraFrame_0.w;
    var random_0 : vec3<f32>;
    if((p_0.reach_0.w) > 0.5f)
    {
        random_0 = WhiteNoise3_0(_S14, u32(frame_2), u32(61861));
    }
    else
    {
        random_0 = PixelRandom_0(_S14, frame_2);
    }
    var axis_0 : vec3<f32> = p_0.axisCull_0.xyz;
    var helper_0 : vec3<f32>;
    if((abs(axis_0.y)) < 0.89999997615814209f)
    {
        helper_0 = vec3<f32>(0.0f, 1.0f, 0.0f);
    }
    else
    {
        helper_0 = vec3<f32>(1.0f, 0.0f, 0.0f);
    }
    var across_0 : vec3<f32> = normalize(cross(helper_0, axis_0));
    var spread_0 : f32 = sqrt(random_0.x) * p_0.sourceLens_0.w;
    var around_0 : f32 = 6.28318548202514648f * random_0.y;
    var origin_0 : vec3<f32> = viewPos_0 + normalView_0 * vec3<f32>((0.02999999932944775f + 0.00100000004749745f * viewPos_0.z));
    var toTarget_0 : vec3<f32> = p_0.sourceLens_0.xyz + across_0 * vec3<f32>((spread_0 * cos(around_0))) + cross(axis_0, across_0) * vec3<f32>((spread_0 * sin(around_0))) - origin_0;
    var span_0 : f32 = length(toTarget_0);
    var direction_1 : vec3<f32> = toTarget_0 / vec3<f32>(max(span_0, 0.00000999999974738f));
    var segment_1 : f32 = span_0 - p_0.reach_0.y;
    if(segment_1 <= 0.01999999955296516f)
    {
        textureStore((uRaw_0), (vec2<u32>(_S14)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var t_1 : f32;
    var hit_0 : bool = ScreenBlocked_0(origin_0, direction_1, segment_1, random_0.z, &(t_1));
    var ratio_0 : f32;
    if(hit_0)
    {
        ratio_0 = clamp(t_1 / max(span_0 - t_1, 0.05000000074505806f), 0.00999999977648258f, 64.0f);
    }
    else
    {
        ratio_0 = 0.0f;
    }
    var _S17 : vec2<u32> = vec2<u32>(_S14);
    var _S18 : f32;
    if(hit_0)
    {
        _S18 = 0.0f;
    }
    else
    {
        _S18 = 1.0f;
    }
    textureStore((uRaw_0), (_S17), (vec4<f32>(_S18, ratio_0, 0.0f, 0.0f)));
    return;
}

