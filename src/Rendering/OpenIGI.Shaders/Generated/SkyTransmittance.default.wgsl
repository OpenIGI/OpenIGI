struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) sunDir_0 : vec4<f32>,
    @align(16) sunColour_0 : vec4<f32>,
    @align(16) misc_0 : vec4<f32>,
    @align(16) moonDir_0 : vec4<f32>,
    @align(16) moonColour_0 : vec4<f32>,
};

@binding(1) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uTransmittance_0 : texture_storage_2d<rgba16float, write>;

fn TransmittanceUvToParams_0( uv_0 : vec2<f32>,  viewHeight_0 : ptr<function, f32>,  viewZenithCos_0 : ptr<function, f32>)
{
    var horizon_0 : f32 = sqrt(7.668e+05f);
    var rho_0 : f32 = horizon_0 * uv_0.y;
    var _S1 : f32 = rho_0 * rho_0;
    var _S2 : f32 = sqrt(_S1 + 4.04496e+07f);
    (*viewHeight_0) = _S2;
    var dMin_0 : f32 = 6420.0f - _S2;
    var d_0 : f32 = dMin_0 + uv_0.x * (rho_0 + horizon_0 - dMin_0);
    var _S3 : f32;
    if(d_0 == 0.0f)
    {
        _S3 = 1.0f;
    }
    else
    {
        _S3 = (horizon_0 * horizon_0 - _S1 - d_0 * d_0) / (2.0f * (*viewHeight_0) * d_0);
    }
    (*viewZenithCos_0) = clamp(_S3, -1.0f, 1.0f);
    return;
}

fn RayIntersectSphere_0( origin_0 : vec3<f32>,  direction_0 : vec3<f32>,  radius_0 : f32) -> vec2<f32>
{
    var a_0 : f32 = dot(direction_0, direction_0);
    var b_0 : f32 = 2.0f * dot(origin_0, direction_0);
    var discriminant_0 : f32 = b_0 * b_0 - 4.0f * a_0 * (dot(origin_0, origin_0) - radius_0 * radius_0);
    if(discriminant_0 < 0.0f)
    {
        return vec2<f32>(-1.0f, -1.0f);
    }
    var root_0 : f32 = sqrt(discriminant_0);
    var _S4 : f32 = - b_0;
    var _S5 : f32 = 2.0f * a_0;
    return vec2<f32>((_S4 - root_0) / _S5, (_S4 + root_0) / _S5);
}

fn MiePhase_0( g_0 : f32,  cosTheta_0 : f32) -> f32
{
    var _S6 : f32 = g_0 * g_0;
    var denominator_0 : f32 = 1.0f + _S6 + 2.0f * g_0 * cosTheta_0;
    return (1.0f - _S6) / (12.56637096405029297f * denominator_0 * sqrt(max(denominator_0, 9.99999993922529029e-09f)));
}

fn RayleighPhase_0( cosTheta_1 : f32) -> f32
{
    return 0.05968310311436653f * (1.0f + cosTheta_1 * cosTheta_1);
}

struct AtmosphereSample_0
{
     scattering_0 : vec3<f32>,
     extinction_0 : vec3<f32>,
     mieScattering_0 : vec3<f32>,
     rayleighScattering_0 : vec3<f32>,
};

