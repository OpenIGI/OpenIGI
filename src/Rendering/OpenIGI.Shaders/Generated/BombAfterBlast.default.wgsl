struct AfterBlastParams_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) depth_0 : vec4<f32>,
    @align(16) unproject_0 : vec4<f32>,
    @align(16) blast_0 : vec4<f32>,
    @align(16) shell_0 : vec4<f32>,
    @align(16) reach_0 : vec4<f32>,
    @align(16) haze_0 : vec4<f32>,
    @align(16) tuning_0 : vec4<f32>,
};

@binding(3) @group(0) var<uniform> p_0 : AfterBlastParams_std140_0;
@binding(0) @group(0) var uSource_0 : texture_2d<f32>;

@binding(1) @group(0) var uDepth_0 : texture_depth_2d;

@binding(2) @group(0) var uTarget_0 : texture_storage_2d<rgba16float, write>;

fn SourceAt_0( pixel_0 : vec2<i32>) -> vec3<f32>
{
    var _S1 : vec3<i32> = vec3<i32>(clamp(pixel_0, vec2<i32>(i32(0), i32(0)), vec2<i32>(p_0.size_0.xy) - vec2<i32>(i32(1))), i32(0));
    return (textureLoad((uSource_0), ((_S1)).xy, ((_S1)).z)).xyz;
}

fn DepthAt_0( pixel_1 : vec2<i32>) -> f32
{
    var _S2 : vec3<i32> = vec3<i32>(clamp(vec2<i32>((vec2<f32>(pixel_1) + vec2<f32>(0.5f)) * p_0.depth_0.zw), vec2<i32>(i32(0), i32(0)), vec2<i32>(p_0.depth_0.xy) - vec2<i32>(i32(1))), i32(0));
    return (textureLoad((uDepth_0), ((_S2)).xy, ((_S2)).z));
}

fn ViewDepthAt_0( depth_1 : f32,  unproject_1 : vec4<f32>) -> f32
{
    var _S3 : f32 = unproject_1.z;
    var _S4 : f32 = unproject_1.w;
    return 2.0f * _S3 * _S4 / (_S4 + _S3 - (1.0f - 2.0f * depth_1) * (_S4 - _S3));
}

fn ViewPositionAt_0( uv_0 : vec2<f32>,  depth_2 : f32,  unproject_2 : vec4<f32>) -> vec3<f32>
{
    var viewZ_0 : f32 = ViewDepthAt_0(depth_2, unproject_2);
    var ndc_0 : vec2<f32> = uv_0 * vec2<f32>(2.0f) - vec2<f32>(1.0f);
    return vec3<f32>(ndc_0.x * viewZ_0 / unproject_2.x, - ndc_0.y * viewZ_0 / unproject_2.y, viewZ_0);
}

fn ScreenOf_0( view_0 : vec3<f32>) -> vec2<f32>
{
    var v_0 : vec3<f32>;
    if((view_0.z) > 0.00999999977648258f)
    {
        v_0 = view_0;
    }
    else
    {
        v_0 = (vec3<f32>(0) - view_0);
    }
    var _S5 : f32 = max(v_0.z, 0.00999999977648258f);
    var _S6 : vec2<f32> = vec2<f32>(0.5f);
    return vec2<f32>(v_0.x * p_0.unproject_0.x / _S5, - v_0.y * p_0.unproject_0.y / _S5) * _S6 + _S6;
}

fn Hash_0( seed_0 : vec2<f32>) -> f32
{
    var q_0 : vec3<f32> = fract(seed_0.xyx * vec3<f32>(0.1031000018119812f));
    var q_1 : vec3<f32> = q_0 + vec3<f32>(dot(q_0, q_0.yzx + vec3<f32>(33.3300018310546875f)));
    return fract((q_1.x + q_1.y) * q_1.z);
}

