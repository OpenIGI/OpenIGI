struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct BlastVolumePush_std140_0
{
    @align(16) modelView_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) draw_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> push_0 : BlastVolumePush_std140_0;
struct BlastSlot_std140_0
{
    @align(16) centre_0 : vec4<f32>,
    @align(16) time_0 : vec4<f32>,
    @align(16) shape_0 : vec4<f32>,
    @align(16) fire_0 : vec4<f32>,
};

struct _Array_std140_BlastSlot2_0
{
    @align(16) data_1 : array<BlastSlot_std140_0, i32(2)>,
};

struct BlastVolumeParams_std140_0
{
    @align(16) projection_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) viewToWorld0_0 : vec4<f32>,
    @align(16) viewToWorld1_0 : vec4<f32>,
    @align(16) viewToWorld2_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) screen_0 : vec4<f32>,
    @align(16) sun_0 : vec4<f32>,
    @align(16) sunColour_0 : vec4<f32>,
    @align(16) ambient_0 : vec4<f32>,
    @align(16) blasts_0 : _Array_std140_BlastSlot2_0,
};

@binding(3) @group(1) var<uniform> p_0 : BlastVolumeParams_std140_0;
@binding(0) @group(1) var uBlastDepth_0 : texture_depth_2d;

@binding(1) @group(1) var uBackdrop_0 : texture_2d<f32>;

@binding(2) @group(1) var uBackdropSampler_0 : sampler;

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

struct BlastVaryings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) viewPos_0 : vec3<f32>,
};

struct vertexInput_0
{
    @location(0) position_1 : vec3<f32>,
    @location(1) normal_0 : vec3<f32>,
    @location(2) diffuseUv_0 : vec2<f32>,
    @location(3) lightmapUv_0 : vec2<f32>,
    @location(4) colorBgra_0 : vec4<f32>,
    @location(5) previousPosition_0 : vec3<f32>,
    @location(6) previousNormal_0 : vec3<f32>,
};

@vertex
fn vertexMain( _S2 : vertexInput_0) -> BlastVaryings_0
{
    var view_0 : vec4<f32> = (((vec4<f32>(_S2.position_1, 1.0f)) * (mat4x4<f32>(push_0.modelView_0.data_0[i32(0)][i32(0)], push_0.modelView_0.data_0[i32(1)][i32(0)], push_0.modelView_0.data_0[i32(2)][i32(0)], push_0.modelView_0.data_0[i32(3)][i32(0)], push_0.modelView_0.data_0[i32(0)][i32(1)], push_0.modelView_0.data_0[i32(1)][i32(1)], push_0.modelView_0.data_0[i32(2)][i32(1)], push_0.modelView_0.data_0[i32(3)][i32(1)], push_0.modelView_0.data_0[i32(0)][i32(2)], push_0.modelView_0.data_0[i32(1)][i32(2)], push_0.modelView_0.data_0[i32(2)][i32(2)], push_0.modelView_0.data_0[i32(3)][i32(2)], push_0.modelView_0.data_0[i32(0)][i32(3)], push_0.modelView_0.data_0[i32(1)][i32(3)], push_0.modelView_0.data_0[i32(2)][i32(3)], push_0.modelView_0.data_0[i32(3)][i32(3)]))));
    var output_0 : BlastVaryings_0;
    output_0.viewPos_0 = view_0.xyz;
    var _S3 : vec4<f32> = (((view_0) * (mat4x4<f32>(p_0.projection_0.data_0[i32(0)][i32(0)], p_0.projection_0.data_0[i32(1)][i32(0)], p_0.projection_0.data_0[i32(2)][i32(0)], p_0.projection_0.data_0[i32(3)][i32(0)], p_0.projection_0.data_0[i32(0)][i32(1)], p_0.projection_0.data_0[i32(1)][i32(1)], p_0.projection_0.data_0[i32(2)][i32(1)], p_0.projection_0.data_0[i32(3)][i32(1)], p_0.projection_0.data_0[i32(0)][i32(2)], p_0.projection_0.data_0[i32(1)][i32(2)], p_0.projection_0.data_0[i32(2)][i32(2)], p_0.projection_0.data_0[i32(3)][i32(2)], p_0.projection_0.data_0[i32(0)][i32(3)], p_0.projection_0.data_0[i32(1)][i32(3)], p_0.projection_0.data_0[i32(2)][i32(3)], p_0.projection_0.data_0[i32(3)][i32(3)]))));
    var clip_0 : vec4<f32> = _S3;
    clip_0[i32(1)] = OpenIgiClipY_0(_S3.y);
    clip_0[i32(2)] = (clip_0.w - clip_0.z) * 0.5f;
    output_0.position_0 = clip_0;
    return output_0;
}

