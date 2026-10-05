struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct _Array_std140_float4_0
{
    @align(16) data_1 : array<vec4<f32>, i32(4)>,
};

struct Params_std140_0
{
    @align(16) viewToWorld_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) resolution_0 : vec4<f32>,
    @align(16) trace_0 : vec4<f32>,
    @align(16) cacheOrigin_0 : vec4<f32>,
    @align(16) cacheGrid_0 : vec4<f32>,
    @align(16) cacheAtlas_0 : vec4<f32>,
    @align(16) debug_0 : vec4<f32>,
    @align(16) cameraWorld_0 : vec4<f32>,
    @align(16) records_0 : vec4<u32>,
    @align(16) sun_0 : vec4<f32>,
    @align(16) sunColour_0 : vec4<f32>,
    @align(16) ambient_0 : vec4<f32>,
    @align(16) field_0 : vec4<f32>,
    @align(16) sdfOrigin_0 : array<vec4<f32>, i32(4)>,
    @align(16) sdfRange_0 : _Array_std140_float4_0,
    @align(16) shelter0_0 : vec4<f32>,
    @align(16) shelter1_0 : vec4<f32>,
    @align(16) shelter2_0 : vec4<f32>,
};

@binding(22) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(4) @group(0) var uGlass_0 : texture_2d<f32>;

@binding(8) @group(0) var uOut_0 : texture_storage_2d<rgba16float, write>;

@binding(6) @group(0) var uSkyView_0 : texture_2d<f32>;

@binding(7) @group(0) var uSkyViewSampler_0 : sampler;

@binding(20) @group(0) var uShelterMap_0 : texture_2d<f32>;

@binding(18) @group(0) var uClipmaps_0 : texture_3d<f32>;

@binding(19) @group(0) var uClipmapsSampler_0 : sampler;

@binding(2) @group(0) var uSceneDepth_0 : texture_depth_2d;

@binding(0) @group(0) var uLit_0 : texture_2d<f32>;

@binding(1) @group(0) var uLitSampler_0 : sampler;

@binding(10) @group(0) var<storage, read> uCells_0 : array<vec2<i32>>;

@binding(12) @group(0) var uAlbedo_0 : texture_2d<f32>;

@binding(11) @group(0) var<storage, read> uCellPages_0 : array<i32>;

@binding(9) @group(0) var<storage, read> uPages_0 : array<vec4<f32>>;

@binding(13) @group(0) var uAlbedoSampler_0 : sampler;

@binding(14) @group(0) var uDepth_0 : texture_2d<f32>;

@binding(15) @group(0) var uDepthSampler_0 : sampler;

@binding(16) @group(0) var uFinal_0 : texture_2d<f32>;

@binding(17) @group(0) var uFinalSampler_0 : sampler;

fn rsqrt_0( x_0 : f32) -> f32
{
    return 1.0f / sqrt(x_0);
}

var<private> gRoute_0 : i32;

fn SkyViewUv_0( worldDir_0 : vec3<f32>,  viewHeight_0 : f32) -> vec2<f32>
{
    var beta_0 : f32 = acos(clamp(sqrt(max(0.0f, viewHeight_0 * viewHeight_0 - 4.04496e+07f)) / viewHeight_0, -1.0f, 1.0f));
    var zenithHorizonAngle_0 : f32 = 3.14159274101257324f - beta_0;
    var viewZenithAngle_0 : f32 = acos(clamp(worldDir_0.z, -1.0f, 1.0f));
    var v_0 : f32;
    if(viewZenithAngle_0 < zenithHorizonAngle_0)
    {
        v_0 = (1.0f - sqrt(max(0.0f, 1.0f - viewZenithAngle_0 / zenithHorizonAngle_0))) * 0.5f;
    }
    else
    {
        v_0 = sqrt(max(0.0f, (viewZenithAngle_0 - zenithHorizonAngle_0) / max(beta_0, 9.99999997475242708e-07f))) * 0.5f + 0.5f;
    }
    var azimuth_0 : f32 = atan2(worldDir_0.y, worldDir_0.x);
    var azimuth_1 : f32;
    if(azimuth_0 < 0.0f)
    {
        azimuth_1 = azimuth_0 + 6.28318548202514648f;
    }
    else
    {
        azimuth_1 = azimuth_0;
    }
    return vec2<f32>(azimuth_1 / 6.28318548202514648f, v_0);
}

fn SkyViewHeldAboveHorizon_0( uv_0 : vec2<f32>,  rows_0 : f32) -> vec2<f32>
{
    var _S1 : vec2<f32> = uv_0;
    _S1[i32(1)] = min(uv_0.y, (floor(rows_0 * 0.5f) - 1.0f) / max(rows_0 - 1.0f, 1.0f));
    return _S1;
}

fn SkyViewInset_0( uv_1 : vec2<f32>,  size_0 : vec2<f32>) -> vec2<f32>
{
    return uv_1 * ((size_0 - vec2<f32>(1.0f)) / size_0) + vec2<f32>(0.5f) / size_0;
}

