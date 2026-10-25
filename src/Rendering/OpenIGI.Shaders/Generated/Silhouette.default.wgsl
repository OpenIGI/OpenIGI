struct SilhouetteParams_std140_0
{
    @align(16) frame_0 : vec4<f32>,
    @align(16) vignettes_0 : vec4<f32>,
    @align(16) fill0_0 : vec4<f32>,
    @align(16) outline0_0 : vec4<f32>,
    @align(16) fill1_0 : vec4<f32>,
    @align(16) outline1_0 : vec4<f32>,
    @align(16) fill2_0 : vec4<f32>,
    @align(16) outline2_0 : vec4<f32>,
};

@binding(0) @group(3) var<uniform> p_0 : SilhouetteParams_std140_0;
@binding(0) @group(0) var uMask_0 : texture_2d<f32>;

@id(64) override cClipYDown_0 : i32 = i32(1);

fn OpenIgiClipY_0( y_0 : f32) -> f32
{
    var _S1 : f32;
    if(cClipYDown_0 != i32(0))
    {
        _S1 = - y_0;
    }
    else
    {
        _S1 = y_0;
    }
    return _S1;
}

struct FullscreenVaryings_0
{
    @builtin(position) position_0 : vec4<f32>,
    @location(0) uv_0 : vec2<f32>,
};

fn FullscreenTriangle_0( vertexIndex_0 : u32) -> FullscreenVaryings_0
{
    var output_0 : FullscreenVaryings_0;
    var _S2 : f32 = f32((((vertexIndex_0 << (u32(1)))) & (u32(2))));
    var _S3 : f32 = f32((vertexIndex_0 & (u32(2))));
    output_0.uv_0 = vec2<f32>(_S2, _S3);
    output_0.position_0 = vec4<f32>(_S2 * 2.0f - 1.0f, OpenIgiClipY_0(1.0f - _S3 * 2.0f), 0.0f, 1.0f);
    return output_0;
}

@vertex
fn vertexMain(@builtin(vertex_index) index_0 : u32) -> FullscreenVaryings_0
{
    return FullscreenTriangle_0(index_0);
}

fn EdgeFade_0( position_1 : vec2<f32>,  scale_0 : vec2<f32>) -> f32
{
    var u_0 : vec2<f32> = (position_1 - p_0.frame_0.xy * vec2<f32>(0.5f)) / vec2<f32>(p_0.frame_0.y) * scale_0;
    var _S4 : f32 = u_0.x;
    var _S5 : f32 = u_0.y;
    return 16.0f * saturate(0.5f + _S4) * saturate(0.5f + _S5) * saturate(0.5f - _S4) * saturate(0.5f - _S5);
}

fn Mask_0( pixel_0 : vec2<i32>) -> vec4<f32>
{
    var _S6 : vec2<i32> = vec2<i32>(p_0.frame_0.xy);
    var _S7 : bool;
    if((any((pixel_0 < vec2<i32>(i32(0), i32(0))))))
    {
        _S7 = true;
    }
    else
    {
        _S7 = (any((pixel_0 >= _S6)));
    }
    if(_S7)
    {
        return vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
    }
    var _S8 : vec3<i32> = vec3<i32>(pixel_0, i32(0));
    return (textureLoad((uMask_0), ((_S8)).xy, ((_S8)).z));
}

fn Lay_0( under_0 : ptr<function, vec4<f32>>,  inside_0 : bool,  n_0 : f32,  offsets_0 : vec2<f32>,  least_0 : f32,  fill_0 : vec4<f32>,  outline_0 : vec4<f32>,  scale_1 : f32,  fade_0 : f32)
{
    var mean_0 : f32;
    if(n_0 > 0.0f)
    {
        mean_0 = length(offsets_0) / n_0;
    }
    else
    {
        mean_0 = 100.0f;
    }
    var x_0 : f32 = scale_1 * max(0.89999997615814209f * max(mean_0, sqrt(least_0)) - 1.0f, 0.0f);
    var e_0 : f32 = outline_0.w * exp(-4.0f * x_0 * x_0);
    var fillAlpha_0 : f32;
    if(inside_0)
    {
        fillAlpha_0 = fill_0.w;
    }
    else
    {
        fillAlpha_0 = 0.0f;
    }
    var fillColour_0 : vec3<f32> = fill_0.xyz * vec3<f32>(fillAlpha_0);
    var alpha_0 : f32 = (1.0f - (1.0f - fillAlpha_0) * (1.0f - e_0)) * fade_0;
    var _S9 : f32 = 1.0f - alpha_0;
    (*under_0) = vec4<f32>((fillColour_0 + vec3<f32>(e_0) * (outline_0.xyz - fillColour_0)) * vec3<f32>(fade_0) + (*under_0).xyz * vec3<f32>(_S9), alpha_0 + (*under_0).w * _S9);
    return;
}

