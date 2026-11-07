using System.Globalization;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <content>
/// What became of the players' rounds at one another since the last report, for the server's log: each shooter's rounds fired,
/// those that met another player's body, those that were let hurt, and the damage they did. A round that never meets a body
/// is lost in the cast; one that meets and does not hurt was ruled a team mate's.
/// </content>
public sealed partial class Igi1ReplicatedWorld
{
    private readonly Dictionary<int, ShooterTally> _shooterTallies = [];
    private readonly Dictionary<int, int> _shotsSeen = [];
    private int _unknownShooterHits;

    // A round from a player's gun met another player: counted on its shooter, with whether it hurt and how much.
    internal void NoteRoundMet(ShotTally? by, bool hurt, float damage)
    {
        if (by is null || EntityOf(by) is not { } entity)
        {
            _unknownShooterHits++;
            return;
        }

        int slot = SlotOfEntity(entity);
        ShooterTally tally = _shooterTallies.GetValueOrDefault(slot) ?? new ShooterTally();
        tally.Met++;
        if (hurt)
        {
            tally.Hurt++;
            tally.Damage += damage;
        }

        _shooterTallies[slot] = tally;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> TakeCombatReport()
    {
        if (!PlayersMeetRounds)
        {
            return [];
        }

        List<string> lines = [];
        IEnumerable<(int Slot, Player.PlayerCamera Rig, Game.Player.HumanHealth Health)> players = _bySlot
            .Select(p => (p.Key, p.Value.Player, p.Value.Health));
        if (IncludesHost && _game.World.Player is { } own)
        {
            players = players.Append((ServerSession.HostSlot, own, _game.World.PlayerHealth));
        }

        foreach ((int slot, Player.PlayerCamera rig, Game.Player.HumanHealth health) in players)
        {
            int shots = rig.Weapons?.ShotSequence ?? 0;
            int fired = _shotsSeen.TryGetValue(slot, out int before) ? Math.Max(0, shots - before) : 0;
            _shotsSeen[slot] = shots;
            ShooterTally tally = _shooterTallies.GetValueOrDefault(slot) ?? new ShooterTally();
            if (fired == 0 && tally.Met == 0)
            {
                continue;
            }

            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"combat: slot {slot} team {TeamOf(slot)} {(_versus == Versus.None ? "coop" : _versus.ToString())} fired {fired} met {tally.Met} hurt {tally.Hurt} damage {tally.Damage:F2} | " +
                $"own health {health.RemainingFraction:P0} zones {(ReferenceEquals(health.Zones, Game.Player.HumanHitZoneTable.Versus) ? "versus" : "campaign")} " +
                $"invulnerable {health.Invulnerable} | {rig.Weapons?.InHand?.Weapon.ScriptId}"));
        }

        if (_unknownShooterHits > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"combat: {_unknownShooterHits} round(s) met a player from no known shooter"));
        }

        _shooterTallies.Clear();
        _unknownShooterHits = 0;
        return lines;
    }

    private sealed class ShooterTally
    {
        public int Met { get; set; }

        public int Hurt { get; set; }

        public float Damage { get; set; }
    }
}
