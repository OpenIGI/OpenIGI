using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>A co-op client's I.G.I. mission: its own player as usual, the others stood where the server's snapshots put them.</summary>
public sealed class Igi1ClientWorld : IReplicatedClientWorld
{
    private readonly Igi1Gameplay _game;
    private readonly Dictionary<int, Igi1NetworkPlayer> _others = [];
    private readonly float[] _channels = new float[Igi1Command.ChannelCount];
    private readonly Dictionary<int, ReplicatedHuman> _shown = [];

    // The other players' names, by entity, from the server's scoreboard.
    private readonly Dictionary<int, string> _names = [];

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

    /// <summary>Initializes a new instance of the <see cref="Igi1ClientWorld"/> class.</summary>
    /// <param name="game">The client's own mission.</param>
    public Igi1ClientWorld(Igi1Gameplay game)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        game.World.PartnerBodies = PartnerBodies;
        game.World.CoopClient = true;
    }

    // The partners the frame draws on a client: each where the snapshots put it, its legs and torso on the clips the server's ran
    // (a torso clip started again when the server's was), and the weapon the server has in its hand, fired when its count moved.
    private List<Ai.PartnerBody> PartnerBodies()
    {
        List<Ai.PartnerBody> bodies = [];
        foreach (ReplicatedHuman h in _shown.Values)
        {
            if ((h.Flags & ReplicatedHumanCondition.Alive) == 0)
            {
                continue;
            }

            bool fired = _shotsDrawn.TryGetValue(h.Id, out byte drawn) && drawn != h.Shots;
            _shotsDrawn[h.Id] = h.Shots;
            OpenIGI.Game.Weapons.WeaponType? gun =
                h.Weapon > 0 && _game.PlayerSetup?.Weapons is { } roster && roster.TryGet(h.Weapon - 1, out OpenIGI.Game.Weapons.WeaponType held)
                    ? held
                    : null;
            bodies.Add(new Ai.PartnerBody(
                h.Id, h.Position.ToVector(), -Quantize.AngleFromBits(h.Yaw), h.State - 1, true,
                new Ai.PlayerBodyPose(h.Action - 1, true, 0f, h.ActionSequence, Quantize.AngleFromBits(h.Pitch)),
                gun,
                h.Shots,
                fired,
                (h.Flags & ReplicatedHumanCondition.Climbing) != 0 ? h.ClipTime / Igi1ReplicatedWorld.ClipTimeScale : null,
                _names.GetValueOrDefault(h.Id)));
        }

        return bodies;
    }

    /// <inheritdoc/>
    public ReadOnlySpan<float> LocalFrame
    {
        get
        {
            if (_game.World.Player is { } player)
            {
                Igi1Command.Pack(player.LastFrame, player.Body.Yaw, player.Body.Pitch, _channels);
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
    public void Name(int entity, string name) => _names[entity] = name;

    /// <inheritdoc/>
    public void MoveLocal(Vector3d offset)
    {
        if (_game.World.Player is { } player)
        {
            player.Body.Position += offset;
            player.ViewError.Note(offset);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Its rig is made the first time and then only stood and turned: a client runs nobody's controls but its own.</remarks>
    public void Show(in ReplicatedHuman human)
    {
        // A soldier: the client's own copy is posed as the server's.
        if (human.Id >= Igi1ReplicatedWorld.SoldierEntityBase)
        {
            if (_game.World.Soldiers is { } soldiers && soldiers.SoldierById(human.Id - Igi1ReplicatedWorld.SoldierEntityBase) is { } soldier)
            {
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

            other = new Igi1NetworkPlayer(made.Player, made.Health);
            _others[human.Id] = other;
        }

        _shown[human.Id] = human;
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
    public void ApplyWorld(Snapshot snapshot, int? ownEntity)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Where the others' rounds landed, once each; this client's own it showed as it fired them.
        if (snapshot.Tick > _impactsShown)
        {
            _impactsShown = snapshot.Tick;
            foreach (ReplicatedImpact impact in snapshot.Impacts ?? [])
            {
                if (impact.Source != ownEntity)
                {
                    if (!_bloodOwners.TryGetValue(impact.Source, out object? owner))
                    {
                        _bloodOwners[impact.Source] = owner = new object();
                    }

                    _game.Impacts.Replay(impact, owner);
                }
            }
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

    /// <inheritdoc/>
    public void Hide(int id)
    {
        _others.Remove(id);
        _shown.Remove(id);
        _shotsDrawn.Remove(id);
    }
}
