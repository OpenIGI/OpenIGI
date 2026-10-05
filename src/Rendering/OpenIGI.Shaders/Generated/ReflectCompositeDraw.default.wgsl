@binding(0) @group(0) var uReflection_0 : texture_2d<f32>;

@binding(1) @group(0) var uDepth_0 : texture_depth_2d;

@binding(2) @group(0) var uNormal_0 : texture_2d<f32>;

var<private> kSpread_0 : array<vec2<f32>, i32(12)> = array<vec2<f32>, i32(12)>( vec2<f32>(-0.32600000500679016f, -0.40599998831748962f), vec2<f32>(-0.8399999737739563f, -0.07400000095367432f), vec2<f32>(-0.69599997997283936f, 0.45699998736381531f), vec2<f32>(-0.20299999415874481f, 0.62099999189376831f), vec2<f32>(0.96200001239776611f, -0.19499999284744263f), vec2<f32>(0.47299998998641968f, -0.47999998927116394f), vec2<f32>(0.51899999380111694f, 0.76700001955032349f), vec2<f32>(0.18500000238418579f, -0.89300000667572021f), vec2<f32>(0.50700002908706665f, 0.06400000303983688f), vec2<f32>(0.89600002765655518f, 0.41200000047683716f), vec2<f32>(-0.32199999690055847f, -0.93300002813339233f), vec2<f32>(-0.79199999570846558f, -0.59799998998641968f) );
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
fn vertexMain(@builtin(vertex_index) vertexIndex_1 : u32) -> FullscreenVaryings_0
{
    return FullscreenTriangle_0(vertexIndex_1);
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
fn fragmentMain( _S4 : pixelInput_0, @builtin(position) position_1 : vec4<f32>) -> pixelOutput_0
{
    var _S5 : vec2<i32> = vec2<i32>(position_1.xy);
    var _S6 : vec3<i32> = vec3<i32>(_S5, i32(0));
    var centre_0 : vec4<f32> = (textureLoad((uReflection_0), ((_S6)).xy, ((_S6)).z));
    var reflection_0 : vec3<f32> = centre_0.xyz;
    var _S7 : f32 = centre_0.w;
    var reflection_1 : vec3<f32>;
    if(_S7 >= 2.0f)
    {
        var roughness_0 : f32 = _S7 - 2.0f;
        var radius_0 : f32 = clamp(roughness_0 * roughness_0 * 28.0f, 0.0f, 10.0f);
        if(radius_0 >= 1.0f)
        {
            var width_0 : u32;
            var height_0 : u32;
            {var dim = textureDimensions((uDepth_0));((width_0)) = dim.x;((height_0)) = dim.y;};
            var _S8 : vec2<i32> = vec2<i32>(i32(width_0) - i32(1), i32(height_0) - i32(1));
            var _S9 : f32 = (textureLoad((uDepth_0), ((_S6)).xy, ((_S6)).z));
            var _S10 : vec3<f32> = vec3<f32>(2.0f);
            var _S11 : vec3<f32> = vec3<f32>(1.0f);
            var _S12 : vec3<f32> = normalize((textureLoad((uNormal_0), ((_S6)).xy, ((_S6)).z)).xyz * _S10 - _S11);
            var angle_0 : f32 = fract(52.98291778564453125f * fract(dot(vec2<f32>(_S5), vec2<f32>(0.06711056083440781f, 0.00583714991807938f)))) * 6.28318548202514648f;
            var _S13 : f32 = sin(angle_0);
            var _S14 : f32 = cos(angle_0);
            var i_0 : i32 = i32(0);
            reflection_1 = reflection_0;
            var weightSum_0 : f32 = 1.0f;
            for(;;)
            {
                if(i_0 < i32(12))
                {
                }
                else
                {
                    break;
                }
                var _S15 : f32 = kSpread_0[i_0].x;
                var _S16 : f32 = kSpread_0[i_0].y;
                var _S17 : vec3<i32> = vec3<i32>(clamp(_S5 + vec2<i32>(round(vec2<f32>(_S15 * _S14 - _S16 * _S13, _S15 * _S13 + _S16 * _S14) * vec2<f32>(radius_0))), vec2<i32>(i32(0), i32(0)), _S8), i32(0));
                var sample_0 : vec4<f32> = (textureLoad((uReflection_0), ((_S17)).xy, ((_S17)).z));
                if((sample_0.w) < 2.0f)
                {
                    i_0 = i_0 + i32(1);
                    continue;
                }
                var sampleNormal_0 : vec3<f32> = normalize((textureLoad((uNormal_0), ((_S17)).xy, ((_S17)).z)).xyz * _S10 - _S11);
                var sameDepth_0 : f32;
                if((abs((textureLoad((uDepth_0), ((_S17)).xy, ((_S17)).z)) - _S9)) <= (0.02999999932944775f * max(_S9, 9.99999997475242708e-07f)))
                {
                    sameDepth_0 = 1.0f;
                }
                else
                {
                    sameDepth_0 = 0.0f;
                }
                var facing_0 : f32 = saturate(dot(sampleNormal_0, _S12));
                var weight_0 : f32 = sameDepth_0 * facing_0 * facing_0 * facing_0 * facing_0;
                var weightSum_1 : f32 = weightSum_0 + weight_0;
                reflection_1 = reflection_1 + sample_0.xyz * vec3<f32>(weight_0);
                weightSum_0 = weightSum_1;
                i_0 = i_0 + i32(1);
            }
            reflection_1 = reflection_1 / vec3<f32>(weightSum_0);
        }
        else
        {
            reflection_1 = reflection_0;
        }
    }
    else
    {
        reflection_1 = reflection_0;
    }
    var _S18 : pixelOutput_0 = pixelOutput_0( vec4<f32>(max(reflection_1, vec3<f32>(0.0f, 0.0f, 0.0f)), 0.0f) );
    return _S18;
}

