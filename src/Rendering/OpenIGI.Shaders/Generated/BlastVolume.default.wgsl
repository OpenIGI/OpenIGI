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
    @align(16) extra_0 : vec4<f32>,
};

struct _Array_std140_BlastSlot3_0
{
    @align(16) data_1 : array<BlastSlot_std140_0, i32(3)>,
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
    @align(16) blasts_0 : _Array_std140_BlastSlot3_0,
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

fn InterleavedGradient_0( pixel_0 : vec2<f32>,  frame_0 : f32) -> f32
{
    return fract(52.98291778564453125f * fract(dot(pixel_0 + vec2<f32>(frame_0) * vec2<f32>(47.0f, 17.0f) * vec2<f32>(0.69499999284744263f), vec2<f32>(0.06711056083440781f, 0.00583714991807938f))));
}

fn FireCentre_0( b_0 : ptr<function, BlastSlot_std140_0>) -> vec3<f32>
{
    var age_0 : f32 = (*b_0).time_0.x;
    var _S12 : f32 = max((*b_0).time_0.z, 0.10000000149011612f);
    var rise_0 : f32 = _S12 * (0.34999999403953552f + 0.60000002384185791f * smoothstep(0.20000000298023224f, 3.0f, age_0));
    return vec3<f32>(0.0f, 0.0f, mix(rise_0, max((*b_0).shape_0.x - _S12 * 0.40000000596046448f, rise_0), smoothstep(0.80000001192092896f, 6.0f, age_0)));
}

fn Lobe_0( seed_0 : f32,  i_1 : i32) -> vec4<f32>
{
    var k_0 : f32 = f32(i_1) + 1.0f;
    var h2_0 : f32 = Hash3_0(vec3<f32>(0.37000000476837158f, k_0 * 3.06999993324279785f, seed_0 * 0.00156999996397644f));
    return vec4<f32>(normalize(vec3<f32>(Hash3_0(vec3<f32>(seed_0 * 0.00112999998964369f, k_0 * 1.37000000476837158f, 0.70999997854232788f)) * 2.0f - 1.0f, Hash3_0(vec3<f32>(k_0 * 2.1099998950958252f, seed_0 * 0.00070999999297783f, 1.9299999475479126f)) * 2.0f - 1.0f, h2_0 * 1.39999997615814209f - 0.20000000298023224f) + vec3<f32>(0.0f, 0.0f, 0.00100000004749745f)) * vec3<f32>((0.31999999284744263f + 0.34000000357627869f * Hash3_0(vec3<f32>(seed_0 * 0.00031000000308268f + k_0, 4.09999990463256836f, k_0 * 0.52999997138977051f)))), 0.41999998688697815f + 0.25999999046325684f * h2_0);
}

var<private> gLobeAt_0 : array<vec4<f32>, i32(6)>;

var<private> gLobeCore_0 : array<f32, i32(6)>;

fn PrepareLobes_0( b_1 : ptr<function, BlastSlot_std140_0>,  fireCentre_0 : vec3<f32>)
{
    var age_1 : f32 = (*b_1).time_0.x;
    var _S13 : f32 = (*b_1).time_0.y;
    var _S14 : f32 = max((*b_1).time_0.z, 0.10000000149011612f);
    var _S15 : f32 = 1.0f - exp(- age_1 / 0.10999999940395355f);
    var i_2 : i32 = i32(0);
    for(;;)
    {
        if(i_2 < i32(6))
        {
        }
        else
        {
            break;
        }
        var lobe_0 : vec4<f32> = Lobe_0(_S13, i_2);
        var _S16 : f32 = age_1 - 0.01200000010430813f * f32(i_2);
        var _S17 : f32 = 1.0f - saturate(_S16 / 0.2800000011920929f);
        var _S18 : f32 = lobe_0.w;
        gLobeAt_0[i_2] = vec4<f32>(fireCentre_0 + lobe_0.xyz * vec3<f32>(_S14) * vec3<f32>((0.40000000596046448f + 0.60000002384185791f * (1.0f - _S17 * _S17))), 1.0f / max(_S14 * _S18 * saturate(_S16 / 0.09000000357627869f + 0.15000000596046448f), 0.05000000074505806f));
        gLobeCore_0[i_2] = 1.0f - 0.34999999403953552f * _S15 * _S18;
        i_2 = i_2 + i32(1);
    }
    return;
}

fn Billow_0( x_1 : vec3<f32>,  octaves_0 : i32) -> f32
{
    var _S19 : vec3<f32> = x_1;
    var i_3 : i32 = i32(0);
    var amplitude_0 : f32 = 0.5f;
    var sum_0 : f32 = 0.0f;
    var total_0 : f32 = 0.0f;
    for(;;)
    {
        if(i_3 < i32(4))
        {
        }
        else
        {
            break;
        }
        if(i_3 >= octaves_0)
        {
            break;
        }
        var sum_1 : f32 = sum_0 + (1.0f - abs(Noise3_0(_S19) * 2.0f - 1.0f)) * amplitude_0;
        var total_1 : f32 = total_0 + amplitude_0;
        var _S20 : f32 = _S19.x;
        var _S21 : f32 = _S19.z;
        var _S22 : vec3<f32> = vec3<f32>(_S20 * 1.70000004768371582f - _S21 * 1.10000002384185791f, _S19.y * 1.70000004768371582f + _S21 * 0.89999997615814209f, _S21 * 1.89999997615814209f + _S20 * 0.40000000596046448f) + vec3<f32>(5.69999980926513672f, 2.29999995231628418f, 9.10000038146972656f);
        var amplitude_1 : f32 = amplitude_0 * 0.5f;
        var _S23 : i32 = i_3 + i32(1);
        _S19 = _S22;
        i_3 = _S23;
        amplitude_0 = amplitude_1;
        sum_0 = sum_1;
        total_0 = total_1;
    }
    return sum_0 / max(total_0, 0.00009999999747379f);
}

fn Fbm_0( x_2 : vec3<f32>,  octaves_1 : i32) -> f32
{
    var _S24 : vec3<f32> = x_2;
    var i_4 : i32 = i32(0);
    var amplitude_2 : f32 = 0.5f;
    var sum_2 : f32 = 0.0f;
    var total_2 : f32 = 0.0f;
    for(;;)
    {
        if(i_4 < i32(5))
        {
        }
        else
        {
            break;
        }
        if(i_4 >= octaves_1)
        {
            break;
        }
        var sum_3 : f32 = sum_2 + Noise3_0(_S24) * amplitude_2;
        var total_3 : f32 = total_2 + amplitude_2;
        var _S25 : f32 = _S24.x;
        var _S26 : f32 = _S24.y;
        var _S27 : vec3<f32> = vec3<f32>(_S25 * 1.60000002384185791f + _S26 * 1.20000004768371582f, _S26 * 1.60000002384185791f - _S25 * 1.20000004768371582f, _S24.z * 2.0f) + vec3<f32>(3.09999990463256836f, 1.70000004768371582f, 5.30000019073486328f);
        var amplitude_3 : f32 = amplitude_2 * 0.5f;
        var _S28 : i32 = i_4 + i32(1);
        _S24 = _S27;
        i_4 = _S28;
        amplitude_2 = amplitude_3;
        sum_2 = sum_3;
        total_2 = total_3;
    }
    return sum_2 / max(total_2, 0.00009999999747379f);
}

struct VolumeSample_0
{
     density_0 : f32,
     heat_0 : f32,
};

fn Dust_0( b_2 : ptr<function, BlastSlot_std140_0>,  q_1 : vec3<f32>,  seedShift_0 : vec3<f32>,  octaves_2 : i32) -> VolumeSample_0
{
    var s_0 : VolumeSample_0;
    var age_2 : f32 = (*b_2).time_0.x;
    var _S29 : f32 = max((*b_2).time_0.z, 0.5f);
    var _S30 : vec4<f32> = (*b_2).shape_0;
    var _S31 : f32 = max((*b_2).shape_0.x, 0.5f);
    var _S32 : vec2<f32> = q_1.xy;
    var _S33 : f32 = q_1.z;
    var _S34 : f32 = max(_S33, 0.0f);
    var _S35 : f32 = saturate(1.0f - length(vec3<f32>(_S32 / vec2<f32>(_S29), _S34 / (_S31 * 0.75f))));
    var grain_0 : f32;
    if(_S33 > -1.0f)
    {
        grain_0 = 1.0f;
    }
    else
    {
        grain_0 = 0.0f;
    }
    var _S36 : f32 = max(_S35 * grain_0 * 0.60000002384185791f, saturate(1.0f - length(vec2<f32>(length(_S32) - _S29 * 0.77999997138977051f, _S34 - _S31 * 0.18000000715255737f)) / max(_S31 * 0.41999998688697815f, 0.30000001192092896f)));
    s_0.heat_0 = 0.0f;
    if(_S36 < 0.05000000074505806f)
    {
        s_0.density_0 = 0.0f;
        return s_0;
    }
    var m_0 : vec3<f32> = vec3<f32>(_S32, _S33 * 0.80000001192092896f) / vec3<f32>(_S29) * vec3<f32>(2.20000004768371582f) + seedShift_0;
    m_0[i32(2)] = m_0[i32(2)] - age_2 * 0.11999999731779099f;
    var _S37 : vec2<f32> = m_0.xy - normalize(_S32 + vec2<f32>(0.00100000004749745f, 0.00100000004749745f)) * vec2<f32>((age_2 * 0.18000000715255737f));
    m_0.x = _S37.x;
    m_0.y = _S37.y;
    var puff_0 : f32 = saturate((Billow_0(m_0, octaves_2) - 0.5f) * 2.40000009536743164f + 0.5f);
    if(octaves_2 >= i32(3))
    {
        grain_0 = Fbm_0(m_0 * vec3<f32>(3.29999995231628418f) + vec3<f32>(0.0f, 0.0f, age_2 * 0.30000001192092896f), i32(2));
    }
    else
    {
        grain_0 = 0.5f;
    }
    var d_1 : f32 = smoothstep(0.07999999821186066f, 0.60000002384185791f, _S36 * 1.60000002384185791f - (1.0f - puff_0) * 1.39999997615814209f);
    s_0.density_0 = saturate(d_1 - (1.0f - d_1) * grain_0 * 1.10000002384185791f) * _S30.y * 0.60000002384185791f;
    return s_0;
}

fn Volume_0( b_3 : ptr<function, BlastSlot_std140_0>,  q_2 : vec3<f32>,  octaves_3 : i32) -> VolumeSample_0
{
    var age_3 : f32 = (*b_3).time_0.x;
    var seed_1 : f32 = (*b_3).time_0.y;
    var _S38 : f32 = max((*b_3).time_0.z, 0.10000000149011612f);
    var heat_1 : f32 = (*b_3).time_0.w;
    var top_0 : f32 = (*b_3).shape_0.x;
    var smoke_0 : f32 = (*b_3).shape_0.y;
    var seedShift_1 : vec3<f32> = vec3<f32>(fract(seed_1 * 0.00130000000353903f) * 97.0f, fract(seed_1 * 0.00069999997504056f) * 61.0f, fract(seed_1 * 0.00209999992512167f) * 43.0f);
    if(((*b_3).extra_0.x) > 0.5f)
    {
        var _S39 : VolumeSample_0 = Dust_0(&((*b_3)), q_2, seedShift_1, octaves_3);
        return _S39;
    }
    var s_1 : VolumeSample_0;
    var rise_1 : f32 = _S38 * (0.34999999403953552f + 0.60000002384185791f * smoothstep(0.20000000298023224f, 3.0f, age_3));
    var _S40 : f32 = max(top_0 - _S38 * 0.40000000596046448f, rise_1);
    var _S41 : f32 = length(q_2 - vec3<f32>(0.0f, 0.0f, mix(rise_1, _S40, smoothstep(0.80000001192092896f, 6.0f, age_3))));
    var _S42 : f32 = saturate(1.0f - _S41 / (_S38 * 0.60000002384185791f));
    var ball_0 : f32 = saturate(1.0f - _S41 / (_S38 * 0.77999997138977051f));
    var core_0 : f32 = _S42;
    var i_5 : i32 = i32(0);
    for(;;)
    {
        if(i_5 < i32(6))
        {
        }
        else
        {
            break;
        }
        var d_2 : f32 = length(q_2 - gLobeAt_0[i_5].xyz) * gLobeAt_0[i_5].w;
        var _S43 : f32 = max(ball_0, saturate(1.0f - d_2));
        var _S44 : f32 = max(core_0, saturate(1.0f - d_2 * 1.29999995231628418f) * gLobeCore_0[i_5]);
        var _S45 : i32 = i_5 + i32(1);
        ball_0 = _S43;
        core_0 = _S44;
        i_5 = _S45;
    }
    var _S46 : f32 = q_2.z;
    var _S47 : f32 = length(q_2.xy);
    var _S48 : f32 = _S47 / max(_S38 * mix(0.55000001192092896f, 0.2800000011920929f, saturate(_S46 / max(_S40, 0.10000000149011612f))) * smoothstep(0.15000000596046448f, 1.20000004768371582f, age_3), 0.05000000074505806f);
    var fine_0 : f32;
    if(_S46 < 0.0f)
    {
        fine_0 = - _S46 / _S38 * 4.0f;
    }
    else
    {
        fine_0 = 0.0f;
    }
    var _S49 : f32 = _S48 + fine_0;
    if(_S46 > _S40)
    {
        fine_0 = (_S46 - _S40) / _S38 * 4.0f;
    }
    else
    {
        fine_0 = 0.0f;
    }
    var capR_0 : f32 = _S38 * mix(0.55000001192092896f, 0.94999998807907104f, smoothstep(0.40000000596046448f, 6.0f, age_3));
    var _S50 : f32 = max(ball_0, max(saturate(1.0f - (_S49 + fine_0)) * smoke_0 * 0.85000002384185791f, saturate(1.0f - length(vec2<f32>(_S47 - capR_0 * 0.55000001192092896f, (_S46 - _S40) * 1.60000002384185791f)) / max(capR_0 * 0.60000002384185791f, 0.10000000149011612f)) * smoothstep(0.5f, 1.60000002384185791f, age_3) * smoke_0 * 0.80000001192092896f));
    s_1.heat_0 = 0.0f;
    if(_S50 <= 0.0f)
    {
        s_1.density_0 = 0.0f;
        return s_1;
    }
    var boil_0 : f32 = age_3 * 0.34999999403953552f + 0.89999997615814209f * (1.0f - exp(- age_3 / 0.44999998807907104f));
    var n_0 : vec3<f32> = q_2 / vec3<f32>(max(_S38, 1.0f)) * vec3<f32>(2.59999990463256836f) + seedShift_1;
    n_0[i32(2)] = n_0[i32(2)] - boil_0;
    var _S51 : vec3<f32> = n_0;
    var puffs_0 : f32;
    var warped_0 : vec3<f32>;
    if(octaves_3 >= i32(2))
    {
        var _S52 : vec3<f32> = vec3<f32>(0.5f);
        var warped_1 : vec3<f32> = n_0 + (vec3<f32>(Noise3_0(n_0 * _S52), Noise3_0(n_0 * _S52 + vec3<f32>(7.30000019073486328f)), Noise3_0(n_0 * _S52 + vec3<f32>(13.10000038146972656f))) - _S52) * vec3<f32>(1.70000004768371582f);
        var _S53 : f32 = Billow_0(warped_1 * vec3<f32>(0.69999998807907104f), max(octaves_3 - i32(1), i32(2)));
        fine_0 = Fbm_0(warped_1, octaves_3);
        puffs_0 = _S53;
        warped_0 = warped_1;
    }
    else
    {
        var fine_1 : f32 = Noise3_0(n_0);
        fine_0 = fine_1;
        puffs_0 = fine_1;
        warped_0 = _S51;
    }
    var detail_0 : f32 = saturate((mix(fine_0, puffs_0, 0.55000001192092896f) - 0.5f) * 2.5f + 0.5f);
    var density_1 : f32 = smoothstep(0.0f, 0.44999998807907104f, _S50 * 1.89999997615814209f - (1.0f - detail_0));
    var _S54 : bool;
    if(octaves_3 >= i32(3))
    {
        _S54 = density_1 > 0.0f;
    }
    else
    {
        _S54 = false;
    }
    if(_S54)
    {
        _S54 = density_1 < 0.89999997615814209f;
    }
    else
    {
        _S54 = false;
    }
    var density_2 : f32;
    if(_S54)
    {
        var wisp_0 : f32 = Fbm_0(warped_0 * vec3<f32>(3.09999990463256836f) + vec3<f32>(0.0f, 0.0f, - boil_0 * 1.70000004768371582f), i32(2));
        density_2 = saturate((density_1 - (1.0f - density_1) * wisp_0 * 0.75f) / max(1.0f - wisp_0 * 0.30000001192092896f, 0.30000001192092896f));
    }
    else
    {
        density_2 = density_1;
    }
    var _S55 : f32 = saturate(core_0 * (0.20000000298023224f + detail_0 * 1.10000002384185791f) * smoothstep(0.07999999821186066f, 0.5f, ball_0) * 1.29999995231628418f) * heat_1;
    s_1.heat_0 = _S55;
    s_1.density_0 = density_2 * mix(1.0f, 0.80000001192092896f, _S55) * max(smoke_0, heat_1);
    return s_1;
}

fn HenyeyGreenstein_0( g_0 : f32,  cosTheta_0 : f32) -> f32
{
    var g2_0 : f32 = g_0 * g_0;
    var _S56 : f32 = max(1.0f + g2_0 - 2.0f * g_0 * cosTheta_0, 0.00009999999747379f);
    return (1.0f - g2_0) / (12.56637096405029297f * _S56 * sqrt(_S56));
}

fn DualLobePhase_0( cosTheta_1 : f32) -> f32
{
    return mix(HenyeyGreenstein_0(0.60000002384185791f, cosTheta_1), HenyeyGreenstein_0(-0.25f, cosTheta_1), 0.30000001192092896f);
}

fn BlackBody_0( heat_2 : f32) -> vec3<f32>
{
    var t_0 : f32 = saturate(heat_2);
    return vec3<f32>(1.0f, pow(t_0, 1.60000002384185791f) * 0.5f, pow(t_0, 4.0f) * 0.18000000715255737f) * vec3<f32>((pow(t_0, 2.40000009536743164f) * 16.0f));
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
fn fragmentMain( _S57 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> pixelOutput_0
{
    var _S58 : BlastSlot_std140_0 = p_0.blasts_0.data_1[clamp(i32(push_0.draw_0.x + 0.5f), i32(0), i32(2))];
    var _S59 : vec2<i32> = vec2<i32>(position_2.xy);
    var _S60 : vec2<f32> = vec2<f32>(_S59);
    var _S61 : vec2<f32> = vec2<f32>(0.5f);
    var uv_0 : vec2<f32> = (_S60 + _S61) * p_0.screen_0.zw;
    var dir_1 : vec3<f32> = normalize(_S57.viewPos_1);
    var _S62 : vec3<i32> = vec3<i32>(_S59, i32(0));
    var depth_1 : f32 = (textureLoad((uBlastDepth_0), ((_S62)).xy, ((_S62)).z));
    var _S63 : bool = depth_1 > 0.0f;
    var sceneZ_0 : f32;
    if(_S63)
    {
        sceneZ_0 = ViewDepthAt_0(depth_1, p_0.unproject_0);
    }
    else
    {
        sceneZ_0 = 1.0e+07f;
    }
    var sceneT_0 : f32 = sceneZ_0 / max(dir_1.z, 0.00009999999747379f);
    var _S64 : vec3<f32> = _S58.centre_0.xyz;
    var originW_0 : vec3<f32> = ViewToWorld_0((vec3<f32>(0) - _S64));
    var dirW_0 : vec3<f32> = ViewToWorld_0(dir_1);
    var _S65 : f32 = _S58.centre_0.w;
    if((length(_S57.viewPos_1)) < (RayBox_0(originW_0, dirW_0, vec3<f32>(_S65, _S65, _S65)).y * 0.99500000476837158f))
    {
        discard;
    }
    var _S66 : vec4<f32> = _S58.shape_0;
    var shockR_0 : f32 = _S58.shape_0.z;
    var shockStrength_0 : f32 = _S58.shape_0.w;
    const _S67 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    var _S68 : bool;
    if(shockStrength_0 > 0.00100000004749745f)
    {
        _S68 = shockR_0 > 0.10000000149011612f;
    }
    else
    {
        _S68 = false;
    }
    var dust_0 : f32;
    var hazeHeat_0 : f32;
    var bend_0 : vec2<f32>;
    if(_S68)
    {
        var width_0 : f32 = shockR_0 * 0.05999999865889549f + 0.60000002384185791f;
        var surface_0 : vec3<f32> = originW_0 + dirW_0 * vec3<f32>(min(sceneT_0, 1.0e+05f));
        if(_S63)
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
            _S68 = along_0 > 0.0f;
        }
        else
        {
            _S68 = false;
        }
        if(_S68)
        {
            var entry_0 : f32 = along_0 - sqrt(max(shockR_0 * shockR_0 - miss_0 * miss_0, 0.0f));
            if(entry_0 > 0.0f)
            {
                _S68 = entry_0 < sceneT_0;
            }
            else
            {
                _S68 = false;
            }
            if(_S68)
            {
                var grazing_0 : f32 = miss_0 / shockR_0;
                hazeHeat_0 = smoothstep(0.81999999284744263f, 0.95999997854232788f, grazing_0) * (1.0f - smoothstep(0.95999997854232788f, 1.0f, grazing_0)) * 0.55000001192092896f;
            }
            else
            {
                hazeHeat_0 = 0.0f;
            }
        }
        else
        {
            hazeHeat_0 = 0.0f;
        }
        var centreClip_0 : vec4<f32> = (((vec4<f32>(_S64, 1.0f)) * (mat4x4<f32>(p_0.projection_0.data_0[i32(0)][i32(0)], p_0.projection_0.data_0[i32(1)][i32(0)], p_0.projection_0.data_0[i32(2)][i32(0)], p_0.projection_0.data_0[i32(3)][i32(0)], p_0.projection_0.data_0[i32(0)][i32(1)], p_0.projection_0.data_0[i32(1)][i32(1)], p_0.projection_0.data_0[i32(2)][i32(1)], p_0.projection_0.data_0[i32(3)][i32(1)], p_0.projection_0.data_0[i32(0)][i32(2)], p_0.projection_0.data_0[i32(1)][i32(2)], p_0.projection_0.data_0[i32(2)][i32(2)], p_0.projection_0.data_0[i32(3)][i32(2)], p_0.projection_0.data_0[i32(0)][i32(3)], p_0.projection_0.data_0[i32(1)][i32(3)], p_0.projection_0.data_0[i32(2)][i32(3)], p_0.projection_0.data_0[i32(3)][i32(3)]))));
        var _S69 : f32 = centreClip_0.w;
        if(_S69 > 0.00100000004749745f)
        {
            bend_0 = vec2<f32>(centreClip_0.x / _S69 * 0.5f + 0.5f, 0.5f - centreClip_0.y / _S69 * 0.5f);
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
        var _S70 : vec2<f32> = outward_0 * vec2<f32>(((dust_0 + hazeHeat_0) * shockStrength_0)) * vec2<f32>(0.00999999977648258f);
        dust_0 = dust_0 * shockStrength_0 * saturate(- surface_0.z / max(shockR_0 * 0.15000000596046448f, 1.0f) + 0.60000002384185791f) * 0.34999999403953552f;
        bend_0 = _S70;
    }
    else
    {
        dust_0 = 0.0f;
        bend_0 = _S67;
    }
    var _S71 : bool = (_S58.extra_0.x) > 0.5f;
    if(_S71)
    {
        hazeHeat_0 = 0.0f;
    }
    else
    {
        hazeHeat_0 = _S58.time_0.w;
    }
    if(hazeHeat_0 > 0.05000000074505806f)
    {
        var _S72 : vec4<f32> = _S58.time_0;
        var _S73 : f32 = max(_S58.time_0.z, 0.10000000149011612f);
        var hazeR_0 : f32 = _S73 * 1.5f;
        var hazeCentre_0 : vec3<f32> = vec3<f32>(0.0f, 0.0f, _S73 * 1.60000002384185791f);
        var _S74 : f32 = max(dot(hazeCentre_0 - originW_0, dirW_0), 0.0f);
        if(_S74 < sceneT_0)
        {
            var miss_1 : f32 = length(originW_0 + dirW_0 * vec3<f32>(_S74) - hazeCentre_0);
            var _S75 : vec2<f32> = uv_0 * vec2<f32>(70.0f, 38.0f);
            var _S76 : f32 = _S72.x * 2.59999990463256836f;
            bend_0 = bend_0 + (vec2<f32>(Noise3_0(vec3<f32>(_S75, _S76)), Noise3_0(vec3<f32>(_S75 + vec2<f32>(11.69999980926513672f), _S76 + 5.09999990463256836f))) - _S61) * vec2<f32>((0.0044999998062849f * (exp(- (miss_1 * miss_1) / (hazeR_0 * hazeR_0)) * smoothstep(0.05000000074505806f, 0.5f, hazeHeat_0))));
        }
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
    var _S77 : vec4<f32> = _S58.time_0;
    var _S78 : f32 = max(_S58.time_0.z, 0.10000000149011612f);
    var top_1 : f32 = _S66.x;
    var halfSize_1 : vec3<f32>;
    if(_S71)
    {
        var _S79 : f32 = _S78 * 1.14999997615814209f;
        halfSize_1 = vec3<f32>(_S79, _S79, top_1 * 0.60000002384185791f + 1.0f);
    }
    else
    {
        var _S80 : f32 = _S78 * 1.70000004768371582f;
        halfSize_1 = vec3<f32>(_S80, _S80, (top_1 + _S78 * 1.60000002384185791f) * 0.5f);
    }
    var boxCentre_0 : vec3<f32>;
    if(_S71)
    {
        boxCentre_0 = vec3<f32>(0.0f, 0.0f, halfSize_1.z - 1.0f);
    }
    else
    {
        boxCentre_0 = vec3<f32>(0.0f, 0.0f, halfSize_1.z - _S78 * 0.60000002384185791f);
    }
    var _S81 : vec2<f32> = RayBox_0(originW_0 - boxCentre_0, dirW_0, halfSize_1);
    var span_0 : vec2<f32> = _S81;
    span_0[i32(0)] = max(_S81.x, 0.0f);
    span_0[i32(1)] = min(span_0.y, sceneT_0);
    const _S82 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    if((span_0.y) > (span_0.x))
    {
        if((_S77.w) > 0.00100000004749745f)
        {
            _S68 = true;
        }
        else
        {
            _S68 = (_S66.y) > 0.00100000004749745f;
        }
    }
    else
    {
        _S68 = false;
    }
    var transmittance_0 : f32;
    var light_0 : vec3<f32>;
    if(_S68)
    {
        var steps_0 : i32 = clamp(i32(push_0.draw_0.z + 0.5f), i32(8), i32(64));
        var _S83 : f32 = (span_0.y - span_0.x) / f32(steps_0);
        var _S84 : f32 = InterleavedGradient_0(_S60, push_0.draw_0.w);
        var _S85 : vec3<f32> = ViewToWorld_0(p_0.sun_0.xyz);
        var _S86 : i32;
        if(steps_0 >= i32(20))
        {
            _S86 = i32(4);
        }
        else
        {
            _S86 = i32(3);
        }
        if(_S71)
        {
            sceneZ_0 = 4.0f;
        }
        else
        {
            sceneZ_0 = 7.0f;
        }
        var _S87 : f32 = sceneZ_0 / max(_S78, 1.0f);
        var _S88 : f32 = _S77.w;
        var soot_0 : f32 = saturate(_S88 * 1.60000002384185791f);
        if(_S71)
        {
            behind_0 = vec3<f32>(0.62000000476837158f, 0.56000000238418579f, 0.4699999988079071f);
        }
        else
        {
            behind_0 = mix(vec3<f32>(0.36000001430511475f, 0.34000000357627869f, 0.31999999284744263f), vec3<f32>(0.07999999821186066f, 0.07199999690055847f, 0.06499999761581421f), vec3<f32>(soot_0));
        }
        if(_S71)
        {
            sceneZ_0 = 0.0f;
        }
        else
        {
            sceneZ_0 = exp(- _S77.x / 0.05000000074505806f);
        }
        var _S89 : bool = !_S71;
        if(_S89)
        {
            var _S90 : vec3<f32> = FireCentre_0(&(_S58));
            PrepareLobes_0(&(_S58), _S90);
        }
        var depthUp_0 : f32 = 0.0f;
        var depthFire_0 : f32 = 0.0f;
        var i_6 : i32 = i32(0);
        transmittance_0 = 1.0f;
        var lit_0 : i32 = i32(0);
        var depthSun_0 : f32 = 0.0f;
        light_0 = _S82;
        for(;;)
        {
            if(i_6 < i32(64))
            {
            }
            else
            {
                break;
            }
            if(i_6 >= steps_0)
            {
                _S68 = true;
            }
            else
            {
                _S68 = transmittance_0 < 0.00999999977648258f;
            }
            if(_S68)
            {
                break;
            }
            var q_3 : vec3<f32> = originW_0 + dirW_0 * vec3<f32>((span_0.x + (f32(i_6) + _S84) * _S83));
            var _S91 : VolumeSample_0 = Volume_0(&(_S58), q_3, _S86);
            var depthSun_1 : f32;
            var depthFire_1 : f32;
            if((_S91.density_0) < 0.00300000002607703f)
            {
                depthFire_1 = depthFire_0;
                depthSun_1 = depthSun_0;
                var _S92 : i32 = i_6 + i32(1);
                depthFire_0 = depthFire_1;
                i_6 = _S92;
                depthSun_0 = depthSun_1;
                continue;
            }
            var reachAll_0 : f32;
            if(_S71)
            {
                reachAll_0 = max(top_1, 2.0f) * 0.89999997615814209f;
            }
            else
            {
                reachAll_0 = _S78 * 1.29999995231628418f;
            }
            var depthUp_1 : f32;
            var o_0 : i32;
            if(((lit_0 & (i32(1)))) == i32(0))
            {
                var toCore_0 : vec3<f32> = normalize(vec3<f32>(0.0f, 0.0f, _S78 * 0.5f) - q_3 + vec3<f32>(0.0f, 0.0f, 0.00100000004749745f));
                depthUp_1 = 0.0f;
                o_0 = i32(1);
                depthSun_1 = 0.0f;
                for(;;)
                {
                    if(o_0 <= i32(3))
                    {
                    }
                    else
                    {
                        break;
                    }
                    var f_2 : f32 = f32(o_0) / 3.0f;
                    var reach_0 : f32 = reachAll_0 * f_2 * f_2;
                    var segment_0 : f32 = reach_0 - depthUp_1;
                    var _S93 : VolumeSample_0 = Volume_0(&(_S58), q_3 + _S85 * vec3<f32>(reach_0), i32(1));
                    var depthSun_2 : f32 = depthSun_1 + _S93.density_0 * segment_0;
                    var _S94 : i32 = o_0 + i32(1);
                    depthUp_1 = reach_0;
                    o_0 = _S94;
                    depthSun_1 = depthSun_2;
                }
                var _S95 : bool;
                if(_S89)
                {
                    _S95 = _S88 > 0.00999999977648258f;
                }
                else
                {
                    _S95 = false;
                }
                if(_S95)
                {
                    var _S96 : VolumeSample_0 = Volume_0(&(_S58), q_3 + toCore_0 * vec3<f32>((_S78 * 0.40000000596046448f)), i32(1));
                    depthFire_1 = _S96.density_0 * _S78 * 0.60000002384185791f;
                }
                else
                {
                    depthFire_1 = 0.0f;
                }
                var upReach_0 : f32 = reachAll_0 * 0.5f;
                var _S97 : VolumeSample_0 = Volume_0(&(_S58), q_3 + vec3<f32>(0.0f, 0.0f, upReach_0), i32(1));
                depthUp_1 = _S97.density_0 * upReach_0;
            }
            else
            {
                depthUp_1 = depthUp_0;
                depthFire_1 = depthFire_0;
                depthSun_1 = depthSun_0;
            }
            var _S98 : i32 = lit_0 + i32(1);
            var _S99 : f32 = DualLobePhase_0(dot(dirW_0, _S85));
            o_0 = i32(0);
            var phaseWeight_0 : f32 = 1.0f;
            var scatterWeight_0 : f32 = 1.0f;
            var extinctionWeight_0 : f32 = 1.0f;
            var sunLit_0 : f32 = 0.0f;
            for(;;)
            {
                if(o_0 < i32(3))
                {
                }
                else
                {
                    break;
                }
                var sunLit_1 : f32 = sunLit_0 + scatterWeight_0 * exp(- depthSun_1 * _S87 * extinctionWeight_0) * mix(0.07957746833562851f, _S99, phaseWeight_0);
                var scatterWeight_1 : f32 = scatterWeight_0 * 0.5f;
                var extinctionWeight_1 : f32 = extinctionWeight_0 * 0.40000000596046448f;
                var phaseWeight_1 : f32 = phaseWeight_0 * 0.5f;
                o_0 = o_0 + i32(1);
                phaseWeight_0 = phaseWeight_1;
                scatterWeight_0 = scatterWeight_1;
                extinctionWeight_0 = extinctionWeight_1;
                sunLit_0 = sunLit_1;
            }
            var _S100 : f32 = _S58.fire_0.w;
            var absorbed_0 : f32 = 1.0f - exp(- (_S91.density_0 * _S87) * _S83);
            var light_1 : vec3<f32> = light_0 + vec3<f32>((transmittance_0 * absorbed_0)) * (behind_0 * (p_0.sunColour_0.xyz * vec3<f32>((sunLit_0 * p_0.sun_0.w)) + p_0.ambient_0.xyz * vec3<f32>((mix(0.34999999403953552f, 1.0f, exp(- depthUp_1 * _S87 * 0.60000002384185791f)) + 0.18000000715255737f * saturate(1.0f - q_3.z / max(reachAll_0, 1.0f)))) * vec3<f32>(0.25f)) + ((BlackBody_0(_S91.heat_0) + vec3<f32>(1.0f, 0.81999999284744263f, 0.60000002384185791f) * vec3<f32>((sceneZ_0 * 60.0f * saturate(_S91.density_0 * 2.0f)))) * vec3<f32>(_S100) + behind_0 * _S58.fire_0.xyz * vec3<f32>((_S100 * _S88 * exp(- depthFire_1 * _S87) * 3.0f))) * vec3<f32>(push_0.draw_0.y));
            var transmittance_1 : f32 = transmittance_0 * (1.0f - absorbed_0);
            depthUp_0 = depthUp_1;
            transmittance_0 = transmittance_1;
            lit_0 = _S98;
            light_0 = light_1;
            var _S92 : i32 = i_6 + i32(1);
            depthFire_0 = depthFire_1;
            i_6 = _S92;
            depthSun_0 = depthSun_1;
        }
    }
    else
    {
        light_0 = _S82;
        transmittance_0 = 1.0f;
    }
    var colour_0 : vec3<f32> = light_0 + behind_1 * vec3<f32>(transmittance_0);
    var coverage_0 : f32 = saturate(1.0f - transmittance_0 + bendAmount_0 * 30.0f + dust_0);
    if(coverage_0 <= 0.00050000002374873f)
    {
        discard;
    }
    var _S101 : pixelOutput_0 = pixelOutput_0( vec4<f32>(colour_0, coverage_0) );
    return _S101;
}

