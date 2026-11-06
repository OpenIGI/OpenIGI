using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <content>
/// The session's hits as its players learn of them (<see cref="CombatNote"/>): a round on a player, from whoever fired it,
/// with how much of its health it took; and a player's round on one of the level's soldiers, read off the shooter's tally.
/// </content>
public sealed partial class Igi1ReplicatedWorld
{
    private readonly List<CombatNote> _combat = [];

    // Each player's health as last seen, to tell how much a hit took; each shooter's soldier hits and kills as last counted.
    private readonly Dictionary<int, float> _healthSeen = [];
    private readonly Dictionary<ShotTally, (int Hits, int Kills)> _tallied = new(ReferenceEqualityComparer.Instance);

    /// <inheritdoc/>
    public IReadOnlyList<CombatNote> TakeCombat()
    {
        foreach ((int entity, PlayerCamera rig) in Rigs())
        {
            ShotTally tally = rig.Shots;
            (int hits, int kills) = _tallied.GetValueOrDefault(tally, (tally.Hits, tally.Kills));
            if (tally.Hits > hits || tally.Kills > kills)
            {
                bool killed = tally.Kills > kills;
                _combat.Add(new CombatNote(entity, -1, 0f, killed, killed ? tally.LastKillHead : tally.LastHitHead, rig.Body.Position));
            }

            _tallied[tally] = (tally.Hits, tally.Kills);
        }

        if (_combat.Count == 0)
        {
            return [];
        }

        CombatNote[] notes = [.. _combat];
        _combat.Clear();
        return notes;
    }

    // A round on a player of the session: who fired it, from where, and how much it took.
    private void NoteCombat(IHarmedPlayer hurt, int victim, int? attacker)
    {
        float now = hurt.Health.RemainingFraction;
        float before = _healthSeen.TryGetValue(victim, out float seen) && seen >= now ? seen : 1f;
        _healthSeen[victim] = now;
        Vector3d from = attacker is { } shooter && RigOf(shooter) is { } rig ? rig.Body.Position : hurt.Rig?.Body.Position ?? default;
        _combat.Add(new CombatNote(attacker ?? -1, victim, Math.Clamp(before - now, 0f, 1f), hurt.Health.IsDead, Head: false, from));
    }

    // Every player of the session by entity, with its rig.
    private IEnumerable<(int Entity, PlayerCamera Rig)> Rigs()
    {
        if (IncludesHost && _game.World.Player is { } own)
        {
            yield return (HostEntity, own);
        }

        foreach (Igi1NetworkPlayer other in _bySlot.Values)
        {
            yield return (other.Entity, other.Player);
        }
    }

    private PlayerCamera? RigOf(int entity) => Rigs().FirstOrDefault(r => r.Entity == entity).Rig;
}
