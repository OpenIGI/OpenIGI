using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ai;
using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Physics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Game;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using System.Runtime.InteropServices;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The player's own third-person body where it is a soldier's: whether it is shown, followed or climbing, the clip a
/// ladder, a keypad or a vault plays on it, and the rounds aimed at the player that are scored on its mesh.
/// </summary>
/// <remarks>
/// What draws it is the presentation's; this holds the state the follow and the traversal clips agree on, so the two can
/// hand the rig back and forth without either leaving an override behind.
/// </remarks>
internal sealed class SoldierPlayerBody
{
    /// <summary>Gets or sets the player's body, posed by the follow and the traversal clips.</summary>
    public SoldierView? View { get; set; }

    /// <summary>Gets or sets the player's third-person body's model and collision mesh, which rounds aimed at the player are scored on.</summary>
    public SoldierBodyModel? Model { get; set; }

    /// <summary>Gets or sets whether the body is being shown climbing a ladder.</summary>
    public bool Climbing { get; set; }

    /// <summary>Gets or sets whether the standing body is drawn.</summary>
    public bool Shown { get; set; }

    /// <summary>Gets or sets whether the third-person view is placing and animating the body.</summary>
    public bool Following { get; set; }

    /// <summary>Gets or sets the torso request the followed body last acted on.</summary>
    // The torso request the followed body last acted on. -1 is "none seen": PlayerCamera's counter
    // starts at zero and only ever climbs, so this forces a re-issue the first time the follow takes
    // the body and every time it takes it back off a ladder, a keypad or a death.
    public int UpperBodySequence { get; set; } = -1;

    /// <summary>Gets or sets whether the traversal clip's root height is kept out of the drawn body.</summary>
    public bool SuppressRootHeight { get; set; }

    /// <summary>Gets or sets how far the traversal clip's body is lifted where it is drawn.</summary>
    public double VisualLift { get; set; }

    /// <summary>Gets or sets the clip's root height as the traversal started.</summary>
    public double RootHeightAtStart { get; set; }

    /// <summary>Whether an animation is one of the wire-cutting clips.</summary>
    public static bool IsWireAnimation(int animation) =>
        animation == HumanLocomotionStates.For(42).AnimationId
        || animation == HumanLocomotionStates.For(43).AnimationId;

    /// <summary>Stops following the player — the view has gone back inside their head.</summary>
    /// <remarks>
    /// <para>
    /// <b>And gives the rig back.</b> The torso clip on bone 1 and the aim bend belong to the follow and
    /// to nothing else: while the player is on a ladder, at a keypad, cutting a wire or dead, an authored
    /// full-body clip owns the whole character, and a weapon stance left in its shoulders replaces the
    /// half of that clip above the waist. <see cref="SoldierView.StartLadderAnimation"/> and
    /// <see cref="SoldierView.Kill"/> release it too, so no ordering between this and them can leave the
    /// override behind — but doing it here as well is what makes the invariant a property of the follow
    /// rather than of a list of call sites.
    /// </para>
    /// <para>
    /// Cheap to repeat: this is called on every tick the follow is not running, and both releases return
    /// at once when there is nothing to release.
    /// </para>
    /// </remarks>
    public void HideFollowing()
    {
        if (!Following)
        {
            return;
        }

        Following = false;
        UpperBodySequence = -1;
        View?.ReleaseUpperBody();
    }

    /// <summary>Starts a traversal clip -- a ladder, a fence, a keypad -- on the player's body.</summary>
    public void StartTraversalAnimation(
        SoldierView view,
        int animation,
        bool loops,
        bool backwards,
        bool paused,
        bool suppressRootHeight,
        double visualLift)
    {
        Climbing = true;
        SuppressRootHeight = suppressRootHeight;
        VisualLift = visualLift;
        view.StartLadderAnimation(animation, loops, backwards, paused);
        RootHeightAtStart = view.RootSample.Z;
    }

