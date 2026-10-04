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
    @align(16) ssr_0 : vec4<f32>,
    @align(16) misc_0 : vec4<f32>,
    @align(16) wet0_0 : vec4<f32>,
    @align(16) wet1_0 : vec4<f32>,
    @align(16) wet2_0 : vec4<f32>,
    @align(16) shelter0_0 : vec4<f32>,
    @align(16) shelter1_0 : vec4<f32>,
    @align(16) shelter2_0 : vec4<f32>,
    @align(16) trace_0 : vec4<f32>,
    @align(16) cacheOrigin_0 : vec4<f32>,
    @align(16) cacheGrid_0 : vec4<f32>,
    @align(16) cacheAtlas_0 : vec4<f32>,
    @align(16) trace2_0 : vec4<f32>,
    @align(16) field_0 : vec4<f32>,
    @align(16) sdfOrigin_0 : array<vec4<f32>, i32(4)>,
    @align(16) sdfRange_0 : _Array_std140_float4_0,
};

@binding(26) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(2) @group(0) var uSceneDepth_0 : texture_depth_2d;

@binding(3) @group(0) var uSceneDepthSampler_0 : sampler;

@binding(6) @group(0) var uMaterial_0 : texture_2d<f32>;

@binding(7) @group(0) var uMaterialSampler_0 : sampler;

@binding(14) @group(0) var uReflection_0 : texture_storage_2d<rgba16float, write>;

@binding(4) @group(0) var uNormal_0 : texture_2d<f32>;

@binding(5) @group(0) var uNormalSampler_0 : sampler;

@binding(10) @group(0) var uShelterMap_0 : texture_2d<f32>;

@binding(12) @group(0) var uBaseColour_0 : texture_2d<f32>;

@binding(13) @group(0) var uBaseColourSampler_0 : sampler;

@binding(0) @group(0) var uLit_0 : texture_2d<f32>;

@binding(1) @group(0) var uLitSampler_0 : sampler;

@binding(8) @group(0) var uSkyView_0 : texture_2d<f32>;

@binding(9) @group(0) var uSkyViewSampler_0 : sampler;

@binding(24) @group(0) var uClipmaps_0 : texture_3d<f32>;

@binding(25) @group(0) var uClipmapsSampler_0 : sampler;

@binding(16) @group(0) var<storage, read> uCells_0 : array<vec2<i32>>;

@binding(18) @group(0) var uAlbedo_0 : texture_2d<f32>;

@binding(17) @group(0) var<storage, read> uCellPages_0 : array<i32>;

@binding(15) @group(0) var<storage, read> uPages_0 : array<vec4<f32>>;

@binding(19) @group(0) var uAlbedoSampler_0 : sampler;

@binding(20) @group(0) var uDepth_0 : texture_2d<f32>;

@binding(21) @group(0) var uDepthSampler_0 : sampler;

@binding(22) @group(0) var uFinal_0 : texture_2d<f32>;

@binding(23) @group(0) var uFinalSampler_0 : sampler;

var<private> kRippleOffset_0 : array<vec2<f32>, i32(4)> = array<vec2<f32>, i32(4)>( vec2<f32>(0.0f, 0.0f), vec2<f32>(0.43000000715255737f, 0.70999997854232788f), vec2<f32>(0.79000002145767212f, 0.23999999463558197f), vec2<f32>(0.17000000178813934f, 0.46000000834465027f) );
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

