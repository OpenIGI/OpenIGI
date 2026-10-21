struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct Params_std140_0
{
    @align(16) viewToWorld_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) resolution_0 : vec4<f32>,
    @align(16) cameraFrame_0 : vec4<f32>,
    @align(16) sunLight_0 : vec4<f32>,
    @align(16) lampControl_0 : vec4<f32>,
    @align(16) lampSource_0 : vec4<f32>,
    @align(16) lampPositionRadius_0 : array<vec4<f32>, i32(160)>,
    @align(16) lampColourIntensity_0 : array<vec4<f32>, i32(160)>,
    @align(16) lampDirection_0 : array<vec4<f32>, i32(160)>,
    @align(16) lampRect_0 : array<vec4<f32>, i32(160)>,
};

@binding(8) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(5) @group(0) var uVisibility_0 : texture_storage_2d<rgba16float, write>;

@binding(0) @group(0) var uDepth_0 : texture_depth_2d;

@binding(1) @group(0) var uDepthSampler_0 : sampler;

@binding(2) @group(0) var uNormal_0 : texture_2d<f32>;

@binding(3) @group(0) var uNormalSampler_0 : sampler;

@binding(10) @group(0) var uSunVisibility_0 : texture_2d<f32>;

struct _Array_std140_matrixx3Cfloatx2C4x2C4x3E240_0
{
    @align(16) data_1 : array<_MatrixStorage_float4x4_ColMajorstd140_0, i32(240)>,
};

struct FieldParams_std140_0
{
    @align(16) origin_0 : array<vec4<f32>, i32(4)>,
    @align(16) range_0 : vec4<f32>,
    @align(16) levels_0 : vec4<i32>,
    @align(16) mapMatrix_0 : _Array_std140_matrixx3Cfloatx2C4x2C4x3E240_0,
    @align(16) mapDepth_0 : array<vec4<f32>, i32(120)>,
    @align(16) mapSlot_0 : array<vec4<i32>, i32(40)>,
};

@binding(9) @group(0) var<uniform> f_0 : FieldParams_std140_0;
@binding(11) @group(0) var uLampMaps_0 : texture_depth_2d_array;

@binding(12) @group(0) var uLampMapSampler_0 : sampler_comparison;

struct CoarseParams_std140_0
{
    @align(16) record_0 : array<vec4<f32>, i32(3840)>,
};

@binding(14) @group(0) var<uniform> c_0 : CoarseParams_std140_0;
@binding(13) @group(0) var uCoarseMaps_0 : texture_depth_2d_array;

@binding(4) @group(0) var uClipmaps_0 : texture_3d<f32>;

@binding(6) @group(0) var uClipmapsSampler_0 : sampler;

fn rsqrt_0( x_0 : f32) -> f32
{
    return 1.0f / sqrt(x_0);
}

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

fn DepthBilinear_0( depth_1 : texture_depth_2d,  linear_0 : sampler,  uv_1 : vec2<f32>) -> f32
{
    return DepthBilinearLoaded_0(depth_1, uv_1);
}

fn ViewDepthAt_0( depth_2 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S8 : f32 = unproject_1.z;
    var _S9 : f32 = unproject_1.w;
    return 2.0f * _S8 * _S9 / (_S9 + _S8 - (1.0f - 2.0f * depth_2) * (_S9 - _S8));
}

fn ViewPositionAt_0( uv_2 : vec2<f32>,  depth_3 : f32,  unproject_2 : vec4<f32>) -> vec3<f32>
{
    var viewZ_0 : f32 = ViewDepthAt_0(depth_3, unproject_2);
    var ndc_0 : vec2<f32> = uv_2 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / unproject_2.x, - ndc_0.y * viewZ_0 / unproject_2.y, viewZ_0);
}

fn Pcg3d_0( v_0 : vec3<u32>) -> vec3<u32>
{
    var _S10 : vec3<u32> = v_0 * vec3<u32>(u32(1664525)) + vec3<u32>(u32(1013904223));
    var _S11 : vec3<u32> = _S10;
    _S11[i32(0)] = _S11[i32(0)] + _S10.y * _S10.z;
    _S11[i32(1)] = _S11[i32(1)] + _S11.z * _S11.x;
    _S11[i32(2)] = _S11[i32(2)] + _S11.x * _S11.y;
    var _S12 : vec3<u32> = (_S11 ^ (((_S11 >> (vec3<u32>(u32(16)))))));
    _S11 = _S12;
    _S11[i32(0)] = _S11[i32(0)] + _S12.y * _S12.z;
    _S11[i32(1)] = _S11[i32(1)] + _S11.z * _S11.x;
    _S11[i32(2)] = _S11[i32(2)] + _S11.x * _S11.y;
    return _S11;
}

fn WhiteNoise3_0( px_0 : vec2<i32>,  frame_0 : u32,  salt_0 : u32) -> vec3<f32>
{
    return vec3<f32>(((Pcg3d_0(vec3<u32>(vec2<u32>(px_0), (frame_0 ^ ((salt_0 * u32(2654435769))))))) >> (vec3<u32>(u32(8))))) * vec3<f32>(5.9604644775390625e-08f);
}

fn PixelRandom_0( px_1 : vec2<i32>) -> vec3<f32>
{
    return vec3<f32>(((Pcg3d_0(vec3<u32>(vec2<u32>(px_1), u32(2654435769)))) >> (vec3<u32>(u32(8))))) * vec3<f32>(5.9604644775390625e-08f);
}

fn LampReachWindow_0( distanceSq_0 : f32,  reach_0 : f32) -> f32
{
    var _S13 : f32 = reach_0 * reach_0;
    if(distanceSq_0 >= _S13)
    {
        return 0.0f;
    }
    var ratio_0 : f32 = distanceSq_0 / max(_S13, 0.00009999999747379f);
    var window_0 : f32 = clamp(1.0f - ratio_0 * ratio_0, 0.0f, 1.0f);
    return window_0 * window_0;
}

fn LampFaces_0( direction_0 : vec4<f32>) -> bool
{
    var _S14 : vec3<f32> = direction_0.xyz;
    return (dot(_S14, _S14)) > 0.25f;
}

fn LampCone_0( axis_0 : vec3<f32>,  centreDir_0 : vec3<f32>) -> f32
{
    return max(dot(normalize(axis_0), (vec3<f32>(0) - centreDir_0)), 0.0f);
}

fn LampSource_0( direction_1 : vec4<f32>) -> f32
{
    return clamp(direction_1.w, 0.05000000074505806f, 1.0f);
}

