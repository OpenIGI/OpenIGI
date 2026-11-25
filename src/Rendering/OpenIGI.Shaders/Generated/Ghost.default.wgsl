struct GhostParams_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) depth_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) look_0 : vec4<f32>,
    @align(16) tint_0 : vec4<f32>,
    @align(16) hot_0 : vec4<f32>,
    @align(16) cloak_0 : vec4<f32>,
};

@binding(5) @group(0) var<uniform> p_0 : GhostParams_std140_0;
@binding(0) @group(0) var uSource_0 : texture_2d<f32>;

@binding(2) @group(0) var uGhostShape_0 : texture_2d<f32>;

@binding(3) @group(0) var uGhostSkin_0 : texture_2d<f32>;

@binding(4) @group(0) var uTarget_0 : texture_storage_2d<rgba16float, write>;

@binding(1) @group(0) var uDepth_0 : texture_depth_2d;

fn SourceAt_0( pixel_0 : vec2<i32>) -> vec3<f32>
{
    var _S1 : vec3<i32> = vec3<i32>(clamp(pixel_0, vec2<i32>(i32(0), i32(0)), vec2<i32>(p_0.size_0.xy) - vec2<i32>(i32(1))), i32(0));
    return (textureLoad((uSource_0), ((_S1)).xy, ((_S1)).z)).xyz;
}

fn Hash_0( seed_0 : vec2<f32>) -> f32
{
    var q_0 : vec3<f32> = fract(seed_0.xyx * vec3<f32>(0.1031000018119812f));
    var q_1 : vec3<f32> = q_0 + vec3<f32>(dot(q_0, q_0.yzx + vec3<f32>(33.3300018310546875f)));
    return fract((q_1.x + q_1.y) * q_1.z);
}

struct GhostSample_0
{
     covered_0 : f32,
     normal_0 : vec2<f32>,
     metres_0 : f32,
     height_0 : f32,
     noise_0 : f32,
     rim_0 : f32,
};

fn GhostAt_0( pixel_1 : vec2<i32>) -> GhostSample_0
{
    var g_0 : GhostSample_0;
    var _S2 : vec2<i32> = vec2<i32>(p_0.size_0.xy);
    var _S3 : bool;
    if((any((pixel_1 < vec2<i32>(i32(0), i32(0))))))
    {
        _S3 = true;
    }
    else
    {
        _S3 = (any((pixel_1 >= _S2)));
    }
    if(_S3)
    {
        g_0.covered_0 = 0.0f;
        g_0.normal_0 = vec2<f32>(0.0f, 0.0f);
        g_0.metres_0 = 0.0f;
        g_0.height_0 = 0.0f;
        g_0.noise_0 = 0.0f;
        g_0.rim_0 = 0.0f;
        return g_0;
    }
    var _S4 : vec3<i32> = vec3<i32>(pixel_1, i32(0));
    var shape_0 : vec4<f32> = (textureLoad((uGhostShape_0), ((_S4)).xy, ((_S4)).z));
    var skin_0 : vec4<f32> = (textureLoad((uGhostSkin_0), ((_S4)).xy, ((_S4)).z));
    g_0.covered_0 = shape_0.w;
    g_0.normal_0 = shape_0.xy;
    g_0.metres_0 = shape_0.z;
    g_0.height_0 = skin_0.x;
    g_0.noise_0 = skin_0.y;
    g_0.rim_0 = skin_0.z;
    return g_0;
}

fn Bayer4_0( pixel_2 : vec2<i32>) -> f32
{
    var y_0 : u32 = (u32(pixel_2.y) & (u32(3)));
    var a_0 : u32 = (((u32(pixel_2.x) & (u32(3)))) ^ (y_0));
    return (f32((((((((((a_0 & (u32(1)))) << (u32(3)))) | (((((y_0 & (u32(1)))) << (u32(2))))))) | (((a_0 & (u32(2))))))) | (((((y_0 & (u32(2)))) >> (u32(1))))))) + 0.5f) / 16.0f;
}

