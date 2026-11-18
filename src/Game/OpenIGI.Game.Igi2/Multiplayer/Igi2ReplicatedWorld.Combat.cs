using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <content>
/// The session's hits as its players learn of them (<see cref="CombatNote"/>), as Project I.G.I.'s world tells them: a round or
/// blast on a player, from whoever it came from, with how much of its health it took; and a player's round on one of the level's
/// soldiers, read off the shooter's tally. I.G.I. 2 told none, so its players had no hit marks, no mark of where a hit came from,
/// and no guard in the kill feed, which named no weapon for a player's death either.
/// </content>
public sealed partial class Igi2ReplicatedWorld
{
    private readonly List<CombatNote> _combat = [];

    // Each player's health as last seen, to tell how much a hit took; each shooter's soldier hits and kills as last counted; and
    // the weapon and the head of each player's killing blow, for its death's line in the feed.
    private readonly Dictionary<int, float> _healthSeen = [];
    private readonly Dictionary<ShotTally, (int Hits, int Kills)> _tallied = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, (string Weapon, bool Head)> _killingBlow = [];

    /// <inheritdoc/>
    public IReadOnlyList<CombatNote> TakeCombat()
    {
        foreach ((int entity, Igi2HumanPlayer human) in Players())
        {
            ShotTally tally = human.Shots;
            (int hits, int kills) = _tallied.GetValueOrDefault(tally, (tally.SoldierHits, tally.SoldierKills));
            if (tally.SoldierHits > hits || tally.SoldierKills > kills)
            {
                bool killed = tally.SoldierKills > kills;
                _combat.Add(new CombatNote(
                    entity, -1, 0f, killed, killed ? tally.LastKillHead : tally.LastHitHead, human.Position, WeaponOf(human)));
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

    // A player's harm heard, under its entity.
    private void HearHarm(int entity, Igi2HumanPlayer human) => human.Struck = (by, part) => NoteHarm(entity, human, by, part);

    // A round or blast on a player of the session: who it came from, from where, how much it took, and whether to the head.
    private void NoteHarm(int victim, Igi2HumanPlayer hurt, object? by, Igi2DamagePart part)
    {
        if (victim == HostEntity && !IncludesHost)
        {
            return;
        }

        float max = hurt.Damage.MaxDamage;
        float now = max > 0f ? Math.Clamp(1f - (hurt.Damage.Damage / max), 0f, 1f) : 1f;
        float before = _healthSeen.TryGetValue(victim, out float seen) && seen >= now ? seen : 1f;
        _healthSeen[victim] = now;
        string weapon = WeaponOf(by);
        bool head = part == Igi2DamagePart.Head;
        Vector3d from = (by as Igi2Human)?.Position ?? hurt.Position;
        _combat.Add(new CombatNote(EntityOf(by), victim, Math.Clamp(before - now, 0f, 1f), hurt.Damage.IsFatal, head, from, weapon));
        if (hurt.Damage.IsFatal)
        {
            _killingBlow[victim] = (weapon, head);
        }
    }

    // The weapon in a body's hands, by the key the kill feed draws it by.
    private static string WeaponOf(object? body) => body switch
    {
        Igi2HumanPlayer player => player.Weapon.Inventory?.Current?.WeaponType.WeaponId ?? string.Empty,
        Soldiers.Igi2HumanSoldier soldier => soldier.Weapon.Inventory?.Current?.WeaponType.WeaponId ?? string.Empty,
        _ => string.Empty,
    };

    // Every player of the session by entity.
    private IEnumerable<(int Entity, Igi2HumanPlayer Human)> Players()
    {
        if (IncludesHost)
        {
            yield return (HostEntity, _level.Human);
        }

        foreach ((int slot, Igi2NetworkHuman other) in _bySlot)
        {
            yield return (slot + 1, other.Human);
        }
    }
}
