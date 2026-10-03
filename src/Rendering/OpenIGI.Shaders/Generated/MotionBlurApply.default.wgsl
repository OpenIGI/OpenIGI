struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

@binding(9) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(6) @group(0) var uDilated_0 : texture_2d<f32>;

@binding(7) @group(0) var uDilatedSampler_0 : sampler;

@binding(0) @group(0) var uColour_0 : texture_2d<f32>;

@binding(1) @group(0) var uColourSampler_0 : sampler;

@binding(8) @group(0) var uTarget_0 : texture_storage_2d<rgba16float, write>;

@binding(2) @group(0) var uHalf_0 : texture_2d<f32>;

@binding(3) @group(0) var uHalfSampler_0 : sampler;

@binding(4) @group(0) var uFlat_0 : texture_2d<f32>;

@binding(5) @group(0) var uFlatSampler_0 : sampler;

fn GradientNoise_0( q_0 : vec2<f32>) -> f32
{
    return fract(52.98291778564453125f * fract(0.06711056083440781f * q_0.x + 0.00583714991807938f * q_0.y));
}

fn FrameNoise_0( pos_0 : vec2<f32>,  frame_0 : f32) -> f32
{
    return GradientNoise_0(pos_0 + vec2<f32>(frame_0) * vec2<f32>(12.07804298400878906f, 9.11744499206542969f));
}

fn InterleavedGradientNoise_0( pos_1 : vec2<f32>,  frame_1 : f32) -> f32
{
    return FrameNoise_0(pos_1, frame_1);
}

var<workgroup> sGroupSamples_0 : atomic<u32>;

fn BlurTap_0( uv_0 : vec2<f32>,  halfMix_0 : f32) -> vec4<f32>
{
    return mix((textureSampleLevel((uColour_0), (uColourSampler_0), (uv_0), (0.0f))), (textureSampleLevel((uHalf_0), (uHalfSampler_0), (uv_0), (0.0f))), vec4<f32>(halfMix_0));
}

fn Reach_0( spread_0 : f32,  steps_0 : f32,  stepsPerPixel_0 : f32) -> f32
{
    return saturate(stepsPerPixel_0 * spread_0 - max(steps_0 - 1.0f, 0.0f));
}

fn SampleWeight_0( centreFlat_0 : vec2<f32>,  sampleFlat_0 : vec2<f32>,  steps_1 : f32,  stepsPerPixel_1 : f32) -> f32
{
    var _S1 : f32 = sampleFlat_0.y - centreFlat_0.y;
    return saturate(0.5f + _S1) * Reach_0(centreFlat_0.x, steps_1, stepsPerPixel_1) + saturate(0.5f - _S1) * Reach_0(sampleFlat_0.x, steps_1, stepsPerPixel_1);
}

