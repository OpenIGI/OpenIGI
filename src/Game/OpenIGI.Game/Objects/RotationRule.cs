using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Objects;

/// <summary>
/// How a rotating object turns, as both games run it (Project I.G.I. <c>0x439090</c>, I.G.I. 2
/// <c>RotatingObject_RunHandler</c>): each axis's speed steps toward its target by its acceleration over a tick, each angle
/// gains its speed over a tick and is wrapped from above, and the object faces its placed orientation turned by the three.
/// </summary>
/// <remarks>
/// Switched off -- its "On expression" reads false -- or shot out, the targets themselves are zeroed, and nothing writes
/// them again: an object switched off for one tick runs down to rest and stays there. The arithmetic is the original's, on
/// the x87 register: a speed is compared unrounded, so an axis with no acceleration never changes speed.
/// </remarks>
public static class RotationRule
{
    /// <summary>A tick, in seconds, as the original has it (<c>0.03333333507180214</c>).</summary>
    public const float TickSeconds = 1f / 30f;

    private const float DegreesToRadians = MathF.PI / 180f;

    /// <summary>Steps a speed toward its target by an acceleration over one tick.</summary>
    /// <param name="current">The speed, in degrees a second.</param>
    /// <param name="target">The speed it is making for.</param>
    /// <param name="acceleration">How fast it changes, in degrees a second each second.</param>
    /// <returns>The speed after the tick, never past the target.</returns>
    public static float StepSpeed(float current, float target, float acceleration)
    {
        if (current == target)
        {
            return current;
        }

        if (current < target)
        {
            X87 next = ((X87)acceleration * TickSeconds) + current;
            return next.Value > target ? target : next.F;
        }

        X87 down = (X87)current - ((X87)acceleration * TickSeconds);
        return down.Value < target ? target : down.F;
    }

    /// <summary>Turns an angle by a speed over one tick, wrapped from above only.</summary>
    /// <param name="angle">The angle, in degrees.</param>
    /// <param name="speed">The speed, in degrees a second.</param>
    /// <returns>The angle after the tick, below 360.</returns>
    public static float StepAngle(float angle, float speed)
    {
        float next = (((X87)speed * TickSeconds) + angle).F;
        if (next >= 360f)
        {
            X87 wrapped;
            do
            {
                wrapped = (X87)next - 360f;
                next = wrapped.F;
            }
            while (wrapped.Value >= 360.0);
        }

        return next;
    }

    /// <summary>Gets the orientation of an object placed one way and turned by its three angles.</summary>
    /// <param name="placed">The orientation its level placed it at.</param>
    /// <param name="x">The turn about X, in degrees.</param>
    /// <param name="y">The turn about Y, in degrees.</param>
    /// <param name="z">The turn about Z, in degrees.</param>
    /// <returns>The orientation.</returns>
    public static Matrix3f Orientation(Matrix3f placed, float x, float y, float z)
    {
        Matrix3f turn = (x == 0f && y == 0f)
            ? Matrix3f.RotationZ(z * DegreesToRadians)
            : Matrix3f.FromEngineEulerAngles(x * DegreesToRadians, y * DegreesToRadians, z * DegreesToRadians);
        return placed * turn;
    }

    /// <summary>
    /// Gets how hard an object is turning, for the parameter its rotate sound is played with: each axis's speed over the
    /// speed it was authored with, added up and held to 0..1.
    /// </summary>
    /// <param name="speedX">The speed about X.</param>
    /// <param name="speedY">The speed about Y.</param>
    /// <param name="speedZ">The speed about Z.</param>
    /// <param name="authoredX">The speed about X it was authored with.</param>
    /// <param name="authoredY">The speed about Y it was authored with.</param>
    /// <param name="authoredZ">The speed about Z it was authored with.</param>
    /// <returns>The level, 0 at rest and 1 at full speed.</returns>
    /// <remarks>An axis authored with no speed makes the sum no number at all, which reads as nought.</remarks>
    public static float SoundLevel(float speedX, float speedY, float speedZ, float authoredX, float authoredY, float authoredZ)
    {
        float level = (speedZ / authoredZ) + (speedX / authoredX) + (speedY / authoredY);
        return level < 0f || float.IsNaN(level) ? 0f : Math.Min(level, 1f);
    }
}