fn LampTubeBound_0( normal_0 : vec3<f32>,  toCentre_0 : vec3<f32>,  halfAxis_0 : vec3<f32>,  thickness_0 : f32) -> f32
{
    var start_0 : vec3<f32> = toCentre_0 - halfAxis_0;
    var end_0 : vec3<f32> = toCentre_0 + halfAxis_0;
    var _S15 : f32 = max(dot(start_0, start_0), 9.99999993922529029e-09f);
    var _S16 : f32 = max(dot(end_0, end_0), 9.99999993922529029e-09f);
    var _S17 : f32 = max(dot(toCentre_0, toCentre_0), 9.99999993922529029e-09f);
    var startFacing_0 : f32 = dot(normal_0, start_0) * rsqrt_0(_S15);
    var endFacing_0 : f32 = dot(normal_0, end_0) * rsqrt_0(_S16);
    if((max(startFacing_0, endFacing_0)) <= 0.0f)
    {
        return 0.0f;
    }
    var soft_0 : f32 = thickness_0 * thickness_0;
    return max(max(saturate(startFacing_0) / max(_S15, soft_0), saturate(endFacing_0) / max(_S16, soft_0)), saturate(dot(normal_0, toCentre_0) * rsqrt_0(_S17)) / max(_S17, soft_0));
}

fn LampPointIrradiance_0( normal_1 : vec3<f32>,  centreDir_1 : vec3<f32>,  distanceSq_1 : f32,  source_0 : f32) -> f32
{
    return saturate(dot(normal_1, centreDir_1)) / max(distanceSq_1, source_0 * source_0);
}

fn LampMapDepthTerms_0( slot_0 : i32) -> vec2<f32>
{
    var pair_0 : vec4<f32> = f_0.mapDepth_0[(slot_0 >> (u32(1)))];
    var _S18 : vec2<f32>;
    if(((slot_0 & (i32(1)))) != i32(0))
    {
        _S18 = pair_0.zw;
    }
    else
    {
        _S18 = pair_0.xy;
    }
    return _S18;
}

fn LampMapPlaneDepth_0( uv_3 : vec2<f32>,  scale_0 : f32,  normalView_0 : vec3<f32>,  planeOffset_0 : f32,  viewZ_1 : f32,  across_0 : f32) -> f32
{
    var facing_0 : f32 = dot(normalView_0, vec3<f32>(vec2<f32>(uv_3.x * 2.0f - 1.0f, 1.0f - uv_3.y * 2.0f) / vec2<f32>(scale_0), 1.0f));
    var z_0 : f32;
    if((abs(facing_0)) > 0.00009999999747379f)
    {
        z_0 = planeOffset_0 / facing_0;
    }
    else
    {
        z_0 = viewZ_1;
    }
    var reach_1 : f32 = 2.0f * across_0;
    return clamp(z_0, viewZ_1 - reach_1, viewZ_1 + reach_1);
}

fn LampMapDeviceDepth_0( viewZ_2 : f32,  terms_0 : vec2<f32>) -> f32
{
    return (terms_0.x + terms_0.y / max(viewZ_2, 0.00009999999747379f)) * 0.5f + 0.5f;
}

fn LampMapViewDepth_0( stored_0 : f32,  terms_1 : vec2<f32>) -> f32
{
    return terms_1.y / min(stored_0 * 2.0f - 1.0f - terms_1.x, -9.99999997475242708e-07f);
}

