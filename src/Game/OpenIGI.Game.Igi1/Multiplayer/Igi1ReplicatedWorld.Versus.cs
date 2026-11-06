using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>The world played player against player: teams, whose rounds hurt whom, and where the fallen are put back.</summary>
public sealed partial class Igi1ReplicatedWorld
{
    /// <inheritdoc/>
    public void SetTeam(int slot, int team) => _teams[slot] = team;

    /// <inheritdoc/>
    public void UseLayout(Game.Multiplayer.Modes.MpLayout layout) => _layout = layout ?? throw new ArgumentNullException(nameof(layout));

    private Game.Multiplayer.Modes.MpLayout? _layout;

    // A versus player put back is handed what it starts with again; a co-op partner revived keeps what it had.
    private void Restock(PlayerCamera rig)
    {
        if (IsVersus && _game.PlayerSetup is { } setup)
        {
            setup.Restock(rig, [.. (_layout?.Loadout ?? []).Select(l => (l.Weapon, l.Rounds))]);
        }
    }

    // The mode the players' sides follow: co-op until a versus game is played.
    private string VersusMode => _versus switch
    {
        Versus.Teams => Game.Multiplayer.Modes.GameModes.TeamDeathmatch,
        Versus.Everyone => Game.Multiplayer.Modes.GameModes.FreeForAll,
        _ => Game.Multiplayer.Modes.GameModes.Coop,
    };

    /// <inheritdoc/>
    public Vector3d? PositionOf(int slot) =>
        slot == ServerSession.HostSlot
            ? IncludesHost && _game.World.Player is { } own ? own.Body.Position : null
            : _bySlot.TryGetValue(slot, out Igi1NetworkPlayer? player) ? player.Player.Body.Position : null;

    /// <inheritdoc/>
    public void KillPlayer(int slot)
    {
        PlayerCamera? rig = slot == ServerSession.HostSlot ? _game.World.Player : _bySlot.GetValueOrDefault(slot)?.Player;
        if (rig is null || _game.Harm.Players.FirstOrDefault(p => ReferenceEquals(p.Rig, rig)) is not { IsDead: false } target)
        {
            return;
        }

        _game.Harm.BeginPlayerDeath(target, rig.Body.Position, zone: 0, bone: -1, Game.Weapons.WeaponCategory.Gun);
    }

    // Where a player of a versus match is put: one of its team's spawns (anyone's in a free-for-all), the one farthest from
    // every other player still standing, as Source's deathmatch spawn picks; the level's own start without a layout.
    private IEnumerable<PlayerSpawnPoint> VersusSpawns(int slot, PlayerSpawnPoint start)
    {
        if (_layout is not { Spawns.Length: > 0 } layout)
        {
            return [];
        }

        List<Vector3d> others = [];
        if (slot != ServerSession.HostSlot && IncludesHost && _game.World.Player is { } host && !_game.World.IsPlayerDead)
        {
            others.Add(host.Body.Position);
        }

        others.AddRange(_bySlot.Where(p => p.Key != slot && !p.Value.IsDead).Select(p => p.Value.Player.Body.Position));
        int team = _versus == Versus.Teams ? TeamOf(slot) : -1;

        // Farthest from the nearest other player first.
        return layout.SpawnsFor(team)
            .Select(spawn => start with
            {
                Position = new Vector3d(spawn.Position[0], spawn.Position[1], spawn.Position[2]),
                Gamma = spawn.Yaw * MathF.PI / 180f,
            })
            .OrderByDescending(at => others.Count == 0 ? 0 : others.Min(o => Vector3d.Distance(o, at.Position)));
    }

    // Puts a versus player at the farthest of the map's spawns that has ground under it -- one with none (a soldier's place
    // up a tower, or over a drop) would leave it falling -- or, failing all, at the level's own start.
    private void PlaceVersus(PlayerCamera rig, int slot, PlayerSpawnPoint start)
    {
        foreach (PlayerSpawnPoint spawn in VersusSpawns(slot, start))
        {
            bool grounded = rig.PlaceAt(_game.World.Ground, spawn);
            _game.PlayerSetup?.NoteSession(
                $"versus spawn for slot {slot}: {spawn.Position} -> {rig.Body.Position} ({(spawn.Position.Z - rig.Body.Position.Z) / 4096:0.0} m down), ground {grounded}");
            if (grounded)
            {
                return;
            }
        }

        bool atStart = rig.PlaceAt(_game.World.Ground, start);
        _game.PlayerSetup?.NoteSession($"versus spawn for slot {slot}: level start {start.Position} -> {rig.Body.Position}, ground {atStart}");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The players' rounds then meet each other always, an enemy's hurting and a team mate's not; nobody is held down for a
    /// revive, and a player put back is put at the level's start.
    /// </remarks>
    public bool TryPlayVersus(bool teams)
    {
        _versus = teams ? Versus.Teams : Versus.Everyone;
        return true;
    }

    /// <summary>Gets a value indicating whether the players fight each other rather than the level together.</summary>
    public bool IsVersus => _versus != Versus.None;

    // Whether a round from a player's gun hurts the player it met: always for a co-op room with friendly fire, and in a
    // versus match always but from a team mate.
    internal bool Hurts(ShotTally? by, IHarmedPlayer target)
    {
        if (_versus == Versus.None)
        {
            return CoopCVars.FriendlyFire.Value;
        }

        if (_versus == Versus.Everyone || by is null || EntityOf(by) is not { } shooter || EntityOf(target.Rig) is not { } struck)
        {
            return true;
        }

        return TeamOf(SlotOfEntity(shooter)) != TeamOf(SlotOfEntity(struck));
    }

    // Whether the players' rounds meet each other at all.
    internal bool PlayersMeetRounds => _versus != Versus.None || CoopCVars.FriendlyFire.Value;

    private static int SlotOfEntity(int entity) => entity == HostEntity ? ServerSession.HostSlot : entity - 1;

    private enum Versus
    {
        None,
        Teams,
        Everyone,
    }

    private Versus _versus;
    private readonly Dictionary<int, int> _teams = [];
}
