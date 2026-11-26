using OpenIGI.Game.Igi2.Multiplayer;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// A race's ghost beside the player: a body made as another player's is but part of nothing in the level
/// (<see cref="Igi2NetworkHumanFactory.MakeGhost"/>), posed each tick from the runner's track as a client poses its copy of a
/// partner, and drawn only by the ghost's own pass.
/// </content>
public sealed partial class Igi2TitlePlayer : ITitleGhost
{
    private Igi2NetworkHuman? _ghost;

    /// <inheritdoc/>
    public void FollowGhost(ReplicatedHuman? frame)
    {
        if (frame is not { } runner)
        {
            _ghost = null;
            return;
        }

        if (_ghost is null)
        {
            if (SpawnKit is not { } kit || Human.Model is not { } dress)
            {
                return;
            }

            _ghost = Igi2NetworkHumanFactory.MakeGhost(
                this, kit, _surroundings, dress.Name, runner.Position.ToVector(), OpenIGI.Net.Quantize.AngleFromBits(runner.Yaw));
        }

        _ghost.PuppetTarget = runner;
        _ghost.RunDelayed(_ticks);
    }

    /// <inheritdoc/>
    public IReadOnlyList<TitleWorldModel> GhostModels =>
        _ghost is { Human.Model: { } dress } ghost
            ? Igi2BodyFrame.Build(
                ghost.Human, ghost.Human.PresentedPose(), dress.Name, ghost.Weapons?.Inventory.Current?.WeaponType.FixedModel, _ticks, ghost.Weapons?.Firing.Flashes)
            : [];
}