fn ViewDepthAt_0( depth_0 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S4 : f32 = unproject_1.z;
    var _S5 : f32 = unproject_1.w;
    return 2.0f * _S4 * _S5 / (_S5 + _S4 - (1.0f - 2.0f * depth_0) * (_S5 - _S4));
}

fn ViewToWorld_0( d_0 : vec3<f32>) -> vec3<f32>
{
    return vec3<f32>(dot(p_0.viewToWorld0_0.xyz, d_0), dot(p_0.viewToWorld1_0.xyz, d_0), dot(p_0.viewToWorld2_0.xyz, d_0));
}

fn RayBox_0( origin_0 : vec3<f32>,  dir_0 : vec3<f32>,  halfSize_0 : vec3<f32>) -> vec2<f32>
{
    var inv_0 : vec3<f32> = vec3<f32>(1.0f) / max(abs(dir_0), vec3<f32>(9.99999997475242708e-07f, 9.99999997475242708e-07f, 9.99999997475242708e-07f)) * vec3<f32>((vec3<i32>(sign((dir_0 + vec3<f32>(9.99999971718068537e-10f, 9.99999971718068537e-10f, 9.99999971718068537e-10f))))));
    var t0_0 : vec3<f32> = ((vec3<f32>(0) - halfSize_0) - origin_0) * inv_0;
    var t1_0 : vec3<f32> = (halfSize_0 - origin_0) * inv_0;
    var lo_0 : vec3<f32> = min(t0_0, t1_0);
    var hi_0 : vec3<f32> = max(t0_0, t1_0);
    return vec2<f32>(max(max(lo_0.x, lo_0.y), lo_0.z), min(min(hi_0.x, hi_0.y), hi_0.z));
}

fn InterleavedGradient_0( pixel_0 : vec2<f32>,  frame_0 : f32) -> f32
{
    return fract(52.98291778564453125f * fract(dot(pixel_0 + vec2<f32>(frame_0) * vec2<f32>(47.0f, 17.0f) * vec2<f32>(0.69499999284744263f), vec2<f32>(0.06711056083440781f, 0.00583714991807938f))));
}

fn Hash3_0( q_0 : vec3<f32>) -> f32
{
    var _S6 : vec3<f32> = fract(q_0 * vec3<f32>(0.31830990314483643f) + vec3<f32>(0.70999997854232788f, 0.11299999803304672f, 0.41899999976158142f)) * vec3<f32>(17.0f);
    var _S7 : f32 = _S6.x;
    var _S8 : f32 = _S6.y;
    var _S9 : f32 = _S6.z;
    return fract(_S7 * _S8 * _S9 * (_S7 + _S8 + _S9));
}

