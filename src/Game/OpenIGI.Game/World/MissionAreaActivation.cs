using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Flow;
using OpenIGI.Game.Player;

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
}
