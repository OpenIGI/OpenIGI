@binding(0) @group(0) var uPicture_0 : texture_2d<f32>;

@id(65) override cTargetEncodes_0 : i32 = i32(0);

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

fn DecodeSrgb_0( c_0 : vec3<f32>) -> vec3<f32>
{
    return mix(pow(max((c_0 + vec3<f32>(0.05499999970197678f)) / vec3<f32>(1.0549999475479126f), vec3<f32>(0.0f, 0.0f, 0.0f)), vec3<f32>(2.40000009536743164f, 2.40000009536743164f, 2.40000009536743164f)), c_0 / vec3<f32>(12.92000007629394531f), step(c_0, vec3<f32>(0.04044999927282333f, 0.04044999927282333f, 0.04044999927282333f)));
}

fn ToTarget_0( display_0 : vec4<f32>) -> vec4<f32>
{
    var _S4 : vec4<f32>;
    if(cTargetEncodes_0 != i32(0))
    {
        _S4 = vec4<f32>(DecodeSrgb_0(display_0.xyz), display_0.w);
    }
    else
    {
        _S4 = display_0;
    }
    return _S4;
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
    var _S6 : vec3<i32> = vec3<i32>(vec2<i32>(position_1.xy), i32(0));
    var _S7 : pixelOutput_0 = pixelOutput_0( ToTarget_0(vec4<f32>((textureLoad((uPicture_0), ((_S6)).xy, ((_S6)).z)).xyz, 1.0f)) );
    return _S7;
}

