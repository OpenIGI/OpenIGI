using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>A whole-body clip a player plays -- a ladder's, a computer's, a lock's, a vault's -- as another player's window poses it.</summary>
/// <param name="Animation">The clip.</param>
/// <param name="Feet">Where the body stands, in world units: the place the clip is posed at.</param>
/// <param name="Facing">Which way the clip faces the body, a gamma (the look's yaw negated).</param>
/// <param name="Ladder">
/// Whether it is a ladder's clip, posed on the ladder with the clip's root height already in <paramref name="Feet"/>; any
/// other is posed as the player's own body poses that interaction's (<see cref="PlayerCamera.KeepsRootHeight"/>).
/// </param>
public readonly record struct PlayerBodyClip(int Animation, Vector3d Feet, float Facing, bool Ladder);

public sealed partial class PlayerCamera
{
    /// <summary>Gets whether the body plays a world interaction's own clip: any interaction but the mounted gun's sight.</summary>
    public bool PlaysInteractionClip => IsInWorldInteraction && !IsMountedStationaryGun && InteractionAnimation >= 0;

    /// <summary>Gets the ladder clip the climb is in, or -1 off a ladder: what another player's window draws it doing.</summary>
    public int LadderAnimation => !IsOnLadder
        ? -1
        : LadderPhase switch
        {
            LadderTraversalPhase.SlidingDown => LadderSlideAnimation,
            LadderTraversalPhase.GettingOnTop or LadderTraversalPhase.GettingOffTop => LadderTopAnimation,
            _ => LadderClimbAnimation,
        };

    /// <summary>
    /// Gets the whole-body clip the player plays, or <see langword="null"/> while it walks: a ladder's, else a world
    /// interaction's. What every other player's window draws it doing, the server's snapshot and a host's own partner alike.
    /// </summary>
    public PlayerBodyClip? BodyClip => LadderAnimation >= 0
        ? new PlayerBodyClip(LadderAnimation, LadderFeet, LadderFacing, Ladder: true)
        : PlaysInteractionClip
            ? new PlayerBodyClip(InteractionAnimation, InteractionFeet, InteractionFacing, Ladder: false)
            : null;

    /// <summary>Gets whether a clip is one of the ladder's three: the climb, the slide, the top.</summary>
    /// <param name="animation">The clip.</param>
    /// <returns><see langword="true"/> for 168, 169 and 170 in the player's set.</returns>
    public static bool IsLadderAnimation(int animation) =>
        animation == LadderClimbAnimation || animation == LadderSlideAnimation || animation == LadderTopAnimation;

    /// <summary>Gets whether an interaction clip lifts the body by its root height, as the walk's do.</summary>
    /// <param name="animation">The interaction's clip.</param>
    /// <returns>
    /// <see langword="true"/> for the computer, cabinet, lock and charge clips, whose task position is the feet;
    /// <see langword="false"/> for the fence's vault and the wires' rides, whose root motion carries the height already.
    /// </returns>
    public static bool KeepsRootHeight(int animation) =>
        animation != HumanLocomotionStates.For(41).AnimationId && !Ai.SoldierSimulation.IsWireAnimation(animation);
}
