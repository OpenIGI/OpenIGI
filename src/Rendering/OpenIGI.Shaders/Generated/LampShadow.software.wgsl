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
    @align(16) lampControl_0 : vec4<f32>,
    @align(16) lampSource_0 : vec4<f32>,
    @align(16) lampPositionRadius_0 : array<vec4<f32>, i32(160)>,
    @align(16) lampColourIntensity_0 : array<vec4<f32>, i32(160)>,
    @align(16) lampDirection_0 : array<vec4<f32>, i32(160)>,
    @align(16) lampRect_0 : array<vec4<f32>, i32(160)>,
};

@binding(8) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(5) @group(0) var uVisibility_0 : texture_storage_2d<rgba32float, write>;

@binding(0) @group(0) var uDepth_0 : texture_depth_2d;

@binding(1) @group(0) var uDepthSampler_0 : sampler;

@binding(2) @group(0) var uNormal_0 : texture_2d<f32>;

@binding(3) @group(0) var uNormalSampler_0 : sampler;

struct FieldParams_std140_0
{
    @align(16) origin_0 : array<vec4<f32>, i32(4)>,
    @align(16) range_0 : vec4<f32>,
    @align(16) levels_0 : vec4<i32>,
};

@binding(9) @group(0) var<uniform> f_0 : FieldParams_std140_0;
@binding(4) @group(0) var uClipmaps_0 : texture_3d<f32>;

@binding(6) @group(0) var uClipmapsSampler_0 : sampler;

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

fn ViewDepthAt_0( depth_2 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S8 : f32 = unproject_1.z;
    var _S9 : f32 = unproject_1.w;
    return 2.0f * _S8 * _S9 / (_S9 + _S8 - (1.0f - 2.0f * depth_2) * (_S9 - _S8));
}

fn ViewPositionAt_0( uv_2 : vec2<f32>,  depth_3 : f32,  unproject_2 : vec4<f32>) -> vec3<f32>
{
    var viewZ_0 : f32 = ViewDepthAt_0(depth_3, unproject_2);
    var ndc_0 : vec2<f32> = uv_2 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / unproject_2.x, - ndc_0.y * viewZ_0 / unproject_2.y, viewZ_0);
}

fn Pcg3d_0( v_0 : vec3<u32>) -> vec3<u32>
{
    var _S10 : vec3<u32> = v_0 * vec3<u32>(u32(1664525)) + vec3<u32>(u32(1013904223));
    var _S11 : vec3<u32> = _S10;
    _S11[i32(0)] = _S11[i32(0)] + _S10.y * _S10.z;
    _S11[i32(1)] = _S11[i32(1)] + _S11.z * _S11.x;
    _S11[i32(2)] = _S11[i32(2)] + _S11.x * _S11.y;
    var _S12 : vec3<u32> = (_S11 ^ (((_S11 >> (vec3<u32>(u32(16)))))));
    _S11 = _S12;
    _S11[i32(0)] = _S11[i32(0)] + _S12.y * _S12.z;
    _S11[i32(1)] = _S11[i32(1)] + _S11.z * _S11.x;
    _S11[i32(2)] = _S11[i32(2)] + _S11.x * _S11.y;
    return _S11;
}

fn PixelRandom_0( px_0 : vec2<i32>) -> vec3<f32>
{
    return vec3<f32>(((Pcg3d_0(vec3<u32>(vec2<u32>(px_0), u32(2654435769)))) >> (vec3<u32>(u32(8))))) * vec3<f32>(5.9604644775390625e-08f);
}

fn ProjectToUv_0( viewPosition_0 : vec3<f32>) -> vec2<f32>
{
    var _S13 : f32 = viewPosition_0.z;
    var _S14 : vec2<f32> = vec2<f32>(0.5f);
    return vec2<f32>(viewPosition_0.x * p_0.unproject_0.x / _S13, - viewPosition_0.y * p_0.unproject_0.y / _S13) * _S14 + _S14;
}

