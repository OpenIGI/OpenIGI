struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct _Array_std140_matrixx3Cfloatx2C4x2C4x3E4_0
{
    @align(16) data_1 : array<_MatrixStorage_float4x4_ColMajorstd140_0, i32(4)>,
};

struct FlashlightParams_std140_0
{
    @align(16) position_0 : vec4<f32>,
    @align(16) axis_0 : vec4<f32>,
    @align(16) up_0 : vec4<f32>,
    @align(16) colour_0 : vec4<f32>,
    @align(16) cone_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

struct FogParams_std140_0
{
    @align(16) viewToWorld_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) prevWorldToView_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) cascade_0 : _Array_std140_matrixx3Cfloatx2C4x2C4x3E4_0,
    @align(16) cascadeSplits_0 : vec4<f32>,
    @align(16) grid_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) prevUnproject_0 : vec4<f32>,
    @align(16) zParams_0 : vec4<f32>,
    @align(16) medium_0 : vec4<f32>,
    @align(16) march_0 : vec4<f32>,
    @align(16) albedo_0 : vec4<f32>,
    @align(16) sun_0 : vec4<f32>,
    @align(16) sunColour_0 : vec4<f32>,
    @align(16) ambient_0 : vec4<f32>,
    @align(16) camera_0 : vec4<f32>,
    @align(16) jitter_0 : vec4<f32>,
    @align(16) cameraWorld_0 : vec4<f32>,
    @align(16) shelter0_0 : vec4<f32>,
    @align(16) shelter1_0 : vec4<f32>,
    @align(16) shelter2_0 : vec4<f32>,
    @align(16) volumeCentre_0 : array<vec4<f32>, i32(8)>,
    @align(16) volumeExtent_0 : array<vec4<f32>, i32(8)>,
    @align(16) lampPosition_0 : array<vec4<f32>, i32(160)>,
    @align(16) lampColour_0 : array<vec4<f32>, i32(160)>,
    @align(16) flashlight_0 : FlashlightParams_std140_0,
    @align(16) phase_0 : vec4<f32>,
    @align(16) dust_0 : vec4<f32>,
    @align(16) records_0 : vec4<f32>,
    @align(16) fieldMisc_0 : vec4<f32>,
    @align(16) fieldOrigin_0 : array<vec4<f32>, i32(4)>,
    @align(16) fieldRange_0 : vec4<f32>,
    @align(16) lampDirection_0 : array<vec4<f32>, i32(160)>,
};

@binding(9) @group(0) var<uniform> p_0 : FogParams_std140_0;
@binding(7) @group(0) var uShelterMap_0 : texture_2d<f32>;

@binding(10) @group(0) var uClipmaps_0 : texture_3d<f32>;

@binding(11) @group(0) var uClipmapsSampler_0 : sampler;

@binding(0) @group(0) var uShadowMap_0 : texture_depth_2d_array;

struct _Array_std140_matrixx3Cfloatx2C4x2C4x3E240_0
{
    @align(16) data_2 : array<_MatrixStorage_float4x4_ColMajorstd140_0, i32(240)>,
};

struct FogLampMapParams_std140_0
{
    @align(16) origin_0 : array<vec4<f32>, i32(4)>,
    @align(16) range_0 : vec4<f32>,
    @align(16) levels_0 : vec4<i32>,
    @align(16) mapMatrix_0 : _Array_std140_matrixx3Cfloatx2C4x2C4x3E240_0,
    @align(16) mapDepth_0 : array<vec4<f32>, i32(120)>,
    @align(16) mapSlot_0 : array<vec4<i32>, i32(40)>,
};

@binding(15) @group(0) var<uniform> uFogLampMapParams_0 : FogLampMapParams_std140_0;
@binding(14) @group(0) var uFogLampMaps_0 : texture_depth_2d_array;

@binding(2) @group(0) var uSkyIrradiance_0 : texture_2d<f32>;

@binding(4) @group(0) var uHistory_0 : texture_3d<f32>;

@binding(5) @group(0) var uHistorySampler_0 : sampler;

@binding(6) @group(0) var uScattering_0 : texture_storage_3d<rgba16float, write>;

fn Hash31_0( q_0 : vec3<f32>) -> f32
{
    var _S1 : vec3<f32> = fract(q_0 * vec3<f32>(0.1031000018119812f));
    var _S2 : vec3<f32> = _S1 + vec3<f32>(dot(_S1, _S1.zyx + vec3<f32>(31.31999969482421875f)));
    return fract((_S2.x + _S2.y) * _S2.z);
}

fn SliceViewDepth_0( slice_0 : f32) -> f32
{
    return (exp2(slice_0 / p_0.zParams_0.z) - p_0.zParams_0.y) / p_0.zParams_0.x;
}

fn ViewPositionAt_0( uv_0 : vec2<f32>,  viewZ_0 : f32) -> vec3<f32>
{
    var ndc_0 : vec2<f32> = uv_0 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / p_0.unproject_0.x, - ndc_0.y * viewZ_0 / p_0.unproject_0.y, viewZ_0);
}

fn Indoors_0( world_0 : vec3<f32>) -> bool
{
    if((p_0.shelter1_0.w) < 0.5f)
    {
        return false;
    }
    var absolute_0 : vec3<f32> = world_0 + p_0.cameraWorld_0.xyz;
    var _S3 : vec2<i32> = vec2<i32>(floor((absolute_0.xy - p_0.shelter0_0.xy) * vec2<f32>(p_0.shelter0_0.z)));
    var _S4 : i32 = _S3.x;
    var _S5 : bool;
    if(_S4 < i32(0))
    {
        _S5 = true;
    }
    else
    {
        _S5 = (_S3.y) < i32(0);
    }
    if(_S5)
    {
        _S5 = true;
    }
    else
    {
        _S5 = _S4 >= i32(p_0.shelter2_0.x);
    }
    if(_S5)
    {
        _S5 = true;
    }
    else
    {
        _S5 = (_S3.y) >= i32(p_0.shelter2_0.y);
    }
    if(_S5)
    {
        return false;
    }
    var _S6 : vec3<i32> = vec3<i32>(_S3, i32(0));
    var texel_0 : vec4<f32> = (textureLoad((uShelterMap_0), ((_S6)).xy, ((_S6)).z));
    var packed_0 : f32 = floor(texel_0.z * 255.0f + 0.5f) + floor(texel_0.y * 255.0f + 0.5f) * 256.0f;
    if(packed_0 < 0.5f)
    {
        return false;
    }
    var surface_0 : f32 = p_0.shelter0_0.w + (packed_0 - 1.0f) * p_0.shelter1_0.x;
    var _S7 : f32 = absolute_0.z;
    if(surface_0 > (_S7 + p_0.shelter1_0.y))
    {
        _S5 = surface_0 < (_S7 + p_0.shelter1_0.z);
    }
    else
    {
        _S5 = false;
    }
    return _S5;
}

