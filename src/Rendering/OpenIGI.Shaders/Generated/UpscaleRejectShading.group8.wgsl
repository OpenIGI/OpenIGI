struct Params_std140_0
{
    @align(16) size_0 : vec4<f32>,
    @align(16) control_0 : vec4<f32>,
};

@binding(12) @group(0) var<uniform> p_0 : Params_std140_0;
@binding(0) @group(0) var uCurrent_0 : texture_2d<f32>;

@binding(2) @group(0) var uDilatedVelocity_0 : texture_2d<f32>;

@binding(4) @group(0) var uPrevGuide_0 : texture_2d<f32>;

@binding(5) @group(0) var uPrevGuideSampler_0 : sampler;

@binding(10) @group(0) var uThinGeometry_0 : texture_2d<f32>;

@binding(8) @group(0) var uRejection_0 : texture_storage_2d<rgba32float, write>;

@binding(6) @group(0) var uParallaxRejection_0 : texture_2d<f32>;

@binding(7) @group(0) var uParallaxRejectionSampler_0 : sampler;

@binding(9) @group(0) var uGuide_0 : texture_storage_2d<rgba16float, write>;

fn RegionCount_0( r_0 : i32) -> u32
{
    var extent_0 : i32 = i32(8) + i32(2) * r_0;
    return u32(extent_0 * extent_0);
}

fn RegionCell_0( i_0 : u32,  r_1 : i32) -> vec2<i32>
{
    var extent_1 : i32 = i32(8) + i32(2) * r_1;
    var _S1 : i32 = i32(i_0);
    var _S2 : i32 = _S1 % extent_1;
    var _S3 : i32 = _S1 / extent_1;
    return vec2<i32>(_S2, _S3) - vec2<i32>(r_1);
}

var<workgroup> sInput_0 : array<f32, i32(1200)>;

var<workgroup> sHistory_0 : array<f32, i32(1200)>;

var<workgroup> sMid_0 : array<f32, i32(972)>;

var<workgroup> sInClamped_0 : array<f32, i32(768)>;

var<workgroup> sHistClamped_0 : array<f32, i32(768)>;

fn Binomial_0( x_0 : i32,  y_0 : i32) -> f32
{
    var wx_0 : f32;
    if(x_0 == i32(0))
    {
        wx_0 = 0.5f;
    }
    else
    {
        wx_0 = 0.25f;
    }
    var wy_0 : f32;
    if(y_0 == i32(0))
    {
        wy_0 = 0.5f;
    }
    else
    {
        wy_0 = 0.25f;
    }
    return wx_0 * wy_0;
}

var<workgroup> sBlur_0 : array<f32, i32(768)>;

fn Order_0( a_0 : ptr<function, vec3<f32>>,  b_0 : ptr<function, vec3<f32>>)
{
    var lo_0 : vec3<f32> = min((*a_0), (*b_0));
    (*b_0) = max((*a_0), (*b_0));
    (*a_0) = lo_0;
    return;
}

fn Median9_0( v_0 : array<vec3<f32>, i32(9)>) -> vec3<f32>
{
    var p0_0 : vec3<f32> = v_0[i32(0)];
    var p1_0 : vec3<f32> = v_0[i32(1)];
    var p2_0 : vec3<f32> = v_0[i32(2)];
    var p3_0 : vec3<f32> = v_0[i32(3)];
    var p4_0 : vec3<f32> = v_0[i32(4)];
    var p5_0 : vec3<f32> = v_0[i32(5)];
    var p6_0 : vec3<f32> = v_0[i32(6)];
    var p7_0 : vec3<f32> = v_0[i32(7)];
    var p8_0 : vec3<f32> = v_0[i32(8)];
    Order_0(&(p1_0), &(p2_0));
    Order_0(&(p4_0), &(p5_0));
    Order_0(&(p7_0), &(p8_0));
    Order_0(&(p0_0), &(p1_0));
    Order_0(&(p3_0), &(p4_0));
    Order_0(&(p6_0), &(p7_0));
    Order_0(&(p1_0), &(p2_0));
    Order_0(&(p4_0), &(p5_0));
    Order_0(&(p7_0), &(p8_0));
    Order_0(&(p0_0), &(p3_0));
    Order_0(&(p5_0), &(p8_0));
    Order_0(&(p4_0), &(p7_0));
    Order_0(&(p3_0), &(p6_0));
    Order_0(&(p1_0), &(p4_0));
    Order_0(&(p2_0), &(p5_0));
    Order_0(&(p4_0), &(p7_0));
    Order_0(&(p4_0), &(p2_0));
    Order_0(&(p6_0), &(p4_0));
    Order_0(&(p4_0), &(p2_0));
    return p4_0;
}