fn Noise3_0( x_0 : vec3<f32>) -> f32
{
    var i_0 : vec3<f32> = floor(x_0);
    var f_0 : vec3<f32> = fract(x_0);
    var f_1 : vec3<f32> = f_0 * f_0 * (vec3<f32>(3.0f) - vec3<f32>(2.0f) * f_0);
    var _S10 : f32 = f_1.x;
    var _S11 : f32 = f_1.y;
    return mix(mix(mix(Hash3_0(i_0), Hash3_0(i_0 + vec3<f32>(1.0f, 0.0f, 0.0f)), _S10), mix(Hash3_0(i_0 + vec3<f32>(0.0f, 1.0f, 0.0f)), Hash3_0(i_0 + vec3<f32>(1.0f, 1.0f, 0.0f)), _S10), _S11), mix(mix(Hash3_0(i_0 + vec3<f32>(0.0f, 0.0f, 1.0f)), Hash3_0(i_0 + vec3<f32>(1.0f, 0.0f, 1.0f)), _S10), mix(Hash3_0(i_0 + vec3<f32>(0.0f, 1.0f, 1.0f)), Hash3_0(i_0 + vec3<f32>(1.0f, 1.0f, 1.0f)), _S10), _S11), f_1.z);
}

fn Fbm_0( x_1 : vec3<f32>,  octaves_0 : i32) -> f32
{
    var _S12 : vec3<f32> = x_1;
    var i_1 : i32 = i32(0);
    var amplitude_0 : f32 = 0.5f;
    var sum_0 : f32 = 0.0f;
    var total_0 : f32 = 0.0f;
    for(;;)
    {
        if(i_1 < i32(5))
        {
        }
        else
        {
            break;
        }
        if(i_1 >= octaves_0)
        {
            break;
        }
        var sum_1 : f32 = sum_0 + Noise3_0(_S12) * amplitude_0;
        var total_1 : f32 = total_0 + amplitude_0;
        var _S13 : f32 = _S12.x;
        var _S14 : f32 = _S12.y;
        var _S15 : vec3<f32> = vec3<f32>(_S13 * 1.60000002384185791f + _S14 * 1.20000004768371582f, _S14 * 1.60000002384185791f - _S13 * 1.20000004768371582f, _S12.z * 2.0f) + vec3<f32>(3.09999990463256836f, 1.70000004768371582f, 5.30000019073486328f);
        var amplitude_1 : f32 = amplitude_0 * 0.5f;
        var _S16 : i32 = i_1 + i32(1);
        _S12 = _S15;
        i_1 = _S16;
        amplitude_0 = amplitude_1;
        sum_0 = sum_1;
        total_0 = total_1;
    }
    return sum_0 / max(total_0, 0.00009999999747379f);
}

struct VolumeSample_0
{
     density_0 : f32,
     heat_0 : f32,
};

