using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>A co-op client's I.G.I. mission: its own player as usual, the others stood where the server's snapshots put them.</summary>
public sealed class Igi1ClientWorld(Igi1Gameplay game) : IReplicatedClientWorld
{
    private readonly Igi1Gameplay _game = game ?? throw new ArgumentNullException(nameof(game));
    private readonly Dictionary<int, Igi1NetworkPlayer> _others = [];
    private readonly float[] _channels = new float[Igi1Command.ChannelCount];
    private readonly Dictionary<int, ReplicatedHuman> _shown = Watch(game);

    // The partners the frame draws on a client: each where the snapshots put it, its legs and torso on the clips the server's ran.
    private static Dictionary<int, ReplicatedHuman> Watch(Igi1Gameplay game)
    {
        Dictionary<int, ReplicatedHuman> shown = [];
        game.World.PartnerBodies = () => [.. shown.Values
            .Where(static h => (h.Flags & ReplicatedHumanCondition.Alive) != 0)
            .Select(static h => new Ai.PartnerBody(
                h.Id, h.Position.ToVector(), -Quantize.AngleFromBits(h.Yaw), h.State - 1, true,
                new Ai.PlayerBodyPose(h.Action - 1, true, 0f, -1, Quantize.AngleFromBits(h.Pitch))))];
        return shown;
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
    public void MoveLocal(Vector3d offset)
    {
        if (_game.World.Player is { } player)
        {
            player.Body.Position += offset;
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
                    (human.Flags & ReplicatedHumanCondition.Alive) != 0));
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
    public void Hide(int id)
    {
        _others.Remove(id);
        _shown.Remove(id);
    }
}
