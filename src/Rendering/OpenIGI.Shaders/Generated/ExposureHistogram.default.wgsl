struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) range_0 : vec4<f32>,
};

@binding(3) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uScene_0 : texture_2d<f32>;

@binding(4) @group(0) var uMarker_0 : texture_2d<f32>;

@binding(2) @group(0) var<storage, read_write> uBins_0 : array<atomic<u32>>;

var<workgroup> sHistogram_0 : array<atomic<u32>, i32(64)>;

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(global_invocation_id) id_0 : vec3<u32>, @builtin(local_invocation_index) slot_0 : u32)
{
    atomicStore(&(sHistogram_0[slot_0]), u32(0));
    workgroupBarrier();
    var _S1 : vec2<i32> = vec2<i32>(id_0.xy);
    var _S2 : bool;
    if((_S1.x) < i32(p_0.size_0.x))
    {
        _S2 = (_S1.y) < i32(p_0.size_0.y);
    }
    else
    {
        _S2 = false;
    }
    if(_S2)
    {
        var _S3 : vec3<i32> = vec3<i32>(_S1, i32(0));
        var lit_0 : vec4<f32> = (textureLoad((uScene_0), ((_S3)).xy, ((_S3)).z));
        var markerWidth_0 : u32;
        var markerHeight_0 : u32;
        {var dim = textureDimensions((uMarker_0));((markerWidth_0)) = dim.x;((markerHeight_0)) = dim.y;};
        var _S4 : vec3<i32> = vec3<i32>(vec2<i32>((vec2<f32>(_S1) + vec2<f32>(0.5f)) * vec2<f32>(f32(markerWidth_0), f32(markerHeight_0)) / p_0.size_0.xy), i32(0));
        if(((textureLoad((uMarker_0), ((_S4)).xy, ((_S4)).z)).w) >= 0.5f)
        {
            var raw_0 : f32 = dot(lit_0.xyz, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f));
            var position_0 : f32 = saturate(p_0.range_0.x * log2(max(raw_0, p_0.range_0.z)) + p_0.range_0.y) * 63.0f;
            var low_0 : u32 = u32(position_0);
            var _S5 : u32 = min(low_0 + u32(1), u32(63));
            var upper_0 : f32 = fract(position_0);
            var _S6 : f32 = 1.0f - upper_0;
            if(low_0 == u32(0))
            {
                _S2 = raw_0 <= 1.00000001168609742e-07f;
            }
            else
            {
                _S2 = false;
            }
            var lower_0 : f32;
            if(_S2)
            {
                lower_0 = 0.0f;
            }
            else
            {
                lower_0 = _S6;
            }
            var lowCount_0 : u32 = u32(lower_0 * p_0.range_0.w + 0.5f);
            var highCount_0 : u32 = u32(upper_0 * p_0.range_0.w + 0.5f);
            if(lowCount_0 > u32(0))
            {
                var _S7 : u32 = atomicAdd(&(sHistogram_0[low_0]), lowCount_0);
            }
            if(highCount_0 > u32(0))
            {
                var _S8 : u32 = atomicAdd(&(sHistogram_0[_S5]), highCount_0);
            }
        }
    }
    workgroupBarrier();
    var total_0 : u32 = atomicLoad(&(sHistogram_0[slot_0]));
    if(total_0 > u32(0))
    {
        var _S9 : u32 = atomicAdd(&(uBins_0[slot_0]), total_0);
    }
    return;
}