fn SampleSkyViewUv_0( skyView_0 : texture_2d<f32>,  skyViewSampler_0 : sampler,  uv_2 : vec2<f32>) -> vec3<f32>
{
    var width_0 : u32;
    var height_0 : u32;
    {var dim = textureDimensions((skyView_0));((width_0)) = dim.x;((height_0)) = dim.y;};
    return (textureSampleLevel((skyView_0), (skyViewSampler_0), (SkyViewInset_0(uv_2, vec2<f32>(f32(width_0), f32(height_0)))), (0.0f))).xyz;
}

fn SampleSkyBackground_0( skyView_1 : texture_2d<f32>,  skyViewSampler_1 : sampler,  worldDir_1 : vec3<f32>,  viewHeight_1 : f32) -> vec3<f32>
{
    var width_1 : u32;
    var height_1 : u32;
    {var dim = textureDimensions((skyView_1));((width_1)) = dim.x;((height_1)) = dim.y;};
    return SampleSkyViewUv_0(skyView_1, skyViewSampler_1, SkyViewHeldAboveHorizon_0(SkyViewUv_0(worldDir_1, viewHeight_1), f32(height_1)));
}

fn FallbackSky_0( rayW_0 : vec3<f32>) -> vec3<f32>
{
    return SampleSkyBackground_0(uSkyView_0, uSkyViewSampler_0, rayW_0, p_0.trace_0.w) * vec3<f32>(smoothstep(-0.10000000149011612f, 0.25f, rayW_0.z));
}

struct ShelterMap_0
{
     origin_0 : vec2<f32>,
     invCell_0 : f32,
     lowestZ_0 : f32,
     scale_0 : f32,
     headroom_0 : f32,
     reach_0 : f32,
     size_1 : vec2<f32>,
     present_0 : f32,
};

fn GlazingRoofs_0() -> ShelterMap_0
{
    var m_0 : ShelterMap_0;
    m_0.origin_0 = p_0.shelter0_0.xy;
    m_0.invCell_0 = p_0.shelter0_0.z;
    m_0.lowestZ_0 = p_0.shelter0_0.w;
    m_0.scale_0 = p_0.shelter1_0.x;
    m_0.headroom_0 = p_0.shelter1_0.y;
    m_0.reach_0 = p_0.shelter1_0.z;
    m_0.size_1 = p_0.shelter2_0.xy;
    m_0.present_0 = p_0.shelter1_0.w;
    return m_0;
}

fn ShelterTexel_0( m_1 : ShelterMap_0,  worldXY_0 : vec2<f32>) -> vec2<i32>
{
    return vec2<i32>(floor((worldXY_0 - m_1.origin_0) * vec2<f32>(m_1.invCell_0)));
}

fn ShelterHolds_0( m_2 : ShelterMap_0,  texel_0 : vec2<i32>) -> bool
{
    var _S2 : bool;
    if((m_2.present_0) > 0.5f)
    {
        _S2 = (texel_0.x) >= i32(0);
    }
    else
    {
        _S2 = false;
    }
    if(_S2)
    {
        _S2 = (texel_0.y) >= i32(0);
    }
    else
    {
        _S2 = false;
    }
    if(_S2)
    {
        _S2 = (texel_0.x) < i32(m_2.size_1.x);
    }
    else
    {
        _S2 = false;
    }
    if(_S2)
    {
        _S2 = (texel_0.y) < i32(m_2.size_1.y);
    }
    else
    {
        _S2 = false;
    }
    return _S2;
}

fn ShelterSurface_0( m_3 : ShelterMap_0,  texel_1 : vec4<f32>) -> f32
{
    var packed_0 : f32 = floor(texel_1.z * 255.0f + 0.5f) + floor(texel_1.y * 255.0f + 0.5f) * 256.0f;
    var _S3 : f32;
    if(packed_0 < 0.5f)
    {
        _S3 = -1.00000001504746622e+30f;
    }
    else
    {
        _S3 = m_3.lowestZ_0 + (packed_0 - 1.0f) * m_3.scale_0;
    }
    return _S3;
}

fn LosesCeiling_0( m_4 : ShelterMap_0,  map_0 : texture_2d<f32>,  worldXY_1 : vec2<f32>,  worldZ_0 : f32,  rayW_1 : vec3<f32>) -> bool
{
    var at_0 : vec2<i32> = ShelterTexel_0(m_4, worldXY_1);
    if(!ShelterHolds_0(m_4, at_0))
    {
        return false;
    }
    var _S4 : vec3<i32> = vec3<i32>(at_0, i32(0));
    var roof_0 : f32 = ShelterSurface_0(m_4, (textureLoad((map_0), ((_S4)).xy, ((_S4)).z)));
    var _S5 : f32 = worldZ_0 + m_4.headroom_0;
    if(roof_0 <= _S5)
    {
        return false;
    }
    var _S6 : f32 = rayW_1.z;
    if(_S6 <= 0.05000000074505806f)
    {
        return false;
    }
    var above_0 : vec2<i32> = ShelterTexel_0(m_4, worldXY_1 + rayW_1.xy * vec2<f32>(((roof_0 - worldZ_0) / _S6)));
    if(!ShelterHolds_0(m_4, above_0))
    {
        return false;
    }
    var _S7 : vec3<i32> = vec3<i32>(above_0, i32(0));
    return (ShelterSurface_0(m_4, (textureLoad((map_0), ((_S7)).xy, ((_S7)).z)))) > _S5;
}

