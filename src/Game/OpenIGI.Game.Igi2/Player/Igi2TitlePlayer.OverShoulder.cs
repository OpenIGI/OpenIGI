using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// The player as the host's over-the-shoulder view watches it (<see cref="ITitleOverShoulder"/>). <b>Not retail</b>: the original
/// has no free third-person view of its own player; its other cameras -- a ladder's, a wire's, a cabinet's, the death camera
/// (<see cref="Humans.Igi2Human.CameraMode"/> 3 and 5) -- keep the view as they always did.
/// </content>
public sealed partial class Igi2TitlePlayer : ITitleOverShoulder
{
    // Whether the host's camera has left the head far enough for the body to be drawn, and the crosshair's ray it last gave.
    private bool _overShoulderBody;
    private (Vector3d EyeOffset, Vector3f Direction)? _overShoulderAim;

    /// <inheritdoc/>
    public bool AllowsOverShoulder =>
        ActingStandIn is null
        && Level?.CutsceneView is null
        && !IsComputerShown
        && !Human.IsDead
        && Human.CameraMode == 1
        && !Human.View.IsOverride;

    /// <inheritdoc/>
    /// <remarks>The binoculars, the thermal goggles, a scope or a zoom: any overlay the zoom has up, or a zoom fire mode.</remarks>
    public bool HasOpticUp =>
        Weapons is { Firing.OverlayStatus: not Igi2WeaponOverlayStatus.None } || Weapons?.IsZoomFireModeActive == true;

    /// <inheritdoc/>
    public void PresentOverShoulder(bool showsBody)
    {
        Human.IsOverShoulder = showsBody;
        if (showsBody == _overShoulderBody)
        {
            return;
        }

        // The body comes into the world's models, or goes, on the frame the camera clears the head, not on the next tick.
        _overShoulderBody = showsBody;
        _gatheredTick = long.MinValue;
        _lyingTick = -1;
        _outlineTick = -1;
    }

    // Whether the working body is drawn: the original's own cameras that watch it, or the remaster's over the shoulder -- and only
    // while the body is in the world. A cutscene's container takes the player out (its remove handler), and a player caught at a
    // winch or a ladder as the scene began stood frozen beside the actor that plays it.
    private bool IsBodyShown => Human.IsActive && (Human.CameraMode is 3 or 5 || _overShoulderBody);

    // The tick's crosshair ray, from its input: the eye it was measured from as the tick began, held to the human's own view.
    private void TakeTickAim(in TitlePlayerInput input) =>
        _overShoulderAim = input.Aim is { } aim ? (EyePosition + aim.EyeOffset - Human.View.Position, aim.Direction) : null;

    // Hands the tick's crosshair ray to the player before it runs, and the world it is traced through.
    private void TakeOverShoulderAim()
    {
        Human.AimWorld = CollisionWorld;
        Human.AimConvergence = _overShoulderAim;
    }
}