fn HaloAt_0( at_0 : vec2<i32>) -> vec2<f32>
{
    var _S5 : vec2<f32> = vec2<f32>(4.0f);
    var g_1 : GhostSample_0 = GhostAt_0(at_0 + vec2<i32>(round(vec2<f32>(cos(0.0f), sin(0.0f)) * _S5)));
    var nearest_0 : f32;
    if((g_1.covered_0) > 0.0f)
    {
        nearest_0 = min(1.0e+06f, g_1.metres_0);
    }
    else
    {
        nearest_0 = 1.0e+06f;
    }
    var g_2 : GhostSample_0 = GhostAt_0(at_0 + vec2<i32>(round(vec2<f32>(cos(0.78539818525314331f), sin(0.78539818525314331f)) * _S5)));
    var ring_0 : f32 = g_1.covered_0 + g_2.covered_0;
    if((g_2.covered_0) > 0.0f)
    {
        nearest_0 = min(nearest_0, g_2.metres_0);
    }
    var g_3 : GhostSample_0 = GhostAt_0(at_0 + vec2<i32>(round(vec2<f32>(cos(1.57079637050628662f), sin(1.57079637050628662f)) * _S5)));
    var ring_1 : f32 = ring_0 + g_3.covered_0;
    if((g_3.covered_0) > 0.0f)
    {
        nearest_0 = min(nearest_0, g_3.metres_0);
    }
    var g_4 : GhostSample_0 = GhostAt_0(at_0 + vec2<i32>(round(vec2<f32>(cos(2.35619449615478516f), sin(2.35619449615478516f)) * _S5)));
    var ring_2 : f32 = ring_1 + g_4.covered_0;
    if((g_4.covered_0) > 0.0f)
    {
        nearest_0 = min(nearest_0, g_4.metres_0);
    }
    var g_5 : GhostSample_0 = GhostAt_0(at_0 + vec2<i32>(round(vec2<f32>(cos(3.14159274101257324f), sin(3.14159274101257324f)) * _S5)));
    var ring_3 : f32 = ring_2 + g_5.covered_0;
    if((g_5.covered_0) > 0.0f)
    {
        nearest_0 = min(nearest_0, g_5.metres_0);
    }
    var g_6 : GhostSample_0 = GhostAt_0(at_0 + vec2<i32>(round(vec2<f32>(cos(3.92699098587036133f), sin(3.92699098587036133f)) * _S5)));
    var ring_4 : f32 = ring_3 + g_6.covered_0;
    if((g_6.covered_0) > 0.0f)
    {
        nearest_0 = min(nearest_0, g_6.metres_0);
    }
    var g_7 : GhostSample_0 = GhostAt_0(at_0 + vec2<i32>(round(vec2<f32>(cos(4.71238899230957031f), sin(4.71238899230957031f)) * _S5)));
    var ring_5 : f32 = ring_4 + g_7.covered_0;
    if((g_7.covered_0) > 0.0f)
    {
        nearest_0 = min(nearest_0, g_7.metres_0);
    }
    var g_8 : GhostSample_0 = GhostAt_0(at_0 + vec2<i32>(round(vec2<f32>(cos(5.4977874755859375f), sin(5.4977874755859375f)) * _S5)));
    var ring_6 : f32 = ring_5 + g_8.covered_0;
    if((g_8.covered_0) > 0.0f)
    {
        nearest_0 = min(nearest_0, g_8.metres_0);
    }
    return vec2<f32>(ring_6, nearest_0);
}

fn CoverageAt_0( pixel_3 : vec2<i32>) -> f32
{
    var _S6 : vec2<i32> = vec2<i32>(p_0.size_0.xy);
    var _S7 : bool;
    if((any((pixel_3 < vec2<i32>(i32(0), i32(0))))))
    {
        _S7 = true;
    }
    else
    {
        _S7 = (any((pixel_3 >= _S6)));
    }
    var _S8 : f32;
    if(_S7)
    {
        _S8 = 0.0f;
    }
    else
    {
        var _S9 : vec3<i32> = vec3<i32>(pixel_3, i32(0));
        _S8 = (textureLoad((uGhostShape_0), ((_S9)).xy, ((_S9)).z)).w;
    }
    return _S8;
}