fn DecodeMatte_0( material_0 : vec4<f32>) -> bool
{
    return ((i32(material_0.w * 255.0f + 0.5f) & (i32(16)))) != i32(0);
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

struct WetParams_0
{
     camera_0 : vec2<f32>,
     amount_0 : f32,
     coverage_0 : f32,
     puddleSize_0 : f32,
     rippleStrength_0 : f32,
     rippleSize_0 : f32,
     windStrength_0 : f32,
     seconds_0 : f32,
};

fn Weather_0() -> WetParams_0
{
    var w_0 : WetParams_0;
    w_0.camera_0 = p_0.wet1_0.zw;
    w_0.amount_0 = max(p_0.misc_0.x, 0.0f);
    w_0.coverage_0 = p_0.wet0_0.x;
    w_0.puddleSize_0 = p_0.wet0_0.y;
    w_0.rippleStrength_0 = p_0.wet0_0.z;
    w_0.seconds_0 = p_0.wet0_0.w;
    w_0.rippleSize_0 = p_0.wet1_0.x;
    w_0.windStrength_0 = p_0.wet1_0.y;
    return w_0;
}

struct ShelterMap_0
{
     origin_0 : vec2<f32>,
     invCell_0 : f32,
     lowestZ_0 : f32,
     scale_0 : f32,
     headroom_0 : f32,
     reach_0 : f32,
     size_0 : vec2<f32>,
     present_0 : f32,
};

fn Roofs_0() -> ShelterMap_0
{
    var m_0 : ShelterMap_0;
    m_0.origin_0 = p_0.shelter0_0.xy;
    m_0.invCell_0 = p_0.shelter0_0.z;
    m_0.lowestZ_0 = p_0.shelter0_0.w;
    m_0.scale_0 = p_0.shelter1_0.x;
    m_0.headroom_0 = p_0.shelter1_0.y;
    m_0.reach_0 = p_0.shelter1_0.z;
    m_0.size_0 = p_0.shelter2_0.xy;
    m_0.present_0 = p_0.wet2_0.x;
    return m_0;
}

fn ShelterTexel_0( m_1 : ShelterMap_0,  worldXY_0 : vec2<f32>) -> vec2<i32>
{
    return vec2<i32>(floor((worldXY_0 - m_1.origin_0) * vec2<f32>(m_1.invCell_0)));
}

fn ShelterHolds_0( m_2 : ShelterMap_0,  texel_0 : vec2<i32>) -> bool
{
    var _S10 : bool;
    if((m_2.present_0) > 0.5f)
    {
        _S10 = (texel_0.x) >= i32(0);
    }
    else
    {
        _S10 = false;
    }
    if(_S10)
    {
        _S10 = (texel_0.y) >= i32(0);
    }
    else
    {
        _S10 = false;
    }
    if(_S10)
    {
        _S10 = (texel_0.x) < i32(m_2.size_0.x);
    }
    else
    {
        _S10 = false;
    }
    if(_S10)
    {
        _S10 = (texel_0.y) < i32(m_2.size_0.y);
    }
    else
    {
        _S10 = false;
    }
    return _S10;
}

fn ShelterSurface_0( m_3 : ShelterMap_0,  texel_1 : vec4<f32>) -> f32
{
    var packed_0 : f32 = floor(texel_1.z * 255.0f + 0.5f) + floor(texel_1.y * 255.0f + 0.5f) * 256.0f;
    var _S11 : f32;
    if(packed_0 < 0.5f)
    {
        _S11 = -1.00000001504746622e+30f;
    }
    else
    {
        _S11 = m_3.lowestZ_0 + (packed_0 - 1.0f) * m_3.scale_0;
    }
    return _S11;
}

fn ShelterCovers_0( m_4 : ShelterMap_0,  surfaceZ_0 : f32,  worldZ_0 : f32) -> bool
{
    var _S12 : bool;
    if(surfaceZ_0 > (worldZ_0 + m_4.headroom_0))
    {
        _S12 = surfaceZ_0 < (worldZ_0 + m_4.reach_0);
    }
    else
    {
        _S12 = false;
    }
    return _S12;
}

fn UnderShelter_0( relative_0 : vec3<f32>) -> bool
{
    var m_5 : ShelterMap_0 = Roofs_0();
    var at_0 : vec2<i32> = ShelterTexel_0(m_5, relative_0.xy + p_0.wet1_0.zw);
    if(!ShelterHolds_0(m_5, at_0))
    {
        return false;
    }
    var _S13 : vec3<i32> = vec3<i32>(at_0, i32(0));
    return ShelterCovers_0(m_5, ShelterSurface_0(m_5, (textureLoad((uShelterMap_0), ((_S13)).xy, ((_S13)).z))), relative_0.z + p_0.wet2_0.y);
}

fn WetRotate_0( v_0 : vec2<f32>,  rotation_0 : vec2<f32>) -> vec2<f32>
{
    var _S14 : f32 = rotation_0.x;
    var _S15 : f32 = v_0.x;
    var _S16 : f32 = rotation_0.y;
    var _S17 : f32 = v_0.y;
    return vec2<f32>(_S14 * _S15 - _S16 * _S17, _S16 * _S15 + _S14 * _S17);
}

struct WetGrid_0
{
     uv_3 : vec2<f32>,
     base_0 : vec2<f32>,
};

fn WetGridAt_0( w_1 : WetParams_0,  relative_1 : vec3<f32>,  size_1 : f32,  rotation_1 : vec2<f32>) -> WetGrid_0
{
    var _S18 : vec2<f32> = vec2<f32>(size_1);
    var camera_1 : vec2<f32> = WetRotate_0(w_1.camera_0, rotation_1) / _S18;
    var cell_0 : vec2<f32> = floor(camera_1);
    var grid_0 : WetGrid_0;
    grid_0.uv_3 = WetRotate_0(relative_1.xy, rotation_1) / _S18 + (camera_1 - cell_0);
    grid_0.base_0 = cell_0;
    return grid_0;
}

fn WetHash_0( cell_1 : vec2<f32>) -> f32
{
    var _S19 : vec2<u32> = vec2<u32>(vec2<i32>(cell_1) + vec2<i32>(i32(1048576)));
    var h_0 : u32 = ((_S19.x * u32(73856093)) ^ ((_S19.y * u32(19349663))));
    var h_1 : u32 = ((h_0 ^ (((h_0 >> (u32(13))))))) * u32(1540483477);
    return f32((((h_1 ^ (((h_1 >> (u32(15))))))) & (u32(16777215)))) * 5.96046518808179826e-08f;
}

fn WetNoise_0( p_1 : vec2<f32>,  base_1 : vec2<f32>) -> f32
{
    var cell_2 : vec2<f32> = floor(p_1);
    var f_0 : vec2<f32> = p_1 - cell_2;
    var f_1 : vec2<f32> = f_0 * f_0 * (vec2<f32>(3.0f) - vec2<f32>(2.0f) * f_0);
    var cell_3 : vec2<f32> = cell_2 + base_1;
    var _S20 : f32 = f_1.x;
    return mix(mix(WetHash_0(cell_3), WetHash_0(cell_3 + vec2<f32>(1.0f, 0.0f)), _S20), mix(WetHash_0(cell_3 + vec2<f32>(0.0f, 1.0f)), WetHash_0(cell_3 + vec2<f32>(1.0f, 1.0f)), _S20), f_1.y);
}

fn WetGridAt_1( w_2 : WetParams_0,  relative_2 : vec3<f32>,  size_2 : f32) -> WetGrid_0
{
    return WetGridAt_0(w_2, relative_2, size_2, vec2<f32>(1.0f, 0.0f));
}

fn PuddleHeight_0( w_3 : WetParams_0,  relative_3 : vec3<f32>,  size_3 : f32) -> f32
{
    var fine_0 : WetGrid_0 = WetGridAt_0(w_3, relative_3, size_3 * 0.46000000834465027f, vec2<f32>(0.80000001192092896f, 0.60000002384185791f));
    var detail_0 : f32 = WetNoise_0(fine_0.uv_3 + vec2<f32>(11.30000019073486328f), fine_0.base_0);
    var coarse_0 : WetGrid_0 = WetGridAt_1(w_3, relative_3, size_3);
    return WetNoise_0(coarse_0.uv_3 + vec2<f32>(((detail_0 - 0.5f) * 0.40000000596046448f)), coarse_0.base_0) * 0.64999997615814209f + detail_0 * 0.34999999403953552f;
}

fn PuddleDepth_0( w_4 : WetParams_0,  relative_4 : vec3<f32>) -> f32
{
    return clamp(w_4.coverage_0, 0.0f, 1.0f) - PuddleHeight_0(w_4, relative_4, max(w_4.puddleSize_0, 0.05000000074505806f));
}

fn PuddleLevel_0( normal_0 : vec3<f32>) -> f32
{
    return clamp((normal_0.z - 0.86000001430511475f) * 12.0f, 0.0f, 1.0f);
}

fn PuddleMask_0( w_5 : WetParams_0,  relative_5 : vec3<f32>,  normal_1 : vec3<f32>) -> f32
{
    return clamp(PuddleDepth_0(w_5, relative_5) * 20.0f, 0.0f, 1.0f) * PuddleLevel_0(normal_1) * w_5.amount_0;
}

fn WetFootprint_0( ndcPerPixel_0 : f32,  viewZ_1 : f32,  xScale_0 : f32,  nDotV_0 : f32) -> f32
{
    return abs(ndcPerPixel_0 * viewZ_1) / max(xScale_0, 0.00009999999747379f) / max(nDotV_0, 0.07999999821186066f);
}

fn WetHash3_0( cell_4 : vec2<f32>) -> vec3<f32>
{
    var _S21 : vec2<u32> = vec2<u32>(vec2<i32>(cell_4) + vec2<i32>(i32(1048576)));
    var h_2 : u32 = ((_S21.x * u32(73856093)) ^ ((_S21.y * u32(19349663))));
    var h_3 : u32 = ((h_2 ^ (((h_2 >> (u32(13))))))) * u32(1540483477);
    var h_4 : u32 = (h_3 ^ (((h_3 >> (u32(15))))));
    var g_0 : u32 = h_4 * u32(668265261);
    var g_1 : u32 = (g_0 ^ (((g_0 >> (u32(15))))));
    var k_0 : u32 = g_1 * u32(2246822507);
    return vec3<f32>(f32((h_4 & (u32(16777215)))), f32((g_1 & (u32(16777215)))), f32((((k_0 ^ (((k_0 >> (u32(13))))))) & (u32(16777215))))) * vec3<f32>(5.96046518808179826e-08f);
}

fn RippleLayer_0( uv_4 : vec2<f32>,  base_2 : vec2<f32>,  phase_0 : f32,  weight_1 : f32) -> vec2<f32>
{
    if(weight_1 <= 0.0f)
    {
        return vec2<f32>(0.0f, 0.0f);
    }
    var _S22 : f32 = mix(0.20000000298023224f, 1.0f, clamp(weight_1, 0.0f, 1.0f));
    const _S23 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    var _S24 : vec2<f32> = vec2<f32>(0.5f);
    var _S25 : vec2<f32> = floor(uv_4 - _S24);
    var j_0 : i32 = i32(0);
    var slope_0 : vec2<f32> = _S23;
    for(;;)
    {
        if(j_0 < i32(2))
        {
        }
        else
        {
            break;
        }
        var i_0 : i32 = i32(0);
        var slope_1 : vec2<f32> = slope_0;
        for(;;)
        {
            if(i_0 < i32(2))
            {
            }
            else
            {
                break;
            }
            var cell_5 : vec2<f32> = _S25 + vec2<f32>(f32(i_0), f32(j_0));
            var fromCentre_0 : vec2<f32> = uv_4 - (cell_5 + _S24);
            if((dot(fromCentre_0, fromCentre_0)) > 0.60839992761611938f)
            {
                i_0 = i_0 + i32(1);
                continue;
            }
            var h_5 : vec3<f32> = WetHash3_0(cell_5 + base_2);
            var t_0 : f32 = fract(phase_0 + h_5.x);
            var fromStrike_0 : vec2<f32> = fromCentre_0 - (h_5.yz - _S24) * _S24;
            var distance_0 : f32 = length(fromStrike_0);
            var wave_0 : f32 = sin(3.14159274101257324f * clamp((t_0 - 1.0f + (1.0f - clamp(distance_0 / 0.46000000834465027f, 0.0f, 1.0f))) * 24.0f, 0.0f, 4.0f));
            var envelope_0 : f32 = clamp(_S22 - t_0, 0.0f, 1.0f);
            var outward_0 : vec2<f32>;
            if(distance_0 > 0.00000999999974738f)
            {
                outward_0 = fromStrike_0 / vec2<f32>(distance_0);
            }
            else
            {
                outward_0 = _S23;
            }
            slope_1 = slope_1 - outward_0 * vec2<f32>((wave_0 * envelope_0 * 0.44999998807907104f));
            i_0 = i_0 + i32(1);
        }
        j_0 = j_0 + i32(1);
        slope_0 = slope_1;
    }
    return slope_0;
}

fn RainRippleSlope_0( w_6 : WetParams_0,  relative_6 : vec3<f32>,  size_4 : f32,  time_0 : f32,  intensity_0 : f32) -> vec2<f32>
{
    const _S26 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    var k_1 : i32 = i32(0);
    var slope_2 : vec2<f32> = _S26;
    for(;;)
    {
        if(k_1 < i32(4))
        {
        }
        else
        {
            break;
        }
        var weight_2 : f32 = clamp((intensity_0 - vec4<f32>(0.0f, 0.2199999988079071f, 0.43999999761581421f, 0.6600000262260437f)[k_1]) / 0.25f, 0.0f, 1.0f);
        if(weight_2 <= 0.0f)
        {
            k_1 = k_1 + i32(1);
            continue;
        }
        var grid_1 : WetGrid_0 = WetGridAt_1(w_6, relative_6, size_4 * vec4<f32>(0.51999998092651367f, 0.74000000953674316f, 0.97000002861022949f, 1.27999997138977051f)[k_1]);
        slope_2 = slope_2 + RippleLayer_0(grid_1.uv_3 + kRippleOffset_0[k_1], grid_1.base_0, fract(time_0 * vec4<f32>(0.95999997854232788f, 0.81000000238418579f, 0.88999998569488525f, 0.72000002861022949f)[k_1] + vec4<f32>(0.0f, 0.31000000238418579f, 0.57999998331069946f, 0.82999998331069946f)[k_1]), weight_2);
        k_1 = k_1 + i32(1);
    }
    return slope_2;
}

fn WindRippleSlope_0( w_7 : WetParams_0,  relative_7 : vec3<f32>,  time_1 : f32) -> vec2<f32>
{
    var grid_2 : WetGrid_0 = WetGridAt_1(w_7, relative_7, 1.79999995231628418f);
    var p_2 : vec2<f32> = grid_2.uv_3 + vec2<f32>(time_1 * 0.07999999821186066f, time_1 * 0.05000000074505806f);
    var here_0 : f32 = WetNoise_0(p_2, grid_2.base_0);
    return vec2<f32>(WetNoise_0(p_2 + vec2<f32>(0.15000000596046448f, 0.0f), grid_2.base_0) - here_0, WetNoise_0(p_2 + vec2<f32>(0.0f, 0.15000000596046448f), grid_2.base_0) - here_0);
}

struct WaterSurface_0
{
     normal_2 : vec3<f32>,
     roughness_0 : f32,
     mirror_0 : f32,
};

fn WaterSurfaceAt_0( w_8 : WetParams_0,  relative_8 : vec3<f32>,  footprint_0 : f32) -> WaterSurface_0
{
    var _S27 : f32 = max(w_8.rippleSize_0, 0.01999999955296516f);
    var slope_3 : vec2<f32> = RainRippleSlope_0(w_8, relative_8, _S27, w_8.seconds_0 * 0.60000002384185791f, w_8.rippleStrength_0) + WindRippleSlope_0(w_8, relative_8, w_8.seconds_0) * vec2<f32>(w_8.windStrength_0) * vec2<f32>(1.60000002384185791f);
    var blur_0 : f32 = clamp(footprint_0 / (_S27 * 0.5f), 0.0f, 1.0f);
    var surface_0 : WaterSurface_0;
    var _S28 : f32 = 1.0f - blur_0;
    surface_0.normal_2 = normalize(vec3<f32>(slope_3 * vec2<f32>(_S28), 1.0f));
    surface_0.roughness_0 = clamp(0.05000000074505806f + length(slope_3) * _S28 * 0.10000000149011612f + (0.20000000298023224f * w_8.rippleStrength_0 + 0.10000000149011612f * w_8.windStrength_0) * blur_0, 0.05000000074505806f, 0.31999999284744263f);
    surface_0.mirror_0 = clamp(1.0f - blur_0 * 2.0f, 0.0f, 1.0f);
    return surface_0;
}

fn WaterFresnel_0( normal_3 : vec3<f32>,  toEye_0 : vec3<f32>) -> f32
{
    var grazing_0 : f32 = 1.0f - clamp(dot(normal_3, toEye_0), 0.0f, 1.0f);
    var squared_0 : f32 = grazing_0 * grazing_0;
    return 0.01999999955296516f + 0.98000001907348633f * squared_0 * squared_0 * grazing_0;
}

fn GradientNoise_0( q_0 : vec2<f32>) -> f32
{
    return fract(52.98291778564453125f * fract(0.06711056083440781f * q_0.x + 0.00583714991807938f * q_0.y));
}

fn FrameNoise_0( pos_0 : vec2<f32>,  frame_0 : f32) -> f32
{
    return GradientNoise_0(pos_0 + vec2<f32>(frame_0) * vec2<f32>(12.07804298400878906f, 9.11744499206542969f));
}

fn Dither_0( pos_1 : vec2<f32>,  frame_1 : f32) -> f32
{
    return FrameNoise_0(pos_1, frame_1);
}

fn ViewToUv_0( v_1 : vec3<f32>) -> vec2<f32>
{
    var _S29 : f32 = v_1.z;
    var _S30 : vec2<f32> = vec2<f32>(0.5f);
    return vec2<f32>(v_1.x * p_0.unproject_0.x / _S29, - v_1.y * p_0.unproject_0.y / _S29) * _S30 + _S30;
}

fn SceneViewZ_0( depth_4 : f32) -> f32
{
    return 2.0f * p_0.unproject_0.z * p_0.unproject_0.w / (p_0.unproject_0.w + p_0.unproject_0.z - (1.0f - 2.0f * depth_4) * (p_0.unproject_0.w - p_0.unproject_0.z));
}

fn SkyViewUv_0( worldDir_0 : vec3<f32>,  viewHeight_0 : f32) -> vec2<f32>
{
    var beta_0 : f32 = acos(clamp(sqrt(max(0.0f, viewHeight_0 * viewHeight_0 - 4.04496e+07f)) / viewHeight_0, -1.0f, 1.0f));
    var zenithHorizonAngle_0 : f32 = 3.14159274101257324f - beta_0;
    var viewZenithAngle_0 : f32 = acos(clamp(worldDir_0.z, -1.0f, 1.0f));
    var v_2 : f32;
    if(viewZenithAngle_0 < zenithHorizonAngle_0)
    {
        v_2 = (1.0f - sqrt(max(0.0f, 1.0f - viewZenithAngle_0 / zenithHorizonAngle_0))) * 0.5f;
    }
    else
    {
        v_2 = sqrt(max(0.0f, (viewZenithAngle_0 - zenithHorizonAngle_0) / max(beta_0, 9.99999997475242708e-07f))) * 0.5f + 0.5f;
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
    return vec2<f32>(azimuth_1 / 6.28318548202514648f, v_2);
}

fn SkyViewInset_0( uv_5 : vec2<f32>,  size_5 : vec2<f32>) -> vec2<f32>
{
    return uv_5 * ((size_5 - vec2<f32>(1.0f)) / size_5) + vec2<f32>(0.5f) / size_5;
}

fn SampleSkyViewUv_0( skyView_0 : texture_2d<f32>,  skyViewSampler_0 : sampler,  uv_6 : vec2<f32>) -> vec3<f32>
{
    var width_1 : u32;
    var height_1 : u32;
    {var dim = textureDimensions((skyView_0));((width_1)) = dim.x;((height_1)) = dim.y;};
    return (textureSampleLevel((skyView_0), (skyViewSampler_0), (SkyViewInset_0(uv_6, vec2<f32>(f32(width_1), f32(height_1)))), (0.0f))).xyz;
}

fn SampleSkyView_0( skyView_1 : texture_2d<f32>,  skyViewSampler_1 : sampler,  worldDir_1 : vec3<f32>,  viewHeight_1 : f32) -> vec3<f32>
{
    return SampleSkyViewUv_0(skyView_1, skyViewSampler_1, SkyViewUv_0(worldDir_1, viewHeight_1));
}

fn WaterSkyward_0( reflected_0 : vec3<f32>) -> f32
{
    return clamp(reflected_0.z * 20.0f, 0.0f, 1.0f);
}

fn LosesCeiling_0( m_6 : ShelterMap_0,  map_0 : texture_2d<f32>,  worldXY_1 : vec2<f32>,  worldZ_1 : f32,  rayW_0 : vec3<f32>) -> bool
{
    var at_1 : vec2<i32> = ShelterTexel_0(m_6, worldXY_1);
    if(!ShelterHolds_0(m_6, at_1))
    {
        return false;
    }
    var _S31 : vec3<i32> = vec3<i32>(at_1, i32(0));
    var roof_0 : f32 = ShelterSurface_0(m_6, (textureLoad((map_0), ((_S31)).xy, ((_S31)).z)));
    var _S32 : f32 = worldZ_1 + m_6.headroom_0;
    if(roof_0 <= _S32)
    {
        return false;
    }
    var _S33 : f32 = rayW_0.z;
    if(_S33 <= 0.05000000074505806f)
    {
        return false;
    }
    var above_0 : vec2<i32> = ShelterTexel_0(m_6, worldXY_1 + rayW_0.xy * vec2<f32>(((roof_0 - worldZ_1) / _S33)));
    if(!ShelterHolds_0(m_6, above_0))
    {
        return false;
    }
    var _S34 : vec3<i32> = vec3<i32>(above_0, i32(0));
    return (ShelterSurface_0(m_6, (textureLoad((map_0), ((_S34)).xy, ((_S34)).z)))) > _S32;
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
        var _S35 : f32 = p_0.sdfOrigin_0[level_0].w;
        var voxel_0 : vec3<f32> = (world_0 - p_0.sdfOrigin_0[level_0].xyz) / vec3<f32>(_S35);
        var _S36 : bool;
        if((any((voxel_0 < vec3<f32>(0.5f, 0.5f, 0.5f)))))
        {
            _S36 = true;
        }
        else
        {
            _S36 = (any((voxel_0 > vec3<f32>(f32(i32(p_0.field_0.y)) - 0.5f, f32(i32(p_0.field_0.y)) - 0.5f, f32(i32(p_0.field_0.y)) - 0.5f))));
        }
        if(_S36)
        {
            level_0 = level_0 + i32(1);
            continue;
        }
        var uvw_0 : vec3<f32> = vec3<f32>(voxel_0.x / f32(i32(p_0.field_0.y)), voxel_0.y / f32(i32(p_0.field_0.y)), (f32(level_0) * f32(i32(p_0.field_0.y)) + voxel_0.z) / f32(i32(p_0.field_0.y) * i32(p_0.field_0.x)));
        (*known_0) = true;
        (*raw_0) = (textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (uvw_0), (0.0f))).x;
        (*resolved_0) = _S35;
        return ((*raw_0) * 2.0f - 1.0f) * p_0.sdfRange_0.data_1[level_0].x;
    }
    return 1.0e+06f;
}

