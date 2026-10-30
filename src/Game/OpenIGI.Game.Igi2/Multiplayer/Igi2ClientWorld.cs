using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>A client's I.G.I. 2 level: its own player as usual, and each other player a puppet the snapshots pose.</summary>
public sealed class Igi2ClientWorld : IReplicatedClientWorld
{
    private readonly Igi2TitlePlayer _level;
    private readonly Dictionary<int, Igi2NetworkHuman> _puppets = [];

    /// <summary>Initializes a new instance of the <see cref="Igi2ClientWorld"/> class.</summary>
    /// <param name="level">The client's level, as its own player holds it.</param>
    public Igi2ClientWorld(Igi2TitlePlayer level) => _level = level ?? throw new ArgumentNullException(nameof(level));

    /// <inheritdoc/>
    public ReadOnlySpan<float> LocalFrame => _level.LastFrame;

    /// <inheritdoc/>
    public IReadOnlyCollection<int> Shown => _puppets.Keys;

    /// <summary>Gets the puppet of another player, or <see langword="null"/>.</summary>
    /// <param name="id">Its entity id.</param>
    /// <returns>The puppet.</returns>
    public Igi2NetworkHuman? PuppetOf(int id) => _puppets.GetValueOrDefault(id);

    /// <inheritdoc/>
    public void Show(in ReplicatedHuman human)
    {
        if (!_puppets.TryGetValue(human.Id, out Igi2NetworkHuman? puppet))
        {
            if (_level.Human.Model is not { } model
                || _level.AddNetworkHuman(model.Name, human.Position.ToVector(), Quantize.AngleFromBits(human.Yaw), human.Team) is not { } made)
            {
                return;
            }

            puppet = made;
            _puppets[human.Id] = puppet;
        }

        puppet.PuppetTarget = human;
    }

    /// <inheritdoc/>
    public void Hide(int id)
    {
        if (_puppets.Remove(id, out Igi2NetworkHuman? puppet))
        {
            _level.RemoveNetworkHuman(puppet);
        }
    }
}