fn DistanceAt_0( world_0 : vec3<f32>,  known_0 : ptr<function, bool>,  raw_0 : ptr<function, f32>,  resolved_0 : ptr<function, f32>) -> f32
{
    (*known_0) = false;
    (*raw_0) = 0.0f;
    (*resolved_0) = f_0.origin_0[i32(0)].w;
    var level_0 : i32 = i32(0);
    for(;;)
    {
        if(level_0 < (f_0.levels_0.x))
        {
        }
        else
        {
            break;
        }
        var _S15 : f32 = f_0.origin_0[level_0].w;
        var voxel_0 : vec3<f32> = (world_0 - f_0.origin_0[level_0].xyz) / vec3<f32>(_S15);
        var _S16 : bool;
        if((any((voxel_0 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S16 = true;
        }
        else
        {
            _S16 = (any((voxel_0 > vec3<f32>(f32(f_0.levels_0.y) - 0.5f, f32(f_0.levels_0.y) - 0.5f, f32(f_0.levels_0.y) - 0.5f))));
        }
        if(_S16)
        {
            level_0 = level_0 + i32(1);
            continue;
        }
        var uvw_0 : vec3<f32> = vec3<f32>(voxel_0.x / f32(f_0.levels_0.y), voxel_0.y / f32(f_0.levels_0.y), (f32(level_0) * f32(f_0.levels_0.y) + voxel_0.z) / f32(f_0.levels_0.y * f_0.levels_0.x));
        (*known_0) = true;
        (*raw_0) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_0), (0.0f))).x;
        (*resolved_0) = _S15;
        return ((*raw_0) * 2.0f - 1.0f) * f_0.range_0[level_0];
    }
    return 1.0e+06f;
}