fn VolumeScaleAt_0( world_1 : vec3<f32>) -> f32
{
    var scale_0 : f32 = 1.0f;
    var i_0 : i32 = i32(0);
    for(;;)
    {
        if(i_0 < i32(8))
        {
        }
        else
        {
            break;
        }
        if((p_0.volumeExtent_0[i_0].x) <= 0.0f)
        {
            i_0 = i_0 + i32(1);
            continue;
        }
        var delta_0 : vec3<f32> = abs(world_1 - p_0.volumeCentre_0[i_0].xyz);
        var reach_0 : f32;
        if((p_0.volumeCentre_0[i_0].w) > 0.5f)
        {
            reach_0 = length(delta_0) / max(p_0.volumeExtent_0[i_0].x, 0.00100000004749745f);
        }
        else
        {
            reach_0 = max(delta_0.x / max(p_0.volumeExtent_0[i_0].x, 0.00100000004749745f), max(delta_0.y / max(p_0.volumeExtent_0[i_0].y, 0.00100000004749745f), delta_0.z / max(p_0.volumeExtent_0[i_0].z, 0.00100000004749745f)));
        }
        scale_0 = mix(scale_0, p_0.volumeExtent_0[i_0].w, 1.0f - smoothstep(0.69999998807907104f, 1.0f, reach_0));
        i_0 = i_0 + i32(1);
    }
    return scale_0;
}

fn ValueNoise_0( x_0 : vec3<f32>) -> f32
{
    var i_1 : vec3<f32> = floor(x_0);
    var f_0 : vec3<f32> = fract(x_0);
    var u_0 : vec3<f32> = f_0 * f_0 * (vec3<f32>(3.0f) - vec3<f32>(2.0f) * f_0);
    var _S8 : f32 = u_0.x;
    var _S9 : f32 = u_0.y;
    return mix(mix(mix(Hash31_0(i_1), Hash31_0(i_1 + vec3<f32>(1.0f, 0.0f, 0.0f)), _S8), mix(Hash31_0(i_1 + vec3<f32>(0.0f, 1.0f, 0.0f)), Hash31_0(i_1 + vec3<f32>(1.0f, 1.0f, 0.0f)), _S8), _S9), mix(mix(Hash31_0(i_1 + vec3<f32>(0.0f, 0.0f, 1.0f)), Hash31_0(i_1 + vec3<f32>(1.0f, 0.0f, 1.0f)), _S8), mix(Hash31_0(i_1 + vec3<f32>(0.0f, 1.0f, 1.0f)), Hash31_0(i_1 + vec3<f32>(1.0f, 1.0f, 1.0f)), _S8), _S9), u_0.z);
}

fn DensityVariation_0( world_2 : vec3<f32>) -> f32
{
    var amount_0 : f32 = p_0.dust_0.w;
    if(amount_0 <= 0.0f)
    {
        return 1.0f;
    }
    var _S10 : vec3<f32> = vec3<f32>(4096.0f);
    var absolute_1 : vec3<f32> = world_2 + fract(p_0.cameraWorld_0.xyz / _S10) * _S10;
    var drift_0 : vec3<f32> = vec3<f32>(0.60000002384185791f, 0.25f, 0.03999999910593033f) * vec3<f32>(p_0.phase_0.z);
    return mix(1.0f, 0.30000001192092896f + 1.39999997615814209f * (ValueNoise_0((absolute_1 + drift_0) / vec3<f32>(24.0f)) * 0.69999998807907104f + ValueNoise_0((absolute_1 + drift_0 * vec3<f32>(1.70000004768371582f)) / vec3<f32>(9.0f)) * 0.30000001192092896f), amount_0);
}

fn DensityAt_0( world_3 : vec3<f32>,  indoors_0 : bool) -> f32
{
    if(indoors_0)
    {
        return 0.0f;
    }
    return max(p_0.medium_0.x * mix(exp(- max(world_3.z - p_0.medium_0.z, 0.0f) * p_0.medium_0.y), 1.0f, p_0.march_0.x) * VolumeScaleAt_0(world_3) * DensityVariation_0(world_3), 0.0f);
}

fn DustAt_0( world_4 : vec3<f32>,  viewZ_1 : f32,  indoors_1 : bool) -> f32
{
    var _S11 : f32 = max(p_0.dust_0.y, 1.0f);
    if(indoors_1)
    {
        return max(p_0.dust_0.z, 0.0f);
    }
    return max(p_0.dust_0.x * (1.0f - smoothstep(0.5f * _S11, _S11, viewZ_1)) * DensityVariation_0(world_4), 0.0f);
}