fn LampMapVisibility_0( lamp_0 : i32,  cameraRelative_0 : vec3<f32>,  receiverNormal_0 : vec3<f32>,  sourceWidth_0 : f32) -> f32
{
    var blockerSum_0 : f32;
    var entry_0 : i32 = f_0.mapSlot_0[(lamp_0 >> (u32(2)))][(lamp_0 & (i32(3)))];
    if((f_0.mapSlot_0[(lamp_0 >> (u32(2)))][(lamp_0 & (i32(3)))]) < i32(0))
    {
        return -1.0f;
    }
    var slot_1 : i32 = (entry_0 & (i32(255)));
    var _S19 : mat4x4<f32> = mat4x4<f32>(f_0.mapMatrix_0.data_1[slot_1].data_0[i32(0)][i32(0)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(1)][i32(0)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(2)][i32(0)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(3)][i32(0)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(0)][i32(1)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(1)][i32(1)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(2)][i32(1)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(3)][i32(1)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(0)][i32(2)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(1)][i32(2)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(2)][i32(2)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(3)][i32(2)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(0)][i32(3)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(1)][i32(3)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(2)][i32(3)], f_0.mapMatrix_0.data_1[slot_1].data_0[i32(3)][i32(3)]);
    var _S20 : vec4<f32> = vec4<f32>(cameraRelative_0, 1.0f);
    var clip_0 : vec4<f32> = (((_S20) * (_S19)));
    const _S21 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var blockerSum_1 : f32;
    var ndc_1 : vec3<f32>;
    var clip_1 : vec4<f32>;
    var m_0 : mat4x4<f32>;
    var by_0 : i32;
    var slot_2 : i32;
    var bx_0 : i32;
    if(((entry_0 & (i32(256)))) != i32(0))
    {
        var found_0 : bool = false;
        ndc_1 = _S21;
        clip_1 = clip_0;
        m_0 = _S19;
        by_0 = i32(0);
        slot_2 = slot_1;
        for(;;)
        {
            var _S22 : bool;
            if(by_0 < i32(6))
            {
                _S22 = !found_0;
            }
            else
            {
                _S22 = false;
            }
            if(_S22)
            {
            }
            else
            {
                break;
            }
            var _S23 : i32 = slot_2 + by_0;
            var _S24 : mat4x4<f32> = mat4x4<f32>(f_0.mapMatrix_0.data_1[_S23].data_0[i32(0)][i32(0)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(1)][i32(0)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(2)][i32(0)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(3)][i32(0)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(0)][i32(1)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(1)][i32(1)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(2)][i32(1)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(3)][i32(1)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(0)][i32(2)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(1)][i32(2)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(2)][i32(2)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(3)][i32(2)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(0)][i32(3)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(1)][i32(3)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(2)][i32(3)], f_0.mapMatrix_0.data_1[_S23].data_0[i32(3)][i32(3)]);
            var faceClip_0 : vec4<f32> = (((_S20) * (_S24)));
            var _S25 : f32 = faceClip_0.w;
            if(_S25 > 0.00009999999747379f)
            {
                var faceNdc_0 : vec3<f32> = faceClip_0.xyz / vec3<f32>(_S25);
                var found_1 : bool;
                var ndc_2 : vec3<f32>;
                var clip_2 : vec4<f32>;
                var m_1 : mat4x4<f32>;
                if((all(((abs(faceNdc_0.xy)) <= vec2<f32>(1.0f, 1.0f)))))
                {
                    found_1 = true;
                    ndc_2 = faceNdc_0;
                    clip_2 = faceClip_0;
                    m_1 = _S24;
                    bx_0 = _S23;
                }
                else
                {
                    found_1 = found_0;
                    ndc_2 = ndc_1;
                    clip_2 = clip_1;
                    m_1 = m_0;
                    bx_0 = slot_2;
                }
                found_0 = found_1;
                ndc_1 = ndc_2;
                clip_1 = clip_2;
                m_0 = m_1;
                slot_2 = bx_0;
            }
            by_0 = by_0 + i32(1);
        }
        if(!found_0)
        {
            return -2.0f;
        }
        if((ndc_1.z) < -1.0f)
        {
            return 1.0f;
        }
    }
    else
    {
        var nearTerms_0 : vec2<f32> = LampMapDepthTerms_0(slot_1);
        var mapNear_0 : f32 = nearTerms_0.y / (-1.0f - nearTerms_0.x);
        var _S26 : f32 = clip_0.w;
        if(_S26 <= mapNear_0)
        {
            return 1.0f;
        }
        var ndc_3 : vec3<f32> = clip_0.xyz / vec3<f32>(_S26);
        if((any(((abs(ndc_3.xy)) > vec2<f32>(0.98000001907348633f, 0.98000001907348633f)))))
        {
            var fromLamp_0 : vec3<f32> = vec3<f32>(clip_0.xy / vec2<f32>(max(length(vec3<f32>(_S19[i32(0)][i32(0)], _S19[i32(0)][i32(1)], _S19[i32(0)][i32(2)])), 0.00100000004749745f)), _S26 - mapNear_0);
            if((dot(fromLamp_0, fromLamp_0)) < 2.25f)
            {
                blockerSum_1 = 1.0f;
            }
            else
            {
                blockerSum_1 = -2.0f;
            }
            return blockerSum_1;
        }
        clip_1 = clip_0;
        m_0 = _S19;
        ndc_1 = ndc_3;
        slot_2 = slot_1;
    }
    var viewZ_3 : f32 = clip_1.w;
    var _S27 : vec3<f32> = vec3<f32>(m_0[i32(0)][i32(0)], m_0[i32(0)][i32(1)], m_0[i32(0)][i32(2)]);
    var _S28 : f32 = max(length(_S27), 0.00100000004749745f);
    var terms_2 : vec2<f32> = LampMapDepthTerms_0(slot_2);
    var nearPlane_0 : f32 = terms_2.y / (-1.0f - terms_2.x);
    var _S29 : f32 = ndc_1.x;
    var _S30 : f32 = ndc_1.y;
    var uv_4 : vec2<f32> = vec2<f32>(_S29 * 0.5f + 0.5f, 0.5f - _S30 * 0.5f);
    var width_1 : u32;
    var height_1 : u32;
    var layers_0 : u32;
    {var dim = textureDimensions((uLampMaps_0));((width_1)) = dim.x;((height_1)) = dim.y;((layers_0)) = textureNumLayers((uLampMaps_0));};
    var texel_0 : f32 = 1.0f / f32(max(width_1, u32(1)));
    var size_0 : vec2<i32> = vec2<i32>(i32(width_1), i32(height_1));
    var normalView_1 : vec3<f32> = vec3<f32>(dot(_S27, receiverNormal_0) / _S28, dot(vec3<f32>(m_0[i32(1)][i32(0)], m_0[i32(1)][i32(1)], m_0[i32(1)][i32(2)]), receiverNormal_0) / _S28, dot(vec3<f32>(m_0[i32(3)][i32(0)], m_0[i32(3)][i32(1)], m_0[i32(3)][i32(2)]), receiverNormal_0));
    var _S31 : f32 = dot(normalView_1, vec3<f32>(_S29 * viewZ_3 / _S28, _S30 * viewZ_3 / _S28, viewZ_3));
    var _S32 : f32 = 0.01499999966472387f + 0.00300000002607703f * viewZ_3 + 1.5f * (viewZ_3 * (2.0f / _S28) / f32(max(width_1, u32(1))));
    var frustumAtUnit_0 : f32 = 2.0f / _S28;
    var lightUv_0 : f32 = sourceWidth_0 / frustumAtUnit_0;
    var _S33 : f32 = max(viewZ_3 * 0.25f, nearPlane_0 + 0.00999999977648258f);
    var _S34 : f32 = clamp(lightUv_0 * 0.5f * max(viewZ_3 - _S33, 0.0f) / (_S33 * viewZ_3), 2.0f * texel_0, 32.0f * texel_0);
    by_0 = i32(-2);
    blockerSum_1 = 0.0f;
    var blockers_0 : f32 = 0.0f;
    for(;;)
    {
        if(by_0 <= i32(2))
        {
        }
        else
        {
            break;
        }
        bx_0 = i32(-2);
        blockerSum_0 = blockerSum_1;
        var blockers_1 : f32 = blockers_0;
        for(;;)
        {
            if(bx_0 <= i32(2))
            {
            }
            else
            {
                break;
            }
            var at_0 : vec2<f32> = uv_4 + vec2<f32>(f32(bx_0), f32(by_0)) * vec2<f32>((_S34 * 0.5f));
            var _S35 : vec4<i32> = vec4<i32>(clamp(vec2<i32>(at_0 * vec2<f32>(size_0)), vec2<i32>(i32(0), i32(0)), size_0 - vec2<i32>(i32(1))), slot_2, i32(0));
            var stored_1 : f32 = (textureLoad((uLampMaps_0), ((_S35)).xy, i32(((_S35)).z), ((_S35)).w));
            if(stored_1 < (LampMapDeviceDepth_0(max(LampMapPlaneDepth_0(at_0, _S28, normalView_1, _S31, viewZ_3, length(at_0 - uv_4) * frustumAtUnit_0 * viewZ_3) - _S32, nearPlane_0 + 0.00499999988824129f), terms_2)))
            {
                var blockers_2 : f32 = blockers_1 + 1.0f;
                blockerSum_0 = blockerSum_0 + LampMapViewDepth_0(stored_1, terms_2);
                blockers_1 = blockers_2;
            }
            bx_0 = bx_0 + i32(1);
        }
        by_0 = by_0 + i32(1);
        blockerSum_1 = blockerSum_0;
        blockers_0 = blockers_1;
    }
    if(blockers_0 <= 0.0f)
    {
        return 1.0f;
    }
    var _S36 : f32 = max(blockerSum_1 / blockers_0, 0.05000000074505806f);
    var _S37 : f32 = clamp(lightUv_0 * max(viewZ_3 - _S36, 0.0f) / (_S36 * viewZ_3) * 0.5f, texel_0, 24.0f * texel_0);
    var angle_0 : f32 = fract(52.98291778564453125f * fract(dot(uv_4 * vec2<f32>(size_0), vec2<f32>(0.06711056083440781f, 0.00583714991807938f)))) * 6.28318548202514648f;
    var _S38 : f32 = sin(angle_0);
    var _S39 : f32 = cos(angle_0);
    var _S40 : array<vec2<f32>, i32(12)> = array<vec2<f32>, i32(12)>( vec2<f32>(-0.32600000500679016f, -0.40599998831748962f), vec2<f32>(-0.8399999737739563f, -0.07400000095367432f), vec2<f32>(-0.69599997997283936f, 0.45699998736381531f), vec2<f32>(-0.20299999415874481f, 0.62099999189376831f), vec2<f32>(0.96200001239776611f, -0.19499999284744263f), vec2<f32>(0.47299998998641968f, -0.47999998927116394f), vec2<f32>(0.51899999380111694f, 0.76700001955032349f), vec2<f32>(0.18500000238418579f, -0.89300000667572021f), vec2<f32>(0.50700002908706665f, 0.06400000303983688f), vec2<f32>(0.89600002765655518f, 0.41200000047683716f), vec2<f32>(-0.32199999690055847f, -0.93300002813339233f), vec2<f32>(-0.79199999570846558f, -0.59799998998641968f) );
    bx_0 = i32(0);
    blockerSum_0 = 0.0f;
    for(;;)
    {
        if(bx_0 < i32(12))
        {
        }
        else
        {
            break;
        }
        var _S41 : f32 = _S40[bx_0].x;
        var _S42 : f32 = _S40[bx_0].y;
        var offset_0 : vec2<f32> = vec2<f32>(_S41 * _S39 - _S42 * _S38, _S41 * _S38 + _S42 * _S39) * vec2<f32>(_S37);
        var at_1 : vec2<f32> = uv_4 + offset_0;
        var _S43 : vec3<f32> = vec3<f32>(at_1, f32(slot_2));
        var sum_0 : f32 = blockerSum_0 + (textureSampleCompareLevel((uLampMaps_0), (uLampMapSampler_0), ((_S43)).xy, i32(((_S43)).z), (LampMapDeviceDepth_0(max(LampMapPlaneDepth_0(at_1, _S28, normalView_1, _S31, viewZ_3, length(offset_0) * frustumAtUnit_0 * viewZ_3) - _S32, nearPlane_0 + 0.00499999988824129f), terms_2))));
        bx_0 = bx_0 + i32(1);
        blockerSum_0 = sum_0;
    }
    return blockerSum_0 / 12.0f;
}

