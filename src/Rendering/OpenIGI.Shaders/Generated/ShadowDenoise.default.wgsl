struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) sun_0 : vec4<f32>,
};

@binding(7) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uRaw_0 : texture_2d<f32>;

@binding(2) @group(0) var uDepth_0 : texture_2d<f32>;

@binding(6) @group(0) var uShadow_0 : texture_storage_2d<rgba32float, write>;

@binding(4) @group(0) var uHistory_0 : texture_2d<f32>;

@binding(1) @group(0) var uRawSampler_0 : sampler;

@binding(3) @group(0) var uDepthSampler_0 : sampler;

var<private> kSampleSet_0 : array<vec2<f32>, i32(8)> = array<vec2<f32>, i32(8)>( vec2<f32>(0.93999999761581421f, 0.34200000762939453f), vec2<f32>(-0.9649999737739563f, -0.26199999451637268f), vec2<f32>(0.13500000536441803f, -0.42899999022483826f), vec2<f32>(-0.13699999451637268f, 0.42899999022483826f), vec2<f32>(0.34900000691413879f, -0.93699997663497925f), vec2<f32>(-0.27399998903274536f, 0.96200001239776611f), vec2<f32>(-0.40700000524520874f, -0.19200000166893005f), vec2<f32>(0.43500000238418579f, 0.11599999666213989f) );
fn ViewDepthAt_0( depth_0 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S1 : f32 = unproject_1.z;
    var _S2 : f32 = unproject_1.w;
    return 2.0f * _S1 * _S2 / (_S2 + _S1 - (1.0f - 2.0f * depth_0) * (_S2 - _S1));
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S3 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S4 : vec2<i32> = vec2<i32>(p_0.size_0.xy);
    var _S5 : i32 = _S3.x;
    var _S6 : bool;
    if(_S5 >= (_S4.x))
    {
        _S6 = true;
    }
    else
    {
        _S6 = (_S3.y) >= (_S4.y);
    }
    if(_S6)
    {
        return;
    }
    var _S7 : vec3<i32> = vec3<i32>(_S3, i32(0));
    var centre_0 : vec2<f32> = (textureLoad((uRaw_0), ((_S7)).xy, ((_S7)).z)).xy;
    var centreDepth_0 : f32 = (textureLoad((uDepth_0), ((_S7)).xy, ((_S7)).z)).x;
    if(centreDepth_0 <= 0.0f)
    {
        _S6 = true;
    }
    else
    {
        _S6 = (centre_0.y) <= 0.0f;
    }
    if(_S6)
    {
        var _S8 : f32 = centre_0.x;
        textureStore((uShadow_0), (vec2<u32>(_S3)), (vec4<f32>(_S8, _S8, _S8, _S8)));
        return;
    }
    var _S9 : f32 = max(ViewDepthAt_0(centreDepth_0, p_0.unproject_0), 0.00009999999747379f);
    var pixelRadius_0 : f32 = p_0.sun_0.x * centre_0.y * p_0.unproject_0.x * (0.5f * p_0.size_0.x) / _S9;
    var pixelRadius_1 : f32 = mix(pixelRadius_0, max(2.0f, 0.34999999403953552f * pixelRadius_0), smoothstep(0.25f, 0.89999997615814209f, clamp((textureLoad((uHistory_0), ((_S7)).xy, ((_S7)).z)).z * p_0.sun_0.y, 0.0f, 1.0f)));
    if(pixelRadius_1 < 0.5f)
    {
        var _S10 : f32 = centre_0.x;
        textureStore((uShadow_0), (vec2<u32>(_S3)), (vec4<f32>(_S10, _S10, _S10, _S10)));
        return;
    }
    var _S11 : f32 = min(pixelRadius_1, 32.0f);
    var _S12 : vec2<f32> = vec2<f32>(1.0f);
    var _S13 : vec2<f32> = (_S12 - vec2<f32>(f32((_S5 & (i32(1)))), f32(((_S3.y) & (i32(1))))) * vec2<f32>(2.0f)) * vec2<f32>(0.31999999284744263f);
    var _S14 : f32 = centre_0.x;
    var _S15 : vec2<f32> = vec2<f32>(0.5f);
    var _S16 : vec2<f32> = (vec2<f32>(_S3) + _S15) * p_0.size_0.zw;
    var i_0 : i32 = i32(0);
    var visibility_0 : f32 = _S14;
    var weightSum_0 : f32 = 1.0f;
    for(;;)
    {
        if(i_0 < i32(8))
        {
        }
        else
        {
            break;
        }
        var tapUv_0 : vec2<f32> = clamp(_S16 + (kSampleSet_0[i_0] + _S13) * vec2<f32>(_S11) * p_0.size_0.zw, _S15 * p_0.size_0.zw, _S12 - _S15 * p_0.size_0.zw);
        var tapVisibility_0 : f32 = (textureSampleLevel((uRaw_0), (uRawSampler_0), (tapUv_0), (0.0f))).x;
        var tapDepth_0 : f32 = (textureSampleLevel((uDepth_0), (uDepthSampler_0), (tapUv_0), (0.0f))).x;
        var tapViewZ_0 : f32 = ViewDepthAt_0(tapDepth_0, p_0.unproject_0);
        if(tapDepth_0 > 0.0f)
        {
            _S6 = (abs(tapViewZ_0 - _S9)) < (_S9 * 0.01999999955296516f);
        }
        else
        {
            _S6 = false;
        }
        var weight_0 : f32;
        if(_S6)
        {
            weight_0 = 1.0f;
        }
        else
        {
            weight_0 = 0.0f;
        }
        var visibility_1 : f32 = visibility_0 + tapVisibility_0 * weight_0;
        var weightSum_1 : f32 = weightSum_0 + weight_0;
        i_0 = i_0 + i32(1);
        visibility_0 = visibility_1;
        weightSum_0 = weightSum_1;
    }
    var resolved_0 : f32 = visibility_0 / weightSum_0;
    textureStore((uShadow_0), (vec2<u32>(_S3)), (vec4<f32>(resolved_0, resolved_0, resolved_0, resolved_0)));
    return;
}