fn EdgeAt_0( at_1 : vec2<i32>) -> f32
{
    if((CoverageAt_0(at_1)) <= 0.0f)
    {
        return 0.0f;
    }
    var _S10 : vec2<f32> = vec2<f32>(1.5f);
    return saturate((8.0f - (CoverageAt_0(at_1 + vec2<i32>(round(vec2<f32>(cos(0.0f), sin(0.0f)) * _S10))) + CoverageAt_0(at_1 + vec2<i32>(round(vec2<f32>(cos(0.78539818525314331f), sin(0.78539818525314331f)) * _S10))) + CoverageAt_0(at_1 + vec2<i32>(round(vec2<f32>(cos(1.57079637050628662f), sin(1.57079637050628662f)) * _S10))) + CoverageAt_0(at_1 + vec2<i32>(round(vec2<f32>(cos(2.35619449615478516f), sin(2.35619449615478516f)) * _S10))) + CoverageAt_0(at_1 + vec2<i32>(round(vec2<f32>(cos(3.14159274101257324f), sin(3.14159274101257324f)) * _S10))) + CoverageAt_0(at_1 + vec2<i32>(round(vec2<f32>(cos(3.92699098587036133f), sin(3.92699098587036133f)) * _S10))) + CoverageAt_0(at_1 + vec2<i32>(round(vec2<f32>(cos(4.71238899230957031f), sin(4.71238899230957031f)) * _S10))) + CoverageAt_0(at_1 + vec2<i32>(round(vec2<f32>(cos(5.4977874755859375f), sin(5.4977874755859375f)) * _S10))))) * 0.5f);
}

fn Square_0( x_0 : f32) -> f32
{
    return x_0 * x_0;
}

fn ViewDepthAt_0( depth_1 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S11 : f32 = unproject_1.z;
    var _S12 : f32 = unproject_1.w;
    return 2.0f * _S11 * _S12 / (_S12 + _S11 - (1.0f - 2.0f * depth_1) * (_S12 - _S11));
}

fn SceneMetresAt_0( pixel_4 : vec2<i32>) -> f32
{
    var _S13 : vec3<i32> = vec3<i32>(clamp(vec2<i32>((vec2<f32>(pixel_4) + vec2<f32>(0.5f)) * p_0.depth_0.zw), vec2<i32>(i32(0), i32(0)), vec2<i32>(p_0.depth_0.xy) - vec2<i32>(i32(1))), i32(0));
    var d_0 : f32 = (textureLoad((uDepth_0), ((_S13)).xy, ((_S13)).z));
    var _S14 : f32;
    if(d_0 > 0.0f)
    {
        _S14 = ViewDepthAt_0(d_0, p_0.unproject_0);
    }
    else
    {
        _S14 = p_0.unproject_0.w;
    }
    return _S14;
}

fn SolidBody_0( g_9 : GhostSample_0,  pixel_5 : vec2<i32>) -> vec3<f32>
{
    var _S15 : vec2<f32> = g_9.normal_0;
    var shade_0 : f32 = 0.2199999988079071f + 0.77999997138977051f * (floor(saturate(dot(vec3<f32>(g_9.normal_0, - sqrt(saturate(1.0f - dot(_S15, _S15)))), normalize(vec3<f32>(-0.34999999403953552f, 0.55000001192092896f, -0.75f)))) * 3.0f + 0.5f) / 3.0f);
    var rows_0 : f32;
    if((((pixel_5.y) & (i32(1)))) == i32(0))
    {
        rows_0 = 1.0f;
    }
    else
    {
        rows_0 = 0.81999999284744263f;
    }
    return p_0.tint_0.xyz * vec3<f32>(shade_0) * vec3<f32>(rows_0) * vec3<f32>(0.89999997615814209f) + p_0.hot_0.xyz * vec3<f32>(Square_0(g_9.rim_0)) * vec3<f32>(0.34999999403953552f);
}

