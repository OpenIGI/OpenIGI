struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct Params_std140_0
{
    @align(16) viewToWorld_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) prevWorldToView_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) resolution_0 : vec4<f32>,
    @align(16) cameraDelta_0 : vec4<f32>,
    @align(16) prevProjection_0 : vec4<f32>,
    @align(16) jitter_0 : vec4<f32>,
};

@binding(8) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uDepth_0 : texture_depth_2d;

@binding(2) @group(0) var uDilatedVelocity_0 : texture_2d<f32>;

@binding(3) @group(0) var uDilatedVelocitySampler_0 : sampler;

@binding(6) @group(0) var uRejection_0 : texture_storage_2d<rgba32float, write>;

@binding(4) @group(0) var uPrevDepth_0 : texture_2d<f32>;

@binding(7) @group(0) var<storage, read> uOccluder_0 : array<u32>;

fn DepthBilinearLoaded_0( depth_0 : texture_depth_2d,  uv_0 : vec2<f32>) -> f32
{
    var width_0 : u32;
    var height_0 : u32;
    {var dim = textureDimensions((depth_0));((width_0)) = dim.x;((height_0)) = dim.y;};
    var last_0 : vec2<i32> = vec2<i32>(i32(width_0) - i32(1), i32(height_0) - i32(1));
    var position_0 : vec2<f32> = uv_0 * vec2<f32>(f32(width_0), f32(height_0)) - vec2<f32>(0.5f);
    var floored_0 : vec2<f32> = floor(position_0);
    var weight_0 : vec2<f32> = position_0 - floored_0;
    var _S1 : vec2<i32> = vec2<i32>(floored_0);
    const _S2 : vec2<i32> = vec2<i32>(i32(0), i32(0));
    var _S3 : vec3<i32> = vec3<i32>(clamp(_S1, _S2, last_0), i32(0));
    var _S4 : vec3<i32> = vec3<i32>(clamp(_S1 + vec2<i32>(i32(1), i32(0)), _S2, last_0), i32(0));
    var _S5 : vec3<i32> = vec3<i32>(clamp(_S1 + vec2<i32>(i32(0), i32(1)), _S2, last_0), i32(0));
    var _S6 : vec3<i32> = vec3<i32>(clamp(_S1 + vec2<i32>(i32(1), i32(1)), _S2, last_0), i32(0));
    var _S7 : f32 = weight_0.x;
    return mix(mix((textureLoad((depth_0), ((_S3)).xy, ((_S3)).z)), (textureLoad((depth_0), ((_S4)).xy, ((_S4)).z)), _S7), mix((textureLoad((depth_0), ((_S5)).xy, ((_S5)).z)), (textureLoad((depth_0), ((_S6)).xy, ((_S6)).z)), _S7), weight_0.y);
}

fn ViewDistanceWith_0( d_0 : f32,  near_0 : f32,  far_0 : f32) -> f32
{
    return 2.0f * near_0 * far_0 / (far_0 + near_0 - (1.0f - 2.0f * d_0) * (far_0 - near_0));
}

fn ViewDistance_0( d_1 : f32) -> f32
{
    return ViewDistanceWith_0(d_1, p_0.unproject_0.z, p_0.unproject_0.w);
}

fn DistanceOrFar_0( d_2 : f32) -> f32
{
    var _S8 : f32;
    if(d_2 <= 0.0f)
    {
        _S8 = p_0.unproject_0.w;
    }
    else
    {
        _S8 = ViewDistance_0(d_2);
    }
    return _S8;
}

fn PixelHeight_0( z_0 : f32) -> f32
{
    return z_0 / max(0.5f * p_0.resolution_0.y * p_0.unproject_0.y, 9.99999997475242708e-07f);
}