fn Settle_0( at_2 : vec3<f32>,  direction_0 : vec3<f32>,  travelled_0 : f32,  voxel_1 : ptr<function, f32>) -> vec3<f32>
{
    (*voxel_1) = p_0.sdfOrigin_0[i32(0)].w;
    var pass_0 : i32 = i32(0);
    var _S37 : f32 = travelled_0;
    for(;;)
    {
        if(pass_0 < i32(2))
        {
        }
        else
        {
            break;
        }
        var known_1 : bool;
        var raw_1 : f32;
        var resolved_1 : f32;
        var distance_1 : f32 = DistanceAt_0(at_2 + direction_0 * vec3<f32>(_S37), &(known_1), &(raw_1), &(resolved_1));
        if(!known_1)
        {
            break;
        }
        (*voxel_1) = resolved_1;
        var _S38 : f32 = _S37 + max(distance_1, 0.0f);
        pass_0 = pass_0 + i32(1);
        _S37 = _S38;
    }
    return at_2 + direction_0 * vec3<f32>((_S37 + (*voxel_1) * 0.25f));
}

fn SurfaceAt_0( world_1 : vec3<f32>,  normal_4 : vec3<f32>,  direction_1 : vec3<f32>,  reach_1 : f32,  hit_0 : ptr<function, vec3<f32>>,  voxel_2 : ptr<function, f32>) -> bool
{
    (*hit_0) = world_1;
    (*voxel_2) = p_0.sdfOrigin_0[i32(0)].w;
    if(i32(p_0.field_0.x) <= i32(0))
    {
        return false;
    }
    var _S39 : vec3<f32> = world_1 + normal_4 * vec3<f32>(p_0.sdfOrigin_0[i32(0)].w) * vec3<f32>(1.5f);
    var started_0 : bool = false;
    var previous_0 : f32 = 0.0f;
    var previousDistance_0 : f32 = 0.0f;
    var step_0 : i32 = i32(0);
    var travelled_1 : f32 = 0.0f;
    for(;;)
    {
        var _S40 : bool;
        if(step_0 < i32(96))
        {
            _S40 = travelled_1 < reach_1;
        }
        else
        {
            _S40 = false;
        }
        if(_S40)
        {
        }
        else
        {
            break;
        }
        var known_2 : bool;
        var raw_2 : f32;
        var resolved_2 : f32;
        var distance_2 : f32 = DistanceAt_0(_S39 + direction_1 * vec3<f32>(travelled_1), &(known_2), &(raw_2), &(resolved_2));
        if(!known_2)
        {
            return false;
        }
        if(started_0)
        {
            if(distance_2 <= 0.0f)
            {
                var span_0 : f32 = travelled_1 - previous_0;
                var fall_0 : f32 = previousDistance_0 - distance_2;
                var crossing_0 : f32;
                if(fall_0 > 0.00000999999974738f)
                {
                    crossing_0 = previous_0 + span_0 * (previousDistance_0 / fall_0);
                }
                else
                {
                    crossing_0 = travelled_1;
                }
                var _S41 : vec3<f32> = Settle_0(_S39, direction_1, clamp(crossing_0, previous_0, travelled_1), &((*voxel_2)));
                (*hit_0) = _S41;
                return true;
            }
            var _S42 : bool;
            if(distance_2 > previousDistance_0)
            {
                _S42 = previousDistance_0 < (resolved_2 * 0.05000000074505806f);
            }
            else
            {
                _S42 = false;
            }
            if(_S42)
            {
                var _S43 : vec3<f32> = Settle_0(_S39, direction_1, previous_0, &((*voxel_2)));
                (*hit_0) = _S43;
                return true;
            }
        }
        var travelled_2 : f32 = travelled_1 + max(distance_2, resolved_2 * 0.25f);
        var _S44 : i32 = step_0 + i32(1);
        started_0 = true;
        previous_0 = travelled_1;
        travelled_1 = travelled_2;
        previousDistance_0 = distance_2;
        step_0 = _S44;
    }
    return false;
}

