namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>
/// Keeps the players inside the part of the level a map plays: one that strays outside its layout's bounds has a few seconds
/// to come back, and dies where it stands if it doesn't.
/// </summary>
public sealed class OutOfBounds
{
    // Since when each player has been outside, by slot.
    private readonly Dictionary<int, long> _outSince = [];

    /// <summary>Gets the slots of the players outside now, and the tick each left.</summary>
    public IReadOnlyDictionary<int, long> Outside => _outSince;

    /// <summary>Checks every living player against the map's bounds.</summary>
    /// <param name="host">The session.</param>
    /// <param name="tick">The tick.</param>
    public void Tick(IModeHost host, long tick)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Layout is not { Bounds.Length: > 0 } layout)
        {
            _outSince.Clear();
            return;
        }

        long grace = (long)Math.Ceiling(layout.OutOfBoundsSeconds * host.TickRate);
        foreach (int slot in host.PlayerSlots)
        {
            if (!host.Match.Players.TryGetValue(slot, out MatchPlayer? player) || !player.IsAlive
                || host.World.PositionOf(slot) is not { } at || layout.InBounds(at))
            {
                _outSince.Remove(slot);
                continue;
            }

            if (!_outSince.TryGetValue(slot, out long since))
            {
                _outSince[slot] = tick;
            }
            else if (tick - since >= grace)
            {
                _outSince.Remove(slot);
                host.World.KillPlayer(slot);
            }
        }
    }
}
