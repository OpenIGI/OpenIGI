using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;

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
    /// Keeps a lying player from looking into the ground or the roof: <c>humanplayer.c HumanPlayer_CheckViewCollision</c>
    /// (<c>igi2 0x4BF340</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A point a reach ahead of the eyes along the body's forward axis is probed straight down and straight up, a
    /// reach of two each way. Each limit is the angle (<c>Real32_Acos</c>) between the body's forward axis and the line
    /// from the eyes to the surface struck. Ground below that faces up limits how far the view may look down, to minus
    /// that angle. A ceiling above that faces down limits how far it may look up, to the angle; ground above that faces up
    /// (a rise ahead) lifts the view to at least the angle.
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
        float aheadY = ((Fpu)o.M22 * ViewProbeReach).F;
        float aheadZ = ((Fpu)o.M32 * ViewProbeReach).F;
        Vector3d eyes = view.Position;
        Vector3d probe = new(((Fpu)aheadX + eyes.X).Value, ((Fpu)aheadY + eyes.Y).Value, ((Fpu)aheadZ + eyes.Z).Value);
        _viewProbe.Starts[0] = probe;
        _viewProbe.Starts[1] = probe;
        Surroundings.Collision.NextZ(_viewProbe, ViewProbeDistances, ViewCollisionGroups);

        if (aheadX != 0f || aheadY != 0f || aheadZ != 0f)
        {
            Fpu inverse = (Fpu)1f / Fpu.Sqrt(((Fpu)aheadX * aheadX) + ((Fpu)aheadY * aheadY) + ((Fpu)aheadZ * aheadZ));
            aheadX = ((Fpu)aheadX * inverse).F;
            aheadY = ((Fpu)aheadY * inverse).F;
            aheadZ = ((Fpu)aheadZ * inverse).F;
        }

        Vector3f ahead = new(aheadX, aheadY, aheadZ);
        Igi2Intersection below = _viewProbe.Intersections[0];
        if (below.IsHit && below.Normal.Z > 0f)
        {
            float slope = -Igi2Pose.Acos(Cosine(below, view, ahead));
            if (slope >= view.IdealPitch)
            {
                view.IdealPitch = slope;
                Channels[Igi2HumanChannel.LookY] = Channels[Igi2HumanChannel.LookY] >= 0f ? Channels[Igi2HumanChannel.LookY] : 0f;
                AimAgain(view);
            }
        }

        Igi2Intersection above = _viewProbe.Intersections[1];
        if (!above.IsHit)
        {
            return;
        }

        float rise = Igi2Pose.Acos(Cosine(above, view, ahead));
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

    /// <summary>The cosine between the body's forward axis and the line from the eyes to a hit, both made unit first.</summary>
    private static float Cosine(Igi2Intersection hit, Igi2HumanView view, Vector3f ahead)
    {
        Vector3d eyes = view.Position;
        Fpu x = (Fpu)hit.Position.X - eyes.X;
        Fpu y = (Fpu)hit.Position.Y - eyes.Y;
        Fpu z = (Fpu)hit.Position.Z - eyes.Z;
        if (x.Value != 0.0 || y.Value != 0.0 || z.Value != 0.0)
        {
            float length = Fpu.Sqrt((x * x) + (y * y) + (z * z)).F;
            Fpu inverse = (Fpu)1f / length;
            x *= inverse;
            y *= inverse;
            z *= inverse;
        }

        return ((z * ahead.Z) + (y * ahead.Y) + (x * ahead.X)).F;
    }

    private void AimAgain(Igi2HumanView view)
    {
        view.Pitch = view.IdealPitch;
        view.PitchOffset = 0f;
        view.UpdateView(Channels, UnfilteredMouse);
    }
}
