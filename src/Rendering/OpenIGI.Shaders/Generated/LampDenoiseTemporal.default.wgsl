struct _MatrixStorage_float4x4_ColMajorstd140_0
{
    @align(16) data_0 : array<vec4<f32>, i32(4)>,
};

struct Params_std140_0
{
    @align(16) viewToWorld_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) prevWorldToView_0 : _MatrixStorage_float4x4_ColMajorstd140_0,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) prevUnproject_0 : vec4<f32>,
    @align(16) resolution_0 : vec4<f32>,
    @align(16) cameraDelta_0 : vec4<f32>,
    @align(16) jitter_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

@binding(7) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(2) @group(0) var uDepth_0 : texture_depth_2d;

@binding(3) @group(0) var uDepthSampler_0 : sampler;

@binding(6) @group(0) var uOutput_0 : texture_storage_2d<rgba16float, write>;

@binding(0) @group(0) var uRaw_0 : texture_2d<f32>;

@binding(8) @group(0) var uVelocity_0 : texture_2d<f32>;

@binding(4) @group(0) var uHistory_0 : texture_2d<f32>;

fn JitteredDepthUv_0( px_0 : vec2<i32>) -> vec2<f32>
{
    return (vec2<f32>(px_0) + vec2<f32>(0.5f) - p_0.jitter_0.xy) * p_0.resolution_0.zw;
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

fn RawAt_0( px_1 : vec2<i32>) -> vec4<f32>
{
    var _S10 : vec3<i32> = vec3<i32>(clamp(px_1, vec2<i32>(i32(0), i32(0)), vec2<i32>(p_0.resolution_0.xy) - vec2<i32>(i32(1))), i32(0));
    return (textureLoad((uRaw_0), ((_S10)).xy, ((_S10)).z));
}

fn NeighbourhoodRange_0( px_2 : vec2<i32>,  centre_0 : vec2<f32>,  low_0 : ptr<function, vec2<f32>>,  high_0 : ptr<function, vec2<f32>>)
{
    var _S11 : bool;
    var _S12 : bool;
    var sum_0 : vec2<f32>;
    var sumSquares_0 : vec2<f32>;
    var count_0 : f32;
    var _S13 : bool;
    var _S14 : bool;
    var _S15 : bool;
    var _S16 : bool;
    const _S17 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
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
                            var _S18 : bool = (abs(i32(-2))) == i32(2);
                            _S11 = _S18;
                            if(_S18)
                            {
                                _S12 = _S18;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                sum_0 = _S17;
                                sumSquares_0 = _S17;
                                count_0 = 0.0f;
                                break;
                            }
                            var tap_0 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(-2), i32(-2))).xy;
                            if((tap_0.y) <= 0.0f)
                            {
                                sum_0 = _S17;
                                sumSquares_0 = _S17;
                                count_0 = 0.0f;
                                break;
                            }
                            var _S19 : vec2<f32> = tap_0 * tap_0;
                            sum_0 = tap_0;
                            sumSquares_0 = _S19;
                            count_0 = 1.0f;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S20 : bool = (abs(i32(-1))) == i32(2);
                            _S13 = _S20;
                            if(_S20)
                            {
                                _S12 = _S11;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_1 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(-1), i32(-2))).xy;
                            if((tap_1.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_1 : vec2<f32> = sumSquares_0 + tap_1 * tap_1;
                            var count_1 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_1;
                            sumSquares_0 = sumSquares_1;
                            count_0 = count_1;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S21 : bool = (abs(i32(0))) == i32(2);
                            _S14 = _S21;
                            if(_S21)
                            {
                                _S12 = _S11;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_2 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(0), i32(-2))).xy;
                            if((tap_2.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_2 : vec2<f32> = sumSquares_0 + tap_2 * tap_2;
                            var count_2 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_2;
                            sumSquares_0 = sumSquares_2;
                            count_0 = count_2;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S22 : bool = (abs(i32(1))) == i32(2);
                            _S15 = _S22;
                            if(_S22)
                            {
                                _S12 = _S11;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_3 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(1), i32(-2))).xy;
                            if((tap_3.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_3 : vec2<f32> = sumSquares_0 + tap_3 * tap_3;
                            var count_3 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_3;
                            sumSquares_0 = sumSquares_3;
                            count_0 = count_3;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            var _S23 : bool = (abs(i32(2))) == i32(2);
                            _S16 = _S23;
                            if(_S23)
                            {
                                _S12 = _S11;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_4 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(2), i32(-2))).xy;
                            if((tap_4.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_4 : vec2<f32> = sumSquares_0 + tap_4 * tap_4;
                            var count_4 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_4;
                            sumSquares_0 = sumSquares_4;
                            count_0 = count_4;
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
                            if(_S11)
                            {
                                _S12 = _S13;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_5 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(-2), i32(-1))).xy;
                            if((tap_5.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_5 : vec2<f32> = sumSquares_0 + tap_5 * tap_5;
                            var count_5 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_5;
                            sumSquares_0 = sumSquares_5;
                            count_0 = count_5;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S13)
                            {
                                _S12 = _S13;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_6 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(-1), i32(-1))).xy;
                            if((tap_6.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_6 : vec2<f32> = sumSquares_0 + tap_6 * tap_6;
                            var count_6 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_6;
                            sumSquares_0 = sumSquares_6;
                            count_0 = count_6;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S14)
                            {
                                _S12 = _S13;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_7 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(0), i32(-1))).xy;
                            if((tap_7.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_7 : vec2<f32> = sumSquares_0 + tap_7 * tap_7;
                            var count_7 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_7;
                            sumSquares_0 = sumSquares_7;
                            count_0 = count_7;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S15)
                            {
                                _S12 = _S13;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_8 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(1), i32(-1))).xy;
                            if((tap_8.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_8 : vec2<f32> = sumSquares_0 + tap_8 * tap_8;
                            var count_8 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_8;
                            sumSquares_0 = sumSquares_8;
                            count_0 = count_8;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S16)
                            {
                                _S12 = _S13;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_9 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(2), i32(-1))).xy;
                            if((tap_9.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_9 : vec2<f32> = sumSquares_0 + tap_9 * tap_9;
                            var count_9 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_9;
                            sumSquares_0 = sumSquares_9;
                            count_0 = count_9;
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
                            if(_S11)
                            {
                                _S12 = _S14;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_10 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(-2), i32(0))).xy;
                            if((tap_10.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_10 : vec2<f32> = sumSquares_0 + tap_10 * tap_10;
                            var count_10 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_10;
                            sumSquares_0 = sumSquares_10;
                            count_0 = count_10;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S13)
                            {
                                _S12 = _S14;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_11 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(-1), i32(0))).xy;
                            if((tap_11.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_11 : vec2<f32> = sumSquares_0 + tap_11 * tap_11;
                            var count_11 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_11;
                            sumSquares_0 = sumSquares_11;
                            count_0 = count_11;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S14)
                            {
                                _S12 = _S14;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_12 : vec2<f32> = RawAt_0(px_2).xy;
                            if((tap_12.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_12 : vec2<f32> = sumSquares_0 + tap_12 * tap_12;
                            var count_12 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_12;
                            sumSquares_0 = sumSquares_12;
                            count_0 = count_12;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S15)
                            {
                                _S12 = _S14;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_13 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(1), i32(0))).xy;
                            if((tap_13.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_13 : vec2<f32> = sumSquares_0 + tap_13 * tap_13;
                            var count_13 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_13;
                            sumSquares_0 = sumSquares_13;
                            count_0 = count_13;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S16)
                            {
                                _S12 = _S14;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_14 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(2), i32(0))).xy;
                            if((tap_14.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_14 : vec2<f32> = sumSquares_0 + tap_14 * tap_14;
                            var count_14 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_14;
                            sumSquares_0 = sumSquares_14;
                            count_0 = count_14;
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
                            if(_S11)
                            {
                                _S12 = _S15;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_15 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(-2), i32(1))).xy;
                            if((tap_15.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_15 : vec2<f32> = sumSquares_0 + tap_15 * tap_15;
                            var count_15 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_15;
                            sumSquares_0 = sumSquares_15;
                            count_0 = count_15;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S13)
                            {
                                _S12 = _S15;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_16 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(-1), i32(1))).xy;
                            if((tap_16.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_16 : vec2<f32> = sumSquares_0 + tap_16 * tap_16;
                            var count_16 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_16;
                            sumSquares_0 = sumSquares_16;
                            count_0 = count_16;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S14)
                            {
                                _S12 = _S15;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_17 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(0), i32(1))).xy;
                            if((tap_17.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_17 : vec2<f32> = sumSquares_0 + tap_17 * tap_17;
                            var count_17 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_17;
                            sumSquares_0 = sumSquares_17;
                            count_0 = count_17;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S15)
                            {
                                _S12 = _S15;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_18 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(1), i32(1))).xy;
                            if((tap_18.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_18 : vec2<f32> = sumSquares_0 + tap_18 * tap_18;
                            var count_18 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_18;
                            sumSquares_0 = sumSquares_18;
                            count_0 = count_18;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S16)
                            {
                                _S12 = _S15;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_19 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(2), i32(1))).xy;
                            if((tap_19.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_19 : vec2<f32> = sumSquares_0 + tap_19 * tap_19;
                            var count_19 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_19;
                            sumSquares_0 = sumSquares_19;
                            count_0 = count_19;
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
                            if(_S11)
                            {
                                _S12 = _S16;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_20 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(-2), i32(2))).xy;
                            if((tap_20.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_20 : vec2<f32> = sumSquares_0 + tap_20 * tap_20;
                            var count_20 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_20;
                            sumSquares_0 = sumSquares_20;
                            count_0 = count_20;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S13)
                            {
                                _S12 = _S16;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_21 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(-1), i32(2))).xy;
                            if((tap_21.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_21 : vec2<f32> = sumSquares_0 + tap_21 * tap_21;
                            var count_21 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_21;
                            sumSquares_0 = sumSquares_21;
                            count_0 = count_21;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S14)
                            {
                                _S12 = _S16;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_22 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(0), i32(2))).xy;
                            if((tap_22.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_22 : vec2<f32> = sumSquares_0 + tap_22 * tap_22;
                            var count_22 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_22;
                            sumSquares_0 = sumSquares_22;
                            count_0 = count_22;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S15)
                            {
                                _S12 = _S16;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_23 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(1), i32(2))).xy;
                            if((tap_23.y) <= 0.0f)
                            {
                                break;
                            }
                            var sumSquares_23 : vec2<f32> = sumSquares_0 + tap_23 * tap_23;
                            var count_23 : f32 = count_0 + 1.0f;
                            sum_0 = sum_0 + tap_23;
                            sumSquares_0 = sumSquares_23;
                            count_0 = count_23;
                            break;
                        }
                        break;
                    }
                    for(;;)
                    {
                        for(;;)
                        {
                            if(_S16)
                            {
                                _S12 = _S16;
                            }
                            else
                            {
                                _S12 = false;
                            }
                            if(_S12)
                            {
                                break;
                            }
                            var tap_24 : vec2<f32> = RawAt_0(px_2 + vec2<i32>(i32(2), i32(2))).xy;
                            if((tap_24.y) <= 0.0f)
                            {
                                break;
                            }
                            var sum_1 : vec2<f32> = sum_0 + tap_24;
                            var sumSquares_24 : vec2<f32> = sumSquares_0 + tap_24 * tap_24;
                            count_0 = count_0 + 1.0f;
                            sum_0 = sum_1;
                            sumSquares_0 = sumSquares_24;
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
    var _S24 : f32 = max(count_0, 1.0f);
    var _S25 : vec2<f32> = vec2<f32>(_S24);
    var mean_0 : vec2<f32> = sum_0 / _S25;
    var _S26 : vec2<f32> = vec2<f32>(1.25f) * sqrt(max(sumSquares_0 / _S25 - mean_0 * mean_0, _S17));
    (*low_0) = mean_0 - _S26;
    (*high_0) = mean_0 + _S26;
    (*low_0) = min((*low_0), centre_0);
    (*high_0) = max((*high_0), centre_0);
    var floorHalfWidth_0 : f32 = 3.0f / (2.0f * _S24);
    var _S27 : f32 = mean_0.x;
    (*low_0)[i32(0)] = min((*low_0).x, _S27 - floorHalfWidth_0);
    (*high_0)[i32(0)] = max((*high_0).x, _S27 + floorHalfWidth_0);
    return;
}

fn MotionAt_0( uv_3 : vec2<f32>) -> vec2<f32>
{
    var width_1 : u32;
    var height_1 : u32;
    {var dim = textureDimensions((uVelocity_0));((width_1)) = dim.x;((height_1)) = dim.y;};
    var _S28 : vec3<i32> = vec3<i32>(clamp(vec2<i32>(floor(uv_3 * vec2<f32>(f32(width_1), f32(height_1)))), vec2<i32>(i32(0), i32(0)), vec2<i32>(i32(width_1) - i32(1), i32(height_1) - i32(1))), i32(0));
    return (textureLoad((uVelocity_0), ((_S28)).xy, ((_S28)).z)).xy;
}

fn HistoryAt_0( px_3 : vec2<i32>) -> vec4<f32>
{
    var _S29 : vec3<i32> = vec3<i32>(clamp(px_3, vec2<i32>(i32(0), i32(0)), vec2<i32>(p_0.resolution_0.xy) - vec2<i32>(i32(1))), i32(0));
    return (textureLoad((uHistory_0), ((_S29)).xy, ((_S29)).z));
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S30 : vec2<i32> = vec2<i32>(id_0.xy);
    var ownMotion_0 : bool;
    if((_S30.x) >= i32(p_0.resolution_0.x))
    {
        ownMotion_0 = true;
    }
    else
    {
        ownMotion_0 = (_S30.y) >= i32(p_0.resolution_0.y);
    }
    if(ownMotion_0)
    {
        return;
    }
    var depthUv_0 : vec2<f32> = JitteredDepthUv_0(_S30);
    var depth_4 : f32 = DepthBilinear_0(uDepth_0, uDepthSampler_0, depthUv_0);
    if(depth_4 <= 0.0f)
    {
        textureStore((uOutput_0), (vec2<u32>(_S30)), (vec4<f32>(1.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var viewPos_0 : vec3<f32> = ViewPositionAt_0(depthUv_0, depth_4, p_0.unproject_0);
    var current_0 : vec2<f32> = RawAt_0(_S30).xy;
    if((current_0.y) <= 0.0f)
    {
        textureStore((uOutput_0), (vec2<u32>(_S30)), (vec4<f32>(1.0f, 0.0f, 1.0f, viewPos_0.z)));
        return;
    }
    var low_1 : vec2<f32>;
    var high_1 : vec2<f32>;
    NeighbourhoodRange_0(_S30, current_0, &(low_1), &(high_1));
    var _S31 : vec2<f32> = vec2<f32>(0.5f);
    var extent_0 : vec2<f32> = (high_1 - low_1) * _S31;
    if((p_0.cameraDelta_0.w) > 0.5f)
    {
        ownMotion_0 = (p_0.control_0.x) > 0.5f;
    }
    else
    {
        ownMotion_0 = false;
    }
    var result_0 : vec2<f32>;
    var frames_0 : f32;
    if(ownMotion_0)
    {
        var prevView_0 : vec3<f32> = (((vec4<f32>((((vec4<f32>(viewPos_0, 0.0f)) * (mat4x4<f32>(p_0.viewToWorld_0.data_0[i32(0)][i32(0)], p_0.viewToWorld_0.data_0[i32(1)][i32(0)], p_0.viewToWorld_0.data_0[i32(2)][i32(0)], p_0.viewToWorld_0.data_0[i32(3)][i32(0)], p_0.viewToWorld_0.data_0[i32(0)][i32(1)], p_0.viewToWorld_0.data_0[i32(1)][i32(1)], p_0.viewToWorld_0.data_0[i32(2)][i32(1)], p_0.viewToWorld_0.data_0[i32(3)][i32(1)], p_0.viewToWorld_0.data_0[i32(0)][i32(2)], p_0.viewToWorld_0.data_0[i32(1)][i32(2)], p_0.viewToWorld_0.data_0[i32(2)][i32(2)], p_0.viewToWorld_0.data_0[i32(3)][i32(2)], p_0.viewToWorld_0.data_0[i32(0)][i32(3)], p_0.viewToWorld_0.data_0[i32(1)][i32(3)], p_0.viewToWorld_0.data_0[i32(2)][i32(3)], p_0.viewToWorld_0.data_0[i32(3)][i32(3)])))).xyz + p_0.cameraDelta_0.xyz, 0.0f)) * (mat4x4<f32>(p_0.prevWorldToView_0.data_0[i32(0)][i32(0)], p_0.prevWorldToView_0.data_0[i32(1)][i32(0)], p_0.prevWorldToView_0.data_0[i32(2)][i32(0)], p_0.prevWorldToView_0.data_0[i32(3)][i32(0)], p_0.prevWorldToView_0.data_0[i32(0)][i32(1)], p_0.prevWorldToView_0.data_0[i32(1)][i32(1)], p_0.prevWorldToView_0.data_0[i32(2)][i32(1)], p_0.prevWorldToView_0.data_0[i32(3)][i32(1)], p_0.prevWorldToView_0.data_0[i32(0)][i32(2)], p_0.prevWorldToView_0.data_0[i32(1)][i32(2)], p_0.prevWorldToView_0.data_0[i32(2)][i32(2)], p_0.prevWorldToView_0.data_0[i32(3)][i32(2)], p_0.prevWorldToView_0.data_0[i32(0)][i32(3)], p_0.prevWorldToView_0.data_0[i32(1)][i32(3)], p_0.prevWorldToView_0.data_0[i32(2)][i32(3)], p_0.prevWorldToView_0.data_0[i32(3)][i32(3)])))).xyz;
        var _S32 : f32 = prevView_0.z;
        if(_S32 > (p_0.prevUnproject_0.z))
        {
            var prevUv_0 : vec2<f32> = vec2<f32>(prevView_0.x * p_0.prevUnproject_0.x / _S32, - (prevView_0.y * p_0.prevUnproject_0.y / _S32)) * _S31 + _S31;
            var prevUv_1 : vec2<f32>;
            if((p_0.control_0.y) > 0.5f)
            {
                var motion_0 : vec2<f32> = MotionAt_0(depthUv_0);
                var _S33 : bool = (length((motion_0 - (depthUv_0 - prevUv_0)) * p_0.resolution_0.xy)) > 1.5f;
                if(_S33)
                {
                    prevUv_1 = depthUv_0 - motion_0;
                }
                else
                {
                    prevUv_1 = prevUv_0;
                }
                ownMotion_0 = _S33;
            }
            else
            {
                prevUv_1 = prevUv_0;
                ownMotion_0 = false;
            }
            var historyUv_0 : vec2<f32> = prevUv_1 + p_0.jitter_0.zw * p_0.resolution_0.zw;
            var _S34 : bool;
            if((all((historyUv_0 >= vec2<f32>(0.0f)))))
            {
                _S34 = (all((historyUv_0 <= vec2<f32>(1.0f))));
            }
            else
            {
                _S34 = false;
            }
            if(_S34)
            {
                var position_1 : vec2<f32> = historyUv_0 * p_0.resolution_0.xy - _S31;
                var floored_1 : vec2<f32> = floor(position_1);
                var _S35 : vec2<f32> = position_1 - floored_1;
                var _S36 : vec2<i32> = vec2<i32>(floored_1);
                var _S37 : f32 = 0.02999999932944775f * max(_S32, 1.0f);
                var _S38 : vec2<f32> = vec2<f32>(1.0f) - _S35;
                var bilinear_0 : vec2<f32> = mix(_S38, _S35, vec2<f32>(vec2<i32>(i32(0), i32(0))));
                var _S39 : f32 = bilinear_0.x * bilinear_0.y;
                var texel_0 : vec4<f32> = HistoryAt_0(_S36);
                var _S40 : bool = !ownMotion_0;
                if(_S40)
                {
                    ownMotion_0 = (abs(texel_0.w - _S32)) > _S37;
                }
                else
                {
                    ownMotion_0 = false;
                }
                var weight_1 : f32;
                if(ownMotion_0)
                {
                    weight_1 = 0.0f;
                }
                else
                {
                    weight_1 = _S39;
                }
                if((texel_0.y) <= 0.00009999999747379f)
                {
                    ownMotion_0 = true;
                }
                else
                {
                    ownMotion_0 = (texel_0.z) < 0.5f;
                }
                if(ownMotion_0)
                {
                    weight_1 = 0.0f;
                }
                var _S41 : vec3<f32> = texel_0.xyz * vec3<f32>(weight_1);
                const corner_0 : vec2<i32> = vec2<i32>(i32(1), i32(0));
                var bilinear_1 : vec2<f32> = mix(_S38, _S35, vec2<f32>(corner_0));
                var _S42 : f32 = bilinear_1.x * bilinear_1.y;
                var texel_1 : vec4<f32> = HistoryAt_0(_S36 + corner_0);
                if(_S40)
                {
                    ownMotion_0 = (abs(texel_1.w - _S32)) > _S37;
                }
                else
                {
                    ownMotion_0 = false;
                }
                var weight_2 : f32;
                if(ownMotion_0)
                {
                    weight_2 = 0.0f;
                }
                else
                {
                    weight_2 = _S42;
                }
                if((texel_1.y) <= 0.00009999999747379f)
                {
                    ownMotion_0 = true;
                }
                else
                {
                    ownMotion_0 = (texel_1.z) < 0.5f;
                }
                if(ownMotion_0)
                {
                    weight_2 = 0.0f;
                }
                var weighted_0 : vec3<f32> = _S41 + texel_1.xyz * vec3<f32>(weight_2);
                var weightSum_0 : f32 = weight_1 + weight_2;
                const corner_1 : vec2<i32> = vec2<i32>(i32(0), i32(1));
                var bilinear_2 : vec2<f32> = mix(_S38, _S35, vec2<f32>(corner_1));
                var _S43 : f32 = bilinear_2.x * bilinear_2.y;
                var texel_2 : vec4<f32> = HistoryAt_0(_S36 + corner_1);
                if(_S40)
                {
                    ownMotion_0 = (abs(texel_2.w - _S32)) > _S37;
                }
                else
                {
                    ownMotion_0 = false;
                }
                if(ownMotion_0)
                {
                    weight_1 = 0.0f;
                }
                else
                {
                    weight_1 = _S43;
                }
                if((texel_2.y) <= 0.00009999999747379f)
                {
                    ownMotion_0 = true;
                }
                else
                {
                    ownMotion_0 = (texel_2.z) < 0.5f;
                }
                if(ownMotion_0)
                {
                    weight_1 = 0.0f;
                }
                var weighted_1 : vec3<f32> = weighted_0 + texel_2.xyz * vec3<f32>(weight_1);
                var weightSum_1 : f32 = weightSum_0 + weight_1;
                const corner_2 : vec2<i32> = vec2<i32>(i32(1), i32(1));
                var bilinear_3 : vec2<f32> = mix(_S38, _S35, vec2<f32>(corner_2));
                var _S44 : f32 = bilinear_3.x * bilinear_3.y;
                var texel_3 : vec4<f32> = HistoryAt_0(_S36 + corner_2);
                if(_S40)
                {
                    ownMotion_0 = (abs(texel_3.w - _S32)) > _S37;
                }
                else
                {
                    ownMotion_0 = false;
                }
                if(ownMotion_0)
                {
                    weight_1 = 0.0f;
                }
                else
                {
                    weight_1 = _S44;
                }
                if((texel_3.y) <= 0.00009999999747379f)
                {
                    ownMotion_0 = true;
                }
                else
                {
                    ownMotion_0 = (texel_3.z) < 0.5f;
                }
                if(ownMotion_0)
                {
                    weight_1 = 0.0f;
                }
                var weighted_2 : vec3<f32> = weighted_1 + texel_3.xyz * vec3<f32>(weight_1);
                var weightSum_2 : f32 = weightSum_1 + weight_1;
                if(weightSum_2 > 0.00100000004749745f)
                {
                    var history_0 : vec3<f32> = weighted_2 / vec3<f32>(weightSum_2);
                    var historyVis_0 : f32 = history_0.x;
                    var historyShare_0 : f32 = history_0.y;
                    var historyFrames_0 : f32 = history_0.z;
                    if(historyFrames_0 > 0.0f)
                    {
                        var clipped_0 : f32 = clamp(historyVis_0, low_1.x, high_1.x);
                        var confidence_0 : f32 = saturate(1.0f - abs(clipped_0 - historyVis_0) / max(extent_0.x, 0.00999999977648258f));
                        var softHistory_0 : vec2<f32> = vec2<f32>(mix(clipped_0, historyVis_0, confidence_0), historyShare_0);
                        if((p_0.control_0.z) > 0.5f)
                        {
                            frames_0 = 4.0f;
                        }
                        else
                        {
                            frames_0 = mix(4.0f, 32.0f, confidence_0);
                        }
                        var _S45 : f32 = min(historyFrames_0 + 1.0f, frames_0);
                        result_0 = mix(softHistory_0, current_0, vec2<f32>((1.0f / _S45)));
                        frames_0 = _S45;
                    }
                    else
                    {
                        result_0 = current_0;
                        frames_0 = 1.0f;
                    }
                }
                else
                {
                    result_0 = current_0;
                    frames_0 = 1.0f;
                }
            }
            else
            {
                result_0 = current_0;
                frames_0 = 1.0f;
            }
        }
        else
        {
            result_0 = current_0;
            frames_0 = 1.0f;
        }
    }
    else
    {
        result_0 = current_0;
        frames_0 = 1.0f;
    }
    textureStore((uOutput_0), (vec2<u32>(_S30)), (vec4<f32>(saturate(result_0.x), saturate(result_0.y), frames_0, viewPos_0.z)));
    return;
}