fn SampleAtmosphere_0( position_0 : vec3<f32>) -> AtmosphereSample_0
{
    var _S7 : f32 = max(0.0f, length(position_0) - 6360.0f);
    var _S8 : f32 = - _S7;
    var rayleighDensity_0 : f32 = exp(_S8 / 8.0f);
    var mieDensity_0 : f32 = exp(_S8 / 1.20000004768371582f);
    var ozoneDensity_0 : f32;
    if(_S7 < 25.0f)
    {
        ozoneDensity_0 = saturate(_S7 / 15.0f - 0.66666668653488159f);
    }
    else
    {
        ozoneDensity_0 = saturate(_S8 / 15.0f + 2.66666674613952637f);
    }
    var s_0 : AtmosphereSample_0;
    var _S9 : vec3<f32> = vec3<f32>(0.00580199994146824f, 0.01355800032615662f, 0.03310000151395798f) * vec3<f32>(rayleighDensity_0);
    s_0.rayleighScattering_0 = _S9;
    var mieScattering_1 : f32 = 0.00399600015953183f * mieDensity_0;
    var mieExtinction_0 : f32 = 0.00443999981507659f * mieDensity_0;
    var _S10 : vec3<f32> = vec3<f32>(mieScattering_1, mieScattering_1, mieScattering_1);
    s_0.mieScattering_0 = _S10;
    s_0.scattering_0 = _S10 + _S9;
    s_0.extinction_0 = vec3<f32>(mieExtinction_0, mieExtinction_0, mieExtinction_0) + _S9 + vec3<f32>(0.00065000000176951f, 0.00188100000377744f, 0.00008499999967171f) * vec3<f32>(ozoneDensity_0);
    return s_0;
}

fn SampleTransmittanceLut_0( viewHeight_1 : f32,  lightZenithCos_0 : f32) -> vec3<f32>
{
    return vec3<f32>(1.0f, 1.0f, 1.0f);
}

fn NearestHit_0( roots_0 : vec2<f32>) -> f32
{
    var _S11 : f32 = roots_0.x;
    var _S12 : bool = _S11 < 0.0f;
    var _S13 : bool;
    if(_S12)
    {
        _S13 = (roots_0.y) < 0.0f;
    }
    else
    {
        _S13 = false;
    }
    if(_S13)
    {
        return -1.0f;
    }
    if(_S12)
    {
        return max(0.0f, roots_0.y);
    }
    var _S14 : f32 = roots_0.y;
    if(_S14 < 0.0f)
    {
        return max(0.0f, _S11);
    }
    return max(0.0f, min(_S11, _S14));
}

fn SampleMultiScattering_0( worldPos_0 : vec3<f32>,  lightZenithCos_1 : f32) -> vec3<f32>
{
    return vec3<f32>(0.0f, 0.0f, 0.0f);
}

struct ScatteringResult_0
{
     luminance_0 : vec3<f32>,
     opticalDepth_0 : vec3<f32>,
     transmittance_0 : vec3<f32>,
     multiScatteringAs1_0 : vec3<f32>,
};

