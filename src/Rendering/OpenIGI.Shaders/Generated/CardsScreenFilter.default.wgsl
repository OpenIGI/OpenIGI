struct Globals_std140_0
{
    @align(16) uWidth_0 : i32,
    @align(4) uHeight_0 : i32,
    @align(8) uRadius_0 : i32,
    @align(4) uAccum_0 : i32,
    @align(16) uPosition_0 : i32,
    @align(4) uSceneNormal_0 : i32,
};

@binding(49) @group(0) var<uniform> g_0 : Globals_std140_0;
@binding(33) @group(0) var uPosition_1 : texture_2d<f32>;

@binding(32) @group(0) var uAccum_1 : texture_2d<f32>;

@binding(0) @group(0) var uFiltered_0 : texture_storage_2d<rgba16float, write>;

@binding(34) @group(0) var uSceneNormal_1 : texture_2d<f32>;

@binding(66) @group(0) var uSceneNormalSampler_0 : sampler;

fn ComputeSample_0( texture_0 : texture_2d<f32>,  sampler_0 : sampler,  uv_0 : vec2<f32>) -> vec4<f32>
{
    return (textureSampleLevel((texture_0), (sampler_0), (uv_0), (0.0f)));
}

fn ProbeNormal_0( probe_0 : vec2<i32>) -> vec3<f32>
{
    var n_0 : vec3<f32> = ComputeSample_0(uSceneNormal_1, uSceneNormalSampler_0, (vec2<f32>(probe_0) + vec2<f32>(0.5f)) / vec2<f32>(f32(g_0.uWidth_0), f32(g_0.uHeight_0))).xyz * vec3<f32>(2.0f) - vec3<f32>(1.0f);
    var len_0 : f32 = length(n_0);
    var _S1 : vec3<f32>;
    if(len_0 > 0.00100000004749745f)
    {
        _S1 = n_0 / vec3<f32>(len_0);
    }
    else
    {
        _S1 = vec3<f32>(0.0f, 0.0f, 0.0f);
    }
    return _S1;
}

fn FacingWeight_0( cosine_0 : f32) -> f32
{
    var _S2 : f32 = max(cosine_0, 0.0f);
    var c2_0 : f32 = _S2 * _S2;
    return c2_0 * c2_0;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var sum_0 : vec3<f32>;
    var _S3 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S4 : bool;
    if((_S3.x) >= (g_0.uWidth_0))
    {
        _S4 = true;
    }
    else
    {
        _S4 = (_S3.y) >= (g_0.uHeight_0);
    }
    if(_S4)
    {
        return;
    }
    var _S5 : vec3<i32> = vec3<i32>(_S3, i32(0));
    var centre_0 : vec4<f32> = (textureLoad((uPosition_1), ((_S5)).xy, ((_S5)).z));
    var accum_0 : vec4<f32> = (textureLoad((uAccum_1), ((_S5)).xy, ((_S5)).z));
    if((centre_0.w) <= 0.0f)
    {
        textureStore((uFiltered_0), (vec2<u32>(_S3)), (accum_0));
        return;
    }
    var _S6 : vec3<f32> = centre_0.xyz;
    var _S7 : vec3<f32> = ProbeNormal_0(_S3);
    var _S8 : array<vec2<i32>, i32(4)> = array<vec2<i32>, i32(4)>( vec2<i32>(i32(1), i32(0)), vec2<i32>(i32(-1), i32(0)), vec2<i32>(i32(0), i32(1)), vec2<i32>(i32(0), i32(-1)) );
    var i_0 : i32 = i32(0);
    var spacing_0 : f32 = 0.0f;
    var spacingCount_0 : f32 = 0.0f;
    for(;;)
    {
        if(i_0 < i32(4))
        {
        }
        else
        {
            break;
        }
        var _S9 : vec3<i32> = vec3<i32>(clamp(_S3 + _S8[i_0], vec2<i32>(i32(0), i32(0)), vec2<i32>(g_0.uWidth_0 - i32(1), g_0.uHeight_0 - i32(1))), i32(0));
        var np_0 : vec4<f32> = (textureLoad((uPosition_1), ((_S9)).xy, ((_S9)).z));
        if((np_0.w) > 0.0f)
        {
            var spacingCount_1 : f32 = spacingCount_0 + 1.0f;
            spacing_0 = spacing_0 + length(np_0.xyz - _S6);
            spacingCount_0 = spacingCount_1;
        }
        i_0 = i_0 + i32(1);
    }
    if(spacingCount_0 > 0.0f)
    {
        spacing_0 = spacing_0 / spacingCount_0;
    }
    else
    {
        spacing_0 = 1.0f;
    }
    var _S10 : f32 = 2.0f * spacing_0 * spacing_0 + 9.99999997475242708e-07f;
    const _S11 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var y_0 : i32 = - g_0.uRadius_0;
    var sum_1 : vec3<f32> = _S11;
    var weight_0 : f32 = 0.0f;
    for(;;)
    {
        if(y_0 <= (g_0.uRadius_0))
        {
        }
        else
        {
            break;
        }
        var x_0 : i32 = - g_0.uRadius_0;
        sum_0 = sum_1;
        var weight_1 : f32 = weight_0;
        for(;;)
        {
            if(x_0 <= (g_0.uRadius_0))
            {
            }
            else
            {
                break;
            }
            var n_1 : vec2<i32> = clamp(_S3 + vec2<i32>(x_0, y_0), vec2<i32>(i32(0), i32(0)), vec2<i32>(g_0.uWidth_0 - i32(1), g_0.uHeight_0 - i32(1)));
            var _S12 : vec3<i32> = vec3<i32>(n_1, i32(0));
            var np_1 : vec4<f32> = (textureLoad((uPosition_1), ((_S12)).xy, ((_S12)).z));
            if((np_1.w) <= 0.0f)
            {
                x_0 = x_0 + i32(1);
                continue;
            }
            var na_0 : vec4<f32> = (textureLoad((uAccum_1), ((_S12)).xy, ((_S12)).z));
            var delta_0 : vec3<f32> = np_1.xyz - _S6;
            var nn_0 : vec3<f32> = ProbeNormal_0(n_1);
            if((dot(_S7, _S7)) < 0.5f)
            {
                _S4 = true;
            }
            else
            {
                _S4 = (dot(nn_0, nn_0)) < 0.5f;
            }
            var facing_0 : f32;
            if(_S4)
            {
                facing_0 = 1.0f;
            }
            else
            {
                facing_0 = FacingWeight_0(dot(_S7, nn_0));
            }
            var w_0 : f32 = exp(- dot(delta_0, delta_0) / _S10) * (na_0.w + 1.0f) * facing_0;
            var weight_2 : f32 = weight_1 + w_0;
            sum_0 = sum_0 + na_0.xyz * vec3<f32>(w_0);
            weight_1 = weight_2;
            x_0 = x_0 + i32(1);
        }
        y_0 = y_0 + i32(1);
        sum_1 = sum_0;
        weight_0 = weight_1;
    }
    if(weight_0 > 0.0f)
    {
        sum_0 = sum_1 / vec3<f32>(weight_0);
    }
    else
    {
        sum_0 = accum_0.xyz;
    }
    textureStore((uFiltered_0), (vec2<u32>(_S3)), (vec4<f32>(sum_0, accum_0.w)));
    return;
}

