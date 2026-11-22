namespace OpenIGI.Game.Multiplayer;

/// <summary>What a co-op client foresees its player's round doing to one of the level's soldiers.</summary>
public enum ForeseenHarm
{
    /// <summary>It is struck and lives.</summary>
    Hit,

    /// <summary>It is struck dead.</summary>
    Kill,
}

/// <summary>What a client's copy of a soldier does with the server's word on it, once a hit has been foreseen.</summary>
public enum ForeseenHarmVerdict
{
    /// <summary>It goes on as the server has it.</summary>
    Follow,

    /// <summary>It stays down: it was foreseen dead and the server's word, which comes a round trip and more behind, has yet to say so.</summary>
    StayDown,

    /// <summary>It gets up: it was foreseen dead and the server kept it alive for longer than its word takes to come.</summary>
    GetUp,
}

/// <summary>
/// A soldier's health as a co-op server's snapshots carry it (<see cref="ReplicatedHuman.Health"/>): what is left of it, as a
/// share of the soldier's own health before a squad's toughness scaled it, in 64ths. Each game knows a soldier's own health
/// from the level's data, so a client weighs its round against the server's soldier without knowing how many players toughen
/// it. <see cref="Unforeseeable"/> marks one whose death a client is not to foresee: harm kept off it, or the head's one shot.
/// </summary>
public static class SoldierVitality
{
    /// <summary>The byte for a soldier whose death is not to be foreseen.</summary>
    public const byte Unforeseeable = byte.MaxValue;

    /// <summary>How much of a soldier's own health one step of the byte is.</summary>
    public const float Step = 1f / 64f;

    /// <summary>Writes what is left of a soldier.</summary>
    /// <param name="remaining">The health it has left, in its game's units; nought once dead.</param>
    /// <param name="ownMaximum">Its own health, unscaled, in the same units.</param>
    /// <param name="foreseeable">Whether a client may foresee its death.</param>
    /// <returns>The byte.</returns>
    public static byte Encode(float remaining, float ownMaximum, bool foreseeable)
    {
        if (!foreseeable || !(ownMaximum > 0f) || !float.IsFinite(remaining))
        {
            return Unforeseeable;
        }

        // Rounded up: a client never takes a soldier for weaker than it is, or the round that leaves it a sliver would fell it
        // on that client's screen and it would get up again when the server's word came. Taken for a step stronger, the fall
        // waits for that word, as every fall once did.
        double steps = Math.Ceiling((Math.Max(0f, remaining) / ownMaximum / Step) - 1e-3);
        return steps >= Unforeseeable ? Unforeseeable : (byte)steps;
    }

    /// <summary>Reads what is left of a soldier.</summary>
    /// <param name="value">The byte.</param>
    /// <param name="ownMaximum">Its own health, unscaled, in its game's units, as this game has it.</param>
    /// <returns>The health left, or <see langword="null"/> when its death is not to be foreseen.</returns>
    public static float? Decode(byte value, float ownMaximum) =>
        value == Unforeseeable || !(ownMaximum > 0f) ? null : value * Step * ownMaximum;
}

/// <summary>
/// A co-op client's foresight of its player's rounds on the level's soldiers: each hit marked, each kill felled, at once.
/// </summary>
/// <remarks>
/// <para>
/// The soldiers are the server's: only its copies are hurt and only its word kills one. Waiting on that word, a client saw its
/// round strike a soldier that flinched and fell a round trip and its interpolation later -- three tenths of a second at a
/// tenth's ping -- where the host saw it at once. Source waits the same way (its hit sounds ride the server's hurt events), and
/// so did this port.
/// </para>
/// <para>
/// So the client weighs its round here against the health the server last said the soldier had, less what its own rounds are
/// still waiting on the server for: short of it the soldier flinches, past it he falls, here and now. The server traces the
/// same round against the same soldier where the client drew him (its lag compensation), so they agree all but always. When
/// they don't, the server's word stands: a soldier foreseen dead whom the server keeps alive for longer than its word takes to
/// come back (<see cref="ConfirmSeconds"/>) gets up again.
/// </para>
/// <para>
/// Keyed by whatever names a soldier in its game; units are its game's health. One shared by both games.
/// </para>
/// </remarks>
public sealed class PredictedSoldierHarm
{
    /// <summary>The longest a soldier foreseen dead waits for the server's word, in seconds: a lost word should not keep it down for long.</summary>
    public const double MostConfirmSeconds = 1.0;

    /// <summary>The least it waits, in seconds.</summary>
    public const double LeastConfirmSeconds = 0.25;

    /// <summary>What the wait has on top of the round trip and the interpolation, in seconds: a snapshot's spacing and a jitter or two.</summary>
    public const double ConfirmSlackSeconds = 0.15;

    private readonly Dictionary<int, Soldier> _soldiers = [];

    /// <summary>Gets how many soldiers foreseen dead got up again, the server having kept them alive: the foresight's misses.</summary>
    public int GetUps { get; private set; }

    /// <summary>Gets how many soldiers were foreseen dead.</summary>
    public int Kills { get; private set; }

    /// <summary>Gets the clock, in seconds, as last advanced.</summary>
    public double Now { get; private set; }

