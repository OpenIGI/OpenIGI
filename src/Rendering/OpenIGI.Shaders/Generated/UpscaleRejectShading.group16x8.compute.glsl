#version 430
layout(local_size_x = 16, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, std140) uniform Params_std140
{
    vec4 size;
    vec4 control;
} p;

layout(binding = 0, r8) uniform restrict writeonly image2D uRejection;
layout(binding = 1, rgba16f) uniform restrict writeonly image2D uGuide;
layout(binding = 0) uniform sampler2D _3321;
layout(binding = 1) uniform sampler2D _3324;
layout(binding = 2) uniform sampler2D _3326;
layout(binding = 4) uniform sampler2D _3329;
layout(binding = 3) uniform sampler2D _3331;

shared float sInput[1680];
shared float sHistory[1680];
shared float sMid[1404];
shared float sInClamped[1152];
shared float sBlur[1152];

void main()
{
    {
        ivec2 _142 = ivec2(p.size.xy);
        ivec2 _145 = _142 - ivec2(1);
        ivec2 origin = ivec2(gl_WorkGroupID.xy) * ivec2(16, 8);
        ivec2 _160 = ivec2(gl_LocalInvocationID.xy);
        ivec2 px = origin + _160;
        uint i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 560u))
            {
                break;
            }
            int _2939 = int(i);
            ivec2 _2944 = ivec2(_2939 - 28 * (_2939 / 28), _2939 / 28) - ivec2(6);
            ivec2 _202 = clamp(origin + _2944, ivec2(0), _145);
            ivec2 _207 = ivec3(_202, 0).xy;
            vec3 x = max(texelFetch(_3321, _207, 0).xyz, vec3(0.0)) * p.control.x;
            vec3 inputValue = x / (x + vec3(0.17000000178813934326171875));
            vec3 _251 = max(textureLod(_3326, clamp(((vec2(_202) + vec2(0.5)) * p.size.zw) - texelFetch(_3324, _207, 0).xy, vec2(0.0), vec2(1.0)), 0.0).xyz, vec3(0.0));
            int at_ = (((_2944.y + 6) * 28) + (_2944.x + 6)) * 3;
            sInput[at_] = inputValue.x;
            int _269 = at_ + 1;
            sInput[_269] = inputValue.y;
            int _273 = at_ + 2;
            sInput[_273] = inputValue.z;
            sHistory[at_] = _251.x;
            sHistory[_269] = _251.y;
            sHistory[_273] = _251.z;
            i += 128u;
            continue;
        }
        barrier();
        int _298 = _160.y;
        int _301 = _160.x;
        int _304 = (((_298 + 6) * 28) + (_301 + 6)) * 3;
        float _306 = sInput[_304];
        int _307 = _304 + 1;
        float _309 = sInput[_307];
        int _310 = _304 + 2;
        float _312 = sInput[_310];
        float _315 = sHistory[_304];
        float _317 = sHistory[_307];
        float _319 = sHistory[_310];
        i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 468u))
            {
                break;
            }
            int _2956 = int(i);
            ivec2 _2961 = ivec2(_2956 - 26 * (_2956 / 26), _2956 / 26) - ivec2(5);
            ivec2 _331 = _2961 + ivec2(-1);
            int _340 = (((_331.y + 6) * 28) + (_331.x + 6)) * 3;
            vec3 s = vec3(sInput[_340], sInput[_340 + 1], sInput[_340 + 2]);
            ivec2 _356 = _2961 + ivec2(0, -1);
            int _364 = (((_356.y + 6) * 28) + (_356.x + 6)) * 3;
            vec3 s_1 = vec3(sInput[_364], sInput[_364 + 1], sInput[_364 + 2]);
            ivec2 _376 = _2961 + ivec2(1, -1);
            int _384 = (((_376.y + 6) * 28) + (_376.x + 6)) * 3;
            vec3 s_2 = vec3(sInput[_384], sInput[_384 + 1], sInput[_384 + 2]);
            ivec2 _396 = _2961 + ivec2(-1, 0);
            int _404 = (((_396.y + 6) * 28) + (_396.x + 6)) * 3;
            vec3 s_3 = vec3(sInput[_404], sInput[_404 + 1], sInput[_404 + 2]);
            int _416 = _2961.y;
            int _419 = _2961.x;
            int _422 = (((_416 + 6) * 28) + (_419 + 6)) * 3;
            int _425 = _422 + 1;
            int _428 = _422 + 2;
            vec3 s_4 = vec3(sInput[_422], sInput[_425], sInput[_428]);
            ivec2 _434 = _2961 + ivec2(1, 0);
            int _442 = (((_434.y + 6) * 28) + (_434.x + 6)) * 3;
            vec3 s_5 = vec3(sInput[_442], sInput[_442 + 1], sInput[_442 + 2]);
            ivec2 _454 = _2961 + ivec2(-1, 1);
            int _462 = (((_454.y + 6) * 28) + (_454.x + 6)) * 3;
            vec3 s_6 = vec3(sInput[_462], sInput[_462 + 1], sInput[_462 + 2]);
            ivec2 _474 = _2961 + ivec2(0, 1);
            int _482 = (((_474.y + 6) * 28) + (_474.x + 6)) * 3;
            vec3 s_7 = vec3(sInput[_482], sInput[_482 + 1], sInput[_482 + 2]);
            ivec2 _494 = _2961 + ivec2(1);
            int _502 = (((_494.y + 6) * 28) + (_494.x + 6)) * 3;
            vec3 s_8 = vec3(sInput[_502], sInput[_502 + 1], sInput[_502 + 2]);
            int at_1 = (((_416 + 5) * 26) + (_419 + 5)) * 3;
            vec3 _527 = clamp(vec3(sHistory[_422], sHistory[_425], sHistory[_428]), min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), s), s_1), s_2), s_3), s_4), s_5), s_6), s_7), s_8), max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), s), s_1), s_2), s_3), s_4), s_5), s_6), s_7), s_8));
            sMid[at_1] = _527.x;
            sMid[at_1 + 1] = _527.y;
            sMid[at_1 + 2] = _527.z;
            i += 128u;
            continue;
        }
        barrier();
        i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 384u))
            {
                break;
            }
            int _2973 = int(i);
            ivec2 _2978 = ivec2(_2973 - 24 * (_2973 / 24), _2973 / 24) - ivec2(4);
            ivec2 _561 = _2978 + ivec2(-1);
            int _569 = (((_561.y + 5) * 26) + (_561.x + 5)) * 3;
            vec3 s_9 = vec3(sMid[_569], sMid[_569 + 1], sMid[_569 + 2]);
            ivec2 _583 = _2978 + ivec2(0, -1);
            int _591 = (((_583.y + 5) * 26) + (_583.x + 5)) * 3;
            vec3 s_10 = vec3(sMid[_591], sMid[_591 + 1], sMid[_591 + 2]);
            ivec2 _603 = _2978 + ivec2(1, -1);
            int _611 = (((_603.y + 5) * 26) + (_603.x + 5)) * 3;
            vec3 s_11 = vec3(sMid[_611], sMid[_611 + 1], sMid[_611 + 2]);
            ivec2 _623 = _2978 + ivec2(-1, 0);
            int _631 = (((_623.y + 5) * 26) + (_623.x + 5)) * 3;
            vec3 s_12 = vec3(sMid[_631], sMid[_631 + 1], sMid[_631 + 2]);
            int _643 = _2978.y;
            int _646 = _2978.x;
            int _649 = (((_643 + 5) * 26) + (_646 + 5)) * 3;
            vec3 s_13 = vec3(sMid[_649], sMid[_649 + 1], sMid[_649 + 2]);
            ivec2 _661 = _2978 + ivec2(1, 0);
            int _669 = (((_661.y + 5) * 26) + (_661.x + 5)) * 3;
            vec3 s_14 = vec3(sMid[_669], sMid[_669 + 1], sMid[_669 + 2]);
            ivec2 _681 = _2978 + ivec2(-1, 1);
            int _689 = (((_681.y + 5) * 26) + (_681.x + 5)) * 3;
            vec3 s_15 = vec3(sMid[_689], sMid[_689 + 1], sMid[_689 + 2]);
            ivec2 _701 = _2978 + ivec2(0, 1);
            int _709 = (((_701.y + 5) * 26) + (_701.x + 5)) * 3;
            vec3 s_16 = vec3(sMid[_709], sMid[_709 + 1], sMid[_709 + 2]);
            ivec2 _721 = _2978 + ivec2(1);
            int _729 = (((_721.y + 5) * 26) + (_721.x + 5)) * 3;
            vec3 s_17 = vec3(sMid[_729], sMid[_729 + 1], sMid[_729 + 2]);
            int at_2 = (((_643 + 4) * 24) + (_646 + 4)) * 3;
            int _751 = (((_643 + 6) * 28) + (_646 + 6)) * 3;
            vec3 _761 = clamp(vec3(sInput[_751], sInput[_751 + 1], sInput[_751 + 2]), min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), s_9), s_10), s_11), s_12), s_13), s_14), s_15), s_16), s_17), max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), s_9), s_10), s_11), s_12), s_13), s_14), s_15), s_16), s_17));
            sInClamped[at_2] = _761.x;
            sInClamped[at_2 + 1] = _761.y;
            sInClamped[at_2 + 2] = _761.z;
            i += 128u;
            continue;
        }
        barrier();
        i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 468u))
            {
                break;
            }
            int _2983 = int(i);
            ivec2 _2988 = ivec2(_2983 - 26 * (_2983 / 26), _2983 / 26) - ivec2(5);
            ivec2 _793 = _2988 + ivec2(-1);
            int _801 = (((_793.y + 6) * 28) + (_793.x + 6)) * 3;
            vec3 s_18 = vec3(sHistory[_801], sHistory[_801 + 1], sHistory[_801 + 2]);
            ivec2 _815 = _2988 + ivec2(0, -1);
            int _823 = (((_815.y + 6) * 28) + (_815.x + 6)) * 3;
            vec3 s_19 = vec3(sHistory[_823], sHistory[_823 + 1], sHistory[_823 + 2]);
            ivec2 _835 = _2988 + ivec2(1, -1);
            int _843 = (((_835.y + 6) * 28) + (_835.x + 6)) * 3;
            vec3 s_20 = vec3(sHistory[_843], sHistory[_843 + 1], sHistory[_843 + 2]);
            ivec2 _855 = _2988 + ivec2(-1, 0);
            int _863 = (((_855.y + 6) * 28) + (_855.x + 6)) * 3;
            vec3 s_21 = vec3(sHistory[_863], sHistory[_863 + 1], sHistory[_863 + 2]);
            int _875 = _2988.y;
            int _878 = _2988.x;
            int _881 = (((_875 + 6) * 28) + (_878 + 6)) * 3;
            int _884 = _881 + 1;
            int _887 = _881 + 2;
            vec3 s_22 = vec3(sHistory[_881], sHistory[_884], sHistory[_887]);
            ivec2 _893 = _2988 + ivec2(1, 0);
            int _901 = (((_893.y + 6) * 28) + (_893.x + 6)) * 3;
            vec3 s_23 = vec3(sHistory[_901], sHistory[_901 + 1], sHistory[_901 + 2]);
            ivec2 _913 = _2988 + ivec2(-1, 1);
            int _921 = (((_913.y + 6) * 28) + (_913.x + 6)) * 3;
            vec3 s_24 = vec3(sHistory[_921], sHistory[_921 + 1], sHistory[_921 + 2]);
            ivec2 _933 = _2988 + ivec2(0, 1);
            int _941 = (((_933.y + 6) * 28) + (_933.x + 6)) * 3;
            vec3 s_25 = vec3(sHistory[_941], sHistory[_941 + 1], sHistory[_941 + 2]);
            ivec2 _953 = _2988 + ivec2(1);
            int _961 = (((_953.y + 6) * 28) + (_953.x + 6)) * 3;
            vec3 s_26 = vec3(sHistory[_961], sHistory[_961 + 1], sHistory[_961 + 2]);
            int at_3 = (((_875 + 5) * 26) + (_878 + 5)) * 3;
            vec3 _985 = clamp(vec3(sInput[_881], sInput[_884], sInput[_887]), min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), s_18), s_19), s_20), s_21), s_22), s_23), s_24), s_25), s_26), max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), s_18), s_19), s_20), s_21), s_22), s_23), s_24), s_25), s_26));
            sMid[at_3] = _985.x;
            sMid[at_3 + 1] = _985.y;
            sMid[at_3 + 2] = _985.z;
            i += 128u;
            continue;
        }
        barrier();
        i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 384u))
            {
                break;
            }
            int _2993 = int(i);
            ivec2 _2998 = ivec2(_2993 - 24 * (_2993 / 24), _2993 / 24) - ivec2(4);
            ivec2 _1013 = _2998 + ivec2(-1);
            int _1021 = (((_1013.y + 5) * 26) + (_1013.x + 5)) * 3;
            vec3 s_27 = vec3(sMid[_1021], sMid[_1021 + 1], sMid[_1021 + 2]);
            ivec2 _1035 = _2998 + ivec2(0, -1);
            int _1043 = (((_1035.y + 5) * 26) + (_1035.x + 5)) * 3;
            vec3 s_28 = vec3(sMid[_1043], sMid[_1043 + 1], sMid[_1043 + 2]);
            ivec2 _1055 = _2998 + ivec2(1, -1);
            int _1063 = (((_1055.y + 5) * 26) + (_1055.x + 5)) * 3;
            vec3 s_29 = vec3(sMid[_1063], sMid[_1063 + 1], sMid[_1063 + 2]);
            ivec2 _1075 = _2998 + ivec2(-1, 0);
            int _1083 = (((_1075.y + 5) * 26) + (_1075.x + 5)) * 3;
            vec3 s_30 = vec3(sMid[_1083], sMid[_1083 + 1], sMid[_1083 + 2]);
            int _1095 = _2998.y;
            int _1098 = _2998.x;
            int _1101 = (((_1095 + 5) * 26) + (_1098 + 5)) * 3;
            vec3 s_31 = vec3(sMid[_1101], sMid[_1101 + 1], sMid[_1101 + 2]);
            ivec2 _1113 = _2998 + ivec2(1, 0);
            int _1121 = (((_1113.y + 5) * 26) + (_1113.x + 5)) * 3;
            vec3 s_32 = vec3(sMid[_1121], sMid[_1121 + 1], sMid[_1121 + 2]);
            ivec2 _1133 = _2998 + ivec2(-1, 1);
            int _1141 = (((_1133.y + 5) * 26) + (_1133.x + 5)) * 3;
            vec3 s_33 = vec3(sMid[_1141], sMid[_1141 + 1], sMid[_1141 + 2]);
            ivec2 _1153 = _2998 + ivec2(0, 1);
            int _1161 = (((_1153.y + 5) * 26) + (_1153.x + 5)) * 3;
            vec3 s_34 = vec3(sMid[_1161], sMid[_1161 + 1], sMid[_1161 + 2]);
            ivec2 _1173 = _2998 + ivec2(1);
            int _1181 = (((_1173.y + 5) * 26) + (_1173.x + 5)) * 3;
            vec3 s_35 = vec3(sMid[_1181], sMid[_1181 + 1], sMid[_1181 + 2]);
            int at_4 = (((_1095 + 6) * 28) + (_1098 + 6)) * 3;
            float _1199 = sHistory[at_4];
            int _1200 = at_4 + 1;
            float _1202 = sHistory[_1200];
            int _1203 = at_4 + 2;
            float _1205 = sHistory[_1203];
            vec3 _1207 = clamp(vec3(_1199, _1202, _1205), min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), s_27), s_28), s_29), s_30), s_31), s_32), s_33), s_34), s_35), max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), s_27), s_28), s_29), s_30), s_31), s_32), s_33), s_34), s_35));
            sHistory[at_4] = _1207.x;
            sHistory[_1200] = _1207.y;
            sHistory[_1203] = _1207.z;
            i += 128u;
            continue;
        }
        barrier();
        i = gl_LocalInvocationIndex;
        float _3016;
        float _3017;
        float _3031;
        float _3032;
        float _3046;
        float _3047;
        float _3061;
        float _3062;
        float _3076;
        float _3077;
        float _3091;
        float _3092;
        float _3106;
        float _3107;
        float _3121;
        float _3122;
        float _3136;
        float _3137;
        for (;;)
        {
            if (!(i < 308u))
            {
                break;
            }
            int _3010 = int(i);
            ivec2 _3015 = ivec2(_3010 - 22 * (_3010 / 22), _3010 / 22) - ivec2(3);
            if (false)
            {
                _3016 = 0.5;
            }
            else
            {
                _3016 = 0.25;
            }
            if (false)
            {
                _3017 = 0.5;
            }
            else
            {
                _3017 = 0.25;
            }
            float _3030 = _3016 * _3017;
            ivec2 _1262 = _3015 + ivec2(-1);
            int _1264 = _1262.y;
            int _1267 = _1262.x;
            int _1270 = (((_1264 + 4) * 24) + (_1267 + 4)) * 3;
            int _1285 = (((_1264 + 6) * 28) + (_1267 + 6)) * 3;
            if (true)
            {
                _3031 = 0.5;
            }
            else
            {
                _3031 = 0.25;
            }
            if (false)
            {
                _3032 = 0.5;
            }
            else
            {
                _3032 = 0.25;
            }
            float _3045 = _3031 * _3032;
            ivec2 _1297 = _3015 + ivec2(0, -1);
            int _1299 = _1297.y;
            int _1302 = _1297.x;
            int _1305 = (((_1299 + 4) * 24) + (_1302 + 4)) * 3;
            int _1321 = (((_1299 + 6) * 28) + (_1302 + 6)) * 3;
            if (false)
            {
                _3046 = 0.5;
            }
            else
            {
                _3046 = 0.25;
            }
            if (false)
            {
                _3047 = 0.5;
            }
            else
            {
                _3047 = 0.25;
            }
            float _3060 = _3046 * _3047;
            ivec2 _1334 = _3015 + ivec2(1, -1);
            int _1336 = _1334.y;
            int _1339 = _1334.x;
            int _1342 = (((_1336 + 4) * 24) + (_1339 + 4)) * 3;
            int _1358 = (((_1336 + 6) * 28) + (_1339 + 6)) * 3;
            if (false)
            {
                _3061 = 0.5;
            }
            else
            {
                _3061 = 0.25;
            }
            if (true)
            {
                _3062 = 0.5;
            }
            else
            {
                _3062 = 0.25;
            }
            float _3075 = _3061 * _3062;
            ivec2 _1371 = _3015 + ivec2(-1, 0);
            int _1373 = _1371.y;
            int _1376 = _1371.x;
            int _1379 = (((_1373 + 4) * 24) + (_1376 + 4)) * 3;
            int _1395 = (((_1373 + 6) * 28) + (_1376 + 6)) * 3;
            if (true)
            {
                _3076 = 0.5;
            }
            else
            {
                _3076 = 0.25;
            }
            if (true)
            {
                _3077 = 0.5;
            }
            else
            {
                _3077 = 0.25;
            }
            float _3090 = _3076 * _3077;
            int _1408 = _3015.y;
            int _1411 = _3015.x;
            int _1414 = (((_1408 + 4) * 24) + (_1411 + 4)) * 3;
            int _1417 = _1414 + 1;
            int _1420 = _1414 + 2;
            int _1430 = (((_1408 + 6) * 28) + (_1411 + 6)) * 3;
            if (false)
            {
                _3091 = 0.5;
            }
            else
            {
                _3091 = 0.25;
            }
            if (true)
            {
                _3092 = 0.5;
            }
            else
            {
                _3092 = 0.25;
            }
            float _3105 = _3091 * _3092;
            ivec2 _1443 = _3015 + ivec2(1, 0);
            int _1445 = _1443.y;
            int _1448 = _1443.x;
            int _1451 = (((_1445 + 4) * 24) + (_1448 + 4)) * 3;
            int _1467 = (((_1445 + 6) * 28) + (_1448 + 6)) * 3;
            if (false)
            {
                _3106 = 0.5;
            }
            else
            {
                _3106 = 0.25;
            }
            if (false)
            {
                _3107 = 0.5;
            }
            else
            {
                _3107 = 0.25;
            }
            float _3120 = _3106 * _3107;
            ivec2 _1480 = _3015 + ivec2(-1, 1);
            int _1482 = _1480.y;
            int _1485 = _1480.x;
            int _1488 = (((_1482 + 4) * 24) + (_1485 + 4)) * 3;
            vec3 blurIn = ((((((vec3(sInClamped[_1270], sInClamped[_1270 + 1], sInClamped[_1270 + 2]) * _3030) + (vec3(sInClamped[_1305], sInClamped[_1305 + 1], sInClamped[_1305 + 2]) * _3045)) + (vec3(sInClamped[_1342], sInClamped[_1342 + 1], sInClamped[_1342 + 2]) * _3060)) + (vec3(sInClamped[_1379], sInClamped[_1379 + 1], sInClamped[_1379 + 2]) * _3075)) + (vec3(sInClamped[_1414], sInClamped[_1417], sInClamped[_1420]) * _3090)) + (vec3(sInClamped[_1451], sInClamped[_1451 + 1], sInClamped[_1451 + 2]) * _3105)) + (vec3(sInClamped[_1488], sInClamped[_1488 + 1], sInClamped[_1488 + 2]) * _3120);
            int _1504 = (((_1482 + 6) * 28) + (_1485 + 6)) * 3;
            vec3 blurHist = ((((((vec3(sHistory[_1285], sHistory[_1285 + 1], sHistory[_1285 + 2]) * _3030) + (vec3(sHistory[_1321], sHistory[_1321 + 1], sHistory[_1321 + 2]) * _3045)) + (vec3(sHistory[_1358], sHistory[_1358 + 1], sHistory[_1358 + 2]) * _3060)) + (vec3(sHistory[_1395], sHistory[_1395 + 1], sHistory[_1395 + 2]) * _3075)) + (vec3(sHistory[_1430], sHistory[_1430 + 1], sHistory[_1430 + 2]) * _3090)) + (vec3(sHistory[_1467], sHistory[_1467 + 1], sHistory[_1467 + 2]) * _3105)) + (vec3(sHistory[_1504], sHistory[_1504 + 1], sHistory[_1504 + 2]) * _3120);
            if (true)
            {
                _3121 = 0.5;
            }
            else
            {
                _3121 = 0.25;
            }
            if (false)
            {
                _3122 = 0.5;
            }
            else
            {
                _3122 = 0.25;
            }
            float _3135 = _3121 * _3122;
            ivec2 _1517 = _3015 + ivec2(0, 1);
            int _1519 = _1517.y;
            int _1522 = _1517.x;
            int _1525 = (((_1519 + 4) * 24) + (_1522 + 4)) * 3;
            int _1541 = (((_1519 + 6) * 28) + (_1522 + 6)) * 3;
            if (false)
            {
                _3136 = 0.5;
            }
            else
            {
                _3136 = 0.25;
            }
            if (false)
            {
                _3137 = 0.5;
            }
            else
            {
                _3137 = 0.25;
            }
            float _3150 = _3136 * _3137;
            ivec2 _1554 = _3015 + ivec2(1);
            int _1556 = _1554.y;
            int _1559 = _1554.x;
            int _1562 = (((_1556 + 4) * 24) + (_1559 + 4)) * 3;
            vec3 blurIn_1 = (blurIn + (vec3(sInClamped[_1525], sInClamped[_1525 + 1], sInClamped[_1525 + 2]) * _3135)) + (vec3(sInClamped[_1562], sInClamped[_1562 + 1], sInClamped[_1562 + 2]) * _3150);
            int _1578 = (((_1556 + 6) * 28) + (_1559 + 6)) * 3;
            vec3 blurHist_1 = (blurHist + (vec3(sHistory[_1541], sHistory[_1541 + 1], sHistory[_1541 + 2]) * _3135)) + (vec3(sHistory[_1578], sHistory[_1578 + 1], sHistory[_1578 + 2]) * _3150);
            sBlur[_1414] = blurIn_1.x;
            sBlur[_1417] = blurIn_1.y;
            sBlur[_1420] = blurIn_1.z;
            int at_5 = (((_1408 + 5) * 26) + (_1411 + 5)) * 3;
            sMid[at_5] = blurHist_1.x;
            sMid[at_5 + 1] = blurHist_1.y;
            sMid[at_5 + 2] = blurHist_1.z;
            i += 128u;
            continue;
        }
        barrier();
        ivec2 _1625 = _160 + ivec2(-1);
        int _1633 = (((_1625.y + 4) * 24) + (_1625.x + 4)) * 3;
        int _1636 = _1633 + 1;
        int _1639 = _1633 + 2;
        vec3 s_36 = vec3(sInClamped[_1633], sInClamped[_1636], sInClamped[_1639]);
        ivec2 _1647 = _160 + ivec2(0, -1);
        int _1655 = (((_1647.y + 4) * 24) + (_1647.x + 4)) * 3;
        int _1658 = _1655 + 1;
        int _1661 = _1655 + 2;
        vec3 s_37 = vec3(sInClamped[_1655], sInClamped[_1658], sInClamped[_1661]);
        ivec2 _1667 = _160 + ivec2(1, -1);
        int _1675 = (((_1667.y + 4) * 24) + (_1667.x + 4)) * 3;
        int _1678 = _1675 + 1;
        int _1681 = _1675 + 2;
        vec3 s_38 = vec3(sInClamped[_1675], sInClamped[_1678], sInClamped[_1681]);
        ivec2 _1687 = _160 + ivec2(-1, 0);
        int _1695 = (((_1687.y + 4) * 24) + (_1687.x + 4)) * 3;
        int _1698 = _1695 + 1;
        int _1701 = _1695 + 2;
        vec3 s_39 = vec3(sInClamped[_1695], sInClamped[_1698], sInClamped[_1701]);
        int _1711 = (((_298 + 4) * 24) + (_301 + 4)) * 3;
        int _1714 = _1711 + 1;
        int _1717 = _1711 + 2;
        vec3 s_40 = vec3(sInClamped[_1711], sInClamped[_1714], sInClamped[_1717]);
        ivec2 _1723 = _160 + ivec2(1, 0);
        int _1731 = (((_1723.y + 4) * 24) + (_1723.x + 4)) * 3;
        int _1734 = _1731 + 1;
        int _1737 = _1731 + 2;
        vec3 s_41 = vec3(sInClamped[_1731], sInClamped[_1734], sInClamped[_1737]);
        ivec2 _1743 = _160 + ivec2(-1, 1);
        int _1751 = (((_1743.y + 4) * 24) + (_1743.x + 4)) * 3;
        int _1754 = _1751 + 1;
        int _1757 = _1751 + 2;
        vec3 s_42 = vec3(sInClamped[_1751], sInClamped[_1754], sInClamped[_1757]);
        ivec2 _1763 = _160 + ivec2(0, 1);
        int _1771 = (((_1763.y + 4) * 24) + (_1763.x + 4)) * 3;
        int _1774 = _1771 + 1;
        int _1777 = _1771 + 2;
        vec3 s_43 = vec3(sInClamped[_1771], sInClamped[_1774], sInClamped[_1777]);
        ivec2 _1783 = _160 + ivec2(1);
        int _1791 = (((_1783.y + 4) * 24) + (_1783.x + 4)) * 3;
        int _1794 = _1791 + 1;
        int _1797 = _1791 + 2;
        vec3 s_44 = vec3(sInClamped[_1791], sInClamped[_1794], sInClamped[_1797]);
        float _1804 = sBlur[_1711];
        float _1806 = sBlur[_1714];
        float _1808 = sBlur[_1717];
        int _1814 = (((_298 + 5) * 26) + (_301 + 5)) * 3;
        float _1816 = sMid[_1814];
        float _1819 = sMid[_1814 + 1];
        float _1822 = sMid[_1814 + 2];
        i = gl_LocalInvocationIndex;
        vec3 boxLo;
        vec3 boxHi;
        for (;;)
        {
            if (!(i < 240u))
            {
                break;
            }
            int _3162 = int(i);
            ivec2 _3167 = ivec2(_3162 - 20 * (_3162 / 20), _3162 / 20) - ivec2(2);
            int _1840 = _3167.y;
            int _1843 = _3167.x;
            int _1846 = (((_1840 + 5) * 26) + (_1843 + 5)) * 3;
            int _1849 = _1846 + 1;
            int _1852 = _1846 + 2;
            vec3 fh = vec3(sMid[_1846], sMid[_1849], sMid[_1852]);
            int _1860 = (((_1840 + 4) * 24) + (_1843 + 4)) * 3;
            int _1863 = _1860 + 1;
            int _1866 = _1860 + 2;
            vec3 inC = vec3(sInClamped[_1860], sInClamped[_1863], sInClamped[_1866]);
            int _1874 = (((_1840 + 6) * 28) + (_1843 + 6)) * 3;
            int _1877 = _1874 + 1;
            int _1880 = _1874 + 2;
            ivec2 r = _3167 + ivec2(-1);
            int _1886 = r.y;
            int _1889 = r.x;
            int _1892 = (((_1886 + 4) * 24) + (_1889 + 4)) * 3;
            int _1895 = _1892 + 1;
            int _1898 = _1892 + 2;
            vec3 c = vec3(sInClamped[_1892], sInClamped[_1895], sInClamped[_1898]);
            vec3 b = vec3(sBlur[_1892], sBlur[_1895], sBlur[_1898]);
            int _1917 = (((_1886 + 6) * 28) + (_1889 + 6)) * 3;
            ivec2 r_1 = _3167 + ivec2(0, -1);
            int _1930 = r_1.y;
            int _1933 = r_1.x;
            int _1936 = (((_1930 + 4) * 24) + (_1933 + 4)) * 3;
            int _1939 = _1936 + 1;
            int _1942 = _1936 + 2;
            vec3 c_1 = vec3(sInClamped[_1936], sInClamped[_1939], sInClamped[_1942]);
            vec3 b_1 = vec3(sBlur[_1936], sBlur[_1939], sBlur[_1942]);
            int _1962 = (((_1930 + 6) * 28) + (_1933 + 6)) * 3;
            ivec2 r_2 = _3167 + ivec2(1, -1);
            int _1976 = r_2.y;
            int _1979 = r_2.x;
            int _1982 = (((_1976 + 4) * 24) + (_1979 + 4)) * 3;
            int _1985 = _1982 + 1;
            int _1988 = _1982 + 2;
            vec3 c_2 = vec3(sInClamped[_1982], sInClamped[_1985], sInClamped[_1988]);
            vec3 b_2 = vec3(sBlur[_1982], sBlur[_1985], sBlur[_1988]);
            int _2008 = (((_1976 + 6) * 28) + (_1979 + 6)) * 3;
            ivec2 r_3 = _3167 + ivec2(-1, 0);
            int _2022 = r_3.y;
            int _2025 = r_3.x;
            int _2028 = (((_2022 + 4) * 24) + (_2025 + 4)) * 3;
            int _2031 = _2028 + 1;
            int _2034 = _2028 + 2;
            vec3 c_3 = vec3(sInClamped[_2028], sInClamped[_2031], sInClamped[_2034]);
            vec3 b_3 = vec3(sBlur[_2028], sBlur[_2031], sBlur[_2034]);
            int _2054 = (((_2022 + 6) * 28) + (_2025 + 6)) * 3;
            vec3 c_4 = vec3(sInClamped[_1860], sInClamped[_1863], sInClamped[_1866]);
            vec3 b_4 = vec3(sBlur[_1860], sBlur[_1863], sBlur[_1866]);
            ivec2 r_4 = _3167 + ivec2(1, 0);
            int _2091 = r_4.y;
            int _2094 = r_4.x;
            int _2097 = (((_2091 + 4) * 24) + (_2094 + 4)) * 3;
            int _2100 = _2097 + 1;
            int _2103 = _2097 + 2;
            vec3 c_5 = vec3(sInClamped[_2097], sInClamped[_2100], sInClamped[_2103]);
            vec3 b_5 = vec3(sBlur[_2097], sBlur[_2100], sBlur[_2103]);
            int _2123 = (((_2091 + 6) * 28) + (_2094 + 6)) * 3;
            vec3 sumDiff_1 = ((((abs(vec3(sInput[_1917], sInput[_1917 + 1], sInput[_1917 + 2]) - c) + abs(vec3(sInput[_1962], sInput[_1962 + 1], sInput[_1962 + 2]) - c_1)) + abs(vec3(sInput[_2008], sInput[_2008 + 1], sInput[_2008 + 2]) - c_2)) + abs(vec3(sInput[_2054], sInput[_2054 + 1], sInput[_2054 + 2]) - c_3)) + abs(vec3(sInput[_1874], sInput[_1877], sInput[_1880]) - c_4)) + abs(vec3(sInput[_2123], sInput[_2123 + 1], sInput[_2123 + 2]) - c_5);
            ivec2 r_5 = _3167 + ivec2(-1, 1);
            int _2137 = r_5.y;
            int _2140 = r_5.x;
            int _2143 = (((_2137 + 4) * 24) + (_2140 + 4)) * 3;
            int _2146 = _2143 + 1;
            int _2149 = _2143 + 2;
            vec3 c_6 = vec3(sInClamped[_2143], sInClamped[_2146], sInClamped[_2149]);
            vec3 b_6 = vec3(sBlur[_2143], sBlur[_2146], sBlur[_2149]);
            int _2169 = (((_2137 + 6) * 28) + (_2140 + 6)) * 3;
            ivec2 r_6 = _3167 + ivec2(0, 1);
            int _2183 = r_6.y;
            int _2186 = r_6.x;
            int _2189 = (((_2183 + 4) * 24) + (_2186 + 4)) * 3;
            int _2192 = _2189 + 1;
            int _2195 = _2189 + 2;
            vec3 c_7 = vec3(sInClamped[_2189], sInClamped[_2192], sInClamped[_2195]);
            vec3 b_7 = vec3(sBlur[_2189], sBlur[_2192], sBlur[_2195]);
            int _2215 = (((_2183 + 6) * 28) + (_2186 + 6)) * 3;
            ivec2 r_7 = _3167 + ivec2(1);
            int _2229 = r_7.y;
            int _2232 = r_7.x;
            int _2235 = (((_2229 + 4) * 24) + (_2232 + 4)) * 3;
            int _2238 = _2235 + 1;
            int _2241 = _2235 + 2;
            vec3 c_8 = vec3(sInClamped[_2235], sInClamped[_2238], sInClamped[_2241]);
            vec3 b_8 = vec3(sBlur[_2235], sBlur[_2238], sBlur[_2241]);
            vec3 _2254 = min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), b), b_1), b_2), b_3), b_4), b_5), b_6), b_7), b_8);
            vec3 _2255 = max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), b), b_1), b_2), b_3), b_4), b_5), b_6), b_7), b_8);
            int _2261 = (((_2229 + 6) * 28) + (_2232 + 6)) * 3;
            if (p.control.w > 0.0)
            {
                float _2291 = clamp(texelFetch(_3329, ivec3(clamp(origin + _3167, ivec2(0), _145), 0).xy, 0).y * p.control.w, 0.0, 1.0);
                if (_2291 > 0.0078740157186985015869140625)
                {
                    int _2299 = (((_1886 + 5) * 26) + (_1889 + 5)) * 3;
                    vec3 b_9 = vec3(sMid[_2299], sMid[_2299 + 1], sMid[_2299 + 2]);
                    int _2315 = (((_1930 + 5) * 26) + (_1933 + 5)) * 3;
                    vec3 b_10 = vec3(sMid[_2315], sMid[_2315 + 1], sMid[_2315 + 2]);
                    int _2331 = (((_1976 + 5) * 26) + (_1979 + 5)) * 3;
                    vec3 b_11 = vec3(sMid[_2331], sMid[_2331 + 1], sMid[_2331 + 2]);
                    int _2347 = (((_2022 + 5) * 26) + (_2025 + 5)) * 3;
                    vec3 b_12 = vec3(sMid[_2347], sMid[_2347 + 1], sMid[_2347 + 2]);
                    vec3 b_13 = vec3(sMid[_1846], sMid[_1849], sMid[_1852]);
                    int _2369 = (((_2091 + 5) * 26) + (_2094 + 5)) * 3;
                    vec3 b_14 = vec3(sMid[_2369], sMid[_2369 + 1], sMid[_2369 + 2]);
                    int _2385 = (((_2137 + 5) * 26) + (_2140 + 5)) * 3;
                    vec3 b_15 = vec3(sMid[_2385], sMid[_2385 + 1], sMid[_2385 + 2]);
                    int _2401 = (((_2183 + 5) * 26) + (_2186 + 5)) * 3;
                    vec3 b_16 = vec3(sMid[_2401], sMid[_2401 + 1], sMid[_2401 + 2]);
                    int _2417 = (((_2229 + 5) * 26) + (_2232 + 5)) * 3;
                    vec3 b_17 = vec3(sMid[_2417], sMid[_2417 + 1], sMid[_2417 + 2]);
                    vec3 _2430 = vec3(_2291);
                    boxLo = mix(_2254, min(_2254, min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), b_9), b_10), b_11), b_12), b_13), b_14), b_15), b_16), b_17)), _2430);
                    boxHi = mix(_2255, max(_2255, max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), b_9), b_10), b_11), b_12), b_13), b_14), b_15), b_16), b_17)), _2430);
                }
                else
                {
                    boxLo = _2254;
                    boxHi = _2255;
                }
            }
            else
            {
                boxLo = _2254;
                boxHi = _2255;
            }
            vec3 _2460 = max(vec3(0.00048828125), min(abs(((abs(vec3(sInput[_1874], sInput[_1877], sInput[_1880]) - inC) * 9.0) - (((sumDiff_1 + abs(vec3(sInput[_2169], sInput[_2169 + 1], sInput[_2169 + 2]) - c_6)) + abs(vec3(sInput[_2215], sInput[_2215 + 1], sInput[_2215 + 2]) - c_7)) + abs(vec3(sInput[_2261], sInput[_2261 + 1], sInput[_2261 + 2]) - c_8))) * vec3(0.125)), abs(((inC * 9.0) - ((((((((c + c_1) + c_2) + c_3) + c_4) + c_5) + c_6) + c_7) + c_8)) * vec3(0.125))));
            vec3 err = max(_2460, (max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), c), c_1), c_2), c_3), c_4), c_5), c_6), c_7), c_8) - min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), c), c_1), c_2), c_3), c_4), c_5), c_6), c_7), c_8)) * vec3(0.0625)) + vec3(0.00048828125);
            vec3 _2475 = abs(clamp(fh, boxLo - err, boxHi + err) - fh);
            sHistory[_1874] = _2475.x;
            sHistory[_1877] = _2475.y;
            sHistory[_1880] = _2475.z;
            i += 128u;
            continue;
        }
        barrier();
        i = gl_LocalInvocationIndex;
        for (;;)
        {
            if (!(i < 180u))
            {
                break;
            }
            int _3179 = int(i);
            ivec2 _3184 = ivec2(_3179 - 18 * (_3179 / 18), _3179 / 18) - ivec2(1);
            ivec2 _2503 = _3184 + ivec2(-1);
            int _2510 = (((_2503.y + 6) * 28) + (_2503.x + 6)) * 3;
            vec3 _2519 = vec3(sHistory[_2510], sHistory[_2510 + 1], sHistory[_2510 + 2]);
            ivec2 _2522 = _3184 + ivec2(0, -1);
            int _2529 = (((_2522.y + 6) * 28) + (_2522.x + 6)) * 3;
            vec3 _2538 = vec3(sHistory[_2529], sHistory[_2529 + 1], sHistory[_2529 + 2]);
            ivec2 _2541 = _3184 + ivec2(1, -1);
            int _2548 = (((_2541.y + 6) * 28) + (_2541.x + 6)) * 3;
            vec3 _2557 = vec3(sHistory[_2548], sHistory[_2548 + 1], sHistory[_2548 + 2]);
            ivec2 _2560 = _3184 + ivec2(-1, 0);
            int _2567 = (((_2560.y + 6) * 28) + (_2560.x + 6)) * 3;
            vec3 _2576 = vec3(sHistory[_2567], sHistory[_2567 + 1], sHistory[_2567 + 2]);
            int _2579 = _3184.y;
            int _2582 = _3184.x;
            int _2585 = (((_2579 + 6) * 28) + (_2582 + 6)) * 3;
            vec3 _2594 = vec3(sHistory[_2585], sHistory[_2585 + 1], sHistory[_2585 + 2]);
            ivec2 _2597 = _3184 + ivec2(1, 0);
            int _2604 = (((_2597.y + 6) * 28) + (_2597.x + 6)) * 3;
            vec3 _2613 = vec3(sHistory[_2604], sHistory[_2604 + 1], sHistory[_2604 + 2]);
            ivec2 _2616 = _3184 + ivec2(-1, 1);
            int _2623 = (((_2616.y + 6) * 28) + (_2616.x + 6)) * 3;
            vec3 _2632 = vec3(sHistory[_2623], sHistory[_2623 + 1], sHistory[_2623 + 2]);
            ivec2 _2636 = _3184 + ivec2(0, 1);
            int _2643 = (((_2636.y + 6) * 28) + (_2636.x + 6)) * 3;
            vec3 _2652 = vec3(sHistory[_2643], sHistory[_2643 + 1], sHistory[_2643 + 2]);
            ivec2 _2655 = _3184 + ivec2(1);
            int _2662 = (((_2655.y + 6) * 28) + (_2655.x + 6)) * 3;
            vec3 _2671 = vec3(sHistory[_2662], sHistory[_2662 + 1], sHistory[_2662 + 2]);
            int at_6 = (((_2579 + 4) * 24) + (_2582 + 4)) * 3;
            vec3 _3205 = min(_2538, _2557);
            vec3 _3206 = max(_2538, _2557);
            vec3 _3207 = min(_2594, _2613);
            vec3 _3208 = max(_2594, _2613);
            vec3 _3209 = min(_2652, _2671);
            vec3 _3210 = max(_2652, _2671);
            vec3 _3212 = max(_2519, _3205);
            vec3 _3214 = max(_2576, _3207);
            vec3 _3216 = max(_2632, _3209);
            vec3 _3219 = min(_3214, _3208);
            vec3 _3221 = min(_3216, _3210);
            vec3 _3233 = min(max(_3212, _3206), min(max(_3214, _3208), max(_3216, _3210)));
            vec3 _3235 = min(max(min(_3212, _3206), min(_3219, _3221)), max(_3219, _3221));
            vec3 _3241 = min(max(max(max(min(_2519, _3205), min(_2576, _3207)), min(_2632, _3209)), min(_3235, _3233)), max(_3235, _3233));
            sBlur[at_6] = _3241.x;
            sBlur[at_6 + 1] = _3241.y;
            sBlur[at_6 + 2] = _3241.z;
            i += 128u;
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
            vec3 _2830 = max(max(max(max(max(max(max(vec3(0.0), vec3(sBlur[_1633], sBlur[_1636], sBlur[_1639])), vec3(sBlur[_1655], sBlur[_1658], sBlur[_1661])), vec3(sBlur[_1675], sBlur[_1678], sBlur[_1681])), vec3(sBlur[_1695], sBlur[_1698], sBlur[_1701])), vec3(sBlur[_1711], sBlur[_1714], sBlur[_1717])), vec3(sBlur[_1731], sBlur[_1734], sBlur[_1737])), vec3(sBlur[_1751], sBlur[_1754], sBlur[_1757]));
            vec3 _2852 = vec3(1.0) - (max(max(_2830, vec3(sBlur[_1771], sBlur[_1774], sBlur[_1777])), vec3(sBlur[_1791], sBlur[_1794], sBlur[_1797])) / max(max(abs(vec3(_1804, _1806, _1808) - vec3(_1816, _1819, _1822)), ((max(max(max(max(max(max(max(max(max(vec3(-1000000015047466219876688855040.0), s_36), s_37), s_38), s_39), s_40), s_41), s_42), s_43), s_44) - min(min(min(min(min(min(min(min(min(vec3(1000000015047466219876688855040.0), s_36), s_37), s_38), s_39), s_40), s_41), s_42), s_43), s_44)) * 0.25) + vec3(0.000244140625)), vec3(9.9999999747524270787835121154785e-07)));
            vec3 _3245 = clamp(_2852, vec3(0.0), vec3(1.0));
            float _2866 = min(_3245.x, min(_3245.y, _3245.z));
            uvec2 _2867 = uvec2(px);
            imageStore(uRejection, ivec2(_2867), vec4(_2866, 0.0, 0.0, 0.0));
            float parallax;
            if (p.control.z > 0.5)
            {
                parallax = textureLod(_3331, (vec2(px) + vec2(0.5)) * p.size.zw, 0.0).x;
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
                total = clamp(1.0 - (4.0 * _2866), 0.0, 1.0);
            }
            imageStore(uGuide, ivec2(_2867), vec4(mix(vec3(_315, _317, _319), vec3(_306, _309, _312), vec3(max(p.control.y, max(1.0 - _2866, total)))), 1.0));
        }
    }
}