fn DistanceAt_0( world_0 : vec3<f32>,  known_0 : ptr<function, bool>,  raw_0 : ptr<function, f32>,  resolved_0 : ptr<function, f32>) -> f32
{
    (*known_0) = false;
    (*raw_0) = 0.0f;
    (*resolved_0) = p_0.sdfOrigin_0[i32(0)].w;
    var level_0 : i32 = i32(0);
    for(;;)
    {
        if(level_0 < i32(p_0.field_0.x))
        {
        }
        else
        {
            break;
        }
        var _S8 : f32 = p_0.sdfOrigin_0[level_0].w;
        var voxel_0 : vec3<f32> = (world_0 - p_0.sdfOrigin_0[level_0].xyz) / vec3<f32>(_S8);
        var _S9 : bool;
        if((any((voxel_0 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S9 = true;
        }
        else
        {
            _S9 = (any((voxel_0 > vec3<f32>(f32(i32(p_0.field_0.y)) - 0.5f, f32(i32(p_0.field_0.y)) - 0.5f, f32(i32(p_0.field_0.y)) - 0.5f))));
        }
        if(_S9)
        {
            level_0 = level_0 + i32(1);
            continue;
        }
        var uvw_0 : vec3<f32> = vec3<f32>(voxel_0.x / f32(i32(p_0.field_0.y)), voxel_0.y / f32(i32(p_0.field_0.y)), (f32(level_0) * f32(i32(p_0.field_0.y)) + voxel_0.z) / f32(i32(p_0.field_0.y) * i32(p_0.field_0.x)));
        (*known_0) = true;
        (*raw_0) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_0), (0.0f))).x;
        (*resolved_0) = _S8;
        return ((*raw_0) * 2.0f - 1.0f) * p_0.sdfRange_0.data_1[level_0].x;
    }
    return 1.0e+06f;
}

fn DistanceAt_1( world_1 : vec3<f32>,  known_1 : ptr<function, bool>,  raw_1 : ptr<function, f32>,  resolved_1 : ptr<function, f32>) -> f32
{
    (*known_1) = false;
    (*raw_1) = 0.0f;
    (*resolved_1) = p_0.sdfOrigin_0[i32(0)].w;
    var level_1 : i32 = i32(0);
    for(;;)
    {
        if(level_1 < i32(p_0.field_0.x))
        {
        }
        else
        {
            break;
        }
        var _S10 : f32 = p_0.sdfOrigin_0[level_1].w;
        var voxel_1 : vec3<f32> = (world_1 - p_0.sdfOrigin_0[level_1].xyz) / vec3<f32>(_S10);
        var _S11 : bool;
        if((any((voxel_1 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S11 = true;
        }
        else
        {
            _S11 = (any((voxel_1 > vec3<f32>(f32(i32(p_0.field_0.y)) - 0.5f, f32(i32(p_0.field_0.y)) - 0.5f, f32(i32(p_0.field_0.y)) - 0.5f))));
        }
        if(_S11)
        {
            level_1 = level_1 + i32(1);
            continue;
        }
        var uvw_1 : vec3<f32> = vec3<f32>(voxel_1.x / f32(i32(p_0.field_0.y)), voxel_1.y / f32(i32(p_0.field_0.y)), (f32(level_1) * f32(i32(p_0.field_0.y)) + voxel_1.z) / f32(i32(p_0.field_0.y) * i32(p_0.field_0.x)));
        (*known_1) = true;
        (*raw_1) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_1), (0.0f))).x;
        (*resolved_1) = _S10;
        return ((*raw_1) * 2.0f - 1.0f) * p_0.sdfRange_0.data_1[level_1].x;
    }
    return 1.0e+06f;
}

fn Settle_0( at_1 : vec3<f32>,  direction_0 : vec3<f32>,  travelled_0 : f32,  voxel_2 : ptr<function, f32>) -> vec3<f32>
{
    (*voxel_2) = p_0.sdfOrigin_0[i32(0)].w;
    var pass_0 : i32 = i32(0);
    var _S12 : f32 = travelled_0;
    for(;;)
    {
        if(pass_0 < i32(2))
        {
        }
        else
        {
            break;
        }
        var known_2 : bool;
        var raw_2 : f32;
        var resolved_2 : f32;
        var distance_0 : f32 = DistanceAt_1(at_1 + direction_0 * vec3<f32>(_S12), &(known_2), &(raw_2), &(resolved_2));
        if(!known_2)
        {
            break;
        }
        (*voxel_2) = resolved_2;
        var _S13 : f32 = _S12 + max(distance_0, 0.0f);
        pass_0 = pass_0 + i32(1);
        _S12 = _S13;
    }
    return at_1 + direction_0 * vec3<f32>((_S12 + (*voxel_2) * 0.25f));
}

