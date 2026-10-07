using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Igi2.Animation;

/// <summary>The family a weapon type borrows clips from when it has none of its own, <c>WeaponType+668</c>.</summary>
public enum Igi2WeaponAnimFamily
{
    /// <summary><c>rifle</c>.</summary>
    Rifle = 0,

    /// <summary><c>pistol</c>.</summary>
    Pistol = 1,

    /// <summary><c>shotgun</c>.</summary>
    Shotgun = 2,

    /// <summary><c>knife</c>.</summary>
    Knife = 3,

    /// <summary><c>grenade</c>.</summary>
    Grenade = 4,

    /// <summary><c>binocular</c>.</summary>
    Binocular = 5,

    /// <summary><c>medipack</c>.</summary>
    Medipack = 6,

    /// <summary><c>mine</c>.</summary>
    Mine = 7,

    /// <summary><c>bomb</c>.</summary>
    Bomb = 8,

    /// <summary><c>special</c>.</summary>
    Special = 9,
}

/// <summary>
/// The clips a weapon type gives a body, one per base animation: <c>WeaponTypeAnim_MakeAnimTable</c>
/// (<c>igi2 0x4F9CA0</c>) over the base names <c>WeaponTypeAnim_InitBaseAnimNameTable</c> (<c>0x4F7940</c>) sets.
/// </summary>
/// <remarks>
/// <para>
/// A state names a base animation by index (a human's state-animation table); the weapon it holds turns that into a
/// clip. For each base name the table tries, on the body's rig: the base name, an underscore and the weapon's own
/// animation name, lower-cased (<c>stand_ak47</c>); then the base name and the weapon's family (<c>stand_rifle</c>);
/// then the base name alone; then <c>stand_lowerbody</c>. Only the first is lower-cased, so a clip whose name has a
/// capital in it is never found by the first attempt.
/// </para>
/// <para>
/// Several slots share a name, and two are left empty (they are never assigned and find nothing).
/// </para>
/// </remarks>
public static class Igi2WeaponTypeAnims
{
    /// <summary>How many base animations there are.</summary>
    public const int Count = 239;

    /// <summary>The weapon-raise base a state plays backwards, to lower the weapon: <c>233</c>.</summary>
    public const int LowerWeapon = 233;

    /// <summary>The lying weapon-raise base a state plays backwards: <c>235</c>.</summary>
    public const int LowerWeaponLyingFlat = 235;

    /// <summary>The clip a table falls back to when nothing else is found.</summary>
    public const string LastResort = "stand_lowerbody";

    private static readonly string[] Families =
        ["rifle", "pistol", "shotgun", "knife", "grenade", "binocular", "medipack", "mine", "bomb", "special"];

    private static readonly string[] Names = BuildNames();

    /// <summary>Gets each base animation's name, by index.</summary>
    public static IReadOnlyList<string> BaseNames => Names;

    /// <summary>Builds a weapon type's clips for one rig.</summary>
    /// <param name="library">The level's clips.</param>
    /// <param name="rig">The rig the clips must be authored on.</param>
    /// <param name="weaponAnimName">The weapon type's own animation name.</param>
    /// <param name="family">The family it borrows from.</param>
    /// <returns>One clip per base animation, <see langword="null"/> where nothing at all is found.</returns>
    public static Igi2AnimationClip?[] BuildTable(
        Igi2AnimationLibrary library,
        Igi2BoneHierarchy rig,
        string weaponAnimName,
        Igi2WeaponAnimFamily family)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(weaponAnimName);
        string familyName = Families[(int)family];

        Igi2AnimationClip?[] table = new Igi2AnimationClip?[Count];
        for (int i = 0; i < Count; i++)
        {
            string name = Names[i];
            table[i] = library.Find(rig, (name + "_" + weaponAnimName).ToLowerInvariant())
                ?? library.Find(rig, name + "_" + familyName)
                ?? library.Find(rig, name)
                ?? library.Find(rig, LastResort);
        }

