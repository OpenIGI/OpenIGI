struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) range_0 : vec4<f32>,
};

@binding(3) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uScene_0 : texture_2d<f32>;

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
        if((lit_0.w) >= 0.5f)
        {
            var raw_0 : f32 = (lit_0.x + lit_0.y + lit_0.z) * 0.3333333432674408f;
            var position_0 : f32 = saturate(p_0.range_0.x * log2(max(raw_0, p_0.range_0.z)) + p_0.range_0.y) * 63.0f;
            var low_0 : u32 = u32(position_0);
            var _S4 : u32 = min(low_0 + u32(1), u32(63));
            var upper_0 : f32 = fract(position_0);
            var _S5 : f32 = 1.0f - upper_0;
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
                lower_0 = _S5;
            }
            var lowCount_0 : u32 = u32(lower_0 * p_0.range_0.w + 0.5f);
            var highCount_0 : u32 = u32(upper_0 * p_0.range_0.w + 0.5f);
            if(lowCount_0 > u32(0))
            {
                var _S6 : u32 = atomicAdd(&(sHistogram_0[low_0]), lowCount_0);
            }
            if(highCount_0 > u32(0))
            {
                var _S7 : u32 = atomicAdd(&(sHistogram_0[_S4]), highCount_0);
            }
        }
    }
    workgroupBarrier();
    var total_0 : u32 = atomicLoad(&(sHistogram_0[slot_0]));
    if(total_0 > u32(0))
    {
        var _S8 : u32 = atomicAdd(&(uBins_0[slot_0]), total_0);
    }
    return;
}

