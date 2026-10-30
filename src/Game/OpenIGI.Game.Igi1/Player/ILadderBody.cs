using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>The body a ladder climb plays its clips on and takes its root motion from.</summary>
/// <remarks>
/// The mission's own player climbs on the body the frame draws (<see cref="PresentedLadderBody"/>); a co-op partner, or any
/// player of a server with nothing drawn, climbs on a body of its own that only the simulation keeps
/// (<see cref="SimulatedLadderBody"/>). The climb itself is the same for both: <see cref="World.LadderClimb"/>.
/// </remarks>
public interface ILadderBody
{
    /// <summary>Starts a ladder clip.</summary>
    /// <param name="animation">The clip.</param>
    /// <param name="loops">Whether it repeats.</param>
    /// <param name="backwards">Whether it plays from its end.</param>
    void StartLadderClip(int animation, bool loops, bool backwards = false);

    /// <summary>Changes the clip's playback direction without restarting it.</summary>
    /// <param name="direction">−1 down, 0 paused at a boundary, or +1 up.</param>
    void SetLadderPlaybackDirection(int direction);

    /// <summary>Advances the clip a tick.</summary>
    /// <param name="facing">The ladder's facing, which turns the root motion into the world.</param>
    /// <param name="rootDelta">The clip's root step this tick, in world units.</param>
    /// <param name="eventTime">The crossed rung event's time, or <see langword="null"/>.</param>
    /// <param name="ended">Whether the clip ended this tick.</param>
    void AdvanceLadderClip(float facing, out Vector3d rootDelta, out float? eventTime, out bool ended);

    /// <summary>Stands the body on the ladder where the climb has it.</summary>
    /// <param name="feet">The climber's feet, in world units.</param>
    /// <param name="facing">The ladder's facing.</param>
    void PlaceClimbing(Vector3d feet, float facing);
}

/// <summary>The mission's own player's ladder body: the one the frame draws.</summary>
/// <param name="soldiers">The presenter that draws the player's body.</param>
public sealed class PresentedLadderBody(ISoldierPresenter soldiers) : ILadderBody
{
    private readonly ISoldierPresenter _soldiers = soldiers ?? throw new ArgumentNullException(nameof(soldiers));

    /// <inheritdoc/>
    public void StartLadderClip(int animation, bool loops, bool backwards = false) =>
        _soldiers.StartPlayerLadderAnimation(animation, loops, backwards);

    /// <inheritdoc/>
    public void SetLadderPlaybackDirection(int direction) => _soldiers.Simulation.SetPlayerLadderPlaybackDirection(direction);

    /// <inheritdoc/>
    public void AdvanceLadderClip(float facing, out Vector3d rootDelta, out float? eventTime, out bool ended) =>
        _soldiers.AdvancePlayerLadderAnimation(facing, out rootDelta, out eventTime, out ended);

    /// <inheritdoc/>
    public void PlaceClimbing(Vector3d feet, float facing) => _soldiers.PlacePlayerBodyClimbing(feet, facing);
}

/// <summary>A ladder body only the simulation keeps: a co-op partner's, posed on the player's own animation set.</summary>
public sealed class SimulatedLadderBody : ILadderBody
{
    private readonly SoldierView _view;

    /// <summary>Initializes a new instance of the <see cref="SimulatedLadderBody"/> class.</summary>
    /// <param name="animations">The player body's animation set, skeleton 0.</param>
    /// <remarks>No model: nothing draws it, and the root motion is the clips' own.</remarks>
    public SimulatedLadderBody(Formats.Animation.BoneAnimationSet animations)
    {
        ArgumentNullException.ThrowIfNull(animations);
        _view = new SoldierView(
            new HumanSoldierTask
            {
                BoneHierarchy = animations.Skeleton.Id,
                StandAnimation = HumanPlayerBody.StandAnimation,
            },
            animations);
    }

    /// <summary>Gets the clip it is playing.</summary>
    public int Animation => _view.CurrentAnimation;

    /// <summary>Gets where it is in that clip.</summary>
    public float ClipTime => _view.LadderClipTime;

    /// <inheritdoc/>
    public void StartLadderClip(int animation, bool loops, bool backwards = false) =>
        _view.StartLadderAnimation(animation, loops, backwards);

    /// <inheritdoc/>
    public void SetLadderPlaybackDirection(int direction) => _view.SetLadderPlaybackDirection(direction);

    /// <inheritdoc/>
    public void AdvanceLadderClip(float facing, out Vector3d rootDelta, out float? eventTime, out bool ended)
    {
        _view.Tick();
        Vector3f root = _view.RootMotion();
        rootDelta = SoldierDeathFall.RotateAboutZ(new Vector3d(root.X, root.Y, root.Z), facing);
        eventTime = _view.LadderBoundaryTime;
        ended = _view.ClipEndedThisTick;
    }

    /// <inheritdoc/>
    public void PlaceClimbing(Vector3d feet, float facing) => _view.PlaceAt(feet, Matrix3f.FromEngineEulerAngles(0f, 0f, facing));
}
