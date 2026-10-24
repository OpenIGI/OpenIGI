struct ScopeGlintParams_std140_0
{
    @align(16) frame_0 : vec4<f32>,
    @align(16) glints_0 : array<vec4<f32>, i32(7)>,
};

@binding(0) @group(3) var<uniform> p_0 : ScopeGlintParams_std140_0;
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

fn Noise_0( x_0 : f32) -> f32
{
    var i_0 : f32 = floor(x_0);
    var f_0 : f32 = fract(x_0);
    return mix(fract(sin(i_0 * 12.98980045318603516f) * 43758.546875f), fract(sin((i_0 + 1.0f) * 12.98980045318603516f) * 43758.546875f), f_0 * f_0 * (3.0f - 2.0f * f_0));
}

fn Glint_0( d_0 : vec2<f32>,  strength_0 : f32,  seconds_0 : f32,  seed_0 : f32) -> vec3<f32>
{
    var r2_0 : f32 = dot(d_0, d_0);
    var _S4 : f32 = - r2_0;
    const sun_0 : vec3<f32> = vec3<f32>(1.0f, 0.94999998807907104f, 0.86000001430511475f);
    return vec3<f32>((strength_0 * (0.72000002861022949f + 0.20000000298023224f * Noise_0(seconds_0 * 3.09999990463256836f + seed_0 * 7.0f) + 0.07999999821186066f * Noise_0(seconds_0 * 11.0f + seed_0)))) * (sun_0 * vec3<f32>((exp(_S4 * 70.0f) * 3.0f)) + sun_0 * vec3<f32>((exp(_S4 * 2.79999995231628418f) * 0.31999999284744263f + exp(- sqrt(r2_0) * 1.29999995231628418f) * 0.10000000149011612f)) + vec3<f32>(0.92000001668930054f, 0.94999998807907104f, 1.0f) * vec3<f32>((exp(- abs(d_0.y) * 40.0f) * exp(- abs(d_0.x) * 1.5f) * 0.30000001192092896f)));
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
fn fragmentMain( _S5 : pixelInput_0, @builtin(position) position_1 : vec4<f32>) -> pixelOutput_0
{
    var _S6 : vec2<f32> = _S5.uv_1 * p_0.frame_0.xy;
    var _S7 : i32 = min(i32(p_0.frame_0.w), i32(7));
    const _S8 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var i_1 : i32 = i32(0);
    var light_0 : vec3<f32> = _S8;
    for(;;)
    {
        if(i_1 < _S7)
        {
        }
        else
        {
            break;
        }
        var g_0 : vec4<f32> = p_0.glints_0[i_1];
        var _S9 : f32 = p_0.glints_0[i_1].z;
        var _S10 : bool;
        if(_S9 <= 0.0f)
        {
            _S10 = true;
        }
        else
        {
            _S10 = (g_0.w) <= 0.0f;
        }
        if(_S10)
        {
            i_1 = i_1 + i32(1);
            continue;
        }
        var d_1 : vec2<f32> = (_S6 - g_0.xy) / vec2<f32>(_S9);
        var _S11 : bool;
        if((abs(d_1.x)) > 6.0f)
        {
            _S11 = true;
        }
        else
        {
            _S11 = (abs(d_1.y)) > 4.0f;
        }
        if(_S11)
        {
            i_1 = i_1 + i32(1);
            continue;
        }
        light_0 = light_0 + Glint_0(d_1, g_0.w, p_0.frame_0.z, f32(i_1) * 1.61800003051757812f);
        i_1 = i_1 + i32(1);
    }
    var _S12 : pixelOutput_0 = pixelOutput_0( vec4<f32>(light_0, 0.0f) );
    return _S12;
}