fn Volume_0( b_0 : ptr<function, BlastSlot_std140_0>,  q_1 : vec3<f32>,  octaves_1 : i32) -> VolumeSample_0
{
    var s_0 : VolumeSample_0;
    var age_0 : f32 = (*b_0).time_0.x;
    var seed_0 : f32 = (*b_0).time_0.y;
    var _S17 : f32 = max((*b_0).time_0.z, 0.10000000149011612f);
    var heat_1 : f32 = (*b_0).time_0.w;
    var smoke_0 : f32 = (*b_0).shape_0.y;
    var rise_0 : f32 = _S17 * (0.34999999403953552f + 0.60000002384185791f * smoothstep(0.20000000298023224f, 3.0f, age_0));
    var _S18 : f32 = max((*b_0).shape_0.x - _S17 * 0.40000000596046448f, rise_0);
    var fireCentre_0 : vec3<f32> = vec3<f32>(0.0f, 0.0f, mix(rise_0, _S18, smoothstep(0.80000001192092896f, 6.0f, age_0)));
    var n_0 : vec3<f32> = q_1 / vec3<f32>(max(_S17, 1.0f)) * vec3<f32>(2.79999995231628418f) + vec3<f32>(fract(seed_0 * 0.00130000000353903f) * 97.0f, fract(seed_0 * 0.00069999997504056f) * 61.0f, fract(seed_0 * 0.00209999992512167f) * 43.0f);
    n_0[i32(2)] = n_0[i32(2)] - age_0 * 0.55000001192092896f;
    var _S19 : vec3<f32> = vec3<f32>(0.5f);
    var detail_0 : f32 = saturate((Fbm_0(n_0 + (vec3<f32>(Noise3_0(n_0 * _S19), Noise3_0(n_0 * _S19 + vec3<f32>(7.30000019073486328f)), Noise3_0(n_0 * _S19 + vec3<f32>(13.10000038146972656f))) - _S19) * vec3<f32>(1.60000002384185791f), octaves_1) - 0.5f) * 2.40000009536743164f + 0.5f);
    var dBall_0 : f32 = length(q_1 - fireCentre_0) / _S17;
    var _S20 : f32 = q_1.z;
    var _S21 : f32 = length(q_1.xy);
    var _S22 : f32 = _S21 / max(_S17 * mix(0.55000001192092896f, 0.2800000011920929f, saturate(_S20 / max(_S18, 0.10000000149011612f))) * smoothstep(0.15000000596046448f, 1.20000004768371582f, age_0), 0.05000000074505806f);
    var _S23 : f32;
    if(_S20 < 0.0f)
    {
        _S23 = - _S20 / _S17 * 4.0f;
    }
    else
    {
        _S23 = 0.0f;
    }
    var _S24 : f32 = _S22 + _S23;
    if(_S20 > _S18)
    {
        _S23 = (_S20 - _S18) / _S17 * 4.0f;
    }
    else
    {
        _S23 = 0.0f;
    }
    var capR_0 : f32 = _S17 * mix(0.60000002384185791f, 1.25f, smoothstep(0.40000000596046448f, 6.0f, age_0));
    var density_1 : f32 = smoothstep(0.0f, 0.5f, max(saturate(1.0f - dBall_0), max(saturate(1.0f - (_S24 + _S23)) * smoke_0 * 0.85000002384185791f, saturate(1.0f - length(vec2<f32>(_S21 - capR_0 * 0.55000001192092896f, (_S20 - _S18) * 1.60000002384185791f)) / max(capR_0 * 0.60000002384185791f, 0.10000000149011612f)) * smoothstep(0.5f, 1.60000002384185791f, age_0) * smoke_0)) * 1.89999997615814209f - (1.0f - detail_0) * 0.94999998807907104f);
    var _S25 : f32 = saturate(saturate(1.0f - dBall_0 * 1.10000002384185791f) * (0.55000001192092896f + detail_0 * 0.69999998807907104f) * heat_1);
    s_0.heat_0 = _S25;
    s_0.density_0 = density_1 * mix(1.0f, 0.75f, _S25) * max(smoke_0, heat_1);
    return s_0;
}

fn BlackBody_0( heat_2 : f32) -> vec3<f32>
{
    var t_0 : f32 = saturate(heat_2);
    return vec3<f32>(1.0f, pow(t_0, 1.79999995231628418f) * 0.41999998688697815f, pow(t_0, 4.5f) * 0.11999999731779099f) * vec3<f32>((pow(t_0, 2.20000004768371582f) * 26.0f));
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) viewPos_1 : vec3<f32>,
};