fn SourceBetween_0( at_2 : vec2<f32>) -> vec3<f32>
{
    var _S16 : vec2<f32> = at_2 - vec2<f32>(0.5f);
    var base_0 : vec2<f32> = floor(_S16);
    var f_0 : vec2<f32> = _S16 - base_0;
    var _S17 : vec2<i32> = vec2<i32>(base_0);
    var _S18 : vec3<f32> = vec3<f32>(f_0.x);
    return mix(mix(SourceAt_0(_S17), SourceAt_0(_S17 + vec2<i32>(i32(1), i32(0))), _S18), mix(SourceAt_0(_S17 + vec2<i32>(i32(0), i32(1))), SourceAt_0(_S17 + vec2<i32>(i32(1), i32(1))), _S18), vec3<f32>(f_0.y));
}

fn HexDistance_0( at_3 : vec2<f32>) -> f32
{
    const r_0 : vec2<f32> = vec2<f32>(1.0f, 1.73205077648162842f);
    var h_0 : vec2<f32> = r_0 * vec2<f32>(0.5f);
    var a_1 : vec2<f32> = at_3 - r_0 * floor(at_3 / r_0) - h_0;
    var _S19 : vec2<f32> = at_3 - h_0;
    var b_0 : vec2<f32> = _S19 - r_0 * floor(_S19 / r_0) - h_0;
    var g_10 : vec2<f32>;
    if((dot(a_1, a_1)) < (dot(b_0, b_0)))
    {
        g_10 = a_1;
    }
    else
    {
        g_10 = b_0;
    }
    var q_2 : vec2<f32> = abs(g_10);
    return max(dot(q_2, vec2<f32>(0.5f, 0.86602538824081421f)), q_2.x);
}