    /// <summary>
    /// Gets or sets how long, in seconds, a foreseen hit waits for the server's word on it: a round trip and the interpolation the
    /// soldiers are drawn behind by, and some slack.
    /// </summary>
    public double ConfirmSeconds { get; set; } = 0.6;

    /// <summary>Moves the clock on, and the wait with the line as it now is.</summary>
    /// <param name="now">The clock, in seconds.</param>
    /// <param name="roundTrip">The line's round trip, in seconds.</param>
    /// <param name="interpolation">How far behind the server the others are drawn, in seconds.</param>
    public void Advance(double now, double roundTrip, double interpolation)
    {
        Now = now;
        ConfirmSeconds = Math.Clamp(roundTrip + interpolation + ConfirmSlackSeconds, LeastConfirmSeconds, MostConfirmSeconds);
    }

    /// <summary>Weighs the player's round on a soldier.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <param name="damage">What the round deals, in its game's units.</param>
    /// <returns>Whether it is struck dead.</returns>
    public ForeseenHarm NoteHit(int soldier, float damage)
    {
        Soldier s = Of(soldier);
        if (s.FellAt is not null || !s.Alive)
        {
            return ForeseenHarm.Hit;
        }

        // Rounds the server never counted (it met them nowhere) are let go once their word was due.
        if (Now - s.PendingAt > ConfirmSeconds)
        {
            s.Pending = 0f;
        }

        s.Pending += Math.Max(0f, damage);
        s.PendingAt = Now;
        if (s.Remaining is { } left && s.Pending >= left)
        {
            s.FellAt = Now;
            s.KilledAt = Now;
            s.KillUnclaimed = true;
            Kills++;
            return ForeseenHarm.Kill;
        }

        return ForeseenHarm.Hit;
    }

    /// <summary>Gets whether a soldier was foreseen dead and the server has yet to say so.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <returns><see langword="true"/> while it lies here on the client's word alone.</returns>
    public bool IsFallen(int soldier) => _soldiers.TryGetValue(soldier, out Soldier? s) && s.FellAt is not null;

    /// <summary>Takes the server's word on a soldier.</summary>
    /// <param name="soldier">The soldier.</param>
    /// <param name="remaining">The health the server says it has left, in its game's units, or <see langword="null"/> when its death is not to be foreseen.</param>
    /// <param name="alive">Whether the server has it alive.</param>
    /// <returns>What the client's copy does.</returns>
    public ForeseenHarmVerdict TakeServer(int soldier, float? remaining, bool alive)
    {
        Soldier s = Of(soldier);
        if (!alive)
        {
            s.Alive = false;
            s.Pending = 0f;
            s.FellAt = null;
            s.Remaining = 0f;
            return ForeseenHarmVerdict.Follow;
        }

        // Alive again after it died there: another life (a generator's), with nothing foreseen of it.
        if (!s.Alive)
        {
            s.Alive = true;
            s.Pending = 0f;
            s.KillUnclaimed = false;
            s.Remaining = remaining;
        }

        // What the server took off it is what the client's rounds were waiting on, as far as it goes.
        if (s.Remaining is { } was && remaining is { } now && now < was)
        {
            s.Pending = Math.Max(0f, s.Pending - (was - now));
        }

        s.Remaining = remaining;
        if (s.FellAt is not { } fell)
        {
            return ForeseenHarmVerdict.Follow;
        }

        if (Now - fell <= ConfirmSeconds)
        {
            return ForeseenHarmVerdict.StayDown;
        }

        s.FellAt = null;
        s.Pending = 0f;
        s.KillUnclaimed = false;
        GetUps++;
        return ForeseenHarmVerdict.GetUp;
    }

    /// <summary>
    /// Claims a kill the server told of for one the client foresaw: the server's note on a soldier's death names no soldier, so
    /// it is matched to the oldest foreseen kill it has not yet been matched to.
    /// </summary>
    /// <returns><see langword="true"/> when the client foresaw it, and has counted it already.</returns>
    public bool ClaimKill()
    {
        Soldier? oldest = null;
        foreach (Soldier s in _soldiers.Values)
        {
            if (s.KillUnclaimed && (oldest is null || s.KilledAt < oldest.KilledAt))
            {
                oldest = s;
            }
        }

        if (oldest is null)
        {
            return false;
        }

        oldest.KillUnclaimed = false;
        return true;
    }

    /// <summary>Forgets every soldier, as the level changes.</summary>
    public void Clear() => _soldiers.Clear();

    private Soldier Of(int soldier)
    {
        if (!_soldiers.TryGetValue(soldier, out Soldier? s))
        {
            s = new Soldier();
            _soldiers[soldier] = s;
        }

        return s;
    }

    private sealed class Soldier
    {
        // The server's word: its health left (null: not to be foreseen) and whether it lives.
        public float? Remaining;
        public bool Alive = true;

        // What the client's rounds dealt that the server has yet to count, and when the last was fired.
        public float Pending;
        public double PendingAt = double.MinValue;

        // When it was foreseen dead, while the server has yet to say so; and whether the server's note of it is yet to come.
        public double? FellAt;
        public double KilledAt;
        public bool KillUnclaimed;
    }
}