@compute
@workgroup_size(8, 8, 1)
fn main(@builtin(global_invocation_id) id_0 : vec3<u32>)
{
    var _S7 : vec2<u32> = id_0.xy;
    if((any((_S7 >= vec2<u32>(p_0.size_0.xy)))))
    {
        return;
    }
    var _S8 : vec2<i32> = vec2<i32>(_S7);
    var _S9 : vec2<f32> = vec2<f32>(_S8);
    var uv_1 : vec2<f32> = (_S9 + vec2<f32>(0.5f)) * p_0.size_0.zw;
    var scene_0 : vec3<f32> = SourceAt_0(_S8);
    var front_0 : f32 = p_0.blast_0.w;
    var depth_3 : f32 = DepthAt_0(_S8);
    var surface_0 : vec3<f32>;
    if(depth_3 > 0.0f)
    {
        surface_0 = ViewPositionAt_0(uv_1, depth_3, p_0.unproject_0);
    }
    else
    {
        surface_0 = vec3<f32>(0.0f, 0.0f, p_0.unproject_0.w * 4.0f);
    }
    var fromBomb_0 : f32 = distance(surface_0, p_0.blast_0.xyz);
    var fromEye_0 : f32 = length(surface_0);
    var wake_0 : f32 = smoothstep(front_0 - p_0.shell_0.y, front_0, fromBomb_0);
    var shell_1 : f32 = smoothstep(front_0 + p_0.shell_0.x, front_0, fromBomb_0) * wake_0 * wake_0 * smoothstep(p_0.reach_0.x, p_0.reach_0.y, fromEye_0) * smoothstep(p_0.reach_0.w, p_0.reach_0.z, fromEye_0);
    var shown_0 : vec3<f32>;
    if(shell_1 > 0.00100000004749745f)
    {
        var toBomb_0 : vec2<f32> = ScreenOf_0(p_0.blast_0.xyz) - uv_1;
        var span_0 : f32 = length(toBomb_0);
        var _S10 : vec2<f32>;
        if(span_0 > 0.00000999999974738f)
        {
            _S10 = toBomb_0 / vec2<f32>(span_0);
        }
        else
        {
            _S10 = vec2<f32>(0.0f, 0.0f);
        }
        var _S11 : f32 = shell_1 * p_0.shell_0.z * min(span_0 * 2.0f, 1.0f);
        var _S12 : i32 = max(i32(p_0.tuning_0.z), i32(1));
        var _S13 : f32 = Hash_0(_S9 + vec2<f32>((p_0.tuning_0.y * 7.30999994277954102f))) - 0.5f;
        const _S14 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
        var i_0 : i32 = i32(0);
        shown_0 = _S14;
        for(;;)
        {
            if(i_0 < _S12)
            {
            }
            else
            {
                break;
            }
            var _S15 : f32 = f32(_S12);
            var sum_0 : vec3<f32> = shown_0 + SourceAt_0(vec2<i32>(clamp(mix(uv_1 + _S10 * vec2<f32>(_S11) * vec2<f32>(((f32(i_0) + 0.5f) / _S15 - 0.5f + _S13 / _S15)), vec2<f32>(0.5f, 0.5f), vec2<f32>((0.01999999955296516f * shell_1))), vec2<f32>(0.0f), vec2<f32>(1.0f)) * p_0.size_0.xy));
            i_0 = i_0 + i32(1);
            shown_0 = sum_0;
        }
        var streaked_0 : vec3<f32> = shown_0 / vec3<f32>(f32(_S12));
        shown_0 = mix(scene_0, streaked_0 + streaked_0 * vec3<f32>(pow(saturate(dot(streaked_0, vec3<f32>(0.2125999927520752f, 0.71520000696182251f, 0.07220000028610229f)) * p_0.tuning_0.x), 6.0f)) * vec3<f32>(1.5f), vec3<f32>(saturate(shell_1 * p_0.shell_0.w)));
    }
    else
    {
        shown_0 = scene_0;
    }
    var passed_0 : f32 = smoothstep(front_0, front_0 - p_0.reach_0.w, length(p_0.blast_0.xyz));
    textureStore((uTarget_0), (vec2<u32>(_S8)), (vec4<f32>(mix(shown_0, p_0.haze_0.xyz, vec3<f32>((passed_0 * passed_0 * p_0.haze_0.w))), 1.0f)));
    return;
}

