namespace OpenIGI.Core.Configuration;

/// <summary>
/// Where a setting is read: a game's id, <see cref="Multiplayer"/>, <see cref="Coop"/>. A setting with a scope is read only
/// where each of its scope's words holds -- I.G.I. 2's movement in I.G.I. 2, a server's time limit in a multiplayer match --
/// and one without is read everywhere. What a run is held to is the settings read where it was played (a single-player run
/// of Project I.G.I. is not held to I.G.I. 2's movement, nor to a server's time limit).
/// </summary>
public static class CVarScope
{
    /// <summary>A multiplayer match's: read on its server and by its players, never in single player.</summary>
    public const string Multiplayer = "multiplayer";

    /// <summary>A co-op session's: read while a mission is played together, never alone.</summary>
    public const string Coop = "coop";

    /// <summary>Scopes a setting: the words that must each hold where it is read.</summary>
    /// <typeparam name="TVar">The setting's type.</typeparam>
    /// <param name="variable">The setting.</param>
    /// <param name="scope">The words: a game's id, <see cref="Multiplayer"/>, <see cref="Coop"/>.</param>
    /// <returns>The setting, for its declaration to go on.</returns>
    public static TVar ReadIn<TVar>(this TVar variable, params string[] scope)
        where TVar : ICVar
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(scope);
        if (variable is IScopedCVar scoped)
        {
            scoped.Scope = [.. scope];
        }

        return variable;
    }

    /// <summary>Whether a setting is read where these words hold.</summary>
    /// <param name="variable">The setting.</param>
    /// <param name="where">The words that hold: the game played, and multiplayer or co-op where they are played.</param>
    /// <returns>Whether it is read there.</returns>
    public static bool IsReadIn(this ICVar variable, IReadOnlyCollection<string> where)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(where);
        return variable.Scope.All(where.Contains);
    }
}

/// <summary>A setting whose scope its declaration gives (<see cref="CVarScope.ReadIn"/>).</summary>
internal interface IScopedCVar
{
    /// <summary>Sets the words that must each hold where it is read.</summary>
    IReadOnlyList<string> Scope { set; }
}
