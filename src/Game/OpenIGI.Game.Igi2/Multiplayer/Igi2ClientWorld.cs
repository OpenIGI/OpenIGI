using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Soldiers;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>A client's I.G.I. 2 level: its own player as usual, each other player a puppet the snapshots pose, and the soldiers the server's.</summary>
/// <remarks>
/// <para>
/// The soldiers are one world for every player: only the server's minds run them and only the server's copies are hurt. Here
/// each is the server's puppet from the moment the session takes the level, standing where it was put until the first
/// snapshot poses it, so no mind of this client's ever moves or fires one.
/// </para>
/// <para>
/// The other players are drawn and block this player's way, but stand in none of the level's own lists: the server's soldiers,
/// areas and alarms weigh the server's copies.
/// </para>
/// </remarks>
public sealed partial class Igi2ClientWorld : IReplicatedClientWorld
{
    // The applies in a row the server's health has stood above this game's before it is taken: two seconds.
    private const int HealthPatience = 60;

    private readonly Igi2TitlePlayer _level;
    private readonly Dictionary<int, Igi2NetworkHuman> _puppets = [];
    private int _healthBehind;

    /// <summary>Initializes a new instance of the <see cref="Igi2ClientWorld"/> class.</summary>
    /// <param name="level">The client's level, as its own player holds it.</param>
    public Igi2ClientWorld(Igi2TitlePlayer level)
    {
        _level = level ?? throw new ArgumentNullException(nameof(level));

        // A session joining again on a new line (its old one went quiet) takes the same level over: the copies the last
        // session's world made of the others are let go, or they stood on, frozen where they were last shown and drawn beside
        // the new copies this world makes of the same players.
        foreach (Igi2NetworkHuman stale in _level.NetworkHumans.ToArray())
        {
            _level.RemoveNetworkHuman(stale);
        }

        foreach (Igi2HumanSoldier soldier in _level.Soldiers?.Bodies ?? [])
        {
            soldier.Puppet = soldier.PuppetPose();
            soldier.Weapon.Firing?.IsReplica = true;
        }

        _level.Marks = this;

        // The client's own player falls when the server's does and stands again when revived: what it carries is the server's
        // to take, not the fall's, so its HUD and its predicted shots keep the kit through it.
        _level.Human.KeepsWeaponsWhenDown = true;

        // Its player's draws come from a stream of its own, set from each command's seed, as the server's copy's are: the
        // level's other users of the level stream it shared keep drawing from that one.
        _level.Human.Random = new IgiRandom();
        _level.SeedsCommands = true;
        _vehicles = new Igi2VehicleStates(_level);
        _vehicles.Puppet();
        BindLevelState();
    }

    // The command sent up for the tick: the player's frame, where it looked as the frame began, and the weapon it chose (Igi2Command).
    private readonly float[] _command = new float[Igi2Command.ChannelCount];

