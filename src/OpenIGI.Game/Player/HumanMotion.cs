using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// How a human moves: gravity and drag while airborne, and root motion while animating.
/// </summary>
/// <remarks>
/// <para>
/// Both halves come out of the player's per-tick, <c>0x4109A0</c>, and the airborne integrator it
/// calls, <c>0x40DBF0</c>. Between them they are the whole of how a body's position changes — there is
/// no walking-speed constant anywhere, because walking is not driven by one.
/// </para>
/// </remarks>
public static class HumanMotion
{
    /// <summary>Gravity, in world units per tick squared.</summary>
    /// <remarks>
    /// <c>44.600887</c>. In ordinary terms that is <b>exactly 9.8 m/s²</b>:
    /// <c>44.600887 × 900 ÷ 4096 = 9.8000</c>, the 900 being ticks squared per second squared and the
    /// 4096 units per metre.
    /// </remarks>
    public const float GravityPerTick = 44.600887f;

    /// <summary>What each velocity component is multiplied by every tick.</summary>
    /// <remarks>
    /// <c>0.99000001</c>, applied to all three axes — and on the vertical it is applied <i>after</i>
    /// gravity, not before. That ordering is what sets the terminal velocity.
    /// </remarks>
    public const float DragPerTick = 0.99000001f;

    /// <summary>The fastest a body ever falls, in world units per tick.</summary>
    /// <remarks>
    /// The fixed point of <c>v ← (v − g) · d</c>, which is <c>−g·d / (1 − d)</c> — about 4415 units a
    /// tick, or 32 m/s. A body reaches it asymptotically and never exceeds it.
    /// </remarks>
    public static float TerminalVelocity => -GravityPerTick * DragPerTick / (1f - DragPerTick);

    /// <summary>Advances an airborne velocity by one tick.</summary>
    /// <param name="velocity">The current velocity, in world units per tick.</param>
    /// <returns>The velocity after gravity and drag.</returns>
    /// <remarks>
    /// The order matters and is the original's: gravity is subtracted from the vertical component
    /// <b>first</b>, and the drag is then applied to the result. Damping before subtracting would give
    /// a different terminal velocity and a visibly different fall.
    /// </remarks>
    public static Vector3f Integrate(Vector3f velocity) => new(
        velocity.X * DragPerTick,
        velocity.Y * DragPerTick,
        (velocity.Z - GravityPerTick) * DragPerTick);

    /// <summary>
    /// The factor an animation's delta translation is scaled by before it moves the body.
    /// </summary>
    /// <remarks>
    /// Argument 0 of <c>humanplayer.qvm</c>; <b>1.75</b> in retail. It is applied only while the
    /// human's gate at <c>+1324</c> is clear — see <see cref="ApplyRootMotion"/>.
    /// </remarks>
    public const float DefaultDeltaTranslationScale = 1.75f;

    /// <summary>Turns an animation's own delta translation into a world-space step.</summary>
    /// <param name="delta">The animation's delta translation for this tick, in the body's own frame.</param>
    /// <param name="basis">The body's orientation.</param>
    /// <param name="deltaTranslationScale">
    /// The scale from <see cref="HumanPlayerParameters.DeltaTranslationScale"/>.
    /// </param>
    /// <param name="scaleSuppressed">
    /// Whether the human's <c>+1324</c> gate is set, which leaves the delta unscaled.
    /// </param>
    /// <returns>The step to move the body by, in world units.</returns>
    /// <remarks>
    /// <para>
    /// <b>This is how a human walks.</b> There is no speed constant: the animation carries the
    /// translation, the scale stretches it, and the body's own basis turns it into a world direction.
    /// A walk cycle that covers less ground is a slower walk, and changing the animation changes the
    /// speed.
    /// </para>
    /// <para>
    /// The scale is applied <i>before</i> the rotation, which matters only in that the two do not
    /// commute with the gate — a suppressed tick rotates the raw delta rather than a scaled one.
    /// </para>
    /// </remarks>
    public static Vector3f ApplyRootMotion(
        Vector3f delta,
        in Matrix3f basis,
        float deltaTranslationScale = DefaultDeltaTranslationScale,
        bool scaleSuppressed = false)
    {
        if (!scaleSuppressed)
        {
            delta = new Vector3f(
                delta.X * deltaTranslationScale,
                delta.Y * deltaTranslationScale,
                delta.Z * deltaTranslationScale);
        }

        // The original writes the three rows out by hand against the body's basis at +120..+152, which
        // is a column-major multiply of the delta by the orientation.
        return new Vector3f(
            (basis.M11 * delta.X) + (basis.M12 * delta.Y) + (basis.M13 * delta.Z),
            (basis.M21 * delta.X) + (basis.M22 * delta.Y) + (basis.M23 * delta.Z),
            (basis.M31 * delta.X) + (basis.M32 * delta.Y) + (basis.M33 * delta.Z));
    }
}