@fragment
fn fragmentMain( _S26 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> pixelOutput_0
{
    var _S27 : BlastSlot_std140_0 = p_0.blasts_0.data_1[clamp(i32(push_0.draw_0.x + 0.5f), i32(0), i32(1))];
    var _S28 : vec2<i32> = vec2<i32>(position_2.xy);
    var _S29 : vec2<f32> = vec2<f32>(_S28);
    var uv_0 : vec2<f32> = (_S29 + vec2<f32>(0.5f)) * p_0.screen_0.zw;
    var dir_1 : vec3<f32> = normalize(_S26.viewPos_1);
    var _S30 : vec3<i32> = vec3<i32>(_S28, i32(0));
    var depth_1 : f32 = (textureLoad((uBlastDepth_0), ((_S30)).xy, ((_S30)).z));
    var _S31 : bool = depth_1 > 0.0f;
    var sceneZ_0 : f32;
    if(_S31)
    {
        sceneZ_0 = ViewDepthAt_0(depth_1, p_0.unproject_0);
    }
    else
    {
        sceneZ_0 = 1.0e+07f;
    }
    var sceneT_0 : f32 = sceneZ_0 / max(dir_1.z, 0.00009999999747379f);
    var _S32 : vec3<f32> = _S27.centre_0.xyz;
    var originW_0 : vec3<f32> = ViewToWorld_0((vec3<f32>(0) - _S32));
    var dirW_0 : vec3<f32> = ViewToWorld_0(dir_1);
    var _S33 : f32 = _S27.centre_0.w;
    if((length(_S26.viewPos_1)) < (RayBox_0(originW_0, dirW_0, vec3<f32>(_S33, _S33, _S33)).y * 0.99500000476837158f))
    {
        discard;
    }
    var _S34 : vec4<f32> = _S27.shape_0;
    var shockR_0 : f32 = _S27.shape_0.z;
    var shockStrength_0 : f32 = _S27.shape_0.w;
    const _S35 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    var _S36 : bool;
    if(shockStrength_0 > 0.00100000004749745f)
    {
        _S36 = shockR_0 > 0.10000000149011612f;
    }
    else
    {
        _S36 = false;
    }
    var dust_0 : f32;
    var transmittance_0 : f32;
    var bend_0 : vec2<f32>;
    if(_S36)
    {
        var width_0 : f32 = shockR_0 * 0.05999999865889549f + 0.60000002384185791f;
        var surface_0 : vec3<f32> = originW_0 + dirW_0 * vec3<f32>(min(sceneT_0, 1.0e+05f));
        if(_S31)
        {
            dust_0 = exp(- pow((length(surface_0) - shockR_0) / width_0, 2.0f));
        }
        else
        {
            dust_0 = 0.0f;
        }
        var along_0 : f32 = - dot(originW_0, dirW_0);
        var miss_0 : f32 = length(originW_0 + dirW_0 * vec3<f32>(along_0));
        if(miss_0 < shockR_0)
        {
            _S36 = along_0 > 0.0f;
        }
        else
        {
            _S36 = false;
        }
        if(_S36)
        {
            var entry_0 : f32 = along_0 - sqrt(max(shockR_0 * shockR_0 - miss_0 * miss_0, 0.0f));
            if(entry_0 > 0.0f)
            {
                _S36 = entry_0 < sceneT_0;
            }
            else
            {
                _S36 = false;
            }
            if(_S36)
            {
                var grazing_0 : f32 = miss_0 / shockR_0;
                transmittance_0 = smoothstep(0.81999999284744263f, 0.95999997854232788f, grazing_0) * (1.0f - smoothstep(0.95999997854232788f, 1.0f, grazing_0)) * 0.55000001192092896f;
            }
            else
            {
                transmittance_0 = 0.0f;
            }
        }
        else
        {
            transmittance_0 = 0.0f;
        }
        var centreClip_0 : vec4<f32> = (((vec4<f32>(_S32, 1.0f)) * (mat4x4<f32>(p_0.projection_0.data_0[i32(0)][i32(0)], p_0.projection_0.data_0[i32(1)][i32(0)], p_0.projection_0.data_0[i32(2)][i32(0)], p_0.projection_0.data_0[i32(3)][i32(0)], p_0.projection_0.data_0[i32(0)][i32(1)], p_0.projection_0.data_0[i32(1)][i32(1)], p_0.projection_0.data_0[i32(2)][i32(1)], p_0.projection_0.data_0[i32(3)][i32(1)], p_0.projection_0.data_0[i32(0)][i32(2)], p_0.projection_0.data_0[i32(1)][i32(2)], p_0.projection_0.data_0[i32(2)][i32(2)], p_0.projection_0.data_0[i32(3)][i32(2)], p_0.projection_0.data_0[i32(0)][i32(3)], p_0.projection_0.data_0[i32(1)][i32(3)], p_0.projection_0.data_0[i32(2)][i32(3)], p_0.projection_0.data_0[i32(3)][i32(3)]))));
        var _S37 : f32 = centreClip_0.w;
        if(_S37 > 0.00100000004749745f)
        {
            bend_0 = vec2<f32>(centreClip_0.x / _S37 * 0.5f + 0.5f, 0.5f - centreClip_0.y / _S37 * 0.5f);
        }
        else
        {
            bend_0 = uv_0;
        }
        var out2_0 : vec2<f32> = uv_0 - bend_0;
        var outLength_0 : f32 = length(out2_0);
        var outward_0 : vec2<f32>;
        if(outLength_0 > 0.00009999999747379f)
        {
            outward_0 = out2_0 / vec2<f32>(outLength_0);
        }
        else
        {
            outward_0 = vec2<f32>(0.0f, 1.0f);
        }
        var _S38 : f32 = dust_0 * shockStrength_0 * saturate(- surface_0.z / max(shockR_0 * 0.15000000596046448f, 1.0f) + 0.60000002384185791f) * 0.34999999403953552f;
        bend_0 = outward_0 * vec2<f32>(((dust_0 + transmittance_0) * shockStrength_0)) * vec2<f32>(0.00999999977648258f);
        dust_0 = _S38;
    }
    else
    {
        bend_0 = _S35;
        dust_0 = 0.0f;
    }
    var bendAmount_0 : f32 = length(bend_0);
    var behind_0 : vec3<f32>;
    if(bendAmount_0 > 0.00000999999974738f)
    {
        behind_0 = vec3<f32>((textureSampleLevel((uBackdrop_0), (uBackdropSampler_0), (uv_0 + bend_0 * vec2<f32>(1.08000004291534424f)), (0.0f))).x, (textureSampleLevel((uBackdrop_0), (uBackdropSampler_0), (uv_0 + bend_0), (0.0f))).y, (textureSampleLevel((uBackdrop_0), (uBackdropSampler_0), (uv_0 + bend_0 * vec2<f32>(0.92000001668930054f)), (0.0f))).z) * vec3<f32>((1.0f + bendAmount_0 * 3.0f));
    }
    else
    {
        behind_0 = (textureSampleLevel((uBackdrop_0), (uBackdropSampler_0), (uv_0), (0.0f))).xyz;
    }
    var behind_1 : vec3<f32> = mix(behind_0, vec3<f32>(0.31999999284744263f, 0.27000001072883606f, 0.2199999988079071f) * (p_0.ambient_0.xyz + p_0.sunColour_0.xyz * vec3<f32>(p_0.sun_0.w) * vec3<f32>(0.5f)), vec3<f32>(saturate(dust_0)));
    var _S39 : vec4<f32> = _S27.time_0;
    var _S40 : f32 = max(_S27.time_0.z, 0.10000000149011612f);
    var _S41 : f32 = _S40 * 1.60000002384185791f;
    var _S42 : f32 = (_S34.x + _S40 * 1.5f) * 0.5f;
    var _S43 : vec2<f32> = RayBox_0(originW_0 - vec3<f32>(0.0f, 0.0f, _S42 - _S40 * 0.60000002384185791f), dirW_0, vec3<f32>(_S41, _S41, _S42));
    var span_0 : vec2<f32> = _S43;
    span_0[i32(0)] = max(_S43.x, 0.0f);
    span_0[i32(1)] = min(span_0.y, sceneT_0);
    const _S44 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    if((span_0.y) > (span_0.x))
    {
        if((_S39.w) > 0.00100000004749745f)
        {
            _S36 = true;
        }
        else
        {
            _S36 = (_S34.y) > 0.00100000004749745f;
        }
    }
    else
    {
        _S36 = false;
    }
    var light_0 : vec3<f32>;
    if(_S36)
    {
        var steps_0 : i32 = clamp(i32(push_0.draw_0.z + 0.5f), i32(8), i32(64));
        var _S45 : f32 = (span_0.y - span_0.x) / f32(steps_0);
        var _S46 : f32 = InterleavedGradient_0(_S29, push_0.draw_0.w);
        var _S47 : vec3<f32> = ViewToWorld_0(p_0.sun_0.xyz);
        var _S48 : i32;
        if(steps_0 >= i32(40))
        {
            _S48 = i32(5);
        }
        else
        {
            if(steps_0 >= i32(24))
            {
                _S48 = i32(4);
            }
            else
            {
                _S48 = i32(3);
            }
        }
        var _S49 : f32 = 7.0f / max(_S40, 1.0f);
        var i_2 : i32 = i32(0);
        transmittance_0 = 1.0f;
        light_0 = _S44;
        for(;;)
        {
            if(i_2 < i32(64))
            {
            }
            else
            {
                break;
            }
            if(i_2 >= steps_0)
            {
                _S36 = true;
            }
            else
            {
                _S36 = transmittance_0 < 0.00999999977648258f;
            }
            if(_S36)
            {
                break;
            }
            var q_2 : vec3<f32> = originW_0 + dirW_0 * vec3<f32>((span_0.x + (f32(i_2) + _S46) * _S45));
            var _S50 : VolumeSample_0 = Volume_0(&(_S27), q_2, _S48);
            if((_S50.density_0) < 0.00300000002607703f)
            {
                i_2 = i_2 + i32(1);
                continue;
            }
            var _S51 : vec3<f32> = normalize(vec3<f32>(0.0f, 0.0f, _S40 * 0.5f) - q_2 + vec3<f32>(0.0f, 0.0f, 0.00100000004749745f));
            var k_0 : i32 = i32(1);
            var towardSun_0 : f32 = 0.0f;
            var towardFire_0 : f32 = 0.0f;
            for(;;)
            {
                if(k_0 <= i32(2))
                {
                }
                else
                {
                    break;
                }
                var _S52 : vec3<f32> = vec3<f32>((_S40 * 0.34999999403953552f * f32(k_0)));
                var _S53 : VolumeSample_0 = Volume_0(&(_S27), q_2 + _S47 * _S52, i32(2));
                var towardSun_1 : f32 = towardSun_0 + _S53.density_0;
                var _S54 : VolumeSample_0 = Volume_0(&(_S27), q_2 + _S51 * _S52, i32(2));
                var towardFire_1 : f32 = towardFire_0 + _S54.density_0;
                k_0 = k_0 + i32(1);
                towardSun_0 = towardSun_1;
                towardFire_0 = towardFire_1;
            }
            var _S55 : vec3<f32> = vec3<f32>(_S27.fire_0.w);
            var absorbed_0 : f32 = 1.0f - exp(- (_S50.density_0 * _S49) * _S45);
            var light_1 : vec3<f32> = light_0 + vec3<f32>((transmittance_0 * absorbed_0)) * (vec3<f32>(0.12999999523162842f, 0.11999999731779099f, 0.10999999940395355f) * (p_0.sunColour_0.xyz * vec3<f32>((mix(0.25f, 1.0f, exp(- towardSun_0 * _S49 * _S40 * 0.18000000715255737f)) * p_0.sun_0.w)) + p_0.ambient_0.xyz * vec3<f32>(1.10000002384185791f) + _S27.fire_0.xyz * _S55 * vec3<f32>(exp(- towardFire_0 * _S49 * _S40 * 0.20000000298023224f)) * vec3<f32>(0.34999999403953552f)) + BlackBody_0(_S50.heat_0) * _S55 * vec3<f32>(push_0.draw_0.y));
            transmittance_0 = transmittance_0 * (1.0f - absorbed_0);
            light_0 = light_1;
            i_2 = i_2 + i32(1);
        }
    }
    else
    {
        light_0 = _S44;
        transmittance_0 = 1.0f;
    }
    var colour_0 : vec3<f32> = light_0 + behind_1 * vec3<f32>(transmittance_0);
    var coverage_0 : f32 = saturate(1.0f - transmittance_0 + bendAmount_0 * 30.0f + dust_0);
    if(coverage_0 <= 0.00050000002374873f)
    {
        discard;
    }
    var _S56 : pixelOutput_0 = pixelOutput_0( vec4<f32>(colour_0, coverage_0) );
    return _S56;
}

