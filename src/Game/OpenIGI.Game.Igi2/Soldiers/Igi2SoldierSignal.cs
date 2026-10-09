using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <summary>
/// The soldier's locomotion signal word, <c>tState.bSignal</c>, as <c>HumanSoldier_StateSignalHandler</c> (<c>igi2 0x4E4010</c>)
/// packs it from the channels its mind writes.
/// </summary>
[Flags]
public enum Igi2SoldierSignal : uint
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The main clip has played out (the active main controller's ended bit).</summary>
    MainEnded = 0x1,

    /// <summary>On the ground, raised by the ground collision.</summary>
    Grounded = 0x2,

    /// <summary>Kept from the tick before: set by the body itself.</summary>
    Kept4 = 0x4,

    /// <summary>Kept from the tick before; with <see cref="Kneel"/> it sends a standing soldier down to a kneel.</summary>
    Kept8 = 0x8,

    /// <summary>The upper-body clip has played out.</summary>
    UpperBodyEnded = 0x10,

    /// <summary>Strafe left, channel 2.</summary>
    StrafeLeft = 0x20,

    /// <summary>Strafe right, channel 3.</summary>
    StrafeRight = 0x40,

    /// <summary>Go forward, channel 4.</summary>
    Forward = 0x80,

    /// <summary>Go backward, channel 5.</summary>
    Backward = 0x100,

    /// <summary>Fire, channel 6.</summary>
    Fire = 0x200,

    /// <summary>Channel 7.</summary>
    Channel7 = 0x400,

    /// <summary>Reload, channel 8.</summary>
    Reload = 0x800,

    /// <summary>Channel 9.</summary>
    Channel9 = 0x1000,

    /// <summary>Kneel, channel 24.</summary>
    Kneel = 0x2000,

    /// <summary>Kept: bits the body raises itself.</summary>
    Kept4000 = 0x4000,

    /// <summary>Kept: bits the body raises itself.</summary>
    Kept8000 = 0x8000,

    /// <summary>Hit this tick, raised by the damage handlers and dropped after the upper body runs.</summary>
    Hit = 0x10000,

    /// <summary>Dead, raised by <c>Human_UpdateDie</c>.</summary>
    Dead = 0x20000,

    /// <summary>The soldier walks rather than runs, <c>isWalk</c>.</summary>
    Walk = 0x40000,

    /// <summary>Press a switch, channel 35.</summary>
    PressSwitch = 0x80000,

    /// <summary>Lie down, channel 25.</summary>
    LieDown = 0x100000,

    /// <summary>Play a stand action, channel 26.</summary>
    StandAction = 0x200000,

    /// <summary>Activate something, channel 27; the channel holds what.</summary>
    Activate = 0x400000,

    /// <summary>Tense, weapon up, channel 29.</summary>
    Tense = 0x800000,

    /// <summary>Aim, channel 30.</summary>
    Aim = 0x1000000,

    /// <summary>Throw a grenade, channel 32.</summary>
    ThrowGrenade = 0x2000000,

    /// <summary>Close combat, channel 33.</summary>
    CloseCombat = 0x4000000,

    /// <summary>Kept: bits the body raises itself.</summary>
    Kept8000000 = 0x8000000,

    /// <summary>Kept: bits the body raises itself.</summary>
    Kept10000000 = 0x10000000,

    /// <summary>Turn on the spot, channel 34.</summary>
    Turn = 0x20000000,

    /// <summary>Roll left, channel 36.</summary>
    RollLeft = 0x40000000,

    /// <summary>Roll right, channel 37.</summary>
    RollRight = 0x80000000,

    /// <summary>The bits the handler keeps from the word before, <c>0x1803C00E</c>.</summary>
    KeptMask = 0x1803C00E,
}

/// <summary>The channel slots a soldier's mind writes, as <c>HumanSoldier_StateSignalHandler</c> (<c>igi2 0x4E4010</c>) reads them.</summary>
/// <remarks>The same block the player's input fills, read with the soldier's own meanings.</remarks>
public static class Igi2SoldierChannel
{
    /// <summary>Strafe left.</summary>
    public const Igi2HumanChannel StrafeLeft = (Igi2HumanChannel)2;