fn SoftwareBlocked_0( from_0 : vec3<f32>,  direction_0 : vec3<f32>,  segment_0 : f32,  hitT_0 : ptr<function, f32>) -> bool
{
    var leaving_0 : bool;
    var _S17 : bool;
    (*hitT_0) = 0.0f;
    var _S18 : f32 = min(segment_0, 1.0f);
    var screenT_0 : f32 = 0.0f;
    var i_0 : i32 = i32(1);
    for(;;)
    {
        if(i_0 <= i32(16))
        {
        }
        else
        {
            break;
        }
        var t_0 : f32 = _S18 * (f32(i_0) / 16.0f);
        var view_0 : vec3<f32> = (((mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])) * (vec4<f32>(from_0 + direction_0 * vec3<f32>(t_0), 0.0f)))).xyz;
        var _S19 : f32 = view_0.z;
        if(_S19 <= (p_0.unproject_0.z))
        {
            break;
        }
        var uv_3 : vec2<f32> = ProjectToUv_0(view_0);
        if((any((uv_3 < vec2<f32>(0.0f, 0.0f)))))
        {
            leaving_0 = true;
        }
        else
        {
            leaving_0 = (any((uv_3 > vec2<f32>(1.0f, 1.0f))));
        }
        if(leaving_0)
        {
            break;
        }
        var depth_4 : f32 = DepthBilinear_0(uDepth_0, uDepthSampler_0, uv_3);
        if(depth_4 > 0.0f)
        {
            var sceneZ_0 : f32 = ViewDepthAt_0(depth_4, p_0.unproject_0);
            var behind_0 : f32 = _S19 - sceneZ_0;
            if(behind_0 > 0.01999999955296516f)
            {
                _S17 = behind_0 < (max(0.05000000074505806f, 0.00999999977648258f * sceneZ_0));
            }
            else
            {
                _S17 = false;
            }
            if(_S17)
            {
                (*hitT_0) = t_0;
                return true;
            }
        }
        var _S20 : i32 = i_0 + i32(1);
        screenT_0 = t_0;
        i_0 = _S20;
    }
    if((f_0.levels_0.x) <= i32(0))
    {
        return false;
    }
    var _S21 : f32 = 0.5f * f_0.origin_0[i32(0)].w;
    var _S22 : f32 = max(screenT_0, _S21);
    var _S23 : f32 = segment_0 - _S21;
    leaving_0 = true;
    var leaveBy_0 : f32 = 0.0f;
    var step_0 : i32 = i32(0);
    var travelled_0 : f32 = _S22;
    for(;;)
    {
        if(step_0 < i32(64))
        {
            _S17 = travelled_0 < _S23;
        }
        else
        {
            _S17 = false;
        }
        if(_S17)
        {
        }
        else
        {
            break;
        }
        var known_1 : bool;
        var raw_1 : f32;
        var resolved_1 : f32;
        var distance_0 : f32 = DistanceAt_0(from_0 + direction_0 * vec3<f32>(travelled_0), &(known_1), &(raw_1), &(resolved_1));
        if(!known_1)
        {
            return false;
        }
        var leaveBy_1 : f32;
        var leaving_1 : bool;
        if(leaving_0)
        {
            if(leaveBy_0 <= 0.0f)
            {
                leaveBy_1 = travelled_0 + 1.5f * resolved_1;
            }
            else
            {
                leaveBy_1 = leaveBy_0;
            }
            if(distance_0 <= 0.0f)
            {
                leaving_1 = travelled_0 < leaveBy_1;
            }
            else
            {
                leaving_1 = false;
            }
            if(leaving_1)
            {
                travelled_0 = travelled_0 + resolved_1 * 0.25f;
                var _S24 : i32 = step_0 + i32(1);
                leaveBy_0 = leaveBy_1;
                step_0 = _S24;
                continue;
            }
            leaving_1 = false;
        }
        else
        {
            leaving_1 = leaving_0;
            leaveBy_1 = leaveBy_0;
        }
        if(distance_0 < (0.5f * resolved_1 * saturate(travelled_0 / (2.0f * resolved_1))))
        {
            (*hitT_0) = travelled_0;
            return true;
        }
        var travelled_1 : f32 = travelled_0 + max(distance_0, resolved_1 * 0.25f);
        leaving_0 = leaving_1;
        travelled_0 = travelled_1;
        var _S24 : i32 = step_0 + i32(1);
        leaveBy_0 = leaveBy_1;
        step_0 = _S24;
    }
    return false;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var atten_0 : f32;
    var stopShort_0 : f32;
    var _S25 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S26 : bool;
    if((_S25.x) >= i32(p_0.resolution_0.x))
    {
        _S26 = true;
    }
    else
    {
        _S26 = (_S25.y) >= i32(p_0.resolution_0.y);
    }
    if(_S26)
    {
        return;
    }
    var lampCount_0 : i32 = i32(p_0.lampControl_0.x + 0.5f);
    if(lampCount_0 <= i32(0))
    {
        textureStore((uVisibility_0), (vec2<u32>(_S25)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var uv_4 : vec2<f32> = (vec2<f32>(_S25) + vec2<f32>(0.5f)) * p_0.resolution_0.zw;
    var depth_5 : f32 = DepthBilinear_0(uDepth_0, uDepthSampler_0, uv_4);
    if(depth_5 <= 0.0f)
    {
        textureStore((uVisibility_0), (vec2<u32>(_S25)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var viewPos_0 : vec3<f32> = ViewPositionAt_0(uv_4, depth_5, p_0.unproject_0);
    var normal_0 : vec3<f32> = normalize((textureSampleLevel((uNormal_0), (uNormalSampler_0), (uv_4), (0.0f))).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f));
    var _S27 : vec3<f32> = (((vec4<f32>(viewPos_0, 1.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + p_0.cameraFrame_0.xyz + normal_0 * vec3<f32>((0.07999999821186066f + 0.00050000002374873f * viewPos_0.z));
    var frame_0 : f32 = f32(u32(p_0.cameraFrame_0.w) % u32(65536));
    var pixelRandom_0 : vec3<f32> = PixelRandom_0(_S25);
    var chosen_0 : array<i32, i32(2)>;
    var pick_0 : array<f32, i32(2)>;
    var _S28 : f32 = fract(pixelRandom_0.x + frame_0 * 0.6180340051651001f);
    var k_0 : i32 = i32(0);
    for(;;)
    {
        if(k_0 < i32(2))
        {
        }
        else
        {
            break;
        }
        chosen_0[k_0] = i32(-1);
        pick_0[k_0] = fract(_S28 + f32(k_0) / 2.0f);
        k_0 = k_0 + i32(1);
    }
    var i_1 : i32 = i32(0);
    var weightSum_0 : f32 = 0.0f;
    for(;;)
    {
        if(i_1 < lampCount_0)
        {
        }
        else
        {
            break;
        }
        var offset_0 : vec3<f32> = p_0.lampPositionRadius_0[i_1].xyz - viewPos_0;
        var distanceSquared_0 : f32 = dot(offset_0, offset_0);
        var _S29 : f32 = p_0.lampPositionRadius_0[i_1].w;
        var reachSquared_0 : f32 = _S29 * _S29;
        if(distanceSquared_0 >= reachSquared_0)
        {
            i_1 = i_1 + i32(1);
            continue;
        }
        var toLamp_0 : vec3<f32> = normalize((((vec4<f32>(offset_0, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz);
        var nDotL_0 : f32 = dot(normal_0, toLamp_0);
        if(nDotL_0 <= 0.0f)
        {
            i_1 = i_1 + i32(1);
            continue;
        }
        var falloff_0 : f32 = saturate(1.0f - distanceSquared_0 / reachSquared_0 * (distanceSquared_0 / reachSquared_0));
        var atten_1 : f32 = falloff_0 * falloff_0 / (distanceSquared_0 + 1.0f);
        var facing_0 : vec3<f32> = p_0.lampDirection_0[i_1].xyz;
        if((dot(facing_0, facing_0)) > 0.25f)
        {
            atten_0 = atten_1 * max(dot(normalize((((vec4<f32>(facing_0, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz), (vec3<f32>(0) - toLamp_0)), 0.0f);
        }
        else
        {
            atten_0 = atten_1;
        }
        var weight_1 : f32 = atten_0 * nDotL_0 * dot(p_0.lampColourIntensity_0[i_1].xyz * vec3<f32>(p_0.lampColourIntensity_0[i_1].w), vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f));
        if(weight_1 <= 9.99999997475242708e-07f)
        {
            i_1 = i_1 + i32(1);
            continue;
        }
        var weightSum_1 : f32 = weightSum_0 + weight_1;
        var _S30 : f32 = weight_1 / max(weightSum_1, 9.99999997475242708e-07f);
        k_0 = i32(0);
        for(;;)
        {
            if(k_0 < i32(2))
            {
            }
            else
            {
                break;
            }
            if((pick_0[k_0]) < _S30)
            {
                chosen_0[k_0] = i_1;
                pick_0[k_0] = pick_0[k_0] / max(_S30, 9.99999997475242708e-07f);
            }
            else
            {
                pick_0[k_0] = (pick_0[k_0] - _S30) / max(1.0f - _S30, 9.99999997475242708e-07f);
            }
            pick_0[k_0] = min(pick_0[k_0], 0.99999994039535522f);
            k_0 = k_0 + i32(1);
        }
        weightSum_0 = weightSum_1;
        i_1 = i_1 + i32(1);
    }
    if(weightSum_0 <= 0.0f)
    {
        textureStore((uVisibility_0), (vec2<u32>(_S25)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    k_0 = i32(0);
    atten_0 = 0.0f;
    var penumbraSum_0 : f32 = 0.0f;
    var blocked_0 : f32 = 0.0f;
    for(;;)
    {
        if(k_0 < i32(2))
        {
        }
        else
        {
            break;
        }
        var lamp_0 : i32 = chosen_0[k_0];
        var lampCentre_0 : vec3<f32> = (((vec4<f32>(p_0.lampPositionRadius_0[chosen_0[k_0]].xyz, 1.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + p_0.cameraFrame_0.xyz;
        var lampDir_0 : vec3<f32> = normalize(lampCentre_0 - _S27);
        var source_0 : f32 = clamp(p_0.lampDirection_0[chosen_0[k_0]].w, 0.01999999955296516f, p_0.lampSource_0.x);
        const _S31 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        var _S32 : vec3<f32> = p_0.lampDirection_0[chosen_0[k_0]].xyz;
        var emitting_0 : vec3<f32>;
        var axis_0 : vec3<f32>;
        if((dot(_S32, _S32)) > 0.25f)
        {
            var axis_1 : vec3<f32> = normalize((((vec4<f32>(_S32, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz);
            emitting_0 = axis_1 * vec3<f32>(0.01999999955296516f);
            axis_0 = axis_1;
        }
        else
        {
            emitting_0 = _S31;
            axis_0 = lampDir_0;
        }
        var rand_0 : vec2<f32> = fract(pixelRandom_0.yz + vec2<f32>((frame_0 * 2.0f + f32(k_0))) * vec2<f32>(0.75487768650054932f, 0.56984031200408936f));
        var rect_0 : vec4<f32> = p_0.lampRect_0[lamp_0];
        var _S33 : bool = (dot(emitting_0, emitting_0)) > 0.0f;
        if(_S33)
        {
            _S26 = (rect_0.w) > 0.0f;
        }
        else
        {
            _S26 = false;
        }
        var _S34 : bool;
        if(_S26)
        {
            var _S35 : vec3<f32> = rect_0.xyz;
            _S34 = (dot(_S35, _S35)) > 0.0f;
        }
        else
        {
            _S34 = false;
        }
        var target_0 : vec3<f32>;
        if(_S34)
        {
            var halfLength_0 : vec3<f32> = (((vec4<f32>(rect_0.xyz, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz;
            var uv_5 : vec2<f32> = rand_0 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
            target_0 = lampCentre_0 + emitting_0 + halfLength_0 * vec3<f32>(uv_5.x) + normalize(cross(axis_0, halfLength_0)) * vec3<f32>(rect_0.w) * vec3<f32>(uv_5.y);
        }
        else
        {
            if((abs(axis_0.z)) < 0.89999997615814209f)
            {
                target_0 = vec3<f32>(0.0f, 0.0f, 1.0f);
            }
            else
            {
                target_0 = vec3<f32>(1.0f, 0.0f, 0.0f);
            }
            var across_0 : vec3<f32> = normalize(cross(target_0, axis_0));
            var spread_0 : f32 = sqrt(rand_0.x) * source_0;
            var around_0 : f32 = 6.28318548202514648f * rand_0.y;
            target_0 = lampCentre_0 + emitting_0 + across_0 * vec3<f32>((spread_0 * cos(around_0))) + cross(axis_0, across_0) * vec3<f32>((spread_0 * sin(around_0)));
        }
        var toTarget_0 : vec3<f32> = target_0 - _S27;
        var distance_1 : f32 = length(toTarget_0);
        var _S36 : bool;
        if(_S33)
        {
            _S36 = (rect_0.w) > 0.0f;
        }
        else
        {
            _S36 = false;
        }
        var onRect_0 : bool;
        if(_S36)
        {
            var _S37 : vec3<f32> = rect_0.xyz;
            onRect_0 = (dot(_S37, _S37)) > 0.0f;
        }
        else
        {
            onRect_0 = false;
        }
        if(onRect_0)
        {
            stopShort_0 = 0.05000000074505806f;
        }
        else
        {
            stopShort_0 = clamp(source_0 * 0.5f, 0.05000000074505806f, 0.20000000298023224f);
        }
        if(distance_1 <= (stopShort_0 + 0.01999999955296516f))
        {
            atten_0 = atten_0 + 1.0f;
            k_0 = k_0 + i32(1);
            continue;
        }
        var t_1 : f32;
        var hit_0 : bool = SoftwareBlocked_0(_S27 - p_0.cameraFrame_0.xyz, toTarget_0 / vec3<f32>(distance_1), distance_1 - stopShort_0, &(t_1));
        var visibleSum_0 : f32;
        var penumbraSum_1 : f32;
        var blocked_1 : f32;
        if(!hit_0)
        {
            visibleSum_0 = atten_0 + 1.0f;
            penumbraSum_1 = penumbraSum_0;
            blocked_1 = blocked_0;
        }
        else
        {
            if(onRect_0)
            {
                visibleSum_0 = length(rect_0.xyz) + rect_0.w;
            }
            else
            {
                visibleSum_0 = 2.0f * source_0;
            }
            var penumbraSum_2 : f32 = penumbraSum_0 + visibleSum_0 * t_1 / max(distance_1 - t_1, 0.05000000074505806f);
            var blocked_2 : f32 = blocked_0 + 1.0f;
            visibleSum_0 = atten_0;
            penumbraSum_1 = penumbraSum_2;
            blocked_1 = blocked_2;
        }
        atten_0 = visibleSum_0;
        penumbraSum_0 = penumbraSum_1;
        blocked_0 = blocked_1;
        k_0 = k_0 + i32(1);
    }
    if(blocked_0 > 0.0f)
    {
        stopShort_0 = clamp(penumbraSum_0 / blocked_0, 0.0020000000949949f, 1.0f);
    }
    else
    {
        stopShort_0 = 0.0020000000949949f;
    }
    textureStore((uVisibility_0), (vec2<u32>(_S25)), (vec4<f32>(atten_0 / 2.0f, stopShort_0, 0.0f, 0.0f)));
    return;
}

