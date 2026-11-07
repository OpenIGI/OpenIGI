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
    public void SetTeam(int slot, int team)
    {
        bool changed = !_teams.TryGetValue(slot, out int was) || was != team;
        _teams[slot] = team;
        if (changed && slot == ServerSession.HostSlot)
        {
            PutHostInArena();
        }
    }

    /// <inheritdoc/>
    public void UseLayout(Game.Multiplayer.Modes.MpLayout layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        PutHostInArena();
    }

    // A listen server's own player starts the match in the arena, at one of its side's spawns: left where the level puts
    // it -- often a long walk from where the level is fought, so outside the arena's bounds -- it would be killed for it.
    private void PutHostInArena()
    {
        if (IsVersus && _layout is { Spawns.Length: > 0 } && IncludesHost && _game.World.Player is { } own && !_game.World.IsPlayerDead
            && _game.PlayerSetup?.LevelSpawn() is { } start)
        {
            PlaceVersus(own, ServerSession.HostSlot, start);
        }
    }

    private Game.Multiplayer.Modes.MpLayout? _layout;

    // A versus player put back is handed what it starts with again; a co-op partner revived keeps what it had.
    // A versus player standing again: the spawn's kit topped up -- after a death, all it carries -- and its own game told.
    private void Restock(PlayerCamera rig, int slot, bool fresh)
    {
        if (IsVersus && _game.PlayerSetup is { } setup)
        {
            setup.Restock(rig, [.. (_layout?.Loadout ?? []).Select(l => (l.Weapon, l.Rounds))]);
            if (slot != ServerSession.HostSlot)
            {
                _rearmed.Add(new RearmedPlayer(slot, fresh, setup.Kit(rig)));
            }
        }
    }

    private readonly List<RearmedPlayer> _rearmed = [];
    private readonly List<(PlayerCamera Rig, Vector3d Position, float Yaw)> _rewoundPlayers = [];

    // Every living player but the shooter put where the shooter's client drew it at that moment: the rounds trace against the
    // rig's body where it stands (CoopPlayerBodies), so it stands there while the shooter's command runs.
    private void RewindPlayers(LagCompensation lag, Igi1NetworkPlayer shooter, double moment)
    {
        if (IncludesHost && !_game.World.IsPlayerDead && _game.World.Player is { } own)
        {
            Rewind(own, HostEntity);
        }

        foreach (Igi1NetworkPlayer other in _bySlot.Values)
        {
            if (!ReferenceEquals(other, shooter) && !other.IsDead)
            {
                Rewind(other.Player, other.Entity);
            }
        }

        void Rewind(PlayerCamera rig, int entity)
        {
            if (lag.History.Sample(moment, entity, out ReplicatedHuman then))
            {
                _rewoundPlayers.Add((rig, rig.Body.Position, rig.Body.Yaw));
                rig.Body.Position = then.Position.ToVector();
                rig.Body.Yaw = Quantize.AngleFromBits(then.Yaw);
            }
        }
    }

    private void RestorePlayers()
    {
        foreach ((PlayerCamera rig, Vector3d position, float yaw) in _rewoundPlayers)
        {
            rig.Body.Position = position;
            rig.Body.Yaw = yaw;
        }

        _rewoundPlayers.Clear();
    }

    /// <inheritdoc/>
    public IReadOnlyList<RearmedPlayer> TakeRearmed()
    {
        if (_rearmed.Count == 0)
        {
            return [];
        }

        RearmedPlayer[] taken = [.. _rearmed];
        _rearmed.Clear();
        return taken;
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
    /// <remarks>
    /// The plant plays once over the plant's time, the kneel coming down as the hold begins and the charge set as it ends; the
    /// defuse loops at its own pace.
    /// </remarks>
    public void ShowWork(int slot, Game.Multiplayer.Modes.ModeWorkKind? work, int heldTicks, int totalTicks)
    {
        PlayerCamera? rig = slot == ServerSession.HostSlot
            ? IncludesHost ? _game.World.Player : null
            : _bySlot.TryGetValue(slot, out Igi1NetworkPlayer? player) ? player.Player : null;
        if (rig is null)
        {
            return;
        }

        switch (work)
        {
            case Game.Multiplayer.Modes.ModeWorkKind.Plant:
                rig.PlayModeClip(Igi1BombAnimations.Plant, Igi1BombAnimations.Duration(Igi1BombAnimations.Plant) * Math.Clamp(heldTicks / (float)Math.Max(1, totalTicks), 0f, 1f));
                break;
            case Game.Multiplayer.Modes.ModeWorkKind.Defuse:
                rig.PlayModeClip(Igi1BombAnimations.Defuse, heldTicks * Formats.Animation.BoneAnimation.FrameStep);
                break;
            default:
                if (rig.ModeAnimation >= 0)
                {
                    rig.PlayModeClip(-1, 0f);
                }

                break;
        }
    }

    // The slot that carries the mode's bomb, or -1.
    private int _bombCarrier = -1;

    /// <inheritdoc/>
    public void ShowCarrier(int slot)
    {
        // The carrier carries the bomb as a weapon, and nobody else does: given on the tick it is taken up, taken on the tick
        // it is planted or lost -- and given again after a respawn's fresh kit.
        _bombCarrier = slot;
        if (IncludesHost && _game.World.Player is { } own)
        {
            Igi1BombWeapon.Keep(own, slot == ServerSession.HostSlot);
        }

        foreach ((int joined, Igi1NetworkPlayer player) in _bySlot)
        {
            Igi1BombWeapon.Keep(player.Player, joined == slot);
        }
    }

    /// <inheritdoc/>
    public bool HoldsBomb(int slot) => LivingRig(slot) is { } rig && Igi1BombWeapon.Holds(rig);

    /// <inheritdoc/>
    public bool TakeBombDrop(int slot) => LivingRig(slot)?.TakeBombDrop() == true;

    // The bomb thrown into the world, while it lies there.
    private OpenIGI.Game.World.GunPickup? _thrownBomb;

    /// <inheritdoc/>
    /// <remarks>
    /// A pickup of the bomb's weapon, flown as a thrown gun is (<see cref="OpenIGI.Game.World.PickupThrow"/>) against the level with
    /// the C4's own shape, and sent to the joiners as any throw; walking over it takes nothing -- the mode hands it to an attacker.
    /// </remarks>
    public bool ThrowBomb(int slot, Vector3d from, float yaw, bool far)
    {
        if (_game.World.GunPickups is not { Mirrored: false } guns)
        {
            return false;
        }

        TakeThrownBomb();
        PlayerCamera? rig = slot >= 0 ? LivingRig(slot) : null;
        double heading = OpenIGI.Game.World.PickupThrow.Heading(rig?.Body.Yaw ?? yaw, far, Random.Shared.NextDouble());
        Vector3d root = rig?.Body.Origin ?? new Vector3d(from.X, from.Y, from.Z + BodyRootUnits);
        Vector3d at = new(root.X, root.Y, root.Z + OpenIGI.Game.World.PickupThrow.AboveRoot);
        _thrownBomb = guns.Throw(
            at, Matrix3f.RotationZ(-(float)heading), Igi1BombWeapon.Type, OpenIGI.Game.World.PickupThrow.Velocity(heading, far), null, rig?.Weapons);
        _thrownBomb.WalkOverTakes = false;
        if (Igi1BombWeapon.Collider is { } shape)
        {
            _thrownBomb.UseCollider(shape);
        }

        return true;
    }

    // Where a body's root stands above its feet, for a throw from one no longer there: a metre.
    private const double BodyRootUnits = WorldUnits.PerMetre;

    /// <inheritdoc/>
    public Vector3d? ThrownBombAt => _thrownBomb is { IsCollected: false } bomb ? bomb.Position : null;

    /// <inheritdoc/>
    public void TakeThrownBomb()
    {
        if (_thrownBomb is { IsCollected: false } bomb && _game.World.GunPickups is { } guns)
        {
            guns.Withdraw(bomb);
        }

        _thrownBomb = null;
    }

    /// <inheritdoc/>
    public bool IsPlanting(int slot) =>
        LivingRig(slot) is { } rig && Igi1BombWeapon.Holds(rig)
        && (rig.LastFrame.IsSet(PlayerInputSlot.Activate) || rig.LastFrame.IsSet(PlayerInputSlot.Fire));

    // A player as a snapshot carries it, marked when it carries the bomb.
    private ReplicatedHuman Carrying(ReplicatedHuman human, int slot) =>
        slot == _bombCarrier && _bombCarrier >= 0 ? human with { Flags = human.Flags | ReplicatedHumanCondition.CarriesBomb } : human;

    /// <inheritdoc/>
    public float? YawOf(int slot) =>
        slot == ServerSession.HostSlot
            ? IncludesHost && _game.World.Player is { } own ? own.Body.Yaw : null
            : _bySlot.TryGetValue(slot, out Igi1NetworkPlayer? player) ? player.Player.Body.Yaw : null;

    /// <inheritdoc/>
    public void Blast(Vector3d at, double radius) => _game.Blasts.ModeBlast(at, radius);

    /// <inheritdoc/>
    public bool IsUsing(int slot) => LivingRig(slot) is { } rig && rig.LastFrame.IsSet(PlayerInputSlot.Activate);

    /// <inheritdoc/>
    public void HoldStill(bool held)
    {
        foreach (Igi1NetworkPlayer joined in _bySlot.Values)
        {
            joined.Player.HeldStill = held;
        }

        if (IncludesHost && _game.World.Player is { } own)
        {
            own.HeldStill = held;
        }
    }

    /// <inheritdoc/>
    public bool Give(int slot, string weapon, int rounds) =>
        LivingRig(slot) is { } rig && _game.PlayerSetup is { } setup && setup.Give(rig, weapon, rounds);

    // A player's rig while it lives: the host's own, or a joined player's.
    private PlayerCamera? LivingRig(int slot) =>
        slot == ServerSession.HostSlot
            ? (IncludesHost && !_game.World.IsPlayerDead ? _game.World.Player : null)
            : _bySlot.TryGetValue(slot, out Igi1NetworkPlayer? player) && !player.IsDead ? player.Player : null;

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

        // No cameras in an arena: they would only raise an alarm for soldiers who are not there.
        _game.World.SecurityCameras?.SwitchedOff = true;

        // Nor the level's scenes, which would take the players' views from them mid-round.
        _game.World.Cutscenes?.Held = true;

        // A match's rounds put the level back, so a pickup taken keeps its task to come back to.
        _game.World.GunPickups?.KeepsTakenTasks = true;
        _game.World.GenericPickups?.KeepsTakenTasks = true;
        PutHostInArena();
        return true;
    }

    /// <inheritdoc/>
    public void MarkLevelStart() => _game.MarkLevelStart();

    /// <inheritdoc/>
    /// <remarks>Who took which gun goes with the guns.</remarks>
    public void ResetToLevelStart()
    {
        _game.ResetToLevelStart();
        _takenBy.Clear();
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
