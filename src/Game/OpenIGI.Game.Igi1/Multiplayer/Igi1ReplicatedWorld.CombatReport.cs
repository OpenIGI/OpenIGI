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

        lines.AddRange(_shotLines);
        if (_shotLinesDropped > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"shot: {_shotLinesDropped} more passes not logged"));
        }

        _shotLines.Clear();
        _shotLinesDropped = 0;
        if (_unknownShooterHits > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"combat: {_unknownShooterHits} round(s) met a player from no known shooter"));
        }

        _shooterTallies.Clear();
        _unknownShooterHits = 0;
        return lines;
    }

    // The shot log's lines waiting for the next report, and how many more were dropped past its cap.
    private readonly List<string> _shotLines = [];
    private int _shotLinesDropped;
    private const int MaxShotLines = 400;

    // One pass of a round, as the bodies saw it: who fired it, what it struck, and each other player's body.
    internal void NoteShotPass(Core.Numerics.Vector3d origin, Core.Numerics.Vector3d target, Engine.Tasks.GameTask? struck, IReadOnlyList<(IHarmedPlayer Player, string Outcome)> casts, string? noBodies)
    {
        if (_shotLines.Count >= MaxShotLines)
        {
            _shotLinesDropped++;
            return;
        }

        List<(string Name, Player.PlayerCamera Rig, IHarmedPlayer Harmed)> everyone = [.. _bySlot.Select(p => (p.Value.Name, p.Value.Player, (IHarmedPlayer)p.Value))];
        if (IncludesHost && _game.World.Player is { } own && _game.Harm.Own is { } ownHarm)
        {
            everyone.Add(("host", own, ownHarm));
        }

        // The shooter: whoever's eye the round left from; a pass that starts elsewhere carries on past a surface or a body.
        (string Name, Player.PlayerCamera Rig, IHarmedPlayer Harmed)? shooter = everyone
            .Where(e => Core.Numerics.Vector3d.Distance(e.Rig.Body.Eye, origin) < 1.5 * Core.Numerics.WorldUnits.PerMetre)
            .OrderBy(e => Core.Numerics.Vector3d.Distance(e.Rig.Body.Eye, origin))
            .Cast<(string, Player.PlayerCamera, IHarmedPlayer)?>()
            .FirstOrDefault();
        if (shooter is null && casts.Count == 0)
        {
            return;
        }

        Core.Numerics.Vector3d along = target - origin;
        double length = along.Length;
        System.Text.StringBuilder line = new();
        line.Append(CultureInfo.InvariantCulture, $"shot: {(shooter is { } who ? who.Name : "(pass)")} tick {CurrentTick} len {length / Core.Numerics.WorldUnits.PerMetre:F1} m struck {StruckName(struck, casts)}");
        if (noBodies is not null)
        {
            line.Append(CultureInfo.InvariantCulture, $" | no bodies: {noBodies}");
        }

        foreach ((string name, Player.PlayerCamera rig, IHarmedPlayer harmed) in everyone)
        {
            if (shooter is { } s && ReferenceEquals(s.Rig, rig))
            {
                continue;
            }

            // How near the round's line passes the chest, and how far along it.
            Core.Numerics.Vector3d chest = rig.Body.Position + new Core.Numerics.Vector3d(0, 0, 1.2 * Core.Numerics.WorldUnits.PerMetre);
            double t = length > 0 ? Math.Clamp(Core.Numerics.Vector3d.Dot(chest - origin, along) / (length * length), 0, 1) : 0;
            double miss = Core.Numerics.Vector3d.Distance(origin + (along * t), chest) / Core.Numerics.WorldUnits.PerMetre;
            string outcome = casts.Where(c => ReferenceEquals(c.Player, harmed)).Select(c => c.Outcome).FirstOrDefault()
                ?? (harmed.IsDead ? "not cast: counted dead" : "not cast");
            line.Append(CultureInfo.InvariantCulture, $" | {name}: {outcome}, passes {miss:F2} m from chest at {t * length / Core.Numerics.WorldUnits.PerMetre:F1} m, hp {harmed.Health.RemainingFraction:P0}, team {TeamOf(SlotOfEntity(EntityOf(rig) ?? -1))}, anim {rig.LocomotionAnimation}");
        }

        _shotLines.Add(line.ToString());
    }

    // A player's rig and harm by its slot: the listen host's own, or a joined player's.
    private (Player.PlayerCamera Rig, IHarmedPlayer Harmed)? HarmedRigOf(int slot) =>
        slot == ServerSession.HostSlot
            ? IncludesHost && _game.World.Player is { } own ? (own, _game.Harm.Own) : null
            : _bySlot.TryGetValue(slot, out Igi1NetworkPlayer? player) ? (player.Player, player) : null;

    /// <inheritdoc/>
    public string? Describe(int slot)
    {
        if (HarmedRigOf(slot) is not { } found)
        {
            return null;
        }

        Core.Numerics.Vector3d at = found.Rig.Body.Position / Core.Numerics.WorldUnits.PerMetre;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"at ({at.X:F1}, {at.Y:F1}, {at.Z:F1}) m, hp {found.Harmed.Health.RemainingFraction:P0}{(found.Harmed.IsDead ? ", down" : string.Empty)}, " +
            $"world team {TeamOf(slot)}, holds {found.Rig.Weapons?.InHand?.Weapon.ScriptId ?? "nothing"}, anim {found.Rig.LocomotionAnimation}");
    }

    /// <inheritdoc/>
    public string? Teleport(int slot, int toSlot, double metres)
    {
        if (HarmedRigOf(slot) is not { } moved || HarmedRigOf(toSlot) is not { } before)
        {
            return "that player is not in the level";
        }

        // In front of the other, along the way it faces, on the ground there; turned to face it.
        float yaw = before.Rig.Body.Yaw;
        Core.Numerics.Vector3d facing = new(Math.Sin(yaw), Math.Cos(yaw), 0);
        Core.Numerics.Vector3d spot = before.Rig.Body.Position + (facing * metres * Core.Numerics.WorldUnits.PerMetre);
        if (_game.World.Ground is { } field && field.TryGetGround(spot.X, spot.Y, spot.Z, out Game.World.TerrainGround ground))
        {
            spot = new Core.Numerics.Vector3d(spot.X, spot.Y, ground.Height);
        }

        moved.Rig.Body.Position = spot;
        moved.Rig.Body.Yaw = yaw + MathF.PI;
        return null;
    }

    private static string StruckName(Engine.Tasks.GameTask? struck, IReadOnlyList<(IHarmedPlayer Player, string Outcome)> casts) =>
        struck is null ? "nothing on the bodies' side (world or open air)" : casts.Any(c => c.Outcome.StartsWith("met", StringComparison.Ordinal)) ? "a player" : struck.Type.Name;

    private sealed class ShooterTally
    {
        public int Met { get; set; }

        public int Hurt { get; set; }

        public float Damage { get; set; }
    }
}
