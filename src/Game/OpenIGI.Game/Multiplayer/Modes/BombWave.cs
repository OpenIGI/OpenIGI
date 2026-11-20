namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>
/// The planted bomb's front as it reaches the players: when it gets to each, and what it does to the ones it leaves standing.
/// </summary>
/// <remarks>
/// <para>
/// The bomb's boom is heard and its flash seen at once; the front itself is held for a moment -- the fireball first, the
/// air thrown out after -- and then swells out from the bomb as a sphere, straight through everything, reaching a player when
/// it reaches the place it stands. How hard it hits there is another matter, the path it had to take round corners and
/// through walls (<see cref="BlastWaveField"/>, <see cref="BombDamage"/>): the sphere only decides when.
/// </para>
/// <para>
/// A beat before it arrives each player hears it coming. Those it does not kill it shakes by how much it took, throws the
/// view up and shoves away from the bomb.
/// </para>
/// </remarks>
public static class BombWave
{
    /// <summary>How long the front is held after the boom before it sets out, in seconds.</summary>
    public const double HoldSeconds = 1.0;

    /// <summary>How fast the front swells out once it has set out, in metres a second.</summary>
    public const double FrontMetresPerSecond = 76.2;

    /// <summary>How long before the front arrives a player hears it coming, in seconds.</summary>
    public const double CueLeadSeconds = 0.2;

    /// <summary>The longest the front runs once set out, in seconds, however far it has still to go.</summary>
    public const double MostSeconds = 10.0;

    /// <summary>How long a hit's shake lasts, in seconds.</summary>
    public const double ShakeSeconds = 1.5;

    // The view's throw upward at the least and at the most of a hit, in degrees.
    private const float LeastKickDegrees = 10f;
    private const float MostKickDegrees = 20f;

    // A hit's shake, against an ordinary hit's: at the least the front does and at the most.
    private const float LeastShake = 0.6f;
    private const float MostShake = 1.6f;

    /// <summary>How far the front has swelled from the bomb, in metres.</summary>
    /// <param name="secondsSinceBlast">How long since the bomb went off.</param>
    /// <returns>The front's radius; nought while it is held.</returns>
    public static double FrontMetres(double secondsSinceBlast) =>
        Math.Max(0.0, Math.Min(secondsSinceBlast - HoldSeconds, MostSeconds)) * FrontMetresPerSecond;

    /// <summary>How long after the boom the front reaches a place.</summary>
    /// <param name="straightMetres">The place's straight distance from the bomb.</param>
    /// <returns>The seconds.</returns>
    public static double ArrivalSeconds(double straightMetres) => HoldSeconds + (Math.Max(0.0, straightMetres) / FrontMetresPerSecond);

    /// <summary>Whether a place a straight distance away has heard the front coming.</summary>
    /// <param name="secondsSinceBlast">How long since the bomb went off.</param>
    /// <param name="straightMetres">The place's straight distance from the bomb.</param>
    /// <returns><see langword="true"/> from <see cref="CueLeadSeconds"/> before the front gets there.</returns>
    public static bool IsHeard(double secondsSinceBlast, double straightMetres) =>
        secondsSinceBlast + CueLeadSeconds >= ArrivalSeconds(straightMetres);

    /// <summary>Whether the front has reached a place a straight distance away.</summary>
    /// <param name="secondsSinceBlast">How long since the bomb went off.</param>
    /// <param name="straightMetres">The place's straight distance from the bomb.</param>
    /// <returns><see langword="true"/> once it has.</returns>
    public static bool HasReached(double secondsSinceBlast, double straightMetres) =>
        secondsSinceBlast >= ArrivalSeconds(straightMetres);

    /// <summary>Whether the front has nothing left to reach.</summary>
    /// <param name="secondsSinceBlast">How long since the bomb went off.</param>
    /// <param name="farthestMetres">The farthest it could still matter, straight from the bomb.</param>
    /// <returns><see langword="true"/> once it has run its longest or passed the farthest place.</returns>
    public static bool IsSpent(double secondsSinceBlast, double farthestMetres) =>
        secondsSinceBlast >= HoldSeconds + MostSeconds || FrontMetres(secondsSinceBlast) > farthestMetres;

    /// <summary>How hard a hit shakes the view, against an ordinary hit's shake.</summary>
    /// <param name="share">The share of a whole health the front took.</param>
    /// <returns>The scale.</returns>
    public static float ShakeScale(float share) => LeastShake + ((MostShake - LeastShake) * Math.Clamp(share, 0f, 1f));

    /// <summary>How far a hit throws the view up.</summary>
    /// <param name="share">The share of a whole health the front took.</param>
    /// <returns>The throw, in radians.</returns>
    public static float KickRadians(float share) =>
        float.DegreesToRadians(LeastKickDegrees + ((MostKickDegrees - LeastKickDegrees) * Math.Clamp(share, 0f, 1f)));

    /// <summary>How much of a blast's full shove a hit gives.</summary>
    /// <param name="share">The share of a whole health the front took.</param>
    /// <returns>The share of the shove, in <c>[0,1]</c>.</returns>
    public static float ShoveShare(float share) => Math.Clamp(share, 0f, 1f);
}