fn IntegrateScatteredLuminance_0( worldPos_1 : vec3<f32>,  worldDir_0 : vec3<f32>,  sunDir_1 : vec3<f32>,  sunIlluminance_0 : vec3<f32>,  ground_0 : bool,  sampleCount_0 : f32,  mieRayPhase_0 : bool,  applyMultiScattering_0 : bool,  variableSampleCount_0 : bool) -> ScatteringResult_0
{
    var result_0 : ScatteringResult_0;
    const _S15 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    result_0.luminance_0 = _S15;
    result_0.opticalDepth_0 = _S15;
    const _S16 : vec3<f32> = vec3<f32>(1.0f, 1.0f, 1.0f);
    result_0.transmittance_0 = _S16;
    result_0.multiScatteringAs1_0 = _S15;
    if((dot(worldPos_1, worldPos_1)) <= 4.04496e+07f)
    {
        return result_0;
    }
    var topHits_0 : vec2<f32> = RayIntersectSphere_0(worldPos_1, worldDir_0, 6420.0f);
    var _S17 : bool;
    if((topHits_0.x) < 0.0f)
    {
        _S17 = (topHits_0.y) < 0.0f;
    }
    else
    {
        _S17 = false;
    }
    if(_S17)
    {
        return result_0;
    }
    var groundHits_0 : vec2<f32> = RayIntersectSphere_0(worldPos_1, worldDir_0, 6360.0f);
    var _S18 : f32 = groundHits_0.x;
    if(_S18 < 0.0f)
    {
        _S17 = (groundHits_0.y) < 0.0f;
    }
    else
    {
        _S17 = false;
    }
    var tMax_0 : f32;
    var tBottom_0 : f32;
    if(_S17)
    {
        tMax_0 = topHits_0.y;
        tBottom_0 = 0.0f;
    }
    else
    {
        var _S19 : f32 = max(0.0f, min(_S18, groundHits_0.y));
        tMax_0 = _S19;
        tBottom_0 = _S19;
    }
    var cosTheta_2 : f32 = dot(sunDir_1, worldDir_0);
    var _S20 : f32 = MiePhase_0(0.80000001192092896f, - cosTheta_2);
    var _S21 : f32 = RayleighPhase_0(cosTheta_2);
    var count_0 : f32;
    var countFloor_0 : f32;
    var tMaxFloor_0 : f32;
    if(variableSampleCount_0)
    {
        var count_1 : f32 = mix(4.0f, 128.0f, saturate(tMax_0 / 150.0f));
        var countFloor_1 : f32 = floor(count_1);
        var _S22 : f32 = tMax_0 * countFloor_1 / count_1;
        count_0 = count_1;
        countFloor_0 = countFloor_1;
        tMaxFloor_0 = _S22;
    }
    else
    {
        count_0 = sampleCount_0;
        countFloor_0 = sampleCount_0;
        tMaxFloor_0 = tMax_0;
    }
    var i_0 : f32 = 0.0f;
    var opticalDepth_1 : vec3<f32> = _S15;
    var throughput_0 : vec3<f32> = _S16;
    var multiScatteringAs1_1 : vec3<f32> = _S15;
    var luminance_1 : vec3<f32> = _S15;
    for(;;)
    {
        if(i_0 < count_0)
        {
        }
        else
        {
            break;
        }
        var t_0 : f32;
        var dt_0 : f32;
        if(variableSampleCount_0)
        {
            var t0_0 : f32 = i_0 / countFloor_0;
            var t1_0 : f32 = (i_0 + 1.0f) / countFloor_0;
            var t1_1 : f32 = t1_0 * t1_0;
            var t0_1 : f32 = tMaxFloor_0 * (t0_0 * t0_0);
            if(t1_1 > 1.0f)
            {
                t_0 = tMax_0;
            }
            else
            {
                t_0 = tMaxFloor_0 * t1_1;
            }
            var dt_1 : f32 = t_0 - t0_1;
            t_0 = t0_1 + 0.30000001192092896f * dt_1;
            dt_0 = dt_1;
        }
        else
        {
            var _S23 : f32 = tMax_0 / count_0;
            t_0 = tMax_0 * (i_0 + 0.30000001192092896f) / count_0;
            dt_0 = _S23;
        }
        var position_1 : vec3<f32> = worldPos_1 + vec3<f32>(t_0) * worldDir_0;
        var height_0 : f32 = length(position_1);
        var up_0 : vec3<f32> = position_1 / vec3<f32>(height_0);
        var medium_0 : AtmosphereSample_0 = SampleAtmosphere_0(position_1);
        var segmentDepth_0 : vec3<f32> = medium_0.extinction_0 * vec3<f32>(dt_0);
        var segmentTransmittance_0 : vec3<f32> = exp((vec3<f32>(0) - segmentDepth_0));
        var opticalDepth_2 : vec3<f32> = opticalDepth_1 + segmentDepth_0;
        var sunZenithCos_0 : f32 = dot(sunDir_1, up_0);
        var sunTransmittance_0 : vec3<f32> = SampleTransmittanceLut_0(height_0, sunZenithCos_0);
        var phaseScattering_0 : vec3<f32>;
        if(mieRayPhase_0)
        {
            phaseScattering_0 = medium_0.mieScattering_0 * vec3<f32>(_S20) + medium_0.rayleighScattering_0 * vec3<f32>(_S21);
        }
        else
        {
            phaseScattering_0 = medium_0.scattering_0 * vec3<f32>(0.07957746833562851f);
        }
        var planetShadow_0 : f32;
        if((NearestHit_0(RayIntersectSphere_0(position_1 + vec3<f32>(0.00100000004749745f) * up_0, sunDir_1, 6360.0f))) >= 0.0f)
        {
            planetShadow_0 = 0.0f;
        }
        else
        {
            planetShadow_0 = 1.0f;
        }
        var multiScattered_0 : vec3<f32>;
        if(applyMultiScattering_0)
        {
            multiScattered_0 = SampleMultiScattering_0(position_1, sunZenithCos_0);
        }
        else
        {
            multiScattered_0 = _S15;
        }
        var source_0 : vec3<f32> = sunIlluminance_0 * (vec3<f32>(planetShadow_0) * sunTransmittance_0 * phaseScattering_0 + multiScattered_0 * medium_0.scattering_0);
        var multiScatteringAs1_2 : vec3<f32> = multiScatteringAs1_1 + throughput_0 * medium_0.scattering_0 * vec3<f32>(dt_0);
        var luminance_2 : vec3<f32> = luminance_1 + throughput_0 * (source_0 - source_0 * segmentTransmittance_0) / max(medium_0.extinction_0, vec3<f32>(9.99999971718068537e-10f));
        var throughput_1 : vec3<f32> = throughput_0 * segmentTransmittance_0;
        i_0 = i_0 + 1.0f;
        opticalDepth_1 = opticalDepth_2;
        throughput_0 = throughput_1;
        multiScatteringAs1_1 = multiScatteringAs1_2;
        luminance_1 = luminance_2;
    }
    if(ground_0)
    {
        _S17 = tMax_0 == tBottom_0;
    }
    else
    {
        _S17 = false;
    }
    if(_S17)
    {
        _S17 = tBottom_0 > 0.0f;
    }
    else
    {
        _S17 = false;
    }
    if(_S17)
    {
        var groundPos_0 : vec3<f32> = worldPos_1 + vec3<f32>(tBottom_0) * worldDir_0;
        var groundHeight_0 : f32 = length(groundPos_0);
        var groundUp_0 : vec3<f32> = groundPos_0 / vec3<f32>(groundHeight_0);
        luminance_1 = luminance_1 + sunIlluminance_0 * SampleTransmittanceLut_0(groundHeight_0, dot(sunDir_1, groundUp_0)) * throughput_0 * vec3<f32>(saturate(dot(groundUp_0, sunDir_1))) * vec3<f32>(0.40000000596046448f) / vec3<f32>(3.14159274101257324f);
    }
    result_0.luminance_0 = luminance_1;
    result_0.opticalDepth_0 = opticalDepth_1;
    result_0.transmittance_0 = throughput_0;
    result_0.multiScatteringAs1_0 = multiScatteringAs1_1;
    return result_0;
}