fn SurfaceAt_0( world_2 : vec3<f32>,  normal_0 : vec3<f32>,  direction_1 : vec3<f32>,  reach_1 : f32,  hit_0 : ptr<function, vec3<f32>>,  voxel_3 : ptr<function, f32>) -> bool
{
    (*hit_0) = world_2;
    (*voxel_3) = p_0.sdfOrigin_0[i32(0)].w;
    if(i32(p_0.field_0.x) <= i32(0))
    {
        return false;
    }
    var _S14 : vec3<f32> = world_2 + normal_0 * vec3<f32>(p_0.sdfOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var started_0 : bool = false;
    var previous_0 : f32 = 0.0f;
    var previousDistance_0 : f32 = 0.0f;
    var step_0 : i32 = i32(0);
    var travelled_1 : f32 = 0.0f;
    for(;;)
    {
        var _S15 : bool;
        if(step_0 < i32(96))
        {
            _S15 = travelled_1 < reach_1;
        }
        else
        {
            _S15 = false;
        }
        if(_S15)
        {
        }
        else
        {
            break;
        }
        var known_3 : bool;
        var raw_3 : f32;
        var resolved_3 : f32;
        var distance_1 : f32 = DistanceAt_1(_S14 + direction_1 * vec3<f32>(travelled_1), &(known_3), &(raw_3), &(resolved_3));
        if(!known_3)
        {
            return false;
        }
        if(started_0)
        {
            if(distance_1 <= 0.0f)
            {
                var span_0 : f32 = travelled_1 - previous_0;
                var fall_0 : f32 = previousDistance_0 - distance_1;
                var crossing_0 : f32;
                if(fall_0 > 0.00000999999974738f)
                {
                    crossing_0 = previous_0 + span_0 * (previousDistance_0 / fall_0);
                }
                else
                {
                    crossing_0 = travelled_1;
                }
                var _S16 : vec3<f32> = Settle_0(_S14, direction_1, clamp(crossing_0, previous_0, travelled_1), &((*voxel_3)));
                (*hit_0) = _S16;
                return true;
            }
            var _S17 : bool;
            if(distance_1 > previousDistance_0)
            {
                _S17 = previousDistance_0 < (resolved_3 * 0.05000000074505806f);
            }
            else
            {
                _S17 = false;
            }
            if(_S17)
            {
                var _S18 : vec3<f32> = Settle_0(_S14, direction_1, previous_0, &((*voxel_3)));
                (*hit_0) = _S18;
                return true;
            }
        }
        var travelled_2 : f32 = travelled_1 + max(distance_1, resolved_3 * 0.25f);
        var _S19 : i32 = step_0 + i32(1);
        started_0 = true;
        previous_0 = travelled_1;
        travelled_1 = travelled_2;
        previousDistance_0 = distance_1;
        step_0 = _S19;
    }
    return false;
}

fn Unknown_0( rayW_2 : vec3<f32>) -> vec3<f32>
{
    var grey_0 : f32 = dot(FallbackSky_0(rayW_2), vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f)) * 0.30000001192092896f;
    return vec3<f32>(grey_0, grey_0, grey_0) + p_0.ambient_0.xyz;
}

fn ViewToUv_0( v_1 : vec3<f32>) -> vec2<f32>
{
    var _S20 : f32 = v_1.z;
    var _S21 : vec2<f32> = vec2<f32>(0.5f);
    return vec2<f32>(v_1.x * p_0.unproject_0.x / _S20, - v_1.y * p_0.unproject_0.y / _S20) * _S21 + _S21;
}

fn SceneViewZ_0( depth_0 : f32) -> f32
{
    return 2.0f * p_0.unproject_0.z * p_0.unproject_0.w / (p_0.unproject_0.w + p_0.unproject_0.z - (1.0f - 2.0f * depth_0) * (p_0.unproject_0.w - p_0.unproject_0.z));
}

fn LitAtHitWithin_0( hitRelative_0 : vec3<f32>,  slack_0 : f32,  radiance_0 : ptr<function, vec3<f32>>) -> bool
{
    const _S22 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    (*radiance_0) = _S22;
    var _S23 : mat4x4<f32> = mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)]);
    var hitView_0 : vec3<f32> = (((hitRelative_0) * (transpose(mat3x3<f32>(_S23[i32(0)].xyz, _S23[i32(1)].xyz, _S23[i32(2)].xyz)))));
    var _S24 : f32 = hitView_0.z;
    if(_S24 <= (p_0.unproject_0.z))
    {
        return false;
    }
    var uv_3 : vec2<f32> = ViewToUv_0(hitView_0);
    var _S25 : bool;
    if((any((uv_3 < vec2<f32>(0.0f, 0.0f)))))
    {
        _S25 = true;
    }
    else
    {
        _S25 = (any((uv_3 > vec2<f32>(1.0f, 1.0f))));
    }
    if(_S25)
    {
        return false;
    }
    var _S26 : vec3<i32> = vec3<i32>(vec2<i32>(uv_3 * p_0.resolution_0.xy), i32(0));
    var _S27 : f32 = (textureLoad((uSceneDepth_0), ((_S26)).xy, ((_S26)).z));
    if(_S27 <= 0.0f)
    {
        return false;
    }
    if(_S27 >= 0.89999997615814209f)
    {
        return false;
    }
    var sceneZ_0 : f32 = SceneViewZ_0(_S27);
    if((abs(_S24 - sceneZ_0)) > (0.01999999955296516f * max(sceneZ_0, 1.0f) + slack_0))
    {
        return false;
    }
    (*radiance_0) = max((textureSampleLevel((uLit_0), (uLitSampler_0), (uv_3), (0.0f))).xyz, _S22);
    return true;
}