fn LitAtHitWithin_0( hitRelative_0 : vec3<f32>,  slack_0 : f32,  radiance_0 : ptr<function, vec3<f32>>) -> bool
{
    const _S45 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    (*radiance_0) = _S45;
    var _S46 : mat4x4<f32> = mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)]);
    var hitView_0 : vec3<f32> = (((hitRelative_0) * (transpose(mat3x3<f32>(_S46[i32(0)].xyz, _S46[i32(1)].xyz, _S46[i32(2)].xyz)))));
    var _S47 : f32 = hitView_0.z;
    if(_S47 <= (p_0.unproject_0.z))
    {
        return false;
    }
    var uv_7 : vec2<f32> = ViewToUv_0(hitView_0);
    var _S48 : bool;
    if((any((uv_7 < vec2<f32>(0.0f, 0.0f)))))
    {
        _S48 = true;
    }
    else
    {
        _S48 = (any((uv_7 > vec2<f32>(1.0f, 1.0f))));
    }
    if(_S48)
    {
        return false;
    }
    var _S49 : vec3<i32> = vec3<i32>(vec2<i32>(uv_7 * p_0.resolution_0.xy), i32(0));
    var _S50 : f32 = (textureLoad((uSceneDepth_0), ((_S49)).xy, ((_S49)).z));
    if(_S50 <= 0.0f)
    {
        return false;
    }
    if(_S50 >= 0.89999997615814209f)
    {
        return false;
    }
    var sceneZ_0 : f32 = SceneViewZ_0(_S50);
    if((abs(_S47 - sceneZ_0)) > (0.01999999955296516f * max(sceneZ_0, 1.0f) + slack_0))
    {
        return false;
    }
    (*radiance_0) = max((textureSampleLevel((uLit_0), (uLitSampler_0), (uv_7), (0.0f))).xyz, _S45);
    return true;
}

