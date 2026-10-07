using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>The view's limits while lying flat.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>What a lying player's view is kept out of: the ground, splines, solid objects and forests.</summary>
    private const uint ViewCollisionGroups =
        Igi2CollisionWorld.TerrainGroup | Igi2CollisionWorld.SplineGroup | Igi2RigidObject.RigidGroup | Igi2CollisionWorld.ForestGroup;

    /// <summary>How far ahead of the eyes the ground is looked for, and the run the view's slope is measured over.</summary>
    private const float ViewProbeReach = 2048f;

    private static readonly double[] ViewProbeDistances = [-4096.0, 4096.0];

    private readonly Igi2LineTrace _viewProbe = new(2) { Mesh = 1 };

    /// <summary>
    /// Keeps a lying player from looking into the ground or the roof: <c>HumanPlayer_CheckViewCollision</c>
    /// (<c>igi2 0x4BF340</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A point a reach ahead of the eyes along the body's forward axis is probed straight down and straight up, a
    /// reach of two each way. Ground below that faces up limits how far the view may look down: to the slope down to
    /// it over the reach. A ceiling above that faces down limits how far it may look up, to the slope up to it;
    /// ground above that faces up (a rise ahead) lifts the view to at least the slope up to it.
    /// </para>
    /// <para>
    /// Each limit that bites sets the aim, drops the kick, keeps the look input from pushing further the same way and
    /// aims the view again with what is left of it. Only a lying player is checked.
    /// </para>
    /// </remarks>
    public void CheckViewCollision()
    {
        if (Pose != Igi2HumanPose.LyingFlat)
        {
            return;
        }

        Igi2HumanView view = View;
        Matrix3f o = Orientation;
        float aheadX = ((Fpu)o.M12 * ViewProbeReach).F;
        float aheadZ = o.M32;
        Vector3d eyes = view.Position;
        Vector3d probe = new(
            ((Fpu)aheadX + eyes.X).Value,
            (((Fpu)o.M22 * ViewProbeReach) + eyes.Y).Value,
            (((Fpu)aheadZ * ViewProbeReach) + eyes.Z).Value);
        _viewProbe.Starts[0] = probe;
        _viewProbe.Starts[1] = probe;
        Surroundings.Collision.NextZ(_viewProbe, ViewProbeDistances, ViewCollisionGroups);

        Igi2Intersection below = _viewProbe.Intersections[0];
        if (below.IsHit && below.Normal.Z > 0f)
        {
            Fpu slope = -Fpu.Atan2(Rise(below, view), 1.0);
            if (slope.Value >= view.IdealPitch)
            {
                view.IdealPitch = slope.F;
                Channels[Igi2HumanChannel.LookY] = Channels[Igi2HumanChannel.LookY] >= 0f ? Channels[Igi2HumanChannel.LookY] : 0f;
                AimAgain(view);
            }
        }

        Igi2Intersection above = _viewProbe.Intersections[1];
        if (!above.IsHit)
        {
            return;
        }

        float rise = Fpu.Atan2(Rise(above, view), 1.0).F;
        if (above.Normal.Z < 0f && view.IdealPitch >= rise)
        {
            view.IdealPitch = rise;
            Channels[Igi2HumanChannel.LookY] = Channels[Igi2HumanChannel.LookY] <= 0f ? Channels[Igi2HumanChannel.LookY] : 0f;
            AimAgain(view);
        }
        else if (above.Normal.Z > 0f && view.IdealPitch < rise)
        {
            view.IdealPitch = view.IdealPitch <= rise ? rise : view.IdealPitch;
            Channels[Igi2HumanChannel.LookY] = Channels[Igi2HumanChannel.LookY] >= 0f ? Channels[Igi2HumanChannel.LookY] : 0f;
            AimAgain(view);
        }
    }

    /// <summary>The height between the eyes and a hit, over the probe's reach: <c>|Δz| / 2048</c>.</summary>
    private static Fpu Rise(Igi2Intersection hit, Igi2HumanView view) =>
        Fpu.Abs((Fpu)hit.Position.Z - view.Position.Z) * 0.00048828125;

    private void AimAgain(Igi2HumanView view)
    {
        view.Pitch = view.IdealPitch;
        view.PitchOffset = 0f;
        view.UpdateView(Channels);
    }
}