    /// <summary>Strafe right.</summary>
    public const Igi2HumanChannel StrafeRight = (Igi2HumanChannel)3;

    /// <summary>Go forward.</summary>
    public const Igi2HumanChannel Forward = (Igi2HumanChannel)4;

    /// <summary>Go backward.</summary>
    public const Igi2HumanChannel Backward = (Igi2HumanChannel)5;

    /// <summary>Fire.</summary>
    public const Igi2HumanChannel Fire = (Igi2HumanChannel)6;

    /// <summary>Channel 7.</summary>
    public const Igi2HumanChannel Channel7 = (Igi2HumanChannel)7;

    /// <summary>Reload.</summary>
    public const Igi2HumanChannel Reload = (Igi2HumanChannel)8;

    /// <summary>Channel 9.</summary>
    public const Igi2HumanChannel Channel9 = (Igi2HumanChannel)9;

    /// <summary>Toggles walking and running, <c>HumanSoldier_Update</c> (<c>igi2 0x4E3F50</c>).</summary>
    public const Igi2HumanChannel ToggleWalk = (Igi2HumanChannel)23;

    /// <summary>Kneel.</summary>
    public const Igi2HumanChannel Kneel = (Igi2HumanChannel)24;

    /// <summary>Lie down.</summary>
    public const Igi2HumanChannel LieDown = (Igi2HumanChannel)25;

    /// <summary>Play a stand action.</summary>
    public const Igi2HumanChannel StandAction = (Igi2HumanChannel)26;

    /// <summary>Activate: the task id of what is activated.</summary>
    public const Igi2HumanChannel Activate = (Igi2HumanChannel)27;

    /// <summary>Tense.</summary>
    public const Igi2HumanChannel Tense = (Igi2HumanChannel)29;

    /// <summary>Aim.</summary>
    public const Igi2HumanChannel Aim = (Igi2HumanChannel)30;

    /// <summary>A weapon to take, by its <c>WEAPON_ID_*</c> number.</summary>
    public const Igi2HumanChannel SelectWeapon = (Igi2HumanChannel)31;

    /// <summary>Throw a grenade.</summary>
    public const Igi2HumanChannel ThrowGrenade = (Igi2HumanChannel)32;

    /// <summary>Close combat.</summary>
    public const Igi2HumanChannel CloseCombat = (Igi2HumanChannel)33;

    /// <summary>Turn on the spot.</summary>
    public const Igi2HumanChannel Turn = (Igi2HumanChannel)34;

    /// <summary>Press a switch.</summary>
    public const Igi2HumanChannel PressSwitch = (Igi2HumanChannel)35;

    /// <summary>Roll left.</summary>
    public const Igi2HumanChannel RollLeft = (Igi2HumanChannel)36;

    /// <summary>Roll right.</summary>
    public const Igi2HumanChannel RollRight = (Igi2HumanChannel)37;
}

/// <summary>The soldier's states, the slots of <c>HumanSoldier_apfState</c> (<c>HumanSoldier_InitStateFunctions</c>, <c>igi2 0x4E25F0</c>).</summary>
public static class Igi2SoldierState
{
    /// <summary>The number of slots.</summary>
    public const int Count = 153;

    /// <summary>Standing.</summary>
    public const int Stand = 0;

    /// <summary>Turning on the spot.</summary>
    public const int StandTurn = 1;

    /// <summary>Running forward in a panic.</summary>
    public const int PanicForward = 3;

    /// <summary>Running forward.</summary>
    public const int RunForward = 5;

    /// <summary>Walking forward.</summary>
    public const int WalkForward = 6;

    /// <summary>Rolling left from standing.</summary>
    public const int StandRollLeft = 8;

    /// <summary>Rolling right from standing.</summary>
    public const int StandRollRight = 9;

    /// <summary>Throwing a grenade.</summary>
    public const int StandThrowGrenade = 12;