fn LookupBucket_0( cell_6 : vec3<i32>) -> i32
{
    return i32(((((((u32(cell_6.x) * u32(2376512323)) ^ ((u32(cell_6.y) * u32(3625334849))))) ^ ((u32(cell_6.z) * u32(3407524639))))) & (u32(i32(p_0.cacheGrid_0.w) - i32(1)))));
}

fn MoverPages_0( headers_0 : i32,  index_0 : i32,  world_2 : vec3<f32>,  bias_0 : f32) -> vec2<i32>
{
    var at_3 : i32 = headers_0 + index_0 * i32(6);
    var reach_2 : f32 = (bitcast<f32>((uCellPages_0[at_3 + i32(3)]))) + bias_0;
    var toCentre_0 : vec3<f32> = world_2 - vec3<f32>((bitcast<f32>((uCellPages_0[at_3]))), (bitcast<f32>((uCellPages_0[at_3 + i32(1)]))), (bitcast<f32>((uCellPages_0[at_3 + i32(2)]))));
    var _S51 : vec2<i32>;
    if((dot(toCentre_0, toCentre_0)) > (reach_2 * reach_2))
    {
        _S51 = vec2<i32>(i32(0), i32(0));
    }
    else
    {
        _S51 = vec2<i32>(uCellPages_0[at_3 + i32(4)], uCellPages_0[at_3 + i32(5)]);
    }
    return _S51;
}

fn ProjectOntoCard_0( page_0 : i32,  world_3 : vec3<f32>,  directionW_0 : vec3<f32>,  across_0 : ptr<function, vec2<f32>>,  halfSize_0 : ptr<function, vec2<f32>>,  behind_0 : ptr<function, f32>,  depth_5 : ptr<function, f32>,  facing_0 : ptr<function, vec3<f32>>,  directionLocal_0 : ptr<function, vec3<f32>>,  litRect_0 : ptr<function, vec4<f32>>,  cardPage_0 : ptr<function, vec4<f32>>)
{
    var _S52 : i32 = page_0 * i32(10);
    (*litRect_0) = uPages_0[_S52];
    (*cardPage_0) = uPages_0[_S52 + i32(1)];
    var centreAndHalfWidth_0 : vec4<f32> = uPages_0[_S52 + i32(2)];
    var rightAndHalfHeight_0 : vec4<f32> = uPages_0[_S52 + i32(3)];
    var upAndDepth_0 : vec4<f32> = uPages_0[_S52 + i32(4)];
    var direction_2 : vec4<f32> = uPages_0[_S52 + i32(5)];
    var row0_0 : vec4<f32> = uPages_0[_S52 + i32(6)];
    var row1_0 : vec4<f32> = uPages_0[_S52 + i32(7)];
    var row2_0 : vec4<f32> = uPages_0[_S52 + i32(8)];
    var delta_0 : vec3<f32> = world_3 - vec3<f32>(row0_0.w, row1_0.w, row2_0.w);
    var _S53 : vec3<f32> = row0_0.xyz;
    var _S54 : vec3<f32> = row1_0.xyz;
    var _S55 : vec3<f32> = row2_0.xyz;
    var local_0 : vec3<f32> = vec3<f32>(dot(_S53, delta_0), dot(_S54, delta_0), dot(_S55, delta_0));
    (*directionLocal_0) = vec3<f32>(dot(_S53, directionW_0), dot(_S54, directionW_0), dot(_S55, directionW_0));
    var relative_9 : vec3<f32> = local_0 - centreAndHalfWidth_0.xyz;
    var _S56 : vec2<f32> = max(vec2<f32>(centreAndHalfWidth_0.w, rightAndHalfHeight_0.w), vec2<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f));
    (*halfSize_0) = _S56;
    (*across_0) = vec2<f32>(dot(relative_9, rightAndHalfHeight_0.xyz), dot(relative_9, upAndDepth_0.xyz)) / _S56;
    var _S57 : vec3<f32> = direction_2.xyz;
    (*behind_0) = - dot(relative_9, _S57);
    (*depth_5) = upAndDepth_0.w;
    (*facing_0) = _S57;
    return;
}

