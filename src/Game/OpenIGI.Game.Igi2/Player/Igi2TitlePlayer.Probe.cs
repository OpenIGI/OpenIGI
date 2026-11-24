using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Player;

/// <content>A debugging tool's aim and placing (<see cref="Game.Multiplayer.NetProbe"/>). <b>Temporary, not retail.</b></content>
public sealed partial class Igi2TitlePlayer
{
    /// <summary>Turns the player to look at a point: its heading and its view's pitch, as the mouse would have turned them.</summary>
    /// <param name="target">The point, in world units.</param>
    /// <returns>Whether it could be turned.</returns>
    public bool AimAt(Vector3d target)
    {
        if (Human.IsDead || IsCutscenePlaying)
        {
            return false;
        }

        Vector3d d = target - Human.View.Position;
        double flat = Math.Sqrt((d.X * d.X) + (d.Y * d.Y));
        if (flat < 1e-3)
        {
            return false;
        }

        float gamma = GammaToward(Human.View.Position, target);

        // Up positive, as the view's own (HumanView_UpdateView).
        float pitch = Math.Clamp((float)SimMath.Atan2(d.Z, flat), -Igi2HumanView.PitchLimit, Igi2HumanView.PitchLimit);
        Human.Gamma = gamma;
        Human.IdealGamma = gamma;
        Human.View.IdealPitch = pitch;
        Human.View.Pitch = pitch;
        ResetLook();
        return true;
    }

    /// <summary>Gets the heading a human at one point faces another along.</summary>
    /// <param name="from">Where it stands.</param>
    /// <param name="to">What it faces.</param>
    /// <returns>The heading, in the sense the human's <see cref="Igi2Human.Gamma"/> takes.</returns>
    /// <remarks>Read off the pose the heading builds (<see cref="Igi2Pose.FromAngles"/>), whose forward is its second column.</remarks>
    public static float GammaToward(Vector3d from, Vector3d to)
    {
        double want = SimMath.Atan2(to.Y - from.Y, to.X - from.X);
        double zero = ForwardAngle(0f);
        double turn = Wrap(ForwardAngle(0.5f) - zero) >= 0 ? 1 : -1;
        return (float)Wrap((want - zero) * turn);
    }

    private static double ForwardAngle(float gamma)
    {
        Matrix3f m = Igi2Pose.FromAngles(default, new Vector3f(0f, 0f, gamma)).ToMatrix();
        return SimMath.Atan2(m.M22, m.M12);
    }

    private static double Wrap(double angle) => Math.IEEERemainder(angle, Math.Tau);
}
