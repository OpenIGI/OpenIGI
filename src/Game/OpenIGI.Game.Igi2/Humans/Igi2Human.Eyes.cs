using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>The eyes: where the view sits on the body, and which way it looks.</summary>
public abstract partial class Igi2Human
{
    /// <summary>Every query group: the view's lean is stopped by anything, the ground included.</summary>
    private const uint AnyGroup = 0xFFFFFFFF;

    /// <summary>How far short of a wall a leaning view stops, <c>204.8</c> as a double.</summary>
    private const double ViewWallClearance = 204.8;

    /// <summary>How much further a landing's dip swings each tick, <c>0x3E5676A6</c>: half a turn in fifteen ticks.</summary>
    private const float ImpactSwing = 0.20943952f;

    private const float HalfTurn = 3.1415927f;

    private readonly Igi2LineTrace _eyeTrace = new(1) { Mesh = FootingMesh, Includes = static owner => !IsSweptThrough(owner) };

    /// <summary>
    /// Places the view on the body: <c>HumanView_UpdateBody</c> (<c>igi2 0x4D6510</c>), for the view alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The view looks along the body's pitch plus its own and the body's heading plus its own sideways turn. A shaking
    /// view takes three draws from the run stream for a random pitch, roll and turn, each up to the shake's angle times
    /// what is left of the shake.
    /// </para>
    /// <para>
    /// The eyes are above the feet by the posture's height (<c>HumanView_GetOriginalViewPosition</c>,
    /// <c>igi2 0x4D7150</c>): 6922.24 standing, 4096 crouched, 1228.8 lying. A lean moves them along the view's own
    /// axes; a shake moves them sideways and up by two more draws, up to its range, and then runs down by its time.
    /// A landing dips them by its offset times the sine of an angle that swings on a fifteenth of a half turn each tick
    /// until the dip is over. When the eyes are moved, a line from where they would be to where they are put stops
    /// them 204.8 short of whatever it meets.
    /// </para>
    /// <para>
    /// Not ported yet: the held object's retraction and the first-person model's placement, which follow the view
    /// when an object is registered with it.
    /// </para>
    /// </remarks>
    public void UpdateViewBody()
    {
        Igi2HumanView view = View;
        float alpha = ((Fpu)Alpha + view.Pitch).F;
        float beta = 0f;
        float gamma = ((Fpu)Gamma + view.Yaw).F;
        if (view.ShakeFactor != 0f)
        {
            alpha = (Shake(view.ShakeAngle, view.ShakeFactor) + alpha).F;
            beta = Shake(view.ShakeAngle, view.ShakeFactor).F;
            gamma = (Shake(view.ShakeAngle, view.ShakeFactor) + gamma).F;
        }

        float[] o = Igi2Pose.Rotation(alpha, beta, gamma);
        view.Orientation = new Matrix3f(o[0], o[1], o[2], o[3], o[4], o[5], o[6], o[7], o[8]);
        Vector3d eyes = EyesAt(Position, Pose);

        Fpu leanX = view.PeekOffset.X, leanY = view.PeekOffset.Y, leanZ = view.PeekOffset.Z;
        if (view.ShakeFactor != 0f)
        {
            leanX += Shake(view.ShakeRange, view.ShakeFactor);
            leanZ += Shake(view.ShakeRange, view.ShakeFactor);
            RunDownShake(view);
        }

        if (view.ImpactOffset != 0f)
        {
            Fpu dip = (Fpu)view.ImpactOffset * Math.Sin(view.ImpactAngle);
            leanZ += dip;
            Fpu swing = (Fpu)view.ImpactAngle + ImpactSwing;
            view.ImpactAngle = swing.F;
            if (swing.Value >= HalfTurn)
            {
                view.ImpactOffset = 0f;
            }
        }

        if (leanX.Value != 0.0 || leanY.Value != 0.0 || leanZ.Value != 0.0)
        {
            eyes = Lean(eyes, o, leanX, leanY, leanZ.F);
        }

        view.Position = eyes;
        view.StepObject();
    }

    /// <summary>Where the eyes are above the feet in a posture, <c>HumanView_GetOriginalViewPosition</c> (<c>igi2 0x4D7150</c>).</summary>
    /// <param name="feet">The body's position.</param>
    /// <param name="pose">The posture.</param>
    /// <returns>The eyes, straight above the feet.</returns>
    public static Vector3d EyesAt(Vector3d feet, Igi2HumanPose pose)
    {
        float height = pose switch
        {
            Igi2HumanPose.Crouching => 4096f,
            Igi2HumanPose.LyingFlat => 1228.8f,
            _ => 6922.24f,
        };
        return new Vector3d(feet.X, feet.Y, ((Fpu)feet.Z + height).Value);
    }

    /// <summary>One shake draw: a value in [-1, 1) from the run stream, times a size and the shake left.</summary>
    private Fpu Shake(float size, float factor)
    {
        Fpu draw = Random.NextDouble();
        return ((((draw - 0.5) + draw) - 0.5) * size) * factor;
    }

    /// <summary>Runs a shake down toward zero by its time, stopping at zero.</summary>
    private static void RunDownShake(Igi2HumanView view)
    {
        if (view.ShakeFactor < 0f)
        {
            Fpu raised = (Fpu)view.ShakeTime + view.ShakeFactor;
            view.ShakeFactor = raised.Value > 0.0 ? 0f : raised.F;
        }
        else if (view.ShakeFactor > 0f)
        {
            Fpu lowered = (Fpu)view.ShakeFactor - view.ShakeTime;
            view.ShakeFactor = lowered.Value < 0.0 ? 0f : lowered.F;
        }
    }

    /// <summary>Moves the eyes by a lean in the view's frame, stopping them short of whatever is in the way.</summary>
    private Vector3d Lean(Vector3d eyes, float[] o, Fpu leanX, Fpu leanY, float leanZ)
    {
        float acrossX = ((leanY * o[1]) + (leanX * o[0]) + ((Fpu)o[2] * 0.0)).F;
        Fpu acrossY = (leanY * o[4]) + (leanX * o[3]) + ((Fpu)o[5] * 0.0);
        Vector3d leaned = new(
            ((Fpu)acrossX + eyes.X).Value,
            (acrossY + eyes.Y).Value,
            ((Fpu)leanZ + eyes.Z).Value);

        _eyeTrace.Starts[0] = eyes;
        _eyeTrace.Ends[0] = leaned;
        Surroundings.Collision.TraceLines(_eyeTrace, AnyGroup);
        Igi2Intersection hit = _eyeTrace.Intersections[0];
        if (!hit.IsHit)
        {
            return leaned;
        }

        Fpu dx = (Fpu)hit.Position.X - eyes.X;
        Fpu dy = (Fpu)hit.Position.Y - eyes.Y;
        Fpu dz = (Fpu)hit.Position.Z - eyes.Z;
        Fpu length = Fpu.Sqrt((dy * dy) + (dz * dz) + (dx * dx));
        Fpu inverse = (Fpu)1.0 / length;
        Fpu back = length - ViewWallClearance;
        if (back.Value < 0.0)
        {
            back = 0.0;
        }

        return new Vector3d(
            (((inverse * dx) * back) + eyes.X).Value,
            (((dy * inverse) * back) + eyes.Y).Value,
            (((inverse * dz) * back) + eyes.Z).Value);
    }
}