fn LookupCardWithin_0( world_4 : vec3<f32>,  arrivingW_0 : vec3<f32>,  skin_0 : f32,  found_0 : ptr<function, vec2<f32>>,  litFound_0 : ptr<function, vec2<f32>>,  agreement_0 : ptr<function, f32>,  cardDepth_0 : ptr<function, f32>) -> bool
{
    (*cardDepth_0) = 0.0f;
    const _S58 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    (*found_0) = _S58;
    (*litFound_0) = _S58;
    (*agreement_0) = 1.0e+09f;
    if(i32(p_0.cacheGrid_0.w) <= i32(0))
    {
        return false;
    }
    var _S59 : vec2<i32> = uCells_0[LookupBucket_0(vec3<i32>(floor((world_4 - p_0.cacheGrid_0.xyz) / vec3<f32>(p_0.cacheOrigin_0.w))))];
    var albedoWidth_0 : u32;
    var albedoHeight_0 : u32;
    {var dim = textureDimensions((uAlbedo_0));((albedoWidth_0)) = dim.x;((albedoHeight_0)) = dim.y;};
    var _S60 : vec2<f32> = vec2<f32>(f32(albedoWidth_0), f32(albedoHeight_0));
    var _S61 : vec2<i32> = uCells_0[i32(p_0.cacheGrid_0.w)];
    var settled_0 : bool = false;
    var anyFound_0 : bool = false;
    var walk_0 : i32 = i32(0);
    for(;;)
    {
        var _S62 : bool;
        if(walk_0 <= (_S61.y))
        {
            _S62 = !settled_0;
        }
        else
        {
            _S62 = false;
        }
        if(_S62)
        {
        }
        else
        {
            break;
        }
        var _S63 : bool = walk_0 == i32(0);
        var _S64 : vec2<i32>;
        if(_S63)
        {
            _S64 = _S59;
        }
        else
        {
            _S64 = MoverPages_0(_S61.x, walk_0 - i32(1), world_4, 0.0f);
        }
        var settled_1 : bool = settled_0;
        var anyFound_1 : bool = anyFound_0;
        var slot_0 : i32 = i32(0);
        for(;;)
        {
            var _S65 : bool;
            if(slot_0 < (_S64.y))
            {
                _S65 = !settled_1;
            }
            else
            {
                _S65 = false;
            }
            if(_S65)
            {
            }
            else
            {
                break;
            }
            var page_1 : i32 = uCellPages_0[_S64.x + slot_0];
            var _S66 : i32 = page_1 * i32(10);
            if(((uPages_0[_S66 + i32(5)].w) > 0.5f) == _S63)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var bound_0 : vec4<f32> = uPages_0[_S66 + i32(9)];
            var toCentre_1 : vec3<f32> = world_4 - bound_0.xyz;
            var _S67 : f32 = bound_0.w;
            if((dot(toCentre_1, toCentre_1)) > (_S67 * _S67))
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var across_1 : vec2<f32>;
            var halfSize_1 : vec2<f32>;
            var behind_1 : f32;
            var depth_6 : f32;
            var facing_1 : vec3<f32>;
            var arrivingLocal_0 : vec3<f32>;
            var litRect_1 : vec4<f32>;
            var cardPage_1 : vec4<f32>;
            ProjectOntoCard_0(page_1, world_4, (vec3<f32>(0) - arrivingW_0), &(across_1), &(halfSize_1), &(behind_1), &(depth_6), &(facing_1), &(arrivingLocal_0), &(litRect_1), &(cardPage_1));
            var _S68 : bool;
            if((abs(across_1.x)) > 1.0f)
            {
                _S68 = true;
            }
            else
            {
                _S68 = (abs(across_1.y)) > 1.0f;
            }
            if(_S68)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S69 : bool;
            if((dot(arrivingW_0, arrivingW_0)) > 0.5f)
            {
                _S69 = (dot(facing_1, arrivingLocal_0)) < -0.05000000074505806f;
            }
            else
            {
                _S69 = false;
            }
            if(_S69)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S70 : bool;
            if(behind_1 < (- skin_0))
            {
                _S70 = true;
            }
            else
            {
                _S70 = behind_1 > (depth_6 + skin_0);
            }
            if(_S70)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var _S71 : vec2<f32> = vec2<f32>(0.5f);
            var uv_8 : vec2<f32> = across_1 * _S71 + _S71;
            var texel_2 : vec2<f32> = (cardPage_1.xy + uv_8 * cardPage_1.zw) / _S60;
            if(((textureSampleLevel((uAlbedo_0), (uAlbedoSampler_0), (texel_2), (0.0f))).w) < 0.5f)
            {
                slot_0 = slot_0 + i32(1);
                continue;
            }
            var error_0 : f32 = abs((textureSampleLevel((uDepth_0), (uDepthSampler_0), (texel_2), (0.0f))).x * depth_6 - behind_1);
            var settled_2 : bool;
            var anyFound_2 : bool;
            if(error_0 < (*agreement_0))
            {
                (*agreement_0) = error_0;
                (*cardDepth_0) = depth_6;
                (*found_0) = texel_2;
                var _S72 : bool = error_0 < 0.01999999955296516f;
                (*litFound_0) = (litRect_1.xy + uv_8 * litRect_1.zw) / vec3<f32>(p_0.cacheAtlas_0.xy, 0.0f).xy;
                settled_2 = _S72;
                anyFound_2 = true;
            }
            else
            {
                settled_2 = settled_1;
                anyFound_2 = anyFound_1;
            }
            settled_1 = settled_2;
            anyFound_1 = anyFound_2;
            slot_0 = slot_0 + i32(1);
        }
        var _S73 : i32 = walk_0 + i32(1);
        settled_0 = settled_1;
        anyFound_0 = anyFound_1;
        walk_0 = _S73;
    }
    return anyFound_0;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var resolved_3 : bool;
    var hitEdge_0 : f32;
    var reflection_0 : vec3<f32>;
    var replaced_0 : vec3<f32>;
    var hitUv_0 : vec2<f32>;
    var _S74 : vec2<i32> = vec2<i32>(id_0.xy);
    var marches_0 : bool;
    if((_S74.x) >= i32(p_0.resolution_0.x))
    {
        marches_0 = true;
    }
    else
    {
        marches_0 = (_S74.y) >= i32(p_0.resolution_0.y);
    }
    if(marches_0)
    {
        return;
    }
    var _S75 : vec2<f32> = vec2<f32>(_S74);
    var uv_9 : vec2<f32> = (_S75 + vec2<f32>(0.5f)) * p_0.resolution_0.zw;
    const reflection_1 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var depth_7 : f32 = DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, uv_9);
    var material_1 : vec4<f32> = (textureSampleLevel((uMaterial_0), (uMaterialSampler_0), (uv_9), (0.0f)));
    var model_0 : i32 = (i32(material_1.w * 255.0f + 0.5f) & (i32(15)));
    if(depth_7 <= 0.0f)
    {
        marches_0 = true;
    }
    else
    {
        marches_0 = model_0 == i32(0);
    }
    if(marches_0)
    {
        marches_0 = true;
    }
    else
    {
        marches_0 = depth_7 >= 0.89999997615814209f;
    }
    if(marches_0)
    {
        marches_0 = true;
    }
    else
    {
        marches_0 = DecodeMatte_0(material_1);
    }
    if(marches_0)
    {
        textureStore((uReflection_0), (vec2<u32>(_S74)), (vec4<f32>(reflection_1, 0.0f)));
        return;
    }
    var traces_0 : bool = (p_0.trace_0.x) > 0.5f;
    var _S76 : bool = !traces_0;
    if(_S76)
    {
        marches_0 = (p_0.field_0.x) > 0.5f;
    }
    else
    {
        marches_0 = false;
    }
    var _S77 : vec3<f32> = vec3<f32>(1.0f);
    var groundNormal_0 : vec3<f32> = normalize((textureSampleLevel((uNormal_0), (uNormalSampler_0), (uv_9), (0.0f))).xyz * vec3<f32>(2.0f) - _S77);
    var metallic_0 : f32 = material_1.x;
    var roughness_1 : f32 = material_1.z;
    var viewPos_0 : vec3<f32> = ViewPositionAt_0(uv_9, depth_7, p_0.unproject_0);
    var _S78 : mat4x4<f32> = mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)]);
    var worldRayToPixel_0 : vec3<f32> = normalize((((viewPos_0) * (mat3x3<f32>(_S78[i32(0)].xyz, _S78[i32(1)].xyz, _S78[i32(2)].xyz)))));
    var _S79 : vec3<f32> = (vec3<f32>(0) - worldRayToPixel_0);
    var _S80 : f32 = max(dot(groundNormal_0, _S79), 0.00100000004749745f);
    var _S81 : mat4x4<f32> = mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)]);
    var relative_10 : vec3<f32> = (((viewPos_0) * (mat3x3<f32>(_S81[i32(0)].xyz, _S81[i32(1)].xyz, _S81[i32(2)].xyz))));
    var w_9 : WetParams_0 = Weather_0();
    var screenAllowed_0 : bool;
    if((w_9.amount_0) > 0.0f)
    {
        screenAllowed_0 = (groundNormal_0.z) > 0.86000001430511475f;
    }
    else
    {
        screenAllowed_0 = false;
    }
    if(screenAllowed_0)
    {
        screenAllowed_0 = !UnderShelter_0(relative_10);
    }
    else
    {
        screenAllowed_0 = false;
    }
    var puddle_0 : f32;
    if(screenAllowed_0)
    {
        puddle_0 = PuddleMask_0(w_9, relative_10, groundNormal_0);
    }
    else
    {
        puddle_0 = 0.0f;
    }
    var water_0 : bool = puddle_0 > 0.00999999977648258f;
    var previousDiff_0 : f32;
    var t_1 : f32;
    var reflectance_0 : vec3<f32>;
    var normalW_0 : vec3<f32>;
    if(water_0)
    {
        var surface_1 : WaterSurface_0 = WaterSurfaceAt_0(w_9, relative_10, WetFootprint_0(2.0f * p_0.resolution_0.z, viewPos_0.z, p_0.unproject_0.x, _S80));
        var normalW_1 : vec3<f32> = normalize(mix(groundNormal_0, surface_1.normal_2, vec3<f32>(puddle_0)));
        var share_0 : f32 = WaterFresnel_0(normalW_1, _S79) * puddle_0 * surface_1.mirror_0;
        reflectance_0 = vec3<f32>(share_0, share_0, share_0);
        normalW_0 = normalW_1;
        screenAllowed_0 = true;
    }
    else
    {
        if(traces_0)
        {
            screenAllowed_0 = true;
        }
        else
        {
            screenAllowed_0 = marches_0;
        }
        if(screenAllowed_0)
        {
            previousDiff_0 = p_0.trace2_0.x;
        }
        else
        {
            previousDiff_0 = p_0.ssr_0.z;
        }
        var _S82 : bool = roughness_1 <= (p_0.ssr_0.z);
        if(roughness_1 > previousDiff_0)
        {
            textureStore((uReflection_0), (vec2<u32>(_S74)), (vec4<f32>(reflection_1, 0.0f)));
            return;
        }
        var f0_0 : vec3<f32> = mix(vec3<f32>(0.03999999910593033f, 0.03999999910593033f, 0.03999999910593033f), (textureSampleLevel((uBaseColour_0), (uBaseColourSampler_0), (uv_9), (0.0f))).xyz, vec3<f32>(metallic_0));
        var fresnel_0 : vec3<f32> = f0_0 + (_S77 - f0_0) * vec3<f32>(pow(1.0f - _S80, 5.0f));
        if(traces_0)
        {
            screenAllowed_0 = true;
        }
        else
        {
            screenAllowed_0 = marches_0;
        }
        if(screenAllowed_0)
        {
            t_1 = clamp((previousDiff_0 - roughness_1) / max(previousDiff_0 * 0.25f, 0.00100000004749745f), 0.0f, 1.0f);
        }
        else
        {
            t_1 = clamp((previousDiff_0 - roughness_1) / max(previousDiff_0, 0.00100000004749745f), 0.0f, 1.0f);
        }
        reflectance_0 = fresnel_0 * vec3<f32>(t_1);
        normalW_0 = groundNormal_0;
        screenAllowed_0 = _S82;
    }
    if((max(reflectance_0.x, max(reflectance_0.y, reflectance_0.z))) <= 0.00300000002607703f)
    {
        textureStore((uReflection_0), (vec2<u32>(_S74)), (vec4<f32>(reflection_1, 0.0f)));
        return;
    }
    var _S83 : mat4x4<f32> = mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)]);
    var rayView_0 : vec3<f32> = reflect(normalize(viewPos_0), normalize((((normalW_0) * (transpose(mat3x3<f32>(_S83[i32(0)].xyz, _S83[i32(1)].xyz, _S83[i32(2)].xyz)))))));
    var have_0 : bool;
    if(_S76)
    {
        have_0 = (rayView_0.z) > 0.0f;
    }
    else
    {
        have_0 = false;
    }
    if(have_0)
    {
    }
    else
    {
        screenAllowed_0 = false;
    }
    var _S84 : i32 = max(i32(p_0.ssr_0.x + 0.5f), i32(1));
    var _S85 : f32 = p_0.misc_0.z;
    var _S86 : f32 = 0.20000000298023224f * (0.5f + Dither_0(_S75, p_0.misc_0.y));
    const _S87 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    have_0 = false;
    previousDiff_0 = -1.0f;
    var previous_1 : vec3<f32> = viewPos_0;
    var i_1 : i32 = i32(0);
    t_1 = _S86;
    for(;;)
    {
        if(screenAllowed_0)
        {
            resolved_3 = i_1 < _S84;
        }
        else
        {
            resolved_3 = false;
        }
        if(resolved_3)
        {
        }
        else
        {
            hitEdge_0 = 0.0f;
            hitUv_0 = _S87;
            break;
        }
        var pv_0 : vec3<f32> = viewPos_0 + rayView_0 * vec3<f32>(t_1);
        var _S88 : f32 = pv_0.z;
        var _S89 : bool;
        if(_S88 <= (p_0.unproject_0.z))
        {
            _S89 = true;
        }
        else
        {
            _S89 = t_1 > _S85;
        }
        if(_S89)
        {
            hitEdge_0 = 0.0f;
            hitUv_0 = _S87;
            break;
        }
        var t_2 : f32 = t_1 + max(0.20000000298023224f, t_1 * 0.07999999821186066f);
        var huv_0 : vec2<f32> = ViewToUv_0(pv_0);
        var _S90 : bool;
        if((any((huv_0 < _S87))))
        {
            _S90 = true;
        }
        else
        {
            _S90 = (any((huv_0 > vec2<f32>(1.0f, 1.0f))));
        }
        if(_S90)
        {
            hitEdge_0 = 0.0f;
            hitUv_0 = _S87;
            break;
        }
        var d_0 : f32 = DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, huv_0);
        if(d_0 <= 0.0f)
        {
            previousDiff_0 = -1.0f;
            var _S91 : i32 = i_1 + i32(1);
            have_0 = true;
            previous_1 = pv_0;
            i_1 = _S91;
            t_1 = t_2;
            continue;
        }
        var diff_0 : f32 = _S88 - SceneViewZ_0(d_0);
        if(diff_0 > 0.0f)
        {
            var _S92 : f32 = max(p_0.ssr_0.w, _S88 * 0.05000000074505806f);
            if(have_0)
            {
                resolved_3 = previousDiff_0 < 0.0f;
            }
            else
            {
                resolved_3 = false;
            }
            if(resolved_3)
            {
                screenAllowed_0 = true;
            }
            else
            {
                screenAllowed_0 = diff_0 < _S92;
            }
            if(screenAllowed_0)
            {
                reflection_0 = previous_1;
                replaced_0 = pv_0;
                var k_2 : i32 = i32(0);
                for(;;)
                {
                    if(k_2 < i32(5))
                    {
                    }
                    else
                    {
                        break;
                    }
                    var mid_0 : vec3<f32> = (reflection_0 + replaced_0) * vec3<f32>(0.5f);
                    var md_0 : f32 = DepthBilinear_0(uSceneDepth_0, uSceneDepthSampler_0, ViewToUv_0(mid_0));
                    if(md_0 > 0.0f)
                    {
                        screenAllowed_0 = (mid_0.z - SceneViewZ_0(md_0)) > 0.0f;
                    }
                    else
                    {
                        screenAllowed_0 = false;
                    }
                    if(screenAllowed_0)
                    {
                        replaced_0 = mid_0;
                    }
                    else
                    {
                        reflection_0 = mid_0;
                    }
                    k_2 = k_2 + i32(1);
                }
                var hitUv_1 : vec2<f32> = ViewToUv_0(replaced_0);
                var vig_0 : vec2<f32> = clamp(abs(hitUv_1 * vec2<f32>(2.0f) - vec2<f32>(1.0f)) * vec2<f32>(5.0f) - vec2<f32>(4.0f), _S87, vec2<f32>(1.0f, 1.0f));
                hitEdge_0 = clamp(1.0f - dot(vig_0, vig_0), 0.0f, 1.0f);
                hitUv_0 = hitUv_1;
            }
            else
            {
                hitEdge_0 = 0.0f;
                hitUv_0 = _S87;
            }
            break;
        }
        previousDiff_0 = diff_0;
        var _S91 : i32 = i_1 + i32(1);
        have_0 = true;
        previous_1 = pv_0;
        i_1 = _S91;
        t_1 = t_2;
    }
    var screenWeight_0 : f32;
    if(hitEdge_0 > 0.0f)
    {
        var hit_1 : vec4<f32> = (textureSampleLevel((uLit_0), (uLitSampler_0), (hitUv_0), (0.0f)));
        var world_5 : f32 = step(0.5f, hit_1.w);
        var _S93 : vec3<f32> = max(hit_1.xyz, reflection_1);
        if(water_0)
        {
            puddle_0 = 1.0f;
        }
        else
        {
            puddle_0 = p_0.ssr_0.y;
        }
        var scene_0 : vec3<f32> = _S93 * vec3<f32>(puddle_0);
        if(water_0)
        {
            var reflected_1 : vec3<f32> = normalize(reflect(worldRayToPixel_0, normalW_0));
            reflection_0 = SampleSkyView_0(uSkyView_0, uSkyViewSampler_0, reflected_1, p_0.misc_0.w) * vec3<f32>(WaterSkyward_0(reflected_1));
        }
        else
        {
            reflection_0 = reflection_1;
        }
        var screenWeight_1 : f32 = hitEdge_0 * world_5;
        var _S94 : vec3<f32> = (scene_0 - reflection_0) * reflectance_0 * vec3<f32>(screenWeight_1);
        screenWeight_0 = screenWeight_1;
        reflection_0 = _S94;
    }
    else
    {
        screenWeight_0 = 0.0f;
        reflection_0 = reflection_1;
    }
    if(marches_0)
    {
        screenAllowed_0 = screenWeight_0 < 0.99900001287460327f;
    }
    else
    {
        screenAllowed_0 = false;
    }
    if(screenAllowed_0)
    {
        var rayW_1 : vec3<f32> = normalize(reflect(worldRayToPixel_0, normalW_0));
        var lostCeiling_0 : bool = LosesCeiling_0(Roofs_0(), uShelterMap_0, relative_10.xy + p_0.wet1_0.zw, relative_10.z + p_0.wet2_0.y, rayW_1);
        var hitRelative_1 : vec3<f32>;
        var voxel_3 : f32;
        var struck_0 : bool = SurfaceAt_0(relative_10 + normalW_0 * vec3<f32>((0.01999999955296516f + 0.00050000002374873f * viewPos_0.z)), normalW_0, rayW_1, p_0.trace_0.y, &(hitRelative_1), &(voxel_3));
        var radiance_1 : vec3<f32> = reflection_1;
        if(!struck_0)
        {
            var sky_0 : vec3<f32> = SampleSkyView_0(uSkyView_0, uSkyViewSampler_0, rayW_1, p_0.misc_0.w);
            var grey_0 : f32 = dot(sky_0, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f)) * 0.30000001192092896f;
            if(lostCeiling_0)
            {
                normalW_0 = vec3<f32>(grey_0, grey_0, grey_0);
            }
            else
            {
                normalW_0 = sky_0;
            }
            radiance_1 = normalW_0;
        }
        else
        {
            var resolved_4 : bool = LitAtHitWithin_0(hitRelative_1, voxel_3, &(radiance_1));
            if(!resolved_4)
            {
                var tolerance_0 : f32 = 0.25f + voxel_3 * 2.0f;
                var found_1 : vec2<f32>;
                var litFound_1 : vec2<f32>;
                var agreement_1 : f32;
                var cardDepth_1 : f32;
                var _S95 : bool = LookupCardWithin_0(hitRelative_1 + p_0.cacheOrigin_0.xyz, reflection_1, tolerance_0, &(found_1), &(litFound_1), &(agreement_1), &(cardDepth_1));
                if(_S95)
                {
                    screenAllowed_0 = agreement_1 <= (tolerance_0 + cardDepth_1 * 0.5f);
                }
                else
                {
                    screenAllowed_0 = false;
                }
                if(screenAllowed_0)
                {
                    radiance_1 = max((textureSampleLevel((uFinal_0), (uFinalSampler_0), (litFound_1), (0.0f))).xyz, reflection_1) * vec3<f32>(p_0.trace_0.w);
                    resolved_3 = true;
                }
                else
                {
                    resolved_3 = resolved_4;
                }
            }
            else
            {
                resolved_3 = resolved_4;
            }
            if(!resolved_3)
            {
                var sky_1 : vec3<f32> = SampleSkyView_0(uSkyView_0, uSkyViewSampler_0, rayW_1, p_0.misc_0.w);
                var grey_1 : f32 = dot(sky_1, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f)) * 0.30000001192092896f;
                if(lostCeiling_0)
                {
                    normalW_0 = vec3<f32>(grey_1, grey_1, grey_1);
                }
                else
                {
                    normalW_0 = sky_1;
                }
                radiance_1 = normalW_0;
            }
        }
        var _S96 : f32 = max(radiance_1.x, max(radiance_1.y, radiance_1.z));
        if(_S96 > (p_0.trace_0.z))
        {
            radiance_1 = radiance_1 * vec3<f32>((p_0.trace_0.z / _S96));
        }
        var _S97 : vec3<f32> = radiance_1;
        if(water_0)
        {
            puddle_0 = 1.0f;
        }
        else
        {
            puddle_0 = p_0.ssr_0.y;
        }
        var scene_1 : vec3<f32> = _S97 * vec3<f32>(puddle_0);
        if(water_0)
        {
            replaced_0 = SampleSkyView_0(uSkyView_0, uSkyViewSampler_0, rayW_1, p_0.misc_0.w) * vec3<f32>(WaterSkyward_0(rayW_1));
        }
        else
        {
            replaced_0 = reflection_1;
        }
        var reflection_2 : vec3<f32> = reflection_0 + (scene_1 - replaced_0) * reflectance_0 * vec3<f32>((1.0f - screenWeight_0));
        screenWeight_0 = 1.0f;
        reflection_0 = reflection_2;
    }
    var _S98 : vec2<u32> = vec2<u32>(_S74);
    if(traces_0)
    {
        marches_0 = true;
    }
    if(marches_0)
    {
        puddle_0 = 1.0f;
    }
    else
    {
        puddle_0 = screenWeight_0;
    }
    textureStore((uReflection_0), (_S98), (vec4<f32>(reflection_0, puddle_0)));
    return;
}

