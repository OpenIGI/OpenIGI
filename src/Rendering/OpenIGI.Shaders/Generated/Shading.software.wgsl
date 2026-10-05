@binding(0) @group(0) var uBaseColour_0 : texture_2d<f32>;

@binding(2) @group(0) var uMaterial_0 : texture_2d<f32>;

@binding(3) @group(0) var uBaked_0 : texture_2d<f32>;

@binding(4) @group(0) var uDepth_0 : texture_depth_2d;

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

struct Gi_std140_0
{
    @align(16) unproject_0 : vec4<f32>,
    @align(16) probeOrigin_0 : vec4<f32>,
    @align(16) probeInvCount_0 : vec4<f32>,
    @align(16) probeMisc_0 : vec4<f32>,
    @align(16) probeSky_0 : vec4<f32>,
    @align(16) camera_0 : vec4<f32>,
    @align(16) giParams_0 : vec4<f32>,
    @align(16) viewToWorld_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) cascadeSplits_0 : vec4<f32>,
    @align(16) shadowConfig_0 : vec4<f32>,
    @align(16) cascadeMatrix_0 : _Array_std140_matrixx3Cfloatx2C4x2C4x3E4_0,
    @align(16) wetness0_0 : vec4<f32>,
    @align(16) wetness1_0 : vec4<f32>,
    @align(16) shelterMap0_0 : vec4<f32>,
    @align(16) shelterMap1_0 : vec4<f32>,
    @align(16) shelterMap2_0 : vec4<f32>,
    @align(16) fog_0 : vec4<f32>,
    @align(16) flashlight_0 : FlashlightParams_std140_0,
};

@binding(13) @group(0) var<uniform> gi_0 : Gi_std140_0;
struct Light_std140_0
{
    @align(16) sunDirectionModel_0 : vec4<f32>,
    @align(16) sunColourScale_0 : vec4<f32>,
    @align(16) ambient_0 : vec4<f32>,
    @align(16) sky_0 : vec4<f32>,
    @align(16) moon_0 : vec4<f32>,
    @align(16) stars_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> light_0 : Light_std140_0;
@binding(32) @group(0) var uFogVolume_0 : texture_3d<f32>;

@binding(33) @group(0) var uFogVolumeSampler_0 : sampler;

@binding(20) @group(0) var uSkyView_0 : texture_2d<f32>;

@binding(21) @group(0) var uSkyViewSampler_0 : sampler;

@binding(22) @group(0) var uTransmittanceLut_0 : texture_2d<f32>;

@binding(23) @group(0) var uTransmittanceLutSampler_0 : sampler;

@binding(1) @group(0) var uNormal_0 : texture_2d<f32>;

@binding(18) @group(0) var uOcclusion_0 : texture_2d<f32>;

@binding(19) @group(0) var uOcclusionSampler_0 : sampler;

@binding(14) @group(0) var uScreenGi_0 : texture_2d<f32>;

@binding(15) @group(0) var uScreenGiSampler_0 : sampler;

@binding(36) @group(0) var uFlashlightShadow_0 : texture_2d<f32>;

@binding(26) @group(0) var uSunShadow_0 : texture_2d<f32>;

@binding(27) @group(0) var uSunShadowSampler_0 : sampler;

@binding(16) @group(0) var uShadowMap_0 : texture_depth_2d_array;

@binding(17) @group(0) var uShadowMapSampler_0 : sampler_comparison;

@binding(34) @group(0) var uShelterMap_0 : texture_2d<f32>;

struct Lamps_std140_0
{
    @align(16) control_1 : vec4<f32>,
    @align(16) source_0 : vec4<f32>,
    @align(16) positionRadius_0 : array<vec4<f32>, i32(160)>,
    @align(16) colourIntensity_0 : array<vec4<f32>, i32(160)>,
    @align(16) direction_0 : array<vec4<f32>, i32(160)>,
    @align(16) rect_0 : array<vec4<f32>, i32(160)>,
};

@binding(28) @group(0) var<uniform> lamps_0 : Lamps_std140_0;
@binding(29) @group(0) var uLampVisibility_0 : texture_2d<f32>;

@binding(5) @group(0) var uProbeRed_0 : texture_3d<f32>;

@binding(6) @group(0) var uProbeRedSampler_0 : sampler;

@binding(7) @group(0) var uProbeGreen_0 : texture_3d<f32>;

@binding(8) @group(0) var uProbeGreenSampler_0 : sampler;

@binding(9) @group(0) var uProbeBlue_0 : texture_3d<f32>;

@binding(10) @group(0) var uProbeBlueSampler_0 : sampler;

@binding(11) @group(0) var uProbeDistance_0 : texture_3d<f32>;

@binding(12) @group(0) var uProbeDistanceSampler_0 : sampler;

@binding(24) @group(0) var uSkyIrradiance_0 : texture_2d<f32>;

fn rsqrt_0( x_0 : f32) -> f32
{
    return 1.0f / sqrt(x_0);
}

var<private> kRippleOffset_0 : array<vec2<f32>, i32(4)> = array<vec2<f32>, i32(4)>( vec2<f32>(0.0f, 0.0f), vec2<f32>(0.43000000715255737f, 0.70999997854232788f), vec2<f32>(0.79000002145767212f, 0.23999999463558197f), vec2<f32>(0.17000000178813934f, 0.46000000834465027f) );
var<private> kShadowDisc_0 : array<vec2<f32>, i32(12)> = array<vec2<f32>, i32(12)>( vec2<f32>(-0.32600000500679016f, -0.40599998831748962f), vec2<f32>(-0.8399999737739563f, -0.07400000095367432f), vec2<f32>(-0.69599997997283936f, 0.45699998736381531f), vec2<f32>(-0.20299999415874481f, 0.62099999189376831f), vec2<f32>(0.96200001239776611f, -0.19499999284744263f), vec2<f32>(0.47299998998641968f, -0.47999998927116394f), vec2<f32>(0.51899999380111694f, 0.76700001955032349f), vec2<f32>(0.18500000238418579f, -0.89300000667572021f), vec2<f32>(0.50700002908706665f, 0.06400000303983688f), vec2<f32>(0.89600002765655518f, 0.41200000047683716f), vec2<f32>(-0.32199999690055847f, -0.93300002813339233f), vec2<f32>(-0.79199999570846558f, -0.59799998998641968f) );
@id(64) override cClipYDown_0 : i32 = i32(1);

fn OpenIgiClipY_0( y_0 : f32) -> f32
{
    var _S1 : f32;
    if(cClipYDown_0 != i32(0))
    {
        _S1 = - y_0;
    }
    else
    {
        _S1 = y_0;
    }
    return _S1;
}

struct FullscreenVaryings_0
{
    @builtin(position) position_1 : vec4<f32>,
    @location(0) uv_0 : vec2<f32>,
};

fn FullscreenTriangle_0( vertexIndex_0 : u32) -> FullscreenVaryings_0
{
    var output_0 : FullscreenVaryings_0;
    var _S2 : f32 = f32((((vertexIndex_0 << (u32(1)))) & (u32(2))));
    var _S3 : f32 = f32((vertexIndex_0 & (u32(2))));
    output_0.uv_0 = vec2<f32>(_S2, _S3);
    output_0.position_1 = vec4<f32>(_S2 * 2.0f - 1.0f, OpenIgiClipY_0(1.0f - _S3 * 2.0f), 0.0f, 1.0f);
    return output_0;
}

@vertex
fn vertexMain(@builtin(vertex_index) index_0 : u32) -> FullscreenVaryings_0
{
    return FullscreenTriangle_0(index_0);
}

fn LoadGBuffer_0( image_0 : texture_2d<f32>,  position_2 : vec4<f32>) -> vec4<f32>
{
    var _S4 : vec3<i32> = vec3<i32>(vec2<i32>(position_2.xy), i32(0));
    return (textureLoad((image_0), ((_S4)).xy, ((_S4)).z));
}

fn LoadGBuffer_1( image_1 : texture_depth_2d,  position_3 : vec4<f32>) -> vec4<f32>
{
    var _S5 : vec3<i32> = vec3<i32>(vec2<i32>(position_3.xy), i32(0));
    return vec4<f32>((textureLoad((image_1), ((_S5)).xy, ((_S5)).z)), 0.0f, 0.0f, 0.0f);
}

fn DecodeEmissiveStrength_0( material_0 : vec4<f32>) -> f32
{
    var packed_0 : f32 = material_0.y * 255.0f - 128.0f;
    var _S6 : f32;
    if(packed_0 > 0.5f)
    {
        _S6 = packed_0 / 16.0f;
    }
    else
    {
        _S6 = 1.0f;
    }
    return _S6;
}

fn ApplyFog_0( colour_1 : vec3<f32>,  uv_1 : vec2<f32>,  viewZ_0 : f32) -> vec3<f32>
{
    if((gi_0.fog_0.z) <= 0.0f)
    {
        return colour_1;
    }
    var fog_1 : vec4<f32> = (textureSampleLevel((uFogVolume_0), (uFogVolumeSampler_0), (vec3<f32>(uv_1, clamp(log2(max(viewZ_0 * gi_0.fog_0.x + gi_0.fog_0.y, 9.99999997475242708e-07f)) * gi_0.fog_0.z * gi_0.fog_0.w, 0.0f, 1.0f))), (0.0f)));
    return colour_1 * vec3<f32>(fog_1.w) + fog_1.xyz;
}

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

fn SkyViewHeldAboveHorizon_0( uv_2 : vec2<f32>,  rows_0 : f32) -> vec2<f32>
{
    var _S7 : vec2<f32> = uv_2;
    _S7[i32(1)] = min(uv_2.y, (floor(rows_0 * 0.5f) - 1.0f) / max(rows_0 - 1.0f, 1.0f));
    return _S7;
}

fn SkyViewInset_0( uv_3 : vec2<f32>,  size_0 : vec2<f32>) -> vec2<f32>
{
    return uv_3 * ((size_0 - vec2<f32>(1.0f)) / size_0) + vec2<f32>(0.5f) / size_0;
}

fn SampleSkyViewUv_0( skyView_0 : texture_2d<f32>,  skyViewSampler_0 : sampler,  uv_4 : vec2<f32>) -> vec3<f32>
{
    var width_0 : u32;
    var height_0 : u32;
    {var dim = textureDimensions((skyView_0));((width_0)) = dim.x;((height_0)) = dim.y;};
    return (textureSampleLevel((skyView_0), (skyViewSampler_0), (SkyViewInset_0(uv_4, vec2<f32>(f32(width_0), f32(height_0)))), (0.0f))).xyz;
}

fn SampleSkyBackground_0( skyView_1 : texture_2d<f32>,  skyViewSampler_1 : sampler,  worldDir_1 : vec3<f32>,  viewHeight_1 : f32) -> vec3<f32>
{
    var width_1 : u32;
    var height_1 : u32;
    {var dim = textureDimensions((skyView_1));((width_1)) = dim.x;((height_1)) = dim.y;};
    return SampleSkyViewUv_0(skyView_1, skyViewSampler_1, SkyViewHeldAboveHorizon_0(SkyViewUv_0(worldDir_1, viewHeight_1), f32(height_1)));
}

fn RayIntersectSphere_0( origin_0 : vec3<f32>,  direction_1 : vec3<f32>,  radius_0 : f32) -> vec2<f32>
{
    var a_0 : f32 = dot(direction_1, direction_1);
    var b_0 : f32 = 2.0f * dot(origin_0, direction_1);
    var discriminant_0 : f32 = b_0 * b_0 - 4.0f * a_0 * (dot(origin_0, origin_0) - radius_0 * radius_0);
    if(discriminant_0 < 0.0f)
    {
        return vec2<f32>(-1.0f, -1.0f);
    }
    var root_0 : f32 = sqrt(discriminant_0);
    var _S8 : f32 = - b_0;
    var _S9 : f32 = 2.0f * a_0;
    return vec2<f32>((_S8 - root_0) / _S9, (_S8 + root_0) / _S9);
}

fn TransmittanceParamsToUv_0( viewHeight_2 : f32,  viewZenithCos_0 : f32) -> vec2<f32>
{
    var horizon_0 : f32 = sqrt(7.668e+05f);
    var _S10 : f32 = viewHeight_2 * viewHeight_2;
    var rho_0 : f32 = sqrt(max(0.0f, _S10 - 4.04496e+07f));
    var dMin_0 : f32 = 6420.0f - viewHeight_2;
    return vec2<f32>((max(0.0f, - viewHeight_2 * viewZenithCos_0 + sqrt(max(0.0f, _S10 * (viewZenithCos_0 * viewZenithCos_0 - 1.0f) + 4.12164e+07f))) - dMin_0) / (rho_0 + horizon_0 - dMin_0), rho_0 / horizon_0);
}

fn DecodeTransmittance_0( t_0 : vec3<f32>) -> vec3<f32>
{
    return t_0 * t_0;
}

fn SunTransmittance_0( sunDir_0 : vec3<f32>,  viewHeightKm_0 : f32) -> vec3<f32>
{
    var ground_0 : vec2<f32> = RayIntersectSphere_0(vec3<f32>(0.0f, 0.0f, viewHeightKm_0), sunDir_0, 6360.0f);
    var _S11 : bool;
    if((ground_0.x) > 0.0f)
    {
        _S11 = true;
    }
    else
    {
        _S11 = (ground_0.y) > 0.0f;
    }
    if(_S11)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    return DecodeTransmittance_0((textureSampleLevel((uTransmittanceLut_0), (uTransmittanceLutSampler_0), (TransmittanceParamsToUv_0(viewHeightKm_0, sunDir_0.z)), (0.0f))).xyz);
}

fn NightCelestial_0( worldDir_2 : vec3<f32>,  moonDir_0 : vec3<f32>) -> vec3<f32>
{
    var meridian_0 : vec3<f32> = moonDir_0 - vec3<f32>(dot(moonDir_0, vec3<f32>(0.0f, 0.62000000476837158f, 0.7850000262260437f))) * vec3<f32>(0.0f, 0.62000000476837158f, 0.7850000262260437f);
    var meridianLength_0 : f32 = length(meridian_0);
    var axisX_0 : vec3<f32>;
    if(meridianLength_0 > 0.00009999999747379f)
    {
        axisX_0 = meridian_0 / vec3<f32>(meridianLength_0);
    }
    else
    {
        axisX_0 = vec3<f32>(1.0f, 0.0f, 0.0f);
    }
    return vec3<f32>(dot(worldDir_2, axisX_0), dot(worldDir_2, cross(vec3<f32>(0.0f, 0.62000000476837158f, 0.7850000262260437f), axisX_0)), dot(worldDir_2, vec3<f32>(0.0f, 0.62000000476837158f, 0.7850000262260437f)));
}

fn NightCubeFace_0( d_0 : vec3<f32>,  uv_5 : ptr<function, vec2<f32>>) -> u32
{
    var a_1 : vec3<f32> = abs(d_0);
    var _S12 : f32 = a_1.x;
    var _S13 : f32 = a_1.y;
    var _S14 : bool;
    if(_S12 >= _S13)
    {
        _S14 = _S12 >= (a_1.z);
    }
    else
    {
        _S14 = false;
    }
    var _S15 : u32;
    if(_S14)
    {
        (*uv_5) = d_0.yz / vec2<f32>(_S12);
        if((d_0.x) > 0.0f)
        {
            _S15 = u32(0);
        }
        else
        {
            _S15 = u32(1);
        }
        return _S15;
    }
    var _S16 : f32 = a_1.z;
    if(_S13 >= _S16)
    {
        (*uv_5) = d_0.xz / vec2<f32>(_S13);
        if((d_0.y) > 0.0f)
        {
            _S15 = u32(2);
        }
        else
        {
            _S15 = u32(3);
        }
        return _S15;
    }
    (*uv_5) = d_0.xy / vec2<f32>(_S16);
    if((d_0.z) > 0.0f)
    {
        _S15 = u32(4);
    }
    else
    {
        _S15 = u32(5);
    }
    return _S15;
}

fn NightPcg3_0( v_1 : vec3<u32>) -> vec3<u32>
{
    var _S17 : vec3<u32> = v_1 * vec3<u32>(u32(1664525)) + vec3<u32>(u32(1013904223));
    var _S18 : vec3<u32> = _S17;
    _S18[i32(0)] = _S18[i32(0)] + _S17.y * _S17.z;
    _S18[i32(1)] = _S18[i32(1)] + _S18.z * _S18.x;
    _S18[i32(2)] = _S18[i32(2)] + _S18.x * _S18.y;
    var _S19 : vec3<u32> = (_S18 ^ (((_S18 >> (vec3<u32>(u32(16)))))));
    _S18 = _S19;
    _S18[i32(0)] = _S18[i32(0)] + _S19.y * _S19.z;
    _S18[i32(1)] = _S18[i32(1)] + _S18.z * _S18.x;
    _S18[i32(2)] = _S18[i32(2)] + _S18.x * _S18.y;
    return _S18;
}

fn NightHash3_0( cell_0 : vec3<u32>) -> vec3<f32>
{
    return vec3<f32>(NightPcg3_0(cell_0)) * vec3<f32>(2.32830643653869629e-10f);
}

fn NightCubeDirection_0( face_0 : u32,  uv_6 : vec2<f32>) -> vec3<f32>
{
    var d_1 : vec3<f32>;
    if(face_0 == u32(0))
    {
        d_1 = vec3<f32>(1.0f, uv_6.x, uv_6.y);
    }
    else
    {
        if(face_0 == u32(1))
        {
            d_1 = vec3<f32>(-1.0f, uv_6.x, uv_6.y);
        }
        else
        {
            if(face_0 == u32(2))
            {
                d_1 = vec3<f32>(uv_6.x, 1.0f, uv_6.y);
            }
            else
            {
                if(face_0 == u32(3))
                {
                    d_1 = vec3<f32>(uv_6.x, -1.0f, uv_6.y);
                }
                else
                {
                    if(face_0 == u32(4))
                    {
                        d_1 = vec3<f32>(uv_6.x, uv_6.y, 1.0f);
                    }
                    else
                    {
                        d_1 = vec3<f32>(uv_6.x, uv_6.y, -1.0f);
                    }
                }
            }
        }
    }
    return normalize(d_1);
}

fn NightStarColour_0( t_1 : f32) -> vec3<f32>
{
    if(t_1 < 0.18000000715255737f)
    {
        return vec3<f32>(0.72000002861022949f, 0.81999999284744263f, 1.0f);
    }
    if(t_1 < 0.62000000476837158f)
    {
        return vec3<f32>(0.94999998807907104f, 0.95999997854232788f, 1.0f);
    }
    if(t_1 < 0.86000001430511475f)
    {
        return vec3<f32>(1.0f, 0.92000001668930054f, 0.75999999046325684f);
    }
    return vec3<f32>(1.0f, 0.75999999046325684f, 0.51999998092651367f);
}

fn NightStarLayer_0( celestial_0 : vec3<f32>,  cellsPerFace_0 : f32,  density_0 : f32,  seed_0 : u32,  pixelAngle_0 : f32,  seconds_0 : f32,  scintillation_0 : f32) -> vec3<f32>
{
    var uv_7 : vec2<f32>;
    var face_1 : u32 = NightCubeFace_0(celestial_0, &(uv_7));
    var _S20 : vec2<f32> = vec2<f32>(0.5f);
    var _S21 : vec2<f32> = vec2<f32>(cellsPerFace_0);
    var cell_1 : vec2<f32> = floor((uv_7 * _S20 + _S20) * _S21);
    var key_0 : vec3<u32> = vec3<u32>(u32(cell_1.x), u32(cell_1.y), face_1 * u32(7919) + seed_0);
    var h_0 : vec3<f32> = NightHash3_0(key_0);
    if((h_0.z) > density_0)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var h2_0 : vec3<f32> = NightHash3_0(key_0 + vec3<u32>(u32(101), u32(211), u32(307)));
    var angle_0 : f32 = length(celestial_0 - NightCubeDirection_0(face_1, ((cell_1 + vec2<f32>(0.25f) + _S20 * h_0.xy) / _S21 - _S20) * vec2<f32>(2.0f)));
    var _S22 : f32 = max(pixelAngle_0 * 0.60000002384185791f, 0.00000999999974738f);
    var _S23 : f32 = h2_0.z;
    return NightStarColour_0(_S23) * vec3<f32>(((0.01499999966472387f + 0.98500001430511475f * pow(h2_0.x, 12.0f)) * (1.0f + scintillation_0 * 0.44999998807907104f * sin(seconds_0 * (2.0f + 6.0f * h2_0.y) + _S23 * 60.0f)) * exp(- (angle_0 * angle_0) / (2.0f * _S22 * _S22))));
}

fn NightValueNoise_0( p_0 : vec3<f32>) -> f32
{
    var i_0 : vec3<f32> = floor(p_0);
    var f_0 : vec3<f32> = p_0 - i_0;
    var f_1 : vec3<f32> = f_0 * f_0 * (vec3<f32>(3.0f) - vec3<f32>(2.0f) * f_0);
    var _S24 : vec3<u32> = vec3<u32>(vec3<i32>(i_0) + vec3<i32>(i32(4096), i32(4096), i32(4096)));
    var _S25 : f32 = f_1.x;
    var _S26 : f32 = f_1.y;
    return mix(mix(mix(NightHash3_0(_S24).x, NightHash3_0(_S24 + vec3<u32>(u32(1), u32(0), u32(0))).x, _S25), mix(NightHash3_0(_S24 + vec3<u32>(u32(0), u32(1), u32(0))).x, NightHash3_0(_S24 + vec3<u32>(u32(1), u32(1), u32(0))).x, _S25), _S26), mix(mix(NightHash3_0(_S24 + vec3<u32>(u32(0), u32(0), u32(1))).x, NightHash3_0(_S24 + vec3<u32>(u32(1), u32(0), u32(1))).x, _S25), mix(NightHash3_0(_S24 + vec3<u32>(u32(0), u32(1), u32(1))).x, NightHash3_0(_S24 + vec3<u32>(u32(1), u32(1), u32(1))).x, _S25), _S26), f_1.z);
}

fn NightSkySources_0( worldDir_3 : vec3<f32>,  moonDir_1 : vec3<f32>,  moonRadiance_0 : f32,  starRadiance_0 : f32,  cover_0 : f32,  seconds_1 : f32,  pixelAngle_1 : f32,  transmittance_0 : vec3<f32>) -> vec3<f32>
{
    const sources_0 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var clear_0 : f32 = saturate(1.0f - cover_0);
    var _S27 : bool;
    if(starRadiance_0 > 0.0f)
    {
        _S27 = clear_0 > 0.0f;
    }
    else
    {
        _S27 = false;
    }
    var sources_1 : vec3<f32>;
    if(_S27)
    {
        var celestial_1 : vec3<f32> = NightCelestial_0(worldDir_3, moonDir_1);
        var scintillation_1 : f32 = 0.34999999403953552f + 0.64999997615814209f * saturate(1.0f - worldDir_3.z * 2.5f);
        var bandDistance_0 : f32 = dot(celestial_1, normalize(vec3<f32>(0.46000000834465027f, -0.51999998092651367f, 0.72000002861022949f)));
        sources_1 = (NightStarLayer_0(celestial_1, 55.0f, 0.5f, u32(17), pixelAngle_1, seconds_1, scintillation_1) + vec3<f32>(0.11999999731779099f) * NightStarLayer_0(celestial_1, 150.0f, 0.30000001192092896f, u32(131), pixelAngle_1, seconds_1, scintillation_1) + vec3<f32>((exp(- (bandDistance_0 * bandDistance_0) / 0.02419999986886978f) * saturate((0.55000001192092896f * NightValueNoise_0(celestial_1 * vec3<f32>(9.0f)) + 0.44999998807907104f * NightValueNoise_0(celestial_1 * vec3<f32>(23.0f))) * 1.29999995231628418f - 0.25f) * (1.0f - 0.60000002384185791f * smoothstep(0.34999999403953552f, 0.75f, NightValueNoise_0(celestial_1 * vec3<f32>(14.0f) + vec3<f32>(3.09999990463256836f, 7.69999980926513672f, 1.29999995231628418f)))) * 0.01200000010430813f)) * vec3<f32>(0.85000002384185791f, 0.89999997615814209f, 1.0f)) * vec3<f32>(starRadiance_0) * vec3<f32>((clear_0 * clear_0));
    }
    else
    {
        sources_1 = sources_0;
    }
    if(moonRadiance_0 > 0.0f)
    {
        var cosAngle_0 : f32 = dot(worldDir_3, moonDir_1);
        var angle_1 : f32 = sqrt(max(2.0f - 2.0f * cosAngle_0, 0.0f));
        var _S28 : f32 = max(pixelAngle_1, 0.00052499998128042f);
        var disc_0 : f32 = 1.0f - smoothstep(0.01049999985843897f - _S28, 0.01049999985843897f + _S28, angle_1);
        if(disc_0 > 0.0f)
        {
            var r_0 : f32 = saturate(angle_1 / 0.01049999985843897f);
            sources_1 = sources_1 + vec3<f32>(1.0f, 0.98000001907348633f, 0.93999999761581421f) * vec3<f32>((moonRadiance_0 * disc_0 * (0.62000000476837158f + 0.37999999523162842f * sqrt(max(1.0f - r_0 * r_0, 0.0f))) * (1.0f - 0.2800000011920929f * smoothstep(0.44999998807907104f, 0.80000001192092896f, NightValueNoise_0((worldDir_3 - moonDir_1) / vec3<f32>(0.01049999985843897f) * vec3<f32>(1.60000002384185791f) + vec3<f32>(11.0f, 5.0f, 2.0f)))) * saturate(1.0f - cover_0 * 1.5f)));
        }
        var _S29 : f32 = saturate(cosAngle_0);
        sources_1 = sources_1 + vec3<f32>(0.81999999284744263f, 0.87999999523162842f, 1.0f) * vec3<f32>(moonRadiance_0) * vec3<f32>((pow(_S29, 6000.0f) * 0.00600000005215406f * clear_0 + pow(_S29, 180.0f) * 0.00030000001424924f + pow(_S29, 18.0f) * 0.00060000002849847f * cover_0));
    }
    return sources_1 * transmittance_0;
}

fn DecodeShadingModel_0( material_1 : vec4<f32>) -> i32
{
    return (i32(material_1.w * 255.0f + 0.5f) & (i32(15)));
}

fn ViewPositionOf_0( uv_8 : vec2<f32>,  depth_0 : f32) -> vec3<f32>
{
    var near_0 : f32 = gi_0.unproject_0.z;
    var far_0 : f32 = gi_0.unproject_0.w;
    var viewZ_1 : f32 = 2.0f * near_0 * far_0 / (far_0 + near_0 - (1.0f - 2.0f * depth_0) * (far_0 - near_0));
    var ndc_0 : vec2<f32> = uv_8 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_1 / gi_0.unproject_0.x, - ndc_0.y * viewZ_1 / gi_0.unproject_0.y, viewZ_1);
}