        return table;
    }

    private static string[] BuildNames()
    {
        string[] n = new string[Count];
        n[0] = "stand";
        n[1] = "stand_turn_mp_shuffle";
        n[2] = "stand_lowerbody";
        n[3] = "civilian_walk";
        n[4] = "stand_run_fwd_full";
        n[5] = "stand_run_forward";
        n[6] = "stand_run_left";
        n[7] = "stand_run_right";
        n[8] = "stand_run_forward_left";
        n[9] = "stand_run_forward_right";
        n[10] = "stand_run_backward";
        n[11] = "stand_run_backward_left";
        n[12] = "stand_run_backward_right";
        n[13] = "stand_walk_forward";
        n[14] = "stand_walk_left";
        n[15] = "stand_walk_right";
        n[16] = "stand_walk_forward_left";
        n[17] = "stand_walk_forward_right";
        n[18] = "stand_walk_backward";
        n[19] = "stand_walk_backward_left";
        n[20] = "stand_walk_backward_right";
        n[21] = "stand_fire_stationarygun";
        n[22] = "stand_roll_left_to_kneel";
        n[23] = "stand_roll_right_to_kneel";
        n[24] = "stand_to_kneel";
        n[25] = "stand_to_flat";
        n[26] = "stand_run_panick";
        n[27] = "stand_kick_grenade";
        n[28] = "stand_alert";
        n[29] = "stand_look_about_stunned";
        n[30] = "stand_flashbang_stunned";
        n[31] = "stand_salute";
        n[32] = "stand_talk1";
        n[33] = "stand_talk2";
        n[34] = "stand_bugged_by_fly";
        n[35] = "stand_clean_weapon";
        n[36] = "stand_sneze";
        n[37] = "stand_wipe_nose";
        n[38] = "stand_streatch_neck";
        n[39] = "stand_scratch_ass";
        n[40] = "stand_clean_weapon";
        n[41] = "stand_brush_pants";
        n[42] = "stand_loose";
        n[43] = "stand_inspect_weapon";
        n[44] = "stand_study_ground";
        n[45] = "stand_walk_look_about";
        n[46] = "stand_throw_grenade";
        n[47] = "stand_lowthrow_grenade";
        n[48] = "stand_close_combat";
        n[49] = "stand_walk_unarmed";
        n[50] = "stand_run_unarmed";
        n[51] = "stand_unarmed";
        n[52] = "running_jump";
        n[53] = "stand_jump_land";
        n[54] = "pick_lock";
        n[55] = "tba_filing_search";
        n[56] = "stand";
        n[57] = "none";
        n[58] = "hack_computer";
        n[59] = "press_alarm";
        n[60] = "tense";
        n[61] = "tense_lowerbody";
        n[62] = "stand_run_forward";
        n[63] = "stand_run_left";
        n[64] = "stand_run_right";
        n[65] = "stand_run_forward_left";
        n[66] = "stand_run_forward_right";
        n[67] = "stand_run_backward";
        n[68] = "stand_run_backward_left";
        n[69] = "stand_run_backward_right";
        n[70] = "tense_walk_forward";
        n[71] = "tense_walk_left";
        n[72] = "tense_walk_right";
        n[73] = "tense_walk_forward_left";
        n[74] = "tense_walk_forward_right";
        n[75] = "tense_walk_backward";
        n[76] = "tense_walk_backward_left";
        n[77] = "tense_walk_backward_right";
        n[78] = "fire";
        n[79] = "reload";
        n[80] = "reload_loop";
        n[81] = "reload_finish";
        n[82] = "tense_look_forward";
        n[83] = "tense_look_left";
        n[84] = "tense_look_right";
        n[85] = "tense_look_forward_left";
        n[86] = "tense_look_forward_right";
        n[87] = "tense_look_backward";
        n[88] = "tense_look_backward_left";
        n[89] = "tense_look_backward_right";
        n[90] = "tense_turn_left";
        n[91] = "tense_turn_right";
        n[92] = "tense_turn_forward_left";
        n[93] = "tense_turn_forward_right";
        n[94] = "tense_turn_backward";
        n[95] = "tense_turn_backward_left";
        n[96] = "tense_turn_backward_right";
        n[97] = "tense_to_stand";
        n[98] = "tense_to_kneel";
        n[99] = "tense_to_flat";
        n[100] = "kneel";
        n[101] = "kneel_lowerbody";
        n[102] = "kneel_run_forward";
        n[103] = "kneel_run_left";
        n[104] = "kneel_run_right";
        n[105] = "kneel_run_forward_left";
        n[106] = "kneel_run_forward_right";
        n[107] = "kneel_run_backward";
        n[108] = "kneel_run_backward_left";
        n[109] = "kneel_run_backward_right";
        n[110] = "kneel_fire";
        n[111] = "kneel_reload";
        n[112] = "kneel_roll_right_to_kneel";
        n[113] = "kneel_roll_left_to_kneel";
        n[114] = "kneel_to_stand";
        n[115] = "kneel_to_flat";
        n[116] = "kneel_to_tense";
        n[117] = "crouch_jump";
        n[118] = "crouch_jump_land";
        n[119] = "crouch_turn";
        n[120] = "lieflat";
        n[121] = "lieflat_fire";
        n[122] = "lieflat_reload";
        n[123] = "lieflat_reload_loop";
        n[124] = "lieflat_reload_finish";
        n[125] = "lieflat_die_headshot";
        n[126] = "lieflat_hit";
        n[127] = "lieflat_to_stand";
        n[128] = "crawl";
        n[129] = "crawl";
        n[130] = "crawl";
        n[131] = "crawl";
        n[132] = "fly";
        n[133] = "sit_drive";
        n[134] = "sit_type";
        n[135] = "sit_die";
        n[136] = "climb_ladder";
        n[137] = "get_off_ladder_top";
        n[138] = "get_off_ladder_top";
        n[139] = "slide_ladder";
        n[140] = "climb_fence";
        n[141] = "climb_wire";
        n[142] = "slide_wire";
        n[143] = "tense_hit_stomach";
        n[144] = "tense_hit_torso";
        n[145] = "tense_hit_torso1";
        n[146] = "tense_hit_torso2";
        n[147] = "tense_hit_left_leg";
        n[148] = "tense_hit_left_leg1";
        n[149] = "tense_hit_left_leg2";
        n[150] = "tense_hit_left_leg2";
        n[151] = "tense_hit_right_leg";
        n[152] = "tense_hit_right_leg1";
        n[153] = "tense_hit_right_leg2";
        n[154] = "tense_hit_right_leg3";
        n[155] = "tense_hit_left_arm";
        n[156] = "tense_hit_left_arm1";
        n[157] = "tense_hit_left_arm2";
        n[158] = "tense_hit_left_arm3";
        n[159] = "tense_hit_right_arm";
        n[160] = "tense_hit_right_arm1";
        n[161] = "tense_hit_right_arm2";
        n[162] = "tense_hit_right_arm3";
        n[163] = "tense_hit_stomach";
        n[164] = "tense_hit_torso";
        n[165] = "tense_hit_torso1";
        n[166] = "tense_hit_torso2";
        n[167] = "tense_hit_left_leg1";
        n[168] = "tense_hit_left_leg2";
        n[169] = "tense_hit_left_leg2";
        n[170] = "tense_hit_right_leg1";
        n[171] = "tense_hit_right_leg2";
        n[172] = "tense_hit_right_leg3";
        n[173] = "tense_hit_left_arm1";
        n[174] = "tense_hit_left_arm2";
        n[175] = "tense_hit_left_arm3";
        n[176] = "tense_hit_right_arm1";
        n[177] = "tense_hit_right_arm2";
        n[178] = "tense_hit_right_arm3";
        n[179] = "kneel_hit_right_leg";
        n[180] = "kneel_hit_right_leg1";
        n[181] = "kneel_hit_right_leg2";
        n[182] = "kneel_hit_right_leg3";
        n[183] = "kneel_hit_left_leg";
        n[184] = "kneel_hit_left_leg1";
        n[185] = "kneel_hit_left_leg2";
        n[186] = "kneel_hit_left_leg3";
        n[187] = "lieflat_die_headshot";
        n[188] = "lieflat_die_headshot";
        n[189] = "lieflat_die_normal";
        n[190] = "lieflat_die_normal";
        n[191] = "lieflat_die_front_explosion";
        n[192] = "lieflat_die_back_explosion";
        n[193] = "kneel_die_headshot";
        n[194] = "kneel_die_normal1";
        n[195] = "kneel_die_normal2";
        n[196] = "kneel_die_normal3";
        n[197] = "kneel_die_normal4";
        n[198] = "kneel_die_normal1";
        n[199] = "kneel_die_normal2";
        n[200] = "kneel_die_normal1";
        n[201] = "kneel_die_normal2";
        n[202] = "kneel_die_normal3";
        n[203] = "kneel_die_normal4";
        n[204] = "kneel_die_normal1";
        n[205] = "kneel_die_normal2";
        n[206] = "kneel_die_front_explosion";
        n[207] = "kneel_die_front_explosion";
        n[208] = "stand_die_flying";
        n[209] = "stand_die_headshot";
        n[210] = "stand_die_headshot1";
        n[211] = "stand_die_headshot2";
        n[212] = "stand_die_backshot";
        n[213] = "stand_die_backshot1";
        n[214] = "stand_die_backshot2";
        n[215] = "stand_die_normal";
        n[216] = "stand_die_normal1";
        n[217] = "stand_die_normal2";
        n[218] = "stand_die_normal3";
        n[219] = "stand_die_normal4";
        n[220] = "stand_die_normal5";
        n[221] = "stand_die_normal7";
        n[222] = "stand_die_normal8";
        n[223] = "stand_die_normal6";
        n[224] = "stand_die_front_explosion";
        n[225] = "stand_die_front_explosion";
        n[226] = "stand_die_back_explosion";
        n[227] = "stand_die_back_explosion";
        n[228] = "death_falling";
        n[229] = "swim";
        n[230] = "swim_backward";
        n[231] = "swim_tread_water";
        n[232] = "swim_death";
        n[233] = "weaponraise";
        n[234] = "weaponraise";
        n[235] = "lieflat_weaponraise";
        n[236] = "lieflat_weaponraise";
        n[237] = "defusing_bomb";
        n[238] = "tba_silent_kill_guard";
        return n;
    }
}
