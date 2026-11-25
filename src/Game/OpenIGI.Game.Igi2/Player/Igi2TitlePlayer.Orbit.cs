using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Player;

/// <content>
/// The orbit round the player's working body (camera mode 3: a ladder, a wire, a timed action) and its dead one (mode 5): the
/// first game's (<see cref="HumanOrbitCamera"/>), turned on the tick by the tick's own mouse, which a recording keeps, and
/// drawn between ticks by the host with the mouse still to come (<see cref="ITitleOrbitView"/>). <b>Not retail</b>: the
/// original's own orbit (<c>HumanCamera_UpdateAlphaAndGammaWithCollisions</c>) searches for room on entering (<c>igi2 0x4DE380</c>),
/// pushes off whatever is near (<c>0x4DE830</c>) and turns at most six degrees a poll.
/// </content>
public sealed partial class Igi2TitlePlayer : ITitleOrbitView
{
    private readonly HumanOrbitCamera _orbit = new();

    // The camera mode the orbit was taken for, or 0 while it does not have the view.
    private int _orbitMode;

    private WorldRayCast? _orbitWalls;

    /// <inheritdoc/>
    /// <remarks>
    /// As <see cref="EyePosition"/> ranks it: a cutscene's camera and the map outrank it, and another game's player standing in
    /// has its own.
    /// </remarks>
    public bool IsOrbiting => Level?.CutsceneView is null && !IsMapShown
        && (ActingStandIn is null ? IsOrbitView : ActingStandIn is ITitleOrbitView { IsOrbiting: true });

    // Whether the orbit stands in for the player's eyes, the camera modes that watch the body.
    private bool IsOrbitView => _orbit.IsActive && Human.CameraMode is 3 or 5;

    // What the orbit keeps off: the level's collision as the player's eye sees it, without the player's own body.
    private WorldRayCast OrbitWalls => _orbitWalls ??= EyeWorld.Cast;

    /// <inheritdoc/>
    public Camera3D PreviewOrbit(float tickBlend, float mouseX, float mouseY, float sensitivity, bool invertMouse) =>
        ActingStandIn is ITitleOrbitView standIn
            ? standIn.PreviewOrbit(tickBlend, mouseX, mouseY, sensitivity, invertMouse)
            : _orbit.Preview(tickBlend, mouseX, mouseY, MouseLookSettings.FromSensitivity(sensitivity, invertMouse));

    // The orbit's tick, as the level's has left the body: taken as a mode that watches the body begins, given back as it ends.
    private void RunOrbit(in TitlePlayerInput input)
    {
        int mode = Human.CameraMode;
        if (mode is not (3 or 5))
        {
            _orbitMode = 0;
            _orbit.Reset();
            return;
        }

        // The first game's orbit looks at the standing eyes over the body's place, which a climb carries smoothly, not at
        // the root, which rocks with the animation; its eight hidden-body points stand round the root, the body's middle.
        Vector3d look = Igi2Human.EyesAt(Human.Position, Igi2HumanPose.Standing);
        Vector3d middle = Human.HeldRootPosition();
        if (mode != _orbitMode)
        {
            _orbitMode = mode;
            Matrix3f facing = Human.Orientation;
            _orbit.Begin(look, middle, (float)SimMath.Atan2(facing.M12, facing.M22), OrbitWalls, death: mode == 5);
        }

        _orbit.LookSettings = MouseLookSettings.FromSensitivity(input.MouseSensitivity, input.InvertMouse);
        _orbit.Control(input.MouseX, input.MouseY);
        _orbit.Tick(look, middle);
    }
}
