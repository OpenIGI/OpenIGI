using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>Which combat behaviour <c>AIAction_Combat</c> installs for an <c>AIType</c>.</summary>
/// <remarks>
/// The dispatch is a switch on the type, and its case labels are an independent confirmation of the
/// interleaved <c>AITYPE_*</c> numbering read off the registrar table.
/// </remarks>
public enum AiCombatArm
{
    /// <summary>No combat arm at all — the civilian.</summary>
    None = 0,

    /// <summary>Stand and fight — <c>0x48D000</c>. The sniper, Priboi and every <c>GUARD_*</c> type.</summary>
    Guard = 1,

    /// <summary>Move and fight — <c>0x48ABA0</c>. Every <c>PATROL_*</c> type, and most soldiers in the game.</summary>
    Patrol = 2,

    /// <summary>The RPG soldier — <c>0x48CB40</c>.</summary>
    Rpg = 3,

    /// <summary>An emplacement gunner — <c>0x48D2C0</c>.</summary>
    Gunner = 4,

    /// <summary>Anya and Ekk, the named characters — <c>0x48AC60</c>.</summary>
    NamedCharacter = 5,
}

/// <summary>Maps an <c>AIType</c> to the arm <c>AIAction_Combat</c> gives it.</summary>
public static class AiCombatArms
{
    /// <summary>Gets the arm for a type.</summary>
    /// <param name="type">The soldier's AI type.</param>
    /// <returns>Which behaviour the original installs.</returns>
    /// <remarks>
    /// <para>
    /// The faction prefix means nothing to the dispatch — <c>MAFIA_GUARD_AK</c> and <c>GUARD_AK</c> both
    /// land on the guard arm, exactly as they run byte-identical scripts. What decides is
    /// <c>GUARD</c> against <c>PATROL</c>.
    /// </para>
    /// <para>
    /// <b>This is the correction that matters most to how the game plays.</b> The port gave every
    /// soldier the guard arm, so every soldier stood its ground; in the original the twenty patrol types
    /// — the majority of the roster — advance, reposition and hunt instead.
    /// </para>
    /// </remarks>
    public static AiCombatArm For(AiType type) => type switch
    {
        AiType.Rpg => AiCombatArm.Rpg,
        AiType.Gunner => AiCombatArm.Gunner,
        AiType.Anya or AiType.Ekk => AiCombatArm.NamedCharacter,
        AiType.Civilian => AiCombatArm.None,

        AiType.Sniper
            or AiType.Priboi
            or AiType.GuardUzi or AiType.GuardAk or AiType.GuardSpas or AiType.GuardPistol
            or AiType.MafiaGuardUzi or AiType.MafiaGuardAk or AiType.MafiaGuardSpas
            or AiType.SpetnazGuardUzi or AiType.SpetnazGuardAk or AiType.SpetnazGuardSpas
            => AiCombatArm.Guard,

        AiType.PatrolUzi or AiType.PatrolAk or AiType.PatrolSpas or AiType.PatrolPistol
            or AiType.SecurityPatrolUzi or AiType.SecurityPatrolSpas
            or AiType.MafiaPatrolUzi or AiType.MafiaPatrolAk or AiType.MafiaPatrolSpas
            or AiType.SpetnazPatrolUzi or AiType.SpetnazPatrolAk or AiType.SpetnazPatrolSpas
            => AiCombatArm.Patrol,

        _ => AiCombatArm.None,
    };
}
