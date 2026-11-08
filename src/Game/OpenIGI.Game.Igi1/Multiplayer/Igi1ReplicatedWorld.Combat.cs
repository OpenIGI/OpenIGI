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
            // The soldiers alone: the players' hits on one another are told of where they land, and a listen host's own tally
            // counts those too.
            (int hits, int kills) = _tallied.GetValueOrDefault(tally, (tally.SoldierHits, tally.SoldierKills));
            if (tally.SoldierHits > hits || tally.SoldierKills > kills)
            {
                bool killed = tally.SoldierKills > kills;
                _combat.Add(new CombatNote(entity, -1, 0f, killed, killed ? tally.LastKillHead : tally.LastHitHead, rig.Body.Position));
            }

            _tallied[tally] = (tally.SoldierHits, tally.SoldierKills);
        }

        if (_combat.Count == 0)
        {
            return [];
        }

        CombatNote[] notes = [.. _combat];
        _combat.Clear();
        return notes;
    }

    // A round on a player of the session: who fired it, from where, how much it took, and whether in the head.
    private void NoteCombat(IHarmedPlayer hurt, int victim, int? attacker, bool head = false)
    {
        float now = hurt.Health.RemainingFraction;
        float before = _healthSeen.TryGetValue(victim, out float seen) && seen >= now ? seen : 1f;
        _healthSeen[victim] = now;
        Vector3d from = attacker is { } shooter && RigOf(shooter) is { } rig ? rig.Body.Position : hurt.Rig?.Body.Position ?? default;
        _combat.Add(new CombatNote(attacker ?? -1, victim, Math.Clamp(before - now, 0f, 1f), hurt.Health.IsDead, head, from));
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
