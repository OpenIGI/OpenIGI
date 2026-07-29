using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Player;

/// <summary>
/// How a human moves: gravity and air control while airborne, and root motion while animating.
/// </summary>
/// <remarks>
/// <para>
/// Both halves come out of the player's per-tick, <c>0x4109A0</c>, and the airborne integrator it
/// calls, <c>0x408460</c>. Between them they are the whole of how a body's position changes — there is
/// no walking-speed constant anywhere, because walking is not driven by one.
/// </para>
/// <para>
/// And they are <b>added</b>, not chosen between: the move-and-collide at <c>0x4614B0</c> opens by
/// summing the velocity at <c>+1636</c> with the root-motion delta at <c>+1312</c>. So a jump carries
/// its take-off velocity <i>and</i> whatever the airborne animation covers.
/// </para>
/// </remarks>
public static class HumanMotion
{
    /// <summary>Gravity, in world units per tick squared.</summary>
    /// <remarks>
    /// <para>
    /// <c>84.741692</c>, from the airborne integrator <c>0x408460</c> — about <b>18.6 m/s²</b>, which
    /// is nearly twice Earth's. That is deliberate and it is what stops the jump feeling floaty: a
    /// 1024-unit take-off reaches its apex in twelve ticks rather than twenty-three.
    /// </para>
    /// <para>
    /// <b>Correction.</b> An earlier pass took <c>44.600887</c> for gravity because it sits in an
    /// integrator that looks like the airborne one. It is not: <c>0x40DBF0</c> is the <b>wire slide</b>
    /// state's per-tick — its entry plays <c>wire_slide_1</c> — and that constant, along with the 0.99
    /// drag beside it, belongs to sliding down a wire. See <see cref="WireSlideGravityPerTick"/>.
    /// </para>
    /// </remarks>
    public const float GravityPerTick = 84.741692f;

    /// <summary>The wire slide's own gravity, which is not the airborne one.</summary>
    /// <remarks>
    /// <c>44.600887</c> — exactly 9.8 m/s². Kept named so the next reader does not mistake it for
    /// gravity again, since it is the more ordinary-looking of the two.
    /// </remarks>
    public const float WireSlideGravityPerTick = 44.600887f;

    /// <summary>The wire slide's per-tick damping, applied to all three axes.</summary>
    /// <remarks>
    /// <c>0.99000001</c>, and on the vertical it is applied <i>after</i> its gravity. <b>The airborne
    /// integrator has no damping at all</b> — a falling body simply accelerates.
    /// </remarks>
    public const float WireSlideDragPerTick = 0.99000001f;

    /// <summary>Advances an airborne velocity by one tick.</summary>
    /// <param name="velocity">The current velocity, in world units per tick.</param>
    /// <returns>The velocity after gravity.</returns>
    /// <remarks>
    /// Gravity only: no damping, and no terminal velocity. A long fall keeps accelerating, which is
    /// what <c>0x408460</c> does.
    /// </remarks>
    public static Vector3f Integrate(Vector3f velocity) =>
        new(velocity.X, velocity.Y, velocity.Z - GravityPerTick);

    /// <summary>Advances a wire slide's velocity by one tick.</summary>
    /// <param name="velocity">The current velocity.</param>
    /// <returns>The velocity after the slide's own gravity and damping.</returns>
    /// <remarks>
    /// <c>0x40DBF0</c>. The order matters: its gravity is subtracted from the vertical <b>first</b> and
    /// the damping applied to the result, which is what gives the slide a terminal speed the ordinary
    /// fall does not have.
    /// </remarks>
    public static Vector3f IntegrateWireSlide(Vector3f velocity) => new(
        velocity.X * WireSlideDragPerTick,
        velocity.Y * WireSlideDragPerTick,
        (velocity.Z - WireSlideGravityPerTick) * WireSlideDragPerTick);

    /// <summary>The air control one tick adds to an airborne velocity.</summary>
    /// <param name="forward">Forward intent, −1, 0 or 1 — the movement slots, not an analogue axis.</param>
    /// <param name="strafe">Sideways intent, likewise.</param>
    /// <param name="basis">The body's facing.</param>
    /// <param name="speed">
    /// <see cref="HumanPlayerParameters.Speed3"/> — argument 3 of <c>humanplayer.qvm</c>, which is what
    /// the integrator scales each axis by.
    /// </param>
    /// <returns>The velocity change to add.</returns>
    /// <remarks>
    /// <b>You can steer in mid-air after all</b>, a little: <c>0x408460</c> reads the same four
    /// movement slots the ground states do and adds <paramref name="speed"/> along each held one, in
    /// the body's own frame, every airborne tick. It accumulates rather than replacing, so it nudges a
    /// jump rather than turning it.
    /// </remarks>
    public static Vector3f AirControl(float forward, float strafe, in Matrix3f basis, float speed)
    {
        float right = strafe * speed;
        float ahead = forward * speed;

        return new Vector3f(
            (basis.M11 * right) + (basis.M12 * ahead),
            (basis.M21 * right) + (basis.M22 * ahead),
            (basis.M31 * right) + (basis.M32 * ahead));
    }

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
