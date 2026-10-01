namespace OpenIGI.Game.Ai;

/// <summary>
/// The twenty-nine soldier archetypes a level can spawn. Each names a behaviour script and a row of
/// <c>common/ai/settings.qsc</c>, which is keyed by <c>(AIType, Difficulty)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Registered by a <b>table walk</b> — <c>0x44EEB0</c> iterates the string-pointer array at
/// <c>0x53C9C0</c>..<c>0x53CA34</c> handing out 0, 1, 2, … in order. So the numbering is the table's order,
/// and the table's order is not the string-address order, not alphabetical, and not what you would guess:
/// <b><see cref="Rpg"/> is 0</b> and the named characters come before the generic guards.
/// </para>
/// <para>
/// <b>The faction prefix has no behavioural effect.</b> All twenty-nine collapse onto nine scripts through
/// the map at <c>0x53C94C</c> (see <see cref="AiArchetype"/>), and every <c>PATROL</c> variant — plain,
/// <c>SECURITY</c>, <c>MAFIA</c> and <c>SPETNAZ</c> alike — resolves to <c>patrol</c>, every <c>GUARD</c>
/// variant to <c>guard</c>. <c>AITYPE_MAFIA_GUARD_AK</c> and <c>AITYPE_GUARD_AK</c> run byte-identical
/// behaviour; what differs between them is the settings row and the model, not the script.
/// </para>
/// <para>
/// The weapon in the name is likewise not read by the script — it is part of the settings key.
/// </para>
/// </remarks>
public enum AiType
{
    /// <summary>An RPG soldier.</summary>
    Rpg = 0,

    /// <summary>An emplacement gunner.</summary>
    Gunner = 1,

    /// <summary>A sniper.</summary>
    Sniper = 2,

    /// <summary>Anya, a named character.</summary>
    Anya = 3,

    /// <summary>Ekk, a named character.</summary>
    Ekk = 4,

    /// <summary>Priboi, a named character.</summary>
    Priboi = 5,

    /// <summary>An unarmed civilian.</summary>
    Civilian = 6,

    /// <summary>A patrolling soldier with an Uzi.</summary>
    PatrolUzi = 7,

    /// <summary>A patrolling soldier with an AK.</summary>
    PatrolAk = 8,

    /// <summary>A patrolling soldier with a SPAS.</summary>
    PatrolSpas = 9,

    /// <summary>A patrolling soldier with a pistol.</summary>
    PatrolPistol = 10,

    /// <summary>A stationary guard with an Uzi.</summary>
    GuardUzi = 11,

    /// <summary>A stationary guard with an AK.</summary>
    GuardAk = 12,

    /// <summary>A stationary guard with a SPAS.</summary>
    GuardSpas = 13,

    /// <summary>A stationary guard with a pistol.</summary>
    GuardPistol = 14,

    /// <summary>Security, patrolling, with an Uzi.</summary>
    SecurityPatrolUzi = 15,

    /// <summary>Security, patrolling, with a SPAS.</summary>
    SecurityPatrolSpas = 16,

    /// <summary>Mafia, patrolling, with an Uzi.</summary>
    MafiaPatrolUzi = 17,

    /// <summary>Mafia, patrolling, with an AK.</summary>
    MafiaPatrolAk = 18,

    /// <summary>Mafia, patrolling, with a SPAS.</summary>
    MafiaPatrolSpas = 19,

    /// <summary>Mafia, guarding, with an Uzi.</summary>
    MafiaGuardUzi = 20,

    /// <summary>Mafia, guarding, with an AK.</summary>
    MafiaGuardAk = 21,

    /// <summary>Mafia, guarding, with a SPAS.</summary>
    MafiaGuardSpas = 22,

    /// <summary>Spetsnaz, patrolling, with an Uzi.</summary>
    SpetnazPatrolUzi = 23,

    /// <summary>Spetsnaz, patrolling, with an AK.</summary>
    SpetnazPatrolAk = 24,

    /// <summary>Spetsnaz, patrolling, with a SPAS.</summary>
    SpetnazPatrolSpas = 25,

    /// <summary>Spetsnaz, guarding, with an Uzi.</summary>
    SpetnazGuardUzi = 26,

    /// <summary>Spetsnaz, guarding, with an AK.</summary>
    SpetnazGuardAk = 27,

    /// <summary>Spetsnaz, guarding, with a SPAS.</summary>
    SpetnazGuardSpas = 28,
}
