using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Ai;

/// <summary>
/// One cone as the per-tick eye snapshot stores it — two half-angles and <b>one already-resolved
/// length</b>, the alert choice having been made when the snapshot was taken.
/// </summary>
/// <param name="Alpha">The vertical half-angle in radians.</param>
/// <param name="Gamma">The horizontal half-angle in radians.</param>
/// <param name="Length">The range in engine units that applies right now.</param>
/// <remarks>
/// This is the difference that matters between the snapshot and <see cref="AiVision.IsVisible"/>:
/// <c>0x4502F0</c> holds both lengths and re-picks between them on every call, where the snapshot holds
/// the one the alert latch selected.
/// </remarks>
public readonly record struct AiSnapshotCone(double Alpha, double Gamma, double Length);

/// <summary>
/// The eye snapshot at <c>HumanAI+14896</c>, and the cone test <c>0x489960</c> that reads it — which is
/// <b>the function the detection cascade actually calls</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Correction, from the live game.</b> <c>ai.md</c> §4 introduced <c>0x4502F0</c> as "the cone test" and
/// the port transcribed it into <see cref="AiVision.IsVisible"/>. Hooking the retail executable through a
/// whole mission-1 load that logged 22045 patrol events showed <c>0x4502F0</c> was <b>never called once</b>.
/// The per-tick detection sweep (<c>0x489750</c>) builds this snapshot with <c>0x489960</c> and tests
/// against it from <c>0x4898D0</c>; <c>0x4502F0</c> is reachable only from the event-admission path. So
/// detection has to be modelled on this type, not on that one.
/// </para>
/// <para>
/// Three things differ from <c>0x4502F0</c>, and only two of them can ever show:
/// </para>
/// <list type="number">
/// <item>
/// <b>each cone carries one resolved length</b> rather than a normal/alarm pair — so an AI whose alert latch
/// flips mid-tick still sees with the range the snapshot was taken at (<c>ai.md</c> §3.1's one-tick lag);
/// </item>
/// <item>
/// <b>the secondary cone is gated by a byte</b> at <c>+112</c> that <c>0x4502F0</c> has no equivalent of.
/// The per-tick sets it to 1 for a soldier, but a level-1 <c>SCamera</c> reads back <b>0</b> with an
/// all-zero secondary — so a security camera is a viewer with a primary cone and <em>no peripheral
/// vision</em>, which is what the gate is for;
/// </item>
/// <item>
/// <b>there is no "behind the eye" early-out.</b> <c>0x4502F0</c> returns false immediately on
/// <c>local.y &lt; 0</c>; this function has no such test and computes both angles regardless. It cannot
/// change an answer while every configured half-angle is under 90° — a point behind the eye gives an angle
/// over 90° from forward and fails on the angle instead — and every retail cone is (the widest is the
/// secondary's ±80°). Recorded because it is a real difference between the two functions, not because it
/// is reachable.
/// </item>
/// </list>
/// <para>
/// The layout is pinned field for field by the offsets <c>0x489960</c> indexes (<c>ai.md</c> §5.4):
/// position at <c>+0</c>, the 3×3 at <c>+24</c>, primary <c>{alpha, gamma, length}</c> at
/// <c>+64</c>/<c>+72</c>/<c>+80</c>, secondary at <c>+88</c>/<c>+96</c>/<c>+104</c>, and the gate byte at
/// <c>+112</c>.
/// </para>
/// </remarks>
public readonly struct AiEyeSnapshot
{
    /// <summary>Initializes a new instance of the <see cref="AiEyeSnapshot"/> struct.</summary>
    /// <param name="position">The eye's world position, in engine units.</param>
    /// <param name="orientation">
    /// The eye's orientation, whose <b>columns</b> are the local axes in world space: X right, Y forward,
    /// Z up.
    /// </param>
    /// <param name="primary">The primary cone, with its length already resolved.</param>
    /// <param name="secondary">The secondary cone, with its length already resolved.</param>
    /// <param name="secondaryEnabled">The gate byte at <c>+112</c>.</param>
    public AiEyeSnapshot(
        Vector3d position,
        Matrix3f orientation,
        AiSnapshotCone primary,
        AiSnapshotCone secondary,
        bool secondaryEnabled)
    {
        Position = position;
        Orientation = orientation;
        Primary = primary;
        Secondary = secondary;
        SecondaryEnabled = secondaryEnabled;
    }

    /// <summary>Gets the eye's world position — the snapshot's <c>+0</c>.</summary>
    public Vector3d Position { get; }

    /// <summary>Gets the eye's orientation — the snapshot's <c>+24</c>, the <c>0x28</c>-byte copy.</summary>
    public Matrix3f Orientation { get; }

    /// <summary>Gets the primary cone — the snapshot's <c>+64</c>, <c>+72</c>, <c>+80</c>.</summary>
    public AiSnapshotCone Primary { get; }

    /// <summary>Gets the secondary cone — the snapshot's <c>+88</c>, <c>+96</c>, <c>+104</c>.</summary>
    public AiSnapshotCone Secondary { get; }

    /// <summary>
    /// Gets a value indicating whether the secondary cone is tested at all — the snapshot's <c>+112</c>.
    /// </summary>
    public bool SecondaryEnabled { get; }

    /// <summary>Tests whether a world point falls in either cone — <c>0x489960</c>, transcribed.</summary>
    /// <param name="target">The world point.</param>
    /// <returns><see langword="true"/> when the point is in the primary cone, or in an enabled secondary.</returns>
    /// <remarks>
    /// Note the mixed comparisons, reproduced as-is and identical to <c>0x4502F0</c>'s: the range is
    /// <c>&lt;=</c> while both angles are <c>&lt;</c>. A point at exactly maximum range is seen; a point
    /// exactly on the rim is not. Nothing here casts a ray — occlusion is the next gate in the cascade
    /// (<c>0x489B20</c>), not part of this test.
    /// </remarks>
    public bool IsVisible(Vector3d target)
    {
        Vector3d delta = new(target.X - Position.X, target.Y - Position.Y, target.Z - Position.Z);

        // local = delta . M, the engine's row-vector convention: component k is column k dotted with delta.
        double x = (Orientation.M31 * delta.Z) + (Orientation.M21 * delta.Y) + (Orientation.M11 * delta.X);
        double y = (Orientation.M32 * delta.Z) + (Orientation.M22 * delta.Y) + (Orientation.M12 * delta.X);
        double z = (Orientation.M33 * delta.Z) + (Orientation.M23 * delta.Y) + (Orientation.M13 * delta.X);

        double distance = Math.Sqrt((x * x) + (y * y) + (z * z));
        double elevation = Math.Abs(AiVision.AngleFromForward(y, z));
        double azimuth = Math.Abs(AiVision.AngleFromForward(y, x));

        if (distance <= Primary.Length && elevation < Primary.Alpha && azimuth < Primary.Gamma)
        {
            return true;
        }

        return SecondaryEnabled
            && distance <= Secondary.Length
            && elevation < Secondary.Alpha
            && azimuth < Secondary.Gamma;
    }
}
