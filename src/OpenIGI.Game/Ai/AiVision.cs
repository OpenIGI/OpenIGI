using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Ai;

/// <summary>An AI's pair of view cones, and the test that decides whether a point is inside either.</summary>
/// <remarks>
/// <para>
/// <b>Read <see cref="Capture"/> before <see cref="IsVisible"/>.</b> Hooking the retail executable settled
/// that <c>0x4502F0</c> — which <see cref="IsVisible"/> transcribes — is <b>never called by the detection
/// sweep</b>. What the sweep runs is the cone test on the per-tick eye snapshot,
/// <see cref="AiEyeSnapshot.IsVisible"/>. This type holds the <em>parameters</em> both share and takes the
/// snapshot; <see cref="IsVisible"/> is kept because <c>0x4502F0</c> is a real function on the
/// event-admission path, not because detection uses it. See <c>ai.md</c> §8.
/// </para>
/// <para>
/// From <c>0x4502F0</c>. <b>Both cones are always tested and the results are OR'd</b> — which corrects
/// <c>gameplay-systems.md</c> §5's "cone selection switches on the alert latch". The latch does not select a
/// cone; it selects <em>each</em> cone's range. So an AI sees through a long narrow cone <em>or</em> a short
/// wide one, and being alerted lengthens both.
/// </para>
/// <para>
/// The parameters live at <c>HumanAI+160</c>..<c>+223</c> as two blocks of four doubles —
/// <c>{alpha, gamma, length, alarmLength}</c> — which is why the per-tick snapshot copies exactly that span.
/// </para>
/// </remarks>
public sealed class AiVision
{
    /// <summary>Initializes a new instance of the <see cref="AiVision"/> class with the constructor's defaults.</summary>
    public AiVision()
    {
        Primary = AiViewCone.DefaultPrimary;
        Secondary = AiViewCone.DefaultSecondary;
    }

    /// <summary>Gets or sets the long cone, the one every shipped archetype configures.</summary>
    public AiViewCone Primary { get; set; }

