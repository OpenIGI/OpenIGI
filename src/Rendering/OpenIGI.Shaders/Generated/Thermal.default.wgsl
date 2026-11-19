struct ThermalParams_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) depth_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) sensor_0 : vec4<f32>,
    @align(16) tuning_0 : vec4<f32>,
};

@binding(4) @group(0) var<uniform> p_0 : ThermalParams_std140_0;
@binding(0) @group(0) var uSource_0 : texture_2d<f32>;

@binding(3) @group(0) var uTarget_0 : texture_storage_2d<rgba16float, write>;

@binding(1) @group(0) var uDepth_0 : texture_depth_2d;

@binding(2) @group(0) var uHeat_0 : texture_2d<f32>;

fn SourceAt_0( pixel_0 : vec2<i32>) -> vec3<f32>
{
    var _S1 : vec3<i32> = vec3<i32>(clamp(pixel_0, vec2<i32>(i32(0), i32(0)), vec2<i32>(p_0.size_0.xy) - vec2<i32>(i32(1))), i32(0));
    return (textureLoad((uSource_0), ((_S1)).xy, ((_S1)).z)).xyz;
}

fn SignalAt_0( pixel_1 : vec2<i32>) -> f32
{
    return dot(max(SourceAt_0(pixel_1), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f)) * p_0.tuning_0.x;
}

fn DepthAt_0( pixel_2 : vec2<i32>) -> f32
{
    var _S2 : vec3<i32> = vec3<i32>(clamp(vec2<i32>((vec2<f32>(pixel_2) + vec2<f32>(0.5f)) * p_0.depth_0.zw), vec2<i32>(i32(0), i32(0)), vec2<i32>(p_0.depth_0.xy) - vec2<i32>(i32(1))), i32(0));
    return (textureLoad((uDepth_0), ((_S2)).xy, ((_S2)).z));
}

fn ViewDepthAt_0( depth_1 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S3 : f32 = unproject_1.z;
    var _S4 : f32 = unproject_1.w;
    return 2.0f * _S3 * _S4 / (_S4 + _S3 - (1.0f - 2.0f * depth_1) * (_S4 - _S3));
}

fn MetresAt_0( depth_2 : f32) -> f32
{
    var _S5 : f32;
    if(depth_2 > 0.0f)
    {
        _S5 = ViewDepthAt_0(depth_2, p_0.unproject_0);
    }
    else
    {
        _S5 = p_0.unproject_0.w;
    }
    return _S5;
}

fn SkyTemperature_0( v_0 : f32) -> f32
{
    return mix(0.02999999932944775f, 0.17000000178813934f, smoothstep(0.05000000074505806f, 0.62000000476837158f, v_0));
}

fn WorldTemperature_0( signal_0 : f32) -> f32
{
    return mix(mix(0.15999999642372131f, 0.5f, saturate(log2(1.0f + max(signal_0, 0.0f) * 4.0f) / log2(5.0f))), 0.97000002861022949f, smoothstep(2.5f, 9.0f, signal_0));
}

fn HeatAt_0( pixel_3 : vec2<i32>) -> vec4<f32>
{
    var _S6 : vec3<i32> = vec3<i32>(clamp(pixel_3, vec2<i32>(i32(0), i32(0)), vec2<i32>(p_0.size_0.xy) - vec2<i32>(i32(1))), i32(0));
    return (textureLoad((uHeat_0), ((_S6)).xy, ((_S6)).z));
}

fn Hash_0( seed_0 : vec2<f32>) -> f32
{
    var q_0 : vec3<f32> = fract(seed_0.xyx * vec3<f32>(0.1031000018119812f));
    var q_1 : vec3<f32> = q_0 + vec3<f32>(dot(q_0, q_0.yzx + vec3<f32>(33.3300018310546875f)));
    return fract((q_1.x + q_1.y) * q_1.z);
}

fn Ramp_0( t_0 : f32,  c0_0 : vec3<f32>,  c1_0 : vec3<f32>,  c2_0 : vec3<f32>,  c3_0 : vec3<f32>,  c4_0 : vec3<f32>,  c5_0 : vec3<f32>,  c6_0 : vec3<f32>,  c7_0 : vec3<f32>) -> vec3<f32>
{
    var x_0 : f32 = saturate(t_0) * 7.0f;
    return mix(mix(mix(mix(mix(mix(mix(c0_0, c1_0, vec3<f32>(saturate(x_0))), c2_0, vec3<f32>(saturate(x_0 - 1.0f))), c3_0, vec3<f32>(saturate(x_0 - 2.0f))), c4_0, vec3<f32>(saturate(x_0 - 3.0f))), c5_0, vec3<f32>(saturate(x_0 - 4.0f))), c6_0, vec3<f32>(saturate(x_0 - 5.0f))), c7_0, vec3<f32>(saturate(x_0 - 6.0f)));
}

