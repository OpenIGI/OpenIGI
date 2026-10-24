namespace OpenIGI.Game.Weapons;

/// <summary>The weapons that carry a telescopic sight, whose glass catches the light when one is aimed at the player.</summary>
/// <remarks>
/// <b>Not retail</b>: neither game marks a scope in its weapon data -- a scope is the weapon's own model and its zoom the
/// player's -- so the rifles are named here: the Dragunov in both games, and I.G.I. 2's PSG-1s and Barretts.
/// </remarks>
public static class ScopedWeapons
{
    private static readonly HashSet<string> Scoped = new(StringComparer.OrdinalIgnoreCase)
    {
        "WEAPON_ID_DRAGUNOV",
        "WEAPON_ID_PSG1",
        "WEAPON_ID_PSG1SD",
        "WEAPON_ID_BARRET",
        "WEAPON_ID_BARRETTHERMO",
    };

    /// <summary>Whether a weapon has a telescopic sight.</summary>
    /// <param name="weaponId">The weapon's script id, such as <c>WEAPON_ID_DRAGUNOV</c>.</param>
    /// <returns>Whether it does.</returns>
    public static bool IsScoped(string? weaponId) => weaponId is { Length: > 0 } && Scoped.Contains(weaponId);
}