@compute
@workgroup_size(8, 8, 1)
fn main(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S20 : vec2<u32> = id_0.xy;
    if((any((_S20 >= vec2<u32>(p_0.size_0.xy)))))
    {
        return;
    }
    var _S21 : vec2<i32> = vec2<i32>(_S20);
    var scene_0 : vec3<f32> = SourceAt_0(_S21);
    var seconds_0 : f32 = p_0.look_0.x;
    var glitch_0 : f32 = saturate(p_0.look_0.z);
    var seed_1 : f32 = p_0.look_0.w;
    var presence_0 : f32 = saturate(p_0.cloak_0.x);
    var recloak_0 : f32 = saturate(p_0.cloak_0.y);
    var strength_0 : f32 = p_0.tint_0.w / max(p_0.look_0.y, 0.00100000004749745f);
    var tint_1 : vec3<f32> = p_0.tint_0.xyz;
    var hot_1 : vec3<f32> = p_0.hot_0.xyz;
    var at_4 : vec2<i32> = _S21;
    var torn_0 : vec2<i32>;
    var split_0 : i32;
    var solid_0 : bool;
    if(glitch_0 > 0.0f)
    {
        var blockSize_0 : i32 = i32(6) + i32(Hash_0(vec2<f32>(seed_1, 1.70000004768371582f)) * 10.0f);
        var _S22 : vec2<i32> = vec2<i32>(blockSize_0);
        var block_0 : vec2<i32> = _S21 / _S22;
        var _S23 : f32 = f32(block_0.y);
        if((Hash_0(vec2<f32>(_S23, seed_1))) < (glitch_0 * 0.5f))
        {
            at_4[i32(0)] = at_4[i32(0)] + i32((Hash_0(vec2<f32>(_S23, seed_1 + 7.30000019073486328f)) - 0.5f) * glitch_0 * 4.0f * f32(blockSize_0));
        }
        var _S24 : vec2<i32> = at_4;
        var solid_1 : bool = (Hash_0(vec2<f32>(block_0) + vec2<f32>(seed_1 * 0.37000000476837158f, seed_1))) < (glitch_0 * 0.89999997615814209f);
        if(solid_1)
        {
            var _S25 : vec2<i32> = at_4 / _S22;
            at_4 = _S25 * _S22 + vec2<i32>((blockSize_0 / i32(2)));
        }
        var _S26 : i32 = i32(glitch_0 * (2.0f + Hash_0(vec2<f32>(seed_1, 3.09999990463256836f)) * 6.0f));
        torn_0 = _S24;
        split_0 = _S26;
        solid_0 = solid_1;
    }
    else
    {
        torn_0 = _S21;
        split_0 = i32(0);
        solid_0 = false;
    }
    var g_11 : GhostSample_0 = GhostAt_0(at_4);
    var _S27 : bool;
    if((g_11.covered_0) > 0.0f)
    {
        _S27 = (Bayer4_0(_S21)) >= (smoothstep(0.69999998807907104f, 2.0f, g_11.metres_0));
    }
    else
    {
        _S27 = false;
    }
    if(_S27)
    {
        g_11.covered_0 = 0.0f;
    }
    var light_0 : vec3<f32>;
    if((g_11.covered_0) <= 0.0f)
    {
        var halo_0 : vec2<f32> = HaloAt_0(torn_0);
        var light_1 : vec3<f32> = tint_1 * vec3<f32>((halo_0.x * 0.01200000010430813f * smoothstep(0.69999998807907104f, 2.0f, halo_0.y)));
        if(split_0 != i32(0))
        {
            var _S28 : vec2<i32> = vec2<i32>(split_0, i32(0));
            light_0 = light_1 + vec3<f32>(tint_1.x * EdgeAt_0(torn_0 + _S28), 0.0f, tint_1.z * EdgeAt_0(torn_0 - _S28)) * vec3<f32>(0.80000001192092896f);
        }
        else
        {
            light_0 = light_1;
        }
        textureStore((uTarget_0), (vec2<u32>(_S21)), (vec4<f32>(scene_0 + light_0 * vec3<f32>(strength_0), 1.0f)));
        return;
    }
    var edge_0 : f32 = EdgeAt_0(torn_0);
    var band_0 : f32 = exp(- Square_0((g_11.height_0 - (1.14999997615814209f - fract(seconds_0 / 3.5f) * 1.29999995231628418f)) * 12.0f));
    var hidden_0 : bool = (g_11.metres_0 - SceneMetresAt_0(_S21)) > (0.25f + g_11.metres_0 * 0.00999999977648258f);
    var signal_0 : f32;
    if((Hash_0(vec2<f32>(f32(_S21.y), seed_1 * 1.30999994277954102f))) < (glitch_0 * 0.02999999932944775f))
    {
        signal_0 = glitch_0;
    }
    else
    {
        signal_0 = 0.0f;
    }
    var burn_0 : f32;
    if(presence_0 < 0.99900001287460327f)
    {
        burn_0 = saturate(1.0f - (presence_0 - g_11.noise_0) / 0.05000000074505806f);
    }
    else
    {
        burn_0 = 0.0f;
    }
    var fill_0 : f32;
    if(hidden_0)
    {
        if((Bayer4_0(_S21)) < 0.14000000059604645f)
        {
            fill_0 = 1.0f;
        }
        else
        {
            fill_0 = 0.0f;
        }
        light_0 = scene_0 + tint_1 * vec3<f32>((edge_0 * (0.44999998807907104f + 0.30000001192092896f * band_0) + fill_0 * 0.10000000149011612f + burn_0 * 0.60000002384185791f)) * vec3<f32>(strength_0);
    }
    else
    {
        if(solid_0)
        {
            var _S29 : vec2<i32> = vec2<i32>(split_0, i32(0));
            var _S30 : vec3<f32> = vec3<f32>(strength_0);
            light_0 = mix(scene_0, SolidBody_0(g_11, _S21) * _S30, vec3<f32>(0.89999997615814209f)) + (vec3<f32>(tint_1.x * EdgeAt_0(torn_0 + _S29), tint_1.y * edge_0, tint_1.z * EdgeAt_0(torn_0 - _S29)) * vec3<f32>(0.89999997615814209f) + hot_1 * vec3<f32>(signal_0) * vec3<f32>(0.5f)) * _S30;
        }
        else
        {
            var _S31 : f32 = 1.0f - recloak_0;
            var _S32 : vec2<f32> = vec2<f32>(_S21);
            var centre_0 : vec2<f32> = _S32 + vec2<f32>(0.5f);
            var offset_0 : vec2<f32> = vec2<f32>(g_11.normal_0.x, - g_11.normal_0.y) * vec2<f32>(((2.0f + 7.0f * g_11.rim_0) * (0.80000001192092896f + 0.40000000596046448f * (sin(seconds_0 * 2.29999995231628418f + g_11.height_0 * 20.0f + g_11.noise_0 * 6.28318548202514648f) * 0.5f + 0.5f)) * (p_0.size_0.y / 1080.0f) * (1.0f + _S31 * 1.5f)));
            var bent_0 : vec3<f32> = vec3<f32>(SourceBetween_0(centre_0 + offset_0 * vec2<f32>(1.20000004768371582f)).x, SourceBetween_0(centre_0 + offset_0).y, SourceBetween_0(centre_0 + offset_0 * vec2<f32>(0.80000001192092896f)).z);
            const _S33 : vec3<f32> = vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f);
            var bent_1 : vec3<f32> = mix(bent_0, tint_1 * vec3<f32>((dot(bent_0, _S33) / max(dot(tint_1, _S33), 0.00100000004749745f))), vec3<f32>(0.14000000059604645f));
            var contour_0 : f32 = edge_0 * (0.2199999988079071f + 0.94999998807907104f * band_0);
            var light_2 : f32 = contour_0 + Square_0(g_11.rim_0) * (0.05999999865889549f + 0.25f * band_0) + smoothstep(0.40000000596046448f, 0.47999998927116394f, HexDistance_0(_S32 / vec2<f32>(max(5.0f, 0.09000000357627869f * p_0.unproject_0.y * p_0.size_0.y * 0.5f / max(g_11.metres_0, 0.10000000149011612f))))) * band_0 * 0.34999999403953552f + 0.01999999955296516f;
            const rest_0 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
            if(recloak_0 < 1.0f)
            {
                var outward_0 : f32 = abs(g_11.height_0 - 0.5f) * 2.0f;
                var light_3 : f32 = light_2 + exp(- Square_0((outward_0 - recloak_0) * 10.0f)) * _S31 * 0.80000001192092896f;
                if(outward_0 > recloak_0)
                {
                    light_0 = SolidBody_0(g_11, _S21) * vec3<f32>(0.34999999403953552f) * vec3<f32>(_S31);
                }
                else
                {
                    light_0 = rest_0;
                }
                fill_0 = light_3;
            }
            else
            {
                light_0 = rest_0;
                fill_0 = light_2;
            }
            light_0 = bent_1 + (mix(tint_1, hot_1, vec3<f32>(saturate((contour_0 - 0.69999998807907104f) * 2.5f))) * vec3<f32>(fill_0) + light_0 + hot_1 * vec3<f32>(signal_0) * vec3<f32>(0.40000000596046448f)) * vec3<f32>(strength_0);
        }
    }
    if(burn_0 > 0.0f)
    {
        solid_0 = !hidden_0;
    }
    else
    {
        solid_0 = false;
    }
    if(solid_0)
    {
        var _S34 : vec3<f32> = vec3<f32>(strength_0);
        light_0 = mix(light_0, SolidBody_0(g_11, _S21) * _S34, vec3<f32>((burn_0 * 0.60000002384185791f))) + hot_1 * vec3<f32>(burn_0) * vec3<f32>(1.20000004768371582f) * _S34;
    }
    textureStore((uTarget_0), (vec2<u32>(_S21)), (vec4<f32>(light_0, 1.0f)));
    return;
}

