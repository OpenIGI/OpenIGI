struct GhostParams_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) depth_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) look_0 : vec4<f32>,
    @align(16) tint_0 : vec4<f32>,
};

@binding(4) @group(0) var<uniform> p_0 : GhostParams_std140_0;
@binding(0) @group(0) var uSource_0 : texture_2d<f32>;

@binding(1) @group(0) var uDepth_0 : texture_depth_2d;

@binding(2) @group(0) var uGhost_0 : texture_2d<f32>;

@binding(3) @group(0) var uTarget_0 : texture_storage_2d<rgba16float, write>;

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

fn ViewDepthAt_0( depth_1 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S2 : f32 = unproject_1.z;
    var _S3 : f32 = unproject_1.w;
    return 2.0f * _S2 * _S3 / (_S3 + _S2 - (1.0f - 2.0f * depth_1) * (_S3 - _S2));
}

fn SceneMetresAt_0( pixel_1 : vec2<i32>) -> f32
{
    var _S4 : vec3<i32> = vec3<i32>(clamp(vec2<i32>((vec2<f32>(pixel_1) + vec2<f32>(0.5f)) * p_0.depth_0.zw), vec2<i32>(i32(0), i32(0)), vec2<i32>(p_0.depth_0.xy) - vec2<i32>(i32(1))), i32(0));
    var d_0 : f32 = (textureLoad((uDepth_0), ((_S4)).xy, ((_S4)).z));
    var _S5 : f32;
    if(d_0 > 0.0f)
    {
        _S5 = ViewDepthAt_0(d_0, p_0.unproject_0);
    }
    else
    {
        _S5 = p_0.unproject_0.w;
    }
    return _S5;
}

fn GhostAt_0( pixel_2 : vec2<i32>) -> vec4<f32>
{
    var _S6 : vec2<i32> = vec2<i32>(p_0.size_0.xy);
    var _S7 : bool;
    if((any((pixel_2 < vec2<i32>(i32(0), i32(0))))))
    {
        _S7 = true;
    }
    else
    {
        _S7 = (any((pixel_2 >= _S6)));
    }
    if(_S7)
    {
        return vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
    }
    var _S8 : vec3<i32> = vec3<i32>(pixel_2, i32(0));
    return (textureLoad((uGhost_0), ((_S8)).xy, ((_S8)).z));
}

fn Figure_0( at_0 : vec2<i32>,  sceneMetres_0 : f32) -> vec2<f32>
{
    var m_0 : vec4<f32> = GhostAt_0(at_0);
    var _S9 : vec2<f32> = vec2<f32>(cos(0.0f), sin(0.0f));
    var _S10 : vec2<f32> = vec2<f32>(1.5f);
    var _S11 : vec2<f32> = vec2<f32>(4.0f);
    var _S12 : vec2<f32> = vec2<f32>(cos(0.78539818525314331f), sin(0.78539818525314331f));
    var _S13 : vec2<f32> = vec2<f32>(cos(1.57079637050628662f), sin(1.57079637050628662f));
    var _S14 : vec2<f32> = vec2<f32>(cos(2.35619449615478516f), sin(2.35619449615478516f));
    var _S15 : vec2<f32> = vec2<f32>(cos(3.14159274101257324f), sin(3.14159274101257324f));
    var _S16 : vec2<f32> = vec2<f32>(cos(3.92699098587036133f), sin(3.92699098587036133f));
    var _S17 : vec2<f32> = vec2<f32>(cos(4.71238899230957031f), sin(4.71238899230957031f));
    var _S18 : vec2<f32> = vec2<f32>(cos(5.4977874755859375f), sin(5.4977874755859375f));
    var around_0 : f32 = GhostAt_0(at_0 + vec2<i32>(round(_S9 * _S10))).w + GhostAt_0(at_0 + vec2<i32>(round(_S12 * _S10))).w + GhostAt_0(at_0 + vec2<i32>(round(_S13 * _S10))).w + GhostAt_0(at_0 + vec2<i32>(round(_S14 * _S10))).w + GhostAt_0(at_0 + vec2<i32>(round(_S15 * _S10))).w + GhostAt_0(at_0 + vec2<i32>(round(_S16 * _S10))).w + GhostAt_0(at_0 + vec2<i32>(round(_S17 * _S10))).w + GhostAt_0(at_0 + vec2<i32>(round(_S18 * _S10))).w;
    var ring_0 : f32 = GhostAt_0(at_0 + vec2<i32>(round(_S9 * _S11))).w + GhostAt_0(at_0 + vec2<i32>(round(_S12 * _S11))).w + GhostAt_0(at_0 + vec2<i32>(round(_S13 * _S11))).w + GhostAt_0(at_0 + vec2<i32>(round(_S14 * _S11))).w + GhostAt_0(at_0 + vec2<i32>(round(_S15 * _S11))).w + GhostAt_0(at_0 + vec2<i32>(round(_S16 * _S11))).w + GhostAt_0(at_0 + vec2<i32>(round(_S17 * _S11))).w + GhostAt_0(at_0 + vec2<i32>(round(_S18 * _S11))).w;
    if((m_0.w) <= 0.0f)
    {
        return vec2<f32>(ring_0 * 0.03500000014901161f, 0.0f);
    }
    var edge_0 : f32 = saturate((8.0f - around_0) * 0.5f);
    var body_0 : f32 = 1.0f / max(m_0.z, 0.00009999999747379f) - 1.0f;
    var shown_0 : f32;
    if((body_0 - sceneMetres_0) <= (0.25f + body_0 * 0.00999999977648258f))
    {
        shown_0 = 1.0f;
    }
    else
    {
        shown_0 = 0.33000001311302185f;
    }
    return vec2<f32>((0.07999999821186066f + 0.80000001192092896f * m_0.x + 0.5f * m_0.y + 1.20000004768371582f * edge_0) * shown_0, shown_0);
}