@compute
@workgroup_size(16, 16, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>, @builtin(local_invocation_index) localIndex_0 : u32)
{
    var _S2 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S3 : vec2<f32> = vec2<f32>(_S2);
    var noiseA_0 : f32 = InterleavedGradientNoise_0(_S3, 0.0f);
    var grid_0 : vec2<f32> = p_0.control_0.xy;
    var _S4 : vec2<f32> = vec2<f32>(0.5f);
    var record_0 : vec4<f32> = (textureSampleLevel((uDilated_0), (uDilatedSampler_0), (clamp((_S3 / vec2<f32>(16.0f) + (vec2<f32>(noiseA_0, InterleavedGradientNoise_0(_S3, 1.0f)) - _S4) * _S4) / grid_0, vec2<f32>(0.0f, 0.0f), vec2<f32>(1.0f, 1.0f) - _S4 / grid_0)), (0.0f)));
    var fastest_0 : vec2<f32> = record_0.zw;
    var slowest_0 : vec2<f32> = record_0.xy;
    var speed_0 : f32 = length(fastest_0);
    var wanted_0 : u32 = u32(clamp(4.0f * ceil(speed_0 / 4.0f), 4.0f, p_0.control_0.z));
    if(localIndex_0 == u32(0))
    {
        atomicStore(&(sGroupSamples_0), u32(0));
    }
    workgroupBarrier();
    var _S5 : u32 = atomicMax(&(sGroupSamples_0), wanted_0);
    workgroupBarrier();
    var samples_0 : u32 = atomicLoad(&(sGroupSamples_0));
    var halfSamples_0 : u32 = samples_0 / u32(2);
    var _S6 : bool;
    if((_S2.x) >= i32(p_0.size_0.x))
    {
        _S6 = true;
    }
    else
    {
        _S6 = (_S2.y) >= i32(p_0.size_0.y);
    }
    if(_S6)
    {
        return;
    }
    var uv_1 : vec2<f32> = (_S3 + _S4) * p_0.size_0.zw;
    var centre_0 : vec4<f32> = (textureSampleLevel((uColour_0), (uColourSampler_0), (uv_1), (0.0f)));
    if(speed_0 < 0.5f)
    {
        textureStore((uTarget_0), (vec2<u32>(_S2)), (centre_0));
        return;
    }
    var _S7 : vec2<f32> = fastest_0 * p_0.size_0.zw;
    var _S8 : f32 = f32(samples_0);
    var _S9 : f32 = clamp(speed_0 / _S8 - 1.0f, 0.0f, 1.0f);
    var sides_0 : f32 = f32(halfSamples_0);
    var s_0 : u32;
    var sum_0 : vec4<f32>;
    if((dot(slowest_0, slowest_0)) > (0.40000000596046448f * speed_0 * speed_0))
    {
        const _S10 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
        s_0 = u32(0);
        sum_0 = _S10;
        for(;;)
        {
            if(s_0 < halfSamples_0)
            {
            }
            else
            {
                break;
            }
            var _S11 : f32 = f32(s_0);
            var _S12 : vec2<f32> = vec2<f32>(0.0f);
            var _S13 : vec2<f32> = vec2<f32>(1.0f);
            var sum_1 : vec4<f32> = sum_0 + BlurTap_0(clamp(uv_1 + vec2<f32>(((_S11 + noiseA_0) / sides_0)) * _S7, _S12, _S13), _S9) + BlurTap_0(clamp(uv_1 - vec2<f32>(((_S11 + 1.0f - noiseA_0) / sides_0)) * _S7, _S12, _S13), _S9);
            s_0 = s_0 + u32(1);
            sum_0 = sum_1;
        }
        textureStore((uTarget_0), (vec2<u32>(_S2)), (sum_0 * vec4<f32>((1.0f / _S8))));
        return;
    }
    var _S14 : vec2<f32> = (textureSampleLevel((uFlat_0), (uFlatSampler_0), (uv_1), (0.0f))).xz;
    var _S15 : f32 = sides_0 / speed_0;
    const _S16 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
    s_0 = u32(0);
    sum_0 = _S16;
    var weightSum_0 : f32 = 0.0f;
    for(;;)
    {
        if(s_0 < halfSamples_0)
        {
        }
        else
        {
            break;
        }
        var _S17 : f32 = f32(s_0);
        var _S18 : vec2<f32> = vec2<f32>(0.0f);
        var _S19 : vec2<f32> = vec2<f32>(1.0f);
        var uvAhead_0 : vec2<f32> = clamp(uv_1 + vec2<f32>(((_S17 + noiseA_0) / sides_0)) * _S7, _S18, _S19);
        var uvBehind_0 : vec2<f32> = clamp(uv_1 - vec2<f32>(((_S17 + 1.0f - noiseA_0) / sides_0)) * _S7, _S18, _S19);
        var flatAhead_0 : vec4<f32> = (textureSampleLevel((uFlat_0), (uFlatSampler_0), (uvAhead_0), (0.0f)));
        var flatBehind_0 : vec4<f32> = (textureSampleLevel((uFlat_0), (uFlatSampler_0), (uvBehind_0), (0.0f)));
        var colourAhead_0 : vec4<f32> = BlurTap_0(uvAhead_0, _S9);
        var colourBehind_0 : vec4<f32> = BlurTap_0(uvBehind_0, _S9);
        var steps_2 : f32 = _S17 + 0.5f;
        var weightAhead_0 : f32 = SampleWeight_0(_S14, flatAhead_0.xz, steps_2, _S15);
        var weightBehind_0 : f32 = SampleWeight_0(_S14, flatBehind_0.xz, steps_2, _S15);
        var furtherAhead_0 : bool = (flatAhead_0.z) > (flatBehind_0.z);
        var slowerAhead_0 : bool = (flatAhead_0.x) < (flatBehind_0.x);
        if(furtherAhead_0)
        {
            _S6 = slowerAhead_0;
        }
        else
        {
            _S6 = false;
        }
        var usedAhead_0 : f32;
        if(_S6)
        {
            usedAhead_0 = weightBehind_0;
        }
        else
        {
            usedAhead_0 = weightAhead_0;
        }
        var _S20 : bool;
        if(furtherAhead_0)
        {
            _S20 = true;
        }
        else
        {
            _S20 = slowerAhead_0;
        }
        var usedBehind_0 : f32;
        if(_S20)
        {
            usedBehind_0 = weightBehind_0;
        }
        else
        {
            usedBehind_0 = weightAhead_0;
        }
        var colour_0 : vec4<f32> = sum_0 + (colourAhead_0 * vec4<f32>(usedAhead_0) + colourBehind_0 * vec4<f32>(usedBehind_0));
        var weightSum_1 : f32 = weightSum_0 + (usedAhead_0 + usedBehind_0);
        s_0 = s_0 + u32(1);
        sum_0 = colour_0;
        weightSum_0 = weightSum_1;
    }
    var normalise_0 : f32 = 0.5f / sides_0;
    textureStore((uTarget_0), (vec2<u32>(_S2)), (sum_0 * vec4<f32>(normalise_0) + vec4<f32>(max(1.0f - weightSum_0 * normalise_0, 0.0f)) * centre_0));
    return;
}

