namespace OpenIGI.Game.Player;

/// <summary>
/// The fifteen states of an RPG soldier's body machine and the clip each plays — the table at
/// <c>0x53E720</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>HumanSoldierRPG</c>'s constructor (<c>0x456C70</c>) builds its machine over <c>off_53E6A8</c> and
/// installs this table. The launcher has two ways to fire: a hip shot straight from standing
/// (0 → 4 → 3 → 0) and an aimed shot from the shoulder (0 → 5 → 7 → 10 → 8 → 7), the second held while
/// the AI keeps its kneel channel raised.
/// </para>
/// <para>
/// State meanings come from the entry functions <c>0x455990</c>..<c>0x456960</c> and their per-ticks;
/// see <see cref="HumanSoldierRpgBodyMachine"/> for the transitions.
/// </para>
/// </remarks>
public static class HumanSoldierRpgStates
{
    /// <summary>Standing — <c>0x455990</c>. Plays the declared stand animation, else 2.</summary>
    public const int StandState = 0;

    /// <summary>Walking forward — <c>0x455BA0</c>, animation 3.</summary>
    public const int WalkState = 1;

    /// <summary>Running forward — <c>0x455D10</c>, animation 4.</summary>
    public const int RunState = 2;

    /// <summary>Firing from the hip — <c>0x455EB0</c>, animation 6.</summary>
    public const int HipFiringState = 3;

    /// <summary>The snap raise before a hip shot — <c>0x455FE0</c>, animation 5.</summary>
    public const int HipRaiseState = 4;

    /// <summary>Raising the launcher to the shoulder — <c>0x4560C0</c>, animation 7.</summary>
    public const int ShoulderRaiseState = 5;

    /// <summary>Lowering the launcher — <c>0x456170</c>, animation 9. No per-tick transitions here.</summary>
    public const int ShoulderLowerState = 6;

    /// <summary>Aiming from the shoulder — <c>0x456220</c>, animation 8. The type's kneeling stance.</summary>
    public const int AimState = 7;

    /// <summary>Firing from the shoulder — <c>0x456350</c>, animation 11.</summary>
    public const int AimedFiringState = 8;

    /// <summary>Reloading — <c>0x456480</c>, animation 19.</summary>
    public const int ReloadState = 9;

    /// <summary>The lead-in from aiming to firing — <c>0x456570</c>, animation 10.</summary>
    public const int AimToFireState = 10;

    /// <summary>Off the ground — <c>0x456650</c>.</summary>
    public const int AirborneState = 11;

    /// <summary>Flinching — <c>0x456770</c>. Plays the struck zone's limb animation.</summary>
    public const int FlinchState = 12;

    /// <summary>Dying — <c>0x4567C0</c>.</summary>
    public const int DeathState = 13;

    /// <summary>Playing the animation the AI named — <c>0x456960</c>, once.</summary>
    public const int ScriptedAnimationState = 14;

    /// <summary>Gets the RPG soldier's <c>{animation, mode}</c> table, transcribed from <c>0x53E720</c>.</summary>
    public static HumanBodyStateTable Table { get; } = new(
        (2, 1), (3, 1), (4, 1), (6, 1), (5, 0), (7, 0), (9, 0), (8, 0), (11, 1), (19, 0),
        (10, 0), (2, 1), (0, 0), (0, 0), (0, 0));
}