fn EncodeTransmittance_0( t_1 : vec3<f32>) -> vec3<f32>
{
    return sqrt(t_1);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S24 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S25 : bool;
    if((_S24.x) >= i32(p_0.size_0.x))
    {
        _S25 = true;
    }
    else
    {
        _S25 = (_S24.y) >= i32(p_0.size_0.y);
    }
    if(_S25)
    {
        return;
    }
    var viewHeight_2 : f32;
    var viewZenithCos_1 : f32;
    TransmittanceUvToParams_0((vec2<f32>(_S24) + vec2<f32>(0.5f)) * p_0.size_0.zw, &(viewHeight_2), &(viewZenithCos_1));
    textureStore((uTransmittance_0), (vec2<u32>(_S24)), (vec4<f32>(EncodeTransmittance_0(exp((vec3<f32>(0) - IntegrateScatteredLuminance_0(vec3<f32>(0.0f, 0.0f, viewHeight_2), vec3<f32>(0.0f, sqrt(max(0.0f, 1.0f - viewZenithCos_1 * viewZenithCos_1)), viewZenithCos_1), vec3<f32>(0.0f, 0.0f, 1.0f), vec3<f32>(0.0f, 0.0f, 0.0f), false, 10.0f, false, false, false).opticalDepth_0))), 1.0f)));
    return;
}

