using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <content>
/// Spawn protection: a player put back at a spawn takes no harm for a moment -- its health holds every hit off -- until its
/// time runs out or it fires, whichever is first.
/// </content>
public sealed partial class Igi1ReplicatedWorld
{
    // Each shielded player's slot: the tick its shield ends, and whether its health already held hits off before it (a
    // cheat's), so ending the shield leaves that alone.
    private readonly Dictionary<int, (long Until, bool WasOn)> _shields = [];

    /// <inheritdoc/>
    public void Shield(int slot, int ticks)
    {
        if (ticks <= 0 || HealthOf(slot) is not { IsDead: false } health)
        {
            return;
        }

        bool wasOn = _shields.TryGetValue(slot, out (long Until, bool WasOn) held) ? held.WasOn : health.Invulnerable;
        health.Invulnerable = true;
        _shields[slot] = (CurrentTick + ticks, wasOn);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ShieldState> Shields =>
        [.. _shields.Select(pair => new ShieldState(pair.Key == ServerSession.HostSlot ? HostEntity : pair.Key + 1, (int)Math.Max(0, pair.Value.Until - CurrentTick)))];

    // Each tick: a shield whose time is up, whose player fired, died or left, ends.
    private void EndShields()
    {
        foreach ((int slot, (long until, bool wasOn)) in _shields.ToArray())
        {
            HumanHealth? health = HealthOf(slot);
            bool fired = slot == ServerSession.HostSlot
                ? _game.World.Player?.Weapons?.FiredThisTick == true
                : _bySlot.TryGetValue(slot, out Igi1NetworkPlayer? player) && player.FiredThisTick;
            if (health is null || health.IsDead || fired || CurrentTick >= until)
            {
                if (health is not null)
                {
                    health.Invulnerable = wasOn;
                }

                _shields.Remove(slot);
            }
        }
    }

    // The health a player's harm is taken from: the level's own player's life, or a joined player's.
    private HumanHealth? HealthOf(int slot) =>
        slot == ServerSession.HostSlot
            ? IncludesHost && _game.World.Player is not null ? _game.World.PlayerLife.Health : null
            : _bySlot.TryGetValue(slot, out Igi1NetworkPlayer? player) ? player.Health : null;
}