    /// <summary>Gets the interaction task position adjusted by the clip's visible root-height change.</summary>
    /// <remarks>
    /// The body renderer adds the current root Z above the task, while the third-person camera normally
    /// follows the task itself. Following the delta from the clip's entry height keeps an airborne vault
    /// framed without treating the standing pelvis height as an extra metre of world elevation.
    /// </remarks>
    public Vector3d InteractionCameraFeet(Vector3d taskPosition)
    {
        if (View is not { } view || SuppressRootHeight)
        {
            return taskPosition;
        }

        return new Vector3d(
            taskPosition.X,
            taskPosition.Y,
            taskPosition.Z + view.RootSample.Z - RootHeightAtStart + VisualLift);
    }

    /// <summary>Stops showing the climbing body — the player has left the ladder.</summary>
    public void HideClimbing()
    {
        Climbing = false;
        VisualLift = 0.0;
        RootHeightAtStart = 0.0;
    }

    /// <summary>Scores a guard's round on the <b>player's own body</b> — its skinned collision mesh.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="playerFeetUnits">The player's feet, in world units.</param>
    /// <param name="playerYaw">Which way the player faces, in radians.</param>
    /// <param name="hit">The contact, when there is one.</param>
    /// <returns><see langword="false"/> when the round misses the player's body, or no body is loaded.</returns>
    /// <remarks>
    /// The original resolves a shot at the player exactly as at a soldier — against the human's skinned
    /// collision mesh — so a round through a limb scores that limb (×0.5–0.75), not the torso. The port
    /// had cast at a 0.55 m cylinder and scored every hit as the trunk, which made the player both a
    /// fatter target and one that took maximum damage from every round. This poses the player's own
    /// (otherwise idle) third-person body at the live position and heading and traces the real mesh.
    /// </remarks>
    public bool TryHitPlayerBody(
        Vector3d origin, Vector3d end, Vector3d playerFeetUnits, float playerYaw, out HumanHit hit)
    {
        hit = default;
        if (View is not { } view || Model?.CollisionMesh is not { } mesh)
        {
            return false;
        }

        view.PlaceAt(playerFeetUnits, Matrix3f.FromEngineEulerAngles(0f, 0f, playerYaw));
        Matrix3f m = view.Orientation;
        Vector3f facing = new(m.M12, m.M22, m.M32);
        return HumanHitTest.TryCastMesh(
            origin, end, playerFeetUnits, facing, mesh, view.Pose(), view.RootSample.Z, out hit);
    }

    /// <summary>Every face of the player's own body a round's line crosses, nearest first.</summary>
    /// <param name="origin">Where the round starts, in world units.</param>
    /// <param name="end">Where it stops.</param>
    /// <param name="playerFeetUnits">The player's feet, in world units.</param>
    /// <param name="playerYaw">Which way the player faces, in radians.</param>
    /// <param name="crossings">The crossings, entries and exits, in order along the ray.</param>
    /// <returns><see langword="false"/> when no body is loaded; an empty list when the round misses it.</returns>
    /// <remarks>
    /// <see cref="TryHitPlayerBody"/>'s cast, carried on through the body: a round that penetrates it meets its
    /// exit, and may meet another part behind (<c>0x47B750</c>).
    /// </remarks>
    public bool TryCastPlayerBody(
        Vector3d origin,
        Vector3d end,
        Vector3d playerFeetUnits,
        float playerYaw,
        out IReadOnlyList<HumanBodyCrossing> crossings)
    {
        crossings = [];
        if (View is not { } view || Model?.CollisionMesh is not { } mesh)
        {
            return false;
        }

        view.PlaceAt(playerFeetUnits, Matrix3f.FromEngineEulerAngles(0f, 0f, playerYaw));
        Matrix3f m = view.Orientation;
        Vector3f facing = new(m.M12, m.M22, m.M32);
        crossings = HumanHitTest.CastMeshCrossings(
            origin, end, playerFeetUnits, facing, mesh, view.Pose(), view.RootSample.Z);
        return true;
    }
}