fn FieldNormal_0( world_3 : vec3<f32>,  voxel_4 : f32) -> vec3<f32>
{
    var _S28 : f32 = max(voxel_4, 0.00100000004749745f);
    var _S29 : vec3<f32> = vec3<f32>(_S28, 0.0f, 0.0f);
    var known_4 : bool;
    var raw_4 : f32;
    var resolved_4 : f32;
    var _S30 : f32 = DistanceAt_0(world_3 + _S29, &(known_4), &(raw_4), &(resolved_4));
    var _S31 : f32 = DistanceAt_0(world_3 - _S29, &(known_4), &(raw_4), &(resolved_4));
    var _S32 : f32 = _S30 - _S31;
    var _S33 : vec3<f32> = vec3<f32>(0.0f, _S28, 0.0f);
    var _S34 : f32 = DistanceAt_0(world_3 + _S33, &(known_4), &(raw_4), &(resolved_4));
    var _S35 : f32 = DistanceAt_0(world_3 - _S33, &(known_4), &(raw_4), &(resolved_4));
    var _S36 : f32 = _S34 - _S35;
    var _S37 : vec3<f32> = vec3<f32>(0.0f, 0.0f, _S28);
    var _S38 : f32 = DistanceAt_0(world_3 + _S37, &(known_4), &(raw_4), &(resolved_4));
    var _S39 : f32 = DistanceAt_0(world_3 - _S37, &(known_4), &(raw_4), &(resolved_4));
    var gradient_0 : vec3<f32> = vec3<f32>(_S32, _S36, _S38 - _S39);
    var length2_0 : f32 = dot(gradient_0, gradient_0);
    var _S40 : bool;
    if(length2_0 > 9.999999960041972e-13f)
    {
        _S40 = length2_0 < 1.0e+06f;
    }
    else
    {
        _S40 = false;
    }
    var _S41 : vec3<f32>;
    if(_S40)
    {
        _S41 = gradient_0 * vec3<f32>(rsqrt_0(length2_0));
    }
    else
    {
        _S41 = vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    return _S41;
}

fn LookupBucket_0( cell_0 : vec3<i32>) -> i32
{
    return i32(((((((u32(cell_0.x) * u32(2376512323)) ^ ((u32(cell_0.y) * u32(3625334849))))) ^ ((u32(cell_0.z) * u32(3407524639))))) & (u32(i32(p_0.cacheGrid_0.w) - i32(1)))));
}

fn MoverPages_0( headers_0 : i32,  index_0 : i32,  world_4 : vec3<f32>,  bias_0 : f32) -> vec2<i32>
{
    var at_2 : i32 = headers_0 + index_0 * i32(6);
    var reach_2 : f32 = (bitcast<f32>((uCellPages_0[at_2 + i32(3)]))) + bias_0;
    var toCentre_0 : vec3<f32> = world_4 - vec3<f32>((bitcast<f32>((uCellPages_0[at_2]))), (bitcast<f32>((uCellPages_0[at_2 + i32(1)]))), (bitcast<f32>((uCellPages_0[at_2 + i32(2)]))));
    var _S42 : vec2<i32>;
    if((dot(toCentre_0, toCentre_0)) > (reach_2 * reach_2))
    {
        _S42 = vec2<i32>(i32(0), i32(0));
    }
    else
    {
        _S42 = vec2<i32>(uCellPages_0[at_2 + i32(4)], uCellPages_0[at_2 + i32(5)]);
    }
    return _S42;
}

fn ProjectOntoCard_0( page_0 : i32,  world_5 : vec3<f32>,  directionW_0 : vec3<f32>,  across_0 : ptr<function, vec2<f32>>,  halfSize_0 : ptr<function, vec2<f32>>,  behind_0 : ptr<function, f32>,  depth_1 : ptr<function, f32>,  facing_0 : ptr<function, vec3<f32>>,  directionLocal_0 : ptr<function, vec3<f32>>,  litRect_0 : ptr<function, vec4<f32>>,  cardPage_0 : ptr<function, vec4<f32>>)
{
    var _S43 : i32 = page_0 * i32(10);
    (*litRect_0) = uPages_0[_S43];
    (*cardPage_0) = uPages_0[_S43 + i32(1)];
    var centreAndHalfWidth_0 : vec4<f32> = uPages_0[_S43 + i32(2)];
    var rightAndHalfHeight_0 : vec4<f32> = uPages_0[_S43 + i32(3)];
    var upAndDepth_0 : vec4<f32> = uPages_0[_S43 + i32(4)];
    var direction_2 : vec4<f32> = uPages_0[_S43 + i32(5)];
    var row0_0 : vec4<f32> = uPages_0[_S43 + i32(6)];
    var row1_0 : vec4<f32> = uPages_0[_S43 + i32(7)];
    var row2_0 : vec4<f32> = uPages_0[_S43 + i32(8)];
    var delta_0 : vec3<f32> = world_5 - vec3<f32>(row0_0.w, row1_0.w, row2_0.w);
    var _S44 : vec3<f32> = row0_0.xyz;
    var _S45 : vec3<f32> = row1_0.xyz;
    var _S46 : vec3<f32> = row2_0.xyz;
    var local_0 : vec3<f32> = vec3<f32>(dot(_S44, delta_0), dot(_S45, delta_0), dot(_S46, delta_0));
    (*directionLocal_0) = vec3<f32>(dot(_S44, directionW_0), dot(_S45, directionW_0), dot(_S46, directionW_0));
    var relative_0 : vec3<f32> = local_0 - centreAndHalfWidth_0.xyz;
    var _S47 : vec2<f32> = max(vec2<f32>(centreAndHalfWidth_0.w, rightAndHalfHeight_0.w), vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
    (*halfSize_0) = _S47;
    (*across_0) = vec2<f32>(dot(relative_0, rightAndHalfHeight_0.xyz), dot(relative_0, upAndDepth_0.xyz)) / _S47;
    var _S48 : vec3<f32> = direction_2.xyz;
    (*behind_0) = - dot(relative_0, _S48);
    (*depth_1) = upAndDepth_0.w;
    (*facing_0) = _S48;
    return;
}

fn SampleCardsWithin_0( world_6 : vec3<f32>,  normalW_0 : vec3<f32>,  bias_1 : f32,  radiance_1 : ptr<function, vec3<f32>>) -> bool
{
    const _S49 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    (*radiance_1) = _S49;
    if(i32(p_0.cacheGrid_0.w) <= i32(0))
    {
        return false;
    }
    var _S50 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_6 - p_0.cacheGrid_0.xyz) / vec3<f32>(p_0.cacheOrigin_0.w))))];
    var albedoWidth_0 : u32;
    var albedoHeight_0 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_0)) = dim.x;((albedoHeight_0)) = dim.y;};
    var _S51 : vec2<f32> = vec2<f32>(f32(albedoWidth_0), f32(albedoHeight_0));
    var _S52 : vec2<i32> = uCells_0[i32(p_0.cacheGrid_0.w)];
    var walk_0 : i32 = i32(0);
    var weightSum_0 : f32 = 0.0f;
    for(;;)
    {
        if(walk_0 <= (_S52.y))
        {
        }
        else
        {
            break;
        }
        var _S53 : bool = walk_0 == i32(0);
        var _S54 : vec2<i32>;
        if(_S53)
        {
            _S54 = _S50;
        }
        else
        {
            _S54 = MoverPages_0(_S52.x, walk_0 - i32(1), world_6, bias_1);
        }
        var slot_0 : i32 = i32(0);
        var weightSum_1 : f32 = weightSum_0;
        for(;;)
        {
            if(slot_0 < (_S54.y))
            {
            }
            else
            {
                break;
            }
            var page_1 : i32 = uCellPages_0[_S54.x + slot_0];
            var _S55 : i32 = page_1 * i32(10);
            if(((uPages_0[_S55 + i32(5)].w) > 0.5f) == _S53)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var bound_0 : vec4<f32> = uPages_0[_S55 + i32(9)];
            var toCentre_1 : vec3<f32> = world_6 - bound_0.xyz;
            var reach_3 : f32 = bound_0.w + bias_1;
            if((dot(toCentre_1, toCentre_1)) > (reach_3 * reach_3))
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var across_1 : vec2<f32>;
            var halfSize_1 : vec2<f32>;
            var behind_1 : f32;
            var depth_2 : f32;
            var facing_1 : vec3<f32>;
            var normalLocal_0 : vec3<f32>;
            var litRect_1 : vec4<f32>;
            var cardPage_1 : vec4<f32>;
            ProjectOntoCard_0(page_1, world_6, normalW_0, &(across_1), &(halfSize_1), &(behind_1), &(depth_2), &(facing_1), &(normalLocal_0), &(litRect_1), &(cardPage_1));
            var towards_0 : f32 = dot(facing_1, normalLocal_0);
            if(towards_0 <= 0.0f)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var squareness_0 : f32 = towards_0 * towards_0;
            var slack_1 : f32 = 0.5f * bias_1;
            var acrossSlack_0 : vec2<f32> = vec2<f32>(slack_1) / halfSize_1;
            var _S56 : bool;
            if((abs(across_1.x)) > (1.0f + acrossSlack_0.x))
            {
                _S56 = true;
            }
            else
            {
                _S56 = (abs(across_1.y)) > (1.0f + acrossSlack_0.y);
            }
            if(_S56)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S57 : bool;
            if(behind_1 < (- slack_1))
            {
                _S57 = true;
            }
            else
            {
                _S57 = behind_1 > (depth_2 + slack_1);
            }
            if(_S57)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S58 : vec2<f32> = vec2<f32>(0.5f);
            var atlasAt_0 : vec2<f32> = cardPage_1.xy + (clamp(across_1, vec2<f32>(-1.0f, -1.0f), vec2<f32>(1.0f, 1.0f)) * _S58 + _S58) * cardPage_1.zw - _S58;
            var lowCorner_0 : vec2<f32> = floor(atlasAt_0);
            var _S59 : vec2<f32> = atlasAt_0 - lowCorner_0;
            var _S60 : vec2<f32> = litRect_1.zw / max(cardPage_1.zw, vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
            var _S61 : f32 = behind_1 / max(depth_2, 9.99999997475242708e-07f);
            var _S62 : f32 = bias_1 / max(0.5f * depth_2, 9.99999997475242708e-07f);
            var taps_0 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
            var tapLight_0 : array<vec3<f32>, i32(4)>;
            var tap_0 : i32 = i32(0);
            for(;;)
            {
                if(tap_0 < i32(4))
                {
                }
                else
                {
                    break;
                }
                var step_1 : vec2<f32> = vec2<f32>(f32((tap_0 & (i32(1)))), f32((tap_0 >> (u32(1)))));
                var _S63 : vec2<f32> = lowCorner_0 + step_1 + _S58;
                var centre_0 : vec2<f32> = _S63 / _S51;
                var written_0 : f32;
                if(((textureSampleLevel((uAlbedo_0), (uAlbedoSampler_0), (centre_0), (0.0f))).w) >= 0.5f)
                {
                    written_0 = 1.0f;
                }
                else
                {
                    written_0 = 0.0f;
                }
                var bilinear2_0 : vec2<f32> = mix(vec2<f32>(1.0f) - _S59, _S59, step_1);
                taps_0[tap_0] = bilinear2_0.x * bilinear2_0.y * written_0 * (1.0f - saturate((abs(_S61 - (textureSampleLevel((uDepth_0), (uDepthSampler_0), (centre_0), (0.0f))).x) - _S62) / (0.25f * _S62)));
                tapLight_0[tap_0] = (textureSampleLevel((uFinal_0), (uFinalSampler_0), ((floor(litRect_1.xy + (_S63 - cardPage_1.xy) * _S60) + _S58) / vec3<f32>(p_0.cacheAtlas_0.xy, 0.0f).xy), (0.0f))).xyz;
                tap_0 = tap_0 + i32(1);
            }
            var coverage_0 : f32 = taps_0.x + taps_0.y + taps_0.z + taps_0.w;
            var cardWeight_0 : f32 = squareness_0 * coverage_0;
            if(cardWeight_0 <= 0.0f)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            (*radiance_1) = (*radiance_1) + max((tapLight_0[i32(0)] * vec3<f32>(taps_0.x) + tapLight_0[i32(1)] * vec3<f32>(taps_0.y) + tapLight_0[i32(2)] * vec3<f32>(taps_0.z) + tapLight_0[i32(3)] * vec3<f32>(taps_0.w)) / vec3<f32>(coverage_0), _S49) * vec3<f32>(cardWeight_0);
            weightSum_1 = weightSum_1 + cardWeight_0;
            slot_0 = slot_0 + i32(1);
        }
        walk_0 = walk_0 + i32(1);
        weightSum_0 = weightSum_1;
    }
    if(weightSum_0 <= 0.0f)
    {
        (*radiance_1) = _S49;
        return false;
    }
    (*radiance_1) = (*radiance_1) / vec3<f32>(weightSum_0);
    return true;
}

