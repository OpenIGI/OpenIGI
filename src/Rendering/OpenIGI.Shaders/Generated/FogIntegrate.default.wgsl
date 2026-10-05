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

@binding(3) @group(0) var<uniform> p_0 : FogParams_std140_0;
@binding(0) @group(0) var uScattering_0 : texture_3d<f32>;

@binding(2) @group(0) var uIntegrated_0 : texture_storage_3d<rgba16float, write>;

fn SliceViewDepth_0( slice_0 : f32) -> f32
{
    return (exp2(slice_0 / p_0.zParams_0.z) - p_0.zParams_0.y) / p_0.zParams_0.x;
}

fn ViewPositionAt_0( uv_0 : vec2<f32>,  viewZ_0 : f32) -> vec3<f32>
{
    var ndc_0 : vec2<f32> = uv_0 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / p_0.unproject_0.x, - ndc_0.y * viewZ_0 / p_0.unproject_0.y, viewZ_0);
}

fn FlashlightOn_0( f_0 : ptr<function, FlashlightParams_std140_0>) -> bool
{
    return ((*f_0).position_0.w) > 0.0f;
}

fn FlashlightCookie_0( f_1 : ptr<function, FlashlightParams_std140_0>,  direction_0 : vec3<f32>) -> f32
{
    var _S1 : vec3<f32> = (*f_1).axis_0.xyz;
    var c_0 : f32 = dot(_S1, direction_0);
    var _S2 : bool;
    if(c_0 <= ((*f_1).axis_0.w))
    {
        _S2 = true;
    }
    else
    {
        _S2 = c_0 <= 0.0f;
    }
    if(_S2)
    {
        return 0.0f;
    }
    var r_0 : f32 = sqrt(max(1.0f - c_0 * c_0, 0.0f)) / c_0 / max((*f_1).cone_0.x, 0.00009999999747379f);
    var inner_0 : f32 = (*f_1).cone_0.y;
    var hot_0 : f32 = exp(- (r_0 * r_0 / max(0.30000001192092896f * inner_0 * inner_0, 0.00009999999747379f)));
    var body_0 : f32 = 1.0f - smoothstep(inner_0, 1.0f, r_0);
    var _S3 : f32 = r_0 - 0.86000001430511475f;
    var ring_0 : f32 = exp(- (_S3 * _S3 / 0.00400000018998981f));
    var spill_0 : f32 = (1.0f - smoothstep(1.0f, (*f_1).cone_0.z, r_0)) * (*f_1).cone_0.w;
    var side_0 : vec3<f32> = cross((*f_1).up_0.xyz, _S1);
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
    var around_0 : f32 = atan2(dot(direction_0, cross(_S1, across_0)), dot(direction_0, across_0) + 9.99999997475242708e-07f);
    return max((0.55000001192092896f * hot_0 + 0.44999998807907104f * body_0 + 0.07000000029802322f * ring_0) * (1.0f + 0.04500000178813934f * r_0 * sin(5.0f * around_0 + 1.29999995231628418f) + 0.02999999932944775f * sin(33.0f * r_0 + 2.0f * around_0)) + spill_0, 0.0f);
}

fn FlashlightIrradiance_0( f_2 : ptr<function, FlashlightParams_std140_0>,  viewPos_0 : vec3<f32>,  thrown_0 : bool,  toLight_0 : ptr<function, vec3<f32>>,  distance_0 : ptr<function, f32>) -> vec3<f32>
{
    var _S4 : vec4<f32> = (*f_2).position_0;
    var fromLens_0 : vec3<f32> = viewPos_0 - (*f_2).position_0.xyz;
    var distanceSq_0 : f32 = dot(fromLens_0, fromLens_0);
    var _S5 : f32 = sqrt(max(distanceSq_0, 9.99999993922529029e-09f));
    (*distance_0) = _S5;
    (*toLight_0) = (vec3<f32>(0) - fromLens_0) / vec3<f32>(_S5);
    var range_0 : f32 = _S4.w;
    var _S6 : bool;
    if(range_0 <= 0.0f)
    {
        _S6 = true;
    }
    else
    {
        _S6 = (*distance_0) >= range_0;
    }
    if(_S6)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var _S7 : f32 = FlashlightCookie_0(&((*f_2)), (vec3<f32>(0) - (*toLight_0)));
    if(_S7 <= 0.0f)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var ratio_0 : f32 = distanceSq_0 / (range_0 * range_0);
    var window_0 : f32 = saturate(1.0f - ratio_0 * ratio_0);
    var window_1 : f32 = window_0 * window_0;
    if(thrown_0)
    {
        _S6 = (*distance_0) > 5.0f;
    }
    else
    {
        _S6 = false;
    }
    var carry_0 : f32;
    if(_S6)
    {
        carry_0 = pow((*distance_0) / 5.0f, (*f_2).control_0.z);
    }
    else
    {
        carry_0 = 1.0f;
    }
    return (*f_2).colour_0.xyz * vec3<f32>((_S7 * window_1 * carry_0 / max(distanceSq_0, (*f_2).colour_0.w)));
}