fn CoarseLampVisibility_0( lamp_1 : i32,  cameraRelative_1 : vec3<f32>,  normal_2 : vec3<f32>) -> f32
{
    var _S44 : bool;
    if(lamp_1 < i32(0))
    {
        _S44 = true;
    }
    else
    {
        _S44 = lamp_1 >= i32(160);
    }
    if(_S44)
    {
        return -1.0f;
    }
    var first_0 : i32 = lamp_1 * i32(6);
    if((c_0.record_0[first_0 * i32(4) + i32(3)].z) <= 0.0f)
    {
        return -1.0f;
    }
    var width_2 : u32;
    var height_2 : u32;
    var layers_1 : u32;
    {var dim = textureDimensions((uCoarseMaps_0));((width_2)) = dim.x;((height_2)) = dim.y;((layers_1)) = textureNumLayers((uCoarseMaps_0));};
    var pointZ_0 : f32 = 0.25f;
    var face_0 : i32 = i32(0);
    for(;;)
    {
        if(face_0 < i32(6))
        {
        }
        else
        {
            break;
        }
        var _S45 : f32 = max(pointZ_0, dot(c_0.record_0[(first_0 + face_0) * i32(4) + i32(2)].xyz, cameraRelative_1) + c_0.record_0[(first_0 + face_0) * i32(4) + i32(2)].w);
        var _S46 : i32 = face_0 + i32(1);
        pointZ_0 = _S45;
        face_0 = _S46;
    }
    var at_2 : vec3<f32> = cameraRelative_1 + normal_2 * vec3<f32>((0.05000000074505806f + 1.5f * (pointZ_0 * 2.0f / f32(max(width_2, u32(1))))));
    var deepest_0 : f32 = -1.00000001504746622e+30f;
    var slot_3 : i32 = first_0;
    face_0 = i32(0);
    for(;;)
    {
        if(face_0 < i32(6))
        {
        }
        else
        {
            break;
        }
        var _S47 : i32 = first_0 + face_0;
        var depth_4 : f32 = dot(c_0.record_0[_S47 * i32(4) + i32(2)].xyz, at_2) + c_0.record_0[_S47 * i32(4) + i32(2)].w;
        if(depth_4 > deepest_0)
        {
            deepest_0 = depth_4;
            slot_3 = _S47;
        }
        face_0 = face_0 + i32(1);
    }
    var _S48 : i32 = slot_3 * i32(4);
    var terms_3 : vec4<f32> = c_0.record_0[_S48 + i32(3)];
    if((c_0.record_0[_S48 + i32(3)].z) <= 0.0f)
    {
        return -1.0f;
    }
    var row0_0 : vec4<f32> = c_0.record_0[_S48];
    var row1_0 : vec4<f32> = c_0.record_0[_S48 + i32(1)];
    var _S49 : f32 = terms_3.y;
    var _S50 : f32 = terms_3.x;
    if(deepest_0 <= (_S49 / (-1.0f - _S50)))
    {
        return 1.0f;
    }
    var _S51 : vec3<f32> = row0_0.xyz;
    var ndc_4 : vec2<f32> = vec2<f32>(dot(_S51, at_2) + row0_0.w, dot(row1_0.xyz, at_2) + row1_0.w) / vec2<f32>(deepest_0);
    var size_1 : vec2<i32> = vec2<i32>(i32(width_2), i32(height_2));
    var _S52 : f32 = 0.05000000074505806f + 0.00499999988824129f * deepest_0 + 2.0f * (deepest_0 * (2.0f / max(length(_S51), 0.00100000004749745f)) / f32(max(width_2, u32(1))));
    var _S53 : i32 = i32(terms_3.w + 0.5f);
    var texelAt_0 : vec2<f32> = vec2<f32>(ndc_4.x * 0.5f + 0.5f, 0.5f - ndc_4.y * 0.5f) * vec2<f32>(size_1) - vec2<f32>(0.5f);
    var _S54 : vec2<f32> = floor(texelAt_0);
    var _S55 : vec2<i32> = vec2<i32>(i32(1));
    var _S56 : vec2<i32> = vec2<i32>(_S54) - _S55;
    var _S57 : vec2<f32> = texelAt_0 - _S54;
    var y_0 : i32 = i32(0);
    var lit_0 : f32 = 0.0f;
    var weightSum_0 : f32 = 0.0f;
    for(;;)
    {
        if(y_0 < i32(4))
        {
        }
        else
        {
            break;
        }
        var x_1 : i32 = i32(0);
        for(;;)
        {
            if(x_1 < i32(4))
            {
            }
            else
            {
                break;
            }
            var _S58 : vec4<i32> = vec4<i32>(clamp(_S56 + vec2<i32>(x_1, y_0), vec2<i32>(i32(0), i32(0)), size_1 - _S55), _S53, i32(0));
            var tent_0 : vec2<f32> = saturate(vec2<f32>(1.0f) - abs(vec2<f32>(f32(x_1) - 1.0f, f32(y_0) - 1.0f) - _S57) / vec2<f32>(2.0f));
            var weight_1 : f32 = tent_0.x * tent_0.y;
            if((_S49 / min((textureLoad((uCoarseMaps_0), ((_S58)).xy, i32(((_S58)).z), ((_S58)).w)) * 2.0f - 1.0f - _S50, -9.99999997475242708e-07f)) >= (deepest_0 - _S52))
            {
                pointZ_0 = weight_1;
            }
            else
            {
                pointZ_0 = 0.0f;
            }
            var lit_1 : f32 = lit_0 + pointZ_0;
            var weightSum_1 : f32 = weightSum_0 + weight_1;
            x_1 = x_1 + i32(1);
            lit_0 = lit_1;
            weightSum_0 = weightSum_1;
        }
        y_0 = y_0 + i32(1);
    }
    return lit_0 / max(weightSum_0, 0.00009999999747379f);
}