fn Resolve_0( relative_1 : vec3<f32>,  normalW_1 : vec3<f32>,  rayW_3 : vec3<f32>,  viewDepth_0 : f32) -> vec3<f32>
{
    gRoute_0 = i32(0);
    if(i32(p_0.field_0.x) <= i32(0))
    {
        return FallbackSky_0(rayW_3);
    }
    var sheltered_0 : bool = LosesCeiling_0(GlazingRoofs_0(), uShelterMap_0, relative_1.xy + p_0.cameraWorld_0.xy, relative_1.z + p_0.cameraWorld_0.z, rayW_3);
    var hit_1 : vec3<f32>;
    var voxel_5 : f32;
    var _S64 : bool = SurfaceAt_0(relative_1, normalW_1, rayW_3, p_0.trace_0.y, &(hit_1), &(voxel_5));
    if(!_S64)
    {
        if(!sheltered_0)
        {
            return FallbackSky_0(rayW_3);
        }
        gRoute_0 = i32(3);
        return Unknown_0(rayW_3);
    }
    var radiance_2 : vec3<f32>;
    var resolved_5 : bool = LitAtHitWithin_0(hit_1, voxel_5, &(radiance_2));
    if(resolved_5)
    {
        gRoute_0 = i32(1);
    }
    var hitNormal_0 : vec3<f32>;
    var resolved_6 : bool;
    if(!resolved_5)
    {
        var hitNormal_1 : vec3<f32> = FieldNormal_0(hit_1, voxel_5);
        if((dot(hitNormal_1, hitNormal_1)) <= 0.0f)
        {
            hitNormal_0 = (vec3<f32>(0) - rayW_3);
        }
        else
        {
            hitNormal_0 = hitNormal_1;
        }
        var cardRadiance_0 : vec3<f32>;
        var _S65 : bool = SampleCardsWithin_0(hit_1 + p_0.cacheOrigin_0.xyz, hitNormal_0, 0.25f + voxel_5 * 2.0f, &(cardRadiance_0));
        if(_S65)
        {
            radiance_2 = cardRadiance_0 * vec3<f32>(p_0.cacheAtlas_0.z);
            gRoute_0 = i32(2);
            resolved_6 = true;
        }
        else
        {
            resolved_6 = resolved_5;
        }
    }
    else
    {
        resolved_6 = resolved_5;
    }
    if(!resolved_6)
    {
        gRoute_0 = i32(3);
        if(sheltered_0)
        {
            hitNormal_0 = Unknown_0(rayW_3);
        }
        else
        {
            hitNormal_0 = FallbackSky_0(rayW_3);
        }
        return hitNormal_0;
    }
    var _S66 : f32 = max(radiance_2.x, max(radiance_2.y, radiance_2.z));
    if(_S66 > (p_0.trace_0.z))
    {
        radiance_2 = radiance_2 * vec3<f32>((p_0.trace_0.z / _S66));
    }
    return radiance_2;
}