fn DistanceAt_0( world_5 : vec3<f32>,  known_0 : ptr<function, bool>,  raw_0 : ptr<function, f32>,  resolved_0 : ptr<function, f32>) -> f32
{
    (*known_0) = false;
    (*raw_0) = 0.0f;
    (*resolved_0) = p_0.fieldOrigin_0[i32(0)].w;
    var level_0 : i32 = i32(0);
    for(;;)
    {
        if(level_0 < i32(p_0.fieldMisc_0.x + 0.5f))
        {
        }
        else
        {
            break;
        }
        var _S12 : f32 = p_0.fieldOrigin_0[level_0].w;
        var voxel_0 : vec3<f32> = (world_5 - p_0.fieldOrigin_0[level_0].xyz) / vec3<f32>(_S12);
        var _S13 : bool;
        if((any((voxel_0 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S13 = true;
        }
        else
        {
            _S13 = (any((voxel_0 > vec3<f32>(f32(i32(p_0.fieldMisc_0.y + 0.5f)) - 0.5f, f32(i32(p_0.fieldMisc_0.y + 0.5f)) - 0.5f, f32(i32(p_0.fieldMisc_0.y + 0.5f)) - 0.5f))));
        }
        if(_S13)
        {
            level_0 = level_0 + i32(1);
            continue;
        }
        var uvw_0 : vec3<f32> = vec3<f32>(voxel_0.x / f32(i32(p_0.fieldMisc_0.y + 0.5f)), voxel_0.y / f32(i32(p_0.fieldMisc_0.y + 0.5f)), (f32(level_0) * f32(i32(p_0.fieldMisc_0.y + 0.5f)) + voxel_0.z) / f32(i32(p_0.fieldMisc_0.y + 0.5f) * i32(p_0.fieldMisc_0.x + 0.5f)));
        (*known_0) = true;
        (*raw_0) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_0), (0.0f))).x;
        (*resolved_0) = _S12;
        return ((*raw_0) * 2.0f - 1.0f) * p_0.fieldRange_0[level_0];
    }
    return 1.0e+06f;
}

fn DistanceAt_1( world_6 : vec3<f32>,  known_1 : ptr<function, bool>,  raw_1 : ptr<function, f32>,  resolved_1 : ptr<function, f32>) -> f32
{
    (*known_1) = false;
    (*raw_1) = 0.0f;
    (*resolved_1) = p_0.fieldOrigin_0[i32(0)].w;
    var level_1 : i32 = i32(0);
    for(;;)
    {
        if(level_1 < i32(p_0.fieldMisc_0.x + 0.5f))
        {
        }
        else
        {
            break;
        }
        var _S14 : f32 = p_0.fieldOrigin_0[level_1].w;
        var voxel_1 : vec3<f32> = (world_6 - p_0.fieldOrigin_0[level_1].xyz) / vec3<f32>(_S14);
        var _S15 : bool;
        if((any((voxel_1 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S15 = true;
        }
        else
        {
            _S15 = (any((voxel_1 > vec3<f32>(f32(i32(p_0.fieldMisc_0.y + 0.5f)) - 0.5f, f32(i32(p_0.fieldMisc_0.y + 0.5f)) - 0.5f, f32(i32(p_0.fieldMisc_0.y + 0.5f)) - 0.5f))));
        }
        if(_S15)
        {
            level_1 = level_1 + i32(1);
            continue;
        }
        var uvw_1 : vec3<f32> = vec3<f32>(voxel_1.x / f32(i32(p_0.fieldMisc_0.y + 0.5f)), voxel_1.y / f32(i32(p_0.fieldMisc_0.y + 0.5f)), (f32(level_1) * f32(i32(p_0.fieldMisc_0.y + 0.5f)) + voxel_1.z) / f32(i32(p_0.fieldMisc_0.y + 0.5f) * i32(p_0.fieldMisc_0.x + 0.5f)));
        (*known_1) = true;
        (*raw_1) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_1), (0.0f))).x;
        (*resolved_1) = _S14;
        return ((*raw_1) * 2.0f - 1.0f) * p_0.fieldRange_0[level_1];
    }
    return 1.0e+06f;
}

fn FieldStepHits_0( field_0 : f32,  travelled_0 : f32,  resolved_2 : f32,  hitDistance_0 : ptr<function, f32>) -> bool
{
    var radius_0 : f32 = 0.5f * resolved_2 * saturate(travelled_0 / max(2.0f * resolved_2, 9.99999997475242708e-07f));
    (*hitDistance_0) = max(travelled_0 + field_0 - radius_0, 0.0f);
    return field_0 < radius_0;
}

fn FieldStepHits_1( field_1 : f32,  travelled_1 : f32,  resolved_3 : f32,  hitDistance_1 : ptr<function, f32>) -> bool
{
    var radius_1 : f32 = 0.5f * resolved_3 * saturate(travelled_1 / max(2.0f * resolved_3, 9.99999997475242708e-07f));
    (*hitDistance_1) = max(travelled_1 + field_1 - radius_1, 0.0f);
    return field_1 < radius_1;
}