    /// <summary>Gets or sets the short wide cone. No shipped archetype touches it.</summary>
    public AiViewCone Secondary { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the secondary cone is tested at all — the eye snapshot's
    /// gate byte at <c>+112</c>.
    /// </summary>
    /// <remarks>
    /// The per-tick sets this to 1 unconditionally for a soldier, so it is on for every AI in the game. The
    /// one live observation of it being 0 is a level-1 <c>SCamera</c>, whose secondary cone is all zeroes —
    /// a security camera has a primary cone and no peripheral vision. Only the snapshot test
    /// (<see cref="AiEyeSnapshot.IsVisible"/>) consults it; <c>0x4502F0</c> has no equivalent.
    /// </remarks>
    public bool SecondaryEnabled { get; set; } = true;

    /// <summary>
    /// Takes the per-tick eye snapshot — <c>0x489750</c>'s copy into <c>HumanAI+14896</c>, whose cone test
    /// is what the detection cascade actually runs.
    /// </summary>
    /// <param name="eye">The eye's world position, in engine units.</param>
    /// <param name="orientation">The eye's orientation, columns X right / Y forward / Z up.</param>
    /// <param name="alerted">
    /// The alert latch, which picks each cone's length <b>once, here</b> rather than on every test.
    /// </param>
    /// <returns>The snapshot to test candidates against for the rest of the tick.</returns>
    /// <remarks>
    /// The snapshot is taken <b>before</b> the alert latch is recomputed, so the cones used on a tick are
    /// the ones <em>last</em> tick's state selected — a one-tick lag that is in the original
    /// (<c>ai.md</c> §3.1).
    /// </remarks>
    public AiEyeSnapshot Capture(Vector3d eye, Matrix3f orientation, bool alerted) => new(
        eye,
        orientation,
        new AiSnapshotCone(Primary.Alpha, Primary.Gamma, Primary.RangeAt(alerted)),
        new AiSnapshotCone(Secondary.Alpha, Secondary.Gamma, Secondary.RangeAt(alerted)),
        SecondaryEnabled);

    /// <summary>Tests whether a world point is visible from an eye, ignoring occlusion.</summary>
    /// <param name="eye">The eye's world position.</param>
    /// <param name="orientation">
    /// The eye's orientation, whose <b>columns</b> are the local axes in world space: X right, Y forward,
    /// Z up.
    /// </param>
    /// <param name="target">The world point.</param>
    /// <param name="alerted">The AI's alert latch, which picks each cone's range.</param>
    /// <returns><see langword="true"/> when the point lies in either cone.</returns>
    /// <remarks>
    /// <para>
    /// This is geometry only. The original's caller is the event-admission filter, which has already decided
    /// there is something to look at; nothing here casts a ray, so a wall does not block it.
    /// </para>
    /// <para>
    /// Note the mixed comparisons, reproduced as-is: the range is <c>&lt;=</c> while both angles are
    /// <c>&lt;</c>. A point exactly on the cone's rim is out; a point at exactly maximum range is in.
    /// </para>
    /// </remarks>
    public bool IsVisible(Vector3d eye, Matrix3f orientation, Vector3d target, bool alerted)
    {
        Vector3d delta = new(target.X - eye.X, target.Y - eye.Y, target.Z - eye.Z);

        // local = delta . M, the engine's row-vector convention: component k is column k dotted with delta.
        double x = (orientation.M31 * delta.Z) + (orientation.M21 * delta.Y) + (orientation.M11 * delta.X);
        double y = (orientation.M32 * delta.Z) + (orientation.M22 * delta.Y) + (orientation.M12 * delta.X);
        double z = (orientation.M33 * delta.Z) + (orientation.M23 * delta.Y) + (orientation.M13 * delta.X);

        // Anything at or behind the eye plane is invisible, before any trigonometry.
        if (y < 0.0)
        {
            return false;
        }

        double distance = Math.Sqrt((x * x) + (y * y) + (z * z));
        double elevation = Math.Abs(AngleFromForward(y, z));
        double azimuth = Math.Abs(AngleFromForward(y, x));

        return InCone(Primary, distance, elevation, azimuth, alerted)
            || InCone(Secondary, distance, elevation, azimuth, alerted);
    }

    /// <summary>The vertical half-angle from the forward axis to a direction, with X dropped.</summary>
    /// <param name="forward">The local forward component.</param>
    /// <param name="up">The local up component.</param>
    /// <returns>The signed angle in radians, positive when <paramref name="up"/> is.</returns>
    /// <remarks>
    /// <para>
    /// <c>0x4148F0</c>. It zeroes the local X in place — which is why its caller re-copies the vector before
    /// every call — normalises what is left, takes the arc cosine of the forward component and signs the
    /// result by the remaining one. A degenerate vector yields zero.
    /// </para>
    /// <para>
    /// The horizontal twin <c>0x414950</c> is the same function with Z dropped instead of X, and it signs the
    /// result the <b>other way round</b> (negative when its off-axis component is positive). Both callers take
    /// the absolute value immediately, so the disagreement never shows; this one helper serves both, and the
    /// asymmetry is recorded rather than reproduced.
    /// </para>
    /// </remarks>
    public static double AngleFromForward(double forward, double up)
    {
        double length = Math.Sqrt((forward * forward) + (up * up));
        if (length == 0.0)
        {
            return 0.0;
        }

        double angle = Math.Acos(Math.Clamp(forward / length, -1.0, 1.0));
        return up < 0.0 ? -angle : angle;
    }

    private static bool InCone(AiViewCone cone, double distance, double elevation, double azimuth, bool alerted) =>
        distance <= cone.RangeAt(alerted) && elevation < cone.Alpha && azimuth < cone.Gamma;
}