    /// <summary>Close combat.</summary>
    public const int StandCloseCombat = 14;

    /// <summary>Going down to a kneel.</summary>
    public const int StandToKneel = 19;

    /// <summary>Getting up from a kneel.</summary>
    public const int KneelToStand = 20;

    /// <summary>Lying down.</summary>
    public const int StandToLieDown = 21;

    /// <summary>Getting up from lying.</summary>
    public const int LieDownToStand = 22;

    /// <summary>Running forward, tense.</summary>
    public const int TenseRunForward = 23;

    /// <summary>Running left, tense.</summary>
    public const int TenseRunLeft = 24;

    /// <summary>Running right, tense.</summary>
    public const int TenseRunRight = 25;

    /// <summary>Running backward, tense.</summary>
    public const int TenseRunBackward = 26;

    /// <summary>Kneeling.</summary>
    public const int Kneel = 27;

    /// <summary>Turning while kneeling.</summary>
    public const int KneelTurn = 29;

    /// <summary>Rolling left from a kneel.</summary>
    public const int KneelRollLeft = 30;

    /// <summary>Rolling right from a kneel.</summary>
    public const int KneelRollRight = 31;

    /// <summary>Lying down from a kneel.</summary>
    public const int KneelToLieDown = 35;

    /// <summary>Falling.</summary>
    public const int Fall = 39;

    /// <summary>Killed silently.</summary>
    public const int SilentKill = 40;

    /// <summary>Hit.</summary>
    public const int Hit = 41;

    /// <summary>Hit while lying.</summary>
    public const int LieDownHit = 42;

    /// <summary>Dying from a kneel, the slot the dead bit sends a kneeling soldier to.</summary>
    public const int KneelDie = 55;

    /// <summary>Dying, the slot the dead bit sends a standing soldier to.</summary>
    public const int Die = 71;

    /// <summary>A stand action.</summary>
    public const int StandAction = 84;

    /// <summary>On a mounted gun.</summary>
    public const int StationaryGun = 85;

    /// <summary>At a door.</summary>
    public const int Door = 86;

    /// <summary>Pressing a switch.</summary>
    public const int StandPressSwitch = 100;

    /// <summary>Dead.</summary>
    public const int Dead = 101;
}

/// <summary>
/// The base animation each of the soldier's states plays, <c>HumanSoldier_atStateData[n].iWeaponTypeAnim</c>, as
/// <c>HumanSoldier_InitAnimStateIndexes</c> (<c>igi2 0x4E2020</c>) fills it.
/// </summary>
public static class Igi2SoldierStateAnimations
{
    /// <summary>The table, by state slot.</summary>
    public static IReadOnlyList<int> Table { get; } =
    [
        0, 1, 2, 26, 4, 62, 13, 3, 22, 23, // 0
        78, 79, 46, 47, 48, 60, 233, 78, 78, 24, // 10
        114, 25, 127, 62, 63, 64, 67, 100, 101, 107, // 20
        113, 112, 110, 111, 110, 115, 120, 121, 122, 0, // 30
        238, 143, 126, 187, 188, 189, 190, 191, 207, 193, // 40
        194, 195, 196, 197, 198, 199, 200, 201, 202, 203, // 50
        204, 205, 206, 0, 208, 209, 210, 211, 212, 213, // 60
        214, 215, 216, 217, 218, 219, 220, 221, 222, 223, // 70
        224, 225, 226, 227, 0, 21, 0, 151, 159, 147, // 80
        155, 143, 52, 117, 163, 54, 55, 56, 57, 58, // 90
        59, 120, 60, 61, 34, 35, 36, 37, 38, 39, // 100
        40, 41, 42, 43, 44, 144, 145, 146, 148, 149, // 110
        150, 152, 153, 154, 156, 157, 158, 160, 161, 162, // 120
        164, 165, 166, 167, 168, 169, 170, 171, 172, 173, // 130
        174, 175, 176, 177, 178, 183, 184, 185, 186, 179, // 140
        180, 181, 182, // 150
    ];
}
