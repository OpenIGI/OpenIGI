namespace OpenIGI.Game.World;

/// <summary>The bits of Project I.G.I.'s area criteria, each a script constant a level's <c>Criteria</c> slot names.</summary>
/// <remarks>
/// The humans register <c>CRITERIA_HUMAN</c> and one <c>CRITERIA_HUMAN</c><em>n</em> for each of eight teams (<c>Human_Open</c>),
/// the vehicles <c>CRITERIA_VEHICLE</c> (<c>Vehicle_Open</c>). The original hands each the next free bit; only the names
/// reach level data, so the order here is this port's.
/// </remarks>
public static class Igi1AreaCriteria
{
    /// <summary>Every human, <c>CRITERIA_HUMAN</c>.</summary>
    public const int Human = 1 << 0;

    /// <summary>Every vehicle, <c>CRITERIA_VEHICLE</c>.</summary>
    public const int Vehicle = 1 << 9;

    /// <summary>The number of teams with a criterion of their own.</summary>
    public const int Teams = 8;

    /// <summary>Gets a team's criterion, <c>CRITERIA_HUMAN</c><em>n</em>.</summary>
    /// <param name="team">The team.</param>
    /// <returns>Its bit, or nought for a team that has none.</returns>
    public static int Team(int team) => (uint)team < Teams ? 2 << team : 0;

    /// <summary>Gets the value of a criterion's script constant.</summary>
    /// <param name="name">The name as written.</param>
    /// <returns>Its bit, or <see langword="null"/> for a name that is not a criterion.</returns>
    public static int? Constant(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        const string prefix = "CRITERIA_HUMAN";
        if (name == "CRITERIA_VEHICLE")
        {
            return Vehicle;
        }

        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (name.Length == prefix.Length)
        {
            return Human;
        }

        return name.Length == prefix.Length + 1 && name[^1] is >= '0' and < (char)('0' + Teams) ? Team(name[^1] - '0') : null;
    }

    /// <summary>The name of the constant family the criteria are listed in.</summary>
    public const string FamilyName = "CRITERIA";

    /// <summary>Gets the criteria as a constant family, each with what it counts, for a title's script vocabulary.</summary>
    /// <remarks>
    /// The levels put the player and those on his side in team nought and the enemy in team one; no criterion counts the
    /// player alone.
    /// </remarks>
    public static Scripting.Vocabulary.ConstantFamily Family { get; } = AreaCriteriaFamily.Build(
        FamilyName,
        [
            new("CRITERIA_HUMAN", Human, "every human"),
            .. Enumerable.Range(0, Teams).Select(static team => new AreaCriterion("CRITERIA_HUMAN" + team, Team(team), AreaCriteriaFamily.TeamNote(team))),
            new("CRITERIA_VEHICLE", Vehicle, "every vehicle"),
        ]);
}

/// <summary>One area criterion: the constant a level names it by, its bit, and what it counts.</summary>
/// <param name="Name">The constant's name.</param>
/// <param name="Bit">Its bit in an area's mask.</param>
/// <param name="Note">What it counts, in a few words.</param>
public readonly record struct AreaCriterion(string Name, int Bit, string Note);

/// <summary>Builds the constant family a title lists its area criteria in, the same way for both games.</summary>
public static class AreaCriteriaFamily
{
    /// <summary>Builds the family.</summary>
    /// <param name="name">The family's name.</param>
    /// <param name="criteria">The criteria, in the order they are offered.</param>
    /// <returns>The family, each constant with its note.</returns>
    public static Scripting.Vocabulary.ConstantFamily Build(string name, IReadOnlyList<AreaCriterion> criteria)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(criteria);
        return new Scripting.Vocabulary.ConstantFamily(name, [.. criteria.Select(static c => new KeyValuePair<string, int>(c.Name, c.Bit))])
        {
            Notes = criteria.ToDictionary(static c => c.Name, static c => c.Note, StringComparer.Ordinal),
        };
    }

    /// <summary>What a team's criterion counts, as the levels of both games use the teams.</summary>
    /// <param name="team">The team.</param>
    /// <returns>The note.</returns>
    public static string TeamNote(int team) => team switch
    {
        0 => "team 0: the player and his side",
        1 => "team 1: the enemy",
        _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"team {team}"),
    };
}