@compute
@workgroup_size(8, 8, 1)
fn main(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S19 : vec2<u32> = id_0.xy;
    if((any((_S19 >= vec2<u32>(p_0.size_0.xy)))))
    {
        return;
    }
    var _S20 : vec2<i32> = vec2<i32>(_S19);
    var scene_0 : vec3<f32> = SourceAt_0(_S20);
    var seconds_0 : f32 = p_0.look_0.x;
    var glitch_0 : f32 = saturate(p_0.look_0.z);
    var seed_1 : f32 = p_0.look_0.w;
    var at_1 : vec2<i32> = _S20;
    var noiseLine_0 : f32;
    var split_0 : i32;
    var dropped_0 : bool;
    if(glitch_0 > 0.0f)
    {
        var _S21 : f32 = f32(_S20.y);
        var band_0 : f32 = floor(_S21 / (4.0f + floor(Hash_0(vec2<f32>(seed_1, 1.70000004768371582f)) * 28.0f)));
        if((Hash_0(vec2<f32>(band_0, seed_1))) < (glitch_0 * 0.69999998807907104f))
        {
            at_1[i32(0)] = at_1[i32(0)] + i32((Hash_0(vec2<f32>(band_0, seed_1 + 7.30000019073486328f)) - 0.5f) * glitch_0 * 0.14000000059604645f * p_0.size_0.x);
        }
        var _S22 : i32 = i32(glitch_0 * (3.0f + Hash_0(vec2<f32>(seed_1, 3.09999990463256836f)) * 12.0f));
        var _S23 : bool = (Hash_0(floor(vec2<f32>(_S20) / vec2<f32>(24.0f)) + vec2<f32>(seed_1, seed_1 * 0.37000000476837158f))) < (glitch_0 * 0.2199999988079071f);
        if((Hash_0(vec2<f32>(_S21, seed_1 * 1.30999994277954102f))) < (glitch_0 * 0.03999999910593033f))
        {
            noiseLine_0 = 1.0f;
        }
        else
        {
            noiseLine_0 = 0.0f;
        }
        split_0 = _S22;
        dropped_0 = _S23;
    }
    else
    {
        split_0 = i32(0);
        dropped_0 = false;
        noiseLine_0 = 0.0f;
    }
    var sceneMetres_1 : f32 = SceneMetresAt_0(_S20);
    var g_0 : vec2<f32> = Figure_0(at_1, sceneMetres_1);
    var _S24 : bool = split_0 != i32(0);
    var red_0 : f32;
    if(_S24)
    {
        red_0 = Figure_0(at_1 + vec2<i32>(split_0, i32(0)), sceneMetres_1).x;
    }
    else
    {
        red_0 = g_0.x;
    }
    var blue_0 : f32;
    if(_S24)
    {
        blue_0 = Figure_0(at_1 - vec2<i32>(split_0, i32(0)), sceneMetres_1).x;
    }
    else
    {
        blue_0 = g_0.x;
    }
    var _S25 : f32 = g_0.x;
    if((max(max(_S25, red_0), blue_0)) <= 0.0f)
    {
        textureStore((uTarget_0), (vec2<u32>(_S20)), (vec4<f32>(scene_0, 1.0f)));
        return;
    }
    var frame_0 : f32 = floor(seconds_0 * 30.0f);
    var grain_0 : f32 = Hash_0(vec2<f32>(_S20) + vec2<f32>(frame_0 * 7.13000011444091797f, frame_0 * 3.71000003814697266f));
    var raster_0 : f32;
    if((_S20.y % i32(3)) == i32(0))
    {
        raster_0 = 0.69999998807907104f;
    }
    else
    {
        raster_0 = 1.0f;
    }
    var _S26 : f32 = (0.85000002384185791f + 0.30000001192092896f * grain_0) * raster_0 * (0.89999997615814209f + 0.10000000149011612f * sin(seconds_0 * 23.0f) * sin(seconds_0 * 7.30000019073486328f));
    var shimmer_0 : f32;
    if(dropped_0)
    {
        shimmer_0 = 0.0f;
    }
    else
    {
        shimmer_0 = _S26;
    }
    var tint_1 : vec3<f32> = p_0.tint_0.xyz;
    var colour_0 : vec3<f32> = vec3<f32>(mix(tint_1.x, 0.85000002384185791f, saturate(red_0 - 0.80000001192092896f)) * red_0, mix(tint_1.y, 0.97000002861022949f, saturate(_S25 - 0.80000001192092896f)) * _S25, mix(tint_1.z, 1.0f, saturate(blue_0 - 0.80000001192092896f)) * blue_0) + vec3<f32>(0.85000002384185791f, 0.97000002861022949f, 1.0f) * vec3<f32>(noiseLine_0) * vec3<f32>((0.40000000596046448f + 0.60000002384185791f * grain_0)) * vec3<f32>(glitch_0);
    var strength_0 : f32 = p_0.tint_0.w / max(p_0.look_0.y, 0.00100000004749745f);
    var _S27 : f32 = 0.2199999988079071f * g_0.y;
    if(dropped_0)
    {
        noiseLine_0 = 0.0f;
    }
    else
    {
        noiseLine_0 = 1.0f;
    }
    textureStore((uTarget_0), (vec2<u32>(_S20)), (vec4<f32>(scene_0 * vec3<f32>((1.0f - _S27 * noiseLine_0)) + colour_0 * vec3<f32>(shimmer_0) * vec3<f32>(strength_0), 1.0f)));
    return;
}

