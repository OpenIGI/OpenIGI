#version 430
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
    vec4 control;
} p;

layout(binding = 0, r8) uniform restrict writeonly image2D uRejection;
layout(binding = 1, rgba16f) uniform restrict writeonly image2D uGuide;
layout(binding = 0) uniform sampler2D _3310;
layout(binding = 1) uniform sampler2D _3313;
layout(binding = 2) uniform sampler2D _3315;
layout(binding = 4) uniform sampler2D _3318;
layout(binding = 3) uniform sampler2D _3320;

shared float sInput[1200];
shared float sHistory[1200];
shared float sMid[972];
shared float sInClamped[768];
shared float sBlur[768];

void main()
{
    {
        ivec2 _142 = ivec2(p.size.xy);
        ivec2 _145 = _142 - ivec2(1);
        ivec2 origin = ivec2(gl_WorkGroupID.xy) * ivec2(8);
        ivec2 _159 = ivec2(gl_LocalInvocationID.xy);
        ivec2 px = origin + _159;
        uint i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 400u))
            {
                break;
            }
            int _2936 = int(i);
            ivec2 _2941 = ivec2(_2936 - 20 * (_2936 / 20), _2936 / 20) - ivec2(6);
            ivec2 _200 = clamp(origin + _2941, ivec2(0), _145);
            ivec2 _205 = ivec3(_200, 0).xy;
            vec3 x = max(texelFetch(_3310, _205, 0).xyz, vec3(0.0)) * p.control.x;
            vec3 inputValue = x / (x + vec3(0.17000000178813934326171875));
            vec3 _249 = max(textureLod(_3315, clamp(((vec2(_200) + vec2(0.5)) * p.size.zw) - texelFetch(_3313, _205, 0).xy, vec2(0.0), vec2(1.0)), 0.0).xyz, vec3(0.0));
            int at_ = (((_2941.y + 6) * 20) + (_2941.x + 6)) * 3;
            sInput[at_] = inputValue.x;
            int _267 = at_ + 1;
            sInput[_267] = inputValue.y;
            int _271 = at_ + 2;
            sInput[_271] = inputValue.z;
            sHistory[at_] = _249.x;
            sHistory[_267] = _249.y;
            sHistory[_271] = _249.z;
            i += 64u;
            continue;
        }
        barrier();
        int _296 = _159.y;
        int _299 = _159.x;
        int _302 = (((_296 + 6) * 20) + (_299 + 6)) * 3;
        float _304 = sInput[_302];
        int _305 = _302 + 1;
        float _307 = sInput[_305];
        int _308 = _302 + 2;
        float _310 = sInput[_308];
        float _313 = sHistory[_302];
        float _315 = sHistory[_305];
        float _317 = sHistory[_308];
        i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 324u))
            {
                break;
            }
            int _2952 = int(i);
            ivec2 _2957 = ivec2(_2952 - 18 * (_2952 / 18), _2952 / 18) - ivec2(5);
            ivec2 _329 = _2957 + ivec2(-1);
            int _338 = (((_329.y + 6) * 20) + (_329.x + 6)) * 3;
            vec3 s = vec3(sInput[_338], sInput[_338 + 1], sInput[_338 + 2]);
            ivec2 _354 = _2957 + ivec2(0, -1);
            int _362 = (((_354.y + 6) * 20) + (_354.x + 6)) * 3;
            vec3 s_1 = vec3(sInput[_362], sInput[_362 + 1], sInput[_362 + 2]);
            ivec2 _374 = _2957 + ivec2(1, -1);
            int _382 = (((_374.y + 6) * 20) + (_374.x + 6)) * 3;
            vec3 s_2 = vec3(sInput[_382], sInput[_382 + 1], sInput[_382 + 2]);
            ivec2 _394 = _2957 + ivec2(-1, 0);
            int _402 = (((_394.y + 6) * 20) + (_394.x + 6)) * 3;
            vec3 s_3 = vec3(sInput[_402], sInput[_402 + 1], sInput[_402 + 2]);
            int _414 = _2957.y;
            int _417 = _2957.x;
            int _420 = (((_414 + 6) * 20) + (_417 + 6)) * 3;
            int _423 = _420 + 1;
            int _426 = _420 + 2;
            vec3 s_4 = vec3(sInput[_420], sInput[_423], sInput[_426]);
            ivec2 _432 = _2957 + ivec2(1, 0);
            int _440 = (((_432.y + 6) * 20) + (_432.x + 6)) * 3;
            vec3 s_5 = vec3(sInput[_440], sInput[_440 + 1], sInput[_440 + 2]);
            ivec2 _452 = _2957 + ivec2(-1, 1);
            int _460 = (((_452.y + 6) * 20) + (_452.x + 6)) * 3;
            vec3 s_6 = vec3(sInput[_460], sInput[_460 + 1], sInput[_460 + 2]);
            ivec2 _472 = _2957 + ivec2(0, 1);
            int _480 = (((_472.y + 6) * 20) + (_472.x + 6)) * 3;
            vec3 s_7 = vec3(sInput[_480], sInput[_480 + 1], sInput[_480 + 2]);
            ivec2 _492 = _2957 + ivec2(1);
            int _500 = (((_492.y + 6) * 20) + (_492.x + 6)) * 3;
            vec3 s_8 = vec3(sInput[_500], sInput[_500 + 1], sInput[_500 + 2]);
            int at_1 = (((_414 + 5) * 18) + (_417 + 5)) * 3;
            vec3 _525 = clamp(vec3(sHistory[_420], sHistory[_423], sHistory[_426]), min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), s), s_1), s_2), s_3), s_4), s_5), s_6), s_7), s_8), max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), s), s_1), s_2), s_3), s_4), s_5), s_6), s_7), s_8));
            sMid[at_1] = _525.x;
            sMid[at_1 + 1] = _525.y;
            sMid[at_1 + 2] = _525.z;
            i += 64u;
            continue;
        }
        barrier();
        i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 256u))
            {
                break;
            }
            int _2968 = int(i);
            ivec2 _2973 = ivec2(_2968 - 16 * (_2968 / 16), _2968 / 16) - ivec2(4);
            ivec2 _559 = _2973 + ivec2(-1);
            int _567 = (((_559.y + 5) * 18) + (_559.x + 5)) * 3;
            vec3 s_9 = vec3(sMid[_567], sMid[_567 + 1], sMid[_567 + 2]);
            ivec2 _581 = _2973 + ivec2(0, -1);
            int _589 = (((_581.y + 5) * 18) + (_581.x + 5)) * 3;
            vec3 s_10 = vec3(sMid[_589], sMid[_589 + 1], sMid[_589 + 2]);
            ivec2 _601 = _2973 + ivec2(1, -1);
            int _609 = (((_601.y + 5) * 18) + (_601.x + 5)) * 3;
            vec3 s_11 = vec3(sMid[_609], sMid[_609 + 1], sMid[_609 + 2]);
            ivec2 _621 = _2973 + ivec2(-1, 0);
            int _629 = (((_621.y + 5) * 18) + (_621.x + 5)) * 3;
            vec3 s_12 = vec3(sMid[_629], sMid[_629 + 1], sMid[_629 + 2]);
            int _641 = _2973.y;
            int _644 = _2973.x;
            int _647 = (((_641 + 5) * 18) + (_644 + 5)) * 3;
            vec3 s_13 = vec3(sMid[_647], sMid[_647 + 1], sMid[_647 + 2]);
            ivec2 _659 = _2973 + ivec2(1, 0);
            int _667 = (((_659.y + 5) * 18) + (_659.x + 5)) * 3;
            vec3 s_14 = vec3(sMid[_667], sMid[_667 + 1], sMid[_667 + 2]);
            ivec2 _679 = _2973 + ivec2(-1, 1);
            int _687 = (((_679.y + 5) * 18) + (_679.x + 5)) * 3;
            vec3 s_15 = vec3(sMid[_687], sMid[_687 + 1], sMid[_687 + 2]);
            ivec2 _699 = _2973 + ivec2(0, 1);
            int _707 = (((_699.y + 5) * 18) + (_699.x + 5)) * 3;
            vec3 s_16 = vec3(sMid[_707], sMid[_707 + 1], sMid[_707 + 2]);
            ivec2 _719 = _2973 + ivec2(1);
            int _727 = (((_719.y + 5) * 18) + (_719.x + 5)) * 3;
            vec3 s_17 = vec3(sMid[_727], sMid[_727 + 1], sMid[_727 + 2]);
            int at_2 = (((_641 + 4) * 16) + (_644 + 4)) * 3;
            int _749 = (((_641 + 6) * 20) + (_644 + 6)) * 3;
            vec3 _759 = clamp(vec3(sInput[_749], sInput[_749 + 1], sInput[_749 + 2]), min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), s_9), s_10), s_11), s_12), s_13), s_14), s_15), s_16), s_17), max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), s_9), s_10), s_11), s_12), s_13), s_14), s_15), s_16), s_17));
            sInClamped[at_2] = _759.x;
            sInClamped[at_2 + 1] = _759.y;
            sInClamped[at_2 + 2] = _759.z;
            i += 64u;
            continue;
        }
        barrier();
        i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 324u))
            {
                break;
            }
            int _2978 = int(i);
            ivec2 _2983 = ivec2(_2978 - 18 * (_2978 / 18), _2978 / 18) - ivec2(5);
            ivec2 _791 = _2983 + ivec2(-1);
            int _799 = (((_791.y + 6) * 20) + (_791.x + 6)) * 3;
            vec3 s_18 = vec3(sHistory[_799], sHistory[_799 + 1], sHistory[_799 + 2]);
            ivec2 _813 = _2983 + ivec2(0, -1);
            int _821 = (((_813.y + 6) * 20) + (_813.x + 6)) * 3;
            vec3 s_19 = vec3(sHistory[_821], sHistory[_821 + 1], sHistory[_821 + 2]);
            ivec2 _833 = _2983 + ivec2(1, -1);
            int _841 = (((_833.y + 6) * 20) + (_833.x + 6)) * 3;
            vec3 s_20 = vec3(sHistory[_841], sHistory[_841 + 1], sHistory[_841 + 2]);
            ivec2 _853 = _2983 + ivec2(-1, 0);
            int _861 = (((_853.y + 6) * 20) + (_853.x + 6)) * 3;
            vec3 s_21 = vec3(sHistory[_861], sHistory[_861 + 1], sHistory[_861 + 2]);
            int _873 = _2983.y;
            int _876 = _2983.x;
            int _879 = (((_873 + 6) * 20) + (_876 + 6)) * 3;
            int _882 = _879 + 1;
            int _885 = _879 + 2;
            vec3 s_22 = vec3(sHistory[_879], sHistory[_882], sHistory[_885]);
            ivec2 _891 = _2983 + ivec2(1, 0);
            int _899 = (((_891.y + 6) * 20) + (_891.x + 6)) * 3;
            vec3 s_23 = vec3(sHistory[_899], sHistory[_899 + 1], sHistory[_899 + 2]);
            ivec2 _911 = _2983 + ivec2(-1, 1);
            int _919 = (((_911.y + 6) * 20) + (_911.x + 6)) * 3;
            vec3 s_24 = vec3(sHistory[_919], sHistory[_919 + 1], sHistory[_919 + 2]);
            ivec2 _931 = _2983 + ivec2(0, 1);
            int _939 = (((_931.y + 6) * 20) + (_931.x + 6)) * 3;
            vec3 s_25 = vec3(sHistory[_939], sHistory[_939 + 1], sHistory[_939 + 2]);
            ivec2 _951 = _2983 + ivec2(1);
            int _959 = (((_951.y + 6) * 20) + (_951.x + 6)) * 3;
            vec3 s_26 = vec3(sHistory[_959], sHistory[_959 + 1], sHistory[_959 + 2]);
            int at_3 = (((_873 + 5) * 18) + (_876 + 5)) * 3;
            vec3 _983 = clamp(vec3(sInput[_879], sInput[_882], sInput[_885]), min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), s_18), s_19), s_20), s_21), s_22), s_23), s_24), s_25), s_26), max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), s_18), s_19), s_20), s_21), s_22), s_23), s_24), s_25), s_26));
            sMid[at_3] = _983.x;
            sMid[at_3 + 1] = _983.y;
            sMid[at_3 + 2] = _983.z;
            i += 64u;
            continue;
        }
        barrier();
        i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 256u))
            {
                break;
            }
            int _2988 = int(i);
            ivec2 _2993 = ivec2(_2988 - 16 * (_2988 / 16), _2988 / 16) - ivec2(4);
            ivec2 _1011 = _2993 + ivec2(-1);
            int _1019 = (((_1011.y + 5) * 18) + (_1011.x + 5)) * 3;
            vec3 s_27 = vec3(sMid[_1019], sMid[_1019 + 1], sMid[_1019 + 2]);
            ivec2 _1033 = _2993 + ivec2(0, -1);
            int _1041 = (((_1033.y + 5) * 18) + (_1033.x + 5)) * 3;
            vec3 s_28 = vec3(sMid[_1041], sMid[_1041 + 1], sMid[_1041 + 2]);
            ivec2 _1053 = _2993 + ivec2(1, -1);
            int _1061 = (((_1053.y + 5) * 18) + (_1053.x + 5)) * 3;
            vec3 s_29 = vec3(sMid[_1061], sMid[_1061 + 1], sMid[_1061 + 2]);
            ivec2 _1073 = _2993 + ivec2(-1, 0);
            int _1081 = (((_1073.y + 5) * 18) + (_1073.x + 5)) * 3;
            vec3 s_30 = vec3(sMid[_1081], sMid[_1081 + 1], sMid[_1081 + 2]);
            int _1093 = _2993.y;
            int _1096 = _2993.x;
            int _1099 = (((_1093 + 5) * 18) + (_1096 + 5)) * 3;
            vec3 s_31 = vec3(sMid[_1099], sMid[_1099 + 1], sMid[_1099 + 2]);
            ivec2 _1111 = _2993 + ivec2(1, 0);
            int _1119 = (((_1111.y + 5) * 18) + (_1111.x + 5)) * 3;
            vec3 s_32 = vec3(sMid[_1119], sMid[_1119 + 1], sMid[_1119 + 2]);
            ivec2 _1131 = _2993 + ivec2(-1, 1);
            int _1139 = (((_1131.y + 5) * 18) + (_1131.x + 5)) * 3;
            vec3 s_33 = vec3(sMid[_1139], sMid[_1139 + 1], sMid[_1139 + 2]);
            ivec2 _1151 = _2993 + ivec2(0, 1);
            int _1159 = (((_1151.y + 5) * 18) + (_1151.x + 5)) * 3;
            vec3 s_34 = vec3(sMid[_1159], sMid[_1159 + 1], sMid[_1159 + 2]);
            ivec2 _1171 = _2993 + ivec2(1);
            int _1179 = (((_1171.y + 5) * 18) + (_1171.x + 5)) * 3;
            vec3 s_35 = vec3(sMid[_1179], sMid[_1179 + 1], sMid[_1179 + 2]);
            int at_4 = (((_1093 + 6) * 20) + (_1096 + 6)) * 3;
            float _1197 = sHistory[at_4];
            int _1198 = at_4 + 1;
            float _1200 = sHistory[_1198];
            int _1201 = at_4 + 2;
            float _1203 = sHistory[_1201];
            vec3 _1205 = clamp(vec3(_1197, _1200, _1203), min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), s_27), s_28), s_29), s_30), s_31), s_32), s_33), s_34), s_35), max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), s_27), s_28), s_29), s_30), s_31), s_32), s_33), s_34), s_35));
            sHistory[at_4] = _1205.x;
            sHistory[_1198] = _1205.y;
            sHistory[_1201] = _1205.z;
            i += 64u;
            continue;
        }
        barrier();
        i = gl_LocalInvocationIndex;
        float _3010;
        float _3011;
        float _3025;
        float _3026;
        float _3040;
        float _3041;
        float _3055;
        float _3056;
        float _3070;
        float _3071;
        float _3085;
        float _3086;
        float _3100;
        float _3101;
        float _3115;
        float _3116;
        float _3130;
        float _3131;
        for (;;)
        {
            if (!(i < 196u))
            {
                break;
            }
            int _3004 = int(i);
            ivec2 _3009 = ivec2(_3004 - 14 * (_3004 / 14), _3004 / 14) - ivec2(3);
            if (false)
            {
                _3010 = 0.5;
            }
            else
            {
                _3010 = 0.25;
            }
            if (false)
            {
                _3011 = 0.5;
            }
            else
            {
                _3011 = 0.25;
            }
            float _3024 = _3010 * _3011;
            ivec2 _1260 = _3009 + ivec2(-1);
            int _1262 = _1260.y;
            int _1265 = _1260.x;
            int _1268 = (((_1262 + 4) * 16) + (_1265 + 4)) * 3;
            int _1283 = (((_1262 + 6) * 20) + (_1265 + 6)) * 3;
            if (true)
            {
                _3025 = 0.5;
            }
            else
            {
                _3025 = 0.25;
            }
            if (false)
            {
                _3026 = 0.5;
            }
            else
            {
                _3026 = 0.25;
            }
            float _3039 = _3025 * _3026;
            ivec2 _1295 = _3009 + ivec2(0, -1);
            int _1297 = _1295.y;
            int _1300 = _1295.x;
            int _1303 = (((_1297 + 4) * 16) + (_1300 + 4)) * 3;
            int _1319 = (((_1297 + 6) * 20) + (_1300 + 6)) * 3;
            if (false)
            {
                _3040 = 0.5;
            }
            else
            {
                _3040 = 0.25;
            }
            if (false)
            {
                _3041 = 0.5;
            }
            else
            {
                _3041 = 0.25;
            }
            float _3054 = _3040 * _3041;
            ivec2 _1332 = _3009 + ivec2(1, -1);
            int _1334 = _1332.y;
            int _1337 = _1332.x;
            int _1340 = (((_1334 + 4) * 16) + (_1337 + 4)) * 3;
            int _1356 = (((_1334 + 6) * 20) + (_1337 + 6)) * 3;
            if (false)
            {
                _3055 = 0.5;
            }
            else
            {
                _3055 = 0.25;
            }
            if (true)
            {
                _3056 = 0.5;
            }
            else
            {
                _3056 = 0.25;
            }
            float _3069 = _3055 * _3056;
            ivec2 _1369 = _3009 + ivec2(-1, 0);
            int _1371 = _1369.y;
            int _1374 = _1369.x;
            int _1377 = (((_1371 + 4) * 16) + (_1374 + 4)) * 3;
            int _1393 = (((_1371 + 6) * 20) + (_1374 + 6)) * 3;
            if (true)
            {
                _3070 = 0.5;
            }
            else
            {
                _3070 = 0.25;
            }
            if (true)
            {
                _3071 = 0.5;
            }
            else
            {
                _3071 = 0.25;
            }
            float _3084 = _3070 * _3071;
            int _1406 = _3009.y;
            int _1409 = _3009.x;
            int _1412 = (((_1406 + 4) * 16) + (_1409 + 4)) * 3;
            int _1415 = _1412 + 1;
            int _1418 = _1412 + 2;
            int _1428 = (((_1406 + 6) * 20) + (_1409 + 6)) * 3;
            if (false)
            {
                _3085 = 0.5;
            }
            else
            {
                _3085 = 0.25;
            }
            if (true)
            {
                _3086 = 0.5;
            }
            else
            {
                _3086 = 0.25;
            }
            float _3099 = _3085 * _3086;
            ivec2 _1441 = _3009 + ivec2(1, 0);
            int _1443 = _1441.y;
            int _1446 = _1441.x;
            int _1449 = (((_1443 + 4) * 16) + (_1446 + 4)) * 3;
            int _1465 = (((_1443 + 6) * 20) + (_1446 + 6)) * 3;
            if (false)
            {
                _3100 = 0.5;
            }
            else
            {
                _3100 = 0.25;
            }
            if (false)
            {
                _3101 = 0.5;
            }
            else
            {
                _3101 = 0.25;
            }
            float _3114 = _3100 * _3101;
            ivec2 _1478 = _3009 + ivec2(-1, 1);
            int _1480 = _1478.y;
            int _1483 = _1478.x;
            int _1486 = (((_1480 + 4) * 16) + (_1483 + 4)) * 3;
            vec3 blurIn = ((((((vec3(sInClamped[_1268], sInClamped[_1268 + 1], sInClamped[_1268 + 2]) * _3024) + (vec3(sInClamped[_1303], sInClamped[_1303 + 1], sInClamped[_1303 + 2]) * _3039)) + (vec3(sInClamped[_1340], sInClamped[_1340 + 1], sInClamped[_1340 + 2]) * _3054)) + (vec3(sInClamped[_1377], sInClamped[_1377 + 1], sInClamped[_1377 + 2]) * _3069)) + (vec3(sInClamped[_1412], sInClamped[_1415], sInClamped[_1418]) * _3084)) + (vec3(sInClamped[_1449], sInClamped[_1449 + 1], sInClamped[_1449 + 2]) * _3099)) + (vec3(sInClamped[_1486], sInClamped[_1486 + 1], sInClamped[_1486 + 2]) * _3114);
            int _1502 = (((_1480 + 6) * 20) + (_1483 + 6)) * 3;
            vec3 blurHist = ((((((vec3(sHistory[_1283], sHistory[_1283 + 1], sHistory[_1283 + 2]) * _3024) + (vec3(sHistory[_1319], sHistory[_1319 + 1], sHistory[_1319 + 2]) * _3039)) + (vec3(sHistory[_1356], sHistory[_1356 + 1], sHistory[_1356 + 2]) * _3054)) + (vec3(sHistory[_1393], sHistory[_1393 + 1], sHistory[_1393 + 2]) * _3069)) + (vec3(sHistory[_1428], sHistory[_1428 + 1], sHistory[_1428 + 2]) * _3084)) + (vec3(sHistory[_1465], sHistory[_1465 + 1], sHistory[_1465 + 2]) * _3099)) + (vec3(sHistory[_1502], sHistory[_1502 + 1], sHistory[_1502 + 2]) * _3114);
            if (true)
            {
                _3115 = 0.5;
            }
            else
            {
                _3115 = 0.25;
            }
            if (false)
            {
                _3116 = 0.5;
            }
            else
            {
                _3116 = 0.25;
            }
            float _3129 = _3115 * _3116;
            ivec2 _1515 = _3009 + ivec2(0, 1);
            int _1517 = _1515.y;
            int _1520 = _1515.x;
            int _1523 = (((_1517 + 4) * 16) + (_1520 + 4)) * 3;
            int _1539 = (((_1517 + 6) * 20) + (_1520 + 6)) * 3;
            if (false)
            {
                _3130 = 0.5;
            }
            else
            {
                _3130 = 0.25;
            }
            if (false)
            {
                _3131 = 0.5;
            }
            else
            {
                _3131 = 0.25;
            }
            float _3144 = _3130 * _3131;
            ivec2 _1552 = _3009 + ivec2(1);
            int _1554 = _1552.y;
            int _1557 = _1552.x;
            int _1560 = (((_1554 + 4) * 16) + (_1557 + 4)) * 3;
            vec3 blurIn_1 = (blurIn + (vec3(sInClamped[_1523], sInClamped[_1523 + 1], sInClamped[_1523 + 2]) * _3129)) + (vec3(sInClamped[_1560], sInClamped[_1560 + 1], sInClamped[_1560 + 2]) * _3144);
            int _1576 = (((_1554 + 6) * 20) + (_1557 + 6)) * 3;
            vec3 blurHist_1 = (blurHist + (vec3(sHistory[_1539], sHistory[_1539 + 1], sHistory[_1539 + 2]) * _3129)) + (vec3(sHistory[_1576], sHistory[_1576 + 1], sHistory[_1576 + 2]) * _3144);
            sBlur[_1412] = blurIn_1.x;
            sBlur[_1415] = blurIn_1.y;
            sBlur[_1418] = blurIn_1.z;
            int at_5 = (((_1406 + 5) * 18) + (_1409 + 5)) * 3;
            sMid[at_5] = blurHist_1.x;
            sMid[at_5 + 1] = blurHist_1.y;
            sMid[at_5 + 2] = blurHist_1.z;
            i += 64u;
            continue;
        }
        barrier();
        ivec2 _1623 = _159 + ivec2(-1);
        int _1631 = (((_1623.y + 4) * 16) + (_1623.x + 4)) * 3;
        int _1634 = _1631 + 1;
        int _1637 = _1631 + 2;
        vec3 s_36 = vec3(sInClamped[_1631], sInClamped[_1634], sInClamped[_1637]);
        ivec2 _1645 = _159 + ivec2(0, -1);
        int _1653 = (((_1645.y + 4) * 16) + (_1645.x + 4)) * 3;
        int _1656 = _1653 + 1;
        int _1659 = _1653 + 2;
        vec3 s_37 = vec3(sInClamped[_1653], sInClamped[_1656], sInClamped[_1659]);
        ivec2 _1665 = _159 + ivec2(1, -1);
        int _1673 = (((_1665.y + 4) * 16) + (_1665.x + 4)) * 3;
        int _1676 = _1673 + 1;
        int _1679 = _1673 + 2;
        vec3 s_38 = vec3(sInClamped[_1673], sInClamped[_1676], sInClamped[_1679]);
        ivec2 _1685 = _159 + ivec2(-1, 0);
        int _1693 = (((_1685.y + 4) * 16) + (_1685.x + 4)) * 3;
        int _1696 = _1693 + 1;
        int _1699 = _1693 + 2;
        vec3 s_39 = vec3(sInClamped[_1693], sInClamped[_1696], sInClamped[_1699]);
        int _1709 = (((_296 + 4) * 16) + (_299 + 4)) * 3;
        int _1712 = _1709 + 1;
        int _1715 = _1709 + 2;
        vec3 s_40 = vec3(sInClamped[_1709], sInClamped[_1712], sInClamped[_1715]);
        ivec2 _1721 = _159 + ivec2(1, 0);
        int _1729 = (((_1721.y + 4) * 16) + (_1721.x + 4)) * 3;
        int _1732 = _1729 + 1;
        int _1735 = _1729 + 2;
        vec3 s_41 = vec3(sInClamped[_1729], sInClamped[_1732], sInClamped[_1735]);
        ivec2 _1741 = _159 + ivec2(-1, 1);
        int _1749 = (((_1741.y + 4) * 16) + (_1741.x + 4)) * 3;
        int _1752 = _1749 + 1;
        int _1755 = _1749 + 2;
        vec3 s_42 = vec3(sInClamped[_1749], sInClamped[_1752], sInClamped[_1755]);
        ivec2 _1761 = _159 + ivec2(0, 1);
        int _1769 = (((_1761.y + 4) * 16) + (_1761.x + 4)) * 3;
        int _1772 = _1769 + 1;
        int _1775 = _1769 + 2;
        vec3 s_43 = vec3(sInClamped[_1769], sInClamped[_1772], sInClamped[_1775]);
        ivec2 _1781 = _159 + ivec2(1);
        int _1789 = (((_1781.y + 4) * 16) + (_1781.x + 4)) * 3;
        int _1792 = _1789 + 1;
        int _1795 = _1789 + 2;
        vec3 s_44 = vec3(sInClamped[_1789], sInClamped[_1792], sInClamped[_1795]);
        float _1802 = sBlur[_1709];
        float _1804 = sBlur[_1712];
        float _1806 = sBlur[_1715];
        int _1812 = (((_296 + 5) * 18) + (_299 + 5)) * 3;
        float _1814 = sMid[_1812];
        float _1817 = sMid[_1812 + 1];
        float _1820 = sMid[_1812 + 2];
        i = gl_LocalInvocationIndex;
        vec3 boxLo;
        vec3 boxHi;
        for (;;)
        {
            if (!(i < 144u))
            {
                break;
            }
            int _3155 = int(i);
            ivec2 _3160 = ivec2(_3155 - 12 * (_3155 / 12), _3155 / 12) - ivec2(2);
            int _1838 = _3160.y;
            int _1841 = _3160.x;
            int _1844 = (((_1838 + 5) * 18) + (_1841 + 5)) * 3;
            int _1847 = _1844 + 1;
            int _1850 = _1844 + 2;
            vec3 fh = vec3(sMid[_1844], sMid[_1847], sMid[_1850]);
            int _1858 = (((_1838 + 4) * 16) + (_1841 + 4)) * 3;
            int _1861 = _1858 + 1;
            int _1864 = _1858 + 2;
            vec3 inC = vec3(sInClamped[_1858], sInClamped[_1861], sInClamped[_1864]);
            int _1872 = (((_1838 + 6) * 20) + (_1841 + 6)) * 3;
            int _1875 = _1872 + 1;
            int _1878 = _1872 + 2;
            ivec2 r = _3160 + ivec2(-1);
            int _1884 = r.y;
            int _1887 = r.x;
            int _1890 = (((_1884 + 4) * 16) + (_1887 + 4)) * 3;
            int _1893 = _1890 + 1;
            int _1896 = _1890 + 2;
            vec3 c = vec3(sInClamped[_1890], sInClamped[_1893], sInClamped[_1896]);
            vec3 b = vec3(sBlur[_1890], sBlur[_1893], sBlur[_1896]);
            int _1915 = (((_1884 + 6) * 20) + (_1887 + 6)) * 3;
            ivec2 r_1 = _3160 + ivec2(0, -1);
            int _1928 = r_1.y;
            int _1931 = r_1.x;
            int _1934 = (((_1928 + 4) * 16) + (_1931 + 4)) * 3;
            int _1937 = _1934 + 1;
            int _1940 = _1934 + 2;
            vec3 c_1 = vec3(sInClamped[_1934], sInClamped[_1937], sInClamped[_1940]);
            vec3 b_1 = vec3(sBlur[_1934], sBlur[_1937], sBlur[_1940]);
            int _1960 = (((_1928 + 6) * 20) + (_1931 + 6)) * 3;
            ivec2 r_2 = _3160 + ivec2(1, -1);
            int _1974 = r_2.y;
            int _1977 = r_2.x;
            int _1980 = (((_1974 + 4) * 16) + (_1977 + 4)) * 3;
            int _1983 = _1980 + 1;
            int _1986 = _1980 + 2;
            vec3 c_2 = vec3(sInClamped[_1980], sInClamped[_1983], sInClamped[_1986]);
            vec3 b_2 = vec3(sBlur[_1980], sBlur[_1983], sBlur[_1986]);
            int _2006 = (((_1974 + 6) * 20) + (_1977 + 6)) * 3;
            ivec2 r_3 = _3160 + ivec2(-1, 0);
            int _2020 = r_3.y;
            int _2023 = r_3.x;
            int _2026 = (((_2020 + 4) * 16) + (_2023 + 4)) * 3;
            int _2029 = _2026 + 1;
            int _2032 = _2026 + 2;
            vec3 c_3 = vec3(sInClamped[_2026], sInClamped[_2029], sInClamped[_2032]);
            vec3 b_3 = vec3(sBlur[_2026], sBlur[_2029], sBlur[_2032]);
            int _2052 = (((_2020 + 6) * 20) + (_2023 + 6)) * 3;
            vec3 c_4 = vec3(sInClamped[_1858], sInClamped[_1861], sInClamped[_1864]);
            vec3 b_4 = vec3(sBlur[_1858], sBlur[_1861], sBlur[_1864]);
            ivec2 r_4 = _3160 + ivec2(1, 0);
            int _2089 = r_4.y;
            int _2092 = r_4.x;
            int _2095 = (((_2089 + 4) * 16) + (_2092 + 4)) * 3;
            int _2098 = _2095 + 1;
            int _2101 = _2095 + 2;
            vec3 c_5 = vec3(sInClamped[_2095], sInClamped[_2098], sInClamped[_2101]);
            vec3 b_5 = vec3(sBlur[_2095], sBlur[_2098], sBlur[_2101]);
            int _2121 = (((_2089 + 6) * 20) + (_2092 + 6)) * 3;
            vec3 sumDiff_1 = ((((abs(vec3(sInput[_1915], sInput[_1915 + 1], sInput[_1915 + 2]) - c) + abs(vec3(sInput[_1960], sInput[_1960 + 1], sInput[_1960 + 2]) - c_1)) + abs(vec3(sInput[_2006], sInput[_2006 + 1], sInput[_2006 + 2]) - c_2)) + abs(vec3(sInput[_2052], sInput[_2052 + 1], sInput[_2052 + 2]) - c_3)) + abs(vec3(sInput[_1872], sInput[_1875], sInput[_1878]) - c_4)) + abs(vec3(sInput[_2121], sInput[_2121 + 1], sInput[_2121 + 2]) - c_5);
            ivec2 r_5 = _3160 + ivec2(-1, 1);
            int _2135 = r_5.y;
            int _2138 = r_5.x;
            int _2141 = (((_2135 + 4) * 16) + (_2138 + 4)) * 3;
            int _2144 = _2141 + 1;
            int _2147 = _2141 + 2;
            vec3 c_6 = vec3(sInClamped[_2141], sInClamped[_2144], sInClamped[_2147]);
            vec3 b_6 = vec3(sBlur[_2141], sBlur[_2144], sBlur[_2147]);
            int _2167 = (((_2135 + 6) * 20) + (_2138 + 6)) * 3;
            ivec2 r_6 = _3160 + ivec2(0, 1);
            int _2181 = r_6.y;
            int _2184 = r_6.x;
            int _2187 = (((_2181 + 4) * 16) + (_2184 + 4)) * 3;
            int _2190 = _2187 + 1;
            int _2193 = _2187 + 2;
            vec3 c_7 = vec3(sInClamped[_2187], sInClamped[_2190], sInClamped[_2193]);
            vec3 b_7 = vec3(sBlur[_2187], sBlur[_2190], sBlur[_2193]);
            int _2213 = (((_2181 + 6) * 20) + (_2184 + 6)) * 3;
            ivec2 r_7 = _3160 + ivec2(1);
            int _2227 = r_7.y;
            int _2230 = r_7.x;
            int _2233 = (((_2227 + 4) * 16) + (_2230 + 4)) * 3;
            int _2236 = _2233 + 1;
            int _2239 = _2233 + 2;
            vec3 c_8 = vec3(sInClamped[_2233], sInClamped[_2236], sInClamped[_2239]);
            vec3 b_8 = vec3(sBlur[_2233], sBlur[_2236], sBlur[_2239]);
            vec3 _2252 = min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), b), b_1), b_2), b_3), b_4), b_5), b_6), b_7), b_8);
            vec3 _2253 = max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), b), b_1), b_2), b_3), b_4), b_5), b_6), b_7), b_8);
            int _2259 = (((_2227 + 6) * 20) + (_2230 + 6)) * 3;
            if (p.control.w > 0.0)
            {
                float _2289 = clamp(texelFetch(_3318, ivec3(clamp(origin + _3160, ivec2(0), _145), 0).xy, 0).y * p.control.w, 0.0, 1.0);
                if (_2289 > 0.0078740157186985015869140625)
                {
                    int _2297 = (((_1884 + 5) * 18) + (_1887 + 5)) * 3;
                    vec3 b_9 = vec3(sMid[_2297], sMid[_2297 + 1], sMid[_2297 + 2]);
                    int _2313 = (((_1928 + 5) * 18) + (_1931 + 5)) * 3;
                    vec3 b_10 = vec3(sMid[_2313], sMid[_2313 + 1], sMid[_2313 + 2]);
                    int _2329 = (((_1974 + 5) * 18) + (_1977 + 5)) * 3;
                    vec3 b_11 = vec3(sMid[_2329], sMid[_2329 + 1], sMid[_2329 + 2]);
                    int _2345 = (((_2020 + 5) * 18) + (_2023 + 5)) * 3;
                    vec3 b_12 = vec3(sMid[_2345], sMid[_2345 + 1], sMid[_2345 + 2]);
                    vec3 b_13 = vec3(sMid[_1844], sMid[_1847], sMid[_1850]);
                    int _2367 = (((_2089 + 5) * 18) + (_2092 + 5)) * 3;
                    vec3 b_14 = vec3(sMid[_2367], sMid[_2367 + 1], sMid[_2367 + 2]);
                    int _2383 = (((_2135 + 5) * 18) + (_2138 + 5)) * 3;
                    vec3 b_15 = vec3(sMid[_2383], sMid[_2383 + 1], sMid[_2383 + 2]);
                    int _2399 = (((_2181 + 5) * 18) + (_2184 + 5)) * 3;
                    vec3 b_16 = vec3(sMid[_2399], sMid[_2399 + 1], sMid[_2399 + 2]);
                    int _2415 = (((_2227 + 5) * 18) + (_2230 + 5)) * 3;
                    vec3 b_17 = vec3(sMid[_2415], sMid[_2415 + 1], sMid[_2415 + 2]);
                    vec3 _2428 = vec3(_2289);
                    boxLo = mix(_2252, min(_2252, min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), b_9), b_10), b_11), b_12), b_13), b_14), b_15), b_16), b_17)), _2428);
                    boxHi = mix(_2253, max(_2253, max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), b_9), b_10), b_11), b_12), b_13), b_14), b_15), b_16), b_17)), _2428);
                }
                else
                {
                    boxLo = _2252;
                    boxHi = _2253;
                }
            }
            else
            {
                boxLo = _2252;
                boxHi = _2253;
            }
            vec3 _2458 = max(vec3(0.00048828125), min(abs(((abs(vec3(sInput[_1872], sInput[_1875], sInput[_1878]) - inC) * 9.0) - (((sumDiff_1 + abs(vec3(sInput[_2167], sInput[_2167 + 1], sInput[_2167 + 2]) - c_6)) + abs(vec3(sInput[_2213], sInput[_2213 + 1], sInput[_2213 + 2]) - c_7)) + abs(vec3(sInput[_2259], sInput[_2259 + 1], sInput[_2259 + 2]) - c_8))) * vec3(0.125)), abs(((inC * 9.0) - ((((((((c + c_1) + c_2) + c_3) + c_4) + c_5) + c_6) + c_7) + c_8)) * vec3(0.125))));
            vec3 err = max(_2458, (max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), c), c_1), c_2), c_3), c_4), c_5), c_6), c_7), c_8) - min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), c), c_1), c_2), c_3), c_4), c_5), c_6), c_7), c_8)) * vec3(0.0625)) + vec3(0.00048828125);
            vec3 _2473 = abs(clamp(fh, boxLo - err, boxHi + err) - fh);
            sHistory[_1872] = _2473.x;
            sHistory[_1875] = _2473.y;
            sHistory[_1878] = _2473.z;
            i += 64u;
            continue;
        }
        barrier();
        i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 100u))
            {
                break;
            }
            int _3171 = int(i);
            ivec2 _3176 = ivec2(_3171 - 10 * (_3171 / 10), _3171 / 10) - ivec2(1);
            ivec2 _2501 = _3176 + ivec2(-1);
            int _2508 = (((_2501.y + 6) * 20) + (_2501.x + 6)) * 3;
            vec3 _2517 = vec3(sHistory[_2508], sHistory[_2508 + 1], sHistory[_2508 + 2]);
            ivec2 _2520 = _3176 + ivec2(0, -1);
            int _2527 = (((_2520.y + 6) * 20) + (_2520.x + 6)) * 3;
            vec3 _2536 = vec3(sHistory[_2527], sHistory[_2527 + 1], sHistory[_2527 + 2]);
            ivec2 _2539 = _3176 + ivec2(1, -1);
            int _2546 = (((_2539.y + 6) * 20) + (_2539.x + 6)) * 3;
            vec3 _2555 = vec3(sHistory[_2546], sHistory[_2546 + 1], sHistory[_2546 + 2]);
            ivec2 _2558 = _3176 + ivec2(-1, 0);
            int _2565 = (((_2558.y + 6) * 20) + (_2558.x + 6)) * 3;
            vec3 _2574 = vec3(sHistory[_2565], sHistory[_2565 + 1], sHistory[_2565 + 2]);
            int _2577 = _3176.y;
            int _2580 = _3176.x;
            int _2583 = (((_2577 + 6) * 20) + (_2580 + 6)) * 3;
            vec3 _2592 = vec3(sHistory[_2583], sHistory[_2583 + 1], sHistory[_2583 + 2]);
            ivec2 _2595 = _3176 + ivec2(1, 0);
            int _2602 = (((_2595.y + 6) * 20) + (_2595.x + 6)) * 3;
            vec3 _2611 = vec3(sHistory[_2602], sHistory[_2602 + 1], sHistory[_2602 + 2]);
            ivec2 _2614 = _3176 + ivec2(-1, 1);
            int _2621 = (((_2614.y + 6) * 20) + (_2614.x + 6)) * 3;
            vec3 _2630 = vec3(sHistory[_2621], sHistory[_2621 + 1], sHistory[_2621 + 2]);
            ivec2 _2634 = _3176 + ivec2(0, 1);
            int _2641 = (((_2634.y + 6) * 20) + (_2634.x + 6)) * 3;
            vec3 _2650 = vec3(sHistory[_2641], sHistory[_2641 + 1], sHistory[_2641 + 2]);
            ivec2 _2653 = _3176 + ivec2(1);
            int _2660 = (((_2653.y + 6) * 20) + (_2653.x + 6)) * 3;
            vec3 _2669 = vec3(sHistory[_2660], sHistory[_2660 + 1], sHistory[_2660 + 2]);
            int at_6 = (((_2577 + 4) * 16) + (_2580 + 4)) * 3;
            vec3 _3197 = min(_2536, _2555);
            vec3 _3198 = max(_2536, _2555);
            vec3 _3199 = min(_2592, _2611);
            vec3 _3200 = max(_2592, _2611);
            vec3 _3201 = min(_2650, _2669);
            vec3 _3202 = max(_2650, _2669);
            vec3 _3204 = max(_2517, _3197);
            vec3 _3206 = max(_2574, _3199);
            vec3 _3208 = max(_2630, _3201);
            vec3 _3211 = min(_3206, _3200);
            vec3 _3213 = min(_3208, _3202);
            vec3 _3225 = min(max(_3204, _3198), min(max(_3206, _3200), max(_3208, _3202)));
            vec3 _3227 = min(max(min(_3204, _3198), min(_3211, _3213)), max(_3211, _3213));
            vec3 _3233 = min(max(max(max(min(_2517, _3197), min(_2574, _3199)), min(_2630, _3201)), min(_3227, _3225)), max(_3227, _3225));
            sBlur[at_6] = _3233.x;
            sBlur[at_6 + 1] = _3233.y;
            sBlur[at_6 + 2] = _3233.z;
            i += 64u;
            continue;
        }
        barrier();
        bool _21;
        if (px.x >= _142.x)
        {
            _21 = true;
        }
        else
        {
            _21 = px.y >= _142.y;
        }
        if (!(_21))
        {
            vec3 _2828 = max(max(max(max(max(max(max(vec3(0.0), vec3(sBlur[_1631], sBlur[_1634], sBlur[_1637])), vec3(sBlur[_1653], sBlur[_1656], sBlur[_1659])), vec3(sBlur[_1673], sBlur[_1676], sBlur[_1679])), vec3(sBlur[_1693], sBlur[_1696], sBlur[_1699])), vec3(sBlur[_1709], sBlur[_1712], sBlur[_1715])), vec3(sBlur[_1729], sBlur[_1732], sBlur[_1735])), vec3(sBlur[_1749], sBlur[_1752], sBlur[_1755]));
            vec3 _2850 = vec3(1.0) - (max(max(_2828, vec3(sBlur[_1769], sBlur[_1772], sBlur[_1775])), vec3(sBlur[_1789], sBlur[_1792], sBlur[_1795])) / max(max(abs(vec3(_1802, _1804, _1806) - vec3(_1814, _1817, _1820)), ((max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), s_36), s_37), s_38), s_39), s_40), s_41), s_42), s_43), s_44) - min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), s_36), s_37), s_38), s_39), s_40), s_41), s_42), s_43), s_44)) * 0.25) + vec3(0.000244140625)), vec3(9.9999999747524270787835121154785e-07)));
            vec3 _3237 = clamp(_2850, vec3(0.0), vec3(1.0));
            float _2864 = min(_3237.x, min(_3237.y, _3237.z));
            uvec2 _2865 = uvec2(px);
            imageStore(uRejection, ivec2(_2865), vec4(_2864, 0.0, 0.0, 0.0));
            float parallax;
            if (p.control.z > 0.5)
            {
                parallax = textureLod(_3320, (vec2(px) + vec2(0.5)) * p.size.zw, 0.0).x;
            }
            else
            {
                parallax = 1.0;
            }
            float total;
            if (parallax < 0.5)
            {
                total = 1.0;
            }
            else
            {
                total = clamp(1.0 - (4.0 * _2864), 0.0, 1.0);
            }
            imageStore(uGuide, ivec2(_2865), vec4(mix(vec3(_313, _315, _317), vec3(_304, _307, _310), vec3(max(p.control.y, max(1.0 - _2864, total)))), 1.0));
        }
    }
}

