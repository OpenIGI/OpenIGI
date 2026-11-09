using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>A co-op client's I.G.I. mission: its own player as usual, the others stood where the server's snapshots put them.</summary>
public sealed class Igi1ClientWorld : IReplicatedClientWorld
{
    /// <inheritdoc/>
    public void MarkLevelStart() => _game.MarkLevelStart();

    /// <inheritdoc/>
    /// <remarks>
    /// A player climbing when the round ended lets go here at once: the server puts it back at a spawn with the reset, and
    /// waiting out the ladder patience held it on the ladder here for a second and a half before it snapped there.
    /// </remarks>
    public void ResetToLevelStart()
    {
        _game.ResetToLevelStart();
        _game.World.Player?.ReleaseLadder();
        _offLadderThere = 0;
    }

    /// <inheritdoc/>
    public void ArmOwn(IReadOnlyList<(string Weapon, int Rounds)> kit, bool fresh)
    {
        if (_game.World.Player is { } own && _game.PlayerSetup is { } setup)
        {
            setup.Arm(own, kit, fresh);
        }
    }

    /// <inheritdoc/>
    public bool CarriesOwn(string weapon) =>
        _game.World.Player?.Weapons is { } weapons
        && _game.PlayerSetup?.Weapons is { } types
        && types.TryGetByScriptId(weapon, out Game.Weapons.WeaponType type)
        && weapons.HasWeapon(type.WeaponId);

    /// <inheritdoc/>
    /// <remarks>A gun taken in hand is picked as the weapon wheel picks, so the server is sent the pick and runs it too.</remarks>
    public void GiveOwn(string weapon, int rounds, bool equip)
    {
        if (_game.World.Player is { } own && _game.PlayerSetup is { } setup && setup.Give(own, weapon, rounds) && equip)
        {
            Igi1PlayerSetup.TakeInHand(own, setup, weapon);
        }
    }

    private readonly Igi1Gameplay _game;
    private readonly Dictionary<int, Igi1NetworkPlayer> _others = [];
    private readonly float[] _channels = new float[Igi1Command.ChannelCount];
    private readonly Dictionary<int, ReplicatedHuman> _shown = [];

    // This client's own entity, as the server's snapshots name it.
    private int _ownEntity = -1;

    // Whether the server's level state has been taken yet: the first is a catching-up.
    private bool _levelTaken;

    // The other players' names and looks, by entity, from the server's scoreboard.
    private readonly Dictionary<int, string> _names = [];
    private readonly Dictionary<int, string> _skins = [];

    // Each partner's shot count as last drawn: a count moved on is a shot to flame and sound on this tick.
    private readonly Dictionary<int, byte> _shotsDrawn = [];

    // The newest snapshot whose rounds were shown, so each is shown once however many frames it stays the newest; and what
    // each shooter's blood is kept against.
    private long _impactsShown = long.MinValue;
    private readonly Dictionary<int, object> _bloodOwners = [];

    // The doors as the server last had them, and for how many snapshots each has disagreed with this client's own.
    private byte[]? _serverDoors;
    private int[] _doorDisagreement = [];

    // Snapshots a door may stand otherwise than the server's before the server's is taken: about a second. Long enough
    // for this client's own use of a door to reach the server and come back, so the door doesn't flick shut and open again.
    private const int DoorPatience = 30;

    // The newest snapshot whose lifts were compared.
    private long _liftsChecked = long.MinValue;

    // How far, in world units, this client's car may stand from the server's projected before the server's is taken: a
    // few ticks of travel, the most the server's queue of this client's commands shifting moves the projection.
    private const double LiftTolerance = 512;

    // How many snapshots running this player has been on a ladder here and off it on the server, and how many it may be:
    // a second and a half, past a mount's round trip.
    private int _offLadderThere;
    private const int LadderPatience = 45;