fn OccluderMask_0( prevRasterUv_0 : vec2<f32>,  zRef_0 : f32,  eOcc_0 : f32,  size_0 : vec2<i32>) -> f32
{
    var _S9 : vec2<i32> = size_0 - vec2<i32>(i32(1));
    var q_0 : vec2<f32> = prevRasterUv_0 * p_0.resolution_0.xy - vec2<f32>(0.5f);
    var b_0 : vec2<f32> = floor(q_0);
    var _S10 : vec2<f32> = q_0 - b_0;
    var _S11 : vec2<i32> = vec2<i32>(b_0);
    var _S12 : f32 = _S10.x;
    var _S13 : f32 = 1.0f - _S12;
    var _S14 : f32 = _S10.y;
    var _S15 : f32 = 1.0f - _S14;
    var weight_1 : f32 = _S13 * _S15;
    const _S16 : vec2<i32> = vec2<i32>(i32(0), i32(0));
    var at_0 : vec2<i32> = clamp(_S11, _S16, _S9);
    var _S17 : i32 = size_0.x;
    var word_0 : u32 = uOccluder_0[at_0.y * _S17 + at_0.x];
    var mask_0 : f32;
    if(word_0 == u32(0))
    {
        mask_0 = weight_1;
    }
    else
    {
        var zOcc_0 : f32 = (bitcast<f32>((~word_0)));
        mask_0 = weight_1 * saturate(2.0f - (zRef_0 - zOcc_0) / (max(6.0f * PixelHeight_0(zOcc_0), 0.00009999999747379f) + eOcc_0));
    }
    var weight_2 : f32 = _S12 * _S15;
    var at_1 : vec2<i32> = clamp(_S11 + vec2<i32>(i32(1), i32(0)), _S16, _S9);
    var word_1 : u32 = uOccluder_0[at_1.y * _S17 + at_1.x];
    if(word_1 == u32(0))
    {
        mask_0 = mask_0 + weight_2;
    }
    else
    {
        var zOcc_1 : f32 = (bitcast<f32>((~word_1)));
        mask_0 = mask_0 + weight_2 * saturate(2.0f - (zRef_0 - zOcc_1) / (max(6.0f * PixelHeight_0(zOcc_1), 0.00009999999747379f) + eOcc_0));
    }
    var weight_3 : f32 = _S13 * _S14;
    var at_2 : vec2<i32> = clamp(_S11 + vec2<i32>(i32(0), i32(1)), _S16, _S9);
    var word_2 : u32 = uOccluder_0[at_2.y * _S17 + at_2.x];
    if(word_2 == u32(0))
    {
        mask_0 = mask_0 + weight_3;
    }
    else
    {
        var zOcc_2 : f32 = (bitcast<f32>((~word_2)));
        mask_0 = mask_0 + weight_3 * saturate(2.0f - (zRef_0 - zOcc_2) / (max(6.0f * PixelHeight_0(zOcc_2), 0.00009999999747379f) + eOcc_0));
    }
    var weight_4 : f32 = _S12 * _S14;
    var at_3 : vec2<i32> = clamp(_S11 + vec2<i32>(i32(1), i32(1)), _S16, _S9);
    var word_3 : u32 = uOccluder_0[at_3.y * _S17 + at_3.x];
    if(word_3 == u32(0))
    {
        mask_0 = mask_0 + weight_4;
    }
    else
    {
        var zOcc_3 : f32 = (bitcast<f32>((~word_3)));
        mask_0 = mask_0 + weight_4 * saturate(2.0f - (zRef_0 - zOcc_3) / (max(6.0f * PixelHeight_0(zOcc_3), 0.00009999999747379f) + eOcc_0));
    }
    return mask_0;
}

fn ViewPosition_0( uv_1 : vec2<f32>,  d_3 : f32) -> vec3<f32>
{
    var z_1 : f32 = ViewDistance_0(d_3);
    var ndc_0 : vec2<f32> = vec2<f32>(2.0f) * uv_1 - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * z_1 / p_0.unproject_0.x, - ndc_0.y * z_1 / p_0.unproject_0.y, z_1);
}

