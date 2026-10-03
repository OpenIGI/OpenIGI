struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct Params_std140_0
{
    @align(16) viewToWorld_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) resolution_0 : vec4<f32>,
    @align(16) jitter_0 : vec4<f32>,
};

@binding(7) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uAccumulated_0 : texture_2d<f32>;

@binding(6) @group(0) var uOutput_0 : texture_storage_2d<rgba16float, write>;

@binding(2) @group(0) var uDepth_0 : texture_depth_2d;

@binding(3) @group(0) var uDepthSampler_0 : sampler;

@binding(4) @group(0) var uNormal_0 : texture_2d<f32>;

fn AccumulatedAt_0( px_0 : vec2<i32>) -> vec4<f32>
{
    var _S1 : vec3<i32> = vec3<i32>(px_0, i32(0));
    return (textureLoad((uAccumulated_0), ((_S1)).xy, ((_S1)).z));
}

fn JitteredDepthUv_0( px_1 : vec2<i32>) -> vec2<f32>
{
    return (vec2<f32>(px_1) + vec2<f32>(0.5f) - p_0.jitter_0.xy) * p_0.resolution_0.zw;
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
    var _S2 : vec2<i32> = vec2<i32>(floored_0);
    const _S3 : vec2<i32> = vec2<i32>(i32(0), i32(0));
    var _S4 : vec3<i32> = vec3<i32>(clamp(_S2, _S3, last_0), i32(0));
    var _S5 : vec3<i32> = vec3<i32>(clamp(_S2 + vec2<i32>(i32(1), i32(0)), _S3, last_0), i32(0));
    var _S6 : vec3<i32> = vec3<i32>(clamp(_S2 + vec2<i32>(i32(0), i32(1)), _S3, last_0), i32(0));
    var _S7 : vec3<i32> = vec3<i32>(clamp(_S2 + vec2<i32>(i32(1), i32(1)), _S3, last_0), i32(0));
    var _S8 : f32 = weight_0.x;
    return mix(mix((textureLoad((depth_0), ((_S4)).xy, ((_S4)).z)), (textureLoad((depth_0), ((_S5)).xy, ((_S5)).z)), _S8), mix((textureLoad((depth_0), ((_S6)).xy, ((_S6)).z)), (textureLoad((depth_0), ((_S7)).xy, ((_S7)).z)), _S8), weight_0.y);
}

fn DepthBilinear_0( depth_1 : texture_depth_2d,  linear_0 : sampler,  uv_1 : vec2<f32>) -> f32
{
    return DepthBilinearLoaded_0(depth_1, uv_1);
}

fn ViewDepthAt_0( depth_2 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S9 : f32 = unproject_1.z;
    var _S10 : f32 = unproject_1.w;
    return 2.0f * _S9 * _S10 / (_S10 + _S9 - (1.0f - 2.0f * depth_2) * (_S10 - _S9));
}

fn ViewPositionAt_0( uv_2 : vec2<f32>,  depth_3 : f32,  unproject_2 : vec4<f32>) -> vec3<f32>
{
    var viewZ_0 : f32 = ViewDepthAt_0(depth_3, unproject_2);
    var ndc_0 : vec2<f32> = uv_2 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / unproject_2.x, - ndc_0.y * viewZ_0 / unproject_2.y, viewZ_0);
}

fn NormalAt_0( uv_3 : vec2<f32>) -> vec3<f32>
{
    var width_1 : u32;
    var height_1 : u32;
    {var dim = textureDimensions((uNormal_0));((width_1)) = dim.x;((height_1)) = dim.y;};
    var _S11 : vec3<i32> = vec3<i32>(clamp(vec2<i32>(floor(uv_3 * vec2<f32>(f32(width_1), f32(height_1)))), vec2<i32>(i32(0), i32(0)), vec2<i32>(i32(width_1) - i32(1), i32(height_1) - i32(1))), i32(0));
    return normalize((textureLoad((uNormal_0), ((_S11)).xy, ((_S11)).z)).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f));
}

fn MixBits_0( x_0 : u32) -> u32
{
    var _S12 : u32 = ((x_0 ^ (((x_0 >> (u32(16))))))) * u32(2146121005);
    var _S13 : u32 = ((_S12 ^ (((_S12 >> (u32(15))))))) * u32(2221713035);
    return (_S13 ^ (((_S13 >> (u32(16))))));
}