fn VisibilityAt_0( world_7 : vec3<f32>,  normal_0 : vec3<f32>,  direction_0 : vec3<f32>,  hit_0 : ptr<function, vec3<f32>>) -> f32
{
    (*hit_0) = world_7;
    if(i32(p_0.fieldMisc_0.x + 0.5f) <= i32(0))
    {
        return 1.0f;
    }
    var _S16 : vec3<f32> = world_7 + normal_0 * vec3<f32>(p_0.fieldOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var _S17 : f32 = p_0.fieldOrigin_0[i32(0)].w * f32(i32(p_0.fieldMisc_0.y + 0.5f)) * f32((i32(1) << (u32(i32(p_0.fieldMisc_0.x + 0.5f) - i32(1)))));
    var step_0 : i32 = i32(0);
    var travelled_2 : f32 = 0.0f;
    for(;;)
    {
        var _S18 : bool;
        if(step_0 < i32(64))
        {
            _S18 = travelled_2 < _S17;
        }
        else
        {
            _S18 = false;
        }
        if(_S18)
        {
        }
        else
        {
            break;
        }
        var known_2 : bool;
        var raw_2 : f32;
        var resolved_4 : f32;
        var distance_0 : f32 = DistanceAt_1(_S16 + direction_0 * vec3<f32>(travelled_2), &(known_2), &(raw_2), &(resolved_4));
        if(!known_2)
        {
            return 1.0f;
        }
        var hitDistance_2 : f32;
        var _S19 : bool = FieldStepHits_1(distance_0, travelled_2, resolved_4, &(hitDistance_2));
        if(_S19)
        {
            (*hit_0) = _S16 + direction_0 * vec3<f32>(hitDistance_2);
            return 0.0f;
        }
        var travelled_3 : f32 = travelled_2 + max(distance_0, resolved_4 * 0.25f);
        step_0 = step_0 + i32(1);
        travelled_2 = travelled_3;
    }
    return 1.0f;
}

fn Visibility_0( world_8 : vec3<f32>,  normal_1 : vec3<f32>,  direction_1 : vec3<f32>) -> f32
{
    var ignored_0 : vec3<f32>;
    return VisibilityAt_0(world_8, normal_1, direction_1, &(ignored_0));
}

fn FieldSunVisibility_0( world_9 : vec3<f32>) -> f32
{
    if(i32(p_0.fieldMisc_0.x + 0.5f) <= i32(0))
    {
        return 1.0f;
    }
    return Visibility_0(world_9, vec3<f32>(0.0f, 0.0f, 0.0f), normalize((((vec4<f32>(p_0.sun_0.xyz, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz));
}

fn SunVisibility_0( viewPos_0 : vec3<f32>,  world_10 : vec3<f32>) -> f32
{
    var count_0 : i32 = i32(p_0.albedo_0.w + 0.5f);
    if(count_0 <= i32(0))
    {
        return 1.0f;
    }
    var last_0 : f32 = p_0.cascadeSplits_0[clamp(count_0 - i32(1), i32(0), i32(3))];
    var beyond_0 : f32;
    if((p_0.cascadeSplits_0[clamp(count_0 - i32(1), i32(0), i32(3))]) > 0.0f)
    {
        beyond_0 = clamp((viewPos_0.z - last_0 * 0.55000001192092896f) / max(last_0 * 0.44999998807907104f, 0.00100000004749745f), 0.0f, 1.0f);
    }
    else
    {
        beyond_0 = 1.0f;
    }
    var far_0 : f32;
    if(beyond_0 > 0.0f)
    {
        far_0 = FieldSunVisibility_0(world_10);
    }
    else
    {
        far_0 = 1.0f;
    }
    if(beyond_0 >= 1.0f)
    {
        return far_0;
    }
    var i_2 : i32 = i32(0);
    for(;;)
    {
        if(i_2 < i32(4))
        {
        }
        else
        {
            break;
        }
        var _S20 : bool;
        if(i_2 >= count_0)
        {
            _S20 = true;
        }
        else
        {
            _S20 = (viewPos_0.z) > (p_0.cascadeSplits_0[i_2]);
        }
        if(_S20)
        {
            i_2 = i_2 + i32(1);
            continue;
        }
        var clip_0 : vec4<f32> = (((vec4<f32>(world_10, 1.0f)) * (mat4x4<f32>(p_0.cascade_0.data_1[i_2].data_0[i32(0)][i32(0)], p_0.cascade_0.data_1[i_2].data_0[i32(1)][i32(0)], p_0.cascade_0.data_1[i_2].data_0[i32(2)][i32(0)], p_0.cascade_0.data_1[i_2].data_0[i32(3)][i32(0)], p_0.cascade_0.data_1[i_2].data_0[i32(0)][i32(1)], p_0.cascade_0.data_1[i_2].data_0[i32(1)][i32(1)], p_0.cascade_0.data_1[i_2].data_0[i32(2)][i32(1)], p_0.cascade_0.data_1[i_2].data_0[i32(3)][i32(1)], p_0.cascade_0.data_1[i_2].data_0[i32(0)][i32(2)], p_0.cascade_0.data_1[i_2].data_0[i32(1)][i32(2)], p_0.cascade_0.data_1[i_2].data_0[i32(2)][i32(2)], p_0.cascade_0.data_1[i_2].data_0[i32(3)][i32(2)], p_0.cascade_0.data_1[i_2].data_0[i32(0)][i32(3)], p_0.cascade_0.data_1[i_2].data_0[i32(1)][i32(3)], p_0.cascade_0.data_1[i_2].data_0[i32(2)][i32(3)], p_0.cascade_0.data_1[i_2].data_0[i32(3)][i32(3)]))));
        var ndc_1 : vec3<f32> = clip_0.xyz / vec3<f32>(clip_0.w);
        if((any(((abs(ndc_1.xy)) > vec2<f32>(1.0f, 1.0f)))))
        {
            i_2 = i_2 + i32(1);
            continue;
        }
        var uv_1 : vec2<f32> = vec2<f32>(ndc_1.x * 0.5f + 0.5f, 0.5f - ndc_1.y * 0.5f);
        var _S21 : f32 = ndc_1.z * 0.5f + 0.5f - p_0.sunColour_0.w;
        var mapWidth_0 : u32;
        var mapHeight_0 : u32;
        var mapLayers_0 : u32;
        {var dim = textureDimensions((uShadowMap_0));((mapWidth_0)) = dim.x;((mapHeight_0)) = dim.y;((mapLayers_0)) = textureNumLayers((uShadowMap_0));};
        var mapSize_0 : vec2<f32> = vec2<f32>(f32(mapWidth_0), f32(mapHeight_0));
        var texel_1 : vec2<f32> = uv_1 * mapSize_0 - vec2<f32>(0.5f);
        var _S22 : vec2<i32> = vec2<i32>(floor(texel_1));
        var _S23 : vec2<f32> = fract(texel_1);
        var y_0 : i32 = i32(0);
        var sum_0 : f32 = 0.0f;
        for(;;)
        {
            if(y_0 < i32(2))
            {
            }
            else
            {
                break;
            }
            var x_1 : i32 = i32(0);
            for(;;)
            {
                if(x_1 < i32(2))
                {
                }
                else
                {
                    break;
                }
                var _S24 : vec4<i32> = vec4<i32>(clamp(_S22 + vec2<i32>(x_1, y_0), vec2<i32>(i32(0), i32(0)), vec2<i32>(mapSize_0) - vec2<i32>(i32(1))), i_2, i32(0));
                var lit_0 : f32;
                if(_S21 <= (textureLoad((uShadowMap_0), ((_S24)).xy, i32(((_S24)).z), ((_S24)).w)))
                {
                    lit_0 = 1.0f;
                }
                else
                {
                    lit_0 = 0.0f;
                }
                var _S25 : f32 = _S23.x;
                var _S26 : f32 = _S23.y;
                var sum_1 : f32 = sum_0 + lit_0 * mix(1.0f - _S25, _S25, f32(x_1)) * mix(1.0f - _S26, _S26, f32(y_0));
                x_1 = x_1 + i32(1);
                sum_0 = sum_1;
            }
            y_0 = y_0 + i32(1);
        }
        return mix(sum_0, far_0, beyond_0);
    }
    return far_0;
}

fn PhaseHG_0( cosTheta_0 : f32,  g_0 : f32) -> f32
{
    var gg_0 : f32 = g_0 * g_0;
    return (1.0f - gg_0) / (12.56637096405029297f * pow(max(1.0f + gg_0 - 2.0f * g_0 * cosTheta_0, 0.00009999999747379f), 1.5f));
}

fn Phase_0( cosTheta_1 : f32) -> f32
{
    return mix(mix(PhaseHG_0(cosTheta_1, p_0.medium_0.w), PhaseHG_0(cosTheta_1, p_0.phase_0.x), p_0.phase_0.y), 0.07957746833562851f, p_0.march_0.y);
}

fn FogLampMapVisibility_0( lamp_0 : i32,  cameraRelative_0 : vec3<f32>) -> f32
{
    var found_0 : bool;
    if(lamp_0 < i32(0))
    {
        found_0 = true;
    }
    else
    {
        found_0 = lamp_0 >= i32(160);
    }
    if(found_0)
    {
        return -1.0f;
    }
    var entry_0 : i32 = uFogLampMapParams_0.mapSlot_0[(lamp_0 >> (u32(2)))][(lamp_0 & (i32(3)))];
    if((uFogLampMapParams_0.mapSlot_0[(lamp_0 >> (u32(2)))][(lamp_0 & (i32(3)))]) < i32(0))
    {
        return -1.0f;
    }
    var slot_0 : i32 = (entry_0 & (i32(255)));
    const _S27 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 1.0f);
    const _S28 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var clip_1 : vec4<f32>;
    var ndc_2 : vec3<f32>;
    var y_1 : i32;
    var slot_1 : i32;
    var x_2 : i32;
    if(((entry_0 & (i32(256)))) != i32(0))
    {
        found_0 = false;
        clip_1 = _S27;
        ndc_2 = _S28;
        y_1 = i32(0);
        slot_1 = slot_0;
        for(;;)
        {
            var _S29 : bool;
            if(y_1 < i32(6))
            {
                _S29 = !found_0;
            }
            else
            {
                _S29 = false;
            }
            if(_S29)
            {
            }
            else
            {
                break;
            }
            var _S30 : i32 = slot_1 + y_1;
            var faceClip_0 : vec4<f32> = (((vec4<f32>(cameraRelative_0, 1.0f)) * (mat4x4<f32>(uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(0)][i32(0)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(1)][i32(0)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(2)][i32(0)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(3)][i32(0)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(0)][i32(1)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(1)][i32(1)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(2)][i32(1)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(3)][i32(1)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(0)][i32(2)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(1)][i32(2)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(2)][i32(2)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(3)][i32(2)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(0)][i32(3)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(1)][i32(3)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(2)][i32(3)], uFogLampMapParams_0.mapMatrix_0.data_2[_S30].data_0[i32(3)][i32(3)]))));
            var _S31 : f32 = faceClip_0.w;
            if(_S31 > 0.00009999999747379f)
            {
                var faceNdc_0 : vec3<f32> = faceClip_0.xyz / vec3<f32>(_S31);
                var found_1 : bool;
                var clip_2 : vec4<f32>;
                var ndc_3 : vec3<f32>;
                if((all(((abs(faceNdc_0.xy)) <= vec2<f32>(1.0f, 1.0f)))))
                {
                    found_1 = true;
                    clip_2 = faceClip_0;
                    ndc_3 = faceNdc_0;
                    x_2 = _S30;
                }
                else
                {
                    found_1 = found_0;
                    clip_2 = clip_1;
                    ndc_3 = ndc_2;
                    x_2 = slot_1;
                }
                found_0 = found_1;
                clip_1 = clip_2;
                ndc_2 = ndc_3;
                slot_1 = x_2;
            }
            y_1 = y_1 + i32(1);
        }
        if(!found_0)
        {
            return -1.0f;
        }
    }
    else
    {
        var clip_3 : vec4<f32> = (((vec4<f32>(cameraRelative_0, 1.0f)) * (mat4x4<f32>(uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(0)][i32(0)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(1)][i32(0)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(2)][i32(0)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(3)][i32(0)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(0)][i32(1)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(1)][i32(1)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(2)][i32(1)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(3)][i32(1)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(0)][i32(2)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(1)][i32(2)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(2)][i32(2)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(3)][i32(2)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(0)][i32(3)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(1)][i32(3)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(2)][i32(3)], uFogLampMapParams_0.mapMatrix_0.data_2[slot_0].data_0[i32(3)][i32(3)]))));
        var _S32 : f32 = clip_3.w;
        if(_S32 <= 0.00009999999747379f)
        {
            return -1.0f;
        }
        var ndc_4 : vec3<f32> = clip_3.xyz / vec3<f32>(_S32);
        if((any(((abs(ndc_4.xy)) > vec2<f32>(1.0f, 1.0f)))))
        {
            return -1.0f;
        }
        clip_1 = clip_3;
        ndc_2 = ndc_4;
        slot_1 = slot_0;
    }
    var pair_0 : vec4<f32> = uFogLampMapParams_0.mapDepth_0[(slot_1 >> (u32(1)))];
    var terms_0 : vec2<f32>;
    if(((slot_1 & (i32(1)))) != i32(0))
    {
        terms_0 = pair_0.zw;
    }
    else
    {
        terms_0 = pair_0.xy;
    }
    var _S33 : f32 = terms_0.y;
    var _S34 : f32 = terms_0.x;
    var nearPlane_0 : f32 = _S33 / (-1.0f - _S34);
    var viewZ_2 : f32 = clip_1.w;
    if(viewZ_2 <= nearPlane_0)
    {
        return 1.0f;
    }
    var _S35 : f32 = (_S34 + _S33 / max(max(viewZ_2 - (0.05000000074505806f + 0.00999999977648258f * viewZ_2), nearPlane_0 + 0.00499999988824129f), 0.00009999999747379f)) * 0.5f + 0.5f;
    var width_0 : u32;
    var height_0 : u32;
    var layers_0 : u32;
    {var dim = textureDimensions((uFogLampMaps_0));((width_0)) = dim.x;((height_0)) = dim.y;((layers_0)) = textureNumLayers((uFogLampMaps_0));};
    var _S36 : vec2<i32> = vec2<i32>(floor(vec2<f32>(ndc_2.x * 0.5f + 0.5f, 0.5f - ndc_2.y * 0.5f) * vec2<f32>(f32(width_0), f32(height_0)) - vec2<f32>(0.5f)));
    var _S37 : vec2<i32> = vec2<i32>(i32(width_0) - i32(1), i32(height_0) - i32(1));
    y_1 = i32(0);
    var lit_1 : f32 = 0.0f;
    for(;;)
    {
        if(y_1 < i32(2))
        {
        }
        else
        {
            break;
        }
        x_2 = i32(0);
        for(;;)
        {
            if(x_2 < i32(2))
            {
            }
            else
            {
                break;
            }
            var _S38 : vec4<i32> = vec4<i32>(clamp(_S36 + vec2<i32>(x_2, y_1), vec2<i32>(i32(0), i32(0)), _S37), slot_1, i32(0));
            var _S39 : f32;
            if((textureLoad((uFogLampMaps_0), ((_S38)).xy, i32(((_S38)).z), ((_S38)).w)) >= _S35)
            {
                _S39 = 0.25f;
            }
            else
            {
                _S39 = 0.0f;
            }
            var lit_2 : f32 = lit_1 + _S39;
            x_2 = x_2 + i32(1);
            lit_1 = lit_2;
        }
        y_1 = y_1 + i32(1);
    }
    return lit_1;
}

fn SegmentVisibility_0( world_11 : vec3<f32>,  normal_2 : vec3<f32>,  target_0 : vec3<f32>,  stopShort_0 : f32) -> f32
{
    if(i32(p_0.fieldMisc_0.x + 0.5f) <= i32(0))
    {
        return 1.0f;
    }
    var finest_0 : f32 = p_0.fieldOrigin_0[i32(0)].w;
    var at_0 : vec3<f32> = world_11 + normal_2 * vec3<f32>(finest_0) * vec3<f32>(1.5f);
    var toTarget_0 : vec3<f32> = target_0 - at_0;
    var distance_1 : f32 = length(toTarget_0);
    var limit_0 : f32 = distance_1 - max(stopShort_0, finest_0);
    if(limit_0 <= 0.0f)
    {
        return 1.0f;
    }
    var _S40 : vec3<f32> = toTarget_0 / vec3<f32>(distance_1);
    var step_1 : i32 = i32(0);
    var travelled_4 : f32 = 0.0f;
    for(;;)
    {
        var _S41 : bool;
        if(step_1 < i32(48))
        {
            _S41 = travelled_4 < limit_0;
        }
        else
        {
            _S41 = false;
        }
        if(_S41)
        {
        }
        else
        {
            break;
        }
        var known_3 : bool;
        var raw_3 : f32;
        var resolved_5 : f32;
        var field_2 : f32 = DistanceAt_0(at_0 + _S40 * vec3<f32>(travelled_4), &(known_3), &(raw_3), &(resolved_5));
        if(!known_3)
        {
            return 1.0f;
        }
        var touchedAt_0 : f32;
        if(FieldStepHits_0(field_2, travelled_4, resolved_5, &(touchedAt_0)))
        {
            return 0.0f;
        }
        var travelled_5 : f32 = travelled_4 + max(field_2, resolved_5 * 0.25f);
        step_1 = step_1 + i32(1);
        travelled_4 = travelled_5;
    }
    return 1.0f;
}

fn SkyIrradianceAt_0( direction_2 : vec3<f32>) -> vec3<f32>
{
    var n_0 : vec4<f32> = vec4<f32>(direction_2, 1.0f);
    const _S42 : vec3<i32> = vec3<i32>(i32(0), i32(0), i32(0));
    const _S43 : vec3<i32> = vec3<i32>(i32(1), i32(0), i32(0));
    const _S44 : vec3<i32> = vec3<i32>(i32(2), i32(0), i32(0));
    var q_1 : vec4<f32> = n_0.xyzz * n_0.yzzx;
    const _S45 : vec3<i32> = vec3<i32>(i32(3), i32(0), i32(0));
    const _S46 : vec3<i32> = vec3<i32>(i32(4), i32(0), i32(0));
    const _S47 : vec3<i32> = vec3<i32>(i32(5), i32(0), i32(0));
    const _S48 : vec3<i32> = vec3<i32>(i32(6), i32(0), i32(0));
    var _S49 : f32 = n_0.x;
    var _S50 : f32 = n_0.y;
    return max(vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(dot((textureLoad((uSkyIrradiance_0), ((_S42)).xy, ((_S42)).z)), n_0), dot((textureLoad((uSkyIrradiance_0), ((_S43)).xy, ((_S43)).z)), n_0), dot((textureLoad((uSkyIrradiance_0), ((_S44)).xy, ((_S44)).z)), n_0)) + vec3<f32>(dot((textureLoad((uSkyIrradiance_0), ((_S45)).xy, ((_S45)).z)), q_1), dot((textureLoad((uSkyIrradiance_0), ((_S46)).xy, ((_S46)).z)), q_1), dot((textureLoad((uSkyIrradiance_0), ((_S47)).xy, ((_S47)).z)), q_1)) + (textureLoad((uSkyIrradiance_0), ((_S48)).xy, ((_S48)).z)).xyz * vec3<f32>((_S49 * _S49 - _S50 * _S50)));
}

fn ViewDepthSlice_0( viewZ_3 : f32) -> f32
{
    return p_0.zParams_0.z * log2(max(p_0.zParams_0.x * viewZ_3 + p_0.zParams_0.y, 9.99999997475242708e-07f));
}

fn HistoryCoordinate_0( world_12 : vec3<f32>,  uvw_2 : ptr<function, vec3<f32>>) -> bool
{
    const _S51 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    (*uvw_2) = _S51;
    var prevView_0 : vec3<f32> = (((vec4<f32>(world_12 + p_0.camera_0.xyz, 0.0f)) * (mat4x4<f32>(p_0.prevWorldToView_0.data_0[i32(0)][i32(0)], p_0.prevWorldToView_0.data_0[i32(1)][i32(0)], p_0.prevWorldToView_0.data_0[i32(2)][i32(0)], p_0.prevWorldToView_0.data_0[i32(3)][i32(0)], p_0.prevWorldToView_0.data_0[i32(0)][i32(1)], p_0.prevWorldToView_0.data_0[i32(1)][i32(1)], p_0.prevWorldToView_0.data_0[i32(2)][i32(1)], p_0.prevWorldToView_0.data_0[i32(3)][i32(1)], p_0.prevWorldToView_0.data_0[i32(0)][i32(2)], p_0.prevWorldToView_0.data_0[i32(1)][i32(2)], p_0.prevWorldToView_0.data_0[i32(2)][i32(2)], p_0.prevWorldToView_0.data_0[i32(3)][i32(2)], p_0.prevWorldToView_0.data_0[i32(0)][i32(3)], p_0.prevWorldToView_0.data_0[i32(1)][i32(3)], p_0.prevWorldToView_0.data_0[i32(2)][i32(3)], p_0.prevWorldToView_0.data_0[i32(3)][i32(3)])))).xyz;
    var _S52 : f32 = prevView_0.z;
    if(_S52 <= (p_0.prevUnproject_0.z))
    {
        return false;
    }
    var _S53 : vec2<f32> = vec2<f32>(0.5f);
    var _S54 : vec3<f32> = vec3<f32>(vec2<f32>(prevView_0.x * p_0.prevUnproject_0.x / _S52, - prevView_0.y * p_0.prevUnproject_0.y / _S52) * _S53 + _S53, ViewDepthSlice_0(_S52) * p_0.zParams_0.w);
    (*uvw_2) = _S54;
    var _S55 : bool;
    if((all((_S54 >= _S51))))
    {
        _S55 = (all(((*uvw_2) <= vec3<f32>(1.0f, 1.0f, 1.0f))));
    }
    else
    {
        _S55 = false;
    }
    return _S55;
}

@compute
@workgroup_size(4, 4, 4)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var place_0 : i32;
    var _S56 : vec3<i32> = vec3<i32>(id_0);
    if((any((_S56 >= vec3<i32>(p_0.grid_0.xyz + vec3<f32>(0.5f))))))
    {
        return;
    }
    var offset_0 : vec3<f32> = fract(p_0.jitter_0.xyz + vec3<f32>(Hash31_0(vec3<f32>(_S56) + vec3<f32>(0.37000000476837158f)), Hash31_0(vec3<f32>(_S56.yzx) + vec3<f32>(11.10000038146972656f)), Hash31_0(vec3<f32>(_S56.zxy) + vec3<f32>(23.70000076293945312f))));
    var _S57 : vec2<f32> = vec2<f32>(_S56.xy);
    var _S58 : f32 = f32(_S56.z);
    var viewZ_4 : f32 = SliceViewDepth_0(_S58 + offset_0.z);
    var viewPos_1 : vec3<f32> = ViewPositionAt_0((_S57 + offset_0.xy) / p_0.grid_0.xy, viewZ_4);
    var world_13 : vec3<f32> = (((vec4<f32>(viewPos_1, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz;
    var indoors_2 : bool = Indoors_0(world_13);
    var density_0 : f32 = DensityAt_0(world_13, indoors_2);
    var rayView_0 : vec3<f32> = normalize(viewPos_1);
    const _S59 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var _S60 : f32 = density_0 + DustAt_0(world_13, viewZ_4, indoors_2);
    var scattering_0 : vec3<f32>;
    if(_S60 > 0.0f)
    {
        var _S61 : vec3<f32> = p_0.sunColour_0.xyz * vec3<f32>(p_0.march_0.z) * vec3<f32>(Phase_0(dot(rayView_0, p_0.sun_0.xyz))) * vec3<f32>(SunVisibility_0(viewPos_1, world_13));
        var rankedLamp_0 : array<i32, i32(4)>;
        rankedLamp_0[i32(0)] = i32(-1);
        rankedLamp_0[i32(1)] = i32(-1);
        rankedLamp_0[i32(2)] = i32(-1);
        rankedLamp_0[i32(3)] = i32(-1);
        var rankedLight_0 : array<vec3<f32>, i32(4)>;
        rankedLight_0[i32(0)] = _S59;
        rankedLight_0[i32(1)] = _S59;
        rankedLight_0[i32(2)] = _S59;
        rankedLight_0[i32(3)] = _S59;
        var rankedLuminance_0 : array<f32, i32(4)>;
        rankedLuminance_0[i32(0)] = 0.0f;
        rankedLuminance_0[i32(1)] = 0.0f;
        rankedLuminance_0[i32(2)] = 0.0f;
        rankedLuminance_0[i32(3)] = 0.0f;
        var _S62 : i32 = min(i32(round(p_0.camera_0.w)), i32(160));
        var i_3 : i32 = i32(0);
        scattering_0 = _S61;
        for(;;)
        {
            if(i_3 < _S62)
            {
            }
            else
            {
                break;
            }
            var _S63 : vec3<f32> = p_0.lampPosition_0[i_3].xyz;
            var toLight_0 : vec3<f32> = _S63 - viewPos_1;
            var distanceSquared_0 : f32 = dot(toLight_0, toLight_0);
            var reach_1 : f32 = p_0.lampPosition_0[i_3].w;
            var _S64 : bool;
            if(reach_1 <= 0.0f)
            {
                _S64 = true;
            }
            else
            {
                _S64 = distanceSquared_0 >= (reach_1 * reach_1);
            }
            if(_S64)
            {
                i_3 = i_3 + i32(1);
                continue;
            }
            var _S65 : bool;
            if(Indoors_0((((vec4<f32>(_S63, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz - vec3<f32>(0.0f, 0.0f, 1.0f)))
            {
                _S65 = !indoors_2;
            }
            else
            {
                _S65 = false;
            }
            if(_S65)
            {
                i_3 = i_3 + i32(1);
                continue;
            }
            var ratio_0 : f32 = distanceSquared_0 / (reach_1 * reach_1);
            var window_0 : f32 = saturate(1.0f - ratio_0 * ratio_0);
            var attenuation_0 : f32 = window_0 * window_0 / (distanceSquared_0 + 1.0f);
            var direction_3 : vec3<f32> = toLight_0 / vec3<f32>(sqrt(max(distanceSquared_0, 9.99999993922529029e-09f)));
            var facing_0 : vec3<f32> = p_0.lampDirection_0[i_3].xyz;
            var attenuation_1 : f32;
            if((dot(facing_0, facing_0)) > 0.25f)
            {
                attenuation_1 = attenuation_0 * max(dot(normalize(facing_0), (vec3<f32>(0) - direction_3)), 0.0f);
            }
            else
            {
                attenuation_1 = attenuation_0;
            }
            var lampLight_0 : vec3<f32> = p_0.lampColour_0[i_3].xyz * vec3<f32>(p_0.lampColour_0[i_3].w) * vec3<f32>(attenuation_1) * vec3<f32>(Phase_0(dot(rayView_0, direction_3)));
            var mapped_0 : f32 = FogLampMapVisibility_0(i_3, world_13);
            if(mapped_0 >= 0.0f)
            {
                scattering_0 = scattering_0 + lampLight_0 * vec3<f32>(mapped_0);
                i_3 = i_3 + i32(1);
                continue;
            }
            var direct_0 : vec3<f32> = scattering_0 + lampLight_0;
            var luminance_0 : f32 = dot(lampLight_0, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f));
            place_0 = i32(2);
            var r_0 : i32 = i32(1);
            for(;;)
            {
                if(r_0 >= i32(0))
                {
                }
                else
                {
                    break;
                }
                if(luminance_0 > (rankedLuminance_0[r_0]))
                {
                    place_0 = r_0;
                }
                r_0 = r_0 - i32(1);
            }
            if(place_0 < i32(2))
            {
                var r_1 : i32 = i32(1);
                for(;;)
                {
                    if(r_1 > place_0)
                    {
                    }
                    else
                    {
                        break;
                    }
                    var _S66 : i32 = r_1 - i32(1);
                    rankedLamp_0[r_1] = rankedLamp_0[_S66];
                    rankedLight_0[r_1] = rankedLight_0[_S66];
                    rankedLuminance_0[r_1] = rankedLuminance_0[_S66];
                    r_1 = _S66;
                }
                rankedLamp_0[place_0] = i_3;
                rankedLight_0[place_0] = lampLight_0;
                rankedLuminance_0[place_0] = luminance_0;
            }
            scattering_0 = direct_0;
            i_3 = i_3 + i32(1);
        }
        place_0 = i32(0);
        for(;;)
        {
            if(place_0 < i32(2))
            {
            }
            else
            {
                break;
            }
            var ranked_0 : i32 = rankedLamp_0[place_0];
            if((rankedLamp_0[place_0]) >= i32(0))
            {
                scattering_0 = scattering_0 - rankedLight_0[place_0] * vec3<f32>((1.0f - SegmentVisibility_0(world_13, _S59, (((vec4<f32>(p_0.lampPosition_0[ranked_0].xyz, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz, clamp(p_0.lampDirection_0[ranked_0].w * 0.5f, 0.05000000074505806f, 0.20000000298023224f))));
            }
            place_0 = place_0 + i32(1);
        }
        var rayWorld_0 : vec3<f32> = (((vec4<f32>(rayView_0, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz;
        var sky_0 : vec3<f32>;
        if((p_0.ambient_0.w) > 0.5f)
        {
            sky_0 = SkyIrradianceAt_0((vec3<f32>(0) - rayWorld_0) * vec3<f32>(p_0.medium_0.w));
        }
        else
        {
            sky_0 = p_0.ambient_0.xyz;
        }
        scattering_0 = (max(scattering_0, _S59) * vec3<f32>(_S60) + sky_0 * vec3<f32>(p_0.march_0.w) * vec3<f32>(density_0)) * p_0.albedo_0.xyz;
    }
    else
    {
        scattering_0 = _S59;
    }
    var result_0 : vec4<f32> = vec4<f32>(scattering_0, _S60);
    var result_1 : vec4<f32>;
    if((p_0.jitter_0.w) > 0.5f)
    {
        var historyUvw_0 : vec3<f32>;
        var _S67 : bool = HistoryCoordinate_0((((vec4<f32>(ViewPositionAt_0((_S57 + vec2<f32>(0.5f)) / p_0.grid_0.xy, SliceViewDepth_0(_S58 + 0.5f)), 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz, &(historyUvw_0));
        if(_S67)
        {
            result_1 = mix(result_0, (textureSampleLevel((uHistory_0), (uHistorySampler_0), (historyUvw_0), (0.0f))), vec4<f32>(p_0.grid_0.w));
        }
        else
        {
            result_1 = result_0;
        }
    }
    else
    {
        result_1 = result_0;
    }
    textureStore((uScattering_0), (vec3<u32>(_S56)), (max(result_1, vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f))));
    return;
}

