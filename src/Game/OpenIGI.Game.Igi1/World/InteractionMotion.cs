using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Igi1.World;

/// <summary>One player's world interaction on the body it plays with: the state's own clip, stepped a tick at a time.</summary>
/// <remarks>
/// A computer, a wire, a vault: the clip the interaction's state chose is started as it begins, and each tick its root step
/// moves the player as the state allows. The mounted gun's sight plays none. The mission's own player and every co-op
/// partner interact through this one machine, as they climb through <see cref="LadderClimb"/>.
/// </remarks>
public sealed class InteractionMotion
{
    /// <summary>Gets a value indicating whether an interaction is under way.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Forgets the interaction, as the player leaves it or the level goes.</summary>
    public void Reset() => IsActive = false;

    /// <summary>Runs the interaction a tick.</summary>
    /// <param name="player">The player, in a world interaction.</param>
    /// <param name="body">The body it plays the clip on, or <see langword="null"/> when there is none to pose.</param>
    /// <param name="dragSound">Sounds a wire drag's step at where it was, or <see langword="null"/> for none heard.</param>
    public void Tick(PlayerCamera player, ILadderBody? body, Action<string, Vector3d>? dragSound = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!IsActive)
        {
            IsActive = true;
            if (player.InteractionAnimation >= 0)
            {
                body?.StartInteractionClip(player.InteractionAnimation, player.InteractionAnimationLoops);
            }
        }

        if (player.IsMountedStationaryGun || body is null)
        {
            return;
        }

        // State 42 plays its drag clip only while a step is under way (0x40DDA0).
        if (player.IsOnLevelWire)
        {
            body.SetLadderPlaybackDirection(player.WireDragDirection);
            if (player.TakeWireDragSound() is { } sound)
            {
                dragSound?.Invoke(sound, player.InteractionFeet);
            }
        }

        body.AdvanceLadderClip(player.InteractionFacing, out Vector3d rootDelta, out float? stepTime, out bool clipEnded);
        player.ApplyInteractionRootMotion(rootDelta, clipEnded, stepTime);
        body.PlaceClimbing(player.InteractionFeet, player.InteractionFacing);
    }
}