    /// <inheritdoc/>
    public ReadOnlySpan<float> LocalFrame
    {
        get
        {
            Igi2Command.Pack(_level.LastFrame, _level.CommandStart, _level.CommandEnd, _command);
            return _command;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<int> Shown => _puppets.Keys;

    /// <inheritdoc/>
    public Vector3d LocalPosition => _level.Human.Position;

    /// <inheritdoc/>
    /// <remarks>
    /// The soldiers' rounds land on the server's copy of this player, so this one is told how hurt it is and takes the
    /// difference; a fall the server's took is this one's fall, and the server standing it up again (a revival, a retry from
    /// the checkpoint) stands this one up where the server's stands. Healthier there for longer than a round trip -- a hurt
    /// only this game counted -- the server's stands.
    /// </remarks>
    public void ApplyOwn(in ReplicatedHuman self)
    {
        Igi2HumanPlayer own = _level.Human;
        _ownEntity = self.Id;
        bool alive = (self.Flags & ReplicatedHumanCondition.Alive) != 0;
        if (!alive)
        {
            if (!own.IsDead)
            {
                own.Damage.Damage = own.Damage.MaxDamage + 1f;
                own.UpdateDie();
            }

            return;
        }

        if (own.IsDead)
        {
            own.Revive(self.Position.ToVector(), Quantize.AngleFromBits(self.Yaw));
            _healthBehind = 0;
            return;
        }

        float max = own.Damage.MaxDamage;
        if (max <= 0f)
        {
            return;
        }

        float server = self.Health / 255f;
        float local = Math.Clamp(1f - (own.Damage.Damage / max), 0f, 1f);
        if (server < local - 0.01f)
        {
            own.Damage.Damage += (local - server) * max;
        }

        _healthBehind = server > local + 0.01f ? _healthBehind + 1 : 0;
        if ((server >= 0.999f && local < 0.999f) || _healthBehind > HealthPatience)
        {
            own.Damage.Damage = Math.Max(0f, (1f - server) * max);
            _healthBehind = 0;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Through the body's own shift, so a climber's ladder and a rider's platform take it too (<see cref="Humans.Igi2Human.Shift"/>);
    /// and, as Project I.G.I.'s client does, a body this client has falling that the server has standing, lifted by the
    /// correction, stops falling: kept, the fall carried it through the floor again before the next word came.
    /// </remarks>
    public void MoveLocal(Vector3d offset, bool serverGrounded)
    {
        Igi2HumanPlayer own = _level.Human;
        own.Shift(offset);
        if (serverGrounded && offset.Z > 0 && own.AttachParent is null
            && (own.Signal & Igi2PlayerSignal.Grounded) == 0 && own.Speed.Z < 0f)
        {
            own.Speed = new Vector3f(own.Speed.X, own.Speed.Y, 0f);
            own.ImpactSpeed = default;
        }

        own.UpdateViewBody(updateAnimation: false);
        _level.ViewError.Note(offset);
    }

    /// <summary>Gets the puppet of another player, or <see langword="null"/>.</summary>
    /// <param name="id">Its entity id.</param>
    /// <returns>The puppet.</returns>
    public Igi2NetworkHuman? PuppetOf(int id) => _puppets.GetValueOrDefault(id);

    /// <inheritdoc/>
    public void Show(in ReplicatedHuman human)
    {
        if (Igi2ReplicatedWorld.SoldierIndex(human.Id) is { } index)
        {
            if (_level.Soldiers is { } soldiers && (uint)index < (uint)soldiers.Bodies.Count)
            {
                soldiers.Bodies[index].Puppet = Igi2ReplicatedWorld.PoseOf(human);
            }

            return;
        }

        if (!_puppets.TryGetValue(human.Id, out Igi2NetworkHuman? puppet))
        {
            if (_level.PlayerBodyFor(human.Team, _looks.GetValueOrDefault(human.Id)) is not { } body
                || _level.AddPartnerPuppet(body, human.Position.ToVector(), Quantize.AngleFromBits(human.Yaw), human.Team) is not { } made)
            {
                return;
            }

            puppet = made;
            puppet.Weapons?.Firing.IsReplica = true;
            _puppets[human.Id] = puppet;
        }

        puppet.PuppetTarget = human;
    }

    // The look each other player chose, by entity, a PlayerSkins id, from the server's scoreboard.
    private readonly Dictionary<int, string> _looks = [];

    /// <inheritdoc/>
    /// <remarks>A copy already made in another body is let go, and made again in the look's at the next snapshot.</remarks>
    public void Skin(int entity, string skin)
    {
        if (_looks.TryGetValue(entity, out string? had) && had == skin)
        {
            return;
        }

        _looks[entity] = skin;
        if (_puppets.TryGetValue(entity, out Igi2NetworkHuman? puppet)
            && !string.Equals(puppet.Human.Model?.Name, _level.PlayerBodyFor(puppet.Human.Team, skin), StringComparison.OrdinalIgnoreCase))
        {
            Hide(entity);
        }
    }

    /// <inheritdoc/>
    /// <remarks>A soldier the snapshots leave out (its container out of the world on the server) keeps its last pose.</remarks>
    public void Hide(int id)
    {
        if (_puppets.Remove(id, out Igi2NetworkHuman? puppet))
        {
            _level.RemoveNetworkHuman(puppet);
        }
    }
}