fn ProjectToUv_0( viewPosition_0 : vec3<f32>) -> vec2<f32>
{
    var _S59 : f32 = viewPosition_0.z;
    var _S60 : vec2<f32> = vec2<f32>(0.5f);
    return vec2<f32>(viewPosition_0.x * p_0.unproject_0.x / _S59, - viewPosition_0.y * p_0.unproject_0.y / _S59) * _S60 + _S60;
}

fn LampLevelDistance_0( level_0 : i32,  voxel_0 : vec3<f32>) -> f32
{
    return ((textureSampleLevel((uClipmaps_0), (uClipmapsSampler_0), (vec3<f32>(voxel_0.x / f32(f_0.levels_0.y), voxel_0.y / f32(f_0.levels_0.y), (f32(level_0) * f32(f_0.levels_0.y) + voxel_0.z) / f32(f_0.levels_0.y * f_0.levels_0.x))), (0.0f))).x * 2.0f - 1.0f) * f_0.range_0[level_0];
}

fn LampDistanceAt_0( world_0 : vec3<f32>,  known_0 : ptr<function, bool>,  resolved_0 : ptr<function, f32>) -> f32
{
    (*known_0) = false;
    (*resolved_0) = f_0.origin_0[i32(0)].w;
    var level_1 : i32 = i32(0);
    for(;;)
    {
        if(level_1 < (f_0.levels_0.x))
        {
        }
        else
        {
            break;
        }
        var _S61 : f32 = f_0.origin_0[level_1].w;
        var voxel_1 : vec3<f32> = (world_0 - f_0.origin_0[level_1].xyz) / vec3<f32>(_S61);
        var top_0 : f32 = f32(f_0.levels_0.y) - 0.5f;
        const _S62 : vec3<f32> = vec3<f32>(0.5f, 0.5f, 0.5f);
        var _S63 : bool;
        if((any((voxel_1 < _S62))))
        {
            _S63 = true;
        }
        else
        {
            _S63 = (any((voxel_1 > vec3<f32>(top_0, top_0, top_0))));
        }
        if(_S63)
        {
            level_1 = level_1 + i32(1);
            continue;
        }
        (*known_0) = true;
        var here_0 : f32 = LampLevelDistance_0(level_1, voxel_1);
        (*resolved_0) = _S61;
        var fromEdge_0 : vec3<f32> = min(voxel_1 - vec3<f32>(0.5f), vec3<f32>(top_0) - voxel_1);
        var handOver_0 : f32 = saturate(1.0f - min(fromEdge_0.x, min(fromEdge_0.y, fromEdge_0.z)) / 8.0f);
        if(handOver_0 <= 0.0f)
        {
            _S63 = true;
        }
        else
        {
            _S63 = (level_1 + i32(1)) >= (f_0.levels_0.x);
        }
        if(_S63)
        {
            return here_0;
        }
        var _S64 : i32 = level_1 + i32(1);
        var _S65 : f32 = f_0.origin_0[_S64].w;
        var outerVoxel_0 : vec3<f32> = (world_0 - f_0.origin_0[_S64].xyz) / vec3<f32>(_S65);
        if((any((outerVoxel_0 < _S62))))
        {
            _S63 = true;
        }
        else
        {
            _S63 = (any((outerVoxel_0 > vec3<f32>(top_0, top_0, top_0))));
        }
        if(_S63)
        {
            return here_0;
        }
        (*resolved_0) = mix(_S61, _S65, handOver_0);
        return mix(here_0, LampLevelDistance_0(_S64, outerVoxel_0), handOver_0);
    }
    return 1.0e+06f;
}

