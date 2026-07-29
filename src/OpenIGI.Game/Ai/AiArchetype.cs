namespace OpenIGI.Game.Ai;

/// <summary>The nine behaviour scripts the AI system loads, and which <see cref="AiType"/> uses which.</summary>
/// <remarks>
/// <para>
/// <c>AIFunction_DefaultHandler</c> (<c>0x44E060</c>) is the whole mechanism in one line:
/// <c>Script_Run(scripts[archetypeOf[ai-&gt;aiType]])</c>, where <c>archetypeOf</c> is the twenty-nine-entry
/// map at <c>0x53C94C</c>.
/// </para>
/// <para>
/// <b>The script order is the reverse of the string-address order</b>, which is the trap here. The nine
/// paths sit in <c>.rdata</c> as guard, patrol, civilian, priboi, ekk, anya, sniper, gunner, rpg — so index
/// 0 looks like <c>guard</c>. It is not. The map's first seven entries are the identity (<c>AITYPE_RPG</c> is
/// 0 and maps to script 0, <c>AITYPE_GUNNER</c> is 1 and maps to 1, and so on), which can only hold if
/// script 0 is <c>rpg</c>. The remaining entries then agree perfectly: every <c>PATROL</c> type maps to 7 and
/// every <c>GUARD</c> type to 8.
/// </para>
/// <para>
/// <b>Two archetype scripts ship and are never loaded.</b> <c>common/ai/observer.qvm</c> and
/// <c>common/ai/radioguard.qvm</c> are in every installation, the engine names neither, and <b>no retail
/// <c>.qvm</c> anywhere mentions either name</b>. They are dead content, like M6's <c>SVOL</c> model path and
/// M9's .357 ammunition.
/// </para>
/// </remarks>
public enum AiArchetype
{
    /// <summary>An RPG soldier — <c>rpg.qsc</c>.</summary>
    Rpg = 0,

    /// <summary>An emplacement gunner — <c>gunner.qsc</c>.</summary>
    Gunner = 1,

    /// <summary>A sniper — <c>sniper.qsc</c>.</summary>
    Sniper = 2,

    /// <summary>Anya — <c>anya.qsc</c>.</summary>
    Anya = 3,

    /// <summary>Ekk — <c>ekk.qsc</c>.</summary>
    Ekk = 4,

    /// <summary>Priboi — <c>priboi.qsc</c>.</summary>
    Priboi = 5,

    /// <summary>A civilian — <c>civilian.qsc</c>.</summary>
    Civilian = 6,

    /// <summary>A patrolling soldier — <c>patrol.qsc</c>. Every patrol type of every faction resolves here.</summary>
    Patrol = 7,

    /// <summary>A stationary guard — <c>guard.qsc</c>. Every guard type of every faction resolves here.</summary>
    Guard = 8,
}
