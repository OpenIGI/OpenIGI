struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

@binding(5) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uInput_0 : texture_2d<f32>;

@binding(4) @group(0) var uOffset_0 : texture_storage_2d<rgba32float, write>;

@binding(1) @group(0) var uInputSampler_0 : sampler;

fn LumaOf_0( c_0 : vec4<f32>) -> f32
{
    var l_0 : f32 = dot(max(c_0.xyz, vec3<f32>(0.0f)) * vec3<f32>(p_0.control_0.x), vec3<f32>(0.29899999499320984f, 0.58700001239776611f, 0.11400000005960464f));
    return l_0 / (0.5f + l_0);
}

fn LumaTexel_0( at_0 : vec2<i32>,  size_1 : vec2<i32>) -> f32
{
    var _S1 : vec3<i32> = vec3<i32>(clamp(at_0, vec2<i32>(i32(0), i32(0)), size_1 - vec2<i32>(i32(1))), i32(0));
    return LumaOf_0((textureLoad((uInput_0), ((_S1)).xy, ((_S1)).z)));
}

fn LumaAt_0( uv_0 : vec2<f32>) -> f32
{
    var halfTexel_0 : vec2<f32> = vec2<f32>(0.5f) * p_0.size_0.zw;
    return LumaOf_0((textureSampleLevel((uInput_0), (uInputSampler_0), (clamp(uv_0, halfTexel_0, vec2<f32>(1.0f) - halfTexel_0)), (0.0f))));
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S2 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S3 : vec2<i32> = vec2<i32>(p_0.size_0.xy);
    var doneP_0 : bool;
    if((_S2.x) >= (_S3.x))
    {
        doneP_0 = true;
    }
    else
    {
        doneP_0 = (_S2.y) >= (_S3.y);
    }
    if(doneP_0)
    {
        return;
    }
    var C_0 : f32 = LumaTexel_0(_S2, _S3);
    const _S4 : vec2<i32> = vec2<i32>(i32(0), i32(-1));
    var N_0 : f32 = LumaTexel_0(_S2 + _S4, _S3);
    const _S5 : vec2<i32> = vec2<i32>(i32(0), i32(1));
    var S_0 : f32 = LumaTexel_0(_S2 + _S5, _S3);
    const _S6 : vec2<i32> = vec2<i32>(i32(1), i32(0));
    var E_0 : f32 = LumaTexel_0(_S2 + _S6, _S3);
    const _S7 : vec2<i32> = vec2<i32>(i32(-1), i32(0));
    var W_0 : f32 = LumaTexel_0(_S2 + _S7, _S3);
    var NE_0 : f32 = LumaTexel_0(_S2 + vec2<i32>(i32(1), i32(-1)), _S3);
    var NW_0 : f32 = LumaTexel_0(_S2 + vec2<i32>(i32(-1), i32(-1)), _S3);
    var SE_0 : f32 = LumaTexel_0(_S2 + vec2<i32>(i32(1), i32(1)), _S3);
    var SW_0 : f32 = LumaTexel_0(_S2 + vec2<i32>(i32(-1), i32(1)), _S3);
    if((max(max(max(C_0, N_0), max(S_0, E_0)), max(max(W_0, NE_0), max(NW_0, max(SE_0, SW_0)))) - min(min(min(C_0, N_0), min(S_0, E_0)), min(min(W_0, NE_0), min(NW_0, min(SE_0, SW_0))))) <= 0.05000000074505806f)
    {
        textureStore((uOffset_0), (vec2<u32>(_S2)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var edgeL_0 : f32;
    var side_0 : vec2<i32>;
    var walkDir_0 : vec2<i32>;
    if((abs((NE_0 + NW_0) * 0.5f - N_0) + abs((E_0 + W_0) * 0.5f - C_0) + abs((SE_0 + SW_0) * 0.5f - S_0)) > (abs((NE_0 + SE_0) * 0.5f - E_0) + abs((N_0 + S_0) * 0.5f - C_0) + abs((NW_0 + SW_0) * 0.5f - W_0)))
    {
        if((abs(W_0 - C_0)) > (abs(E_0 - C_0)))
        {
            edgeL_0 = W_0;
            side_0 = _S7;
        }
        else
        {
            edgeL_0 = E_0;
            side_0 = _S6;
        }
        walkDir_0 = _S5;
    }
    else
    {
        if((abs(N_0 - C_0)) > (abs(S_0 - C_0)))
        {
            edgeL_0 = N_0;
            side_0 = _S4;
        }
        else
        {
            edgeL_0 = S_0;
            side_0 = _S5;
        }
        walkDir_0 = _S6;
    }
    var _S8 : f32 = abs(edgeL_0 - C_0);
    if((_S8 * 0.5f) <= 0.05000000074505806f)
    {
        textureStore((uOffset_0), (vec2<u32>(_S2)), (vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f)));
        return;
    }
    var _S9 : vec2<f32> = vec2<f32>(0.5f);
    var _S10 : vec2<f32> = (vec2<f32>(_S2) + _S9 + vec2<f32>(side_0) * _S9) * p_0.size_0.zw;
    var _S11 : vec2<f32> = vec2<f32>(walkDir_0) * p_0.size_0.zw;
    var _S12 : f32 = (edgeL_0 + C_0) * 0.5f;
    var _S13 : f32 = _S8 * 0.25f;
    var brighter_0 : bool = edgeL_0 > C_0;
    var l_1 : f32 = LumaAt_0(_S10 + _S11);
    var _S14 : f32 = _S12 - _S13;
    var highP_0 : bool;
    var lowP_0 : bool;
    var lenP_0 : f32;
    if(l_1 < _S14)
    {
        doneP_0 = true;
        highP_0 = false;
        lenP_0 = 0.0f;
        lowP_0 = true;
    }
    else
    {
        var _S15 : bool = l_1 > (_S12 + _S13);
        if(_S15)
        {
            lenP_0 = 0.0f;
        }
        else
        {
            lenP_0 = 8.0f;
        }
        doneP_0 = _S15;
        highP_0 = _S15;
        lowP_0 = false;
    }
    var l_2 : f32 = LumaAt_0(_S10 - _S11);
    var doneN_0 : bool;
    var highN_0 : bool;
    var lowN_0 : bool;
    var lenN_0 : f32;
    if(l_2 < _S14)
    {
        doneN_0 = true;
        highN_0 = false;
        lenN_0 = 0.0f;
        lowN_0 = true;
    }
    else
    {
        var _S16 : bool = l_2 > (_S12 + _S13);
        if(_S16)
        {
            lenN_0 = 0.0f;
        }
        else
        {
            lenN_0 = 8.0f;
        }
        doneN_0 = _S16;
        highN_0 = _S16;
        lowN_0 = false;
    }
    if(!doneP_0)
    {
        var l_3 : f32 = LumaAt_0(_S10 + vec2<f32>(2.0f) * _S11);
        if(l_3 < _S14)
        {
            doneP_0 = true;
            lowP_0 = true;
            lenP_0 = 1.0f;
        }
        else
        {
            if(l_3 > (_S12 + _S13))
            {
                doneP_0 = true;
                highP_0 = true;
                lenP_0 = 1.0f;
            }
        }
    }
    if(!doneN_0)
    {
        var l_4 : f32 = LumaAt_0(_S10 - vec2<f32>(2.0f) * _S11);
        if(l_4 < _S14)
        {
            doneN_0 = true;
            lowN_0 = true;
            lenN_0 = 1.0f;
        }
        else
        {
            if(l_4 > (_S12 + _S13))
            {
                doneN_0 = true;
                highN_0 = true;
                lenN_0 = 1.0f;
            }
        }
    }
    if(!doneP_0)
    {
        var l_5 : f32 = LumaAt_0(_S10 + vec2<f32>(3.0f) * _S11);
        if(l_5 < _S14)
        {
            doneP_0 = true;
            lowP_0 = true;
            lenP_0 = 2.0f;
        }
        else
        {
            if(l_5 > (_S12 + _S13))
            {
                doneP_0 = true;
                highP_0 = true;
                lenP_0 = 2.0f;
            }
        }
    }
    if(!doneN_0)
    {
        var l_6 : f32 = LumaAt_0(_S10 - vec2<f32>(3.0f) * _S11);
        if(l_6 < _S14)
        {
            doneN_0 = true;
            lowN_0 = true;
            lenN_0 = 2.0f;
        }
        else
        {
            if(l_6 > (_S12 + _S13))
            {
                doneN_0 = true;
                highN_0 = true;
                lenN_0 = 2.0f;
            }
        }
    }
    if(!doneP_0)
    {
        var l_7 : f32 = LumaAt_0(_S10 + vec2<f32>(4.0f) * _S11);
        if(l_7 < _S14)
        {
            doneP_0 = true;
            lowP_0 = true;
            lenP_0 = 3.0f;
        }
        else
        {
            if(l_7 > (_S12 + _S13))
            {
                doneP_0 = true;
                highP_0 = true;
                lenP_0 = 3.0f;
            }
        }
    }
    if(!doneN_0)
    {
        var l_8 : f32 = LumaAt_0(_S10 - vec2<f32>(4.0f) * _S11);
        if(l_8 < _S14)
        {
            doneN_0 = true;
            lowN_0 = true;
            lenN_0 = 3.0f;
        }
        else
        {
            if(l_8 > (_S12 + _S13))
            {
                doneN_0 = true;
                highN_0 = true;
                lenN_0 = 3.0f;
            }
        }
    }
    if(!doneP_0)
    {
        var l_9 : f32 = LumaAt_0(_S10 + vec2<f32>(5.0f) * _S11);
        if(l_9 < _S14)
        {
            doneP_0 = true;
            lowP_0 = true;
            lenP_0 = 4.0f;
        }
        else
        {
            if(l_9 > (_S12 + _S13))
            {
                doneP_0 = true;
                highP_0 = true;
                lenP_0 = 4.0f;
            }
        }
    }
    if(!doneN_0)
    {
        var l_10 : f32 = LumaAt_0(_S10 - vec2<f32>(5.0f) * _S11);
        if(l_10 < _S14)
        {
            doneN_0 = true;
            lowN_0 = true;
            lenN_0 = 4.0f;
        }
        else
        {
            if(l_10 > (_S12 + _S13))
            {
                doneN_0 = true;
                highN_0 = true;
                lenN_0 = 4.0f;
            }
        }
    }
    if(!doneP_0)
    {
        var l_11 : f32 = LumaAt_0(_S10 + vec2<f32>(6.0f) * _S11);
        if(l_11 < _S14)
        {
            doneP_0 = true;
            lowP_0 = true;
            lenP_0 = 5.0f;
        }
        else
        {
            if(l_11 > (_S12 + _S13))
            {
                doneP_0 = true;
                highP_0 = true;
                lenP_0 = 5.0f;
            }
        }
    }
    if(!doneN_0)
    {
        var l_12 : f32 = LumaAt_0(_S10 - vec2<f32>(6.0f) * _S11);
        if(l_12 < _S14)
        {
            doneN_0 = true;
            lowN_0 = true;
            lenN_0 = 5.0f;
        }
        else
        {
            if(l_12 > (_S12 + _S13))
            {
                doneN_0 = true;
                highN_0 = true;
                lenN_0 = 5.0f;
            }
        }
    }
    if(!doneP_0)
    {
        var l_13 : f32 = LumaAt_0(_S10 + vec2<f32>(7.0f) * _S11);
        if(l_13 < _S14)
        {
            doneP_0 = true;
            lowP_0 = true;
            lenP_0 = 6.0f;
        }
        else
        {
            if(l_13 > (_S12 + _S13))
            {
                doneP_0 = true;
                highP_0 = true;
                lenP_0 = 6.0f;
            }
        }
    }
    if(!doneN_0)
    {
        var l_14 : f32 = LumaAt_0(_S10 - vec2<f32>(7.0f) * _S11);
        if(l_14 < _S14)
        {
            doneN_0 = true;
            lowN_0 = true;
            lenN_0 = 6.0f;
        }
        else
        {
            if(l_14 > (_S12 + _S13))
            {
                doneN_0 = true;
                highN_0 = true;
                lenN_0 = 6.0f;
            }
        }
    }
    if(!doneP_0)
    {
        var l_15 : f32 = LumaAt_0(_S10 + vec2<f32>(8.0f) * _S11);
        if(l_15 < _S14)
        {
            doneP_0 = true;
            lowP_0 = true;
            lenP_0 = 7.0f;
        }
        else
        {
            if(l_15 > (_S12 + _S13))
            {
                doneP_0 = true;
                highP_0 = true;
                lenP_0 = 7.0f;
            }
        }
    }
    if(!doneN_0)
    {
        var l_16 : f32 = LumaAt_0(_S10 - vec2<f32>(8.0f) * _S11);
        if(l_16 < _S14)
        {
            doneN_0 = true;
            lowN_0 = true;
            lenN_0 = 7.0f;
        }
        else
        {
            if(l_16 > (_S12 + _S13))
            {
                doneN_0 = true;
                highN_0 = true;
                lenN_0 = 7.0f;
            }
        }
    }
    var incP_0 : bool;
    if(brighter_0)
    {
        incP_0 = highP_0;
    }
    else
    {
        incP_0 = lowP_0;
    }
    var decP_0 : bool;
    if(brighter_0)
    {
        decP_0 = lowP_0;
    }
    else
    {
        decP_0 = highP_0;
    }
    var incN_0 : bool;
    if(brighter_0)
    {
        incN_0 = highN_0;
    }
    else
    {
        incN_0 = lowN_0;
    }
    var decN_0 : bool;
    if(brighter_0)
    {
        decN_0 = lowN_0;
    }
    else
    {
        decN_0 = highN_0;
    }
    var inv_0 : f32 = 1.0f / (1.0f + lenP_0 + lenN_0);
    if(!doneP_0)
    {
        doneP_0 = !doneN_0;
    }
    else
    {
        doneP_0 = false;
    }
    var dist_0 : f32;
    if(doneP_0)
    {
        dist_0 = 0.0f;
    }
    else
    {
        if(incP_0)
        {
            doneP_0 = incN_0;
        }
        else
        {
            doneP_0 = false;
        }
        if(doneP_0)
        {
            dist_0 = 0.5f;
        }
        else
        {
            if(decP_0)
            {
                doneP_0 = decN_0;
            }
            else
            {
                doneP_0 = false;
            }
            if(doneP_0)
            {
                dist_0 = 0.0f;
            }
            else
            {
                if(incN_0)
                {
                    dist_0 = 0.5f - (lenN_0 + 0.5f) * inv_0;
                }
                else
                {
                    if(incP_0)
                    {
                        dist_0 = 0.5f - (lenP_0 + 0.5f) * inv_0;
                    }
                    else
                    {
                        dist_0 = 0.0f;
                    }
                }
            }
        }
    }
    textureStore((uOffset_0), (vec2<u32>(_S2)), (vec4<f32>(f32(side_0.x) * dist_0, f32(side_0.y) * dist_0, 0.0f, 0.0f)));
    return;
}