@compute
@workgroup_size(8, 8, 1)
fn computeMain(@builtin(workgroup_id) groupId_0 : vec3<u32>, @builtin(local_invocation_id) localId_0 : vec3<u32>, @builtin(local_invocation_index) lid_0 : u32)
{
    var _S4 : u32;
    var _S5 : u32;
    var _S6 : vec2<i32> = vec2<i32>(p_0.size_0.xy);
    var _S7 : vec2<i32> = _S6 - vec2<i32>(i32(1));
    var origin_0 : vec2<i32> = vec2<i32>(groupId_0.xy) * vec2<i32>(i32(8));
    var _S8 : vec2<i32> = vec2<i32>(localId_0.xy);
    var px_0 : vec2<i32> = origin_0 + _S8;
    var _S9 : f32 = p_0.control_0.x;
    var i_1 : u32 = lid_0;
    for(;;)
    {
        if(i_1 < (RegionCount_0(i32(6))))
        {
        }
        else
        {
            break;
        }
        var q_0 : vec2<i32> = RegionCell_0(i_1, i32(6));
        var t_0 : vec2<i32> = clamp(origin_0 + q_0, vec2<i32>(i32(0), i32(0)), _S7);
        var _S10 : vec3<i32> = vec3<i32>(t_0, i32(0));
        var _S11 : vec3<f32> = vec3<f32>(0.0f);
        var x_1 : vec3<f32> = max((textureLoad((uCurrent_0), ((_S10)).xy, ((_S10)).z)).xyz, _S11) * vec3<f32>(_S9);
        var inputValue_0 : vec3<f32> = x_1 / (x_1 + vec3<f32>(0.17000000178813934f));
        var historyValue_0 : vec3<f32> = max((textureSampleLevel((uPrevGuide_0), (uPrevGuideSampler_0), (clamp((vec2<f32>(t_0) + vec2<f32>(0.5f)) * p_0.size_0.zw - (textureLoad((uDilatedVelocity_0), ((_S10)).xy, ((_S10)).z)).xy, vec2<f32>(0.0f), vec2<f32>(1.0f))), (0.0f))).xyz, _S11);
        var at_0 : i32 = ((q_0.y + i32(6)) * i32(20) + (q_0.x + i32(6))) * i32(3);
        sInput_0[at_0] = inputValue_0.x;
        var _S12 : i32 = at_0 + i32(1);
        sInput_0[_S12] = inputValue_0.y;
        var _S13 : i32 = at_0 + i32(2);
        sInput_0[_S13] = inputValue_0.z;
        sHistory_0[at_0] = historyValue_0.x;
        sHistory_0[_S12] = historyValue_0.y;
        sHistory_0[_S13] = historyValue_0.z;
        i_1 = i_1 + u32(64);
    }
    workgroupBarrier();
    var _S14 : i32 = _S8.y;
    var _S15 : i32 = _S8.x;
    var _S16 : i32 = ((_S14 + i32(6)) * i32(20) + (_S15 + i32(6))) * i32(3);
    var _S17 : i32 = _S16 + i32(1);
    var _S18 : i32 = _S16 + i32(2);
    var inputPx_0 : vec3<f32> = vec3<f32>(sInput_0[_S16], sInput_0[_S17], sInput_0[_S18]);
    var historyPx_0 : vec3<f32> = vec3<f32>(sHistory_0[_S16], sHistory_0[_S17], sHistory_0[_S18]);
    i_1 = lid_0;
    for(;;)
    {
        var _S19 : u32 = RegionCount_0(i32(5));
        _S4 = _S19;
        if(i_1 < _S19)
        {
        }
        else
        {
            break;
        }
        var q_1 : vec2<i32> = RegionCell_0(i_1, i32(5));
        const _S20 : vec3<f32> = vec3<f32>(1.00000001504746622e+30f, 1.00000001504746622e+30f, 1.00000001504746622e+30f);
        const _S21 : vec3<f32> = vec3<f32>(-1.00000001504746622e+30f, -1.00000001504746622e+30f, -1.00000001504746622e+30f);
        var _S22 : vec2<i32> = q_1 + vec2<i32>(i32(-1), i32(-1));
        var _S23 : i32 = ((_S22.y + i32(6)) * i32(20) + (_S22.x + i32(6))) * i32(3);
        var s_0 : vec3<f32> = vec3<f32>(sInput_0[_S23], sInput_0[_S23 + i32(1)], sInput_0[_S23 + i32(2)]);
        var _S24 : vec2<i32> = q_1 + vec2<i32>(i32(0), i32(-1));
        var _S25 : i32 = ((_S24.y + i32(6)) * i32(20) + (_S24.x + i32(6))) * i32(3);
        var s_1 : vec3<f32> = vec3<f32>(sInput_0[_S25], sInput_0[_S25 + i32(1)], sInput_0[_S25 + i32(2)]);
        var _S26 : vec2<i32> = q_1 + vec2<i32>(i32(1), i32(-1));
        var _S27 : i32 = ((_S26.y + i32(6)) * i32(20) + (_S26.x + i32(6))) * i32(3);
        var s_2 : vec3<f32> = vec3<f32>(sInput_0[_S27], sInput_0[_S27 + i32(1)], sInput_0[_S27 + i32(2)]);
        var _S28 : vec2<i32> = q_1 + vec2<i32>(i32(-1), i32(0));
        var _S29 : i32 = ((_S28.y + i32(6)) * i32(20) + (_S28.x + i32(6))) * i32(3);
        var s_3 : vec3<f32> = vec3<f32>(sInput_0[_S29], sInput_0[_S29 + i32(1)], sInput_0[_S29 + i32(2)]);
        var _S30 : i32 = q_1.y;
        var _S31 : i32 = q_1.x;
        var _S32 : i32 = ((_S30 + i32(6)) * i32(20) + (_S31 + i32(6))) * i32(3);
        var _S33 : i32 = _S32 + i32(1);
        var _S34 : i32 = _S32 + i32(2);
        var s_4 : vec3<f32> = vec3<f32>(sInput_0[_S32], sInput_0[_S33], sInput_0[_S34]);
        var _S35 : vec2<i32> = q_1 + vec2<i32>(i32(1), i32(0));
        var _S36 : i32 = ((_S35.y + i32(6)) * i32(20) + (_S35.x + i32(6))) * i32(3);
        var s_5 : vec3<f32> = vec3<f32>(sInput_0[_S36], sInput_0[_S36 + i32(1)], sInput_0[_S36 + i32(2)]);
        var _S37 : vec2<i32> = q_1 + vec2<i32>(i32(-1), i32(1));
        var _S38 : i32 = ((_S37.y + i32(6)) * i32(20) + (_S37.x + i32(6))) * i32(3);
        var s_6 : vec3<f32> = vec3<f32>(sInput_0[_S38], sInput_0[_S38 + i32(1)], sInput_0[_S38 + i32(2)]);
        var _S39 : vec2<i32> = q_1 + vec2<i32>(i32(0), i32(1));
        var _S40 : i32 = ((_S39.y + i32(6)) * i32(20) + (_S39.x + i32(6))) * i32(3);
        var s_7 : vec3<f32> = vec3<f32>(sInput_0[_S40], sInput_0[_S40 + i32(1)], sInput_0[_S40 + i32(2)]);
        var _S41 : vec2<i32> = q_1 + vec2<i32>(i32(1), i32(1));
        var _S42 : i32 = ((_S41.y + i32(6)) * i32(20) + (_S41.x + i32(6))) * i32(3);
        var s_8 : vec3<f32> = vec3<f32>(sInput_0[_S42], sInput_0[_S42 + i32(1)], sInput_0[_S42 + i32(2)]);
        var at_1 : i32 = ((_S30 + i32(5)) * i32(18) + (_S31 + i32(5))) * i32(3);
        var v_1 : vec3<f32> = clamp(vec3<f32>(sHistory_0[_S32], sHistory_0[_S33], sHistory_0[_S34]), min(min(min(min(min(min(min(min(min(_S20, s_0), s_1), s_2), s_3), s_4), s_5), s_6), s_7), s_8), max(max(max(max(max(max(max(max(max(_S21, s_0), s_1), s_2), s_3), s_4), s_5), s_6), s_7), s_8));
        sMid_0[at_1] = v_1.x;
        sMid_0[at_1 + i32(1)] = v_1.y;
        sMid_0[at_1 + i32(2)] = v_1.z;
        i_1 = i_1 + u32(64);
    }
    workgroupBarrier();
    i_1 = lid_0;
    for(;;)
    {
        var _S43 : u32 = RegionCount_0(i32(4));
        _S5 = _S43;
        if(i_1 < _S43)
        {
        }
        else
        {
            break;
        }
        var q_2 : vec2<i32> = RegionCell_0(i_1, i32(4));
        const _S44 : vec3<f32> = vec3<f32>(1.00000001504746622e+30f, 1.00000001504746622e+30f, 1.00000001504746622e+30f);
        const _S45 : vec3<f32> = vec3<f32>(-1.00000001504746622e+30f, -1.00000001504746622e+30f, -1.00000001504746622e+30f);
        var _S46 : vec2<i32> = q_2 + vec2<i32>(i32(-1), i32(-1));
        var _S47 : i32 = ((_S46.y + i32(5)) * i32(18) + (_S46.x + i32(5))) * i32(3);
        var s_9 : vec3<f32> = vec3<f32>(sMid_0[_S47], sMid_0[_S47 + i32(1)], sMid_0[_S47 + i32(2)]);
        var _S48 : vec2<i32> = q_2 + vec2<i32>(i32(0), i32(-1));
        var _S49 : i32 = ((_S48.y + i32(5)) * i32(18) + (_S48.x + i32(5))) * i32(3);
        var s_10 : vec3<f32> = vec3<f32>(sMid_0[_S49], sMid_0[_S49 + i32(1)], sMid_0[_S49 + i32(2)]);
        var _S50 : vec2<i32> = q_2 + vec2<i32>(i32(1), i32(-1));
        var _S51 : i32 = ((_S50.y + i32(5)) * i32(18) + (_S50.x + i32(5))) * i32(3);
        var s_11 : vec3<f32> = vec3<f32>(sMid_0[_S51], sMid_0[_S51 + i32(1)], sMid_0[_S51 + i32(2)]);
        var _S52 : vec2<i32> = q_2 + vec2<i32>(i32(-1), i32(0));
        var _S53 : i32 = ((_S52.y + i32(5)) * i32(18) + (_S52.x + i32(5))) * i32(3);
        var s_12 : vec3<f32> = vec3<f32>(sMid_0[_S53], sMid_0[_S53 + i32(1)], sMid_0[_S53 + i32(2)]);
        var _S54 : i32 = q_2.y;
        var _S55 : i32 = q_2.x;
        var _S56 : i32 = ((_S54 + i32(5)) * i32(18) + (_S55 + i32(5))) * i32(3);
        var s_13 : vec3<f32> = vec3<f32>(sMid_0[_S56], sMid_0[_S56 + i32(1)], sMid_0[_S56 + i32(2)]);
        var _S57 : vec2<i32> = q_2 + vec2<i32>(i32(1), i32(0));
        var _S58 : i32 = ((_S57.y + i32(5)) * i32(18) + (_S57.x + i32(5))) * i32(3);
        var s_14 : vec3<f32> = vec3<f32>(sMid_0[_S58], sMid_0[_S58 + i32(1)], sMid_0[_S58 + i32(2)]);
        var _S59 : vec2<i32> = q_2 + vec2<i32>(i32(-1), i32(1));
        var _S60 : i32 = ((_S59.y + i32(5)) * i32(18) + (_S59.x + i32(5))) * i32(3);
        var s_15 : vec3<f32> = vec3<f32>(sMid_0[_S60], sMid_0[_S60 + i32(1)], sMid_0[_S60 + i32(2)]);
        var _S61 : vec2<i32> = q_2 + vec2<i32>(i32(0), i32(1));
        var _S62 : i32 = ((_S61.y + i32(5)) * i32(18) + (_S61.x + i32(5))) * i32(3);
        var s_16 : vec3<f32> = vec3<f32>(sMid_0[_S62], sMid_0[_S62 + i32(1)], sMid_0[_S62 + i32(2)]);
        var _S63 : vec2<i32> = q_2 + vec2<i32>(i32(1), i32(1));
        var _S64 : i32 = ((_S63.y + i32(5)) * i32(18) + (_S63.x + i32(5))) * i32(3);
        var s_17 : vec3<f32> = vec3<f32>(sMid_0[_S64], sMid_0[_S64 + i32(1)], sMid_0[_S64 + i32(2)]);
        var at_2 : i32 = ((_S54 + i32(4)) * i32(16) + (_S55 + i32(4))) * i32(3);
        var _S65 : i32 = ((_S54 + i32(6)) * i32(20) + (_S55 + i32(6))) * i32(3);
        var v_2 : vec3<f32> = clamp(vec3<f32>(sInput_0[_S65], sInput_0[_S65 + i32(1)], sInput_0[_S65 + i32(2)]), min(min(min(min(min(min(min(min(min(_S44, s_9), s_10), s_11), s_12), s_13), s_14), s_15), s_16), s_17), max(max(max(max(max(max(max(max(max(_S45, s_9), s_10), s_11), s_12), s_13), s_14), s_15), s_16), s_17));
        sInClamped_0[at_2] = v_2.x;
        sInClamped_0[at_2 + i32(1)] = v_2.y;
        sInClamped_0[at_2 + i32(2)] = v_2.z;
        i_1 = i_1 + u32(64);
    }
    workgroupBarrier();
    i_1 = lid_0;
    for(;;)
    {
        if(i_1 < _S4)
        {
        }
        else
        {
            break;
        }
        var q_3 : vec2<i32> = RegionCell_0(i_1, i32(5));
        const _S66 : vec3<f32> = vec3<f32>(1.00000001504746622e+30f, 1.00000001504746622e+30f, 1.00000001504746622e+30f);
        const _S67 : vec3<f32> = vec3<f32>(-1.00000001504746622e+30f, -1.00000001504746622e+30f, -1.00000001504746622e+30f);
        var _S68 : vec2<i32> = q_3 + vec2<i32>(i32(-1), i32(-1));
        var _S69 : i32 = ((_S68.y + i32(6)) * i32(20) + (_S68.x + i32(6))) * i32(3);
        var s_18 : vec3<f32> = vec3<f32>(sHistory_0[_S69], sHistory_0[_S69 + i32(1)], sHistory_0[_S69 + i32(2)]);
        var _S70 : vec2<i32> = q_3 + vec2<i32>(i32(0), i32(-1));
        var _S71 : i32 = ((_S70.y + i32(6)) * i32(20) + (_S70.x + i32(6))) * i32(3);
        var s_19 : vec3<f32> = vec3<f32>(sHistory_0[_S71], sHistory_0[_S71 + i32(1)], sHistory_0[_S71 + i32(2)]);
        var _S72 : vec2<i32> = q_3 + vec2<i32>(i32(1), i32(-1));
        var _S73 : i32 = ((_S72.y + i32(6)) * i32(20) + (_S72.x + i32(6))) * i32(3);
        var s_20 : vec3<f32> = vec3<f32>(sHistory_0[_S73], sHistory_0[_S73 + i32(1)], sHistory_0[_S73 + i32(2)]);
        var _S74 : vec2<i32> = q_3 + vec2<i32>(i32(-1), i32(0));
        var _S75 : i32 = ((_S74.y + i32(6)) * i32(20) + (_S74.x + i32(6))) * i32(3);
        var s_21 : vec3<f32> = vec3<f32>(sHistory_0[_S75], sHistory_0[_S75 + i32(1)], sHistory_0[_S75 + i32(2)]);
        var _S76 : i32 = q_3.y;
        var _S77 : i32 = q_3.x;
        var _S78 : i32 = ((_S76 + i32(6)) * i32(20) + (_S77 + i32(6))) * i32(3);
        var _S79 : i32 = _S78 + i32(1);
        var _S80 : i32 = _S78 + i32(2);
        var s_22 : vec3<f32> = vec3<f32>(sHistory_0[_S78], sHistory_0[_S79], sHistory_0[_S80]);
        var _S81 : vec2<i32> = q_3 + vec2<i32>(i32(1), i32(0));
        var _S82 : i32 = ((_S81.y + i32(6)) * i32(20) + (_S81.x + i32(6))) * i32(3);
        var s_23 : vec3<f32> = vec3<f32>(sHistory_0[_S82], sHistory_0[_S82 + i32(1)], sHistory_0[_S82 + i32(2)]);
        var _S83 : vec2<i32> = q_3 + vec2<i32>(i32(-1), i32(1));
        var _S84 : i32 = ((_S83.y + i32(6)) * i32(20) + (_S83.x + i32(6))) * i32(3);
        var s_24 : vec3<f32> = vec3<f32>(sHistory_0[_S84], sHistory_0[_S84 + i32(1)], sHistory_0[_S84 + i32(2)]);
        var _S85 : vec2<i32> = q_3 + vec2<i32>(i32(0), i32(1));
        var _S86 : i32 = ((_S85.y + i32(6)) * i32(20) + (_S85.x + i32(6))) * i32(3);
        var s_25 : vec3<f32> = vec3<f32>(sHistory_0[_S86], sHistory_0[_S86 + i32(1)], sHistory_0[_S86 + i32(2)]);
        var _S87 : vec2<i32> = q_3 + vec2<i32>(i32(1), i32(1));
        var _S88 : i32 = ((_S87.y + i32(6)) * i32(20) + (_S87.x + i32(6))) * i32(3);
        var s_26 : vec3<f32> = vec3<f32>(sHistory_0[_S88], sHistory_0[_S88 + i32(1)], sHistory_0[_S88 + i32(2)]);
        var at_3 : i32 = ((_S76 + i32(5)) * i32(18) + (_S77 + i32(5))) * i32(3);
        var v_3 : vec3<f32> = clamp(vec3<f32>(sInput_0[_S78], sInput_0[_S79], sInput_0[_S80]), min(min(min(min(min(min(min(min(min(_S66, s_18), s_19), s_20), s_21), s_22), s_23), s_24), s_25), s_26), max(max(max(max(max(max(max(max(max(_S67, s_18), s_19), s_20), s_21), s_22), s_23), s_24), s_25), s_26));
        sMid_0[at_3] = v_3.x;
        sMid_0[at_3 + i32(1)] = v_3.y;
        sMid_0[at_3 + i32(2)] = v_3.z;
        i_1 = i_1 + u32(64);
    }
    workgroupBarrier();
    i_1 = lid_0;
    for(;;)
    {
        if(i_1 < _S5)
        {
        }
        else
        {
            break;
        }
        var q_4 : vec2<i32> = RegionCell_0(i_1, i32(4));
        const _S89 : vec3<f32> = vec3<f32>(1.00000001504746622e+30f, 1.00000001504746622e+30f, 1.00000001504746622e+30f);
        const _S90 : vec3<f32> = vec3<f32>(-1.00000001504746622e+30f, -1.00000001504746622e+30f, -1.00000001504746622e+30f);
        var _S91 : vec2<i32> = q_4 + vec2<i32>(i32(-1), i32(-1));
        var _S92 : i32 = ((_S91.y + i32(5)) * i32(18) + (_S91.x + i32(5))) * i32(3);
        var s_27 : vec3<f32> = vec3<f32>(sMid_0[_S92], sMid_0[_S92 + i32(1)], sMid_0[_S92 + i32(2)]);
        var _S93 : vec2<i32> = q_4 + vec2<i32>(i32(0), i32(-1));
        var _S94 : i32 = ((_S93.y + i32(5)) * i32(18) + (_S93.x + i32(5))) * i32(3);
        var s_28 : vec3<f32> = vec3<f32>(sMid_0[_S94], sMid_0[_S94 + i32(1)], sMid_0[_S94 + i32(2)]);
        var _S95 : vec2<i32> = q_4 + vec2<i32>(i32(1), i32(-1));
        var _S96 : i32 = ((_S95.y + i32(5)) * i32(18) + (_S95.x + i32(5))) * i32(3);
        var s_29 : vec3<f32> = vec3<f32>(sMid_0[_S96], sMid_0[_S96 + i32(1)], sMid_0[_S96 + i32(2)]);
        var _S97 : vec2<i32> = q_4 + vec2<i32>(i32(-1), i32(0));
        var _S98 : i32 = ((_S97.y + i32(5)) * i32(18) + (_S97.x + i32(5))) * i32(3);
        var s_30 : vec3<f32> = vec3<f32>(sMid_0[_S98], sMid_0[_S98 + i32(1)], sMid_0[_S98 + i32(2)]);
        var _S99 : i32 = q_4.y;
        var _S100 : i32 = q_4.x;
        var _S101 : i32 = ((_S99 + i32(5)) * i32(18) + (_S100 + i32(5))) * i32(3);
        var s_31 : vec3<f32> = vec3<f32>(sMid_0[_S101], sMid_0[_S101 + i32(1)], sMid_0[_S101 + i32(2)]);
        var _S102 : vec2<i32> = q_4 + vec2<i32>(i32(1), i32(0));
        var _S103 : i32 = ((_S102.y + i32(5)) * i32(18) + (_S102.x + i32(5))) * i32(3);
        var s_32 : vec3<f32> = vec3<f32>(sMid_0[_S103], sMid_0[_S103 + i32(1)], sMid_0[_S103 + i32(2)]);
        var _S104 : vec2<i32> = q_4 + vec2<i32>(i32(-1), i32(1));
        var _S105 : i32 = ((_S104.y + i32(5)) * i32(18) + (_S104.x + i32(5))) * i32(3);
        var s_33 : vec3<f32> = vec3<f32>(sMid_0[_S105], sMid_0[_S105 + i32(1)], sMid_0[_S105 + i32(2)]);
        var _S106 : vec2<i32> = q_4 + vec2<i32>(i32(0), i32(1));
        var _S107 : i32 = ((_S106.y + i32(5)) * i32(18) + (_S106.x + i32(5))) * i32(3);
        var s_34 : vec3<f32> = vec3<f32>(sMid_0[_S107], sMid_0[_S107 + i32(1)], sMid_0[_S107 + i32(2)]);
        var _S108 : vec2<i32> = q_4 + vec2<i32>(i32(1), i32(1));
        var _S109 : i32 = ((_S108.y + i32(5)) * i32(18) + (_S108.x + i32(5))) * i32(3);
        var s_35 : vec3<f32> = vec3<f32>(sMid_0[_S109], sMid_0[_S109 + i32(1)], sMid_0[_S109 + i32(2)]);
        var at_4 : i32 = ((_S99 + i32(4)) * i32(16) + (_S100 + i32(4))) * i32(3);
        var _S110 : i32 = ((_S99 + i32(6)) * i32(20) + (_S100 + i32(6))) * i32(3);
        var v_4 : vec3<f32> = clamp(vec3<f32>(sHistory_0[_S110], sHistory_0[_S110 + i32(1)], sHistory_0[_S110 + i32(2)]), min(min(min(min(min(min(min(min(min(_S89, s_27), s_28), s_29), s_30), s_31), s_32), s_33), s_34), s_35), max(max(max(max(max(max(max(max(max(_S90, s_27), s_28), s_29), s_30), s_31), s_32), s_33), s_34), s_35));
        sHistClamped_0[at_4] = v_4.x;
        sHistClamped_0[at_4 + i32(1)] = v_4.y;
        sHistClamped_0[at_4 + i32(2)] = v_4.z;
        i_1 = i_1 + u32(64);
    }
    workgroupBarrier();
    i_1 = lid_0;
    for(;;)
    {
        if(i_1 < (RegionCount_0(i32(3))))
        {
        }
        else
        {
            break;
        }
        var q_5 : vec2<i32> = RegionCell_0(i_1, i32(3));
        var _S111 : vec2<i32> = q_5 + vec2<i32>(i32(-1), i32(-1));
        var _S112 : i32 = ((_S111.y + i32(4)) * i32(16) + (_S111.x + i32(4))) * i32(3);
        var _S113 : i32 = _S112 + i32(1);
        var _S114 : i32 = _S112 + i32(2);
        var _S115 : vec3<f32> = vec3<f32>(Binomial_0(i32(-1), i32(-1)));
        var _S116 : vec2<i32> = q_5 + vec2<i32>(i32(0), i32(-1));
        var _S117 : i32 = ((_S116.y + i32(4)) * i32(16) + (_S116.x + i32(4))) * i32(3);
        var _S118 : i32 = _S117 + i32(1);
        var _S119 : i32 = _S117 + i32(2);
        var _S120 : vec3<f32> = vec3<f32>(Binomial_0(i32(0), i32(-1)));
        var _S121 : vec2<i32> = q_5 + vec2<i32>(i32(1), i32(-1));
        var _S122 : i32 = ((_S121.y + i32(4)) * i32(16) + (_S121.x + i32(4))) * i32(3);
        var _S123 : i32 = _S122 + i32(1);
        var _S124 : i32 = _S122 + i32(2);
        var _S125 : vec3<f32> = vec3<f32>(Binomial_0(i32(1), i32(-1)));
        var _S126 : vec2<i32> = q_5 + vec2<i32>(i32(-1), i32(0));
        var _S127 : i32 = ((_S126.y + i32(4)) * i32(16) + (_S126.x + i32(4))) * i32(3);
        var _S128 : i32 = _S127 + i32(1);
        var _S129 : i32 = _S127 + i32(2);
        var _S130 : vec3<f32> = vec3<f32>(Binomial_0(i32(-1), i32(0)));
        var _S131 : i32 = q_5.y;
        var _S132 : i32 = q_5.x;
        var _S133 : i32 = ((_S131 + i32(4)) * i32(16) + (_S132 + i32(4))) * i32(3);
        var _S134 : i32 = _S133 + i32(1);
        var _S135 : i32 = _S133 + i32(2);
        var _S136 : vec3<f32> = vec3<f32>(Binomial_0(i32(0), i32(0)));
        var _S137 : vec2<i32> = q_5 + vec2<i32>(i32(1), i32(0));
        var _S138 : i32 = ((_S137.y + i32(4)) * i32(16) + (_S137.x + i32(4))) * i32(3);
        var _S139 : i32 = _S138 + i32(1);
        var _S140 : i32 = _S138 + i32(2);
        var _S141 : vec3<f32> = vec3<f32>(Binomial_0(i32(1), i32(0)));
        var _S142 : vec2<i32> = q_5 + vec2<i32>(i32(-1), i32(1));
        var _S143 : i32 = ((_S142.y + i32(4)) * i32(16) + (_S142.x + i32(4))) * i32(3);
        var _S144 : i32 = _S143 + i32(1);
        var _S145 : i32 = _S143 + i32(2);
        var _S146 : vec3<f32> = vec3<f32>(Binomial_0(i32(-1), i32(1)));
        var _S147 : vec2<i32> = q_5 + vec2<i32>(i32(0), i32(1));
        var _S148 : i32 = ((_S147.y + i32(4)) * i32(16) + (_S147.x + i32(4))) * i32(3);
        var _S149 : i32 = _S148 + i32(1);
        var _S150 : i32 = _S148 + i32(2);
        var _S151 : vec3<f32> = vec3<f32>(Binomial_0(i32(0), i32(1)));
        var _S152 : vec2<i32> = q_5 + vec2<i32>(i32(1), i32(1));
        var _S153 : i32 = ((_S152.y + i32(4)) * i32(16) + (_S152.x + i32(4))) * i32(3);
        var _S154 : i32 = _S153 + i32(1);
        var _S155 : i32 = _S153 + i32(2);
        var _S156 : vec3<f32> = vec3<f32>(Binomial_0(i32(1), i32(1)));
        var blurIn_0 : vec3<f32> = vec3<f32>(sInClamped_0[_S112], sInClamped_0[_S113], sInClamped_0[_S114]) * _S115 + vec3<f32>(sInClamped_0[_S117], sInClamped_0[_S118], sInClamped_0[_S119]) * _S120 + vec3<f32>(sInClamped_0[_S122], sInClamped_0[_S123], sInClamped_0[_S124]) * _S125 + vec3<f32>(sInClamped_0[_S127], sInClamped_0[_S128], sInClamped_0[_S129]) * _S130 + vec3<f32>(sInClamped_0[_S133], sInClamped_0[_S134], sInClamped_0[_S135]) * _S136 + vec3<f32>(sInClamped_0[_S138], sInClamped_0[_S139], sInClamped_0[_S140]) * _S141 + vec3<f32>(sInClamped_0[_S143], sInClamped_0[_S144], sInClamped_0[_S145]) * _S146 + vec3<f32>(sInClamped_0[_S148], sInClamped_0[_S149], sInClamped_0[_S150]) * _S151 + vec3<f32>(sInClamped_0[_S153], sInClamped_0[_S154], sInClamped_0[_S155]) * _S156;
        var blurHist_0 : vec3<f32> = vec3<f32>(sHistClamped_0[_S112], sHistClamped_0[_S113], sHistClamped_0[_S114]) * _S115 + vec3<f32>(sHistClamped_0[_S117], sHistClamped_0[_S118], sHistClamped_0[_S119]) * _S120 + vec3<f32>(sHistClamped_0[_S122], sHistClamped_0[_S123], sHistClamped_0[_S124]) * _S125 + vec3<f32>(sHistClamped_0[_S127], sHistClamped_0[_S128], sHistClamped_0[_S129]) * _S130 + vec3<f32>(sHistClamped_0[_S133], sHistClamped_0[_S134], sHistClamped_0[_S135]) * _S136 + vec3<f32>(sHistClamped_0[_S138], sHistClamped_0[_S139], sHistClamped_0[_S140]) * _S141 + vec3<f32>(sHistClamped_0[_S143], sHistClamped_0[_S144], sHistClamped_0[_S145]) * _S146 + vec3<f32>(sHistClamped_0[_S148], sHistClamped_0[_S149], sHistClamped_0[_S150]) * _S151 + vec3<f32>(sHistClamped_0[_S153], sHistClamped_0[_S154], sHistClamped_0[_S155]) * _S156;
        sBlur_0[_S133] = blurIn_0.x;
        sBlur_0[_S134] = blurIn_0.y;
        sBlur_0[_S135] = blurIn_0.z;
        var at_5 : i32 = ((_S131 + i32(5)) * i32(18) + (_S132 + i32(5))) * i32(3);
        sMid_0[at_5] = blurHist_0.x;
        sMid_0[at_5 + i32(1)] = blurHist_0.y;
        sMid_0[at_5 + i32(2)] = blurHist_0.z;
        i_1 = i_1 + u32(64);
    }
    workgroupBarrier();
    const _S157 : vec3<f32> = vec3<f32>(1.00000001504746622e+30f, 1.00000001504746622e+30f, 1.00000001504746622e+30f);
    const _S158 : vec3<f32> = vec3<f32>(-1.00000001504746622e+30f, -1.00000001504746622e+30f, -1.00000001504746622e+30f);
    const _S159 : vec2<i32> = vec2<i32>(i32(-1), i32(-1));
    var _S160 : vec2<i32> = _S8 + _S159;
    var _S161 : i32 = ((_S160.y + i32(4)) * i32(16) + (_S160.x + i32(4))) * i32(3);
    var _S162 : i32 = _S161 + i32(1);
    var _S163 : i32 = _S161 + i32(2);
    var s_36 : vec3<f32> = vec3<f32>(sInClamped_0[_S161], sInClamped_0[_S162], sInClamped_0[_S163]);
    const _S164 : vec2<i32> = vec2<i32>(i32(0), i32(-1));
    var _S165 : vec2<i32> = _S8 + _S164;
    var _S166 : i32 = ((_S165.y + i32(4)) * i32(16) + (_S165.x + i32(4))) * i32(3);
    var _S167 : i32 = _S166 + i32(1);
    var _S168 : i32 = _S166 + i32(2);
    var s_37 : vec3<f32> = vec3<f32>(sInClamped_0[_S166], sInClamped_0[_S167], sInClamped_0[_S168]);
    const _S169 : vec2<i32> = vec2<i32>(i32(1), i32(-1));
    var _S170 : vec2<i32> = _S8 + _S169;
    var _S171 : i32 = ((_S170.y + i32(4)) * i32(16) + (_S170.x + i32(4))) * i32(3);
    var _S172 : i32 = _S171 + i32(1);
    var _S173 : i32 = _S171 + i32(2);
    var s_38 : vec3<f32> = vec3<f32>(sInClamped_0[_S171], sInClamped_0[_S172], sInClamped_0[_S173]);
    const _S174 : vec2<i32> = vec2<i32>(i32(-1), i32(0));
    var _S175 : vec2<i32> = _S8 + _S174;
    var _S176 : i32 = ((_S175.y + i32(4)) * i32(16) + (_S175.x + i32(4))) * i32(3);
    var _S177 : i32 = _S176 + i32(1);
    var _S178 : i32 = _S176 + i32(2);
    var s_39 : vec3<f32> = vec3<f32>(sInClamped_0[_S176], sInClamped_0[_S177], sInClamped_0[_S178]);
    var _S179 : i32 = ((_S14 + i32(4)) * i32(16) + (_S15 + i32(4))) * i32(3);
    var _S180 : i32 = _S179 + i32(1);
    var _S181 : i32 = _S179 + i32(2);
    var s_40 : vec3<f32> = vec3<f32>(sInClamped_0[_S179], sInClamped_0[_S180], sInClamped_0[_S181]);
    const _S182 : vec2<i32> = vec2<i32>(i32(1), i32(0));
    var _S183 : vec2<i32> = _S8 + _S182;
    var _S184 : i32 = ((_S183.y + i32(4)) * i32(16) + (_S183.x + i32(4))) * i32(3);
    var _S185 : i32 = _S184 + i32(1);
    var _S186 : i32 = _S184 + i32(2);
    var s_41 : vec3<f32> = vec3<f32>(sInClamped_0[_S184], sInClamped_0[_S185], sInClamped_0[_S186]);
    const _S187 : vec2<i32> = vec2<i32>(i32(-1), i32(1));
    var _S188 : vec2<i32> = _S8 + _S187;
    var _S189 : i32 = ((_S188.y + i32(4)) * i32(16) + (_S188.x + i32(4))) * i32(3);
    var _S190 : i32 = _S189 + i32(1);
    var _S191 : i32 = _S189 + i32(2);
    var s_42 : vec3<f32> = vec3<f32>(sInClamped_0[_S189], sInClamped_0[_S190], sInClamped_0[_S191]);
    const _S192 : vec2<i32> = vec2<i32>(i32(0), i32(1));
    var _S193 : vec2<i32> = _S8 + _S192;
    var _S194 : i32 = ((_S193.y + i32(4)) * i32(16) + (_S193.x + i32(4))) * i32(3);
    var _S195 : i32 = _S194 + i32(1);
    var _S196 : i32 = _S194 + i32(2);
    var s_43 : vec3<f32> = vec3<f32>(sInClamped_0[_S194], sInClamped_0[_S195], sInClamped_0[_S196]);
    const _S197 : vec2<i32> = vec2<i32>(i32(1), i32(1));
    var _S198 : vec2<i32> = _S8 + _S197;
    var _S199 : i32 = ((_S198.y + i32(4)) * i32(16) + (_S198.x + i32(4))) * i32(3);
    var _S200 : i32 = _S199 + i32(1);
    var _S201 : i32 = _S199 + i32(2);
    var s_44 : vec3<f32> = vec3<f32>(sInClamped_0[_S199], sInClamped_0[_S200], sInClamped_0[_S201]);
    var _S202 : i32 = ((_S14 + i32(5)) * i32(18) + (_S15 + i32(5))) * i32(3);
    var disagreement_0 : vec3<f32> = max(abs(vec3<f32>(sBlur_0[_S179], sBlur_0[_S180], sBlur_0[_S181]) - vec3<f32>(sMid_0[_S202], sMid_0[_S202 + i32(1)], sMid_0[_S202 + i32(2)])), (max(max(max(max(max(max(max(max(max(_S158, s_36), s_37), s_38), s_39), s_40), s_41), s_42), s_43), s_44) - min(min(min(min(min(min(min(min(min(_S157, s_36), s_37), s_38), s_39), s_40), s_41), s_42), s_43), s_44)) * vec3<f32>(0.25f) + vec3<f32>(0.000244140625f));
    i_1 = lid_0;
    for(;;)
    {
        if(i_1 < (RegionCount_0(i32(2))))
        {
        }
        else
        {
            break;
        }
        var q_6 : vec2<i32> = RegionCell_0(i_1, i32(2));
        var _S203 : i32 = q_6.y;
        var _S204 : i32 = q_6.x;
        var _S205 : i32 = ((_S203 + i32(5)) * i32(18) + (_S204 + i32(5))) * i32(3);
        var fh_0 : vec3<f32> = vec3<f32>(sMid_0[_S205], sMid_0[_S205 + i32(1)], sMid_0[_S205 + i32(2)]);
        var _S206 : i32 = ((_S203 + i32(4)) * i32(16) + (_S204 + i32(4))) * i32(3);
        var _S207 : i32 = _S206 + i32(1);
        var _S208 : i32 = _S206 + i32(2);
        var inC_0 : vec3<f32> = vec3<f32>(sInClamped_0[_S206], sInClamped_0[_S207], sInClamped_0[_S208]);
        var _S209 : i32 = ((_S203 + i32(6)) * i32(20) + (_S204 + i32(6))) * i32(3);
        var _S210 : i32 = _S209 + i32(1);
        var _S211 : i32 = _S209 + i32(2);
        var inRaw_0 : vec3<f32> = vec3<f32>(sInput_0[_S209], sInput_0[_S210], sInput_0[_S211]);
        var r_2 : vec2<i32> = q_6 + _S159;
        var _S212 : i32 = r_2.y;
        var _S213 : i32 = r_2.x;
        var _S214 : i32 = ((_S212 + i32(4)) * i32(16) + (_S213 + i32(4))) * i32(3);
        var _S215 : i32 = _S214 + i32(1);
        var _S216 : i32 = _S214 + i32(2);
        var c_0 : vec3<f32> = vec3<f32>(sInClamped_0[_S214], sInClamped_0[_S215], sInClamped_0[_S216]);
        var b_1 : vec3<f32> = vec3<f32>(sBlur_0[_S214], sBlur_0[_S215], sBlur_0[_S216]);
        var _S217 : i32 = ((_S212 + i32(6)) * i32(20) + (_S213 + i32(6))) * i32(3);
        var r_3 : vec2<i32> = q_6 + _S164;
        var _S218 : i32 = r_3.y;
        var _S219 : i32 = r_3.x;
        var _S220 : i32 = ((_S218 + i32(4)) * i32(16) + (_S219 + i32(4))) * i32(3);
        var _S221 : i32 = _S220 + i32(1);
        var _S222 : i32 = _S220 + i32(2);
        var c_1 : vec3<f32> = vec3<f32>(sInClamped_0[_S220], sInClamped_0[_S221], sInClamped_0[_S222]);
        var b_2 : vec3<f32> = vec3<f32>(sBlur_0[_S220], sBlur_0[_S221], sBlur_0[_S222]);
        var _S223 : i32 = ((_S218 + i32(6)) * i32(20) + (_S219 + i32(6))) * i32(3);
        var r_4 : vec2<i32> = q_6 + _S169;
        var _S224 : i32 = r_4.y;
        var _S225 : i32 = r_4.x;
        var _S226 : i32 = ((_S224 + i32(4)) * i32(16) + (_S225 + i32(4))) * i32(3);
        var _S227 : i32 = _S226 + i32(1);
        var _S228 : i32 = _S226 + i32(2);
        var c_2 : vec3<f32> = vec3<f32>(sInClamped_0[_S226], sInClamped_0[_S227], sInClamped_0[_S228]);
        var b_3 : vec3<f32> = vec3<f32>(sBlur_0[_S226], sBlur_0[_S227], sBlur_0[_S228]);
        var _S229 : i32 = ((_S224 + i32(6)) * i32(20) + (_S225 + i32(6))) * i32(3);
        var r_5 : vec2<i32> = q_6 + _S174;
        var _S230 : i32 = r_5.y;
        var _S231 : i32 = r_5.x;
        var _S232 : i32 = ((_S230 + i32(4)) * i32(16) + (_S231 + i32(4))) * i32(3);
        var _S233 : i32 = _S232 + i32(1);
        var _S234 : i32 = _S232 + i32(2);
        var c_3 : vec3<f32> = vec3<f32>(sInClamped_0[_S232], sInClamped_0[_S233], sInClamped_0[_S234]);
        var b_4 : vec3<f32> = vec3<f32>(sBlur_0[_S232], sBlur_0[_S233], sBlur_0[_S234]);
        var _S235 : i32 = ((_S230 + i32(6)) * i32(20) + (_S231 + i32(6))) * i32(3);
        var c_4 : vec3<f32> = vec3<f32>(sInClamped_0[_S206], sInClamped_0[_S207], sInClamped_0[_S208]);
        var b_5 : vec3<f32> = vec3<f32>(sBlur_0[_S206], sBlur_0[_S207], sBlur_0[_S208]);
        var r_6 : vec2<i32> = q_6 + _S182;
        var _S236 : i32 = r_6.y;
        var _S237 : i32 = r_6.x;
        var _S238 : i32 = ((_S236 + i32(4)) * i32(16) + (_S237 + i32(4))) * i32(3);
        var _S239 : i32 = _S238 + i32(1);
        var _S240 : i32 = _S238 + i32(2);
        var c_5 : vec3<f32> = vec3<f32>(sInClamped_0[_S238], sInClamped_0[_S239], sInClamped_0[_S240]);
        var b_6 : vec3<f32> = vec3<f32>(sBlur_0[_S238], sBlur_0[_S239], sBlur_0[_S240]);
        var _S241 : i32 = ((_S236 + i32(6)) * i32(20) + (_S237 + i32(6))) * i32(3);
        var r_7 : vec2<i32> = q_6 + _S187;
        var _S242 : i32 = r_7.y;
        var _S243 : i32 = r_7.x;
        var _S244 : i32 = ((_S242 + i32(4)) * i32(16) + (_S243 + i32(4))) * i32(3);
        var _S245 : i32 = _S244 + i32(1);
        var _S246 : i32 = _S244 + i32(2);
        var c_6 : vec3<f32> = vec3<f32>(sInClamped_0[_S244], sInClamped_0[_S245], sInClamped_0[_S246]);
        var b_7 : vec3<f32> = vec3<f32>(sBlur_0[_S244], sBlur_0[_S245], sBlur_0[_S246]);
        var _S247 : i32 = ((_S242 + i32(6)) * i32(20) + (_S243 + i32(6))) * i32(3);
        var r_8 : vec2<i32> = q_6 + _S192;
        var _S248 : i32 = r_8.y;
        var _S249 : i32 = r_8.x;
        var _S250 : i32 = ((_S248 + i32(4)) * i32(16) + (_S249 + i32(4))) * i32(3);
        var _S251 : i32 = _S250 + i32(1);
        var _S252 : i32 = _S250 + i32(2);
        var c_7 : vec3<f32> = vec3<f32>(sInClamped_0[_S250], sInClamped_0[_S251], sInClamped_0[_S252]);
        var b_8 : vec3<f32> = vec3<f32>(sBlur_0[_S250], sBlur_0[_S251], sBlur_0[_S252]);
        var _S253 : i32 = ((_S248 + i32(6)) * i32(20) + (_S249 + i32(6))) * i32(3);
        var r_9 : vec2<i32> = q_6 + _S197;
        var _S254 : i32 = r_9.y;
        var _S255 : i32 = r_9.x;
        var _S256 : i32 = ((_S254 + i32(4)) * i32(16) + (_S255 + i32(4))) * i32(3);
        var _S257 : i32 = _S256 + i32(1);
        var _S258 : i32 = _S256 + i32(2);
        var c_8 : vec3<f32> = vec3<f32>(sInClamped_0[_S256], sInClamped_0[_S257], sInClamped_0[_S258]);
        var b_9 : vec3<f32> = vec3<f32>(sBlur_0[_S256], sBlur_0[_S257], sBlur_0[_S258]);
        var inLo_0 : vec3<f32> = min(min(min(min(min(min(min(min(min(_S157, c_0), c_1), c_2), c_3), c_4), c_5), c_6), c_7), c_8);
        var inHi_0 : vec3<f32> = max(max(max(max(max(max(max(max(max(_S158, c_0), c_1), c_2), c_3), c_4), c_5), c_6), c_7), c_8);
        var boxLo_0 : vec3<f32> = min(min(min(min(min(min(min(min(min(_S157, b_1), b_2), b_3), b_4), b_5), b_6), b_7), b_8), b_9);
        var boxHi_0 : vec3<f32> = max(max(max(max(max(max(max(max(max(_S158, b_1), b_2), b_3), b_4), b_5), b_6), b_7), b_8), b_9);
        var sumIn_0 : vec3<f32> = c_0 + c_1 + c_2 + c_3 + c_4 + c_5 + c_6 + c_7 + c_8;
        var _S259 : i32 = ((_S254 + i32(6)) * i32(20) + (_S255 + i32(6))) * i32(3);
        var sumDiff_0 : vec3<f32> = abs(vec3<f32>(sInput_0[_S217], sInput_0[_S217 + i32(1)], sInput_0[_S217 + i32(2)]) - c_0) + abs(vec3<f32>(sInput_0[_S223], sInput_0[_S223 + i32(1)], sInput_0[_S223 + i32(2)]) - c_1) + abs(vec3<f32>(sInput_0[_S229], sInput_0[_S229 + i32(1)], sInput_0[_S229 + i32(2)]) - c_2) + abs(vec3<f32>(sInput_0[_S235], sInput_0[_S235 + i32(1)], sInput_0[_S235 + i32(2)]) - c_3) + abs(vec3<f32>(sInput_0[_S209], sInput_0[_S210], sInput_0[_S211]) - c_4) + abs(vec3<f32>(sInput_0[_S241], sInput_0[_S241 + i32(1)], sInput_0[_S241 + i32(2)]) - c_5) + abs(vec3<f32>(sInput_0[_S247], sInput_0[_S247 + i32(1)], sInput_0[_S247 + i32(2)]) - c_6) + abs(vec3<f32>(sInput_0[_S253], sInput_0[_S253 + i32(1)], sInput_0[_S253 + i32(2)]) - c_7) + abs(vec3<f32>(sInput_0[_S259], sInput_0[_S259 + i32(1)], sInput_0[_S259 + i32(2)]) - c_8);
        var boxLo_1 : vec3<f32>;
        var boxHi_1 : vec3<f32>;
        if((p_0.control_0.w) > 0.0f)
        {
            var _S260 : vec3<i32> = vec3<i32>(clamp(origin_0 + q_6, vec2<i32>(i32(0), i32(0)), _S7), i32(0));
            var relax_0 : f32 = saturate((textureLoad((uThinGeometry_0), ((_S260)).xy, ((_S260)).z)).y * p_0.control_0.w);
            if(relax_0 > 0.0078740157186985f)
            {
                var _S261 : i32 = ((_S212 + i32(5)) * i32(18) + (_S213 + i32(5))) * i32(3);
                var b_10 : vec3<f32> = vec3<f32>(sMid_0[_S261], sMid_0[_S261 + i32(1)], sMid_0[_S261 + i32(2)]);
                var _S262 : i32 = ((_S218 + i32(5)) * i32(18) + (_S219 + i32(5))) * i32(3);
                var b_11 : vec3<f32> = vec3<f32>(sMid_0[_S262], sMid_0[_S262 + i32(1)], sMid_0[_S262 + i32(2)]);
                var _S263 : i32 = ((_S224 + i32(5)) * i32(18) + (_S225 + i32(5))) * i32(3);
                var b_12 : vec3<f32> = vec3<f32>(sMid_0[_S263], sMid_0[_S263 + i32(1)], sMid_0[_S263 + i32(2)]);
                var _S264 : i32 = ((_S230 + i32(5)) * i32(18) + (_S231 + i32(5))) * i32(3);
                var b_13 : vec3<f32> = vec3<f32>(sMid_0[_S264], sMid_0[_S264 + i32(1)], sMid_0[_S264 + i32(2)]);
                var b_14 : vec3<f32> = vec3<f32>(sMid_0[_S205], sMid_0[_S205 + i32(1)], sMid_0[_S205 + i32(2)]);
                var _S265 : i32 = ((_S236 + i32(5)) * i32(18) + (_S237 + i32(5))) * i32(3);
                var b_15 : vec3<f32> = vec3<f32>(sMid_0[_S265], sMid_0[_S265 + i32(1)], sMid_0[_S265 + i32(2)]);
                var _S266 : i32 = ((_S242 + i32(5)) * i32(18) + (_S243 + i32(5))) * i32(3);
                var b_16 : vec3<f32> = vec3<f32>(sMid_0[_S266], sMid_0[_S266 + i32(1)], sMid_0[_S266 + i32(2)]);
                var _S267 : i32 = ((_S248 + i32(5)) * i32(18) + (_S249 + i32(5))) * i32(3);
                var b_17 : vec3<f32> = vec3<f32>(sMid_0[_S267], sMid_0[_S267 + i32(1)], sMid_0[_S267 + i32(2)]);
                var _S268 : i32 = ((_S254 + i32(5)) * i32(18) + (_S255 + i32(5))) * i32(3);
                var b_18 : vec3<f32> = vec3<f32>(sMid_0[_S268], sMid_0[_S268 + i32(1)], sMid_0[_S268 + i32(2)]);
                var _S269 : vec3<f32> = vec3<f32>(relax_0);
                var _S270 : vec3<f32> = mix(boxHi_0, max(boxHi_0, max(max(max(max(max(max(max(max(max(_S158, b_10), b_11), b_12), b_13), b_14), b_15), b_16), b_17), b_18)), _S269);
                boxLo_1 = mix(boxLo_0, min(boxLo_0, min(min(min(min(min(min(min(min(min(_S157, b_10), b_11), b_12), b_13), b_14), b_15), b_16), b_17), b_18)), _S269);
                boxHi_1 = _S270;
            }
            else
            {
                boxLo_1 = boxLo_0;
                boxHi_1 = boxHi_0;
            }
        }
        else
        {
            boxLo_1 = boxLo_0;
            boxHi_1 = boxHi_0;
        }
        var _S271 : vec3<f32> = vec3<f32>(9.0f);
        var _S272 : vec3<f32> = vec3<f32>(8.0f);
        var err_0 : vec3<f32> = max(max(vec3<f32>(0.00048828125f), min(abs((_S271 * abs(inRaw_0 - inC_0) - sumDiff_0) / _S272), abs((_S271 * inC_0 - sumIn_0) / _S272))), (inHi_0 - inLo_0) / vec3<f32>(16.0f)) + vec3<f32>(0.00048828125f);
        var removed_0 : vec3<f32> = abs(clamp(fh_0, boxLo_1 - err_0, boxHi_1 + err_0) - fh_0);
        sHistory_0[_S209] = removed_0.x;
        sHistory_0[_S210] = removed_0.y;
        sHistory_0[_S211] = removed_0.z;
        i_1 = i_1 + u32(64);
    }
    workgroupBarrier();
    i_1 = lid_0;
    for(;;)
    {
        if(i_1 < (RegionCount_0(i32(1))))
        {
        }
        else
        {
            break;
        }
        var q_7 : vec2<i32> = RegionCell_0(i_1, i32(1));
        var values_0 : array<vec3<f32>, i32(9)>;
        var _S273 : vec2<i32> = q_7 + _S159;
        var _S274 : i32 = ((_S273.y + i32(6)) * i32(20) + (_S273.x + i32(6))) * i32(3);
        values_0[i32(0)] = vec3<f32>(sHistory_0[_S274], sHistory_0[_S274 + i32(1)], sHistory_0[_S274 + i32(2)]);
        var _S275 : vec2<i32> = q_7 + _S164;
        var _S276 : i32 = ((_S275.y + i32(6)) * i32(20) + (_S275.x + i32(6))) * i32(3);
        values_0[i32(1)] = vec3<f32>(sHistory_0[_S276], sHistory_0[_S276 + i32(1)], sHistory_0[_S276 + i32(2)]);
        var _S277 : vec2<i32> = q_7 + _S169;
        var _S278 : i32 = ((_S277.y + i32(6)) * i32(20) + (_S277.x + i32(6))) * i32(3);
        values_0[i32(2)] = vec3<f32>(sHistory_0[_S278], sHistory_0[_S278 + i32(1)], sHistory_0[_S278 + i32(2)]);
        var _S279 : vec2<i32> = q_7 + _S174;
        var _S280 : i32 = ((_S279.y + i32(6)) * i32(20) + (_S279.x + i32(6))) * i32(3);
        values_0[i32(3)] = vec3<f32>(sHistory_0[_S280], sHistory_0[_S280 + i32(1)], sHistory_0[_S280 + i32(2)]);
        var _S281 : i32 = q_7.y;
        var _S282 : i32 = q_7.x;
        var _S283 : i32 = ((_S281 + i32(6)) * i32(20) + (_S282 + i32(6))) * i32(3);
        values_0[i32(4)] = vec3<f32>(sHistory_0[_S283], sHistory_0[_S283 + i32(1)], sHistory_0[_S283 + i32(2)]);
        var _S284 : vec2<i32> = q_7 + _S182;
        var _S285 : i32 = ((_S284.y + i32(6)) * i32(20) + (_S284.x + i32(6))) * i32(3);
        values_0[i32(5)] = vec3<f32>(sHistory_0[_S285], sHistory_0[_S285 + i32(1)], sHistory_0[_S285 + i32(2)]);
        var _S286 : vec2<i32> = q_7 + _S187;
        var _S287 : i32 = ((_S286.y + i32(6)) * i32(20) + (_S286.x + i32(6))) * i32(3);
        values_0[i32(6)] = vec3<f32>(sHistory_0[_S287], sHistory_0[_S287 + i32(1)], sHistory_0[_S287 + i32(2)]);
        var _S288 : vec2<i32> = q_7 + _S192;
        var _S289 : i32 = ((_S288.y + i32(6)) * i32(20) + (_S288.x + i32(6))) * i32(3);
        values_0[i32(7)] = vec3<f32>(sHistory_0[_S289], sHistory_0[_S289 + i32(1)], sHistory_0[_S289 + i32(2)]);
        var _S290 : vec2<i32> = q_7 + _S197;
        var _S291 : i32 = ((_S290.y + i32(6)) * i32(20) + (_S290.x + i32(6))) * i32(3);
        values_0[i32(8)] = vec3<f32>(sHistory_0[_S291], sHistory_0[_S291 + i32(1)], sHistory_0[_S291 + i32(2)]);
        var at_6 : i32 = ((_S281 + i32(4)) * i32(16) + (_S282 + i32(4))) * i32(3);
        var v_5 : vec3<f32> = Median9_0(values_0);
        sBlur_0[at_6] = v_5.x;
        sBlur_0[at_6 + i32(1)] = v_5.y;
        sBlur_0[at_6 + i32(2)] = v_5.z;
        i_1 = i_1 + u32(64);
    }
    workgroupBarrier();
    var _S292 : bool;
    if((px_0.x) >= (_S6.x))
    {
        _S292 = true;
    }
    else
    {
        _S292 = (px_0.y) >= (_S6.y);
    }
    if(_S292)
    {
        return;
    }
    const _S293 : vec3<f32> = vec3<f32>(0.0f, 0.0f, 0.0f);
    var factor_0 : vec3<f32> = saturate(vec3<f32>(1.0f) - max(max(max(max(max(max(max(max(max(_S293, vec3<f32>(sBlur_0[_S161], sBlur_0[_S162], sBlur_0[_S163])), vec3<f32>(sBlur_0[_S166], sBlur_0[_S167], sBlur_0[_S168])), vec3<f32>(sBlur_0[_S171], sBlur_0[_S172], sBlur_0[_S173])), vec3<f32>(sBlur_0[_S176], sBlur_0[_S177], sBlur_0[_S178])), vec3<f32>(sBlur_0[_S179], sBlur_0[_S180], sBlur_0[_S181])), vec3<f32>(sBlur_0[_S184], sBlur_0[_S185], sBlur_0[_S186])), vec3<f32>(sBlur_0[_S189], sBlur_0[_S190], sBlur_0[_S191])), vec3<f32>(sBlur_0[_S194], sBlur_0[_S195], sBlur_0[_S196])), vec3<f32>(sBlur_0[_S199], sBlur_0[_S200], sBlur_0[_S201])) / max(disagreement_0, vec3<f32>(9.99999997475242708e-07f)));
    var _S294 : f32 = min(factor_0.x, min(factor_0.y, factor_0.z));
    var _S295 : vec2<u32> = vec2<u32>(px_0);
    textureStore((uRejection_0), (_S295), (vec4<f32>(_S294, 0.0f, 0.0f, 0.0f)));
    var parallax_0 : f32;
    if((p_0.control_0.z) > 0.5f)
    {
        parallax_0 = (textureSampleLevel((uParallaxRejection_0), (uParallaxRejectionSampler_0), ((vec2<f32>(px_0) + vec2<f32>(0.5f)) * p_0.size_0.zw), (0.0f))).x;
    }
    else
    {
        parallax_0 = 1.0f;
    }
    var total_0 : f32;
    if(parallax_0 < 0.5f)
    {
        total_0 = 1.0f;
    }
    else
    {
        total_0 = saturate(1.0f - 4.0f * _S294);
    }
    textureStore((uGuide_0), (_S295), (vec4<f32>(mix(historyPx_0, inputPx_0, vec3<f32>(max(p_0.control_0.y, max(1.0f - _S294, total_0)))), 1.0f)));
    return;
}

