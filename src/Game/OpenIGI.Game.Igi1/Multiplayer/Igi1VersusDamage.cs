namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>
/// What a gun's round does to a player of a versus match: I.G.I. 2's damage for the gun, whose multiplayer was balanced for
/// players shooting players, rather than I.G.I.'s, tuned against the campaign's guards.
/// </summary>
/// <remarks>
/// <para>
/// Each figure is the gun's <c>Damage Factor</c> in I.G.I. 2's <c>weapons/weapon.qvm</c>, met with that game's multiplayer
/// body (<see cref="Game.Player.HumanHitZoneTable.Versus"/>: 1.0 kills; chest and back ×0.8, the trunk ×0.7, upper limbs ×0.6,
/// lower limbs ×0.55; the head lethal here). So an AK-47 takes three rounds to the chest and four or five to the limbs, an M16
/// four, a sub-machine gun five; a pistol or a sniper rifle kills with one to the head; a shotgun's pellets are the I.G.I. 2
/// SPAS's, two of them to the chest a kill, so a blast up close is one.
/// </para>
/// <para>A gun I.G.I. 2 has no figure for keeps I.G.I.'s own; the knife, which I.G.I. 2 swings by other rules, gets two stabs.</para>
/// </remarks>
public static class Igi1VersusDamage
{
    private static readonly Dictionary<string, float> ByWeapon = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WEAPON_ID_AK47"] = 0.43f,
        ["WEAPON_ID_M16A2"] = 0.38f,
        ["WEAPON_ID_MINIMI"] = 0.4f,
        ["WEAPON_ID_MP5SD"] = 0.29f,
        ["WEAPON_ID_UZI"] = 0.25f,
        ["WEAPON_ID_UZIX2"] = 0.25f,
        ["WEAPON_ID_GLOCK"] = 0.29f,
        ["WEAPON_ID_COLT"] = 0.75f,
        ["WEAPON_ID_DESERTEAGLE"] = 0.7f,
        ["WEAPON_ID_DRAGUNOV"] = 0.8f,
        ["WEAPON_ID_SPAS12"] = 0.7f,
        ["WEAPON_ID_JACKHAMMER"] = 0.4f,
        ["WEAPON_ID_M2HB"] = 1.5f,
        ["WEAPON_ID_KNIFE"] = 0.65f,
    };

    /// <summary>Gets a round's damage to a versus player.</summary>
    /// <param name="weapon">The script id of the gun that fired it, if known.</param>
    /// <param name="own">I.G.I.'s own damage for it, kept for a gun without a figure.</param>
    /// <returns>The damage factor.</returns>
    public static float Of(string? weapon, float own) =>
        weapon is not null && ByWeapon.TryGetValue(weapon, out float factor) ? factor : own;
}