fn PreviousViewZ_0( viewPos_0 : vec3<f32>) -> f32
{
    return (((vec4<f32>((((vec4<f32>(viewPos_0, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + p_0.cameraDelta_0.xyz, 0.0f)) * (mat4x4<f32>(p_0.prevWorldToView_0.data_0[i32(0)][i32(0)], p_0.prevWorldToView_0.data_0[i32(1)][i32(0)], p_0.prevWorldToView_0.data_0[i32(2)][i32(0)], p_0.prevWorldToView_0.data_0[i32(3)][i32(0)], p_0.prevWorldToView_0.data_0[i32(0)][i32(1)], p_0.prevWorldToView_0.data_0[i32(1)][i32(1)], p_0.prevWorldToView_0.data_0[i32(2)][i32(1)], p_0.prevWorldToView_0.data_0[i32(3)][i32(1)], p_0.prevWorldToView_0.data_0[i32(0)][i32(2)], p_0.prevWorldToView_0.data_0[i32(1)][i32(2)], p_0.prevWorldToView_0.data_0[i32(2)][i32(2)], p_0.prevWorldToView_0.data_0[i32(3)][i32(2)], p_0.prevWorldToView_0.data_0[i32(0)][i32(3)], p_0.prevWorldToView_0.data_0[i32(1)][i32(3)], p_0.prevWorldToView_0.data_0[i32(2)][i32(3)], p_0.prevWorldToView_0.data_0[i32(3)][i32(3)])))).z;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S18 : vec2<i32>;
    var _S19 : vec3<i32>;
    var eOcc_1 : f32;
    var _S20 : vec3<i32>;
    var _S21 : vec3<i32>;
    var _S22 : vec3<i32>;
    var agreement_0 : f32;
    var _S23 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S24 : vec2<i32> = vec2<i32>(p_0.resolution_0.xy);
    var _S25 : bool;
    if((_S23.x) >= (_S24.x))
    {
        _S25 = true;
    }
    else
    {
        _S25 = (_S23.y) >= (_S24.y);
    }
    if(_S25)
    {
        return;
    }
    var _S26 : vec2<i32> = _S24 - vec2<i32>(i32(1));
    var rasterUv_0 : vec2<f32> = (vec2<f32>(_S23) + vec2<f32>(0.5f)) * p_0.resolution_0.zw;
    var d_4 : f32 = DepthBilinearLoaded_0(uDepth_0, rasterUv_0);
    var currentUv_0 : vec2<f32> = rasterUv_0 - p_0.jitter_0.xy * p_0.resolution_0.zw;
    var prevRasterUv_1 : vec2<f32> = currentUv_0 - (textureSampleLevel((uDilatedVelocity_0), (uDilatedVelocitySampler_0), (rasterUv_0), (0.0f))).xy + p_0.jitter_0.zw * p_0.resolution_0.zw;
    if((any((prevRasterUv_1 < vec2<f32>(0.0f)))))
    {
        _S25 = true;
    }
    else
    {
        _S25 = (any((prevRasterUv_1 > vec2<f32>(1.0f))));
    }
    if(_S25)
    {
        textureStore((uRejection_0), (vec2<u32>(_S23)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var _S27 : vec2<i32> = vec2<i32>(prevRasterUv_1 * p_0.resolution_0.xy);
    const _S28 : vec2<i32> = vec2<i32>(i32(1), i32(0));
    const _S29 : vec2<i32> = vec2<i32>(i32(-1), i32(0));
    const _S30 : vec2<i32> = vec2<i32>(i32(0), i32(1));
    const _S31 : vec2<i32> = vec2<i32>(i32(0), i32(-1));
    var _S32 : f32 = DistanceOrFar_0(d_4);
    var _S33 : vec3<i32> = vec3<i32>(_S23, i32(0));
    var _S34 : vec2<f32> = (textureLoad((uDilatedVelocity_0), ((_S33)).xy, ((_S33)).z)).xy * p_0.resolution_0.xy;
    for(;;)
    {
        for(;;)
        {
            for(;;)
            {
                var _S35 : vec2<i32> = _S23 + _S28;
                const _S36 : vec2<i32> = vec2<i32>(i32(0), i32(0));
                _S18 = _S36;
                var _S37 : vec3<i32> = vec3<i32>(clamp(_S35, _S36, _S26), i32(0));
                _S19 = _S37;
                if((length((textureLoad((uDilatedVelocity_0), ((_S37)).xy, ((_S37)).z)).xy * p_0.resolution_0.xy - _S34)) > 0.5f)
                {
                    eOcc_1 = 0.0f;
                    break;
                }
                eOcc_1 = max(0.0f, abs(DistanceOrFar_0((textureLoad((uDepth_0), ((_S37)).xy, ((_S37)).z))) - _S32));
                break;
            }
            break;
        }
        for(;;)
        {
            for(;;)
            {
                var _S38 : vec3<i32> = vec3<i32>(clamp(_S23 + _S29, _S18, _S26), i32(0));
                _S20 = _S38;
                if((length((textureLoad((uDilatedVelocity_0), ((_S38)).xy, ((_S38)).z)).xy * p_0.resolution_0.xy - _S34)) > 0.5f)
                {
                    break;
                }
                eOcc_1 = max(eOcc_1, abs(DistanceOrFar_0((textureLoad((uDepth_0), ((_S38)).xy, ((_S38)).z))) - _S32));
                break;
            }
            break;
        }
        for(;;)
        {
            for(;;)
            {
                var _S39 : vec3<i32> = vec3<i32>(clamp(_S23 + _S30, _S18, _S26), i32(0));
                _S21 = _S39;
                if((length((textureLoad((uDilatedVelocity_0), ((_S39)).xy, ((_S39)).z)).xy * p_0.resolution_0.xy - _S34)) > 0.5f)
                {
                    break;
                }
                eOcc_1 = max(eOcc_1, abs(DistanceOrFar_0((textureLoad((uDepth_0), ((_S39)).xy, ((_S39)).z))) - _S32));
                break;
            }
            break;
        }
        for(;;)
        {
            for(;;)
            {
                var _S40 : vec3<i32> = vec3<i32>(clamp(_S23 + _S31, _S18, _S26), i32(0));
                _S22 = _S40;
                if((length((textureLoad((uDilatedVelocity_0), ((_S40)).xy, ((_S40)).z)).xy * p_0.resolution_0.xy - _S34)) > 0.5f)
                {
                    break;
                }
                eOcc_1 = max(eOcc_1, abs(DistanceOrFar_0((textureLoad((uDepth_0), ((_S40)).xy, ((_S40)).z))) - _S32));
                break;
            }
            break;
        }
        break;
    }
    var wasSky_0 : f32;
    if(d_4 <= 0.0f)
    {
        var _S41 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(-1), i32(-1)), _S18, _S26), i32(0));
        if(((textureLoad((uPrevDepth_0), ((_S41)).xy, ((_S41)).z)).x) <= 0.0f)
        {
            wasSky_0 = 1.0f;
        }
        else
        {
            wasSky_0 = 0.0f;
        }
        var _S42 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(0), i32(-1)), _S18, _S26), i32(0));
        if(((textureLoad((uPrevDepth_0), ((_S42)).xy, ((_S42)).z)).x) <= 0.0f)
        {
            wasSky_0 = 1.0f;
        }
        else
        {
        }
        var _S43 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(1), i32(-1)), _S18, _S26), i32(0));
        if(((textureLoad((uPrevDepth_0), ((_S43)).xy, ((_S43)).z)).x) <= 0.0f)
        {
            wasSky_0 = 1.0f;
        }
        else
        {
        }
        var _S44 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(-1), i32(0)), _S18, _S26), i32(0));
        if(((textureLoad((uPrevDepth_0), ((_S44)).xy, ((_S44)).z)).x) <= 0.0f)
        {
            wasSky_0 = 1.0f;
        }
        else
        {
        }
        var _S45 : vec3<i32> = vec3<i32>(clamp(_S27, _S18, _S26), i32(0));
        if(((textureLoad((uPrevDepth_0), ((_S45)).xy, ((_S45)).z)).x) <= 0.0f)
        {
            wasSky_0 = 1.0f;
        }
        else
        {
        }
        var _S46 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(1), i32(0)), _S18, _S26), i32(0));
        if(((textureLoad((uPrevDepth_0), ((_S46)).xy, ((_S46)).z)).x) <= 0.0f)
        {
            wasSky_0 = 1.0f;
        }
        else
        {
        }
        var _S47 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(-1), i32(1)), _S18, _S26), i32(0));
        if(((textureLoad((uPrevDepth_0), ((_S47)).xy, ((_S47)).z)).x) <= 0.0f)
        {
            wasSky_0 = 1.0f;
        }
        else
        {
        }
        var _S48 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(0), i32(1)), _S18, _S26), i32(0));
        if(((textureLoad((uPrevDepth_0), ((_S48)).xy, ((_S48)).z)).x) <= 0.0f)
        {
            wasSky_0 = 1.0f;
        }
        else
        {
        }
        var _S49 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(1), i32(1)), _S18, _S26), i32(0));
        if(((textureLoad((uPrevDepth_0), ((_S49)).xy, ((_S49)).z)).x) <= 0.0f)
        {
            wasSky_0 = 1.0f;
        }
        else
        {
        }
        textureStore((uRejection_0), (vec2<u32>(_S23)), (vec4<f32>(min(wasSky_0, OccluderMask_0(prevRasterUv_1, p_0.prevProjection_0.w, eOcc_1, _S24)), 0.0f, 0.0f, 0.0f)));
        return;
    }
    var viewPos_1 : vec3<f32> = ViewPosition_0(currentUv_0, d_4);
    var prevViewZ_0 : f32 = PreviousViewZ_0(viewPos_1);
    if(prevViewZ_0 <= (p_0.prevProjection_0.z))
    {
        textureStore((uRejection_0), (vec2<u32>(_S23)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    for(;;)
    {
        for(;;)
        {
            for(;;)
            {
                var _S50 : vec3<i32> = _S19;
                var nd_0 : f32 = (textureLoad((uDepth_0), ((_S50)).xy, ((_S50)).z));
                if(nd_0 <= 0.0f)
                {
                    wasSky_0 = 0.0f;
                    break;
                }
                wasSky_0 = max(0.0f, abs(ViewDistance_0(nd_0) - viewPos_1.z));
                break;
            }
            break;
        }
        for(;;)
        {
            for(;;)
            {
                var _S51 : vec3<i32> = _S20;
                var nd_1 : f32 = (textureLoad((uDepth_0), ((_S51)).xy, ((_S51)).z));
                if(nd_1 <= 0.0f)
                {
                    break;
                }
                wasSky_0 = max(wasSky_0, abs(ViewDistance_0(nd_1) - viewPos_1.z));
                break;
            }
            break;
        }
        for(;;)
        {
            for(;;)
            {
                var _S52 : vec3<i32> = _S21;
                var nd_2 : f32 = (textureLoad((uDepth_0), ((_S52)).xy, ((_S52)).z));
                if(nd_2 <= 0.0f)
                {
                    break;
                }
                wasSky_0 = max(wasSky_0, abs(ViewDistance_0(nd_2) - viewPos_1.z));
                break;
            }
            break;
        }
        for(;;)
        {
            for(;;)
            {
                var _S53 : vec3<i32> = _S22;
                var nd_3 : f32 = (textureLoad((uDepth_0), ((_S53)).xy, ((_S53)).z));
                if(nd_3 <= 0.0f)
                {
                    break;
                }
                wasSky_0 = max(wasSky_0, abs(ViewDistance_0(nd_3) - viewPos_1.z));
                break;
            }
            break;
        }
        break;
    }
    for(;;)
    {
        for(;;)
        {
            for(;;)
            {
                for(;;)
                {
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S54 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(-1), i32(-1)), _S18, _S26), i32(0));
                            var prevD_0 : f32 = (textureLoad((uPrevDepth_0), ((_S54)).xy, ((_S54)).z)).x;
                            if(prevD_0 <= 0.0f)
                            {
                                agreement_0 = 0.0f;
                                break;
                            }
                            var zHist_0 : f32 = ViewDistanceWith_0(prevD_0, p_0.prevProjection_0.z, p_0.prevProjection_0.w);
                            agreement_0 = max(0.0f, saturate(2.0f - abs(zHist_0 - prevViewZ_0) / (max(6.0f * PixelHeight_0(zHist_0), 0.00009999999747379f) + wasSky_0)));
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S55 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(0), i32(-1)), _S18, _S26), i32(0));
                            var prevD_1 : f32 = (textureLoad((uPrevDepth_0), ((_S55)).xy, ((_S55)).z)).x;
                            if(prevD_1 <= 0.0f)
                            {
                                break;
                            }
                            var zHist_1 : f32 = ViewDistanceWith_0(prevD_1, p_0.prevProjection_0.z, p_0.prevProjection_0.w);
                            agreement_0 = max(agreement_0, saturate(2.0f - abs(zHist_1 - prevViewZ_0) / (max(6.0f * PixelHeight_0(zHist_1), 0.00009999999747379f) + wasSky_0)));
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S56 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(1), i32(-1)), _S18, _S26), i32(0));
                            var prevD_2 : f32 = (textureLoad((uPrevDepth_0), ((_S56)).xy, ((_S56)).z)).x;
                            if(prevD_2 <= 0.0f)
                            {
                                break;
                            }
                            var zHist_2 : f32 = ViewDistanceWith_0(prevD_2, p_0.prevProjection_0.z, p_0.prevProjection_0.w);
                            agreement_0 = max(agreement_0, saturate(2.0f - abs(zHist_2 - prevViewZ_0) / (max(6.0f * PixelHeight_0(zHist_2), 0.00009999999747379f) + wasSky_0)));
                            break;
                        }
                        break;
                    }
                    break;
                }
                break;
            }
            break;
        }
        for(;;)
        {
            for(;;)
            {
                for(;;)
                {
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S57 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(-1), i32(0)), _S18, _S26), i32(0));
                            var prevD_3 : f32 = (textureLoad((uPrevDepth_0), ((_S57)).xy, ((_S57)).z)).x;
                            if(prevD_3 <= 0.0f)
                            {
                                break;
                            }
                            var zHist_3 : f32 = ViewDistanceWith_0(prevD_3, p_0.prevProjection_0.z, p_0.prevProjection_0.w);
                            agreement_0 = max(agreement_0, saturate(2.0f - abs(zHist_3 - prevViewZ_0) / (max(6.0f * PixelHeight_0(zHist_3), 0.00009999999747379f) + wasSky_0)));
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S58 : vec3<i32> = vec3<i32>(clamp(_S27, _S18, _S26), i32(0));
                            var prevD_4 : f32 = (textureLoad((uPrevDepth_0), ((_S58)).xy, ((_S58)).z)).x;
                            if(prevD_4 <= 0.0f)
                            {
                                break;
                            }
                            var zHist_4 : f32 = ViewDistanceWith_0(prevD_4, p_0.prevProjection_0.z, p_0.prevProjection_0.w);
                            agreement_0 = max(agreement_0, saturate(2.0f - abs(zHist_4 - prevViewZ_0) / (max(6.0f * PixelHeight_0(zHist_4), 0.00009999999747379f) + wasSky_0)));
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S59 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(1), i32(0)), _S18, _S26), i32(0));
                            var prevD_5 : f32 = (textureLoad((uPrevDepth_0), ((_S59)).xy, ((_S59)).z)).x;
                            if(prevD_5 <= 0.0f)
                            {
                                break;
                            }
                            var zHist_5 : f32 = ViewDistanceWith_0(prevD_5, p_0.prevProjection_0.z, p_0.prevProjection_0.w);
                            agreement_0 = max(agreement_0, saturate(2.0f - abs(zHist_5 - prevViewZ_0) / (max(6.0f * PixelHeight_0(zHist_5), 0.00009999999747379f) + wasSky_0)));
                            break;
                        }
                        break;
                    }
                    break;
                }
                break;
            }
            break;
        }
        for(;;)
        {
            for(;;)
            {
                for(;;)
                {
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S60 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(-1), i32(1)), _S18, _S26), i32(0));
                            var prevD_6 : f32 = (textureLoad((uPrevDepth_0), ((_S60)).xy, ((_S60)).z)).x;
                            if(prevD_6 <= 0.0f)
                            {
                                break;
                            }
                            var zHist_6 : f32 = ViewDistanceWith_0(prevD_6, p_0.prevProjection_0.z, p_0.prevProjection_0.w);
                            agreement_0 = max(agreement_0, saturate(2.0f - abs(zHist_6 - prevViewZ_0) / (max(6.0f * PixelHeight_0(zHist_6), 0.00009999999747379f) + wasSky_0)));
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S61 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(0), i32(1)), _S18, _S26), i32(0));
                            var prevD_7 : f32 = (textureLoad((uPrevDepth_0), ((_S61)).xy, ((_S61)).z)).x;
                            if(prevD_7 <= 0.0f)
                            {
                                break;
                            }
                            var zHist_7 : f32 = ViewDistanceWith_0(prevD_7, p_0.prevProjection_0.z, p_0.prevProjection_0.w);
                            agreement_0 = max(agreement_0, saturate(2.0f - abs(zHist_7 - prevViewZ_0) / (max(6.0f * PixelHeight_0(zHist_7), 0.00009999999747379f) + wasSky_0)));
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S62 : vec3<i32> = vec3<i32>(clamp(_S27 + vec2<i32>(i32(1), i32(1)), _S18, _S26), i32(0));
                            var prevD_8 : f32 = (textureLoad((uPrevDepth_0), ((_S62)).xy, ((_S62)).z)).x;
                            if(prevD_8 <= 0.0f)
                            {
                                break;
                            }
                            var zHist_8 : f32 = ViewDistanceWith_0(prevD_8, p_0.prevProjection_0.z, p_0.prevProjection_0.w);
                            agreement_0 = max(agreement_0, saturate(2.0f - abs(zHist_8 - prevViewZ_0) / (max(6.0f * PixelHeight_0(zHist_8), 0.00009999999747379f) + wasSky_0)));
                            break;
                        }
                        break;
                    }
                    break;
                }
                break;
            }
            break;
        }
        break;
    }
    textureStore((uRejection_0), (vec2<u32>(_S23)), (vec4<f32>(min(agreement_0, OccluderMask_0(prevRasterUv_1, prevViewZ_0, eOcc_1, _S24)), 0.0f, 0.0f, 0.0f)));
    return;
}

