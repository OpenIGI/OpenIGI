using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>What one tick of movement along the waypoint queue concluded.</summary>
public enum AiMovementOutcome
{
    /// <summary>The waypoint was reached and popped; the caller should run the next one this same tick.</summary>
    WaypointReached = 0,

    /// <summary>Still travelling. Steering has been written to the input frame.</summary>
    Travelling = 1,

    /// <summary>The queue is empty — the destination has been reached.</summary>
    Arrived = 2,
}

/// <summary>
/// One tick of an AI walking its waypoint queue — <c>0x451540</c>, and the function that makes a bot move.
/// </summary>
/// <remarks>
/// <para>
/// <b>The AI steers its soldier through the same 160-byte input frame the player's input sampler produces.</b>
/// It writes three floats into the scratch at <c>HumanAI+15476</c> — the block the per-tick memsets at the top
/// of every tick — and the per-tick posts it to the owning <c>Human</c> under the channel message id.
/// <c>HumanPlayerInput</c> receives the same frame, and <c>AnimTask</c> and <c>AnimCam</c> sit on the same
/// interface; this is the <b>fourth</b> receiver. It is also the reason a bot moves
/// exactly like the player: identical locomotion, identical integrator, identical animation, different source
/// of the frame.
/// </para>
/// <para>
/// The three slots used are float 0 (turn), float 1 (pitch) and float 4 (forward, written as a literal
/// <c>1.0</c>). Everything else in the frame stays zero, which is why an AI never strafes, jumps or fires
/// through this path — those come from actions, not from movement.
/// </para>
/// <para>
/// <b>The angles are divided by π before they are written</b> (the original multiplies by
/// <c>0.31830987</c>), so the frame carries a turn as a fraction of a half-turn rather than as radians.
/// </para>
/// </remarks>
public sealed class AiMovementStep
{
    /// <summary>The arrival threshold on each horizontal axis, in engine units — 0.3 m.</summary>
    /// <remarks>
    /// <b>A box test, not a radius</b>, and it <b>ignores Z entirely</b>: the original tests <c>|local.x|</c>
    /// and <c>|local.y|</c> against this separately, so the accepting region is a square column 0.6 m across of
    /// unlimited height. A waypoint directly above or below counts as reached.
    /// </remarks>
    public const double ArrivalThreshold = 1228.8;

    /// <summary>The reciprocal of π the original scales both steering angles by.</summary>
    public const float AngleToFrame = 0.31830987f;

    /// <summary>The tolerance the AI turns to while waiting at a door — 2°.</summary>
    public const float DoorFacingTolerance = 0.034906585f;

    /// <summary>The forward value written into the frame while travelling.</summary>
    public const float ForwardFull = 1.0f;

    /// <summary>Gets or sets the yaw clamp, in radians — <c>HumanAI+16072</c>.</summary>
    /// <remarks>
    /// Set to <b>π</b> by the move-to-node entry point, which is effectively "turn as fast as you like", and to
    /// about <b>0.0257</b> (1.47°) by the look-at entry point and by the constructor. So the same steering code
    /// serves "walk over there" and "aim precisely at that" by nothing but this pair of clamps.
    /// </remarks>
    public float YawClamp { get; set; } = MathF.PI;

    /// <summary>Gets or sets the pitch clamp, in radians — <c>HumanAI+16076</c>.</summary>
    public float PitchClamp { get; set; } = MathF.PI;

    /// <summary>Steers one tick toward a waypoint.</summary>
    /// <param name="position">The AI's world position.</param>
    /// <param name="orientation">
    /// The AI's orientation, whose columns are its local axes in world space — the same convention
    /// <see cref="AiVision"/> uses.
    /// </param>
    /// <param name="waypoint">The waypoint's world position.</param>
    /// <param name="yawError">The yaw error the original computes separately, in radians.</param>
    /// <param name="pitchError">The pitch error, in radians.</param>
    /// <param name="frame">Receives the three steering slots.</param>
    /// <returns>
    /// <see cref="AiMovementOutcome.Travelling"/> while still short of the waypoint, or
    /// <see cref="AiMovementOutcome.WaypointReached"/> once inside the threshold on both horizontal axes.
    /// </returns>
    /// <remarks>
    /// The clamps are applied to the errors <b>before</b> they are scaled into the frame, and they are
    /// symmetric — the original clamps to <c>[-limit, +limit]</c> with two separate comparisons.
    /// </remarks>
    public AiMovementOutcome Step(
        Vector3d position,
        Matrix3f orientation,
        Vector3d waypoint,
        float yawError,
        float pitchError,
        out AiSteeringFrame frame)
    {
        Vector3d delta = new(waypoint.X - position.X, waypoint.Y - position.Y, waypoint.Z - position.Z);

        // Into the AI's own frame: component k is column k dotted with delta.
        double localX = (orientation.M31 * delta.Z) + (orientation.M21 * delta.Y) + (orientation.M11 * delta.X);
        double localY = (orientation.M32 * delta.Z) + (orientation.M22 * delta.Y) + (orientation.M12 * delta.X);

        if (Math.Abs(localX) <= ArrivalThreshold && Math.Abs(localY) <= ArrivalThreshold)
        {
            frame = default;
            return AiMovementOutcome.WaypointReached;
        }

        frame = new AiSteeringFrame(
            Clamp(yawError, YawClamp) * AngleToFrame,
            Clamp(pitchError, PitchClamp) * AngleToFrame,
            ForwardFull);
        return AiMovementOutcome.Travelling;
    }

    private static float Clamp(float value, float limit) =>
        value < -limit ? -limit
        : value > limit ? limit
        : value;
}

/// <summary>The three input-frame slots an AI writes to steer its soldier.</summary>
/// <param name="Turn">Float 0 of the frame — the yaw error over π.</param>
/// <param name="Pitch">Float 1 — the pitch error over π.</param>
/// <param name="Forward">Float 4 — <c>1.0</c> while travelling, zero otherwise.</param>
/// <remarks>
/// The slot numbers are byte offsets <c>+15476</c>, <c>+15480</c> and <c>+15492</c> within <c>HumanAI</c>, which
/// are floats 0, 1 and 4 of the 160-byte frame based at <c>+15476</c>.
/// </remarks>
public readonly record struct AiSteeringFrame(float Turn, float Pitch, float Forward);
