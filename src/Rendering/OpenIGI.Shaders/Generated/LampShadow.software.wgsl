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
    @align(16) sunLight_0 : vec4<f32>,
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

@binding(10) @group(0) var uSunVisibility_0 : texture_2d<f32>;

struct _Array_std140_matrixx3Cfloatx2C4x2C4x3E16_0
{
    @align(16) data_1 : array<_MatrixStorage_float4x4_ColMajorstd140_0, i32(16)>,
};

struct FieldParams_std140_0
{
    @align(16) origin_0 : array<vec4<f32>, i32(4)>,
    @align(16) range_0 : vec4<f32>,
    @align(16) levels_0 : vec4<i32>,
    @align(16) mapMatrix_0 : _Array_std140_matrixx3Cfloatx2C4x2C4x3E16_0,
    @align(16) mapSlot_0 : array<vec4<i32>, i32(40)>,
};

@binding(9) @group(0) var<uniform> f_0 : FieldParams_std140_0;
@binding(11) @group(0) var uLampMaps_0 : texture_depth_2d_array;

@binding(12) @group(0) var uLampMapSampler_0 : sampler_comparison;

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

fn LampMapVisibility_0( lamp_0 : i32,  cameraRelative_0 : vec3<f32>,  cosToLamp_0 : f32) -> f32
{
    var slot_0 : i32 = f_0.mapSlot_0[(lamp_0 >> (u32(2)))][(lamp_0 & (i32(3)))];
    if((f_0.mapSlot_0[(lamp_0 >> (u32(2)))][(lamp_0 & (i32(3)))]) < i32(0))
    {
        return -1.0f;
    }
    var _S13 : mat4x4<f32> = mat4x4<f32>(f_0.mapMatrix_0.data_1[slot_0].data_0[i32(0)][i32(0)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(1)][i32(0)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(2)][i32(0)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(3)][i32(0)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(0)][i32(1)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(1)][i32(1)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(2)][i32(1)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(3)][i32(1)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(0)][i32(2)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(1)][i32(2)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(2)][i32(2)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(3)][i32(2)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(0)][i32(3)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(1)][i32(3)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(2)][i32(3)], f_0.mapMatrix_0.data_1[slot_0].data_0[i32(3)][i32(3)]);
    var clip_0 : vec4<f32> = (((vec4<f32>(cameraRelative_0, 1.0f)) * (_S13)));
    var _S14 : f32 = clip_0.w;
    if(_S14 <= 0.05999999865889549f)
    {
        return 1.0f;
    }
    var ndc_1 : vec3<f32> = clip_0.xyz / vec3<f32>(_S14);
    if((any(((abs(ndc_1.xy)) > vec2<f32>(0.98000001907348633f, 0.98000001907348633f)))))
    {
        return -1.0f;
    }
    var cosine_0 : f32 = clamp(cosToLamp_0, 0.05000000074505806f, 1.0f);
    var _S15 : f32 = (_S13[i32(2)][i32(2)] + _S13[i32(2)][i32(3)] / max(_S14 - (0.02999999932944775f + 0.00499999988824129f * _S14 + _S14 * (2.0f / max(length(vec3<f32>(_S13[i32(0)][i32(0)], _S13[i32(0)][i32(1)], _S13[i32(0)][i32(2)])), 0.00100000004749745f)) / 512.0f * (1.5f + min(sqrt(1.0f - cosine_0 * cosine_0) / cosine_0, 12.0f))), 0.05999999865889549f)) * 0.5f + 0.5f;
    var _S16 : vec2<f32> = vec2<f32>(ndc_1.x * 0.5f + 0.5f, 0.5f - ndc_1.y * 0.5f);
    var width_1 : u32;
    var height_1 : u32;
    var layers_0 : u32;
    {var dim = textureDimensions((uLampMaps_0));((width_1)) = dim.x;((height_1)) = dim.y;((layers_0)) = textureNumLayers((uLampMaps_0));};
    var _S17 : f32 = 1.0f / f32(max(width_1, u32(1)));
    var y_0 : i32 = i32(-1);
    var sum_0 : f32 = 0.0f;
    for(;;)
    {
        if(y_0 <= i32(1))
        {
        }
        else
        {
            break;
        }
        var x_0 : i32 = i32(-1);
        for(;;)
        {
            if(x_0 <= i32(1))
            {
            }
            else
            {
                break;
            }
            var _S18 : vec3<f32> = vec3<f32>(_S16 + vec2<f32>(f32(x_0), f32(y_0)) * vec2<f32>(_S17), f32(slot_0));
            var sum_1 : f32 = sum_0 + (textureSampleCompareLevel((uLampMaps_0), (uLampMapSampler_0), ((_S18)).xy, i32(((_S18)).z), (_S15)));
            x_0 = x_0 + i32(1);
            sum_0 = sum_1;
        }
        y_0 = y_0 + i32(1);
    }
    return sum_0 / 9.0f;
}