struct pixelOutput_0
{
    @location(0) output_1 : vec4<f32>,
};

struct pixelInput_0
{
    @location(0) uv_1 : vec2<f32>,
};

@fragment
fn fragmentMain( _S10 : pixelInput_0, @builtin(position) position_2 : vec4<f32>) -> pixelOutput_0
{
    var position_3 : vec2<f32> = position_2.xy;
    var hostileFade_0 : f32 = EdgeFade_0(position_3, p_0.vignettes_0.xy);
    var otherFade_0 : f32 = EdgeFade_0(position_3, p_0.vignettes_0.zw);
    if((max(hostileFade_0, otherFade_0)) <= 0.0f)
    {
        var _S11 : pixelOutput_0 = pixelOutput_0( vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f) );
        return _S11;
    }
    var _S12 : vec2<i32> = vec2<i32>(position_3);
    var _S13 : vec3<f32> = vec3<f32>(0.5f);
    var inside_1 : vec3<bool> = (Mask_0(_S12).xyz) > _S13;
    var n_1 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    const _S14 : vec2<f32> = vec2<f32>(0.0f, 0.0f);
    var least_1 : vec3<f32> = vec3<f32>(100.0f, 100.0f, 100.0f);
    var dy_0 : i32 = i32(2);
    var s0_0 : vec2<f32> = _S14;
    var s1_0 : vec2<f32> = _S14;
    var s2_0 : vec2<f32> = _S14;
    for(;;)
    {
        if(dy_0 >= i32(-2))
        {
        }
        else
        {
            break;
        }
        var dx_0 : i32 = i32(2);
        var s0_1 : vec2<f32> = s0_0;
        var s1_1 : vec2<f32> = s1_0;
        var s2_1 : vec2<f32> = s2_0;
        for(;;)
        {
            if(dx_0 >= i32(-2))
            {
            }
            else
            {
                break;
            }
            var _S15 : bool;
            if(dx_0 == i32(0))
            {
                _S15 = dy_0 == i32(0);
            }
            else
            {
                _S15 = false;
            }
            if(_S15)
            {
                dx_0 = dx_0 - i32(1);
                continue;
            }
            var there_0 : vec3<bool> = (Mask_0(_S12 + vec2<i32>(dx_0, dy_0)).xyz) > _S13;
            var offset_0 : vec2<f32> = vec2<f32>(f32(dx_0), f32(dy_0));
            var squared_0 : f32 = dot(offset_0, offset_0);
            var s0_2 : vec2<f32>;
            if((there_0.x) != (inside_1.x))
            {
                n_1[i32(0)] = n_1[i32(0)] + 1.0f;
                var s0_3 : vec2<f32> = s0_1 + offset_0;
                least_1[i32(0)] = min(least_1.x, squared_0);
                s0_2 = s0_3;
            }
            else
            {
                s0_2 = s0_1;
            }
            var s1_2 : vec2<f32>;
            if((there_0.y) != (inside_1.y))
            {
                n_1[i32(1)] = n_1[i32(1)] + 1.0f;
                var s1_3 : vec2<f32> = s1_1 + offset_0;
                least_1[i32(1)] = min(least_1.y, squared_0);
                s1_2 = s1_3;
            }
            else
            {
                s1_2 = s1_1;
            }
            var s2_2 : vec2<f32>;
            if((there_0.z) != (inside_1.z))
            {
                n_1[i32(2)] = n_1[i32(2)] + 1.0f;
                var s2_3 : vec2<f32> = s2_1 + offset_0;
                least_1[i32(2)] = min(least_1.z, squared_0);
                s2_2 = s2_3;
            }
            else
            {
                s2_2 = s2_1;
            }
            s0_1 = s0_2;
            s1_1 = s1_2;
            s2_1 = s2_2;
            dx_0 = dx_0 - i32(1);
        }
        dy_0 = dy_0 - i32(1);
        s0_0 = s0_1;
        s1_0 = s1_1;
        s2_0 = s2_1;
    }
    var result_0 : vec4<f32> = vec4<f32>(0.0f, 0.0f, 0.0f, 0.0f);
    Lay_0(&(result_0), inside_1.z, n_1.z, s2_0, least_1.z, p_0.fill2_0, p_0.outline2_0, p_0.frame_0.z, otherFade_0);
    Lay_0(&(result_0), inside_1.y, n_1.y, s1_0, least_1.y, p_0.fill1_0, p_0.outline1_0, p_0.frame_0.z, otherFade_0);
    Lay_0(&(result_0), inside_1.x, n_1.x, s0_0, least_1.x, p_0.fill0_0, p_0.outline0_0, p_0.frame_0.z, hostileFade_0);
    var _S16 : pixelOutput_0 = pixelOutput_0( result_0 );
    return _S16;
}