fn DecodeGBufferNormal_0( packed_1 : vec4<f32>) -> vec3<f32>
{
    return normalize(packed_1.xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f));
}

fn FlashlightShadowAt_0( position_4 : vec4<f32>) -> f32
{
    if((gi_0.flashlight_0.control_0.x) <= 0.5f)
    {
        return 1.0f;
    }
    var _S30 : vec3<i32> = vec3<i32>(vec2<i32>(position_4.xy), i32(0));
    return saturate((textureLoad((uFlashlightShadow_0), ((_S30)).xy, ((_S30)).z)).x);
}

fn FlashlightOn_0( f_2 : ptr<function, FlashlightParams_std140_0>) -> bool
{
    return ((*f_2).position_0.w) > 0.0f;
}

fn FlashlightCookie_0( f_3 : ptr<function, FlashlightParams_std140_0>,  direction_2 : vec3<f32>) -> f32
{
    var _S31 : vec3<f32> = (*f_3).axis_0.xyz;
    var c_0 : f32 = dot(_S31, direction_2);
    var _S32 : bool;
    if(c_0 <= ((*f_3).axis_0.w))
    {
        _S32 = true;
    }
    else
    {
        _S32 = c_0 <= 0.0f;
    }
    if(_S32)
    {
        return 0.0f;
    }
    var r_1 : f32 = sqrt(max(1.0f - c_0 * c_0, 0.0f)) / c_0 / max((*f_3).cone_0.x, 0.00009999999747379f);
    var inner_0 : f32 = (*f_3).cone_0.y;
    var hot_0 : f32 = exp(- (r_1 * r_1 / max(0.30000001192092896f * inner_0 * inner_0, 0.00009999999747379f)));
    var body_0 : f32 = 1.0f - smoothstep(inner_0, 1.0f, r_1);
    var _S33 : f32 = r_1 - 0.86000001430511475f;
    var ring_0 : f32 = exp(- (_S33 * _S33 / 0.00400000018998981f));
    var spill_0 : f32 = (1.0f - smoothstep(1.0f, (*f_3).cone_0.z, r_1)) * (*f_3).cone_0.w;
    var side_0 : vec3<f32> = cross((*f_3).up_0.xyz, _S31);
    var sideLength_0 : f32 = length(side_0);
    var across_0 : vec3<f32>;
    if(sideLength_0 > 0.00009999999747379f)
    {
        across_0 = side_0 / vec3<f32>(sideLength_0);
    }
    else
    {
        across_0 = vec3<f32>(1.0f, 0.0f, 0.0f);
    }
    var around_0 : f32 = atan2(dot(direction_2, cross(_S31, across_0)), dot(direction_2, across_0) + 9.99999997475242708e-07f);
    return max((0.55000001192092896f * hot_0 + 0.44999998807907104f * body_0 + 0.07000000029802322f * ring_0) * (1.0f + 0.04500000178813934f * r_1 * sin(5.0f * around_0 + 1.29999995231628418f) + 0.02999999932944775f * sin(33.0f * r_1 + 2.0f * around_0)) + spill_0, 0.0f);
}

fn FlashlightIrradiance_0( f_4 : ptr<function, FlashlightParams_std140_0>,  viewPos_0 : vec3<f32>,  thrown_0 : bool,  toLight_0 : ptr<function, vec3<f32>>,  distance_0 : ptr<function, f32>) -> vec3<f32>
{
    var _S34 : vec4<f32> = (*f_4).position_0;
    var fromLens_0 : vec3<f32> = viewPos_0 - (*f_4).position_0.xyz;
    var distanceSq_0 : f32 = dot(fromLens_0, fromLens_0);
    var _S35 : f32 = sqrt(max(distanceSq_0, 9.99999993922529029e-09f));
    (*distance_0) = _S35;
    (*toLight_0) = (vec3<f32>(0) - fromLens_0) / vec3<f32>(_S35);
    var range_0 : f32 = _S34.w;
    var _S36 : bool;
    if(range_0 <= 0.0f)
    {
        _S36 = true;
    }
    else
    {
        _S36 = (*distance_0) >= range_0;
    }
    if(_S36)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var _S37 : f32 = FlashlightCookie_0(&((*f_4)), (vec3<f32>(0) - (*toLight_0)));
    if(_S37 <= 0.0f)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var ratio_0 : f32 = distanceSq_0 / (range_0 * range_0);
    var window_0 : f32 = saturate(1.0f - ratio_0 * ratio_0);
    var window_1 : f32 = window_0 * window_0;
    if(thrown_0)
    {
        _S36 = (*distance_0) > 5.0f;
    }
    else
    {
        _S36 = false;
    }
    var carry_0 : f32;
    if(_S36)
    {
        carry_0 = pow((*distance_0) / 5.0f, (*f_4).control_0.z);
    }
    else
    {
        carry_0 = 1.0f;
    }
    return (*f_4).colour_0.xyz * vec3<f32>((_S37 * window_1 * carry_0 / max(distanceSq_0, (*f_4).colour_0.w)));
}