fn SoftwareBlocked_0( from_0 : vec3<f32>,  direction_2 : vec3<f32>,  segment_0 : f32,  receiverLift_0 : f32,  receiverRise_0 : f32,  lampDistance_0 : f32,  lampRise_0 : f32,  hitT_0 : ptr<function, f32>,  nearMiss_0 : ptr<function, f32>) -> bool
{
    var leaving_0 : bool;
    var _S66 : bool;
    (*hitT_0) = 0.0f;
    var _S67 : f32 = min(segment_0, 1.0f);
    var screenT_0 : f32 = 0.0f;
    var i_0 : i32 = i32(1);
    for(;;)
    {
        if(i_0 <= i32(16))
        {
        }
        else
        {
            break;
        }
        var t_0 : f32 = _S67 * (f32(i_0) / 16.0f);
        var view_0 : vec3<f32> = (((mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])) * (vec4<f32>(from_0 + direction_2 * vec3<f32>(t_0), 0.0f)))).xyz;
        var _S68 : f32 = view_0.z;
        if(_S68 <= (p_0.unproject_0.z))
        {
            break;
        }
        var uv_5 : vec2<f32> = ProjectToUv_0(view_0);
        if((any((uv_5 < vec2<f32>(0.0f, 0.0f)))))
        {
            leaving_0 = true;
        }
        else
        {
            leaving_0 = (any((uv_5 > vec2<f32>(1.0f, 1.0f))));
        }
        if(leaving_0)
        {
            break;
        }
        var depth_5 : f32 = DepthBilinear_0(uDepth_0, uDepthSampler_0, uv_5);
        if(depth_5 > 0.0f)
        {
            var sceneZ_0 : f32 = ViewDepthAt_0(depth_5, p_0.unproject_0);
            var behind_0 : f32 = _S68 - sceneZ_0;
            if(behind_0 > 0.01999999955296516f)
            {
                _S66 = behind_0 < (max(0.05000000074505806f, 0.00999999977648258f * sceneZ_0));
            }
            else
            {
                _S66 = false;
            }
            if(_S66)
            {
                (*hitT_0) = t_0;
                return true;
            }
        }
        var _S69 : i32 = i_0 + i32(1);
        screenT_0 = t_0;
        i_0 = _S69;
    }
    if((f_0.levels_0.x) <= i32(0))
    {
        return false;
    }
    var _S70 : f32 = 0.5f * f_0.origin_0[i32(0)].w;
    var _S71 : f32 = max(screenT_0, _S70);
    var end_1 : f32 = segment_0 - _S70;
    var startKnown_0 : bool;
    var startResolved_0 : f32;
    var _S72 : f32 = LampDistanceAt_0(from_0 + direction_2 * vec3<f32>(_S71), &(startKnown_0), &(startResolved_0));
    var endKnown_0 : bool;
    var endResolved_0 : f32;
    var _S73 : f32 = LampDistanceAt_0(from_0 + direction_2 * vec3<f32>(end_1), &(endKnown_0), &(endResolved_0));
    leaving_0 = true;
    var leaveBy_0 : f32 = 0.0f;
    var step_0 : i32 = i32(0);
    var travelled_0 : f32 = _S71;
    for(;;)
    {
        if(step_0 < i32(64))
        {
            _S66 = travelled_0 < end_1;
        }
        else
        {
            _S66 = false;
        }
        if(_S66)
        {
        }
        else
        {
            break;
        }
        var known_1 : bool;
        var resolved_1 : f32;
        var distance_0 : f32 = LampDistanceAt_0(from_0 + direction_2 * vec3<f32>(travelled_0), &(known_1), &(resolved_1));
        if(!known_1)
        {
            return false;
        }
        var leaveBy_1 : f32;
        var leaving_1 : bool;
        if(leaving_0)
        {
            if(leaveBy_0 <= 0.0f)
            {
                leaveBy_1 = travelled_0 + 1.5f * resolved_1;
            }
            else
            {
                leaveBy_1 = leaveBy_0;
            }
            if(distance_0 <= 0.0f)
            {
                leaving_1 = travelled_0 < leaveBy_1;
            }
            else
            {
                leaving_1 = false;
            }
            if(leaving_1)
            {
                travelled_0 = travelled_0 + resolved_1 * 0.25f;
                var _S74 : i32 = step_0 + i32(1);
                leaveBy_0 = leaveBy_1;
                step_0 = _S74;
                continue;
            }
            leaving_1 = false;
        }
        else
        {
            leaving_1 = leaving_0;
            leaveBy_1 = leaveBy_0;
        }
        var hitRadius_0 : f32 = 0.5f * resolved_1 * saturate(travelled_0 / (2.0f * resolved_1));
        var margin_0 : f32 = 0.25f * resolved_1;
        var aboveReceiver_0 : f32;
        if(startKnown_0)
        {
            aboveReceiver_0 = _S72 + (travelled_0 - _S71) * receiverRise_0;
        }
        else
        {
            aboveReceiver_0 = receiverLift_0 + travelled_0 * receiverRise_0;
        }
        var toLamp_0 : f32 = lampDistance_0 - travelled_0;
        var _S75 : bool = lampRise_0 <= 0.0f;
        var belowLamp_0 : f32;
        if(_S75)
        {
            belowLamp_0 = 1.0e+09f;
        }
        else
        {
            if(endKnown_0)
            {
                screenT_0 = _S73 + (end_1 - travelled_0) * lampRise_0;
            }
            else
            {
                screenT_0 = toLamp_0 * lampRise_0;
            }
            belowLamp_0 = screenT_0;
        }
        var atFitting_0 : bool;
        if(_S75)
        {
            atFitting_0 = toLamp_0 < (1.5f * resolved_1);
        }
        else
        {
            atFitting_0 = false;
        }
        var touching_0 : bool = distance_0 < hitRadius_0;
        var _S76 : bool;
        var _S77 : bool;
        if(touching_0)
        {
            if(atFitting_0)
            {
                _S76 = true;
            }
            else
            {
                _S76 = distance_0 >= (aboveReceiver_0 - margin_0);
            }
            if(_S76)
            {
                _S77 = true;
            }
            else
            {
                _S77 = distance_0 >= (belowLamp_0 - 0.25f * resolved_1);
            }
            _S76 = _S77;
        }
        else
        {
            _S76 = false;
        }
        if(_S76)
        {
            (*nearMiss_0) = 1.0f;
        }
        if(!atFitting_0)
        {
            _S77 = touching_0;
        }
        else
        {
            _S77 = false;
        }
        var _S78 : bool;
        if(_S77)
        {
            _S78 = distance_0 < (aboveReceiver_0 - margin_0);
        }
        else
        {
            _S78 = false;
        }
        var _S79 : bool;
        if(_S78)
        {
            _S79 = distance_0 < (belowLamp_0 - 0.25f * resolved_1);
        }
        else
        {
            _S79 = false;
        }
        if(_S79)
        {
            (*hitT_0) = travelled_0;
            return true;
        }
        var travelled_1 : f32 = travelled_0 + max(distance_0, resolved_1 * 0.25f);
        leaving_0 = leaving_1;
        travelled_0 = travelled_1;
        var _S74 : i32 = step_0 + i32(1);
        leaveBy_0 = leaveBy_1;
        step_0 = _S74;
    }
    return false;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var window_1 : f32;
    var lit_2 : f32;
    var stopShort_0 : f32;
    var _S80 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S81 : bool;
    if((_S80.x) >= i32(p_0.resolution_0.x))
    {
        _S81 = true;
    }
    else
    {
        _S81 = (_S80.y) >= i32(p_0.resolution_0.y);
    }
    if(_S81)
    {
        return;
    }
    var lampCount_0 : i32 = i32(p_0.lampControl_0.x + 0.5f);
    if(lampCount_0 <= i32(0))
    {
        textureStore((uVisibility_0), (vec2<u32>(_S80)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var uv_6 : vec2<f32> = (vec2<f32>(_S80) + vec2<f32>(0.5f)) * p_0.resolution_0.zw;
    var depth_6 : f32 = DepthBilinear_0(uDepth_0, uDepthSampler_0, uv_6);
    if(depth_6 <= 0.0f)
    {
        textureStore((uVisibility_0), (vec2<u32>(_S80)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var viewPos_0 : vec3<f32> = ViewPositionAt_0(uv_6, depth_6, p_0.unproject_0);
    var normal_3 : vec3<f32> = normalize((textureSampleLevel((uNormal_0), (uNormalSampler_0), (uv_6), (0.0f))).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f));
    var _S82 : f32 = viewPos_0.z;
    var originLift_0 : f32 = 0.07999999821186066f + 0.00050000002374873f * _S82;
    var _S83 : vec3<f32> = (((vec4<f32>(viewPos_0, 1.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + p_0.cameraFrame_0.xyz + normal_3 * vec3<f32>(originLift_0);
    var rawNoise_0 : bool = (p_0.lampControl_0.w) > 0.5f;
    var frame_1 : f32;
    if(rawNoise_0)
    {
        frame_1 = 0.0f;
    }
    else
    {
        frame_1 = f32(u32(p_0.cameraFrame_0.w) % u32(65536));
    }
    var pixelRandom_0 : vec3<f32>;
    if(rawNoise_0)
    {
        pixelRandom_0 = WhiteNoise3_0(_S80, u32(p_0.cameraFrame_0.w), u32(6717));
    }
    else
    {
        pixelRandom_0 = PixelRandom_0(_S80);
    }
    var chosen_0 : array<i32, i32(2)>;
    var pick_0 : array<f32, i32(2)>;
    var _S84 : f32 = fract(pixelRandom_0.x + frame_1 * 0.6180340051651001f);
    var k_0 : i32 = i32(0);
    for(;;)
    {
        if(k_0 < i32(2))
        {
        }
        else
        {
            break;
        }
        chosen_0[k_0] = i32(-1);
        pick_0[k_0] = fract(_S84 + f32(k_0) / 2.0f);
        k_0 = k_0 + i32(1);
    }
    var i_1 : i32 = i32(0);
    var weightSum_2 : f32 = 0.0f;
    for(;;)
    {
        if(i_1 < lampCount_0)
        {
        }
        else
        {
            break;
        }
        var toLightView_0 : vec3<f32> = p_0.lampPositionRadius_0[i_1].xyz - viewPos_0;
        var distanceSq_2 : f32 = dot(toLightView_0, toLightView_0);
        var window_2 : f32 = LampReachWindow_0(distanceSq_2, p_0.lampPositionRadius_0[i_1].w);
        if(window_2 <= 0.0f)
        {
            i_1 = i_1 + i32(1);
            continue;
        }
        var toCentre_1 : vec3<f32> = (((vec4<f32>(toLightView_0, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz;
        var centreDir_2 : vec3<f32> = toCentre_1 * vec3<f32>(rsqrt_0(max(distanceSq_2, 9.99999993922529029e-09f)));
        var facing_1 : vec4<f32> = p_0.lampDirection_0[i_1];
        if(LampFaces_0(p_0.lampDirection_0[i_1]))
        {
            var window_3 : f32 = window_2 * LampCone_0((((vec4<f32>(facing_1.xyz, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz, centreDir_2);
            if(window_3 <= 0.0f)
            {
                i_1 = i_1 + i32(1);
                continue;
            }
            window_1 = window_3;
        }
        else
        {
            window_1 = window_2;
        }
        var source_1 : f32 = LampSource_0(p_0.lampDirection_0[i_1]);
        var rect_0 : vec4<f32> = p_0.lampRect_0[i_1];
        var halfAxis_1 : vec3<f32> = (((vec4<f32>(p_0.lampRect_0[i_1].xyz, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz;
        if((dot(halfAxis_1, halfAxis_1)) > 0.00009999999747379f)
        {
            lit_2 = LampTubeBound_0(normal_3, toCentre_1, halfAxis_1, clamp(rect_0.w, 0.01999999955296516f, source_1));
        }
        else
        {
            lit_2 = LampPointIrradiance_0(normal_3, centreDir_2, distanceSq_2, source_1);
        }
        var weight_2 : f32 = 2.0f * window_1 * lit_2 * dot(p_0.lampColourIntensity_0[i_1].xyz * vec3<f32>(p_0.lampColourIntensity_0[i_1].w), vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f));
        if(weight_2 <= 9.99999997475242708e-07f)
        {
            i_1 = i_1 + i32(1);
            continue;
        }
        var weightSum_3 : f32 = weightSum_2 + weight_2;
        var _S85 : f32 = weight_2 / max(weightSum_3, 9.99999997475242708e-07f);
        k_0 = i32(0);
        for(;;)
        {
            if(k_0 < i32(2))
            {
            }
            else
            {
                break;
            }
            if((pick_0[k_0]) < _S85)
            {
                chosen_0[k_0] = i_1;
                pick_0[k_0] = pick_0[k_0] / max(_S85, 9.99999997475242708e-07f);
            }
            else
            {
                pick_0[k_0] = (pick_0[k_0] - _S85) / max(1.0f - _S85, 9.99999997475242708e-07f);
            }
            pick_0[k_0] = min(pick_0[k_0], 0.99999994039535522f);
            k_0 = k_0 + i32(1);
        }
        weightSum_2 = weightSum_3;
        i_1 = i_1 + i32(1);
    }
    if((p_0.sunLight_0.w) > 0.0f)
    {
        _S81 = weightSum_2 > 0.0f;
    }
    else
    {
        _S81 = false;
    }
    if(_S81)
    {
        var _S86 : vec3<i32> = vec3<i32>(_S80, i32(0));
        window_1 = p_0.sunLight_0.w * max(dot(normal_3, p_0.sunLight_0.xyz), 0.0f) * saturate((textureLoad((uSunVisibility_0), ((_S86)).xy, ((_S86)).z)).x);
    }
    else
    {
        window_1 = 0.0f;
    }
    if(weightSum_2 <= 0.0f)
    {
        _S81 = true;
    }
    else
    {
        _S81 = weightSum_2 < (p_0.lampSource_0.w);
    }
    if(_S81)
    {
        _S81 = true;
    }
    else
    {
        _S81 = weightSum_2 < (0.00999999977648258f * window_1);
    }
    if(_S81)
    {
        textureStore((uVisibility_0), (vec2<u32>(_S80)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    k_0 = i32(0);
    lit_2 = 0.0f;
    var mappedLitSum_0 : f32 = 0.0f;
    var blocked_0 : f32 = 0.0f;
    var penumbraSum_0 : f32 = 0.0f;
    var fieldRays_0 : f32 = 0.0f;
    var nearMissSum_0 : f32 = 0.0f;
    for(;;)
    {
        if(k_0 < i32(2))
        {
        }
        else
        {
            break;
        }
        var lamp_2 : i32 = chosen_0[k_0];
        var lampCentre_0 : vec3<f32> = (((vec4<f32>(p_0.lampPositionRadius_0[chosen_0[k_0]].xyz, 1.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + p_0.cameraFrame_0.xyz;
        var lampDir_0 : vec3<f32> = normalize(lampCentre_0 - _S83);
        var source_2 : f32 = clamp(p_0.lampDirection_0[chosen_0[k_0]].w, 0.01999999955296516f, p_0.lampSource_0.x);
        const _S87 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        var _S88 : vec3<f32> = p_0.lampDirection_0[chosen_0[k_0]].xyz;
        var emitting_0 : vec3<f32>;
        var axis_1 : vec3<f32>;
        if((dot(_S88, _S88)) > 0.25f)
        {
            var axis_2 : vec3<f32> = normalize((((vec4<f32>(_S88, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz);
            emitting_0 = axis_2 * vec3<f32>(0.01999999955296516f);
            axis_1 = axis_2;
        }
        else
        {
            emitting_0 = _S87;
            axis_1 = lampDir_0;
        }
        var rand_0 : vec2<f32> = fract(pixelRandom_0.yz + vec2<f32>((frame_1 * 2.0f + f32(k_0))) * vec2<f32>(0.75487768650054932f, 0.56984031200408936f));
        var rect_1 : vec4<f32> = p_0.lampRect_0[lamp_2];
        var _S89 : bool = (dot(emitting_0, emitting_0)) > 0.0f;
        if(_S89)
        {
            _S81 = (rect_1.w) > 0.0f;
        }
        else
        {
            _S81 = false;
        }
        var _S90 : bool;
        if(_S81)
        {
            var _S91 : vec3<f32> = rect_1.xyz;
            _S90 = (dot(_S91, _S91)) > 0.0f;
        }
        else
        {
            _S90 = false;
        }
        var target_0 : vec3<f32>;
        if(_S90)
        {
            var halfLength_0 : vec3<f32> = (((vec4<f32>(rect_1.xyz, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz;
            var uv_7 : vec2<f32> = rand_0 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
            target_0 = lampCentre_0 + emitting_0 + halfLength_0 * vec3<f32>(uv_7.x) + normalize(cross(axis_1, halfLength_0)) * vec3<f32>(rect_1.w) * vec3<f32>(uv_7.y);
        }
        else
        {
            if((abs(axis_1.z)) < 0.89999997615814209f)
            {
                target_0 = vec3<f32>(0.0f, 0.0f, 1.0f);
            }
            else
            {
                target_0 = vec3<f32>(1.0f, 0.0f, 0.0f);
            }
            var across_1 : vec3<f32> = normalize(cross(target_0, axis_1));
            var spread_0 : f32 = sqrt(rand_0.x) * source_2;
            var around_0 : f32 = 6.28318548202514648f * rand_0.y;
            target_0 = lampCentre_0 + emitting_0 + across_1 * vec3<f32>((spread_0 * cos(around_0))) + cross(axis_1, across_1) * vec3<f32>((spread_0 * sin(around_0)));
        }
        var toTarget_0 : vec3<f32> = target_0 - _S83;
        var distance_1 : f32 = length(toTarget_0);
        var _S92 : bool;
        if(_S89)
        {
            _S92 = (rect_1.w) > 0.0f;
        }
        else
        {
            _S92 = false;
        }
        var onRect_0 : bool;
        if(_S92)
        {
            var _S93 : vec3<f32> = rect_1.xyz;
            onRect_0 = (dot(_S93, _S93)) > 0.0f;
        }
        else
        {
            onRect_0 = false;
        }
        if(onRect_0)
        {
            stopShort_0 = 0.05000000074505806f;
        }
        else
        {
            stopShort_0 = clamp(source_2 * 0.5f, 0.05000000074505806f, 0.20000000298023224f);
        }
        var blocked_1 : f32;
        var penumbraSum_1 : f32;
        if(distance_1 <= (stopShort_0 + 0.01999999955296516f))
        {
            lit_2 = lit_2 + 1.0f;
            blocked_1 = blocked_0;
            penumbraSum_1 = penumbraSum_0;
            k_0 = k_0 + i32(1);
            blocked_0 = blocked_1;
            penumbraSum_0 = penumbraSum_1;
            continue;
        }
        var t_1 : f32;
        var nearMiss_1 : f32 = 0.0f;
        var _S94 : vec3<f32> = _S83 - p_0.cameraFrame_0.xyz;
        if(onRect_0)
        {
            weightSum_2 = length(rect_1.xyz) + rect_1.w;
        }
        else
        {
            weightSum_2 = 2.0f * source_2;
        }
        var mapped_0 : f32 = LampMapVisibility_0(lamp_2, _S94, normal_3, weightSum_2);
        var mapped_1 : f32;
        if(mapped_0 == -1.0f)
        {
            mapped_1 = CoarseLampVisibility_0(lamp_2, _S83 - p_0.cameraFrame_0.xyz, normal_3);
        }
        else
        {
            mapped_1 = mapped_0;
        }
        if(mapped_1 >= 0.0f)
        {
            var mappedLitSum_1 : f32 = mappedLitSum_0 + mapped_1;
            var visibleSum_0 : f32 = lit_2 + mapped_1;
            if(mapped_1 < 1.0f)
            {
                var _S95 : f32 = 1.0f - mapped_1;
                var penumbraSum_2 : f32 = penumbraSum_0 + _S95 * 0.05000000074505806f;
                blocked_1 = blocked_0 + _S95;
                penumbraSum_1 = penumbraSum_2;
            }
            else
            {
                blocked_1 = blocked_0;
                penumbraSum_1 = penumbraSum_0;
            }
            lit_2 = visibleSum_0;
            mappedLitSum_0 = mappedLitSum_1;
            k_0 = k_0 + i32(1);
            blocked_0 = blocked_1;
            penumbraSum_0 = penumbraSum_1;
            continue;
        }
        var fieldRays_1 : f32 = fieldRays_0 + 1.0f;
        var rayDirection_0 : vec3<f32> = toTarget_0 / vec3<f32>(distance_1);
        var _S96 : vec3<f32> = _S83 - p_0.cameraFrame_0.xyz;
        var _S97 : f32 = distance_1 - stopShort_0;
        var _S98 : f32 = max(dot(rayDirection_0, normal_3), 0.0f);
        if(_S89)
        {
            window_1 = dot((vec3<f32>(0) - rayDirection_0), axis_1);
        }
        else
        {
            window_1 = 0.0f;
        }
        var hit_0 : bool = SoftwareBlocked_0(_S96, rayDirection_0, _S97, originLift_0, _S98, distance_1, window_1, &(t_1), &(nearMiss_1));
        var nearMissSum_1 : f32 = nearMissSum_0 + nearMiss_1;
        var visibleSum_1 : f32;
        if(!hit_0)
        {
            visibleSum_1 = lit_2 + 1.0f;
            blocked_1 = blocked_0;
            penumbraSum_1 = penumbraSum_0;
        }
        else
        {
            if(onRect_0)
            {
                visibleSum_1 = length(rect_1.xyz) + rect_1.w;
            }
            else
            {
                visibleSum_1 = 2.0f * source_2;
            }
            var penumbraSum_3 : f32 = penumbraSum_0 + visibleSum_1 * t_1 / max(distance_1 - t_1, 0.05000000074505806f);
            var blocked_2 : f32 = blocked_0 + 1.0f;
            visibleSum_1 = lit_2;
            blocked_1 = blocked_2;
            penumbraSum_1 = penumbraSum_3;
        }
        lit_2 = visibleSum_1;
        fieldRays_0 = fieldRays_1;
        nearMissSum_0 = nearMissSum_1;
        k_0 = k_0 + i32(1);
        blocked_0 = blocked_1;
        penumbraSum_0 = penumbraSum_1;
    }
    if(blocked_0 > 0.0f)
    {
        stopShort_0 = clamp(penumbraSum_0 / blocked_0, 0.0020000000949949f, 1.0f);
    }
    else
    {
        stopShort_0 = 0.0020000000949949f;
    }
    if((f_0.levels_0.z) == i32(2))
    {
        textureStore((uVisibility_0), (vec2<u32>(_S80)), (vec4<f32>(mappedLitSum_0 / 2.0f, 0.0020000000949949f + fieldRays_0 / 2.0f, 0.0f, 0.0f)));
        return;
    }
    if((f_0.levels_0.z) != i32(0))
    {
        textureStore((uVisibility_0), (vec2<u32>(_S80)), (vec4<f32>(blocked_0 / 2.0f, max(nearMissSum_0 / 2.0f, 0.0020000000949949f), 0.0f, 0.0f)));
        return;
    }
    var _S99 : vec2<u32> = vec2<u32>(_S80);
    var _S100 : f32 = lit_2 / 2.0f;
    if(rawNoise_0)
    {
        frame_1 = _S82;
    }
    else
    {
        frame_1 = 0.0f;
    }
    textureStore((uVisibility_0), (_S99), (vec4<f32>(_S100, stopShort_0, frame_1, 0.0f)));
    return;
}