fn Palette_0( t_1 : f32,  palette_0 : i32) -> vec3<f32>
{
    if(palette_0 == i32(1))
    {
        var k_0 : f32 = pow(1.0f - saturate(t_1), 0.89999997615814209f);
        return vec3<f32>(k_0, k_0, k_0) * vec3<f32>(0.97000002861022949f, 0.99000000953674316f, 1.0f);
    }
    if(palette_0 == i32(2))
    {
        return Ramp_0(t_1, vec3<f32>(0.0f, 0.0f, 0.03999999910593033f), vec3<f32>(0.11999999731779099f, 0.00999999977648258f, 0.30000001192092896f), vec3<f32>(0.40000000596046448f, 0.01999999955296516f, 0.51999998092651367f), vec3<f32>(0.72000002861022949f, 0.07999999821186066f, 0.40000000596046448f), vec3<f32>(0.89999997615814209f, 0.30000001192092896f, 0.10000000149011612f), vec3<f32>(0.98000001907348633f, 0.57999998331069946f, 0.01999999955296516f), vec3<f32>(1.0f, 0.8399999737739563f, 0.30000001192092896f), vec3<f32>(1.0f, 0.99000000953674316f, 0.86000001430511475f));
    }
    if(palette_0 == i32(3))
    {
        return Ramp_0(t_1, vec3<f32>(0.0f, 0.0f, 0.10000000149011612f), vec3<f32>(0.05000000074505806f, 0.07999999821186066f, 0.55000001192092896f), vec3<f32>(0.0f, 0.44999998807907104f, 0.85000002384185791f), vec3<f32>(0.0f, 0.75f, 0.44999998807907104f), vec3<f32>(0.55000001192092896f, 0.87999999523162842f, 0.05000000074505806f), vec3<f32>(1.0f, 0.69999998807907104f, 0.0f), vec3<f32>(0.94999998807907104f, 0.15000000596046448f, 0.05000000074505806f), vec3<f32>(1.0f, 0.94999998807907104f, 0.92000001668930054f));
    }
    return mix(vec3<f32>(0.01499999966472387f, 0.01999999955296516f, 0.02999999932944775f), vec3<f32>(1.0f, 1.0f, 0.98500001430511475f), vec3<f32>(pow(saturate(t_1), 0.89999997615814209f)));
}

fn DecodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(pow(max((c_0 + vec3<f32>(0.05499999970197678f)) / vec3<f32>(1.0549999475479126f), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(2.40000009536743164f, 2.40000009536743164f, 2.40000009536743164f)), c_0 / vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.04044999927282333f, 0.04044999927282333f, 0.04044999927282333f)));
}