fn FlashlightInScatter_0( here_0 : vec3<f32>,  mediumExtinction_0 : f32,  dust_1 : ptr<function, f32>) -> vec3<f32>
{
    (*dust_1) = 0.0f;
    var _S8 : FlashlightParams_std140_0 = p_0.flashlight_0;
    var _S9 : bool = FlashlightOn_0(&(_S8));
    var _S10 : bool;
    if(!_S9)
    {
        _S10 = true;
    }
    else
    {
        _S10 = (_S8.control_0.y) <= 0.0f;
    }
    if(_S10)
    {
        return vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    var toLight_1 : vec3<f32>;
    var distance_1 : f32;
    var _S11 : vec3<f32> = FlashlightIrradiance_0(&(_S8), here_0, false, &(toLight_1), &(distance_1));
    const _S12 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    if((all((_S11 <= _S12))))
    {
        return _S12;
    }
    var irradiance_0 : vec3<f32> = _S11 * vec3<f32>(saturate(distance_1 * distance_1));
    var fadeIn_0 : f32 = smoothstep(0.75f, 4.0f, distance_1);
    const _S13 : vec3<f32> = vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f);
    var _S14 : f32 = _S8.control_0.w * saturate(dot(irradiance_0, _S13) * max(distance_1 * distance_1, 1.0f) / max(dot(_S8.colour_0.xyz, _S13), 0.00000999999974738f)) * fadeIn_0 * (1.0f - smoothstep(5.0f, 12.0f, distance_1));
    (*dust_1) = _S14;
    var g_0 : f32 = p_0.medium_0.w;
    var _S15 : f32 = g_0 * g_0;
    var _S16 : f32 = max(1.0f + _S15 - 2.0f * g_0 * dot((vec3<f32>(0) - toLight_1), normalize((vec3<f32>(0) - here_0))), 0.00009999999747379f);
    return irradiance_0 * p_0.albedo_0.xyz * vec3<f32>((mediumExtinction_0 + _S14)) * vec3<f32>(mix((1.0f - _S15) / (12.56637096405029297f * _S16 * sqrt(_S16)), 0.07957746833562851f, 0.5f)) * vec3<f32>(_S8.control_0.y) * vec3<f32>(fadeIn_0);
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S17 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S18 : vec3<i32> = vec3<i32>(p_0.grid_0.xyz + vec3<f32>(0.5f));
    if((any((_S17 >= (_S18.xy)))))
    {
        return;
    }
    var uv_1 : vec2<f32> = (vec2<f32>(_S17) + vec2<f32>(0.5f)) / p_0.grid_0.xy;
    const _S19 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var previous_0 : vec3<f32> = ViewPositionAt_0(uv_1, SliceViewDepth_0(0.0f));
    var z_0 : i32 = i32(0);
    var transmittance_0 : f32 = 1.0f;
    var accumulated_0 : vec3<f32> = _S19;
    for(;;)
    {
        if(z_0 < (_S18.z))
        {
        }
        else
        {
            break;
        }
        var _S20 : vec4<i32> = vec4<i32>(_S17, z_0, i32(0));
        var _S21 : vec4<f32> = (textureLoad((uScattering_0), ((_S20)).xyz, ((_S20)).w));
        var medium_1 : vec4<f32> = _S21;
        var here_1 : vec3<f32> = ViewPositionAt_0(uv_1, SliceViewDepth_0(f32(z_0) + 0.5f));
        var stepLength_0 : f32 = length(here_1 - previous_0);
        var _S22 : vec3<f32> = _S21.xyz;
        var dust_2 : f32;
        var _S23 : vec3<f32> = FlashlightInScatter_0(here_1, _S21.w, &(dust_2));
        var _S24 : vec3<f32> = _S22 + _S23;
        medium_1.x = _S24.x;
        medium_1.y = _S24.y;
        medium_1.z = _S24.z;
        medium_1[i32(3)] = medium_1[i32(3)] + dust_2;
        var slabTransmittance_0 : f32 = exp(- medium_1.w * stepLength_0);
        var accumulated_1 : vec3<f32> = accumulated_0 + (medium_1.xyz - medium_1.xyz * vec3<f32>(slabTransmittance_0)) / vec3<f32>(max(medium_1.w, 0.00000999999974738f)) * vec3<f32>(transmittance_0);
        var transmittance_1 : f32 = transmittance_0 * slabTransmittance_0;
        textureStore((uIntegrated_0), (vec3<u32>(vec3<i32>(_S17, z_0))), (vec4<f32>(accumulated_1, transmittance_1)));
        var _S25 : i32 = z_0 + i32(1);
        previous_0 = here_1;
        z_0 = _S25;
        transmittance_0 = transmittance_1;
        accumulated_0 = accumulated_1;
    }
    return;
}