fn FlashlightReaching_0( viewPos_1 : vec3<f32>,  normal_0 : vec3<f32>,  position_5 : vec4<f32>,  lightDir_0 : ptr<function, vec3<f32>>) -> vec3<f32>
{
    (*lightDir_0) = vec3<f32>(0.0f, 0.0f, 1.0f);
    var _S38 : FlashlightParams_std140_0 = gi_0.flashlight_0;
    var _S39 : bool = FlashlightOn_0(&(_S38));
    if(!_S39)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var _S40 : FlashlightParams_std140_0 = gi_0.flashlight_0;
    var toLightView_0 : vec3<f32>;
    var distance_1 : f32;
    var _S41 : vec3<f32> = FlashlightIrradiance_0(&(_S40), viewPos_1, true, &(toLightView_0), &(distance_1));
    const _S42 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    if((all((_S41 <= _S42))))
    {
        return _S42;
    }
    var _S43 : vec3<f32> = normalize((((vec4<f32>(toLightView_0, 0.0f)) * (mat4x4<f32>(gi_0.viewToWorld_0.data_0[i32(0)][i32(0)], gi_0.viewToWorld_0.data_0[i32(1)][i32(0)], gi_0.viewToWorld_0.data_0[i32(2)][i32(0)], gi_0.viewToWorld_0.data_0[i32(3)][i32(0)], gi_0.viewToWorld_0.data_0[i32(0)][i32(1)], gi_0.viewToWorld_0.data_0[i32(1)][i32(1)], gi_0.viewToWorld_0.data_0[i32(2)][i32(1)], gi_0.viewToWorld_0.data_0[i32(3)][i32(1)], gi_0.viewToWorld_0.data_0[i32(0)][i32(2)], gi_0.viewToWorld_0.data_0[i32(1)][i32(2)], gi_0.viewToWorld_0.data_0[i32(2)][i32(2)], gi_0.viewToWorld_0.data_0[i32(3)][i32(2)], gi_0.viewToWorld_0.data_0[i32(0)][i32(3)], gi_0.viewToWorld_0.data_0[i32(1)][i32(3)], gi_0.viewToWorld_0.data_0[i32(2)][i32(3)], gi_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz);
    (*lightDir_0) = _S43;
    if((dot(normal_0, _S43)) <= 0.0f)
    {
        return _S42;
    }
    return _S41 * vec3<f32>(FlashlightShadowAt_0(position_5));
}

fn ShadowNoise_0( pixel_0 : vec2<f32>) -> f32
{
    return fract(52.98291778564453125f * fract(dot(pixel_0, vec2<f32>(0.06711056083440781f, 0.00583714991807938f))));
}

fn PcfShadow_0( cascade_0 : i32,  uv_9 : vec3<f32>,  radiusTexels_0 : f32,  pixel_1 : vec2<f32>) -> f32
{
    var texel_0 : f32 = gi_0.shadowConfig_0.w;
    var y_1 : i32;
    var sum_0 : f32;
    if(radiusTexels_0 <= 1.5f)
    {
        y_1 = i32(-1);
        sum_0 = 0.0f;
        for(;;)
        {
            if(y_1 <= i32(1))
            {
            }
            else
            {
                break;
            }
            var x_1 : i32 = i32(-1);
            for(;;)
            {
                if(x_1 <= i32(1))
                {
                }
                else
                {
                    break;
                }
                var _S44 : vec3<f32> = vec3<f32>(uv_9.xy + vec2<f32>(f32(x_1), f32(y_1)) * vec2<f32>(texel_0), f32(cascade_0));
                var sum_1 : f32 = sum_0 + (textureSampleCompareLevel((uShadowMap_0), (uShadowMapSampler_0), ((_S44)).xy, i32(((_S44)).z), (uv_9.z)));
                x_1 = x_1 + i32(1);
                sum_0 = sum_1;
            }
            y_1 = y_1 + i32(1);
        }
        return sum_0 / 9.0f;
    }
    var angle_2 : f32 = ShadowNoise_0(pixel_1) * 6.28318548202514648f;
    var _S45 : f32 = sin(angle_2);
    var _S46 : f32 = cos(angle_2);
    var _S47 : f32 = radiusTexels_0 * texel_0;
    y_1 = i32(0);
    sum_0 = 0.0f;
    for(;;)
    {
        if(y_1 < i32(12))
        {
        }
        else
        {
            break;
        }
        var _S48 : f32 = kShadowDisc_0[y_1].x;
        var _S49 : f32 = kShadowDisc_0[y_1].y;
        var _S50 : vec3<f32> = vec3<f32>(uv_9.xy + vec2<f32>(_S48 * _S46 - _S49 * _S45, _S48 * _S45 + _S49 * _S46) * vec2<f32>(_S47), f32(cascade_0));
        var sum_2 : f32 = sum_0 + (textureSampleCompareLevel((uShadowMap_0), (uShadowMapSampler_0), ((_S50)).xy, i32(((_S50)).z), (uv_9.z)));
        y_1 = y_1 + i32(1);
        sum_0 = sum_2;
    }
    return sum_0 / 12.0f;
}

fn CascadeVisibility_0( i_1 : i32,  base_0 : vec3<f32>,  worldNormal_0 : vec3<f32>,  radiusTexels_1 : f32,  pixel_2 : vec2<f32>,  inside_0 : ptr<function, bool>) -> f32
{
    var _S51 : mat4x4<f32> = mat4x4<f32>(gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(0)][i32(0)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(1)][i32(0)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(2)][i32(0)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(3)][i32(0)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(0)][i32(1)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(1)][i32(1)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(2)][i32(1)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(3)][i32(1)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(0)][i32(2)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(1)][i32(2)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(2)][i32(2)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(3)][i32(2)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(0)][i32(3)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(1)][i32(3)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(2)][i32(3)], gi_0.cascadeMatrix_0.data_1[i_1].data_0[i32(3)][i32(3)]);
    var texel_1 : f32 = 2.0f * gi_0.shadowConfig_0.w / max(length(vec3<f32>(_S51[i32(0)][i32(0)], _S51[i32(1)][i32(0)], _S51[i32(2)][i32(0)])), 9.99999997475242708e-07f);
    var depthRange_0 : f32 = 2.0f / max(length(vec3<f32>(_S51[i32(0)][i32(2)], _S51[i32(1)][i32(2)], _S51[i32(2)][i32(2)])), 9.99999997475242708e-07f);
    var clip_0 : vec4<f32> = (((vec4<f32>(base_0 + worldNormal_0 * vec3<f32>((gi_0.shadowConfig_0.z * texel_1)), 1.0f)) * (_S51)));
    var ndc_1 : vec3<f32> = clip_0.xyz / vec3<f32>(clip_0.w);
    var _S52 : bool = (all(((abs(ndc_1.xy)) <= vec2<f32>(1.0f, 1.0f))));
    (*inside_0) = _S52;
    if(!_S52)
    {
        return 1.0f;
    }
    var depth_1 : f32 = ndc_1.z * 0.5f + 0.5f - gi_0.shadowConfig_0.y * texel_1 / depthRange_0;
    var _S53 : mat4x4<f32> = mat4x4<f32>(gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(0)][i32(0)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(1)][i32(0)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(2)][i32(0)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(3)][i32(0)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(0)][i32(1)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(1)][i32(1)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(2)][i32(1)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(3)][i32(1)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(0)][i32(2)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(1)][i32(2)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(2)][i32(2)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(3)][i32(2)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(0)][i32(3)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(1)][i32(3)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(2)][i32(3)], gi_0.cascadeMatrix_0.data_1[i32(0)].data_0[i32(3)][i32(3)]);
    var _S54 : f32 = max(texel_1, 9.99999997475242708e-07f);
    var _S55 : f32 = max(radiusTexels_1 * (2.0f * gi_0.shadowConfig_0.w / max(length(vec3<f32>(_S53[i32(0)][i32(0)], _S53[i32(1)][i32(0)], _S53[i32(2)][i32(0)])), 9.99999997475242708e-07f)) / _S54, 1.0f);
    var uv_10 : vec2<f32> = vec2<f32>(ndc_1.x * 0.5f + 0.5f, 0.5f - ndc_1.y * 0.5f);
    var mapWidth_0 : u32;
    var mapHeight_0 : u32;
    var mapLayers_0 : u32;
    {var dim = textureDimensions((uShadowMap_0));((mapWidth_0)) = dim.x;((mapHeight_0)) = dim.y;((mapLayers_0)) = textureNumLayers((uShadowMap_0));};
    var _S56 : vec2<i32> = vec2<i32>(i32(mapWidth_0), i32(mapHeight_0));
    var _S57 : f32 = clamp(0.1403999924659729f / _S54, 2.0f, 16.0f);
    var angle_3 : f32 = ShadowNoise_0(pixel_2 + vec2<f32>(7.0f, 3.0f)) * 6.28318548202514648f;
    var _S58 : f32 = sin(angle_3);
    var _S59 : f32 = cos(angle_3);
    var k_0 : i32 = i32(0);
    var blockerSum_0 : f32 = 0.0f;
    var blockers_0 : f32 = 0.0f;
    for(;;)
    {
        if(k_0 < i32(12))
        {
        }
        else
        {
            break;
        }
        var _S60 : f32 = kShadowDisc_0[k_0].x;
        var _S61 : f32 = kShadowDisc_0[k_0].y;
        var _S62 : vec4<i32> = vec4<i32>(clamp(vec2<i32>((uv_10 + vec2<f32>(_S60 * _S59 - _S61 * _S58, _S60 * _S58 + _S61 * _S59) * vec2<f32>((_S57 * gi_0.shadowConfig_0.w))) * vec2<f32>(_S56)), vec2<i32>(i32(0), i32(0)), _S56 - vec2<i32>(i32(1))), i_1, i32(0));
        var stored_0 : f32 = (textureLoad((uShadowMap_0), ((_S62)).xy, i32(((_S62)).z), ((_S62)).w));
        if(stored_0 < depth_1)
        {
            var blockers_1 : f32 = blockers_0 + 1.0f;
            blockerSum_0 = blockerSum_0 + (depth_1 - stored_0);
            blockers_0 = blockers_1;
        }
        k_0 = k_0 + i32(1);
    }
    var filterTexels_0 : f32;
    if(blockers_0 > 0.0f)
    {
        filterTexels_0 = clamp(max(_S55, blockerSum_0 / blockers_0 * depthRange_0 * 0.00467999977990985f / _S54), 1.0f, 24.0f);
    }
    else
    {
        filterTexels_0 = _S55;
    }
    return PcfShadow_0(i_1, vec3<f32>(uv_10, depth_1), filterTexels_0, pixel_2);
}

fn SunVisibility_0( viewPos_2 : vec3<f32>,  worldNormal_1 : vec3<f32>,  pixel_3 : vec2<f32>) -> f32
{
    var count_0 : i32 = i32(gi_0.shadowConfig_0.x + 0.5f);
    if(count_0 <= i32(0))
    {
        return 1.0f;
    }
    var _S63 : f32 = max(gi_0.probeSky_0.w, 1.0f);
    var _S64 : vec3<f32> = (((vec4<f32>(viewPos_2, 1.0f)) * (mat4x4<f32>(gi_0.viewToWorld_0.data_0[i32(0)][i32(0)], gi_0.viewToWorld_0.data_0[i32(1)][i32(0)], gi_0.viewToWorld_0.data_0[i32(2)][i32(0)], gi_0.viewToWorld_0.data_0[i32(3)][i32(0)], gi_0.viewToWorld_0.data_0[i32(0)][i32(1)], gi_0.viewToWorld_0.data_0[i32(1)][i32(1)], gi_0.viewToWorld_0.data_0[i32(2)][i32(1)], gi_0.viewToWorld_0.data_0[i32(3)][i32(1)], gi_0.viewToWorld_0.data_0[i32(0)][i32(2)], gi_0.viewToWorld_0.data_0[i32(1)][i32(2)], gi_0.viewToWorld_0.data_0[i32(2)][i32(2)], gi_0.viewToWorld_0.data_0[i32(3)][i32(2)], gi_0.viewToWorld_0.data_0[i32(0)][i32(3)], gi_0.viewToWorld_0.data_0[i32(1)][i32(3)], gi_0.viewToWorld_0.data_0[i32(2)][i32(3)], gi_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz;
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
        if(i_2 >= count_0)
        {
            break;
        }
        var _S65 : f32 = viewPos_2.z;
        if(_S65 > (gi_0.cascadeSplits_0[i_2]))
        {
            i_2 = i_2 + i32(1);
            continue;
        }
        var inside_1 : bool;
        var visibility_0 : f32 = CascadeVisibility_0(i_2, _S64, worldNormal_1, _S63, pixel_3, &(inside_1));
        if(!inside_1)
        {
            i_2 = i_2 + i32(1);
            continue;
        }
        var sliceStart_0 : f32;
        if(i_2 == i32(0))
        {
            sliceStart_0 = 0.0f;
        }
        else
        {
            sliceStart_0 = gi_0.cascadeSplits_0[i_2 - i32(1)];
        }
        var _S66 : f32 = max((gi_0.cascadeSplits_0[i_2] - sliceStart_0) * 0.10000000149011612f, 0.00100000004749745f);
        var toNext_0 : f32 = saturate((_S65 - (gi_0.cascadeSplits_0[i_2] - _S66)) / _S66);
        var visibility_1 : f32;
        if(toNext_0 > 0.0f)
        {
            var _S67 : i32 = i_2 + i32(1);
            if(_S67 < count_0)
            {
                var nextInside_0 : bool;
                var further_0 : f32 = CascadeVisibility_0(_S67, _S64, worldNormal_1, _S63, pixel_3, &(nextInside_0));
                if(nextInside_0)
                {
                    sliceStart_0 = further_0;
                }
                else
                {
                    sliceStart_0 = 1.0f;
                }
                visibility_1 = sliceStart_0;
            }
            else
            {
                visibility_1 = 1.0f;
            }
            visibility_1 = mix(visibility_0, visibility_1, toNext_0);
        }
        else
        {
            visibility_1 = visibility_0;
        }
        return visibility_1;
    }
    return 1.0f;
}

fn ViewDepthOf_0( depth_2 : f32) -> f32
{
    var near_1 : f32 = gi_0.unproject_0.z;
    var far_1 : f32 = gi_0.unproject_0.w;
    return 2.0f * near_1 * far_1 / (far_1 + near_1 - (1.0f - 2.0f * depth_2) * (far_1 - near_1));
}

fn ContactShadow_0( viewPos_3 : vec3<f32>,  viewSun_0 : vec3<f32>,  reach_0 : f32,  pixel_4 : vec2<f32>) -> f32
{
    var _S68 : bool;
    if(reach_0 <= 0.0f)
    {
        _S68 = true;
    }
    else
    {
        _S68 = (viewSun_0.z) <= -0.99900001287460327f;
    }
    if(_S68)
    {
        return 1.0f;
    }
    var width_2 : u32;
    var height_2 : u32;
    {var dim = textureDimensions((uDepth_0));((width_2)) = dim.x;((height_2)) = dim.y;};
    var _S69 : vec2<f32> = vec2<f32>(f32(width_2), f32(height_2));
    var stride_0 : vec3<f32> = viewSun_0 * vec3<f32>((reach_0 / 12.0f));
    var _S70 : vec3<f32> = viewPos_3 + stride_0 * vec3<f32>(ShadowNoise_0(pixel_4 + vec2<f32>(17.0f, 31.0f)));
    var _S71 : f32 = viewPos_3.z;
    var _S72 : f32 = max(0.30000001192092896f, 0.01999999955296516f * _S71);
    var _S73 : f32 = 0.00999999977648258f + 0.0020000000949949f * _S71;
    var step_0 : i32 = i32(0);
    var at_0 : vec3<f32> = _S70;
    for(;;)
    {
        if(step_0 < i32(12))
        {
        }
        else
        {
            break;
        }
        var at_1 : vec3<f32> = at_0 + stride_0;
        var _S74 : f32 = at_1.z;
        if(_S74 <= (gi_0.unproject_0.z))
        {
            break;
        }
        var uv_11 : vec2<f32> = vec2<f32>(0.5f + 0.5f * at_1.x * gi_0.unproject_0.x / _S74, 0.5f - 0.5f * at_1.y * gi_0.unproject_0.y / _S74);
        if((any((uv_11 < vec2<f32>(0.0f, 0.0f)))))
        {
            _S68 = true;
        }
        else
        {
            _S68 = (any((uv_11 > vec2<f32>(1.0f, 1.0f))));
        }
        if(_S68)
        {
            break;
        }
        var _S75 : vec3<i32> = vec3<i32>(vec2<i32>(uv_11 * _S69), i32(0));
        var sample_0 : f32 = (textureLoad((uDepth_0), ((_S75)).xy, ((_S75)).z));
        if(sample_0 <= 0.0f)
        {
            step_0 = step_0 + i32(1);
            at_0 = at_1;
            continue;
        }
        var behind_0 : f32 = _S74 - ViewDepthOf_0(sample_0);
        var _S76 : bool;
        if(behind_0 > _S73)
        {
            _S76 = behind_0 < _S72;
        }
        else
        {
            _S76 = false;
        }
        if(_S76)
        {
            return f32(step_0) / 12.0f;
        }
        step_0 = step_0 + i32(1);
        at_0 = at_1;
    }
    return 1.0f;
}

fn DecodeMatte_0( material_2 : vec4<f32>) -> bool
{
    return ((i32(material_2.w * 255.0f + 0.5f) & (i32(16)))) != i32(0);
}

fn WetFootprint_0( ndcPerPixel_0 : f32,  viewZ_2 : f32,  xScale_0 : f32,  nDotV_0 : f32) -> f32
{
    return abs(ndcPerPixel_0 * viewZ_2) / max(xScale_0, 0.00009999999747379f) / max(nDotV_0, 0.07999999821186066f);
}

struct WetParams_0
{
     camera_1 : vec2<f32>,
     amount_0 : f32,
     coverage_0 : f32,
     puddleSize_0 : f32,
     rippleStrength_0 : f32,
     rippleSize_0 : f32,
     windStrength_0 : f32,
     seconds_2 : f32,
};

fn Weather_0() -> WetParams_0
{
    var w_0 : WetParams_0;
    w_0.camera_1 = gi_0.camera_0.xy;
    w_0.amount_0 = max(gi_0.wetness0_0.x, 0.0f);
    w_0.coverage_0 = gi_0.wetness0_0.y;
    w_0.rippleStrength_0 = gi_0.wetness0_0.z;
    w_0.seconds_2 = gi_0.wetness0_0.w;
    w_0.puddleSize_0 = gi_0.wetness1_0.x;
    w_0.rippleSize_0 = gi_0.wetness1_0.y;
    w_0.windStrength_0 = gi_0.wetness1_0.z;
    return w_0;
}

struct ShelterMap_0
{
     origin_1 : vec2<f32>,
     invCell_0 : f32,
     lowestZ_0 : f32,
     scale_0 : f32,
     headroom_0 : f32,
     reach_1 : f32,
     size_1 : vec2<f32>,
     present_0 : f32,
};

fn Roofs_0() -> ShelterMap_0
{
    var m_0 : ShelterMap_0;
    m_0.origin_1 = gi_0.shelterMap0_0.xy;
    m_0.invCell_0 = gi_0.shelterMap0_0.z;
    m_0.lowestZ_0 = gi_0.shelterMap0_0.w;
    m_0.scale_0 = gi_0.shelterMap1_0.x;
    m_0.headroom_0 = gi_0.shelterMap1_0.y;
    m_0.reach_1 = gi_0.shelterMap1_0.z;
    m_0.size_1 = gi_0.shelterMap2_0.xy;
    m_0.present_0 = gi_0.wetness1_0.w;
    return m_0;
}

fn ShelterTexel_0( m_1 : ShelterMap_0,  worldXY_0 : vec2<f32>) -> vec2<i32>
{
    return vec2<i32>(floor((worldXY_0 - m_1.origin_1) * vec2<f32>(m_1.invCell_0)));
}

fn ShelterHolds_0( m_2 : ShelterMap_0,  texel_2 : vec2<i32>) -> bool
{
    var _S77 : bool;
    if((m_2.present_0) > 0.5f)
    {
        _S77 = (texel_2.x) >= i32(0);
    }
    else
    {
        _S77 = false;
    }
    if(_S77)
    {
        _S77 = (texel_2.y) >= i32(0);
    }
    else
    {
        _S77 = false;
    }
    if(_S77)
    {
        _S77 = (texel_2.x) < i32(m_2.size_1.x);
    }
    else
    {
        _S77 = false;
    }
    if(_S77)
    {
        _S77 = (texel_2.y) < i32(m_2.size_1.y);
    }
    else
    {
        _S77 = false;
    }
    return _S77;
}

fn ShelterSurface_0( m_3 : ShelterMap_0,  texel_3 : vec4<f32>) -> f32
{
    var packed_2 : f32 = floor(texel_3.z * 255.0f + 0.5f) + floor(texel_3.y * 255.0f + 0.5f) * 256.0f;
    var _S78 : f32;
    if(packed_2 < 0.5f)
    {
        _S78 = -1.00000001504746622e+30f;
    }
    else
    {
        _S78 = m_3.lowestZ_0 + (packed_2 - 1.0f) * m_3.scale_0;
    }
    return _S78;
}

fn ShelterCovers_0( m_4 : ShelterMap_0,  surfaceZ_0 : f32,  worldZ_0 : f32) -> bool
{
    var _S79 : bool;
    if(surfaceZ_0 > (worldZ_0 + m_4.headroom_0))
    {
        _S79 = surfaceZ_0 < (worldZ_0 + m_4.reach_1);
    }
    else
    {
        _S79 = false;
    }
    return _S79;
}

fn UnderShelter_0( relative_0 : vec3<f32>) -> bool
{
    var m_5 : ShelterMap_0 = Roofs_0();
    var at_2 : vec2<i32> = ShelterTexel_0(m_5, relative_0.xy + gi_0.camera_0.xy);
    if(!ShelterHolds_0(m_5, at_2))
    {
        return false;
    }
    var _S80 : vec3<i32> = vec3<i32>(at_2, i32(0));
    return ShelterCovers_0(m_5, ShelterSurface_0(m_5, (textureLoad((uShelterMap_0), ((_S80)).xy, ((_S80)).z))), relative_0.z + gi_0.camera_0.z);
}

fn WetRotate_0( v_2 : vec2<f32>,  rotation_0 : vec2<f32>) -> vec2<f32>
{
    var _S81 : f32 = rotation_0.x;
    var _S82 : f32 = v_2.x;
    var _S83 : f32 = rotation_0.y;
    var _S84 : f32 = v_2.y;
    return vec2<f32>(_S81 * _S82 - _S83 * _S84, _S83 * _S82 + _S81 * _S84);
}

struct WetGrid_0
{
     uv_12 : vec2<f32>,
     base_1 : vec2<f32>,
};

fn WetGridAt_0( w_1 : WetParams_0,  relative_1 : vec3<f32>,  size_2 : f32,  rotation_1 : vec2<f32>) -> WetGrid_0
{
    var _S85 : vec2<f32> = vec2<f32>(size_2);
    var camera_2 : vec2<f32> = WetRotate_0(w_1.camera_1, rotation_1) / _S85;
    var cell_2 : vec2<f32> = floor(camera_2);
    var grid_0 : WetGrid_0;
    grid_0.uv_12 = WetRotate_0(relative_1.xy, rotation_1) / _S85 + (camera_2 - cell_2);
    grid_0.base_1 = cell_2;
    return grid_0;
}

fn WetHash_0( cell_3 : vec2<f32>) -> f32
{
    var _S86 : vec2<u32> = vec2<u32>(vec2<i32>(cell_3) + vec2<i32>(i32(1048576)));
    var h_1 : u32 = ((_S86.x * u32(73856093)) ^ ((_S86.y * u32(19349663))));
    var h_2 : u32 = ((h_1 ^ (((h_1 >> (u32(13))))))) * u32(1540483477);
    return f32((((h_2 ^ (((h_2 >> (u32(15))))))) & (u32(16777215)))) * 5.96046518808179826e-08f;
}

fn WetNoise_0( p_1 : vec2<f32>,  base_2 : vec2<f32>) -> f32
{
    var cell_4 : vec2<f32> = floor(p_1);
    var f_5 : vec2<f32> = p_1 - cell_4;
    var f_6 : vec2<f32> = f_5 * f_5 * (vec2<f32>(3.0f) - vec2<f32>(2.0f) * f_5);
    var cell_5 : vec2<f32> = cell_4 + base_2;
    var _S87 : f32 = f_6.x;
    return mix(mix(WetHash_0(cell_5), WetHash_0(cell_5 + vec2<f32>(1.0f, 0.0f)), _S87), mix(WetHash_0(cell_5 + vec2<f32>(0.0f, 1.0f)), WetHash_0(cell_5 + vec2<f32>(1.0f, 1.0f)), _S87), f_6.y);
}

fn WetGridAt_1( w_2 : WetParams_0,  relative_2 : vec3<f32>,  size_3 : f32) -> WetGrid_0
{
    return WetGridAt_0(w_2, relative_2, size_3, vec2<f32>(1.0f, 0.0f));
}

fn PuddleHeight_0( w_3 : WetParams_0,  relative_3 : vec3<f32>,  size_4 : f32) -> f32
{
    var fine_0 : WetGrid_0 = WetGridAt_0(w_3, relative_3, size_4 * 0.46000000834465027f, vec2<f32>(0.80000001192092896f, 0.60000002384185791f));
    var detail_0 : f32 = WetNoise_0(fine_0.uv_12 + vec2<f32>(11.30000019073486328f), fine_0.base_1);
    var coarse_0 : WetGrid_0 = WetGridAt_1(w_3, relative_3, size_4);
    return WetNoise_0(coarse_0.uv_12 + vec2<f32>(((detail_0 - 0.5f) * 0.40000000596046448f)), coarse_0.base_1) * 0.64999997615814209f + detail_0 * 0.34999999403953552f;
}

fn PuddleDepth_0( w_4 : WetParams_0,  relative_4 : vec3<f32>) -> f32
{
    return clamp(w_4.coverage_0, 0.0f, 1.0f) - PuddleHeight_0(w_4, relative_4, max(w_4.puddleSize_0, 0.05000000074505806f));
}

fn PuddleLevel_0( normal_1 : vec3<f32>) -> f32
{
    return clamp((normal_1.z - 0.86000001430511475f) * 12.0f, 0.0f, 1.0f);
}

fn PuddleShore_0( w_5 : WetParams_0,  relative_5 : vec3<f32>,  normal_2 : vec3<f32>) -> f32
{
    return clamp(PuddleDepth_0(w_5, relative_5) * 6.0f + 0.89999997615814209f, 0.0f, 1.0f) * PuddleLevel_0(normal_2);
}

fn DampAmount_0( w_6 : WetParams_0,  relative_6 : vec3<f32>,  normal_3 : vec3<f32>) -> f32
{
    var facing_0 : f32 = clamp(normal_3.z * 0.5f + 0.5f, 0.0f, 1.0f);
    return clamp(w_6.amount_0 * mix(0.25f, 1.0f, facing_0 * facing_0) + PuddleShore_0(w_6, relative_6, normal_3) * w_6.amount_0 * 0.44999998807907104f, 0.0f, 1.0f);
}

fn PuddleMask_0( w_7 : WetParams_0,  relative_7 : vec3<f32>,  normal_4 : vec3<f32>) -> f32
{
    return clamp(PuddleDepth_0(w_7, relative_7) * 20.0f, 0.0f, 1.0f) * PuddleLevel_0(normal_4) * w_7.amount_0;
}

fn DampRoughness_0( roughness_0 : f32,  damp_0 : f32) -> f32
{
    return mix(roughness_0, max(0.55000001192092896f, roughness_0 * 0.75f), damp_0);
}

fn WetHash3_0( cell_6 : vec2<f32>) -> vec3<f32>
{
    var _S88 : vec2<u32> = vec2<u32>(vec2<i32>(cell_6) + vec2<i32>(i32(1048576)));
    var h_3 : u32 = ((_S88.x * u32(73856093)) ^ ((_S88.y * u32(19349663))));
    var h_4 : u32 = ((h_3 ^ (((h_3 >> (u32(13))))))) * u32(1540483477);
    var h_5 : u32 = (h_4 ^ (((h_4 >> (u32(15))))));
    var g_0 : u32 = h_5 * u32(668265261);
    var g_1 : u32 = (g_0 ^ (((g_0 >> (u32(15))))));
    var k_1 : u32 = g_1 * u32(2246822507);
    return vec3<f32>(f32((h_5 & (u32(16777215)))), f32((g_1 & (u32(16777215)))), f32((((k_1 ^ (((k_1 >> (u32(13))))))) & (u32(16777215))))) * vec3<f32>(5.96046518808179826e-08f);
}

fn RippleLayer_0( uv_13 : vec2<f32>,  base_3 : vec2<f32>,  phase_0 : f32,  weight_0 : f32) -> vec2<f32>
{
    if(weight_0 <= 0.0f)
    {
        return vec2<f32>(0.0f, 0.0f);
    }
    var _S89 : f32 = mix(0.20000000298023224f, 1.0f, clamp(weight_0, 0.0f, 1.0f));
    const _S90 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    var _S91 : vec2<f32> = vec2<f32>(0.5f);
    var _S92 : vec2<f32> = floor(uv_13 - _S91);
    var j_0 : i32 = i32(0);
    var slope_0 : vec2<f32> = _S90;
    for(;;)
    {
        if(j_0 < i32(2))
        {
        }
        else
        {
            break;
        }
        var i_3 : i32 = i32(0);
        var slope_1 : vec2<f32> = slope_0;
        for(;;)
        {
            if(i_3 < i32(2))
            {
            }
            else
            {
                break;
            }
            var cell_7 : vec2<f32> = _S92 + vec2<f32>(f32(i_3), f32(j_0));
            var fromCentre_0 : vec2<f32> = uv_13 - (cell_7 + _S91);
            if((dot(fromCentre_0, fromCentre_0)) > 0.60839992761611938f)
            {
                i_3 = i_3 + i32(1);
                continue;
            }
            var h_6 : vec3<f32> = WetHash3_0(cell_7 + base_3);
            var t_2 : f32 = fract(phase_0 + h_6.x);
            var fromStrike_0 : vec2<f32> = fromCentre_0 - (h_6.yz - _S91) * _S91;
            var distance_2 : f32 = length(fromStrike_0);
            var wave_0 : f32 = sin(3.14159274101257324f * clamp((t_2 - 1.0f + (1.0f - clamp(distance_2 / 0.46000000834465027f, 0.0f, 1.0f))) * 24.0f, 0.0f, 4.0f));
            var envelope_0 : f32 = clamp(_S89 - t_2, 0.0f, 1.0f);
            var outward_0 : vec2<f32>;
            if(distance_2 > 0.00000999999974738f)
            {
                outward_0 = fromStrike_0 / vec2<f32>(distance_2);
            }
            else
            {
                outward_0 = _S90;
            }
            slope_1 = slope_1 - outward_0 * vec2<f32>((wave_0 * envelope_0 * 0.44999998807907104f));
            i_3 = i_3 + i32(1);
        }
        j_0 = j_0 + i32(1);
        slope_0 = slope_1;
    }
    return slope_0;
}

fn RainRippleSlope_0( w_8 : WetParams_0,  relative_8 : vec3<f32>,  size_5 : f32,  time_0 : f32,  intensity_0 : f32) -> vec2<f32>
{
    const _S93 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    var k_2 : i32 = i32(0);
    var slope_2 : vec2<f32> = _S93;
    for(;;)
    {
        if(k_2 < i32(4))
        {
        }
        else
        {
            break;
        }
        var weight_1 : f32 = clamp((intensity_0 - vec4<f32>(0.0f, 0.2199999988079071f, 0.43999999761581421f, 0.6600000262260437f)[k_2]) / 0.25f, 0.0f, 1.0f);
        if(weight_1 <= 0.0f)
        {
            k_2 = k_2 + i32(1);
            continue;
        }
        var grid_1 : WetGrid_0 = WetGridAt_1(w_8, relative_8, size_5 * vec4<f32>(0.51999998092651367f, 0.74000000953674316f, 0.97000002861022949f, 1.27999997138977051f)[k_2]);
        slope_2 = slope_2 + RippleLayer_0(grid_1.uv_12 + kRippleOffset_0[k_2], grid_1.base_1, fract(time_0 * vec4<f32>(0.95999997854232788f, 0.81000000238418579f, 0.88999998569488525f, 0.72000002861022949f)[k_2] + vec4<f32>(0.0f, 0.31000000238418579f, 0.57999998331069946f, 0.82999998331069946f)[k_2]), weight_1);
        k_2 = k_2 + i32(1);
    }
    return slope_2;
}

fn WindRippleSlope_0( w_9 : WetParams_0,  relative_9 : vec3<f32>,  time_1 : f32) -> vec2<f32>
{
    var grid_2 : WetGrid_0 = WetGridAt_1(w_9, relative_9, 1.79999995231628418f);
    var p_2 : vec2<f32> = grid_2.uv_12 + vec2<f32>(time_1 * 0.07999999821186066f, time_1 * 0.05000000074505806f);
    var here_0 : f32 = WetNoise_0(p_2, grid_2.base_1);
    return vec2<f32>(WetNoise_0(p_2 + vec2<f32>(0.15000000596046448f, 0.0f), grid_2.base_1) - here_0, WetNoise_0(p_2 + vec2<f32>(0.0f, 0.15000000596046448f), grid_2.base_1) - here_0);
}

struct WaterSurface_0
{
     normal_5 : vec3<f32>,
     roughness_1 : f32,
     mirror_0 : f32,
};

fn WaterSurfaceAt_0( w_10 : WetParams_0,  relative_10 : vec3<f32>,  footprint_0 : f32) -> WaterSurface_0
{
    var _S94 : f32 = max(w_10.rippleSize_0, 0.01999999955296516f);
    var slope_3 : vec2<f32> = RainRippleSlope_0(w_10, relative_10, _S94, w_10.seconds_2 * 0.60000002384185791f, w_10.rippleStrength_0) + WindRippleSlope_0(w_10, relative_10, w_10.seconds_2) * vec2<f32>(w_10.windStrength_0) * vec2<f32>(1.60000002384185791f);
    var blur_0 : f32 = clamp(footprint_0 / (_S94 * 0.5f), 0.0f, 1.0f);
    var surface_0 : WaterSurface_0;
    var _S95 : f32 = 1.0f - blur_0;
    surface_0.normal_5 = normalize(vec3<f32>(slope_3 * vec2<f32>(_S95), 1.0f));
    surface_0.roughness_1 = clamp(0.05000000074505806f + length(slope_3) * _S95 * 0.10000000149011612f + (0.20000000298023224f * w_10.rippleStrength_0 + 0.10000000149011612f * w_10.windStrength_0) * blur_0, 0.05000000074505806f, 0.31999999284744263f);
    surface_0.mirror_0 = clamp(1.0f - blur_0 * 2.0f, 0.0f, 1.0f);
    return surface_0;
}

fn WaterFresnel_0( normal_6 : vec3<f32>,  toEye_0 : vec3<f32>) -> f32
{
    var grazing_0 : f32 = 1.0f - clamp(dot(normal_6, toEye_0), 0.0f, 1.0f);
    var squared_0 : f32 = grazing_0 * grazing_0;
    return 0.01999999955296516f + 0.98000001907348633f * squared_0 * squared_0 * grazing_0;
}

fn ApplyWetness_0( relative_11 : vec3<f32>,  toEye_1 : vec3<f32>,  footprint_1 : f32,  albedo_0 : ptr<function, vec3<f32>>,  normal_7 : ptr<function, vec3<f32>>,  roughness_2 : ptr<function, f32>,  waterMask_0 : ptr<function, f32>,  waterReflectance_0 : ptr<function, f32>)
{
    (*waterMask_0) = 0.0f;
    (*waterReflectance_0) = 0.0f;
    var w_11 : WetParams_0 = Weather_0();
    var _S96 : bool;
    if((w_11.amount_0) <= 0.0f)
    {
        _S96 = true;
    }
    else
    {
        _S96 = UnderShelter_0(relative_11);
    }
    if(_S96)
    {
        return;
    }
    var damp_1 : f32 = DampAmount_0(w_11, relative_11, (*normal_7));
    var puddle_0 : f32 = PuddleMask_0(w_11, relative_11, (*normal_7));
    (*waterMask_0) = puddle_0;
    (*albedo_0) = (*albedo_0) * vec3<f32>(mix(1.0f, 0.60000002384185791f, damp_1));
    (*roughness_2) = DampRoughness_0((*roughness_2), damp_1);
    if(puddle_0 <= 0.0f)
    {
        return;
    }
    var _S97 : vec3<f32> = vec3<f32>(puddle_0);
    (*albedo_0) = mix((*albedo_0), (*albedo_0) * vec3<f32>(0.5f), _S97);
    var water_0 : WaterSurface_0 = WaterSurfaceAt_0(w_11, relative_11, footprint_1);
    (*normal_7) = normalize(mix((*normal_7), water_0.normal_5, _S97));
    (*roughness_2) = mix((*roughness_2), water_0.roughness_1, puddle_0);
    var _S98 : f32 = WaterFresnel_0((*normal_7), toEye_1) * puddle_0;
    (*waterReflectance_0) = _S98;
    (*albedo_0) = (*albedo_0) * vec3<f32>((1.0f - _S98));
    return;
}

fn EvaluateSurface_0( baseColour_0 : vec3<f32>,  metallic_0 : f32,  specular_0 : f32,  roughness_3 : f32,  n_0 : vec3<f32>,  v_3 : vec3<f32>,  l_0 : vec3<f32>,  diffuse_0 : ptr<function, vec3<f32>>,  specularOut_0 : ptr<function, vec3<f32>>)
{
    var f0_0 : vec3<f32> = mix(vec3<f32>((0.07999999821186066f * specular_0)), baseColour_0, vec3<f32>(metallic_0));
    var _S99 : f32 = max(roughness_3, 0.01999999955296516f);
    var a_2 : f32 = _S99 * _S99;
    var aSquared_0 : f32 = a_2 * a_2;
    var halfSum_0 : vec3<f32> = v_3 + l_0;
    var h_7 : vec3<f32> = halfSum_0 * vec3<f32>(rsqrt_0(max(dot(halfSum_0, halfSum_0), 9.99999993922529029e-09f)));
    var cosNH_0 : f32 = saturate(dot(n_0, h_7));
    var cosNL_0 : f32 = saturate(dot(n_0, l_0));
    var cosNV_0 : f32 = saturate(abs(dot(n_0, v_3)) + 0.00000999999974738f);
    var ggxDenom_0 : f32 = cosNH_0 * cosNH_0 * (aSquared_0 - 1.0f) + 1.0f;
    var ndf_0 : f32 = aSquared_0 / (3.14159274101257324f * ggxDenom_0 * ggxDenom_0);
    var vis_0 : f32 = 0.5f / max(mix(2.0f * cosNL_0 * cosNV_0, cosNL_0 + cosNV_0, a_2), 9.99999997475242708e-07f);
    var fresnel_0 : vec3<f32> = f0_0 + (vec3<f32>(saturate(50.0f * f0_0.y)) - f0_0) * vec3<f32>(pow(1.0f - saturate(dot(v_3, h_7)), 5.0f));
    (*diffuse_0) = baseColour_0 * vec3<f32>((1.0f - metallic_0)) * vec3<f32>((cosNL_0 / 3.14159274101257324f));
    (*specularOut_0) = vec3<f32>((ndf_0 * vis_0)) * fresnel_0 * vec3<f32>(cosNL_0);
    return;
}

fn SampleSkyView_0( skyView_2 : texture_2d<f32>,  skyViewSampler_2 : sampler,  worldDir_4 : vec3<f32>,  viewHeight_3 : f32) -> vec3<f32>
{
    return SampleSkyViewUv_0(skyView_2, skyViewSampler_2, SkyViewUv_0(worldDir_4, viewHeight_3));
}

fn WaterSkyward_0( reflected_0 : vec3<f32>) -> f32
{
    return clamp(reflected_0.z * 20.0f, 0.0f, 1.0f);
}

fn UpsampleLampVisibility_0( uv_14 : vec2<f32>,  viewZ_3 : f32) -> vec3<f32>
{
    var nearest_0 : vec2<f32>;
    var width_3 : u32;
    var height_3 : u32;
    {var dim = textureDimensions((uLampVisibility_0));((width_3)) = dim.x;((height_3)) = dim.y;};
    var size_6 : vec2<f32> = vec2<f32>(f32(width_3), f32(height_3));
    var coord_0 : vec2<f32> = uv_14 * size_6 - vec2<f32>(0.5f);
    var f_7 : vec2<f32> = fract(coord_0);
    var _S100 : vec2<i32> = vec2<i32>(floor(coord_0));
    var _S101 : vec2<i32> = vec2<i32>(size_6) - vec2<i32>(i32(1));
    var _S102 : f32 = f_7.x;
    var _S103 : f32 = 1.0f - _S102;
    var _S104 : f32 = f_7.y;
    var _S105 : f32 = 1.0f - _S104;
    var _S106 : vec4<f32> = vec4<f32>(_S103 * _S105, _S102 * _S105, _S103 * _S104, _S102 * _S104);
    const _S107 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    const _S108 : vec2<f32> = vec2<f32>(1.0f, 0.0f);
    var nearestGap_0 : f32 = 1.00000001504746622e+30f;
    var nearest_1 : vec2<f32> = _S108;
    var i_4 : i32 = i32(0);
    var total_0 : vec2<f32> = _S107;
    var totalWeight_0 : f32 = 0.0f;
    for(;;)
    {
        if(i_4 < i32(4))
        {
        }
        else
        {
            break;
        }
        var _S109 : vec3<i32> = vec3<i32>(clamp(_S100 + vec2<i32>((i_4 & (i32(1))), (i_4 >> (u32(1)))), vec2<i32>(i32(0), i32(0)), _S101), i32(0));
        var tap_0 : vec4<f32> = (textureLoad((uLampVisibility_0), ((_S109)).xy, ((_S109)).z));
        var _S110 : f32 = tap_0.z;
        if(_S110 <= 0.0f)
        {
            i_4 = i_4 + i32(1);
            continue;
        }
        var gap_0 : f32 = abs(_S110 - viewZ_3);
        var nearestGap_1 : f32;
        if(gap_0 < nearestGap_0)
        {
            var _S111 : vec2<f32> = tap_0.xy;
            nearestGap_1 = gap_0;
            nearest_0 = _S111;
        }
        else
        {
            nearestGap_1 = nearestGap_0;
            nearest_0 = nearest_1;
        }
        var weight_2 : f32 = _S106[i_4] * exp2(- gap_0 / max(0.02999999932944775f * viewZ_3, 0.00100000004749745f));
        var total_1 : vec2<f32> = total_0 + tap_0.xy * vec2<f32>(weight_2);
        var totalWeight_1 : f32 = totalWeight_0 + weight_2;
        nearestGap_0 = nearestGap_1;
        nearest_1 = nearest_0;
        total_0 = total_1;
        totalWeight_0 = totalWeight_1;
        i_4 = i_4 + i32(1);
    }
    if(totalWeight_0 > 0.00009999999747379f)
    {
        nearest_0 = total_0 / vec2<f32>(totalWeight_0);
    }
    else
    {
        nearest_0 = nearest_1;
    }
    return vec3<f32>(nearest_0, saturate(totalWeight_0 * 2.0f));
}

fn LineIrradiance_0( n_1 : vec3<f32>,  lineStart_0 : vec3<f32>,  lineDelta_0 : vec3<f32>) -> f32
{
    var startFacing_0 : f32 = dot(n_1, lineStart_0);
    var deltaFacing_0 : f32 = dot(n_1, lineDelta_0);
    var t1_0 : f32;
    var t0_0 : f32;
    if((abs(deltaFacing_0)) > 9.99999997475242708e-07f)
    {
        var crossing_0 : f32 = - startFacing_0 / deltaFacing_0;
        if(deltaFacing_0 > 0.0f)
        {
            var _S112 : f32 = max(0.0f, crossing_0);
            t1_0 = 1.0f;
            t0_0 = _S112;
        }
        else
        {
            t1_0 = min(1.0f, crossing_0);
            t0_0 = 0.0f;
        }
    }
    else
    {
        if(startFacing_0 <= 0.0f)
        {
            return 0.0f;
        }
        t1_0 = 1.0f;
        t0_0 = 0.0f;
    }
    if(t1_0 <= t0_0)
    {
        return 0.0f;
    }
    var a_3 : f32 = dot(lineDelta_0, lineDelta_0);
    var b_1 : f32 = dot(lineStart_0, lineDelta_0);
    var c_1 : f32 = dot(lineStart_0, lineStart_0);
    var det_0 : f32 = a_3 * c_1 - b_1 * b_1;
    if(det_0 <= (0.00000999999974738f * a_3 * c_1))
    {
        var middle_0 : vec3<f32> = lineStart_0 + lineDelta_0 * vec3<f32>((0.5f * (t0_0 + t1_0)));
        var _S113 : f32 = max(dot(middle_0, middle_0), 0.00009999999747379f);
        return saturate(dot(n_1, middle_0) * rsqrt_0(_S113)) / _S113 * (t1_0 - t0_0);
    }
    var _S114 : f32 = a_3 * t0_0;
    var _S115 : f32 = 2.0f * b_1;
    var _S116 : f32 = a_3 * t1_0;
    return max((startFacing_0 * (_S116 + b_1) - deltaFacing_0 * (b_1 * t1_0 + c_1)) / (det_0 * sqrt(max(_S116 * t1_0 + _S115 * t1_0 + c_1, 9.99999993922529029e-09f))) - (startFacing_0 * (_S114 + b_1) - deltaFacing_0 * (b_1 * t0_0 + c_1)) / (det_0 * sqrt(max(_S114 * t0_0 + _S115 * t0_0 + c_1, 9.99999993922529029e-09f))), 0.0f);
}

fn LampShade_0( i_5 : i32,  viewPos_4 : vec3<f32>,  normal_8 : vec3<f32>,  toEye_2 : vec3<f32>,  albedo_1 : vec3<f32>,  metallic_1 : f32,  specularLevel_0 : f32,  roughness_4 : f32,  diffuseWeight_0 : ptr<function, f32>) -> vec3<f32>
{
    (*diffuseWeight_0) = 0.0f;
    var toLightView_1 : vec3<f32> = lamps_0.positionRadius_0[i_5].xyz - viewPos_4;
    var distanceSq_1 : f32 = dot(toLightView_1, toLightView_1);
    var reach_2 : f32 = lamps_0.positionRadius_0[i_5].w;
    var _S117 : f32 = reach_2 * reach_2;
    if(distanceSq_1 >= _S117)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var toCentre_0 : vec3<f32> = (((vec4<f32>(toLightView_1, 0.0f)) * (mat4x4<f32>(gi_0.viewToWorld_0.data_0[i32(0)][i32(0)], gi_0.viewToWorld_0.data_0[i32(1)][i32(0)], gi_0.viewToWorld_0.data_0[i32(2)][i32(0)], gi_0.viewToWorld_0.data_0[i32(3)][i32(0)], gi_0.viewToWorld_0.data_0[i32(0)][i32(1)], gi_0.viewToWorld_0.data_0[i32(1)][i32(1)], gi_0.viewToWorld_0.data_0[i32(2)][i32(1)], gi_0.viewToWorld_0.data_0[i32(3)][i32(1)], gi_0.viewToWorld_0.data_0[i32(0)][i32(2)], gi_0.viewToWorld_0.data_0[i32(1)][i32(2)], gi_0.viewToWorld_0.data_0[i32(2)][i32(2)], gi_0.viewToWorld_0.data_0[i32(3)][i32(2)], gi_0.viewToWorld_0.data_0[i32(0)][i32(3)], gi_0.viewToWorld_0.data_0[i32(1)][i32(3)], gi_0.viewToWorld_0.data_0[i32(2)][i32(3)], gi_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz;
    var centreDir_0 : vec3<f32> = toCentre_0 * vec3<f32>(rsqrt_0(max(distanceSq_1, 9.99999993922529029e-09f)));
    var ratio_1 : f32 = distanceSq_1 / max(_S117, 0.00009999999747379f);
    var window_2 : f32 = clamp(1.0f - ratio_1 * ratio_1, 0.0f, 1.0f);
    var window_3 : f32 = window_2 * window_2;
    var facing_1 : vec4<f32> = lamps_0.direction_0[i_5];
    var _S118 : vec3<f32> = lamps_0.direction_0[i_5].xyz;
    var window_4 : f32;
    if((dot(_S118, _S118)) > 0.25f)
    {
        window_4 = window_3 * max(dot(normalize((((vec4<f32>(_S118, 0.0f)) * (mat4x4<f32>(gi_0.viewToWorld_0.data_0[i32(0)][i32(0)], gi_0.viewToWorld_0.data_0[i32(1)][i32(0)], gi_0.viewToWorld_0.data_0[i32(2)][i32(0)], gi_0.viewToWorld_0.data_0[i32(3)][i32(0)], gi_0.viewToWorld_0.data_0[i32(0)][i32(1)], gi_0.viewToWorld_0.data_0[i32(1)][i32(1)], gi_0.viewToWorld_0.data_0[i32(2)][i32(1)], gi_0.viewToWorld_0.data_0[i32(3)][i32(1)], gi_0.viewToWorld_0.data_0[i32(0)][i32(2)], gi_0.viewToWorld_0.data_0[i32(1)][i32(2)], gi_0.viewToWorld_0.data_0[i32(2)][i32(2)], gi_0.viewToWorld_0.data_0[i32(3)][i32(2)], gi_0.viewToWorld_0.data_0[i32(0)][i32(3)], gi_0.viewToWorld_0.data_0[i32(1)][i32(3)], gi_0.viewToWorld_0.data_0[i32(2)][i32(3)], gi_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz), (vec3<f32>(0) - centreDir_0)), 0.0f);
    }
    else
    {
        window_4 = window_3;
    }
    if(window_4 <= 0.0f)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var colour_2 : vec3<f32> = lamps_0.colourIntensity_0[i_5].xyz * vec3<f32>(lamps_0.colourIntensity_0[i_5].w) * vec3<f32>(window_4);
    var source_1 : f32 = clamp(facing_1.w, 0.05000000074505806f, 1.0f);
    var rect_1 : vec4<f32> = lamps_0.rect_0[i_5];
    var halfAxis_0 : vec3<f32> = (((vec4<f32>(lamps_0.rect_0[i_5].xyz, 0.0f)) * (mat4x4<f32>(gi_0.viewToWorld_0.data_0[i32(0)][i32(0)], gi_0.viewToWorld_0.data_0[i32(1)][i32(0)], gi_0.viewToWorld_0.data_0[i32(2)][i32(0)], gi_0.viewToWorld_0.data_0[i32(3)][i32(0)], gi_0.viewToWorld_0.data_0[i32(0)][i32(1)], gi_0.viewToWorld_0.data_0[i32(1)][i32(1)], gi_0.viewToWorld_0.data_0[i32(2)][i32(1)], gi_0.viewToWorld_0.data_0[i32(3)][i32(1)], gi_0.viewToWorld_0.data_0[i32(0)][i32(2)], gi_0.viewToWorld_0.data_0[i32(1)][i32(2)], gi_0.viewToWorld_0.data_0[i32(2)][i32(2)], gi_0.viewToWorld_0.data_0[i32(3)][i32(2)], gi_0.viewToWorld_0.data_0[i32(0)][i32(3)], gi_0.viewToWorld_0.data_0[i32(1)][i32(3)], gi_0.viewToWorld_0.data_0[i32(2)][i32(3)], gi_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz;
    var irradiance_0 : f32;
    var thickness_0 : f32;
    var toSpecular_0 : vec3<f32>;
    if((dot(halfAxis_0, halfAxis_0)) > 0.00009999999747379f)
    {
        var lineStart_1 : vec3<f32> = toCentre_0 - halfAxis_0;
        var lineDelta_1 : vec3<f32> = halfAxis_0 * vec3<f32>(2.0f);
        var _S119 : f32 = LineIrradiance_0(normal_8, lineStart_1, lineDelta_1);
        var reflected_1 : vec3<f32> = reflect((vec3<f32>(0) - toEye_2), normal_8);
        var along_0 : f32 = dot(reflected_1, lineDelta_1);
        var denominator_0 : f32 = dot(lineDelta_1, lineDelta_1) - along_0 * along_0;
        if(denominator_0 > 9.99999997475242708e-07f)
        {
            irradiance_0 = (dot(reflected_1, lineStart_1) * along_0 - dot(lineStart_1, lineDelta_1)) / denominator_0;
        }
        else
        {
            irradiance_0 = 0.5f;
        }
        var _S120 : f32 = clamp(rect_1.w, 0.01999999955296516f, source_1);
        toSpecular_0 = lineStart_1 + lineDelta_1 * vec3<f32>(saturate(irradiance_0));
        irradiance_0 = _S119;
        thickness_0 = _S120;
    }
    else
    {
        var _S121 : f32 = saturate(dot(normal_8, centreDir_0)) / max(distanceSq_1, source_1 * source_1);
        toSpecular_0 = toCentre_0;
        irradiance_0 = _S121;
        thickness_0 = source_1;
    }
    var _S122 : f32 = max(dot(toSpecular_0, toSpecular_0), 9.99999997475242708e-07f);
    var specularDir_0 : vec3<f32> = toSpecular_0 * vec3<f32>(rsqrt_0(_S122));
    (*diffuseWeight_0) = dot(colour_2, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f)) * irradiance_0;
    var _S123 : bool;
    if(irradiance_0 <= 0.0f)
    {
        _S123 = (dot(normal_8, specularDir_0)) <= 0.0f;
    }
    else
    {
        _S123 = false;
    }
    if(_S123)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var _S124 : f32 = max(roughness_4, 0.01999999955296516f);
    var alpha_0 : f32 = _S124 * _S124;
    var widened_0 : f32 = saturate(alpha_0 + thickness_0 / (2.0f * sqrt(_S122)));
    var normalisation_0 : f32 = alpha_0 / widened_0 * (alpha_0 / widened_0);
    var lobeDiffuse_0 : vec3<f32>;
    var lobeSpecular_0 : vec3<f32>;
    EvaluateSurface_0(albedo_1, metallic_1, specularLevel_0, sqrt(widened_0), normal_8, toEye_2, specularDir_0, &(lobeDiffuse_0), &(lobeSpecular_0));
    return colour_2 * (albedo_1 * vec3<f32>((1.0f - metallic_1)) * vec3<f32>(irradiance_0) + lobeSpecular_0 * vec3<f32>(normalisation_0) * vec3<f32>((1.0f / max(_S122, thickness_0 * thickness_0))) * vec3<f32>(3.14159274101257324f));
}

fn AoMultiBounce_0( albedo_2 : vec3<f32>,  visibility_2 : f32) -> vec3<f32>
{
    var _S125 : vec3<f32> = vec3<f32>(visibility_2);
    return max(vec3<f32>(visibility_2, visibility_2, visibility_2), ((_S125 * (vec3<f32>(2.04040002822875977f) * albedo_2 - vec3<f32>(0.33239999413490295f, 0.33239999413490295f, 0.33239999413490295f)) + (vec3<f32>(0.64170002937316895f, 0.64170002937316895f, 0.64170002937316895f) - vec3<f32>(4.79510021209716797f) * albedo_2)) * _S125 + (vec3<f32>(2.75519990921020508f) * albedo_2 + vec3<f32>(0.69029998779296875f, 0.69029998779296875f, 0.69029998779296875f))) * _S125);
}

fn SplitSumApprox_0( roughness_5 : f32,  cosNV_1 : f32) -> vec2<f32>
{
    var smoothness_0 : f32 = 1.0f - roughness_5;
    var _S126 : f32 = 1.03999996185302734f * (min(smoothness_0 * smoothness_0, exp2(-9.27999973297119141f * cosNV_1)) * smoothness_0 + 0.04250000044703484f - 0.02749999985098839f * roughness_5);
    return vec2<f32>(1.03999996185302734f - 0.57200002670288086f * roughness_5 - _S126, 0.02199999988079071f * roughness_5 - 0.03999999910593033f + _S126);
}

fn SpecularOcclusion_0( noV_0 : f32,  alpha_1 : f32,  visibility_3 : f32) -> f32
{
    return clamp(pow(noV_0 + visibility_3, alpha_1) - 1.0f + visibility_3, 0.0f, 1.0f);
}

fn RayleighPhase_0( cosTheta_0 : f32) -> f32
{
    return 0.05968310311436653f * (1.0f + cosTheta_0 * cosTheta_0);
}

fn MiePhase_0( g_2 : f32,  cosTheta_1 : f32) -> f32
{
    var _S127 : f32 = g_2 * g_2;
    var denominator_1 : f32 = 1.0f + _S127 + 2.0f * g_2 * cosTheta_1;
    return (1.0f - _S127) / (12.56637096405029297f * denominator_1 * sqrt(max(denominator_1, 9.99999993922529029e-09f)));
}

fn AerialPerspective_0( colour_3 : vec3<f32>,  worldRay_0 : vec3<f32>,  sunDirection_0 : vec3<f32>,  sunIlluminance_0 : vec3<f32>,  distanceKm_0 : f32) -> vec3<f32>
{
    var extinction_0 : vec3<f32> = vec3<f32>(0.00580199994146824f, 0.01355800032615662f, 0.03310000151395798f) + vec3<f32>(0.00444000028073788f, 0.00444000028073788f, 0.00444000028073788f);
    var transmittance_1 : vec3<f32> = exp((vec3<f32>(0) - extinction_0) * vec3<f32>(distanceKm_0));
    var cosTheta_2 : f32 = dot(sunDirection_0, worldRay_0);
    var _S128 : f32 = 0.00399600015953183f * MiePhase_0(0.80000001192092896f, - cosTheta_2);
    return colour_3 * transmittance_1 + (vec3<f32>(0.00580199994146824f, 0.01355800032615662f, 0.03310000151395798f) * vec3<f32>(RayleighPhase_0(cosTheta_2)) + vec3<f32>(_S128, _S128, _S128)) * sunIlluminance_0 * ((vec3<f32>(1.0f) - transmittance_1) / max(extinction_0, vec3<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f, 9.99999997475242708e-07f)));
}

fn SkyDiffuseIrradiance_0( worldNormal_2 : vec3<f32>) -> vec3<f32>
{
    var n_2 : vec4<f32> = vec4<f32>(worldNormal_2, 1.0f);
    const _S129 : vec3<i32> = vec3<i32>(i32(0), i32(0), i32(0));
    const _S130 : vec3<i32> = vec3<i32>(i32(1), i32(0), i32(0));
    const _S131 : vec3<i32> = vec3<i32>(i32(2), i32(0), i32(0));
    var q_0 : vec4<f32> = n_2.xyzz * n_2.yzzx;
    const _S132 : vec3<i32> = vec3<i32>(i32(3), i32(0), i32(0));
    const _S133 : vec3<i32> = vec3<i32>(i32(4), i32(0), i32(0));
    const _S134 : vec3<i32> = vec3<i32>(i32(5), i32(0), i32(0));
    const _S135 : vec3<i32> = vec3<i32>(i32(6), i32(0), i32(0));
    var _S136 : f32 = n_2.x;
    var _S137 : f32 = n_2.y;
    return max(vec3<f32>(0.0f, 0.0f, 0.0f), vec3<f32>(dot((textureLoad((uSkyIrradiance_0), ((_S129)).xy, ((_S129)).z)), n_2), dot((textureLoad((uSkyIrradiance_0), ((_S130)).xy, ((_S130)).z)), n_2), dot((textureLoad((uSkyIrradiance_0), ((_S131)).xy, ((_S131)).z)), n_2)) + vec3<f32>(dot((textureLoad((uSkyIrradiance_0), ((_S132)).xy, ((_S132)).z)), q_0), dot((textureLoad((uSkyIrradiance_0), ((_S133)).xy, ((_S133)).z)), q_0), dot((textureLoad((uSkyIrradiance_0), ((_S134)).xy, ((_S134)).z)), q_0)) + (textureLoad((uSkyIrradiance_0), ((_S135)).xy, ((_S135)).z)).xyz * vec3<f32>((_S136 * _S136 - _S137 * _S137)));
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) uv_15 : vec2<f32>,
};

@fragment
fn fragmentMain( _S138 : pixelInput_0, @builtin(position) position_6 : vec4<f32>) -> pixelOutput_0
{
    var uvPixelWidth_0 : f32 = (fwidth((_S138.uv_15.x)));
    var baseColour_1 : vec4<f32> = LoadGBuffer_0(uBaseColour_0, position_6);
    var albedo_3 : vec3<f32> = baseColour_1.xyz;
    var material_3 : vec4<f32> = LoadGBuffer_0(uMaterial_0, position_6);
    var baked_0 : vec3<f32> = LoadGBuffer_0(uBaked_0, position_6).xyz;
    var depth_3 : f32 = LoadGBuffer_1(uDepth_0, position_6).x;
    var viewHeightKm_1 : f32 = gi_0.giParams_0.w;
    var debugView_0 : i32 = i32(light_0.ambient_0.w + 0.5f);
    var _S139 : bool;
    var background_0 : vec3<f32>;
    if(depth_3 <= 0.0f)
    {
        if(debugView_0 != i32(0))
        {
            _S139 = debugView_0 != i32(11);
        }
        else
        {
            _S139 = false;
        }
        if(_S139)
        {
            var _S140 : pixelOutput_0 = pixelOutput_0( vec4<f32>(0.0f, 0.0f, 0.0f, 1.0f) );
            return _S140;
        }
        if((light_0.sky_0.x) > 0.5f)
        {
            var _S141 : pixelOutput_0 = pixelOutput_0( vec4<f32>(ApplyFog_0(albedo_3 * vec3<f32>(DecodeEmissiveStrength_0(material_3)), _S138.uv_15, gi_0.unproject_0.w), 1.0f) );
            return _S141;
        }
        var ndc_2 : vec2<f32> = _S138.uv_15 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
        var worldRay_1 : vec3<f32> = normalize((((vec4<f32>(normalize(vec3<f32>(ndc_2.x / gi_0.unproject_0.x, - ndc_2.y / gi_0.unproject_0.y, 1.0f)), 0.0f)) * (mat4x4<f32>(gi_0.viewToWorld_0.data_0[i32(0)][i32(0)], gi_0.viewToWorld_0.data_0[i32(1)][i32(0)], gi_0.viewToWorld_0.data_0[i32(2)][i32(0)], gi_0.viewToWorld_0.data_0[i32(3)][i32(0)], gi_0.viewToWorld_0.data_0[i32(0)][i32(1)], gi_0.viewToWorld_0.data_0[i32(1)][i32(1)], gi_0.viewToWorld_0.data_0[i32(2)][i32(1)], gi_0.viewToWorld_0.data_0[i32(3)][i32(1)], gi_0.viewToWorld_0.data_0[i32(0)][i32(2)], gi_0.viewToWorld_0.data_0[i32(1)][i32(2)], gi_0.viewToWorld_0.data_0[i32(2)][i32(2)], gi_0.viewToWorld_0.data_0[i32(3)][i32(2)], gi_0.viewToWorld_0.data_0[i32(0)][i32(3)], gi_0.viewToWorld_0.data_0[i32(1)][i32(3)], gi_0.viewToWorld_0.data_0[i32(2)][i32(3)], gi_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz);
        var background_1 : vec3<f32> = SampleSkyBackground_0(uSkyView_0, uSkyViewSampler_0, worldRay_1, viewHeightKm_1);
        if((light_0.stars_0.x) > 0.0f)
        {
            _S139 = true;
        }
        else
        {
            _S139 = (light_0.moon_0.w) > 0.0f;
        }
        if(_S139)
        {
            background_0 = background_1 + NightSkySources_0(worldRay_1, normalize(light_0.moon_0.xyz), light_0.moon_0.w, light_0.stars_0.x, light_0.stars_0.y, light_0.stars_0.z, 2.0f * uvPixelWidth_0 / max(gi_0.unproject_0.x, 0.00009999999747379f), SunTransmittance_0(worldRay_1, viewHeightKm_1));
        }
        else
        {
            background_0 = background_1;
        }
        var _S142 : pixelOutput_0 = pixelOutput_0( vec4<f32>(ApplyFog_0(background_0, _S138.uv_15, gi_0.unproject_0.w), 1.0f) );
        return _S142;
    }
    var _S143 : vec4<f32>;
    if((DecodeShadingModel_0(material_3)) == i32(0))
    {
        if(debugView_0 != i32(0))
        {
            _S139 = debugView_0 != i32(11);
        }
        else
        {
            _S139 = false;
        }
        if(_S139)
        {
            if(debugView_0 == i32(1))
            {
                _S143 = vec4<f32>(albedo_3, 1.0f);
            }
            else
            {
                _S143 = vec4<f32>(0.0f, 0.0f, 0.0f, 1.0f);
            }
            var _S144 : pixelOutput_0 = pixelOutput_0( _S143 );
            return _S144;
        }
        var _S145 : pixelOutput_0 = pixelOutput_0( vec4<f32>(ApplyFog_0(albedo_3 * vec3<f32>(DecodeEmissiveStrength_0(material_3)), _S138.uv_15, ViewPositionOf_0(_S138.uv_15, depth_3).z), 1.0f) );
        return _S145;
    }
    var normal_9 : vec3<f32> = DecodeGBufferNormal_0(LoadGBuffer_0(uNormal_0, position_6));
    var scale_1 : f32 = light_0.sunColourScale_0.w;
    var model_0 : i32 = i32(light_0.sunDirectionModel_0.w + 0.5f);
    if(debugView_0 == i32(1))
    {
        var _S146 : pixelOutput_0 = pixelOutput_0( vec4<f32>(albedo_3, 1.0f) );
        return _S146;
    }
    if(debugView_0 == i32(2))
    {
        var _S147 : vec3<f32> = vec3<f32>(0.5f);
        var _S148 : pixelOutput_0 = pixelOutput_0( vec4<f32>(normal_9 * _S147 + _S147, 1.0f) );
        return _S148;
    }
    if(debugView_0 == i32(3))
    {
        var ramp_0 : f32 = pow(clamp(ViewPositionOf_0(_S138.uv_15, depth_3).z / 400.0f, 0.0f, 1.0f), 0.4544999897480011f);
        var _S149 : pixelOutput_0 = pixelOutput_0( vec4<f32>(ramp_0, ramp_0, ramp_0, 1.0f) );
        return _S149;
    }
    if(debugView_0 == i32(4))
    {
        var _S150 : pixelOutput_0 = pixelOutput_0( vec4<f32>(material_3.z, material_3.x, material_3.w, 1.0f) );
        return _S150;
    }
    if(debugView_0 == i32(6))
    {
        var _S151 : pixelOutput_0 = pixelOutput_0( vec4<f32>(baked_0, 1.0f) );
        return _S151;
    }
    if(debugView_0 == i32(7))
    {
        var ao_0 : f32 = (textureSampleLevel((uOcclusion_0), (uOcclusionSampler_0), (_S138.uv_15), (0.0f))).x;
        var _S152 : pixelOutput_0 = pixelOutput_0( vec4<f32>(ao_0, ao_0, ao_0, 1.0f) );
        return _S152;
    }
    if(debugView_0 == i32(8))
    {
        var _S153 : pixelOutput_0 = pixelOutput_0( vec4<f32>((textureSampleLevel((uScreenGi_0), (uScreenGiSampler_0), (_S138.uv_15), (0.0f))).xyz * vec3<f32>(gi_0.giParams_0.x), 1.0f) );
        return _S153;
    }
    if(debugView_0 == i32(18))
    {
        var _S154 : f32 = material_3.z;
        var _S155 : pixelOutput_0 = pixelOutput_0( vec4<f32>(_S154, _S154, _S154, 1.0f) );
        return _S155;
    }
    if(debugView_0 == i32(19))
    {
        var _S156 : f32 = material_3.x;
        var _S157 : pixelOutput_0 = pixelOutput_0( vec4<f32>(_S156, _S156, _S156, 1.0f) );
        return _S157;
    }
    if(debugView_0 == i32(20))
    {
        var _S158 : f32 = baseColour_1.w;
        var _S159 : pixelOutput_0 = pixelOutput_0( vec4<f32>(_S158, _S158, _S158, 1.0f) );
        return _S159;
    }
    if(debugView_0 == i32(11))
    {
        albedo_3 = vec3<f32>(0.18000000715255737f, 0.18000000715255737f, 0.18000000715255737f);
    }
    if(debugView_0 == i32(28))
    {
        var torch_0 : f32 = FlashlightShadowAt_0(position_6);
        var _S160 : pixelOutput_0 = pixelOutput_0( vec4<f32>(torch_0, torch_0, torch_0, 1.0f) );
        return _S160;
    }
    var viewPos_5 : vec3<f32> = ViewPositionOf_0(_S138.uv_15, depth_3);
    var flashDir_0 : vec3<f32>;
    var flashIrradiance_0 : vec3<f32> = FlashlightReaching_0(viewPos_5, normal_9, position_6, &(flashDir_0));
    var flashDiffuse_0 : vec3<f32> = albedo_3 * flashIrradiance_0 * vec3<f32>(max(dot(normal_9, flashDir_0), 0.0f));
    if(model_0 == i32(0))
    {
        var _S161 : pixelOutput_0 = pixelOutput_0( vec4<f32>(ApplyFog_0(albedo_3 * baked_0 + flashDiffuse_0, _S138.uv_15, viewPos_5.z), 1.0f) );
        return _S161;
    }
    var sunDirection_1 : vec3<f32> = normalize((((vec4<f32>(light_0.sunDirectionModel_0.xyz, 0.0f)) * (mat4x4<f32>(gi_0.viewToWorld_0.data_0[i32(0)][i32(0)], gi_0.viewToWorld_0.data_0[i32(1)][i32(0)], gi_0.viewToWorld_0.data_0[i32(2)][i32(0)], gi_0.viewToWorld_0.data_0[i32(3)][i32(0)], gi_0.viewToWorld_0.data_0[i32(0)][i32(1)], gi_0.viewToWorld_0.data_0[i32(1)][i32(1)], gi_0.viewToWorld_0.data_0[i32(2)][i32(1)], gi_0.viewToWorld_0.data_0[i32(3)][i32(1)], gi_0.viewToWorld_0.data_0[i32(0)][i32(2)], gi_0.viewToWorld_0.data_0[i32(1)][i32(2)], gi_0.viewToWorld_0.data_0[i32(2)][i32(2)], gi_0.viewToWorld_0.data_0[i32(3)][i32(2)], gi_0.viewToWorld_0.data_0[i32(0)][i32(3)], gi_0.viewToWorld_0.data_0[i32(1)][i32(3)], gi_0.viewToWorld_0.data_0[i32(2)][i32(3)], gi_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz);
    var _S162 : f32 = max(dot(normal_9, sunDirection_1), 0.0f);
    if(_S162 > 0.0f)
    {
        _S139 = true;
    }
    else
    {
        _S139 = debugView_0 == i32(12);
    }
    var visibility_4 : f32;
    if(_S139)
    {
        if((gi_0.probeMisc_0.w) > 0.5f)
        {
            visibility_4 = (textureSampleLevel((uSunShadow_0), (uSunShadowSampler_0), (_S138.uv_15), (0.0f))).x;
        }
        else
        {
            var _S163 : vec2<f32> = position_6.xy;
            var visibility_5 : f32 = SunVisibility_0(viewPos_5, normal_9, _S163);
            if(visibility_5 > 0.0f)
            {
                _S139 = (gi_0.shadowConfig_0.x) > 0.5f;
            }
            else
            {
                _S139 = false;
            }
            if(_S139)
            {
                visibility_4 = visibility_5 * ContactShadow_0(viewPos_5, (((mat4x4<f32>(gi_0.viewToWorld_0.data_0[i32(0)][i32(0)], gi_0.viewToWorld_0.data_0[i32(1)][i32(0)], gi_0.viewToWorld_0.data_0[i32(2)][i32(0)], gi_0.viewToWorld_0.data_0[i32(3)][i32(0)], gi_0.viewToWorld_0.data_0[i32(0)][i32(1)], gi_0.viewToWorld_0.data_0[i32(1)][i32(1)], gi_0.viewToWorld_0.data_0[i32(2)][i32(1)], gi_0.viewToWorld_0.data_0[i32(3)][i32(1)], gi_0.viewToWorld_0.data_0[i32(0)][i32(2)], gi_0.viewToWorld_0.data_0[i32(1)][i32(2)], gi_0.viewToWorld_0.data_0[i32(2)][i32(2)], gi_0.viewToWorld_0.data_0[i32(3)][i32(2)], gi_0.viewToWorld_0.data_0[i32(0)][i32(3)], gi_0.viewToWorld_0.data_0[i32(1)][i32(3)], gi_0.viewToWorld_0.data_0[i32(2)][i32(3)], gi_0.viewToWorld_0.data_0[i32(3)][i32(3)])) * (vec4<f32>(sunDirection_1, 0.0f)))).xyz, gi_0.camera_0.w, _S163);
            }
            else
            {
                visibility_4 = visibility_5;
            }
        }
    }
    else
    {
        visibility_4 = 1.0f;
    }
    if(debugView_0 == i32(12))
    {
        var _S164 : pixelOutput_0 = pixelOutput_0( vec4<f32>(visibility_4, visibility_4, visibility_4, 1.0f) );
        return _S164;
    }
    if(model_0 == i32(1))
    {
        if((light_0.sky_0.z) > 0.5f)
        {
            background_0 = vec3<f32>(0.0f, 0.0f, 0.0f);
        }
        else
        {
            background_0 = light_0.sunColourScale_0.xyz * vec3<f32>(_S162) * vec3<f32>(visibility_4);
        }
        var _S165 : pixelOutput_0 = pixelOutput_0( vec4<f32>(ApplyFog_0(albedo_3 * (baked_0 + background_0) * vec3<f32>(scale_1) + flashDiffuse_0, _S138.uv_15, viewPos_5.z), 1.0f) );
        return _S165;
    }
    var metallic_2 : f32 = material_3.x;
    var specularLevel_1 : f32 = material_3.y;
    var roughness_6 : f32 = material_3.z;
    var worldView_0 : vec3<f32> = normalize((vec3<f32>(0) - (((vec4<f32>(viewPos_5, 0.0f)) * (mat4x4<f32>(gi_0.viewToWorld_0.data_0[i32(0)][i32(0)], gi_0.viewToWorld_0.data_0[i32(1)][i32(0)], gi_0.viewToWorld_0.data_0[i32(2)][i32(0)], gi_0.viewToWorld_0.data_0[i32(3)][i32(0)], gi_0.viewToWorld_0.data_0[i32(0)][i32(1)], gi_0.viewToWorld_0.data_0[i32(1)][i32(1)], gi_0.viewToWorld_0.data_0[i32(2)][i32(1)], gi_0.viewToWorld_0.data_0[i32(3)][i32(1)], gi_0.viewToWorld_0.data_0[i32(0)][i32(2)], gi_0.viewToWorld_0.data_0[i32(1)][i32(2)], gi_0.viewToWorld_0.data_0[i32(2)][i32(2)], gi_0.viewToWorld_0.data_0[i32(3)][i32(2)], gi_0.viewToWorld_0.data_0[i32(0)][i32(3)], gi_0.viewToWorld_0.data_0[i32(1)][i32(3)], gi_0.viewToWorld_0.data_0[i32(2)][i32(3)], gi_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz));
    var worldRayToPixel_0 : vec3<f32> = (vec3<f32>(0) - worldView_0);
    var waterMask_1 : f32 = 0.0f;
    var waterReflectance_1 : f32 = 0.0f;
    if((gi_0.wetness0_0.x) > 0.0f)
    {
        _S139 = !DecodeMatte_0(material_3);
    }
    else
    {
        _S139 = false;
    }
    var specularLevel_2 : f32;
    if(_S139)
    {
        ApplyWetness_0((((vec4<f32>(viewPos_5, 1.0f)) * (mat4x4<f32>(gi_0.viewToWorld_0.data_0[i32(0)][i32(0)], gi_0.viewToWorld_0.data_0[i32(1)][i32(0)], gi_0.viewToWorld_0.data_0[i32(2)][i32(0)], gi_0.viewToWorld_0.data_0[i32(3)][i32(0)], gi_0.viewToWorld_0.data_0[i32(0)][i32(1)], gi_0.viewToWorld_0.data_0[i32(1)][i32(1)], gi_0.viewToWorld_0.data_0[i32(2)][i32(1)], gi_0.viewToWorld_0.data_0[i32(3)][i32(1)], gi_0.viewToWorld_0.data_0[i32(0)][i32(2)], gi_0.viewToWorld_0.data_0[i32(1)][i32(2)], gi_0.viewToWorld_0.data_0[i32(2)][i32(2)], gi_0.viewToWorld_0.data_0[i32(3)][i32(2)], gi_0.viewToWorld_0.data_0[i32(0)][i32(3)], gi_0.viewToWorld_0.data_0[i32(1)][i32(3)], gi_0.viewToWorld_0.data_0[i32(2)][i32(3)], gi_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz, worldView_0, WetFootprint_0(uvPixelWidth_0 * 2.0f, viewPos_5.z, gi_0.unproject_0.x, max(dot(normal_9, worldView_0), 0.00100000004749745f)), &(albedo_3), &(normal_9), &(roughness_6), &(waterMask_1), &(waterReflectance_1));
        specularLevel_2 = max(specularLevel_1, 0.5f * waterMask_1);
    }
    else
    {
        specularLevel_2 = specularLevel_1;
    }
    if(debugView_0 == i32(13))
    {
        var _S166 : vec2<f32> = vec2<f32>(0.5f);
        var _S167 : pixelOutput_0 = pixelOutput_0( vec4<f32>(waterMask_1, normal_9.xy * _S166 + _S166, 1.0f) );
        return _S167;
    }
    var _S168 : vec3<f32> = vec3<f32>(scale_1);
    var sunIlluminance_1 : vec3<f32> = light_0.sunColourScale_0.xyz * _S168;
    var surfaceDiffuse_0 : vec3<f32>;
    var surfaceSpecular_0 : vec3<f32>;
    EvaluateSurface_0(albedo_3, metallic_2, specularLevel_2, roughness_6, normal_9, worldView_0, sunDirection_1, &(surfaceDiffuse_0), &(surfaceSpecular_0));
    var sunLight_0 : vec3<f32> = (surfaceDiffuse_0 + surfaceSpecular_0) * sunIlluminance_1 * vec3<f32>(visibility_4);
    if(waterReflectance_1 > 0.0f)
    {
        var reflected_2 : vec3<f32> = normalize(reflect(worldRayToPixel_0, normal_9));
        background_0 = sunLight_0 + SampleSkyView_0(uSkyView_0, uSkyViewSampler_0, reflected_2, viewHeightKm_1) * vec3<f32>(waterReflectance_1) * vec3<f32>(WaterSkyward_0(reflected_2));
    }
    else
    {
        background_0 = sunLight_0;
    }
    var _S169 : i32 = i32(lamps_0.control_1.x + 0.5f);
    var _S170 : f32 = viewPos_5.z;
    var lampShadow_0 : vec3<f32> = UpsampleLampVisibility_0(_S138.uv_15, _S170);
    var lampShadows_0 : bool = (lamps_0.control_1.y) > 0.5f;
    var lampOcclusion_0 : f32;
    if(lampShadows_0)
    {
        lampOcclusion_0 = clamp(lampShadow_0.x, 0.0f, 1.0f);
    }
    else
    {
        lampOcclusion_0 = 1.0f;
    }
    if(debugView_0 == i32(29))
    {
        var _S171 : f32 = saturate(lampShadow_0.x);
        var _S172 : f32 = lampShadow_0.y;
        if(_S172 > 0.00400000018998981f)
        {
            visibility_4 = saturate(_S172);
        }
        else
        {
            visibility_4 = 0.0f;
        }
        var _S173 : pixelOutput_0 = pixelOutput_0( vec4<f32>(_S171, visibility_4, 0.0f, 1.0f) );
        return _S173;
    }
    if(debugView_0 == i32(26))
    {
        if(lampShadows_0)
        {
            _S139 = (lampShadow_0.y) <= 0.0f;
        }
        else
        {
            _S139 = false;
        }
        if(_S139)
        {
            _S143 = vec4<f32>(0.60000002384185791f, 0.0f, 0.0f, 1.0f);
        }
        else
        {
            _S143 = vec4<f32>(lampOcclusion_0, lampOcclusion_0, lampOcclusion_0, 1.0f);
        }
        var _S174 : pixelOutput_0 = pixelOutput_0( _S143 );
        return _S174;
    }
    const _S175 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var i_6 : i32 = i32(0);
    var lampLight_0 : vec3<f32> = _S175;
    for(;;)
    {
        if(i_6 < _S169)
        {
        }
        else
        {
            break;
        }
        var lampWeight_0 : f32;
        var lampTerm_0 : vec3<f32> = LampShade_0(i_6, viewPos_5, normal_9, worldView_0, albedo_3, metallic_2, specularLevel_2, roughness_6, &(lampWeight_0)) * vec3<f32>(lampOcclusion_0);
        if((all((lampTerm_0 <= _S175))))
        {
            i_6 = i_6 + i32(1);
            continue;
        }
        var lampLight_1 : vec3<f32> = lampLight_0 + lampTerm_0;
        background_0 = background_0 + lampTerm_0;
        lampLight_0 = lampLight_1;
        i_6 = i_6 + i32(1);
    }
    if(debugView_0 == i32(14))
    {
        var _S176 : pixelOutput_0 = pixelOutput_0( vec4<f32>(lampLight_0, 1.0f) );
        return _S176;
    }
    var flashLight_0 : vec3<f32>;
    if((any((flashIrradiance_0 > _S175))))
    {
        var flashDiffuseLobe_0 : vec3<f32>;
        var flashSpecularLobe_0 : vec3<f32>;
        EvaluateSurface_0(albedo_3, metallic_2, specularLevel_2, roughness_6, normal_9, worldView_0, flashDir_0, &(flashDiffuseLobe_0), &(flashSpecularLobe_0));
        var flashLight_1 : vec3<f32> = (flashDiffuseLobe_0 + flashSpecularLobe_0) * flashIrradiance_0 * vec3<f32>(3.14159274101257324f);
        var sunLight_1 : vec3<f32> = background_0 + flashLight_1;
        flashLight_0 = flashLight_1;
        background_0 = sunLight_1;
    }
    else
    {
        flashLight_0 = _S175;
    }
    if(debugView_0 == i32(27))
    {
        var _S177 : pixelOutput_0 = pixelOutput_0( vec4<f32>(flashLight_0, 1.0f) );
        return _S177;
    }
    var _S178 : vec3<f32> = vec3<f32>(metallic_2);
    var diffuseColour_0 : vec3<f32> = albedo_3 - albedo_3 * _S178;
    var gathered_0 : bool = (gi_0.giParams_0.x) > 0.0f;
    var _S179 : vec3<f32> = light_0.ambient_0.xyz;
    var _S180 : bool = !gathered_0;
    if(_S180)
    {
        _S139 = true;
    }
    else
    {
        _S139 = debugView_0 == i32(9);
    }
    if(_S139)
    {
        _S139 = (gi_0.probeOrigin_0.w) > 0.5f;
    }
    else
    {
        _S139 = false;
    }
    var ambientVisibility_0 : f32;
    var indirect_0 : vec3<f32>;
    if(_S139)
    {
        var worldPos_0 : vec3<f32> = (((vec4<f32>(viewPos_5, 1.0f)) * (mat4x4<f32>(gi_0.viewToWorld_0.data_0[i32(0)][i32(0)], gi_0.viewToWorld_0.data_0[i32(1)][i32(0)], gi_0.viewToWorld_0.data_0[i32(2)][i32(0)], gi_0.viewToWorld_0.data_0[i32(3)][i32(0)], gi_0.viewToWorld_0.data_0[i32(0)][i32(1)], gi_0.viewToWorld_0.data_0[i32(1)][i32(1)], gi_0.viewToWorld_0.data_0[i32(2)][i32(1)], gi_0.viewToWorld_0.data_0[i32(3)][i32(1)], gi_0.viewToWorld_0.data_0[i32(0)][i32(2)], gi_0.viewToWorld_0.data_0[i32(1)][i32(2)], gi_0.viewToWorld_0.data_0[i32(2)][i32(2)], gi_0.viewToWorld_0.data_0[i32(3)][i32(2)], gi_0.viewToWorld_0.data_0[i32(0)][i32(3)], gi_0.viewToWorld_0.data_0[i32(1)][i32(3)], gi_0.viewToWorld_0.data_0[i32(2)][i32(3)], gi_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + gi_0.camera_0.xyz;
        var worldNormal_3 : vec3<f32> = normal_9;
        var grid_3 : vec3<f32> = (worldPos_0 + normal_9 * vec3<f32>(gi_0.probeMisc_0.x) - gi_0.probeOrigin_0.xyz) / vec3<f32>(gi_0.probeInvCount_0.w);
        var uvw_0 : vec3<f32> = (grid_3 + vec3<f32>(0.5f)) * gi_0.probeInvCount_0.xyz;
        if((all((uvw_0 >= _S175))))
        {
            _S139 = (all((uvw_0 <= vec3<f32>(1.0f, 1.0f, 1.0f))));
        }
        else
        {
            _S139 = false;
        }
        if(_S139)
        {
            var sh_0 : vec4<f32> = vec4<f32>(1.0f, worldNormal_3.x, worldNormal_3.y, worldNormal_3.z);
            var probeLight_0 : vec3<f32> = vec3<f32>(max(dot((textureSampleLevel((uProbeRed_0), (uProbeRedSampler_0), (uvw_0), (0.0f))), sh_0), 0.0f), max(dot((textureSampleLevel((uProbeGreen_0), (uProbeGreenSampler_0), (uvw_0), (0.0f))), sh_0), 0.0f), max(dot((textureSampleLevel((uProbeBlue_0), (uProbeBlueSampler_0), (uvw_0), (0.0f))), sh_0), 0.0f));
            if(debugView_0 == i32(9))
            {
                var _S181 : pixelOutput_0 = pixelOutput_0( vec4<f32>(probeLight_0 * gi_0.probeSky_0.xyz, 1.0f) );
                return _S181;
            }
            var moments_0 : vec2<f32> = (textureSampleLevel((uProbeDistance_0), (uProbeDistanceSampler_0), (uvw_0), (0.0f))).xy;
            var _S182 : f32 = moments_0.x;
            var _S183 : f32 = max(moments_0.y - _S182 * _S182, 0.00009999999747379f);
            var toward_0 : f32 = length(worldPos_0 - (gi_0.probeOrigin_0.xyz + round(grid_3) * vec3<f32>(gi_0.probeInvCount_0.w)));
            if(toward_0 > _S182)
            {
                var difference_0 : f32 = toward_0 - _S182;
                var trust_0 : f32 = _S183 / (_S183 + difference_0 * difference_0);
                ambientVisibility_0 = trust_0 * trust_0 * trust_0;
            }
            else
            {
                ambientVisibility_0 = 1.0f;
            }
            indirect_0 = mix(light_0.ambient_0.xyz, probeLight_0 * gi_0.probeSky_0.xyz, vec3<f32>((ambientVisibility_0 * gi_0.probeMisc_0.y)));
        }
        else
        {
            indirect_0 = _S179;
        }
    }
    else
    {
        indirect_0 = _S179;
    }
    if(debugView_0 == i32(9))
    {
        var _S184 : pixelOutput_0 = pixelOutput_0( vec4<f32>(0.0f, 0.0f, 0.0f, 1.0f) );
        return _S184;
    }
    if(gathered_0)
    {
        indirect_0 = (textureSampleLevel((uScreenGi_0), (uScreenGiSampler_0), (_S138.uv_15), (0.0f))).xyz * vec3<f32>(gi_0.giParams_0.x);
    }
    var ambientVisibility_1 : f32 = baseColour_1.w;
    if((gi_0.giParams_0.z) > 0.5f)
    {
        ambientVisibility_0 = ambientVisibility_1 * (textureSampleLevel((uOcclusion_0), (uOcclusionSampler_0), (_S138.uv_15), (0.0f))).x;
    }
    else
    {
        ambientVisibility_0 = ambientVisibility_1;
    }
    var indirect_1 : vec3<f32>;
    if(ambientVisibility_0 < 1.0f)
    {
        var occlusion_0 : vec3<f32> = AoMultiBounce_0(albedo_3, ambientVisibility_0);
        if((gi_0.probeOrigin_0.w) > 0.5f)
        {
            _S139 = _S180;
        }
        else
        {
            _S139 = false;
        }
        if(_S139)
        {
            lampLight_0 = mix(vec3<f32>(1.0f, 1.0f, 1.0f), occlusion_0, vec3<f32>(0.5f));
        }
        else
        {
            lampLight_0 = occlusion_0;
        }
        indirect_1 = indirect_0 * lampLight_0;
    }
    else
    {
        indirect_1 = indirect_0;
    }
    var noVSpecular_0 : f32 = clamp(dot(normal_9, worldView_0), 0.00009999999747379f, 1.0f);
    var _S185 : f32 = 0.07999999821186066f * specularLevel_2;
    var f0_1 : vec3<f32> = mix(vec3<f32>(_S185, _S185, _S185), albedo_3, _S178);
    var environmentBrdf_0 : vec2<f32> = SplitSumApprox_0(roughness_6, noVSpecular_0);
    var _S186 : vec3<f32> = vec3<f32>(0.31830987334251404f);
    var _S187 : f32 = clamp(50.0f * f0_1.y, 0.0f, 1.0f) * environmentBrdf_0.y;
    var ambientSpecular_0 : vec3<f32> = indirect_0 * _S186 * (f0_1 * vec3<f32>(environmentBrdf_0.x) + vec3<f32>(_S187, _S187, _S187)) * vec3<f32>(SpecularOcclusion_0(noVSpecular_0, roughness_6 * roughness_6, ambientVisibility_0));
    var reflectionCeiling_0 : f32 = gi_0.shelterMap2_0.z;
    var ambientSpecular_1 : vec3<f32>;
    if(reflectionCeiling_0 > 0.0f)
    {
        ambientSpecular_1 = ambientSpecular_0 * vec3<f32>((1.0f - clamp((reflectionCeiling_0 - roughness_6) / max(reflectionCeiling_0 * 0.25f, 0.00100000004749745f), 0.0f, 1.0f)));
    }
    else
    {
        ambientSpecular_1 = ambientSpecular_0;
    }
    if(gathered_0)
    {
        var _S188 : pixelOutput_0 = pixelOutput_0( vec4<f32>(ApplyFog_0(AerialPerspective_0(diffuseColour_0 * _S186 * indirect_1 + ambientSpecular_1 + background_0, worldRayToPixel_0, sunDirection_1, sunIlluminance_1, _S170 * 0.00100000004749745f), _S138.uv_15, _S170), 1.0f) );
        return _S188;
    }
    var _S189 : pixelOutput_0 = pixelOutput_0( vec4<f32>(ApplyFog_0(AerialPerspective_0(albedo_3 * indirect_1 * _S168 + SkyDiffuseIrradiance_0(normal_9) * diffuseColour_0 + ambientSpecular_1 + background_0, worldRayToPixel_0, sunDirection_1, sunIlluminance_1, _S170 * 0.00100000004749745f), _S138.uv_15, _S170), 1.0f) );
    return _S189;
}

