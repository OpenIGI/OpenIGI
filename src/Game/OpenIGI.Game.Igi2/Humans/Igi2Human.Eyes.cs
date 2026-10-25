using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Physics;

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

    /// <summary>How far ahead of the eyes the held object reaches, <c>2048</c>: what is nearer pulls it back.</summary>
    private const float ObjectReach = 2048f;

    /// <summary>How far a held object comes back out each tick once nothing is in front of it, <c>0x4297B426</c>.</summary>
    private const float RetractRelease = 75.851852f;

    /// <summary>How far a held object moves each tick toward a wall's pull, <c>0x42E38E39</c>.</summary>
    private const float RetractFollow = 113.77778f;

    /// <summary>The share of a landing's dip the held object takes on top of the eyes', <c>0.075</c> as a double.</summary>
    private const double ObjectImpactShare = 0.075;

    private readonly WorldLineTrace _eyeTrace = new(1) { Mesh = FootingMesh, Includes = static owner => !IsSweptThrough(owner) };

    private readonly WorldLineTrace _objectTrace = new(1) { Mesh = 0, Includes = static owner => !IsSweptThrough(owner) };

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
    /// With the first-person model registered, a line 2048 ahead along the view finds how far the held object is to be
    /// pulled back from what is in front of it, and the pull steps toward that: 113.8 a tick while something is there,
    /// 75.9 a tick back out once nothing is. The model stands at the eyes plus that pull along the view and its share
    /// of a landing's dip, turned from the view by the object's own pitch and turn; and, asked to, its clip steps by
    /// its step.
    /// </para>
    /// </remarks>
    /// <param name="updateAnimation">Whether the first-person clip steps, <c>isUpdateAnim</c>: the tick's update does, placing the view alone does not.</param>
    public void UpdateViewBody(bool updateAnimation = true)
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

        float[] o = EngineEuler.Rotation(alpha, beta, gamma);
        view.Orientation = new Matrix3f(o[0], o[1], o[2], o[3], o[4], o[5], o[6], o[7], o[8]);
        Vector3d eyes = EyesAt(Position, Pose);

        Fpu leanX = view.PeekOffset.X, leanY = view.PeekOffset.Y, leanZ = view.PeekOffset.Z;
        float objectDip = 0f;
        if (view.ShakeFactor != 0f)
        {
            leanX += Shake(view.ShakeRange, view.ShakeFactor);
            leanZ += Shake(view.ShakeRange, view.ShakeFactor);
            RunDownShake(view);
        }

        if (view.ImpactOffset != 0f)
        {
            double sine = Math.Sin(view.ImpactAngle);
            Fpu dip = (Fpu)view.ImpactOffset * sine;
            leanZ += dip;
            objectDip = (((Fpu)view.ImpactOffset * ObjectImpactShare) * sine).F;
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
        Vector3f objectOffset = new(0f, 0f, objectDip);
        if (view.IsObjectRegistered)
        {
            objectOffset = Retract(view, eyes, o, objectDip);
        }

        view.StepObject();
        if (!view.IsObjectRegistered)
        {
            return;
        }

        float[] turn = EngineEuler.Rotation(view.ObjectPitch, 0f, view.ObjectYaw);
        Matrix3f placed = ObjectFrame(o, turn);
        if (updateAnimation)
        {
            Igi2AnimController arms = view.ObjectAnimation;
            arms.Step(arms.Time + arms.TimeStep);
        }

        view.PlaceObject(
            new Vector3d(
                ((Fpu)objectOffset.X + eyes.X).Value,
                ((Fpu)objectOffset.Y + eyes.Y).Value,
                ((Fpu)objectOffset.Z + eyes.Z).Value),
            placed);
    }

    /// <summary>
    /// Pulls the held object back from what is in front of it, and gives its offset from the eyes
    /// (<c>HumanView_UpdateBody</c>, <c>igi2 0x4D6A90</c>).
    /// </summary>
    /// <remarks>
    /// The line is traced against every object's first collision mesh, which the eyes' own lean does not use, with the
    /// same objects let through.
    /// </remarks>
    private Vector3f Retract(Igi2HumanView view, Vector3d eyes, float[] o, float objectDip)
    {
        Fpu aheadX = ((((Fpu)o[0] + o[2]) * 0.0) + ((Fpu)o[1] * ObjectReach));
        Fpu aheadY = ((((Fpu)o[5] + o[3]) * 0.0) + ((Fpu)o[4] * ObjectReach));
        Fpu aheadZ = ((((Fpu)o[8] + o[6]) * 0.0) + ((Fpu)o[7] * ObjectReach));
        _objectTrace.Starts[0] = eyes;
        _objectTrace.Ends[0] = new Vector3d((aheadX + eyes.X).Value, (aheadY + eyes.Y).Value, (aheadZ + eyes.Z).Value);
        _objectTrace.Evaluator = this;
        Surroundings.Collision.TraceLines(_objectTrace, AnyGroup);
        WorldIntersection hit = _objectTrace.Intersections[0];
        view.RetractIdeal = hit.IsHit ? (-((Fpu)(double)ObjectReach - hit.Distance)).F : 0f;

        float step = view.RetractIdeal == 0f ? RetractRelease : RetractFollow;
        if (view.RetractReal < view.RetractIdeal)
        {
            float raised = ((Fpu)view.RetractReal + step).F;
            view.RetractReal = raised > view.RetractIdeal ? view.RetractIdeal : raised;
        }
        else if (view.RetractReal > view.RetractIdeal)
        {
            float lowered = ((Fpu)view.RetractReal - step).F;
            view.RetractReal = lowered < view.RetractIdeal ? view.RetractIdeal : lowered;
        }

        Fpu pull = view.RetractReal;
        float x = ((((Fpu)o[0] + o[2]) * 0.0) + (pull * o[1])).F;
        float y = ((((Fpu)o[5] + o[3]) * 0.0) + (pull * o[4])).F;
        float z = ((((Fpu)o[8] + o[6]) * 0.0) + (pull * o[7])).F;
        return new Vector3f(x, y, ((Fpu)z + objectDip).F);
    }

    /// <summary>The view's orientation times the object's own turn, each cell summed third term, second, first.</summary>
    /// <remarks>
    /// The original's rows after the first sum their terms in other orders; the model's orientation is drawn and feeds
    /// nothing the game decides, so the first row's order serves for all nine.
    /// </remarks>
    private static Matrix3f ObjectFrame(float[] o, float[] n)
    {
        float Cell(int row, int column) =>
            ((((Fpu)n[6 + column] * o[(row * 3) + 2]) + ((Fpu)n[3 + column] * o[(row * 3) + 1])) + ((Fpu)n[column] * o[row * 3])).F;

        return new Matrix3f(
            Cell(0, 0), Cell(0, 1), Cell(0, 2),
            Cell(1, 0), Cell(1, 1), Cell(1, 2),
            Cell(2, 0), Cell(2, 1), Cell(2, 2));
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
        _eyeTrace.Evaluator = this;
        Surroundings.Collision.TraceLines(_eyeTrace, AnyGroup);
        WorldIntersection hit = _eyeTrace.Intersections[0];
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