    /// <summary>Initializes a new instance of the <see cref="Igi1ClientWorld"/> class.</summary>
    /// <param name="game">The client's own mission.</param>
    public Igi1ClientWorld(Igi1Gameplay game)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        game.World.PartnerBodies = PartnerBodies;
        game.World.CoopClient = true;

        // The level's running state is the server's: counters, messages, result and scenes come with each change.
        game.World.Objectives?.Mirrored = true;
        game.World.Cutscenes?.Mirrored = true;
        game.World.GunPickups?.Mirrored = true;
        game.World.GenericPickups?.Mirrored = true;

        // The switches, terminals, TBAs, cabinets, locks, fences, generators and radios are what the server's are.
        game.World.Interactions?.Mirrored = true;

        // The lifts go where the server's go: a car started here on this game's late copy of its doors and switches.
        game.World.Elevators?.Mirrored = true;

        // The cameras look as the server's do, whom they see there: here they would see this client's player alone.
        if (game.World.SecurityCameras is { } cameras)
        {
            cameras.Mirrored = true;
            cameras.MirroredDestruction = camera => game.Blasts.ShootOut(camera.Task);
        }
        if (game.World.Soldiers is { } soldiers)
        {
            soldiers.PuppetsOnly = true;
        }
    }

    // The partners the frame draws on a client: each where the snapshots put it, its legs and torso on the clips the server's ran
    // (a torso clip started again when the server's was), and the weapon the server has in its hand, fired when its count moved.
    private List<Ai.PartnerBody> PartnerBodies()
    {
        List<Ai.PartnerBody> bodies = [];
        foreach (ReplicatedHuman h in _shown.Values)
        {
            // A fallen partner still lies there, its health byte carrying its revival.
            bool alive = (h.Flags & ReplicatedHumanCondition.Alive) != 0;
            bool fired = _shotsDrawn.TryGetValue(h.Id, out byte drawn) && drawn != h.Shots;
            _shotsDrawn[h.Id] = h.Shots;
            OpenIGI.Game.Weapons.WeaponType? gun =
                h.Weapon - 1 == Igi1BombWeapon.Id ? Igi1BombWeapon.Type
                : h.Weapon > 0 && _game.PlayerSetup?.Weapons is { } roster && roster.TryGet(h.Weapon - 1, out OpenIGI.Game.Weapons.WeaponType held)
                    ? held
                    : null;
            bodies.Add(new Ai.PartnerBody(
                h.Id, h.Position.ToVector(), -Quantize.AngleFromBits(h.Yaw), h.State - 1, true,
                new Ai.PlayerBodyPose(h.Action - 1, (h.Flags & ReplicatedHumanCondition.UpperBodyOnce) == 0, 0f, h.ActionSequence, Quantize.AngleFromBits(h.Pitch)),
                gun,
                h.Shots,
                fired,
                (h.Flags & ReplicatedHumanCondition.Climbing) != 0 ? Igi1ReplicatedWorld.UnpackClipTime(h.ClipTime) : null,
                Game.Multiplayer.Modes.PlayerSides.ShowsName(_mode, _ownTeam, _teamOf(h.Id)) ? _names.GetValueOrDefault(h.Id) : null,
                !alive,
                alive || !Game.Multiplayer.Modes.PlayerSides.Revivable(_mode) ? null : h.Health / 255f,
                PlayerSkins.Of(_skins.GetValueOrDefault(h.Id)).Model,
                (h.Flags & ReplicatedHumanCondition.CarriesBomb) != 0));
        }

        return bodies;
    }

    /// <summary>Gets how many of the level's objects were struck here on the server's word: broken, blown up or shot out there.</summary>
    public int WrecksTaken { get; private set; }

    // The newest snapshot whose projectiles were taken.
    private long _projectilesTaken = long.MinValue;

    // Further than this between two snapshots, a projectile is another one that took its place, and is not drawn sliding there.
    private const double ProjectileJumpUnits = 16384.0;

    // The others' grenades, mines, rockets and launcher rounds, drawn from where the last snapshot had each to where this one
    // does: each matched to the one before it by whose it is and which of that player's it is. This client's own it draws from
    // its own weapons.
    private void ShowProjectiles(ReplicatedProjectile[]? sent, int? ownEntity)
    {
        List<OpenIGI.Game.Weapons.ShownProjectile> shown = _game.World.ShownProjectiles;
        _previousProjectiles.Clear();
        _previousProjectiles.AddRange(shown);
        _previousOwners.Clear();
        _previousOwners.AddRange(_shownOwners);
        shown.Clear();
        _shownOwners.Clear();
        foreach (ReplicatedProjectile projectile in sent ?? [])
        {
            if (projectile.Owner == ownEntity)
            {
                continue;
            }

            Vector3d at = projectile.Position.ToVector();
            int ordinal = _shownOwners.Count(o => o == projectile.Owner);
            Vector3d from = at;
            for (int i = 0, seen = 0; i < _previousProjectiles.Count; i++)
            {
                if (_previousOwners[i] != projectile.Owner)
                {
                    continue;
                }

                if (seen++ == ordinal)
                {
                    OpenIGI.Game.Weapons.ShownProjectile before = _previousProjectiles[i];
                    if (before.Model == projectile.Model && Vector3d.Distance(before.Position, at) <= ProjectileJumpUnits)
                    {
                        from = before.Position;
                    }

                    break;
                }
            }

            shown.Add(new OpenIGI.Game.Weapons.ShownProjectile(projectile.Model, from, at, projectile.Orientation, projectile.Owner, ordinal));
            _shownOwners.Add(projectile.Owner);
        }
    }

    private readonly List<OpenIGI.Game.Weapons.ShownProjectile> _previousProjectiles = [];
    private readonly List<int> _previousOwners = [];
    private readonly List<int> _shownOwners = [];

    // The newest snapshot whose wrecks were taken.
    private long _wrecksChecked = long.MinValue;

    /// <summary>Gets how many times a lift here was put where the server's stood: once per partner's call, not every tick.</summary>
    public int LiftsTaken { get; private set; }

    /// <inheritdoc/>
    public ReadOnlySpan<float> LocalFrame
    {
        get
        {
            if (_game.World.Player is { } player)
            {
                Igi1Command.Pack(player.LastFrame, player.FrameYaw, player.FramePitch, _channels, player.FrameAim, player.Weapons?.ChosenWeaponId ?? -1);
            }

            return _channels;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<int> Shown => _others.Keys;

    /// <inheritdoc/>
    public Vector3d LocalPosition => _game.World.Player?.Body.Position ?? default;

    /// <summary>Gets another player as this client shows it, or <see langword="null"/>.</summary>
    /// <param name="id">Its entity id.</param>
    /// <returns>The player.</returns>
    public Igi1NetworkPlayer? OtherOf(int id) => _others.GetValueOrDefault(id);

    /// <inheritdoc/>
    /// <remarks>
    /// The soldiers' rounds land on the server's copy of this client's player, so the client is told how hurt it is and takes
    /// the difference; a server's respawn (full health again) is taken too.
    /// </remarks>
    public void ApplyOwn(in ReplicatedHuman self)
    {
        _ownEntity = self.Id;

        // A match lets its players drop what they hold; the drop is foreseen here and the gun laid by the server.
        _game.World.Player?.CanDropWeapons = true;
        // Killed on the server: the fall, here. Brought back by the server's respawn: up again where it stood this one.
        bool alive = (self.Flags & ReplicatedHumanCondition.Alive) != 0;
        if (!alive && !_game.World.IsPlayerDead && _game.World.Player is { } falling)
        {
            _game.Harm.BeginPlayerDeath(falling.Body.Position, zone: 0, bone: -1, OpenIGI.Game.Weapons.WeaponCategory.Gun);
            return;
        }

        if (alive && _game.World.IsPlayerDead)
        {
            _game.Harm.RevivePlayer(self.Position.ToVector(), Quantize.AngleFromBits(self.Yaw));
            return;
        }

        if (!alive)
        {
            return;
        }

        // On a ladder here and off it on the server, for longer than this player's own mount takes to reach the server and
        // come back: the server's slide ended, or it never climbed. Let go, as the server has, rather than be held on the
        // ladder here while every snapshot pulls the player to where the server has it standing or falling.
        bool climbingThere = (self.Flags & ReplicatedHumanCondition.Climbing) != 0;

        // Planting or defusing there: the same clip here, at the server's time, for the arms and the hands' bomb. Carrying it
        // there: carried here, a weapon in the cycle.
        if (_game.World.Player is { } own)
        {
            Igi1BombWeapon.Keep(own, (self.Flags & ReplicatedHumanCondition.CarriesBomb) != 0);
            int clip = self.State - 1;
            if (climbingThere && clip is Igi1BombAnimations.Plant or Igi1BombAnimations.Defuse)
            {
                own.PlayModeClip(clip, Igi1ReplicatedWorld.UnpackClipTime(self.ClipTime));
            }
            else if (own.ModeAnimation >= 0)
            {
                own.PlayModeClip(-1, 0f);
            }
        }

        _offLadderThere = _game.World.Player is { IsOnLadder: true } && !climbingThere ? _offLadderThere + 1 : 0;
        if (_offLadderThere > LadderPatience && _game.World.Player is { } climber)
        {
            climber.ReleaseLadder();
            _offLadderThere = 0;
        }

        HumanHealth health = _game.World.PlayerHealth;
        float server = self.Health / 255f;
        float local = health.RemainingFraction;
        if (server >= 0.999f && local < 0.999f)
        {
            health.Reset();
        }
        else if (server < local - 0.01f)
        {
            health.ApplyDirect(0, (local - server) * health.MaximumHealth);
        }
    }

    /// <inheritdoc/>
    public bool? HoldsBomb => _game.World.Player is { } own && Igi1BombWeapon.Holds(own);

    /// <inheritdoc/>
    public void ApplyLevel(byte[] state)
    {
        ArgumentNullException.ThrowIfNull(state);
        BitReader reader = new(state);
        _game.World.Objectives?.ReadState(ref reader, catchingUp: !_levelTaken);
        _levelTaken = true;
        if (reader.ReadBool() && _game.World.Cutscenes is { } cutscenes)
        {
            cutscenes.ReadRuns(ref reader);
        }

        ReadPickups(ref reader);
    }

    // How far a thrown gun flown here may lie from where the server's stopped before it is put there while still moving: two metres.
    private const double StrayThrowUnits = 8192.0;

    // A pickup's place as the server writes it: where, to the unit, and how it is turned.
    private static (Vector3d At, Matrix3f Turn) ReadPlace(ref BitReader reader, scoped Span<float> o)
    {
        Vector3d at = new(reader.ReadVarInt(), reader.ReadVarInt(), reader.ReadVarInt());
        for (int k = 0; k < o.Length; k++)
        {
            o[k] = reader.ReadSingle();
        }

        return (at, Matrix3f.FromRowMajor(o));
    }

    // The server's pickups: the dropped guns laid where its lie, and each one taken -- into this player's hands, as walking
    // over it would, when the server says this player took it.
    private void ReadPickups(ref BitReader reader)
    {
        Player.PlayerCamera? own = _game.World.Player;
        Span<float> o = stackalloc float[9];
        int guns = (int)reader.ReadVarUInt();
        for (int i = 0; i < guns && !reader.IsOverflowed; i++)
        {
            byte taker = (byte)reader.ReadBits(8);
            OpenIGI.Game.World.GunPickup? gun = _game.World.GunPickups is { } registry && i < registry.Items.Count ? registry.Items[i] : null;
            if (reader.ReadBool())
            {
                int weaponId = (int)reader.ReadVarUInt();
                uint rounds = reader.ReadVarUInt();
                int? left = rounds == 0 ? null : (int)rounds - 1;
                (Vector3d At, Matrix3f Turn, Vector3d Speed)? launch = null;
                if (reader.ReadBool())
                {
                    (Vector3d from, Matrix3f turned) = ReadPlace(ref reader, o);
                    launch = (from, turned, new Vector3d(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
                }

                (Vector3d At, Matrix3f Turn)? lies = reader.ReadBool() ? ReadPlace(ref reader, o) : null;
                if (gun is null
                    && _game.World.GunPickups is { } drops
                    && _game.PlayerSetup?.Weapons is { } roster
                    && (roster.TryGet(weaponId, out OpenIGI.Game.Weapons.WeaponType? weapon) || (weapon = weaponId == Igi1BombWeapon.Id ? Igi1BombWeapon.Type : null) is not null))
                {
                    // A throw flies here from where it left the server's hand, drawn between ticks as the server's own is; one at
                    // rest is laid where it lies.
                    gun = launch is { } thrown && lies is null
                        ? drops.Throw(thrown.At, thrown.Turn, weapon, thrown.Speed, left)
                        : lies is { } rest ? drops.SpawnResting(rest.At, rest.Turn, weapon, left) : null;

                    // The bomb flies with the shape the server's does; a gun's is given by the drawing that loads its model.
                    if (gun is { IsMoving: true } && weaponId == Igi1BombWeapon.Id && Igi1BombWeapon.Collider is { } shape)
                    {
                        gun.UseCollider(shape);
                    }
                }
                else if (gun is { IsCollected: false } && lies is { } rest
                    && (!gun.IsMoving || Vector3d.Distance(gun.Position, rest.At) > StrayThrowUnits) && Vector3d.Distance(gun.Position, rest.At) > 1)
                {
                    // Where the server's came to rest, once this one's flight has ended too -- or at once, if it has strayed.
                    gun.Rest(rest.At, rest.Turn);
                }
            }

            if (gun is { IsCollected: false } && taker != 0)
            {
                if (own is not null && taker == _ownEntity + 1)
                {
                    own.TakePickup(gun);
                }
                else
                {
                    _game.World.GunPickups!.Withdraw(gun);
                }
            }
        }

        int items = (int)reader.ReadVarUInt();
        for (int i = 0; i < items && !reader.IsOverflowed; i++)
        {
            byte taker = (byte)reader.ReadBits(8);
            if (taker == 0 || _game.World.GenericPickups is not { } registry || i >= registry.Items.Count || registry.Items[i].IsCollected)
            {
                continue;
            }

            if (own is not null && taker == _ownEntity + 1)
            {
                own.TakePickup(registry.Items[i]);
            }
            else
            {
                registry.Take(registry.Items[i]);
            }
        }
    }

    /// <inheritdoc/>
    public void Name(int entity, string name)
    {
        _names[entity] = name;
        if (_others.TryGetValue(entity, out Igi1NetworkPlayer? other))
        {
            other.Name = name;
        }
    }

    /// <inheritdoc/>
    public void Skin(int entity, string skin) => _skins[entity] = skin;

    /// <inheritdoc/>
    public void Sides(string mode, Func<int, int?> teamOf, int? ownTeam)
    {
        (_mode, _teamOf, _ownTeam) = (mode, teamOf, ownTeam);

        // A versus match shows no scene, and a joiner's own copy of the level must not say the intro's lines either.
        if (Game.Multiplayer.Modes.GameModes.IsVersus(mode) && _game.World.Cutscenes is { Held: false } scenes)
        {
            scenes.Held = true;
        }

        // Nor any of the level's own pickups, as on the server: none to draw, none to walk over.
        if (Game.Multiplayer.Modes.GameModes.IsVersus(mode))
        {
            _game.World.GunPickups?.LevelCleared = true;
            _game.World.GenericPickups?.LevelCleared = true;
        }
    }

    private string _mode = Game.Multiplayer.Modes.GameModes.Coop;
    private Func<int, int?> _teamOf = static _ => null;
    private int? _ownTeam;

    /// <inheritdoc/>
    public void MoveLocal(Vector3d offset, bool serverGrounded)
    {
        if (_game.World.Player is { } player)
        {
            // Through the player, so a climber's ladder takes it too: corrected on the body alone, the climb's next step put
            // the body back, and the server's word was given again every snapshot.
            player.Shift(offset);
            player.ViewError.Note(offset);

            // Put back up onto the floor the server has it standing on, while this body was falling: the fall is this client's
            // alone, and kept, it carried the body through the floor again before the next word came -- fall, put back, fall.
            if (serverGrounded && offset.Z > 0 && !player.IsOnLadder && !player.Body.IsGrounded && player.Body.Velocity.Z < 0f)
            {
                player.Body.StopStrayFall();
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>Its rig is made the first time and then only stood and turned: a client runs nobody's controls but its own.</remarks>
    public void Show(in ReplicatedHuman human)
    {
        // A soldier: the client's own copy is posed as the server's.
        if (human.Id >= Igi1ReplicatedWorld.SoldierEntityBase)
        {
            int runtimeId = human.Id - Igi1ReplicatedWorld.SoldierEntityBase;
            if (_game.World.Soldiers is { } soldiers
                && (soldiers.SoldierByRuntimeId(runtimeId)
                    ?? (human.Source > 0 && (human.Flags & ReplicatedHumanCondition.Alive) != 0 ? soldiers.ReplaceAsServerDid(human.Source - 1, runtimeId) : null)) is { } soldier)
            {
                soldiers.PuppetsOnly = true;
                soldiers.Puppet(soldier, new Ai.SoldierPuppet(
                    human.Position.ToVector(),
                    Quantize.AngleFromBits(human.Yaw),
                    human.State - 1,
                    human.Action - 1,
                    Quantize.AngleFromBits(human.Pitch),
                    (human.Flags & ReplicatedHumanCondition.Alive) != 0,
                    human.Shots));
            }

            return;
        }

        if (!_others.TryGetValue(human.Id, out Igi1NetworkPlayer? other))
        {
            if (_game.PlayerSetup?.BeginExtra() is not { } made)
            {
                return;
            }

            other = new Igi1NetworkPlayer(made.Player, made.Health) { Entity = human.Id };
            _others[human.Id] = other;
            _game.World.ShownPartners.Add(other);
        }

        _shown[human.Id] = human;
        other.IsDead = (human.Flags & ReplicatedHumanCondition.Alive) == 0;
        other.Player.Body.Position = human.Position.ToVector();
        other.Player.Body.Yaw = Quantize.AngleFromBits(human.Yaw);
        other.Player.Body.Pitch = Quantize.AngleFromBits(human.Pitch);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Each door is commanded as the server's is, and swings here on its own as it does there, sounds and all. Taken when the
    /// server's changes, which is how a door the host opened opens here; and taken when this client's has stood otherwise for
    /// <see cref="DoorPatience"/> snapshots, which settles one this client opened that the server didn't.
    /// </remarks>
    public void ApplyWorld(Snapshot snapshot, int? ownEntity, int ticksAhead, PredictionReconciler predictions)
    {
        ArgumentNullException.ThrowIfNull(predictions);
        ArgumentNullException.ThrowIfNull(snapshot);
        ApplyLifts(snapshot, ticksAhead);

        // The others block this client's player where it is shown them, as the server's bodies block it there.
        if (_game.World.Player is { OtherHumans: null } own)
        {
            SessionHumans.Meet(_game.World, own, static () => false);
        }

        // Where the others' rounds landed, once each; this client's own it showed as it fired them.
        if (snapshot.Tick > _impactsShown)
        {
            _impactsShown = snapshot.Tick;
            // What the server's handlers made this client's player feel -- a round, a blow, a blast -- felt here by the
            // same senses the player's own handlers use.
            foreach (ReplicatedSense sense in snapshot.Senses ?? [])
            {
                if (sense.Entity != ownEntity)
                {
                    continue;
                }

                if (RemotePlayerSenses.TryShove(sense, out Vector3f push, out int carried))
                {
                    TakeShove(push, carried + ticksAhead, predictions);
                }
                else
                {
                    RemotePlayerSenses.Replay(sense, _game.Harm.Own.Senses);
                }
            }

            foreach (ReplicatedImpact impact in snapshot.Impacts ?? [])
            {
                if (impact.Source != ownEntity)
                {
                    if (!_bloodOwners.TryGetValue(impact.Source, out object? owner))
                    {
                        _bloodOwners[impact.Source] = owner = new object();
                    }

                    if (impact.Kind == ImpactKind.Blast)
                    {
                        _game.Blasts.ReplayBlast(impact);
                    }
                    else if (impact.Kind == ImpactKind.Bounce)
                    {
                        _game.Audio.PlayGrenadeBounce(impact.Position.ToVector());
                    }
                    else if (impact.Kind == ImpactKind.MineArmed)
                    {
                        _game.Audio.PlayMineArm(impact.Position.ToVector());
                    }
                    else
                    {
                        _game.Impacts.Replay(impact, owner);
                    }
                }
            }
        }

        if (snapshot.Tick > _projectilesTaken)
        {
            _projectilesTaken = snapshot.Tick;
            ShowProjectiles(snapshot.Projectiles, ownEntity);
        }

        // The server's cameras, which each lens here takes as it updates; and what the server has broken for good.
        _game.World.SecurityCameras?.Mirror(snapshot.Cameras);
        if (snapshot.Tick > _wrecksChecked)
        {
            _wrecksChecked = snapshot.Tick;
            WrecksTaken += _game.Blasts.ApplyWrecks(snapshot.Wrecks);
            _game.World.Interactions?.ApplyState(snapshot.Tasks);
        }

        if (snapshot.Doors is not { } bits || _game.World.Doors is not { Doors.Count: > 0 } registry)
        {
            return;
        }

        if (_doorDisagreement.Length != registry.Doors.Count)
        {
            _doorDisagreement = new int[registry.Doors.Count];
        }

        for (int i = 0; i < registry.Doors.Count && (i >> 3) < bits.Length; i++)
        {
            bool open = (bits[i >> 3] & (1 << (i & 7))) != 0;
            bool changed = _serverDoors is null || (i >> 3) >= _serverDoors.Length || ((_serverDoors[i >> 3] & (1 << (i & 7))) != 0) != open;
            OpenIGI.Game.World.DoorState door = registry.Doors[i].State;
            _doorDisagreement[i] = door.IsCommandedOpen == open ? 0 : _doorDisagreement[i] + 1;
            if (changed || _doorDisagreement[i] > DoorPatience)
            {
                if (open)
                {
                    door.CommandOpen();
                }
                else
                {
                    door.CommandClosed();
                }

                _doorDisagreement[i] = 0;
            }
        }

        _serverDoors = bits;
    }

    // A knock the server gave this client's player -- a round, a blast -- some commands ago, which carried the server's body
    // on through every command since: this player's, which ran those commands without it, is moved as far as the knock had
    // carried it, keeps what of the knock is left, and what it predicted for those commands moves with it. Taken late, as
    // it was, the knock was otherwise the server's alone, and this client was put back by it tick after tick.
    private void TakeShove(Vector3f push, int commands, PredictionReconciler predictions)
    {
        if (_game.World.Player is not { } player || _game.World.IsPlayerDead)
        {
            return;
        }

        // Each command's share of the knock, through the walls as the server's body took it.
        List<Vector3d> each = player.CatchUpHitShove(push, commands);

        // The player now stands after the newest of those commands, whose prediction is yet to be recorded: the ones before it
        // were, and move with it.
        if (each.Count > 1)
        {
            predictions.ShiftNewest(each.GetRange(0, each.Count - 1));
        }
    }

    // Each lift as the server has it, where it will be by the tick this client's world has run to: the snapshot is that
    // many ticks old, and this client's player rides its own copy in the present. The copy only coasts on as it was last
    // told (ElevatorRegistry.Mirrored), so it stands where the server's will, and is put there again only when the server's
    // was told something new -- called, sent elsewhere, held -- whoever pressed the button.
    private void ApplyLifts(Snapshot snapshot, int lead)
    {
        if (snapshot.Tick <= _liftsChecked || snapshot.Lifts is not { } lifts || _game.World.Elevators is not { Elevators.Count: > 0 } registry)
        {
            return;
        }

        _liftsChecked = snapshot.Tick;
        for (int i = 0; i < registry.Elevators.Count && i < lifts.Length; i++)
        {
            OpenIGI.Game.World.PlacedElevator elevator = registry.Elevators[i];
            ReplicatedLift lift = lifts[i];
            OpenIGI.Game.World.ElevatorMotion server = new(lift.Position.ToVector(), lift.Floor, lift.Target < 0 ? null : lift.Target, lift.Speed);
            OpenIGI.Game.World.ElevatorMotion due = elevator.State.Project(server, lead);
            OpenIGI.Game.World.ElevatorMotion own = elevator.State.Motion;

            // A car arriving a tick sooner or later here than the projection says, as the server's queue of this client's
            // commands shifts, is the same trip: only a new destination, or a car out of place, is taken.
            bool told = due.Target is int target && target != own.Target;
            if (!told && Vector3d.Distance(due.Position, own.Position) <= LiftTolerance)
            {
                continue;
            }

            // A car this client's player stands in takes the player with it. Put where the server's is, the car jumps by
            // the ticks of travel this client learned of late -- the higher the ping, the further -- and, left where it
            // stood, the player was under the floor of a car going up and fell out of it. Where it stands on the car is
            // what its predictions are weighed by, so they need not move.
            if (_game.World.Player is { } rider
                && OpenIGI.Game.World.ElevatorRegistry.IsPassenger(elevator, rider.Body.Position, _game.World.Solids))
            {
                rider.Shift(due.Position - own.Position);
            }

            registry.Restore(elevator, due, _game.World.Solids);
            LiftsTaken++;
        }
    }

    /// <inheritdoc/>
    /// <remarks>By the rule the server's car carries a player by, the one it marks its riders by too.</remarks>
    public PlatformFrame? LocalPlatform
    {
        get
        {
            if (_game.World.Elevators is not { Elevators.Count: > 0 } lifts
                || _game.World.Player is not { IsOnLadder: false } player
                || _game.World.IsPlayerDead)
            {
                return null;
            }

            for (int i = 0; i < lifts.Elevators.Count && i < ReplicatedLift.MaxPerSnapshot; i++)
            {
                OpenIGI.Game.World.PlacedElevator lift = lifts.Elevators[i];
                if (OpenIGI.Game.World.ElevatorRegistry.IsPassenger(lift, player.Body.Position, _game.World.Solids))
                {
                    return new PlatformFrame(i, lift.State.Position);
                }
            }

            return null;
        }
    }

    /// <inheritdoc/>
    public Vector3d? PlatformOrigin(int index) =>
        _game.World.Elevators is { } lifts && (uint)index < (uint)lifts.Elevators.Count ? lifts.Elevators[index].State.Position : null;

    /// <inheritdoc/>
    public void Hide(int id)
    {
        if (_others.Remove(id, out Igi1NetworkPlayer? gone))
        {
            _game.World.ShownPartners.Remove(gone);
        }

        _shown.Remove(id);
        _shotsDrawn.Remove(id);
    }
}