fn RouteColour_0( route_0 : i32) -> vec3<f32>
{
    if(route_0 == i32(1))
    {
        return vec3<f32>(0.0f, 1.0f, 0.0f);
    }
    if(route_0 == i32(2))
    {
        return vec3<f32>(0.0f, 0.20000000298023224f, 1.0f);
    }
    if(route_0 == i32(3))
    {
        return vec3<f32>(1.0f, 0.0f, 0.0f);
    }
    if(route_0 == i32(4))
    {
        return vec3<f32>(1.0f, 0.80000001192092896f, 0.0f);
    }
    return vec3<f32>(0.25f, 0.25f, 0.25f);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    gRoute_0 = i32(0);
    var _S67 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S68 : bool;
    if((_S67.x) >= i32(p_0.resolution_0.x))
    {
        _S68 = true;
    }
    else
    {
        _S68 = (_S67.y) >= i32(p_0.resolution_0.y);
    }
    if(_S68)
    {
        return;
    }
    var _S69 : vec3<i32> = vec3<i32>(_S67, i32(0));
    var pane_0 : vec4<f32> = (textureLoad((uGlass_0), ((_S69)).xy, ((_S69)).z));
    var _S70 : f32 = pane_0.w;
    if(_S70 <= 0.0f)
    {
        textureStore((uOut_0), (vec2<u32>(_S67)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var normalW_2 : vec3<f32> = normalize(pane_0.xyz);
    var ndc_0 : vec2<f32> = (vec2<f32>(_S67) + vec2<f32>(0.5f)) * p_0.resolution_0.zw * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    var _S71 : mat4x4<f32> = mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)]);
    var relative_2 : vec3<f32> = (((vec3<f32>(ndc_0.x * _S70 / p_0.unproject_0.x, - ndc_0.y * _S70 / p_0.unproject_0.y, _S70)) * (mat3x3<f32>(_S71[i32(0)].xyz, _S71[i32(1)].xyz, _S71[i32(2)].xyz))));
    var toPixel_0 : vec3<f32> = normalize(relative_2);
    var normalW_3 : vec3<f32>;
    if((dot(normalW_2, toPixel_0)) > 0.0f)
    {
        normalW_3 = (vec3<f32>(0) - normalW_2);
    }
    else
    {
        normalW_3 = normalW_2;
    }
    var rayW_4 : vec3<f32> = normalize(reflect(toPixel_0, normalW_3));
    var fresnel_0 : f32 = 0.03999999910593033f + 0.95999997854232788f * pow(1.0f - clamp(dot(normalW_3, (vec3<f32>(0) - toPixel_0)), 0.0f, 1.0f), 5.0f);
    var radiance_3 : vec3<f32> = Resolve_0(relative_2, normalW_3, rayW_4, _S70);
    if((p_0.debug_0.w) > 3.5f)
    {
        var _S72 : vec3<f32> = vec3<f32>(0.5f);
        textureStore((uOut_0), (vec2<u32>(_S67)), (vec4<f32>(normalW_3 * _S72 + _S72, 1.0f)));
        return;
    }
    if((p_0.debug_0.w) > 2.5f)
    {
        var _S73 : vec3<f32> = vec3<f32>(0.5f);
        textureStore((uOut_0), (vec2<u32>(_S67)), (vec4<f32>(rayW_4 * _S73 + _S73, 1.0f)));
        return;
    }
    if((p_0.debug_0.w) > 0.5f)
    {
        _S68 = (p_0.debug_0.w) < 1.5f;
    }
    else
    {
        _S68 = false;
    }
    if(_S68)
    {
        textureStore((uOut_0), (vec2<u32>(_S67)), (vec4<f32>(RouteColour_0(gRoute_0), 1.0f)));
        return;
    }
    if((p_0.debug_0.x) > 0.5f)
    {
        textureStore((uOut_0), (vec2<u32>(_S67)), (vec4<f32>(radiance_3, 1.0f)));
        return;
    }
    textureStore((uOut_0), (vec2<u32>(_S67)), (vec4<f32>(radiance_3 * vec3<f32>(fresnel_0) * vec3<f32>(p_0.cacheAtlas_0.w), 1.0f)));
    return;
}