fn ProjectToUv_0( viewPosition_0 : vec3<f32>) -> vec2<f32>
{
    var _S19 : f32 = viewPosition_0.z;
    var _S20 : vec2<f32> = vec2<f32>(0.5f);
    return vec2<f32>(viewPosition_0.x * p_0.unproject_0.x / _S19, - viewPosition_0.y * p_0.unproject_0.y / _S19) * _S20 + _S20;
}

fn LampLevelDistance_0( level_0 : i32,  voxel_0 : vec3<f32>) -> f32
{
    return ((textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (vec3<f32>(voxel_0.x / f32(f_0.levels_0.y), voxel_0.y / f32(f_0.levels_0.y), (f32(level_0) * f32(f_0.levels_0.y) + voxel_0.z) / f32(f_0.levels_0.y * f_0.levels_0.x))), (0.0f))).x * 2.0f - 1.0f) * f_0.range_0[level_0];
}

fn LampDistanceAt_0( world_0 : vec3<f32>,  known_0 : ptr<function, bool>,  resolved_0 : ptr<function, f32>) -> f32
{
    (*known_0) = false;
    (*resolved_0) = f_0.origin_0[i32(0)].w;
    var level_1 : i32 = i32(0);
    for(;;)
    {
        if(level_1 < (f_0.levels_0.x))
        {
        }
        else
        {
            break;
        }
        var _S21 : f32 = f_0.origin_0[level_1].w;
        var voxel_1 : vec3<f32> = (world_0 - f_0.origin_0[level_1].xyz) / vec3<f32>(_S21);
        var top_0 : f32 = f32(f_0.levels_0.y) - 0.5f;
        const _S22 : vec3<f32> = vec3<f32>(0.5f, 0.5f, 0.5f);
        var _S23 : bool;
        if((any((voxel_1 < _S22))))
        {
            _S23 = true;
        }
        else
        {
            _S23 = (any((voxel_1 > vec3<f32>(top_0, top_0, top_0))));
        }
        if(_S23)
        {
            level_1 = level_1 + i32(1);
            continue;
        }
        (*known_0) = true;
        var here_0 : f32 = LampLevelDistance_0(level_1, voxel_1);
        (*resolved_0) = _S21;
        var fromEdge_0 : vec3<f32> = min(voxel_1 - vec3<f32>(0.5f), vec3<f32>(top_0) - voxel_1);
        var handOver_0 : f32 = saturate(1.0f - min(fromEdge_0.x, min(fromEdge_0.y, fromEdge_0.z)) / 8.0f);
        if(handOver_0 <= 0.0f)
        {
            _S23 = true;
        }
        else
        {
            _S23 = (level_1 + i32(1)) >= (f_0.levels_0.x);
        }
        if(_S23)
        {
            return here_0;
        }
        var _S24 : i32 = level_1 + i32(1);
        var _S25 : f32 = f_0.origin_0[_S24].w;
        var outerVoxel_0 : vec3<f32> = (world_0 - f_0.origin_0[_S24].xyz) / vec3<f32>(_S25);
        if((any((outerVoxel_0 < _S22))))
        {
            _S23 = true;
        }
        else
        {
            _S23 = (any((outerVoxel_0 > vec3<f32>(top_0, top_0, top_0))));
        }
        if(_S23)
        {
            return here_0;
        }
        (*resolved_0) = mix(_S21, _S25, handOver_0);
        return mix(here_0, LampLevelDistance_0(_S24, outerVoxel_0), handOver_0);
    }
    return 1.0e+06f;
}