fn UnitFloat_0( bits_0 : u32) -> f32
{
    return f32((bits_0 >> (u32(8)))) * 5.9604644775390625e-08f;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S14 : vec2<i32>;
    var _S15 : f32;
    var weightSum_0 : f32;
    var visSum_0 : f32;
    var visSquares_0 : f32;
    var _S16 : f32;
    var _S17 : f32;
    var _S18 : f32;
    var _S19 : f32;
    var _S20 : f32;
    var _S21 : f32;
    var _S22 : f32;
    var _S23 : f32;
    var _S24 : f32;
    var _S25 : f32;
    var _S26 : f32;
    var _S27 : f32;
    var _S28 : f32;
    var _S29 : f32;
    var _S30 : f32;
    var _S31 : f32;
    var _S32 : f32;
    var _S33 : f32;
    var _S34 : f32;
    var _S35 : f32;
    var _S36 : f32;
    var _S37 : f32;
    var _S38 : f32;
    var _S39 : f32;
    var _S40 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S41 : vec2<i32> = vec2<i32>(p_0.resolution_0.xy);
    var _S42 : i32 = _S40.x;
    var inPenumbra_0 : bool;
    if(_S42 >= (_S41.x))
    {
        inPenumbra_0 = true;
    }
    else
    {
        inPenumbra_0 = (_S40.y) >= (_S41.y);
    }
    if(inPenumbra_0)
    {
        return;
    }
    var centre_0 : vec4<f32> = AccumulatedAt_0(_S40);
    var _S43 : f32 = centre_0.y;
    if(_S43 <= 0.0f)
    {
        textureStore((uOutput_0), (vec2<u32>(_S40)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var _S44 : f32 = centre_0.z;
    if(_S44 <= 0.0f)
    {
        textureStore((uOutput_0), (vec2<u32>(_S40)), (vec4<f32>(centre_0.x, _S43, 0.0f, 0.0f)));
        return;
    }
    var depthUv_0 : vec2<f32> = JitteredDepthUv_0(_S40);
    var depth_4 : f32 = DepthBilinear_0(uDepth_0, uDepthSampler_0, depthUv_0);
    if(depth_4 <= 0.0f)
    {
        textureStore((uOutput_0), (vec2<u32>(_S40)), (vec4<f32>(centre_0.x, _S43, 0.0f, 0.0f)));
        return;
    }
    var viewPos_0 : vec3<f32> = ViewPositionAt_0(depthUv_0, depth_4, p_0.unproject_0);
    var _S45 : vec3<f32> = (((vec4<f32>(viewPos_0, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz;
    var _S46 : vec3<f32> = NormalAt_0(depthUv_0);
    var disocclusion_0 : f32 = 1.0f - saturate((_S44 - 1.0f) * 0.5f);
    var _S47 : vec2<i32> = _S41 - vec2<i32>(i32(1));
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
                            var _S48 : vec2<i32> = _S40 + vec2<i32>(i32(-2), i32(-2));
                            const _S49 : vec2<i32> = vec2<i32>(i32(0), i32(0));
                            _S14 = _S49;
                            var tap_0 : vec2<f32> = AccumulatedAt_0(clamp(_S48, _S49, _S47)).xy;
                            var _S50 : f32 = tap_0.y;
                            _S15 = max(_S43, _S50);
                            if(_S50 <= 0.0f)
                            {
                                weightSum_0 = 0.0f;
                                visSum_0 = 0.0f;
                                visSquares_0 = 0.0f;
                                break;
                            }
                            var weight_1 : f32 = exp2(-1.77777779102325439f);
                            var _S51 : f32 = tap_0.x;
                            var _S52 : f32 = _S51 * weight_1;
                            var _S53 : f32 = _S51 * _S51 * weight_1;
                            weightSum_0 = weight_1;
                            visSum_0 = _S52;
                            visSquares_0 = _S53;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_1 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(-1), i32(-2)), _S14, _S47)).xy;
                            var _S54 : f32 = tap_1.y;
                            _S16 = max(_S15, _S54);
                            if(_S54 <= 0.0f)
                            {
                                break;
                            }
                            var weight_2 : f32 = exp2(-1.11111116409301758f);
                            var _S55 : f32 = tap_1.x;
                            var visSum_1 : f32 = visSum_0 + _S55 * weight_2;
                            var visSquares_1 : f32 = visSquares_0 + _S55 * _S55 * weight_2;
                            weightSum_0 = weightSum_0 + weight_2;
                            visSum_0 = visSum_1;
                            visSquares_0 = visSquares_1;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_2 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(0), i32(-2)), _S14, _S47)).xy;
                            var _S56 : f32 = tap_2.y;
                            _S17 = max(_S16, _S56);
                            if(_S56 <= 0.0f)
                            {
                                break;
                            }
                            var weight_3 : f32 = exp2(-0.8888888955116272f);
                            var _S57 : f32 = tap_2.x;
                            var visSum_2 : f32 = visSum_0 + _S57 * weight_3;
                            var visSquares_2 : f32 = visSquares_0 + _S57 * _S57 * weight_3;
                            weightSum_0 = weightSum_0 + weight_3;
                            visSum_0 = visSum_2;
                            visSquares_0 = visSquares_2;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_3 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(1), i32(-2)), _S14, _S47)).xy;
                            var _S58 : f32 = tap_3.y;
                            _S18 = max(_S17, _S58);
                            if(_S58 <= 0.0f)
                            {
                                break;
                            }
                            var weight_4 : f32 = exp2(-1.11111116409301758f);
                            var _S59 : f32 = tap_3.x;
                            var visSum_3 : f32 = visSum_0 + _S59 * weight_4;
                            var visSquares_3 : f32 = visSquares_0 + _S59 * _S59 * weight_4;
                            weightSum_0 = weightSum_0 + weight_4;
                            visSum_0 = visSum_3;
                            visSquares_0 = visSquares_3;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_4 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(2), i32(-2)), _S14, _S47)).xy;
                            var _S60 : f32 = tap_4.y;
                            _S19 = max(_S18, _S60);
                            if(_S60 <= 0.0f)
                            {
                                break;
                            }
                            var weight_5 : f32 = exp2(-1.77777779102325439f);
                            var _S61 : f32 = tap_4.x;
                            var visSum_4 : f32 = visSum_0 + _S61 * weight_5;
                            var visSquares_4 : f32 = visSquares_0 + _S61 * _S61 * weight_5;
                            weightSum_0 = weightSum_0 + weight_5;
                            visSum_0 = visSum_4;
                            visSquares_0 = visSquares_4;
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
                            var tap_5 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(-2), i32(-1)), _S14, _S47)).xy;
                            var _S62 : f32 = tap_5.y;
                            _S20 = max(_S19, _S62);
                            if(_S62 <= 0.0f)
                            {
                                break;
                            }
                            var weight_6 : f32 = exp2(-1.11111116409301758f);
                            var _S63 : f32 = tap_5.x;
                            var visSum_5 : f32 = visSum_0 + _S63 * weight_6;
                            var visSquares_5 : f32 = visSquares_0 + _S63 * _S63 * weight_6;
                            weightSum_0 = weightSum_0 + weight_6;
                            visSum_0 = visSum_5;
                            visSquares_0 = visSquares_5;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_6 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(-1), i32(-1)), _S14, _S47)).xy;
                            var _S64 : f32 = tap_6.y;
                            _S21 = max(_S20, _S64);
                            if(_S64 <= 0.0f)
                            {
                                break;
                            }
                            var weight_7 : f32 = exp2(-0.4444444477558136f);
                            var _S65 : f32 = tap_6.x;
                            var visSum_6 : f32 = visSum_0 + _S65 * weight_7;
                            var visSquares_6 : f32 = visSquares_0 + _S65 * _S65 * weight_7;
                            weightSum_0 = weightSum_0 + weight_7;
                            visSum_0 = visSum_6;
                            visSquares_0 = visSquares_6;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_7 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(0), i32(-1)), _S14, _S47)).xy;
                            var _S66 : f32 = tap_7.y;
                            _S22 = max(_S21, _S66);
                            if(_S66 <= 0.0f)
                            {
                                break;
                            }
                            var weight_8 : f32 = exp2(-0.2222222238779068f);
                            var _S67 : f32 = tap_7.x;
                            var visSum_7 : f32 = visSum_0 + _S67 * weight_8;
                            var visSquares_7 : f32 = visSquares_0 + _S67 * _S67 * weight_8;
                            weightSum_0 = weightSum_0 + weight_8;
                            visSum_0 = visSum_7;
                            visSquares_0 = visSquares_7;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_8 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(1), i32(-1)), _S14, _S47)).xy;
                            var _S68 : f32 = tap_8.y;
                            _S23 = max(_S22, _S68);
                            if(_S68 <= 0.0f)
                            {
                                break;
                            }
                            var weight_9 : f32 = exp2(-0.4444444477558136f);
                            var _S69 : f32 = tap_8.x;
                            var visSum_8 : f32 = visSum_0 + _S69 * weight_9;
                            var visSquares_8 : f32 = visSquares_0 + _S69 * _S69 * weight_9;
                            weightSum_0 = weightSum_0 + weight_9;
                            visSum_0 = visSum_8;
                            visSquares_0 = visSquares_8;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_9 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(2), i32(-1)), _S14, _S47)).xy;
                            var _S70 : f32 = tap_9.y;
                            _S24 = max(_S23, _S70);
                            if(_S70 <= 0.0f)
                            {
                                break;
                            }
                            var weight_10 : f32 = exp2(-1.11111116409301758f);
                            var _S71 : f32 = tap_9.x;
                            var visSum_9 : f32 = visSum_0 + _S71 * weight_10;
                            var visSquares_9 : f32 = visSquares_0 + _S71 * _S71 * weight_10;
                            weightSum_0 = weightSum_0 + weight_10;
                            visSum_0 = visSum_9;
                            visSquares_0 = visSquares_9;
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
                            var tap_10 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(-2), i32(0)), _S14, _S47)).xy;
                            var _S72 : f32 = tap_10.y;
                            _S25 = max(_S24, _S72);
                            if(_S72 <= 0.0f)
                            {
                                break;
                            }
                            var weight_11 : f32 = exp2(-0.8888888955116272f);
                            var _S73 : f32 = tap_10.x;
                            var visSum_10 : f32 = visSum_0 + _S73 * weight_11;
                            var visSquares_10 : f32 = visSquares_0 + _S73 * _S73 * weight_11;
                            weightSum_0 = weightSum_0 + weight_11;
                            visSum_0 = visSum_10;
                            visSquares_0 = visSquares_10;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_11 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(-1), i32(0)), _S14, _S47)).xy;
                            var _S74 : f32 = tap_11.y;
                            _S26 = max(_S25, _S74);
                            if(_S74 <= 0.0f)
                            {
                                break;
                            }
                            var weight_12 : f32 = exp2(-0.2222222238779068f);
                            var _S75 : f32 = tap_11.x;
                            var visSum_11 : f32 = visSum_0 + _S75 * weight_12;
                            var visSquares_11 : f32 = visSquares_0 + _S75 * _S75 * weight_12;
                            weightSum_0 = weightSum_0 + weight_12;
                            visSum_0 = visSum_11;
                            visSquares_0 = visSquares_11;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_12 : vec2<f32> = AccumulatedAt_0(clamp(_S40, _S14, _S47)).xy;
                            var _S76 : f32 = tap_12.y;
                            _S27 = max(_S26, _S76);
                            if(_S76 <= 0.0f)
                            {
                                break;
                            }
                            var weight_13 : f32 = exp2(-0.0f);
                            var _S77 : f32 = tap_12.x;
                            var visSum_12 : f32 = visSum_0 + _S77 * weight_13;
                            var visSquares_12 : f32 = visSquares_0 + _S77 * _S77 * weight_13;
                            weightSum_0 = weightSum_0 + weight_13;
                            visSum_0 = visSum_12;
                            visSquares_0 = visSquares_12;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_13 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(1), i32(0)), _S14, _S47)).xy;
                            var _S78 : f32 = tap_13.y;
                            _S28 = max(_S27, _S78);
                            if(_S78 <= 0.0f)
                            {
                                break;
                            }
                            var weight_14 : f32 = exp2(-0.2222222238779068f);
                            var _S79 : f32 = tap_13.x;
                            var visSum_13 : f32 = visSum_0 + _S79 * weight_14;
                            var visSquares_13 : f32 = visSquares_0 + _S79 * _S79 * weight_14;
                            weightSum_0 = weightSum_0 + weight_14;
                            visSum_0 = visSum_13;
                            visSquares_0 = visSquares_13;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_14 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(2), i32(0)), _S14, _S47)).xy;
                            var _S80 : f32 = tap_14.y;
                            _S29 = max(_S28, _S80);
                            if(_S80 <= 0.0f)
                            {
                                break;
                            }
                            var weight_15 : f32 = exp2(-0.8888888955116272f);
                            var _S81 : f32 = tap_14.x;
                            var visSum_14 : f32 = visSum_0 + _S81 * weight_15;
                            var visSquares_14 : f32 = visSquares_0 + _S81 * _S81 * weight_15;
                            weightSum_0 = weightSum_0 + weight_15;
                            visSum_0 = visSum_14;
                            visSquares_0 = visSquares_14;
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
                            var tap_15 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(-2), i32(1)), _S14, _S47)).xy;
                            var _S82 : f32 = tap_15.y;
                            _S30 = max(_S29, _S82);
                            if(_S82 <= 0.0f)
                            {
                                break;
                            }
                            var weight_16 : f32 = exp2(-1.11111116409301758f);
                            var _S83 : f32 = tap_15.x;
                            var visSum_15 : f32 = visSum_0 + _S83 * weight_16;
                            var visSquares_15 : f32 = visSquares_0 + _S83 * _S83 * weight_16;
                            weightSum_0 = weightSum_0 + weight_16;
                            visSum_0 = visSum_15;
                            visSquares_0 = visSquares_15;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_16 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(-1), i32(1)), _S14, _S47)).xy;
                            var _S84 : f32 = tap_16.y;
                            _S31 = max(_S30, _S84);
                            if(_S84 <= 0.0f)
                            {
                                break;
                            }
                            var weight_17 : f32 = exp2(-0.4444444477558136f);
                            var _S85 : f32 = tap_16.x;
                            var visSum_16 : f32 = visSum_0 + _S85 * weight_17;
                            var visSquares_16 : f32 = visSquares_0 + _S85 * _S85 * weight_17;
                            weightSum_0 = weightSum_0 + weight_17;
                            visSum_0 = visSum_16;
                            visSquares_0 = visSquares_16;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_17 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(0), i32(1)), _S14, _S47)).xy;
                            var _S86 : f32 = tap_17.y;
                            _S32 = max(_S31, _S86);
                            if(_S86 <= 0.0f)
                            {
                                break;
                            }
                            var weight_18 : f32 = exp2(-0.2222222238779068f);
                            var _S87 : f32 = tap_17.x;
                            var visSum_17 : f32 = visSum_0 + _S87 * weight_18;
                            var visSquares_17 : f32 = visSquares_0 + _S87 * _S87 * weight_18;
                            weightSum_0 = weightSum_0 + weight_18;
                            visSum_0 = visSum_17;
                            visSquares_0 = visSquares_17;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_18 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(1), i32(1)), _S14, _S47)).xy;
                            var _S88 : f32 = tap_18.y;
                            _S33 = max(_S32, _S88);
                            if(_S88 <= 0.0f)
                            {
                                break;
                            }
                            var weight_19 : f32 = exp2(-0.4444444477558136f);
                            var _S89 : f32 = tap_18.x;
                            var visSum_18 : f32 = visSum_0 + _S89 * weight_19;
                            var visSquares_18 : f32 = visSquares_0 + _S89 * _S89 * weight_19;
                            weightSum_0 = weightSum_0 + weight_19;
                            visSum_0 = visSum_18;
                            visSquares_0 = visSquares_18;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_19 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(2), i32(1)), _S14, _S47)).xy;
                            var _S90 : f32 = tap_19.y;
                            _S34 = max(_S33, _S90);
                            if(_S90 <= 0.0f)
                            {
                                break;
                            }
                            var weight_20 : f32 = exp2(-1.11111116409301758f);
                            var _S91 : f32 = tap_19.x;
                            var visSum_19 : f32 = visSum_0 + _S91 * weight_20;
                            var visSquares_19 : f32 = visSquares_0 + _S91 * _S91 * weight_20;
                            weightSum_0 = weightSum_0 + weight_20;
                            visSum_0 = visSum_19;
                            visSquares_0 = visSquares_19;
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
                            var tap_20 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(-2), i32(2)), _S14, _S47)).xy;
                            var _S92 : f32 = tap_20.y;
                            _S35 = max(_S34, _S92);
                            if(_S92 <= 0.0f)
                            {
                                break;
                            }
                            var weight_21 : f32 = exp2(-1.77777779102325439f);
                            var _S93 : f32 = tap_20.x;
                            var visSum_20 : f32 = visSum_0 + _S93 * weight_21;
                            var visSquares_20 : f32 = visSquares_0 + _S93 * _S93 * weight_21;
                            weightSum_0 = weightSum_0 + weight_21;
                            visSum_0 = visSum_20;
                            visSquares_0 = visSquares_20;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_21 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(-1), i32(2)), _S14, _S47)).xy;
                            var _S94 : f32 = tap_21.y;
                            _S36 = max(_S35, _S94);
                            if(_S94 <= 0.0f)
                            {
                                break;
                            }
                            var weight_22 : f32 = exp2(-1.11111116409301758f);
                            var _S95 : f32 = tap_21.x;
                            var visSum_21 : f32 = visSum_0 + _S95 * weight_22;
                            var visSquares_21 : f32 = visSquares_0 + _S95 * _S95 * weight_22;
                            weightSum_0 = weightSum_0 + weight_22;
                            visSum_0 = visSum_21;
                            visSquares_0 = visSquares_21;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_22 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(0), i32(2)), _S14, _S47)).xy;
                            var _S96 : f32 = tap_22.y;
                            _S37 = max(_S36, _S96);
                            if(_S96 <= 0.0f)
                            {
                                break;
                            }
                            var weight_23 : f32 = exp2(-0.8888888955116272f);
                            var _S97 : f32 = tap_22.x;
                            var visSum_22 : f32 = visSum_0 + _S97 * weight_23;
                            var visSquares_22 : f32 = visSquares_0 + _S97 * _S97 * weight_23;
                            weightSum_0 = weightSum_0 + weight_23;
                            visSum_0 = visSum_22;
                            visSquares_0 = visSquares_22;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_23 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(1), i32(2)), _S14, _S47)).xy;
                            var _S98 : f32 = tap_23.y;
                            _S38 = max(_S37, _S98);
                            if(_S98 <= 0.0f)
                            {
                                break;
                            }
                            var weight_24 : f32 = exp2(-1.11111116409301758f);
                            var _S99 : f32 = tap_23.x;
                            var visSum_23 : f32 = visSum_0 + _S99 * weight_24;
                            var visSquares_23 : f32 = visSquares_0 + _S99 * _S99 * weight_24;
                            weightSum_0 = weightSum_0 + weight_24;
                            visSum_0 = visSum_23;
                            visSquares_0 = visSquares_23;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var tap_24 : vec2<f32> = AccumulatedAt_0(clamp(_S40 + vec2<i32>(i32(2), i32(2)), _S14, _S47)).xy;
                            var _S100 : f32 = tap_24.y;
                            _S39 = max(_S38, _S100);
                            if(_S100 <= 0.0f)
                            {
                                break;
                            }
                            var weight_25 : f32 = exp2(-1.77777779102325439f);
                            var _S101 : f32 = tap_24.x;
                            var visSum_24 : f32 = visSum_0 + _S101 * weight_25;
                            var visSquares_24 : f32 = visSquares_0 + _S101 * _S101 * weight_25;
                            weightSum_0 = weightSum_0 + weight_25;
                            visSum_0 = visSum_24;
                            visSquares_0 = visSquares_24;
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
    var _S102 : f32 = max(weightSum_0, 9.99999997475242708e-07f);
    var mean_0 : f32 = visSum_0 / _S102;
    var sigma_0 : f32 = sqrt(max(visSquares_0 / _S102 - mean_0 * mean_0, 0.0f)) * mix(1.0f, 2.0f, disocclusion_0);
    var relative_0 : f32 = sigma_0 / max(mean_0, 0.10000000149011612f);
    var _S103 : f32 = viewPos_0.z;
    var penumbraRadius_0 : f32 = clamp(0.5f * _S39 * (p_0.unproject_0.x * 0.5f * p_0.resolution_0.x / max(_S103, 0.00100000004749745f)), 1.0f, 24.0f);
    if(mean_0 > 0.01999999955296516f)
    {
        inPenumbra_0 = mean_0 < 0.98000001907348633f;
    }
    else
    {
        inPenumbra_0 = false;
    }
    if(inPenumbra_0)
    {
        inPenumbra_0 = penumbraRadius_0 > 1.5f;
    }
    else
    {
        inPenumbra_0 = false;
    }
    var noisy_0 : bool = relative_0 > (mix(0.15000000596046448f, 0.10000000149011612f, disocclusion_0));
    var _S104 : vec2<f32> = centre_0.xy;
    var _S105 : bool;
    if((p_0.jitter_0.w) > 0.5f)
    {
        if(inPenumbra_0)
        {
            _S105 = true;
        }
        else
        {
            _S105 = noisy_0;
        }
    }
    else
    {
        _S105 = false;
    }
    var result_0 : vec2<f32>;
    if(_S105)
    {
        var _S106 : i32;
        if(inPenumbra_0)
        {
            _S106 = i32(16);
        }
        else
        {
            _S106 = i32(round(mix(12.0f, 24.0f, disocclusion_0)));
        }
        if(inPenumbra_0)
        {
            weightSum_0 = penumbraRadius_0;
        }
        else
        {
            weightSum_0 = 8.0f;
        }
        if(!inPenumbra_0)
        {
            inPenumbra_0 = _S44 >= 6.0f;
        }
        else
        {
            inPenumbra_0 = false;
        }
        var _S107 : f32 = UnitFloat_0(MixBits_0((((u32((_S42 & (i32(1)))) | (((u32(((_S40.y) & (i32(1)))) << (u32(1))))))) | (((u32(p_0.jitter_0.z) << (u32(2)))))))) * 6.28318548202514648f;
        var i_0 : i32 = i32(0);
        result_0 = _S104;
        var total_0 : f32 = 1.0f;
        for(;;)
        {
            if(i_0 < i32(24))
            {
            }
            else
            {
                break;
            }
            if(i_0 >= _S106)
            {
                break;
            }
            var _S108 : f32 = f32(i_0);
            var angle_0 : f32 = _S107 + _S108 * 2.3999631404876709f;
            var _S109 : vec2<i32> = vec2<i32>(floor(vec2<f32>(_S40) + vec2<f32>(cos(angle_0), sin(angle_0)) * vec2<f32>((weightSum_0 * sqrt((_S108 + 0.5f) / f32(_S106)))) + vec2<f32>(0.5f)));
            if((any((_S109 < _S14))))
            {
                _S105 = true;
            }
            else
            {
                _S105 = (any((_S109 >= _S41)));
            }
            if(_S105)
            {
                i_0 = i_0 + i32(1);
                continue;
            }
            var tap_25 : vec2<f32> = AccumulatedAt_0(_S109).xy;
            if((tap_25.y) <= 0.0f)
            {
                i_0 = i_0 + i32(1);
                continue;
            }
            var tapUv_0 : vec2<f32> = JitteredDepthUv_0(_S109);
            var tapDepth_0 : f32 = DepthBilinear_0(uDepth_0, uDepthSampler_0, tapUv_0);
            if(tapDepth_0 <= 0.0f)
            {
                i_0 = i_0 + i32(1);
                continue;
            }
            var planeDistance_0 : f32 = abs(dot((((vec4<f32>(ViewPositionAt_0(tapUv_0, tapDepth_0, p_0.unproject_0), 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz - _S45, _S46)) / max(_S103, 0.00009999999747379f);
            var planeWeight_0 : f32 = exp2(-10000.0f * planeDistance_0 * planeDistance_0);
            var normalWeight_0 : f32 = 1.0f - saturate(acos(clamp(dot(_S46, NormalAt_0(tapUv_0)), -1.0f, 1.0f)));
            var valueWeight_0 : f32;
            if(inPenumbra_0)
            {
                valueWeight_0 = exp2(- abs(centre_0.x - tap_25.x) / max(sigma_0, 0.00100000004749745f));
            }
            else
            {
                valueWeight_0 = 1.0f;
            }
            var weight_26 : f32 = planeWeight_0 * normalWeight_0 * valueWeight_0;
            var total_1 : f32 = total_0 + weight_26;
            result_0 = result_0 + tap_25 * vec2<f32>(weight_26);
            total_0 = total_1;
            i_0 = i_0 + i32(1);
        }
        result_0 = result_0 / vec2<f32>(total_0);
    }
    else
    {
        result_0 = _S104;
    }
    textureStore((uOutput_0), (vec2<u32>(_S40)), (vec4<f32>(saturate(result_0.x), saturate(result_0.y), _S103, 0.0f)));
    return;
}