@compute
@workgroup_size(8, 8, 1)
fn main(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S7 : vec2<u32> = id_0.xy;
    if((any((_S7 >= vec2<u32>(p_0.size_0.xy)))))
    {
        return;
    }
    var _S8 : vec2<i32> = vec2<i32>(_S7);
    var _S9 : vec2<f32> = vec2<f32>(_S8);
    var _S10 : vec2<f32> = vec2<f32>(0.5f);
    var uv_0 : vec2<f32> = (_S9 + _S10) * p_0.size_0.zw;
    var scene_0 : vec3<f32> = SourceAt_0(_S8);
    var amount_0 : f32 = saturate(p_0.sensor_0.x);
    if(amount_0 <= 0.0f)
    {
        textureStore((uTarget_0), (vec2<u32>(_S8)), (vec4<f32>(scene_0, 1.0f)));
        return;
    }
    var signals_0 : array<f32, i32(9)>;
    var metres_0 : array<f32, i32(9)>;
    var _S11 : vec2<i32> = _S8 + vec2<i32>(i32(-1), i32(-1));
    var _S12 : f32 = SignalAt_0(_S11);
    signals_0[i32(0)] = _S12;
    metres_0[i32(0)] = MetresAt_0(DepthAt_0(_S11));
    var _S13 : vec2<i32> = _S8 + vec2<i32>(i32(0), i32(-1));
    var _S14 : f32 = SignalAt_0(_S13);
    signals_0[i32(1)] = _S14;
    metres_0[i32(1)] = MetresAt_0(DepthAt_0(_S13));
    var _S15 : vec2<i32> = _S8 + vec2<i32>(i32(1), i32(-1));
    var _S16 : f32 = SignalAt_0(_S15);
    signals_0[i32(2)] = _S16;
    metres_0[i32(2)] = MetresAt_0(DepthAt_0(_S15));
    var _S17 : vec2<i32> = _S8 + vec2<i32>(i32(-1), i32(0));
    var _S18 : f32 = SignalAt_0(_S17);
    signals_0[i32(3)] = _S18;
    metres_0[i32(3)] = MetresAt_0(DepthAt_0(_S17));
    var _S19 : f32 = SignalAt_0(_S8);
    signals_0[i32(4)] = _S19;
    var _S20 : f32 = DepthAt_0(_S8);
    var _S21 : f32 = MetresAt_0(_S20);
    metres_0[i32(4)] = _S21;
    var _S22 : vec2<i32> = _S8 + vec2<i32>(i32(1), i32(0));
    var _S23 : f32 = SignalAt_0(_S22);
    signals_0[i32(5)] = _S23;
    metres_0[i32(5)] = MetresAt_0(DepthAt_0(_S22));
    var _S24 : vec2<i32> = _S8 + vec2<i32>(i32(-1), i32(1));
    var _S25 : f32 = SignalAt_0(_S24);
    signals_0[i32(6)] = _S25;
    metres_0[i32(6)] = MetresAt_0(DepthAt_0(_S24));
    var _S26 : vec2<i32> = _S8 + vec2<i32>(i32(0), i32(1));
    var _S27 : f32 = SignalAt_0(_S26);
    signals_0[i32(7)] = _S27;
    metres_0[i32(7)] = MetresAt_0(DepthAt_0(_S26));
    var _S28 : vec2<i32> = _S8 + vec2<i32>(i32(1), i32(1));
    var _S29 : f32 = SignalAt_0(_S28);
    signals_0[i32(8)] = _S29;
    metres_0[i32(8)] = MetresAt_0(DepthAt_0(_S28));
    var around_0 : f32 = (_S14 + _S18 + _S23 + _S27) * 0.25f;
    var corners_0 : f32 = (_S12 + _S16 + _S25 + _S29) * 0.25f;
    var signal_1 : f32 = _S19 * 0.5f + around_0 * 0.375f + corners_0 * 0.125f;
    var sky_0 : bool = _S20 <= 0.0f;
    var t_2 : f32;
    if(sky_0)
    {
        t_2 = SkyTemperature_0(uv_0.y);
    }
    else
    {
        t_2 = WorldTemperature_0(signal_1);
    }
    if(!sky_0)
    {
        var gx_0 : f32 = log2(metres_0[i32(2)] + 1.0f) + 2.0f * log2(metres_0[i32(5)] + 1.0f) + log2(metres_0[i32(8)] + 1.0f) - (log2(metres_0[i32(0)] + 1.0f) + 2.0f * log2(metres_0[i32(3)] + 1.0f) + log2(metres_0[i32(6)] + 1.0f));
        var gy_0 : f32 = log2(metres_0[i32(6)] + 1.0f) + 2.0f * log2(metres_0[i32(7)] + 1.0f) + log2(metres_0[i32(8)] + 1.0f) - (log2(metres_0[i32(0)] + 1.0f) + 2.0f * log2(metres_0[i32(1)] + 1.0f) + log2(metres_0[i32(2)] + 1.0f));
        t_2 = mix(t_2 + (clamp((WorldTemperature_0(signals_0[i32(4)]) - WorldTemperature_0((around_0 + corners_0) * 0.5f)) * 0.89999997615814209f, -0.07999999821186066f, 0.07999999821186066f) + saturate(sqrt(gx_0 * gx_0 + gy_0 * gy_0) * 0.34999999403953552f) * 0.03500000014901161f), 0.23999999463558197f, (1.0f - exp(- _S21 / 900.0f)) * 0.64999997615814209f);
    }
    var heat_0 : vec4<f32> = HeatAt_0(_S8);
    var offset_0 : vec2<f32> = vec2<f32>(cos(0.0f), sin(0.0f));
    var _S30 : vec2<f32> = vec2<f32>(3.0f);
    var _S31 : vec2<f32> = vec2<f32>(7.0f);
    var offset_1 : vec2<f32> = vec2<f32>(cos(0.78539818525314331f), sin(0.78539818525314331f));
    var offset_2 : vec2<f32> = vec2<f32>(cos(1.57079637050628662f), sin(1.57079637050628662f));
    var offset_3 : vec2<f32> = vec2<f32>(cos(2.35619449615478516f), sin(2.35619449615478516f));
    var offset_4 : vec2<f32> = vec2<f32>(cos(3.14159274101257324f), sin(3.14159274101257324f));
    var offset_5 : vec2<f32> = vec2<f32>(cos(3.92699098587036133f), sin(3.92699098587036133f));
    var offset_6 : vec2<f32> = vec2<f32>(cos(4.71238899230957031f), sin(4.71238899230957031f));
    var offset_7 : vec2<f32> = vec2<f32>(cos(5.4977874755859375f), sin(5.4977874755859375f));
    var halo_0 : f32 = (HeatAt_0(_S8 + vec2<i32>(round(offset_0 * _S30))).y + HeatAt_0(_S8 + vec2<i32>(round(offset_0 * _S31))).y * 0.60000002384185791f + HeatAt_0(_S8 + vec2<i32>(round(offset_1 * _S30))).y + HeatAt_0(_S8 + vec2<i32>(round(offset_1 * _S31))).y * 0.60000002384185791f + HeatAt_0(_S8 + vec2<i32>(round(offset_2 * _S30))).y + HeatAt_0(_S8 + vec2<i32>(round(offset_2 * _S31))).y * 0.60000002384185791f + HeatAt_0(_S8 + vec2<i32>(round(offset_3 * _S30))).y + HeatAt_0(_S8 + vec2<i32>(round(offset_3 * _S31))).y * 0.60000002384185791f + HeatAt_0(_S8 + vec2<i32>(round(offset_4 * _S30))).y + HeatAt_0(_S8 + vec2<i32>(round(offset_4 * _S31))).y * 0.60000002384185791f + HeatAt_0(_S8 + vec2<i32>(round(offset_5 * _S30))).y + HeatAt_0(_S8 + vec2<i32>(round(offset_5 * _S31))).y * 0.60000002384185791f + HeatAt_0(_S8 + vec2<i32>(round(offset_6 * _S30))).y + HeatAt_0(_S8 + vec2<i32>(round(offset_6 * _S31))).y * 0.60000002384185791f + HeatAt_0(_S8 + vec2<i32>(round(offset_7 * _S30))).y + HeatAt_0(_S8 + vec2<i32>(round(offset_7 * _S31))).y * 0.60000002384185791f) / 12.80000019073486328f;
    if((heat_0.w) > 0.0f)
    {
        var body_0 : f32 = 1.0f / max(heat_0.z, 0.00009999999747379f) - 1.0f;
        var _S32 : f32;
        if(sky_0)
        {
            _S32 = p_0.unproject_0.w;
        }
        else
        {
            _S32 = _S21;
        }
        var gap_0 : f32 = body_0 - _S32;
        var tolerance_0 : f32 = 0.20000000298023224f + body_0 * 0.00999999977648258f;
        if(gap_0 <= tolerance_0)
        {
            t_2 = max(t_2, 0.5f + 0.5f * heat_0.x);
        }
        else
        {
            var _S33 : f32 = heat_0.y;
            t_2 = mix(t_2, 0.41999998688697815f + 0.36000001430511475f * _S33, _S33 * (1.0f - smoothstep(0.0f, p_0.tuning_0.z, gap_0 - tolerance_0)) * 0.60000002384185791f);
        }
    }
    else
    {
        t_2 = t_2 + halo_0 * 0.11999999731779099f;
    }
    var radius_0 : f32 = length((uv_0 - _S10) * vec2<f32>(p_0.size_0.x * p_0.size_0.w, 1.0f));
    var calibration_0 : f32 = saturate(p_0.sensor_0.w);
    var frame_0 : f32 = p_0.tuning_0.w;
    var grain_0 : f32 = Hash_0(_S9 + vec2<f32>(frame_0 * 7.13000011444091797f, frame_0 * 3.71000003814697266f)) - 0.5f;
    textureStore((uTarget_0), (vec2<u32>(_S8)), (vec4<f32>(mix(scene_0, DecodeSrgb_0(saturate(Palette_0(mix(t_2 + 0.01799999922513962f * (1.0f - smoothstep(0.0f, 0.31999999284744263f, radius_0)) + (grain_0 * 0.02999999932944775f * (p_0.tuning_0.y * (1.0f + calibration_0 * 4.0f)) + (Hash_0(vec2<f32>(f32(_S8.x), 17.0f)) - 0.5f) * 0.01200000010430813f * p_0.tuning_0.y + (Hash_0(vec2<f32>(41.0f, f32(_S8.y))) - 0.5f) * 0.00499999988824129f * p_0.tuning_0.y), 0.34000000357627869f + grain_0 * 0.01999999955296516f, smoothstep(0.62000000476837158f, 0.80000001192092896f, calibration_0)), i32(p_0.sensor_0.y + 0.5f)) * vec3<f32>((1.0f - 0.40000000596046448f * smoothstep(0.41999998688697815f, 0.94999998807907104f, radius_0))))) * vec3<f32>(1.29999995231628418f) / vec3<f32>(max(p_0.tuning_0.x, 0.00100000004749745f)), vec3<f32>(amount_0)), 1.0f)));
    return;
}

