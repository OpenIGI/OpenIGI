namespace OpenIGI.Game.Player;

/// <summary>
/// The thirteen states of a female soldier's body machine and the clip each plays — the table at
/// <c>0x53E4A0</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>HumanSoldierFemale</c>'s constructor (<c>0x454B10</c>) builds its machine over <c>off_53E400</c>
/// and installs this table, so none of these ids means what the same number means for a soldier. The
/// machine is a soldier's cut down to one stance: no kneel, no prone, no roll, no grenade, and a single
/// firing state reached through a raise.
/// </para>
/// <para>
/// State meanings come from the entry functions <c>0x453A30</c>..<c>0x454940</c> and their per-ticks;
/// see <see cref="HumanSoldierFemaleBodyMachine"/> for the transitions.
/// </para>
/// </remarks>
public static class HumanSoldierFemaleStates
{
    /// <summary>Standing — <c>0x453A30</c>. Plays the declared stand animation, else 6.</summary>
    public const int StandState = 0;

    /// <summary>Walking forward — <c>0x453C40</c>, animation 3.</summary>
    public const int WalkState = 1;

    /// <summary>Running forward — <c>0x453DE0</c>, animation 2.</summary>
    public const int RunState = 2;

    /// <summary>Firing — <c>0x453F80</c>, animation 9. The only state that forwards the trigger.</summary>
    public const int FiringState = 3;

    /// <summary>The displayed reload — <c>0x4540D0</c>, animation 15.</summary>
    public const int ReloadState = 4;

    /// <summary>Weapon raised and aimed — <c>0x4541F0</c>, animation 14.</summary>
    public const int AimState = 5;

    /// <summary>Raising the weapon — <c>0x454300</c>, animation 7.</summary>
    public const int RaiseWeaponState = 6;

    /// <summary>Lowering the weapon — <c>0x4543E0</c>, animation 8.</summary>
    public const int LowerWeaponState = 7;

    /// <summary>Off the ground — <c>0x4544C0</c>.</summary>
    public const int AirborneState = 8;

    /// <summary>Flinching — <c>0x4545E0</c>. Plays the struck zone's limb animation.</summary>
    public const int FlinchState = 9;

    /// <summary>Dying — <c>0x4546C0</c>. Plays the struck zone's first reaction animation.</summary>
    public const int DeathState = 10;

    /// <summary>Playing the animation the AI named — <c>0x4547F0</c>, looped.</summary>
    public const int ScriptedAnimationState = 11;

    /// <summary>The use-object animation — <c>0x454940</c>. Only the object dispatch enters it.</summary>
    public const int UseObjectState = 12;

    /// <summary>Gets the female soldier's <c>{animation, mode}</c> table, transcribed from <c>0x53E4A0</c>.</summary>
    public static HumanBodyStateTable Table { get; } = new(
        (6, 1), (3, 1), (2, 1), (9, 1), (15, 0), (14, 0), (7, 0), (8, 0), (6, 1), (0, 0),
        (0, 0), (0, 0), (6, 0));
}