fn SoftwareBlocked_0( from_0 : vec3<f32>,  direction_0 : vec3<f32>,  segment_0 : f32,  receiverLift_0 : f32,  receiverRise_0 : f32,  lampDistance_0 : f32,  lampRise_0 : f32,  hitT_0 : ptr<function, f32>,  nearMiss_0 : ptr<function, f32>) -> bool
{
    var leaving_0 : bool;
    var _S26 : bool;
    (*hitT_0) = 0.0f;
    var _S27 : f32 = min(segment_0, 1.0f);
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
        var t_0 : f32 = _S27 * (f32(i_0) / 16.0f);
        var view_0 : vec3<f32> = (((mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])) * (vec4<f32>(from_0 + direction_0 * vec3<f32>(t_0), 0.0f)))).xyz;
        var _S28 : f32 = view_0.z;
        if(_S28 <= (p_0.unproject_0.z))
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
            var behind_0 : f32 = _S28 - sceneZ_0;
            if(behind_0 > 0.01999999955296516f)
            {
                _S26 = behind_0 < (max(0.05000000074505806f, 0.00999999977648258f * sceneZ_0));
            }
            else
            {
                _S26 = false;
            }
            if(_S26)
            {
                (*hitT_0) = t_0;
                return true;
            }
        }
        var _S29 : i32 = i_0 + i32(1);
        screenT_0 = t_0;
        i_0 = _S29;
    }
    if((f_0.levels_0.x) <= i32(0))
    {
        return false;
    }
    var _S30 : f32 = 0.5f * f_0.origin_0[i32(0)].w;
    var _S31 : f32 = max(screenT_0, _S30);
    var end_0 : f32 = segment_0 - _S30;
    var startKnown_0 : bool;
    var startResolved_0 : f32;
    var _S32 : f32 = LampDistanceAt_0(from_0 + direction_0 * vec3<f32>(_S31), &(startKnown_0), &(startResolved_0));
    var endKnown_0 : bool;
    var endResolved_0 : f32;
    var _S33 : f32 = LampDistanceAt_0(from_0 + direction_0 * vec3<f32>(end_0), &(endKnown_0), &(endResolved_0));
    leaving_0 = true;
    var leaveBy_0 : f32 = 0.0f;
    var step_0 : i32 = i32(0);
    var travelled_0 : f32 = _S31;
    for(;;)
    {
        if(step_0 < i32(64))
        {
            _S26 = travelled_0 < end_0;
        }
        else
        {
            _S26 = false;
        }
        if(_S26)
        {
        }
        else
        {
            break;
        }
        var known_1 : bool;
        var resolved_1 : f32;
        var distance_0 : f32 = LampDistanceAt_0(from_0 + direction_0 * vec3<f32>(travelled_0), &(known_1), &(resolved_1));
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
                var _S34 : i32 = step_0 + i32(1);
                leaveBy_0 = leaveBy_1;
                step_0 = _S34;
                continue;
            }
            leaving_1 = false;
        }
        else
        {
            leaving_1 = leaving_0;
            leaveBy_1 = leaveBy_0;
        }
        var hitRadius_0 : f32 = 0.5f * resolved_1 * saturate(travelled_0 / (2.0f * resolved_1));
        var margin_0 : f32 = 0.25f * resolved_1;
        var aboveReceiver_0 : f32;
        if(startKnown_0)
        {
            aboveReceiver_0 = _S32 + (travelled_0 - _S31) * receiverRise_0;
        }
        else
        {
            aboveReceiver_0 = receiverLift_0 + travelled_0 * receiverRise_0;
        }
        var toLamp_0 : f32 = lampDistance_0 - travelled_0;
        var _S35 : bool = lampRise_0 <= 0.0f;
        var belowLamp_0 : f32;
        if(_S35)
        {
            belowLamp_0 = 1.0e+09f;
        }
        else
        {
            if(endKnown_0)
            {
                screenT_0 = _S33 + (end_0 - travelled_0) * lampRise_0;
            }
            else
            {
                screenT_0 = toLamp_0 * lampRise_0;
            }
            belowLamp_0 = screenT_0;
        }
        var atFitting_0 : bool;
        if(_S35)
        {
            atFitting_0 = toLamp_0 < (1.5f * resolved_1);
        }
        else
        {
            atFitting_0 = false;
        }
        var touching_0 : bool = distance_0 < hitRadius_0;
        var _S36 : bool;
        var _S37 : bool;
        if(touching_0)
        {
            if(atFitting_0)
            {
                _S36 = true;
            }
            else
            {
                _S36 = distance_0 >= (aboveReceiver_0 - margin_0);
            }
            if(_S36)
            {
                _S37 = true;
            }
            else
            {
                _S37 = distance_0 >= (belowLamp_0 - 0.25f * resolved_1);
            }
            _S36 = _S37;
        }
        else
        {
            _S36 = false;
        }
        if(_S36)
        {
            (*nearMiss_0) = 1.0f;
        }
        if(!atFitting_0)
        {
            _S37 = touching_0;
        }
        else
        {
            _S37 = false;
        }
        var _S38 : bool;
        if(_S37)
        {
            _S38 = distance_0 < (aboveReceiver_0 - margin_0);
        }
        else
        {
            _S38 = false;
        }
        var _S39 : bool;
        if(_S38)
        {
            _S39 = distance_0 < (belowLamp_0 - 0.25f * resolved_1);
        }
        else
        {
            _S39 = false;
        }
        if(_S39)
        {
            (*hitT_0) = travelled_0;
            return true;
        }
        var travelled_1 : f32 = travelled_0 + max(distance_0, resolved_1 * 0.25f);
        leaving_0 = leaving_1;
        travelled_0 = travelled_1;
        var _S34 : i32 = step_0 + i32(1);
        leaveBy_0 = leaveBy_1;
        step_0 = _S34;
    }
    return false;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var atten_0 : f32;
    var stopShort_0 : f32;
    var _S40 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S41 : bool;
    if((_S40.x) >= i32(p_0.resolution_0.x))
    {
        _S41 = true;
    }
    else
    {
        _S41 = (_S40.y) >= i32(p_0.resolution_0.y);
    }
    if(_S41)
    {
        return;
    }
    var lampCount_0 : i32 = i32(p_0.lampControl_0.x + 0.5f);
    if(lampCount_0 <= i32(0))
    {
        textureStore((uVisibility_0), (vec2<u32>(_S40)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var uv_4 : vec2<f32> = (vec2<f32>(_S40) + vec2<f32>(0.5f)) * p_0.resolution_0.zw;
    var depth_5 : f32 = DepthBilinear_0(uDepth_0, uDepthSampler_0, uv_4);
    if(depth_5 <= 0.0f)
    {
        textureStore((uVisibility_0), (vec2<u32>(_S40)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var viewPos_0 : vec3<f32> = ViewPositionAt_0(uv_4, depth_5, p_0.unproject_0);
    var normal_0 : vec3<f32> = normalize((textureSampleLevel((uNormal_0), (uNormalSampler_0), (uv_4), (0.0f))).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f));
    var originLift_0 : f32 = 0.07999999821186066f + 0.00050000002374873f * viewPos_0.z;
    var _S42 : vec3<f32> = (((vec4<f32>(viewPos_0, 1.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + p_0.cameraFrame_0.xyz + normal_0 * vec3<f32>(originLift_0);
    var frame_0 : f32 = f32(u32(p_0.cameraFrame_0.w) % u32(65536));
    var pixelRandom_0 : vec3<f32> = PixelRandom_0(_S40);
    var chosen_0 : array<i32, i32(2)>;
    var pick_0 : array<f32, i32(2)>;
    var _S43 : f32 = fract(pixelRandom_0.x + frame_0 * 0.6180340051651001f);
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
        pick_0[k_0] = fract(_S43 + f32(k_0) / 2.0f);
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
        var _S44 : f32 = p_0.lampPositionRadius_0[i_1].w;
        var reachSquared_0 : f32 = _S44 * _S44;
        if(distanceSquared_0 >= reachSquared_0)
        {
            i_1 = i_1 + i32(1);
            continue;
        }
        var toLamp_1 : vec3<f32> = normalize((((vec4<f32>(offset_0, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz);
        var nDotL_0 : f32 = dot(normal_0, toLamp_1);
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
            atten_0 = atten_1 * max(dot(normalize((((vec4<f32>(facing_0, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz), (vec3<f32>(0) - toLamp_1)), 0.0f);
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
        var _S45 : f32 = weight_1 / max(weightSum_1, 9.99999997475242708e-07f);
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
            if((pick_0[k_0]) < _S45)
            {
                chosen_0[k_0] = i_1;
                pick_0[k_0] = pick_0[k_0] / max(_S45, 9.99999997475242708e-07f);
            }
            else
            {
                pick_0[k_0] = (pick_0[k_0] - _S45) / max(1.0f - _S45, 9.99999997475242708e-07f);
            }
            pick_0[k_0] = min(pick_0[k_0], 0.99999994039535522f);
            k_0 = k_0 + i32(1);
        }
        weightSum_0 = weightSum_1;
        i_1 = i_1 + i32(1);
    }
    if((p_0.sunLight_0.w) > 0.0f)
    {
        _S41 = weightSum_0 > 0.0f;
    }
    else
    {
        _S41 = false;
    }
    if(_S41)
    {
        var _S46 : vec3<i32> = vec3<i32>(_S40, i32(0));
        atten_0 = p_0.sunLight_0.w * max(dot(normal_0, p_0.sunLight_0.xyz), 0.0f) * saturate((textureLoad((uSunVisibility_0), ((_S46)).xy, ((_S46)).z)).x);
    }
    else
    {
        atten_0 = 0.0f;
    }
    if(weightSum_0 <= 0.0f)
    {
        _S41 = true;
    }
    else
    {
        _S41 = weightSum_0 < (p_0.lampSource_0.w);
    }
    if(_S41)
    {
        _S41 = true;
    }
    else
    {
        _S41 = weightSum_0 < (0.00999999977648258f * atten_0);
    }
    if(_S41)
    {
        textureStore((uVisibility_0), (vec2<u32>(_S40)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    k_0 = i32(0);
    var visibleSum_0 : f32 = 0.0f;
    var blocked_0 : f32 = 0.0f;
    var penumbraSum_0 : f32 = 0.0f;
    var nearMissSum_0 : f32 = 0.0f;
    for(;;)
    {
        if(k_0 < i32(2))
        {
        }
        else
        {
            break;
        }
        var lamp_1 : i32 = chosen_0[k_0];
        var lampCentre_0 : vec3<f32> = (((vec4<f32>(p_0.lampPositionRadius_0[chosen_0[k_0]].xyz, 1.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + p_0.cameraFrame_0.xyz;
        var lampDir_0 : vec3<f32> = normalize(lampCentre_0 - _S42);
        var source_0 : f32 = clamp(p_0.lampDirection_0[chosen_0[k_0]].w, 0.01999999955296516f, p_0.lampSource_0.x);
        const _S47 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        var _S48 : vec3<f32> = p_0.lampDirection_0[chosen_0[k_0]].xyz;
        var emitting_0 : vec3<f32>;
        var axis_0 : vec3<f32>;
        if((dot(_S48, _S48)) > 0.25f)
        {
            var axis_1 : vec3<f32> = normalize((((vec4<f32>(_S48, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz);
            emitting_0 = axis_1 * vec3<f32>(0.01999999955296516f);
            axis_0 = axis_1;
        }
        else
        {
            emitting_0 = _S47;
            axis_0 = lampDir_0;
        }
        var rand_0 : vec2<f32> = fract(pixelRandom_0.yz + vec2<f32>((frame_0 * 2.0f + f32(k_0))) * vec2<f32>(0.75487768650054932f, 0.56984031200408936f));
        var rect_0 : vec4<f32> = p_0.lampRect_0[lamp_1];
        var _S49 : bool = (dot(emitting_0, emitting_0)) > 0.0f;
        if(_S49)
        {
            _S41 = (rect_0.w) > 0.0f;
        }
        else
        {
            _S41 = false;
        }
        var _S50 : bool;
        if(_S41)
        {
            var _S51 : vec3<f32> = rect_0.xyz;
            _S50 = (dot(_S51, _S51)) > 0.0f;
        }
        else
        {
            _S50 = false;
        }
        var target_0 : vec3<f32>;
        if(_S50)
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
        var toTarget_0 : vec3<f32> = target_0 - _S42;
        var distance_1 : f32 = length(toTarget_0);
        var _S52 : bool;
        if(_S49)
        {
            _S52 = (rect_0.w) > 0.0f;
        }
        else
        {
            _S52 = false;
        }
        var onRect_0 : bool;
        if(_S52)
        {
            var _S53 : vec3<f32> = rect_0.xyz;
            onRect_0 = (dot(_S53, _S53)) > 0.0f;
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
        var blocked_1 : f32;
        var penumbraSum_1 : f32;
        if(distance_1 <= (stopShort_0 + 0.01999999955296516f))
        {
            visibleSum_0 = visibleSum_0 + 1.0f;
            blocked_1 = blocked_0;
            penumbraSum_1 = penumbraSum_0;
            k_0 = k_0 + i32(1);
            blocked_0 = blocked_1;
            penumbraSum_0 = penumbraSum_1;
            continue;
        }
        var t_1 : f32;
        var nearMiss_1 : f32 = 0.0f;
        var mapped_0 : f32 = LampMapVisibility_0(lamp_1, _S42 - p_0.cameraFrame_0.xyz, dot(normal_0, normalize(toTarget_0)));
        if(mapped_0 >= 0.0f)
        {
            var visibleSum_1 : f32 = visibleSum_0 + mapped_0;
            if(mapped_0 < 1.0f)
            {
                var _S54 : f32 = 1.0f - mapped_0;
                var penumbraSum_2 : f32 = penumbraSum_0 + _S54 * 0.05000000074505806f;
                blocked_1 = blocked_0 + _S54;
                penumbraSum_1 = penumbraSum_2;
            }
            else
            {
                blocked_1 = blocked_0;
                penumbraSum_1 = penumbraSum_0;
            }
            visibleSum_0 = visibleSum_1;
            k_0 = k_0 + i32(1);
            blocked_0 = blocked_1;
            penumbraSum_0 = penumbraSum_1;
            continue;
        }
        var rayDirection_0 : vec3<f32> = toTarget_0 / vec3<f32>(distance_1);
        var _S55 : vec3<f32> = _S42 - p_0.cameraFrame_0.xyz;
        var _S56 : f32 = distance_1 - stopShort_0;
        var _S57 : f32 = max(dot(rayDirection_0, normal_0), 0.0f);
        if(_S49)
        {
            weightSum_0 = dot((vec3<f32>(0) - rayDirection_0), axis_0);
        }
        else
        {
            weightSum_0 = 0.0f;
        }
        var hit_0 : bool = SoftwareBlocked_0(_S55, rayDirection_0, _S56, originLift_0, _S57, distance_1, weightSum_0, &(t_1), &(nearMiss_1));
        var nearMissSum_1 : f32 = nearMissSum_0 + nearMiss_1;
        var visibleSum_2 : f32;
        if(!hit_0)
        {
            visibleSum_2 = visibleSum_0 + 1.0f;
            blocked_1 = blocked_0;
            penumbraSum_1 = penumbraSum_0;
        }
        else
        {
            if(onRect_0)
            {
                visibleSum_2 = length(rect_0.xyz) + rect_0.w;
            }
            else
            {
                visibleSum_2 = 2.0f * source_0;
            }
            var penumbraSum_3 : f32 = penumbraSum_0 + visibleSum_2 * t_1 / max(distance_1 - t_1, 0.05000000074505806f);
            var blocked_2 : f32 = blocked_0 + 1.0f;
            visibleSum_2 = visibleSum_0;
            blocked_1 = blocked_2;
            penumbraSum_1 = penumbraSum_3;
        }
        visibleSum_0 = visibleSum_2;
        nearMissSum_0 = nearMissSum_1;
        k_0 = k_0 + i32(1);
        blocked_0 = blocked_1;
        penumbraSum_0 = penumbraSum_1;
    }
    if(blocked_0 > 0.0f)
    {
        stopShort_0 = clamp(penumbraSum_0 / blocked_0, 0.0020000000949949f, 1.0f);
    }
    else
    {
        stopShort_0 = 0.0020000000949949f;
    }
    if((f_0.levels_0.z) != i32(0))
    {
        textureStore((uVisibility_0), (vec2<u32>(_S40)), (vec4<f32>(blocked_0 / 2.0f, max(nearMissSum_0 / 2.0f, 0.0020000000949949f), 0.0f, 0.0f)));
        return;
    }
    textureStore((uVisibility_0), (vec2<u32>(_S40)), (vec4<f32>(visibleSum_0 / 2.0f, stopShort_0, 0.0f, 0.0f)));
    return;
}

